from __future__ import annotations

import json
import os
import re
import shutil
import subprocess
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Callable

from .io_utils import atomic_write_bytes, atomic_write_json
from .jsonl_stream import IncrementalJsonlDecoder, JsonlStreamError
from .models import ProcessResult, RawProcessResult, Usage
from .process_runner import ProcessRunner, truncate


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


MAX_CODEX_STDOUT_BYTES = 2_000_000
MAX_CODEX_STDERR_BYTES = 500_000
MAX_CODEX_JSONL_LINE_BYTES = 1_000_000
MAX_CODEX_JSONL_EVENTS = 10_000


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
    def __init__(self, executable: str, repo: Path, process_runner: ProcessRunner | None = None, evidence_dir: Path | None = None) -> None:
        self.executable = executable
        self.repo = repo
        self.process_runner = process_runner or ProcessRunner()
        self.evidence_dir = evidence_dir
        self.turn_index = 0

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
        self.turn_index += 1
        result = self.process_runner.run_bytes(
            argv,
            self.repo,
            timeout_seconds,
            max_stdout_bytes=MAX_CODEX_STDOUT_BYTES,
            max_stderr_bytes=MAX_CODEX_STDERR_BYTES,
        )
        self._write_raw_evidence(result, self.turn_index)
        parsed = parse_jsonl_bytes(result.stdout)
        errors = list(parsed["errors"])
        if result.exit_code != 0:
            errors.append(f"codex exited with code {result.exit_code}")
        if result.timed_out:
            errors.append("codex process timed out")
        if result.stdout_limited:
            errors.append(f"codex stdout exceeded {MAX_CODEX_STDOUT_BYTES} bytes")
        if result.stderr_limited:
            errors.append(f"codex stderr exceeded {MAX_CODEX_STDERR_BYTES} bytes")
        return CodexTurnResult(
            not errors and result.exit_code == 0,
            result.exit_code,
            truncate(result.stdout.decode("utf-8", errors="replace")),
            truncate(result.stderr.decode("utf-8", errors="replace")),
            parsed["session_id"],
            parsed["usage"],
            parsed["final_message"],
            tuple(errors),
        )

    def _write_raw_evidence(self, result: RawProcessResult, turn_index: int) -> None:
        if self.evidence_dir is None:
            return
        prefix = f"codex-turn-{turn_index:03d}"
        stdout_name = f"{prefix}-stdout.raw"
        stderr_name = f"{prefix}-stderr.log"
        atomic_write_bytes(self.evidence_dir / stdout_name, result.stdout)
        atomic_write_bytes(self.evidence_dir / stderr_name, result.stderr)
        atomic_write_json(
            self.evidence_dir / f"{prefix}-metadata.json",
            {
                "schema_version": 1,
                "argv": sanitize_argv(result.argv),
                "exit_code": result.exit_code,
                "timed_out": result.timed_out,
                "stdout_size": len(result.stdout),
                "stdout_sha256": sha256_prefixed(result.stdout),
                "stdout_limited": result.stdout_limited,
                "stderr_size": len(result.stderr),
                "stderr_sha256": sha256_prefixed(result.stderr),
                "stderr_limited": result.stderr_limited,
                "stdout_file": stdout_name,
                "stderr_file": stderr_name,
            },
        )


def parse_jsonl(stdout: str) -> dict[str, Any]:
    return parse_jsonl_bytes(stdout.encode("utf-8"))


def parse_jsonl_bytes(stdout: bytes) -> dict[str, Any]:
    stdout_sha = sha256_prefixed(stdout)
    stdout_size = len(stdout)
    decoder = IncrementalJsonlDecoder(
        max_total_bytes=MAX_CODEX_STDOUT_BYTES,
        max_line_bytes=MAX_CODEX_JSONL_LINE_BYTES,
        max_events=MAX_CODEX_JSONL_EVENTS,
    )
    try:
        decoder.feed(stdout)
        decoded = decoder.finish()
        events = decoded.events
        stdout_sha = decoded.stdout_sha256
        stdout_size = decoded.stdout_size
        framing_errors: list[str] = []
    except JsonlStreamError as exc:
        events = decoder.events
        framing_errors = [
            (
                "codex JSONL parse error "
                f"code={exc.code} line={exc.line} byte_offset={exc.byte_offset} "
                f"stdout_sha256={stdout_sha} stdout_size={stdout_size} partial_eof={str(exc.partial_eof).lower()} "
                f"excerpt={exc.excerpt!r}"
            )
        ]
    usage = Usage()
    session_id: str | None = None
    final_message = ""
    errors: list[str] = list(framing_errors)
    saw_thread_started = False
    saw_turn_terminal = False
    for event in events:
        event_type = str(event.get("type") or event.get("event") or "")
        raw = json.dumps(event)
        match = SESSION_RE.search(raw)
        if match and session_id is None:
            session_id = match.group(0)
        if event_type in {"thread.started", "session.created"}:
            saw_thread_started = True
        if event_type.endswith("turn.completed") or event_type == "turn.completed":
            saw_turn_terminal = True
            usage.add(event.get("usage"))
        if event_type.endswith("turn.failed") or event_type == "turn.failed":
            saw_turn_terminal = True
            errors.append(raw)
        value = extract_message_text(event)
        if value is not None:
            final_message = value
        if "usage limit" in raw.lower() or "rate limit" in raw.lower() or "auth" in raw.lower():
            errors.append(raw)
    if events and not saw_thread_started:
        errors.append("codex JSONL missing thread.started")
    if events and session_id is None:
        errors.append("codex JSONL missing thread_id")
    if events and not saw_turn_terminal:
        errors.append("codex JSONL missing turn.completed or turn.failed")
    if events and not final_message:
        errors.append("codex JSONL missing final response")
    return {"session_id": session_id, "usage": usage, "final_message": final_message, "errors": errors}


def sha256_prefixed(data: bytes) -> str:
    import hashlib

    return "sha256:" + hashlib.sha256(data).hexdigest()


def sanitize_argv(argv: tuple[str, ...]) -> list[str]:
    sanitized: list[str] = []
    skip_next = False
    for part in argv:
        if skip_next:
            sanitized.append("<redacted>")
            skip_next = False
            continue
        sanitized.append(part)
        if part in {"--cd", "--output-schema"}:
            skip_next = True
    return sanitized


def extract_message_text(event: dict[str, Any]) -> str | None:
    event_type = str(event.get("type") or event.get("event") or "")
    for key in ("message", "text"):
        value = event.get(key)
        if isinstance(value, str) and event_type in {"agent_message", "message", "final_message"}:
            return value
    content = event.get("content")
    if isinstance(content, str) and event_type in {"agent_message", "message", "final_message"}:
        return content
    if isinstance(content, list):
        text = text_from_content_items(content)
        if text:
            return text
    item = event.get("item")
    if isinstance(item, dict):
        value = item.get("message") or item.get("text")
        if isinstance(value, str):
            return value
        content = item.get("content")
        if isinstance(content, str):
            return content
        if isinstance(content, list):
            text = text_from_content_items(content)
            if text:
                return text
    return None


def text_from_content_items(items: list[Any]) -> str:
    parts: list[str] = []
    for item in items:
        if isinstance(item, str):
            parts.append(item)
        elif isinstance(item, dict):
            value = item.get("text") or item.get("content")
            if isinstance(value, str):
                parts.append(value)
    return "".join(parts)
