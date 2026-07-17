from __future__ import annotations

import json
import os
import re
import shutil
import subprocess
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Callable

from .models import ProcessResult, Usage
from .process_runner import ProcessRunner


SESSION_RE = re.compile(r"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}")


@dataclass(frozen=True)
class CodexDiscovery:
    ok: bool
    executable: str | None
    method: str | None
    version: str | None
    errors: tuple[str, ...]


@dataclass(frozen=True)
class CodexTurnResult:
    ok: bool
    exit_code: int | None
    stdout: str
    stderr: str
    session_id: str | None
    usage: Usage
    final_message: str
    errors: tuple[str, ...]


def standard_candidates(env: dict[str, str] | None = None) -> list[tuple[str, str]]:
    env = os.environ if env is None else env
    candidates: list[tuple[str, str]] = []
    local = env.get("LOCALAPPDATA")
    if local:
        candidates.append(("windows_standalone", str(Path(local) / "Programs" / "OpenAI" / "Codex" / "bin" / "codex.exe")))
    home = env.get("HOME")
    if home:
        candidates.append(("home_bin", str(Path(home) / ".local" / "bin" / "codex")))
        candidates.append(("homebrew", "/opt/homebrew/bin/codex"))
        candidates.append(("usr_local", "/usr/local/bin/codex"))
    return candidates


def is_windowsapps_private(path: str) -> bool:
    normalized = path.replace("\\", "/").lower()
    return "/windowsapps/openai.codex_" in normalized and "/app/resources/codex.exe" in normalized


def discover_codex(
    explicit: str | None = None,
    *,
    env: dict[str, str] | None = None,
    which: Callable[[str], str | None] = shutil.which,
    runner: Callable[[str], tuple[int | None, str, str]] | None = None,
) -> CodexDiscovery:
    env = os.environ if env is None else env
    candidates: list[tuple[str, str]] = []
    if explicit:
        candidates.append(("argument", explicit))
    if env.get("CODEX_EXECUTABLE"):
        candidates.append(("environment", env["CODEX_EXECUTABLE"]))
    which_path = which("codex")
    if which_path:
        candidates.append(("path", which_path))
    candidates.extend(standard_candidates(env))

    errors: list[str] = []
    seen: set[str] = set()
    verifier = runner or verify_candidate
    for method, candidate in candidates:
        if candidate in seen:
            continue
        seen.add(candidate)
        if is_windowsapps_private(candidate):
            errors.append(f"{method}: rejected private WindowsApps Codex executable")
            continue
        code, stdout, stderr = verifier(candidate)
        if code == 0:
            version = (stdout or stderr).strip().splitlines()[0] if (stdout or stderr).strip() else ""
            return CodexDiscovery(True, candidate, method, version, tuple(errors))
        errors.append(f"{method}: {candidate}: {stderr or stdout or 'not executable'}")
    return CodexDiscovery(False, None, None, None, tuple(errors))


def verify_candidate(path: str) -> tuple[int | None, str, str]:
    try:
        completed = subprocess.run([path, "--version"], capture_output=True, text=True, shell=False, timeout=10)
    except (OSError, subprocess.TimeoutExpired) as exc:
        return None, "", str(exc)
    return completed.returncode, completed.stdout, completed.stderr


class CodexClient:
    def __init__(self, executable: str, repo: Path, process_runner: ProcessRunner | None = None) -> None:
        self.executable = executable
        self.repo = repo
        self.process_runner = process_runner or ProcessRunner()

    def login_status(self) -> ProcessResult:
        return self.process_runner.run([self.executable, "login", "status"], self.repo, 30)

    def exec(self, prompt: str, *, sandbox: str, output_schema: Path | None, timeout_seconds: int) -> CodexTurnResult:
        argv = [self.executable, "exec", "--json", "--sandbox", sandbox, "--cd", str(self.repo)]
        if output_schema:
            argv.extend(["--output-schema", str(output_schema)])
        argv.append(prompt)
        return self._run_codex(argv, timeout_seconds)

    def resume(self, session_id: str, prompt: str, *, output_schema: Path | None, timeout_seconds: int) -> CodexTurnResult:
        argv = [self.executable, "exec", "resume", "--json"]
        if output_schema:
            argv.extend(["--output-schema", str(output_schema)])
        argv.extend([session_id, prompt])
        return self._run_codex(argv, timeout_seconds)

    def _run_codex(self, argv: list[str], timeout_seconds: int) -> CodexTurnResult:
        result = self.process_runner.run(argv, self.repo, timeout_seconds)
        parsed = parse_jsonl(result.stdout)
        errors = list(parsed["errors"])
        if result.exit_code != 0:
            errors.append(f"codex exited with code {result.exit_code}")
        return CodexTurnResult(
            not errors and result.exit_code == 0,
            result.exit_code,
            result.stdout,
            result.stderr,
            parsed["session_id"],
            parsed["usage"],
            parsed["final_message"],
            tuple(errors),
        )


def parse_jsonl(stdout: str) -> dict[str, Any]:
    usage = Usage()
    session_id: str | None = None
    final_message = ""
    errors: list[str] = []
    for line in stdout.splitlines():
        if not line.strip():
            continue
        try:
            event = json.loads(line)
        except json.JSONDecodeError as exc:
            errors.append(f"malformed codex JSONL: {exc}")
            continue
        if not isinstance(event, dict):
            continue
        event_type = str(event.get("type") or event.get("event") or "")
        raw = json.dumps(event)
        match = SESSION_RE.search(raw)
        if match and session_id is None:
            session_id = match.group(0)
        if event_type.endswith("turn.completed") or event_type == "turn.completed":
            usage.add(event.get("usage"))
        if event_type in {"agent_message", "message", "final_message"}:
            value = event.get("message") or event.get("text") or event.get("content")
            if isinstance(value, str):
                final_message = value
        if "usage limit" in raw.lower() or "rate limit" in raw.lower() or "auth" in raw.lower():
            errors.append(raw)
    return {"session_id": session_id, "usage": usage, "final_message": final_message, "errors": errors}
