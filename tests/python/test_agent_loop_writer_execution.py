from __future__ import annotations

import json
import os
import tempfile
import unittest
from contextlib import ExitStack, contextmanager
from pathlib import Path
from unittest import mock

from scripts.agent_loop.codex_client import CodexDiscovery
from scripts.agent_loop.codex_client import CodexClient, CodexTurnResult, parse_jsonl, sanitize_argv
from scripts.agent_loop.git_guard import git
from scripts.agent_loop.git_scope import git_safe_directory_value
from scripts.agent_loop.models import Finding, ProcessResult, Usage
from scripts.agent_loop.reviewer import ReviewError, review_diff
from scripts.agent_loop.runner import LoopRunner, parse_writer_result
from scripts.agent_loop.task_spec import parse_task_spec

from tests.python.test_agent_loop_runtime import FakeProcessRunner
from tests.python.test_agent_loop_task_spec import valid_spec


SESSION_ID = "11111111-1111-1111-1111-111111111111"


class WriterExecutionTest(unittest.TestCase):
    def test_codex_writer_initial_and_resume_commands_are_workspace_write(self) -> None:
        stream = jsonl_stream()
        runner = FakeProcessRunner([ProcessResult(("x",), 0, stream, ""), ProcessResult(("x",), 0, stream, "")])
        client = CodexClient("codex", Path("C:/Repo With Spaces"), runner)
        client.exec("prompt", sandbox="workspace-write", output_schema=Path("schema.json"), timeout_seconds=1)
        client.resume(SESSION_ID, "fix", sandbox="workspace-write", output_schema=Path("schema.json"), timeout_seconds=1)
        initial, resumed = runner.calls
        self.assertIn("--sandbox", initial)
        self.assertIn("workspace-write", initial)
        self.assertIn("--cd", initial)
        self.assertNotIn("danger-full-access", initial)
        self.assertNotIn("--dangerously-bypass-approvals-and-sandbox", initial)
        self.assertLess(initial.index("--sandbox"), initial.index("workspace-write"))
        self.assertLess(resumed.index("--sandbox"), resumed.index("resume"))
        self.assertLess(resumed.index("--cd"), resumed.index("resume"))
        self.assertEqual("resume", resumed[7])
        self.assertNotIn("danger-full-access", resumed)
        self.assertEqual([Path("C:/Repo With Spaces"), Path("C:/Repo With Spaces")], runner.cwds)

    def test_codex_metadata_argv_redacts_prompt_and_session_id(self) -> None:
        sanitized = sanitize_argv(
            (
                "codex",
                "exec",
                "--json",
                "--sandbox",
                "workspace-write",
                "--cd",
                "C:/Users/toin/Documents/Unity/Victoriant Chile",
                "resume",
                SESSION_ID,
                "implement sensitive prompt now",
            )
        )

        self.assertIn("<session-id>", sanitized)
        self.assertIn("<prompt>", sanitized)
        self.assertIn("<redacted>", sanitized)
        self.assertNotIn(SESSION_ID, sanitized)
        self.assertNotIn("implement sensitive prompt now", sanitized)
        self.assertNotIn("C:/Users/toin/Documents/Unity/Victoriant Chile", sanitized)

    def test_reviewer_is_read_only_and_detects_write_attempt(self) -> None:
        with temp_git_repo() as repo:
            spec = repo_spec()

            class FakeReadOnlyClient:
                def __init__(self, mutate: bool) -> None:
                    self.repo = repo
                    self.sandbox = ""
                    self.mutate = mutate

                def exec(self, prompt, *, sandbox, output_schema, timeout_seconds):
                    self.sandbox = sandbox
                    self.prompt = prompt
                    if self.mutate:
                        (repo / "allowed.txt").write_text("bad", encoding="utf-8")
                    return CodexTurnResult(True, 0, "", "", SESSION_ID, Usage(), json.dumps({"verdict": "pass", "summary": "ok", "findings": []}), ())

            client = FakeReadOnlyClient(False)
            findings, _ = review_diff(client, spec, "HEAD", repo / "review.schema.json", 10)
            self.assertEqual([], findings)
            self.assertEqual("read-only", client.sandbox)
            self.assertIn("do not modify", client.prompt.lower())

            with self.assertRaisesRegex(ReviewError, "working tree changed"):
                review_diff(FakeReadOnlyClient(True), spec, "HEAD", repo / "review.schema.json", 10)

    def test_writer_prompt_requires_implementation_and_allows_allowed_paths(self) -> None:
        with temp_git_repo() as repo:
            spec = repo_spec()
            loop_state = __import__("scripts.agent_loop.models", fromlist=["LoopState"]).LoopState("run", spec.task_id, "hash", "origin/main", "abc", spec.branch, "branch_ready")
            prompt = LoopRunner(repo, spec, "hash").writer_prompt(loop_state)
            self.assertIn("implement the requested change now", prompt)
            self.assertIn("authorized and expected to create, modify, rename, or delete only the exact allowed paths", prompt)
            self.assertIn("must edit the working tree", prompt)
            self.assertIn("must not commit", prompt)
            self.assertIn("Allowed paths: allowed.txt", prompt)
            self.assertNotIn("do not make changes", prompt.lower())
            self.assertNotIn("no changes", prompt.lower())

    def test_no_changes_with_failed_check_resumes_same_thread_with_diagnostics(self) -> None:
        with temp_git_repo() as repo:
            spec = repo_spec()
            calls: list[tuple[str, str]] = []

            class NoOpCodex:
                def __init__(self, _exe, repo_path, _runner, _evidence_dir=None, **_kwargs):
                    self.repo = repo_path

                def login_status(self):
                    return ProcessResult(("codex",), 0, "Logged in using ChatGPT", "")

                def exec(self, prompt, **_kwargs):
                    calls.append(("exec", prompt))
                    return writer_turn()

                def resume(self, session_id, prompt, **kwargs):
                    calls.append(("resume", prompt))
                    calls.append(("resume_session", session_id))
                    calls.append(("resume_sandbox", kwargs["sandbox"]))
                    return writer_turn()

            failing_checks = [ProcessResult(("check",), 1, "", "ModuleNotFoundError: No module named test"), ProcessResult(("check",), 1, "", "ModuleNotFoundError: No module named test")]
            with patched_writer_runtime(repo, NoOpCodex):
                state = LoopRunner(repo, spec, "hash", process_runner=FakeProcessRunner(failing_checks), run_id="run").run()

            self.assertEqual("budget_exhausted", state.status)
            self.assertEqual(["exec", "resume"], [call[0] for call in calls if call[0] in {"exec", "resume"}])
            self.assertIn(("resume_session", SESSION_ID), calls)
            self.assertIn(("resume_sandbox", "workspace-write"), calls)
            self.assertIn("ModuleNotFoundError", calls[1][1])
            self.assertIn("No working-tree changes were detected", calls[1][1])
            self.assertIn("writer_no_changes", "\n".join(state.errors))

    def test_tool_failures_with_no_changes_are_delivered_to_resume(self) -> None:
        with temp_git_repo() as repo:
            spec = repo_spec()
            prompts: list[str] = []

            class ToolFailingCodex:
                def __init__(self, _exe, repo_path, _runner, _evidence_dir=None, **_kwargs):
                    pass

                def login_status(self):
                    return ProcessResult(("codex",), 0, "Logged in using ChatGPT", "")

                def exec(self, prompt, **_kwargs):
                    prompts.append(prompt)
                    return writer_turn(tool_failures=[{"event_index": 4, "item_type": "file_change", "status": "failed"}])

                def resume(self, _session_id, prompt, **_kwargs):
                    prompts.append(prompt)
                    return writer_turn(tool_failures=[{"event_index": 5, "item_type": "command_execution", "status": "failed", "exit_code": -1}])

            checks = [ProcessResult(("check",), 1, "", "missing file"), ProcessResult(("check",), 1, "", "missing file")]
            with patched_writer_runtime(repo, ToolFailingCodex):
                state = LoopRunner(repo, spec, "hash", process_runner=FakeProcessRunner(checks), run_id="toolfail").run()
            evidence = json.loads((repo / ".agent-loop" / "runs" / "toolfail" / "writer-turn-001.json").read_text(encoding="utf-8"))
            self.assertEqual("budget_exhausted", state.status)
            self.assertEqual("file_change", evidence["tool_failures"][0]["item_type"])
            self.assertIn("writer_tool_failure", "\n".join(state.errors))
            self.assertIn("writer_tool_failure", prompts[1])
            self.assertIn("No working-tree changes were detected", prompts[1])

    def test_tool_failure_followed_by_real_change_can_pass(self) -> None:
        with temp_git_repo() as repo:
            spec = repo_spec()

            class ToolFailingButImplementingCodex:
                def __init__(self, _exe, repo_path, _runner, _evidence_dir=None, **_kwargs):
                    self.repo = repo_path

                def login_status(self):
                    return ProcessResult(("codex",), 0, "Logged in using ChatGPT", "")

                def exec(self, *_args, **_kwargs):
                    (self.repo / "allowed.txt").write_text("done", encoding="utf-8")
                    return writer_turn(changed_paths_claimed=[], tool_failures=[{"event_index": 2, "item_type": "command_execution", "status": "failed", "exit_code": -1}])

            with patched_writer_runtime(repo, ToolFailingButImplementingCodex), mock.patch("scripts.agent_loop.runner.review_diff", return_value=([], "")):
                state = LoopRunner(repo, spec, "hash", process_runner=FakeProcessRunner([ProcessResult(("check",), 0, "ok", "")]), run_id="recover").run()
            evidence = json.loads((repo / ".agent-loop" / "runs" / "recover" / "writer-turn-001.json").read_text(encoding="utf-8"))
            self.assertEqual("passed", state.status)
            self.assertEqual(["allowed.txt"], state.changed_files)
            self.assertEqual("command_execution", evidence["tool_failures"][0]["item_type"])
            self.assertNotIn("writer_tool_failure", "\n".join(state.errors))

    def test_claimed_changes_without_git_changes_is_recorded_as_discrepancy(self) -> None:
        with temp_git_repo() as repo:
            spec = repo_spec()

            class ClaimOnlyCodex:
                def __init__(self, _exe, repo_path, _runner, _evidence_dir=None, **_kwargs):
                    pass

                def login_status(self):
                    return ProcessResult(("codex",), 0, "Logged in using ChatGPT", "")

                def exec(self, *_args, **_kwargs):
                    return writer_turn(changed_paths_claimed=["allowed.txt"])

                def resume(self, *_args, **_kwargs):
                    return writer_turn(changed_paths_claimed=["allowed.txt"])

            checks = [ProcessResult(("check",), 1, "", "fail"), ProcessResult(("check",), 1, "", "fail")]
            with patched_writer_runtime(repo, ClaimOnlyCodex):
                state = LoopRunner(repo, spec, "hash", process_runner=FakeProcessRunner(checks), run_id="claim").run()
            evidence = json.loads((repo / ".agent-loop" / "runs" / "claim" / "writer-turn-001.json").read_text(encoding="utf-8"))
            self.assertTrue(evidence["claim_discrepancy"])
            self.assertEqual(["allowed.txt"], evidence["changed_paths_claimed"])
            self.assertEqual([], evidence["changed_paths_real"])
            self.assertEqual("budget_exhausted", state.status)

    def test_writer_needs_input_stops_without_checks(self) -> None:
        with temp_git_repo() as repo:
            spec = repo_spec()

            class NeedsInputCodex:
                def __init__(self, _exe, repo_path, _runner, _evidence_dir=None, **_kwargs):
                    pass

                def login_status(self):
                    return ProcessResult(("codex",), 0, "Logged in using ChatGPT", "")

                def exec(self, *_args, **_kwargs):
                    return writer_turn(status="needs_input", needs_input=True, blocker="missing decision")

            with patched_writer_runtime(repo, NeedsInputCodex):
                state = LoopRunner(repo, spec, "hash", process_runner=FakeProcessRunner([ProcessResult(("check",), 0, "ok", "")]), run_id="input").run()
            self.assertEqual("needs_input", state.status)
            self.assertEqual([], state.checks)
            self.assertIn("missing decision", state.errors)

    def test_no_changes_even_when_checks_pass_requires_human_confirmation(self) -> None:
        with temp_git_repo() as repo:
            spec = repo_spec()

            class NoOpCodex:
                def __init__(self, _exe, repo_path, _runner, _evidence_dir=None, **_kwargs):
                    pass

                def login_status(self):
                    return ProcessResult(("codex",), 0, "Logged in using ChatGPT", "")

                def exec(self, *_args, **_kwargs):
                    return writer_turn()

            with patched_writer_runtime(repo, NoOpCodex), mock.patch("scripts.agent_loop.runner.review_diff", return_value=([], "")):
                state = LoopRunner(repo, spec, "hash", process_runner=FakeProcessRunner([ProcessResult(("check",), 0, "ok", "")]), run_id="satisfied").run()
            self.assertEqual("needs_input", state.status)
            self.assertIn("writer produced no changes", "\n".join(state.errors))

    def test_regression_writer_creates_allowed_file_reaches_review_and_passes(self) -> None:
        with temp_git_repo() as repo:
            spec = repo_spec()
            review_called = {"value": False}

            class ImplementingCodex:
                def __init__(self, _exe, repo_path, _runner, _evidence_dir=None, **_kwargs):
                    self.repo = repo_path

                def login_status(self):
                    return ProcessResult(("codex",), 0, "Logged in using ChatGPT", "")

                def exec(self, *_args, **_kwargs):
                    (self.repo / "allowed.txt").write_text("done", encoding="utf-8")
                    return writer_turn(changed_paths_claimed=["allowed.txt"])

            def fake_review(*_args, **_kwargs):
                review_called["value"] = True
                return [], ""

            with patched_writer_runtime(repo, ImplementingCodex), mock.patch("scripts.agent_loop.runner.review_diff", side_effect=fake_review):
                state = LoopRunner(repo, spec, "hash", process_runner=FakeProcessRunner([ProcessResult(("check",), 0, "ok", "")]), run_id="pass").run()
            self.assertEqual("passed", state.status)
            self.assertTrue(review_called["value"])
            self.assertEqual(["allowed.txt"], state.changed_files)

    def test_writer_error_usage_limit_timeout_and_turn_failed_classification(self) -> None:
        with temp_git_repo() as repo:
            spec = repo_spec()

            class ErrorCodex:
                def __init__(self, _exe, repo_path, _runner, _evidence_dir=None, **_kwargs):
                    pass

                def login_status(self):
                    return ProcessResult(("codex",), 0, "Logged in using ChatGPT", "")

                def exec(self, *_args, **_kwargs):
                    return CodexTurnResult(False, 1, "", "", SESSION_ID, Usage(), "", ("usage limit",))

            with patched_writer_runtime(repo, ErrorCodex):
                state = LoopRunner(repo, spec, "hash", process_runner=FakeProcessRunner(), run_id="usage").run()
            self.assertEqual("usage_limit_reached", state.status)

    def test_loop_runner_uses_runner_namespace_discovery_patch_not_real_cli(self) -> None:
        with temp_git_repo() as repo:
            spec = repo_spec()

            class NoOpCodex:
                def __init__(self, _exe, _repo_path, _runner, _evidence_dir=None, **_kwargs):
                    pass

                def login_status(self):
                    return ProcessResult(("codex",), 0, "Logged in using ChatGPT", "")

                def exec(self, *_args, **_kwargs):
                    return writer_turn()

            with patched_writer_runtime(repo, NoOpCodex), mock.patch(
                "scripts.agent_loop.codex_client.discover_codex",
                side_effect=AssertionError("real Codex discovery must not run in unit tests"),
            ), mock.patch("scripts.agent_loop.runner.review_diff", return_value=([], "")):
                state = LoopRunner(repo, spec, "hash", process_runner=FakeProcessRunner([ProcessResult(("check",), 0, "ok", "")]), run_id="isolation").run()

            self.assertEqual("needs_input", state.status)
            self.assertIn("writer produced no changes", "\n".join(state.errors))

    def test_writer_and_reviewer_receive_exact_git_safe_directory_environment(self) -> None:
        with temp_git_repo() as repo:
            spec = repo_spec()
            captured_envs: list[dict[str, str] | None] = []

            class EnvCapturingCodex:
                def __init__(self, _exe, repo_path, _runner, _evidence_dir=None, **kwargs):
                    self.repo = repo_path
                    captured_envs.append(dict(kwargs.get("env") or {}))

                def login_status(self):
                    return ProcessResult(("codex",), 0, "Logged in using ChatGPT", "")

                def exec(self, *_args, **_kwargs):
                    (self.repo / "allowed.txt").write_text("done", encoding="utf-8")
                    return writer_turn(changed_paths_claimed=["allowed.txt"])

            with patched_writer_runtime(repo, EnvCapturingCodex), mock.patch("scripts.agent_loop.runner.review_diff", return_value=([], "")):
                state = LoopRunner(repo, spec, "hash", process_runner=FakeProcessRunner([ProcessResult(("check",), 0, "ok", "")]), run_id="env").run()

            self.assertEqual("passed", state.status)
            self.assertGreaterEqual(len(captured_envs), 2)
            expected = git_safe_directory_value(repo)
            for env in captured_envs:
                self.assertIsNotNone(env)
                self.assertEqual("1", env["GIT_CONFIG_COUNT"])
                self.assertEqual("safe.directory", env["GIT_CONFIG_KEY_0"])
                self.assertEqual(expected, env["GIT_CONFIG_VALUE_0"])

    def test_invalid_git_config_environment_fails_closed_before_launch(self) -> None:
        with temp_git_repo() as repo:
            spec = repo_spec()
            base_sha = git(repo, "rev-parse", "origin/main").stdout.strip()

            class ShouldNotLaunchCodex:
                def __init__(self, *_args, **_kwargs):
                    raise AssertionError("CodexClient must not be constructed when GIT_CONFIG_* is invalid")

            with mock.patch.dict(os.environ, {"GIT_CONFIG_COUNT": "abc"}, clear=False), mock.patch(
                "scripts.agent_loop.runner.ensure_clean",
                return_value=None,
            ), mock.patch(
                "scripts.agent_loop.runner.rev_parse",
                return_value=base_sha,
            ), mock.patch(
                "scripts.agent_loop.runner.current_branch",
                return_value="main",
            ), mock.patch(
                "scripts.agent_loop.runner.discover_codex",
                return_value=CodexDiscovery(True, "codex", "test", "codex-cli test", ()),
            ), mock.patch("scripts.agent_loop.runner.CodexClient", ShouldNotLaunchCodex):
                with self.assertRaisesRegex(RuntimeError, "codex.invalid_git_config_environment"):
                    LoopRunner(repo, spec, "hash", process_runner=FakeProcessRunner(), run_id="badenv").validate_preflight()

    def test_agents_directory_new_empty_is_ephemeral_and_cleaned(self) -> None:
        with temp_git_repo() as repo:
            spec = repo_spec()

            class AgentsCreatingCodex:
                def __init__(self, _exe, repo_path, _runner, _evidence_dir=None, **_kwargs):
                    self.repo = repo_path

                def login_status(self):
                    return ProcessResult(("codex",), 0, "Logged in using ChatGPT", "")

                def exec(self, *_args, **_kwargs):
                    (self.repo / ".agents").mkdir(exist_ok=True)
                    (self.repo / "allowed.txt").write_text("done", encoding="utf-8")
                    return writer_turn(changed_paths_claimed=["allowed.txt"])

            with patched_writer_runtime(repo, AgentsCreatingCodex), mock.patch("scripts.agent_loop.runner.review_diff", return_value=([], "")):
                state = LoopRunner(repo, spec, "hash", process_runner=FakeProcessRunner([ProcessResult(("check",), 0, "ok", "")]), run_id="agents-clean").run()

            self.assertEqual("passed", state.status)
            self.assertFalse((repo / ".agents").exists())
            self.assertTrue(state.agents_runtime["created_by_run"])
            self.assertTrue(state.agents_runtime["removed"])
            self.assertEqual("ephemeral_runtime_artifact_removed", state.agents_runtime["classification"])
            persisted = json.loads((repo / ".agent-loop" / "runs" / "agents-clean" / "state.json").read_text(encoding="utf-8"))
            self.assertTrue(persisted["agents_runtime"]["removed"])
            self.assertFalse(persisted["agents_runtime"]["cleanup_pending"])

    def test_agents_directory_new_nonempty_is_scope_violation(self) -> None:
        with temp_git_repo() as repo:
            spec = repo_spec()

            class NonEmptyAgentsCodex:
                def __init__(self, _exe, repo_path, _runner, _evidence_dir=None, **_kwargs):
                    self.repo = repo_path

                def login_status(self):
                    return ProcessResult(("codex",), 0, "Logged in using ChatGPT", "")

                def exec(self, *_args, **_kwargs):
                    agents = self.repo / ".agents"
                    agents.mkdir(exist_ok=True)
                    (agents / "unexpected.txt").write_text("x", encoding="utf-8")
                    return writer_turn()

            with patched_writer_runtime(repo, NonEmptyAgentsCodex):
                state = LoopRunner(repo, spec, "hash", process_runner=FakeProcessRunner(), run_id="agents-bad").run()

            self.assertEqual("scope_violation", state.status)
            self.assertIn("runtime_artifact_violation", "\n".join(state.errors))
            self.assertTrue((repo / ".agents").exists())
            self.assertTrue((repo / ".agents" / "unexpected.txt").exists())

    def test_agents_directory_cleanup_runs_on_needs_input_budget_exhausted_and_tool_failure(self) -> None:
        scenarios: list[tuple[str, str, list[ProcessResult], CodexTurnResult]] = [
            (
                "needs-input",
                "needs_input",
                [],
                writer_turn(status="needs_input", needs_input=True, blocker="decision"),
            ),
            (
                "budget",
                "budget_exhausted",
                [ProcessResult(("check",), 1, "", "fail"), ProcessResult(("check",), 1, "", "fail")],
                writer_turn(),
            ),
            (
                "tool",
                "tool_failure",
                [],
                CodexTurnResult(False, 1, "", "", SESSION_ID, Usage(), "", ("boom",)),
            ),
        ]
        for run_id, expected_status, checks, turn_result in scenarios:
            with self.subTest(status=expected_status):
                with temp_git_repo() as repo:
                    spec = repo_spec()

                    class AgentsCodex:
                        def __init__(self, _exe, repo_path, _runner, _evidence_dir=None, **_kwargs):
                            self.repo = repo_path

                        def login_status(self):
                            return ProcessResult(("codex",), 0, "Logged in using ChatGPT", "")

                        def exec(self, *_args, **_kwargs):
                            (self.repo / ".agents").mkdir(exist_ok=True)
                            return turn_result

                        def resume(self, *_args, **_kwargs):
                            (self.repo / ".agents").mkdir(exist_ok=True)
                            return turn_result

                    with patched_writer_runtime(repo, AgentsCodex):
                        state = LoopRunner(repo, spec, "hash", process_runner=FakeProcessRunner(checks), run_id=run_id).run()

                    self.assertEqual(expected_status, state.status)
                    self.assertFalse((repo / ".agents").exists())
                    self.assertTrue(state.agents_runtime["removed"])

    def test_agents_cleanup_failure_is_persisted_and_downgrades_passed_state(self) -> None:
        with temp_git_repo() as repo:
            spec = repo_spec()

            class AgentsCreatingCodex:
                def __init__(self, _exe, repo_path, _runner, _evidence_dir=None, **_kwargs):
                    self.repo = repo_path

                def login_status(self):
                    return ProcessResult(("codex",), 0, "Logged in using ChatGPT", "")

                def exec(self, *_args, **_kwargs):
                    (self.repo / ".agents").mkdir(exist_ok=True)
                    (self.repo / "allowed.txt").write_text("done", encoding="utf-8")
                    return writer_turn(changed_paths_claimed=["allowed.txt"])

            with patched_writer_runtime(repo, AgentsCreatingCodex), mock.patch("scripts.agent_loop.runner.review_diff", return_value=([], "")), mock.patch(
                "scripts.agent_loop.git_scope.os.rmdir",
                side_effect=OSError("locked"),
            ):
                state = LoopRunner(repo, spec, "hash", process_runner=FakeProcessRunner([ProcessResult(("check",), 0, "ok", "")]), run_id="agents-fail").run()

            self.assertEqual("tool_failure", state.status)
            self.assertIn("ephemeral_cleanup_failed", "\n".join(state.errors))
            persisted = json.loads((repo / ".agent-loop" / "runs" / "agents-fail" / "state.json").read_text(encoding="utf-8"))
            self.assertTrue(persisted["agents_runtime"]["cleanup_pending"])
            self.assertFalse(persisted["agents_runtime"]["removed"])

    def test_parse_writer_result_uses_git_truth_over_claims(self) -> None:
        parsed = parse_writer_result(json.dumps({"status": "implemented", "summary": "x", "changed_paths_claimed": ["a\\b.txt"], "needs_input": False, "blocker": None}))
        self.assertEqual(["a/b.txt"], parsed["changed_paths_claimed"])
        self.assertFalse(parsed["needs_input"])

    def test_probe_regression_stream_records_failed_tools_despite_passed_final_response(self) -> None:
        stream = "\n".join(
            [
                json.dumps({"type": "thread.started", "thread_id": SESSION_ID}),
                json.dumps({"type": "turn.started"}),
                json.dumps({"type": "item.completed", "item": {"id": "item_1", "type": "file_change", "status": "failed", "changes": [{"path": "probe.txt"}]}}),
                json.dumps({"type": "item.completed", "item": {"id": "item_2", "type": "command_execution", "status": "failed", "exit_code": -1, "command": "pwsh -Command Set-Content C:/Users/example/probe.txt"}}),
                json.dumps({"type": "item.completed", "item": {"id": "item_3", "type": "mcp_tool_call", "status": "failed", "server": "node_repl", "tool": "js", "error": {"message": "missing field sandboxPolicy"}}}),
                json.dumps({"type": "turn.completed"}),
                json.dumps({"type": "final_message", "message": json.dumps({"status": "passed", "message": "writer-smoke"})}),
            ]
        ) + "\n"

        parsed = parse_jsonl(stream)

        self.assertEqual([], parsed["errors"])
        self.assertEqual({"status": "passed", "message": "writer-smoke"}, json.loads(parsed["final_message"]))
        self.assertEqual(["file_change", "command_execution", "mcp_tool_call"], [failure["item_type"] for failure in parsed["tool_failures"]])
        self.assertNotIn("C:/Users/example", parsed["tool_failures"][1]["command"])
        self.assertIn("<path>", parsed["tool_failures"][1]["command"])


def writer_turn(
    *,
    status: str = "implemented",
    changed_paths_claimed: list[str] | None = None,
    needs_input: bool = False,
    blocker: str | None = None,
    tool_failures: list[dict] | None = None,
) -> CodexTurnResult:
    message = json.dumps(
        {
            "status": status,
            "summary": "done",
            "changed_paths_claimed": changed_paths_claimed or [],
            "needs_input": needs_input,
            "blocker": blocker,
        }
    )
    return CodexTurnResult(True, 0, "", "", SESSION_ID, Usage(), message, (), {"thread.started": 1, "turn.completed": 1}, {"agent_message": 1}, tuple(tool_failures or []))


def jsonl_stream() -> str:
    return "\n".join(
        [
            json.dumps({"type": "thread.started", "thread_id": SESSION_ID}),
            json.dumps({"type": "turn.completed"}),
            json.dumps({"type": "final_message", "message": json.dumps({"status": "implemented", "summary": "ok", "changed_paths_claimed": [], "needs_input": False, "blocker": None})}),
        ]
    ) + "\n"


@contextmanager
def patched_writer_runtime(repo: Path, fake_class):
    patched_env = {"CODEX_EXECUTABLE": ""}
    patched_env.update({key: value for key, value in os.environ.items() if not key.startswith("GIT_CONFIG_")})
    with ExitStack() as stack:
        stack.enter_context(mock.patch.dict(os.environ, patched_env, clear=True))
        stack.enter_context(
            mock.patch(
                "scripts.agent_loop.runner.discover_codex",
                return_value=CodexDiscovery(True, "codex", "test", "codex-cli test", ()),
            )
        )
        stack.enter_context(mock.patch("scripts.agent_loop.runner.CodexClient", fake_class))
        yield


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


def repo_spec():
    data = valid_spec()
    data["allowed_paths"] = ["allowed.txt"]
    data["protected_paths"] = ["protected/"]
    data["base_ref"] = "origin/main"
    data["branch"] = "feat/test"
    data["checks"] = [{"id": "focused", "argv": ["python", "--version"], "timeout_seconds": 5}]
    data["budgets"]["max_iterations"] = 2
    data["budgets"]["max_repeated_failure"] = 2
    return parse_task_spec(data)


if __name__ == "__main__":
    unittest.main()
