from __future__ import annotations

import json
import os
import subprocess
import io
import sys
import tempfile
import time
import unittest
import uuid
from contextlib import redirect_stdout
from pathlib import Path
from unittest import mock

import scripts.run_agent_loop as run_agent_loop
from scripts.agent_loop.codex_client import CodexClient, CodexTurnResult, discover_codex, parse_jsonl
from scripts.agent_loop.evidence import EvidenceStore, state_to_json
from scripts.agent_loop.git_guard import audit_scope, git
from scripts.agent_loop.models import Finding, LoopState, ProcessResult, RawProcessResult, Usage
from scripts.agent_loop.process_runner import ProcessRunner
from scripts.agent_loop.publisher import Publisher
from scripts.agent_loop.runner import LoopRunner
from scripts.agent_loop.task_spec import parse_task_spec

from tests.python.test_agent_loop_task_spec import valid_spec


class FakeProcessRunner(ProcessRunner):
    def __init__(self, results: list[ProcessResult] | None = None) -> None:
        self.results = list(results or [])
        self.calls: list[list[str]] = []
        self.cwds: list[Path] = []
        self.envs: list[dict[str, str] | None] = []

    def run(self, argv: list[str], cwd: Path, timeout_seconds: int, *, env: dict[str, str] | None = None) -> ProcessResult:
        self.calls.append(argv)
        self.cwds.append(cwd)
        self.envs.append(dict(env) if env is not None else None)
        if self.results:
            return self.results.pop(0)
        return ProcessResult(tuple(argv), 0, "", "")

    def run_bytes(
        self,
        argv: list[str],
        cwd: Path,
        timeout_seconds: int,
        *,
        max_stdout_bytes: int,
        max_stderr_bytes: int,
        env: dict[str, str] | None = None,
    ) -> RawProcessResult:
        self.calls.append(argv)
        self.cwds.append(cwd)
        self.envs.append(dict(env) if env is not None else None)
        if self.results:
            result = self.results.pop(0)
            return RawProcessResult(
                result.argv,
                result.exit_code,
                result.stdout.encode("utf-8"),
                result.stderr.encode("utf-8"),
                result.timed_out,
            )
        return RawProcessResult(tuple(argv), 0, b"", b"")


class AgentLoopRuntimeTest(unittest.TestCase):
    def test_codex_discovery_prefers_argument_env_path_and_rejects_windowsapps(self) -> None:
        def verifier(path: str):
            if "bad" in path:
                return None, "", "Access denied"
            return 0, "codex-cli 1.2.3\n", ""

        result = discover_codex("C:/Codex With Spaces/codex.exe", env={}, which=lambda _name: None, runner=verifier)
        self.assertTrue(result.ok)
        self.assertEqual("argument", result.method)
        self.assertEqual("codex-cli 1.2.3", result.version)

        result = discover_codex(env={"CODEX_EXECUTABLE": "C:/env/codex.exe"}, which=lambda _name: "C:/path/codex.exe", runner=verifier)
        self.assertEqual("environment", result.method)

        result = discover_codex(env={}, which=lambda _name: "C:/path/codex.exe", runner=verifier)
        self.assertEqual("path", result.method)

        private = "C:/Program Files/WindowsApps/OpenAI.Codex_1/app/resources/codex.exe"
        result = discover_codex(env={"LOCALAPPDATA": "C:/Users/u/AppData/Local"}, which=lambda _name: private, runner=verifier)
        self.assertEqual("windows_standalone", result.method)
        self.assertNotIn("WindowsApps", result.executable or "")

        result = discover_codex(env={}, which=lambda _name: "C:/bad/codex.exe", runner=verifier)
        self.assertFalse(result.ok)

    def test_parse_jsonl_usage_session_and_malformed(self) -> None:
        stdout = "\n".join(
            [
                json.dumps({"type": "session.created", "id": "11111111-1111-1111-1111-111111111111"}),
                json.dumps({"type": "turn.completed", "usage": {"input_tokens": 2, "cached_input_tokens": 1, "output_tokens": 3}}),
                json.dumps({"type": "final_message", "message": "{\"ok\":true}"}),
                "{bad",
            ]
        )
        parsed = parse_jsonl(stdout)
        self.assertEqual("11111111-1111-1111-1111-111111111111", parsed["session_id"])
        self.assertEqual(2, parsed["usage"].input_tokens)
        self.assertIn("malformed", "\n".join(parsed["errors"]))

    def test_codex_commands_use_json_sandbox_resume_and_output_schema(self) -> None:
        stream = "\n".join(
            [
                json.dumps({"type": "thread.started", "thread_id": "11111111-1111-1111-1111-111111111111"}),
                json.dumps({"type": "turn.completed"}),
                json.dumps({"type": "final_message", "message": "ok"}),
            ]
        ) + "\n"
        runner = FakeProcessRunner([ProcessResult(("x",), 0, stream, "")])
        client = CodexClient("codex", Path.cwd(), runner)
        client.exec("prompt", sandbox="read-only", output_schema=Path("schema.json"), timeout_seconds=1)
        self.assertIn("--json", runner.calls[0])
        self.assertIn("--sandbox", runner.calls[0])
        self.assertIn("read-only", runner.calls[0])
        self.assertIn("--output-schema", runner.calls[0])

        runner.results.append(ProcessResult(("x",), 0, stream, ""))
        client.resume("11111111-1111-1111-1111-111111111111", "fix", sandbox="workspace-write", output_schema=None, timeout_seconds=1)
        self.assertEqual(
            ["codex", "exec", "--json", "--sandbox", "workspace-write", "--cd", str(Path.cwd()), "resume", "11111111-1111-1111-1111-111111111111", "fix"],
            runner.calls[1],
        )

    def test_process_runner_rejects_non_positive_timeout_and_times_out_finitely(self) -> None:
        runner = ProcessRunner()
        with self.assertRaises(ValueError):
            runner.run([sys.executable, "--version"], Path.cwd(), 0)
        start = time.perf_counter()
        result = runner.run([sys.executable, "-c", "import time; time.sleep(10)"], Path.cwd(), 1)
        elapsed = time.perf_counter() - start
        self.assertTrue(result.timed_out)
        self.assertIsNone(result.exit_code)
        self.assertLess(elapsed, 8)

    def test_process_runner_run_bytes_separates_streams_and_times_out(self) -> None:
        runner = ProcessRunner()
        result = runner.run_bytes(
            [sys.executable, "-c", "import sys; sys.stdout.buffer.write(b'out'); sys.stderr.buffer.write(b'err')"],
            Path.cwd(),
            5,
            max_stdout_bytes=100,
            max_stderr_bytes=100,
        )
        self.assertEqual(0, result.exit_code)
        self.assertEqual(b"out", result.stdout)
        self.assertEqual(b"err", result.stderr)

        start = time.perf_counter()
        result = runner.run_bytes(
            [sys.executable, "-c", "import time; time.sleep(10)"],
            Path.cwd(),
            1,
            max_stdout_bytes=100,
            max_stderr_bytes=100,
        )
        self.assertTrue(result.timed_out)
        self.assertIsNone(result.exit_code)
        self.assertLess(time.perf_counter() - start, 8)

    def test_scope_allowed_protected_untracked_staged_deleted_and_rename(self) -> None:
        with temp_git_repo() as repo:
            (repo / "allowed").mkdir()
            (repo / "allowed" / "a.txt").write_text("a", encoding="utf-8")
            (repo / "exact.txt").write_text("x", encoding="utf-8")
            (repo / "protected").mkdir()
            (repo / "protected" / "p.txt").write_text("p", encoding="utf-8")
            git(repo, "add", "--", ".")
            git(repo, "commit", "-m", "base")
            spec_data = valid_spec()
            spec_data["allowed_paths"] = ["allowed/", "exact.txt", "renamed.txt"]
            spec_data["protected_paths"] = ["protected/"]
            spec = parse_task_spec(spec_data)

            (repo / "allowed" / "new.txt").write_text("new", encoding="utf-8")
            (repo / "exact.txt").write_text("changed", encoding="utf-8")
            (repo / "outside.txt").write_text("bad", encoding="utf-8")
            (repo / "protected" / "p.txt").write_text("bad", encoding="utf-8")
            git(repo, "mv", "allowed/a.txt", "renamed.txt")
            git(repo, "rm", "-f", "exact.txt")

            audit = audit_scope(repo, spec)
            self.assertFalse(audit.ok)
            self.assertIn("outside.txt: outside allowed paths", audit.violations)
            self.assertIn("protected/p.txt: protected path", audit.violations)
            self.assertIn("renamed.txt", audit.changed_files)

    def test_evidence_json_shape_and_atomic_resume_command(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            repo = Path(tmp)
            state = LoopState("run1", "task", "hash", "origin/main", "abc", "feat/x", "branch_ready")
            store = EvidenceStore(repo, "run1", clock=lambda: 10)
            data = store.write_state(state, start_time=1)
            self.assertEqual(1, data["schema_version"])
            self.assertIn("resume --run-id run1", data["resume_command"])
            raw = (repo / ".agent-loop" / "runs" / "run1" / "state.json").read_bytes()
            self.assertFalse(raw.startswith(b"\xef\xbb\xbf"))
            self.assertTrue(raw.endswith(b"\n"))

    def test_loop_state_from_json_without_agents_runtime_uses_safe_default(self) -> None:
        state = run_agent_loop.loop_state_from_json(
            {
                "run_id": "run1",
                "task_id": "task",
                "task_hash": "hash",
                "base_ref": "origin/main",
                "base_sha": "abc",
                "branch": "feat/x",
                "status": "branch_ready",
            }
        )
        self.assertEqual({}, state.agents_runtime)
        self.assertEqual({}, state.runtime_temp)
        self.assertEqual({}, state.budget_observations)

    def test_loop_state_from_json_restores_checks_and_findings_for_resume(self) -> None:
        state = run_agent_loop.loop_state_from_json(
            {
                "run_id": "run1",
                "task_id": "task",
                "task_hash": "hash",
                "base_ref": "origin/main",
                "base_sha": "abc",
                "branch": "feat/x",
                "status": "fixing",
                "checks": [
                    {
                        "id": "focused",
                        "argv": ["python", "--version"],
                        "status": "FAIL",
                        "exit_code": 1,
                        "stdout": "",
                        "stderr": "boom",
                    }
                ],
                "findings": [
                    {
                        "id": "REV-001",
                        "severity": "medium",
                        "title": "Fix me",
                        "evidence": "details",
                        "path": "allowed.txt",
                        "line": 4,
                        "in_scope": True,
                        "suggested_fix": "change it",
                    }
                ],
            }
        )
        self.assertEqual(1, len(state.checks))
        self.assertEqual("focused", state.checks[0].id)
        self.assertEqual("FAIL", state.checks[0].status)
        self.assertEqual(1, len(state.findings))
        self.assertEqual("REV-001", state.findings[0].id)
        self.assertEqual("medium", state.findings[0].severity)

    def test_resume_rejects_terminal_and_can_continue_valid_checkpoint_with_fake_runner(self) -> None:
        run_id = f"unit-resume-{uuid.uuid4().hex}"
        run_root = Path.cwd() / ".agent-loop" / "runs" / run_id
        try:
            run_root.mkdir(parents=True, exist_ok=True)
            task = valid_spec()
            task["branch"] = "feat/resume-test"
            task["allowed_paths"] = ["docs/agent_loop.md"]
            task_path = run_root / "task.json"
            task_path.write_text(json.dumps(task), encoding="utf-8")
            _spec, task_hash = run_agent_loop.load_task_spec(task_path)
            base_sha = "abc123"
            state = {
                "schema_version": 1,
                "run_id": run_id,
                "task_id": task["task_id"],
                "task_hash": task_hash,
                "status": "passed",
                "base_ref": "origin/main",
                "base_sha": base_sha,
                "branch": task["branch"],
                "iteration": 0,
                "codex_turns": 0,
                "review_turns": 0,
                "usage": {},
                "changed_files": [],
                "checks": [],
                "findings": [],
                "writer_session_id": None,
                "fingerprint": None,
                "errors": [],
                "pr_url": None,
                "resume_command": None,
            }
            (run_root / "state.json").write_text(json.dumps(state), encoding="utf-8")
            args = type("Args", (), {"run_id": run_id, "json_output": None, "codex_executable": None, "publish": False})()
            with mock.patch.object(run_agent_loop, "current_branch", return_value="feat/resume-test"), mock.patch.object(
                run_agent_loop,
                "rev_parse",
                return_value=base_sha,
            ), redirect_stdout(io.StringIO()):
                self.assertEqual(6, run_agent_loop.command_resume(args))

            state["status"] = "branch_ready"
            (run_root / "state.json").write_text(json.dumps(state), encoding="utf-8")

            class FakeLoop:
                def __init__(self, *_args, **_kwargs):
                    pass

                def resume(self, loaded_state, *, publish=False):
                    loaded_state.status = "passed"
                    return loaded_state

            with mock.patch.object(run_agent_loop, "LoopRunner", FakeLoop), mock.patch.object(
                run_agent_loop,
                "current_branch",
                return_value="feat/resume-test",
            ), mock.patch.object(run_agent_loop, "rev_parse", return_value=base_sha):
                with redirect_stdout(io.StringIO()):
                    self.assertEqual(0, run_agent_loop.command_resume(args))
        finally:
            if run_root.exists():
                for path in sorted(run_root.rglob("*"), reverse=True):
                    if path.is_file():
                        path.unlink()
                    elif path.is_dir():
                        path.rmdir()
                if run_root.exists():
                    run_root.rmdir()

    def test_resume_legacy_checkpoint_with_preexisting_agents_directory_does_not_remove_it(self) -> None:
        with temp_git_repo() as repo:
            spec = repo_spec(repo)
            agents = repo / ".agents"
            agents.mkdir()
            git(repo, "switch", "-c", spec.branch)
            state = LoopState("legacy", spec.task_id, "hash", "origin/main", git(repo, "rev-parse", "origin/main").stdout.strip(), spec.branch, "branch_ready")

            class FakeCodex:
                def __init__(self, _exe, repo_path, _runner, _evidence_dir=None, **_kwargs):
                    self.repo = repo_path

                def login_status(self):
                    return ProcessResult(("codex",), 0, "Logged in using ChatGPT", "")

                def exec(self, *_args, **_kwargs):
                    (self.repo / "allowed.txt").write_text("done", encoding="utf-8")
                    return CodexTurnResult(True, 0, "", "", "11111111-1111-1111-1111-111111111111", Usage(), json.dumps({"status": "implemented", "summary": "ok", "changed_paths_claimed": ["allowed.txt"], "needs_input": False, "blocker": None}), ())

            with mock.patch("scripts.agent_loop.runner.discover_codex", return_value=type("D", (), {"ok": True, "executable": "codex", "errors": ()})()), mock.patch(
                "scripts.agent_loop.runner.CodexClient",
                FakeCodex,
            ), mock.patch("scripts.agent_loop.runner.review_diff", return_value=([], "")):
                resumed = LoopRunner(repo, spec, "hash", process_runner=FakeProcessRunner([ProcessResult(("check",), 0, "ok", "")]), run_id="legacy").resume(state)

            self.assertEqual("passed", resumed.status)
            self.assertTrue(agents.exists())
            self.assertFalse(resumed.agents_runtime["created_by_run"])
            self.assertEqual("preexisting", resumed.agents_runtime["classification"])

    def test_command_preflight_rejects_invalid_git_environment_before_git_calls(self) -> None:
        task = valid_spec()
        with tempfile.TemporaryDirectory() as tmp:
            task_path = Path(tmp) / "task.json"
            task_path.write_text(json.dumps(task), encoding="utf-8")
            args = type("Args", (), {"task": task_path, "json_output": None, "codex_executable": None})()
            with mock.patch.object(run_agent_loop, "build_git_scoped_environment", side_effect=ValueError("codex.invalid_git_config_environment: bad env")), mock.patch.object(
                run_agent_loop,
                "ensure_clean",
                side_effect=AssertionError("ensure_clean must not run after invalid env"),
            ), redirect_stdout(io.StringIO()):
                self.assertEqual(6, run_agent_loop.command_preflight(args))

    def test_loop_success_first_iteration(self) -> None:
        with temp_git_repo() as repo:
            spec = repo_spec(repo)

            class FakeCodex:
                def __init__(self, _exe, repo_path, _runner, _evidence_dir=None, **_kwargs):
                    self.repo = repo_path

                def login_status(self):
                    return ProcessResult(("codex",), 0, "Logged in using ChatGPT", "")

                def exec(self, *_args, **_kwargs):
                    (self.repo / "allowed.txt").write_text("done", encoding="utf-8")
                    return CodexTurnResult(True, 0, "", "", "11111111-1111-1111-1111-111111111111", Usage(), "ok", ())

            with mock.patch("scripts.agent_loop.runner.discover_codex", return_value=type("D", (), {"ok": True, "executable": "codex", "errors": ()})()), mock.patch(
                "scripts.agent_loop.runner.CodexClient",
                FakeCodex,
            ), mock.patch("scripts.agent_loop.runner.review_diff", return_value=([], "")):
                runner = LoopRunner(repo, spec, "hash", process_runner=FakeProcessRunner([ProcessResult(("check",), 0, "ok", "")]), run_id="run")
                state = runner.run()

            self.assertEqual("passed", state.status)
            self.assertEqual(["allowed.txt"], state.changed_files)

    def test_loop_check_failure_then_correction_and_low_nonblocking(self) -> None:
        with temp_git_repo() as repo:
            spec = repo_spec(repo)
            calls = {"n": 0}

            class FakeCodex:
                def __init__(self, _exe, repo_path, _runner, _evidence_dir=None, **_kwargs):
                    self.repo = repo_path

                def login_status(self):
                    return ProcessResult(("codex",), 0, "Logged in using ChatGPT", "")

                def exec(self, *_args, **_kwargs):
                    calls["n"] += 1
                    (self.repo / "allowed.txt").write_text(f"done {calls['n']}", encoding="utf-8")
                    return CodexTurnResult(True, 0, "", "", "11111111-1111-1111-1111-111111111111", Usage(), "ok", ())

                def resume(self, *_args, **_kwargs):
                    return self.exec()

            checks = [ProcessResult(("check",), 1, "", "fail"), ProcessResult(("check",), 0, "ok", "")]
            low = [Finding("REV-001", "low", "minor", "e")]
            with mock.patch("scripts.agent_loop.runner.discover_codex", return_value=type("D", (), {"ok": True, "executable": "codex", "errors": ()})()), mock.patch(
                "scripts.agent_loop.runner.CodexClient",
                FakeCodex,
            ), mock.patch("scripts.agent_loop.runner.review_diff", return_value=(low, "")):
                state = LoopRunner(repo, spec, "hash", process_runner=FakeProcessRunner(checks), run_id="run").run()

            self.assertEqual("passed", state.status)
            self.assertEqual(2, state.iteration)
            self.assertEqual("low", state.findings[0].severity)

    def test_loop_finding_out_of_scope_needs_input_and_repeated_failure_budget(self) -> None:
        with temp_git_repo() as repo:
            spec = repo_spec(repo)

            class FakeCodex:
                def __init__(self, _exe, repo_path, _runner, _evidence_dir=None, **_kwargs):
                    self.repo = repo_path

                def login_status(self):
                    return ProcessResult(("codex",), 0, "Logged in using ChatGPT", "")

                def exec(self, *_args, **_kwargs):
                    (self.repo / "allowed.txt").write_text("same", encoding="utf-8")
                    return CodexTurnResult(True, 0, "", "", None, Usage(), "ok", ())

            finding = [Finding("REV-001", "high", "outside", "e", "other.txt", None, False)]
            with mock.patch("scripts.agent_loop.runner.discover_codex", return_value=type("D", (), {"ok": True, "executable": "codex", "errors": ()})()), mock.patch(
                "scripts.agent_loop.runner.CodexClient",
                FakeCodex,
            ), mock.patch("scripts.agent_loop.runner.review_diff", return_value=(finding, "")):
                state = LoopRunner(repo, spec, "hash", process_runner=FakeProcessRunner([ProcessResult(("check",), 0, "ok", "")]), run_id="run").run()
            self.assertEqual("needs_input", state.status)

        with temp_git_repo() as repo:
            spec = repo_spec(repo)
            with mock.patch("scripts.agent_loop.runner.discover_codex", return_value=type("D", (), {"ok": True, "executable": "codex", "errors": ()})()), mock.patch(
                "scripts.agent_loop.runner.CodexClient",
                FakeCodex,
            ):
                checks = [ProcessResult(("check",), 1, "", "fail"), ProcessResult(("check",), 1, "", "fail")]
                state = LoopRunner(repo, spec, "hash", process_runner=FakeProcessRunner(checks), run_id="run").run()
            self.assertEqual("budget_exhausted", state.status)

    def test_publication_requires_double_authorization_and_uses_draft_pr(self) -> None:
        with temp_git_repo() as repo:
            spec_data = valid_spec()
            spec_data["publication"] = {"commit": True, "push": True, "draft_pr": True, "mark_ready": False, "merge": False}
            spec = parse_task_spec(spec_data)
            state = LoopState("run", spec.task_id, "hash", "origin/main", "abc", spec.branch, "passed")
            state.changed_files = ["allowed.txt"]
            publisher = Publisher(repo, FakeProcessRunner())
            self.assertIsNone(publisher.publish(spec, state, publish_flag=False))
            body = publisher.build_pr_body(spec, state)
            self.assertIn("No auto-merge", body)
            self.assertIn("No automatic mark-ready", body)

    def test_publisher_refuses_existing_ready_pr_metadata(self) -> None:
        with temp_git_repo() as repo:
            spec_data = valid_spec()
            spec_data["publication"] = {"commit": True, "push": True, "draft_pr": True, "mark_ready": False, "merge": False}
            spec = parse_task_spec(spec_data)
            state = LoopState("run", spec.task_id, "hash", "origin/main", "abc", spec.branch, "passed")
            state.changed_files = []
            fake = FakeProcessRunner(
                [
                    ProcessResult(("gh",), 0, '{"url":"https://example.invalid/pr/1","isDraft":false}', ""),
                ]
            )
            publisher = Publisher(repo, fake)
            with mock.patch("scripts.agent_loop.publisher.git"), mock.patch("scripts.agent_loop.publisher.explicit_stage"):
                with self.assertRaisesRegex(Exception, "not draft"):
                    publisher.publish(spec, state, publish_flag=True)


def temp_git_repo():
    class RepoContext:
        def __enter__(self):
            self.tmp = tempfile.TemporaryDirectory()
            self.repo = Path(self.tmp.name)
            git(self.repo, "init", "-b", "main")
            git(self.repo, "config", "user.email", "test@example.com")
            git(self.repo, "config", "user.name", "Test")
            (self.repo / "seed.txt").write_text("seed", encoding="utf-8")
            git(self.repo, "add", "--", "seed.txt")
            git(self.repo, "commit", "-m", "base")
            git(self.repo, "branch", "origin/main")
            return self.repo

        def __exit__(self, *_exc):
            self.tmp.cleanup()

    return RepoContext()


def repo_spec(repo: Path):
    data = valid_spec()
    data["allowed_paths"] = ["allowed.txt"]
    data["protected_paths"] = ["protected/"]
    data["base_ref"] = "origin/main"
    data["branch"] = "feat/test"
    data["checks"] = [{"id": "repo", "argv": ["python", "--version"], "timeout_seconds": 5}]
    data["budgets"]["max_iterations"] = 3
    data["budgets"]["max_repeated_failure"] = 2
    return parse_task_spec(data)


if __name__ == "__main__":
    unittest.main()
