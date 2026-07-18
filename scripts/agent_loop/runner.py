from __future__ import annotations

import json
import os
import subprocess
import time
import uuid
from pathlib import Path
from typing import Any

from .codex_client import CodexClient, discover_codex
from .evidence import EvidenceStore
from .git_guard import ScopeAudit, audit_scope, create_branch, current_branch, ensure_clean, fingerprint_worktree, rev_parse
from .git_scope import (
    build_git_scoped_environment,
    build_runtime_temp_environment,
    cleanup_agents_directory,
    cleanup_runtime_temp_directory,
    initial_agents_runtime,
    initial_runtime_temp_state,
    inspect_agents_directory,
    reconcile_agents_runtime,
)
from .io_utils import atomic_write_json
from .models import CheckResult, Finding, LoopState, TaskSpec
from .process_runner import ProcessRunner
from .publisher import PublicationError, Publisher
from .reviewer import ReviewError, review_diff, write_review_schema
from .task_spec import expand_argv


WRITER_SCHEMA: dict[str, Any] = {
    "type": "object",
    "additionalProperties": False,
    "required": ["status", "summary", "changed_paths_claimed", "needs_input", "blocker"],
    "properties": {
        "status": {"enum": ["implemented", "needs_input", "blocked"]},
        "summary": {"type": "string"},
        "changed_paths_claimed": {"type": "array", "items": {"type": "string"}},
        "needs_input": {"type": "boolean"},
        "blocker": {"type": ["string", "null"]},
    },
}


class LoopRunner:
    def __init__(
        self,
        repo: Path,
        spec: TaskSpec,
        task_hash: str,
        *,
        codex_executable: str | None = None,
        process_runner: ProcessRunner | None = None,
        run_id: str | None = None,
        clock: callable = time.time,
    ) -> None:
        self.repo = repo
        self.spec = spec
        self.task_hash = task_hash
        self.process_runner = process_runner or ProcessRunner()
        self.codex_executable = codex_executable
        self.run_id = run_id or uuid.uuid4().hex
        self.clock = clock
        self.codex_environment: dict[str, str] | None = None
        self.codex_git_env_metadata: dict[str, Any] = {}

    def validate_preflight(self) -> tuple[str, str]:
        try:
            git_environment = build_git_scoped_environment(os.environ, self.repo)
        except ValueError as exc:
            raise RuntimeError(str(exc)) from exc
        self.codex_environment = git_environment.environment
        self.codex_git_env_metadata = git_environment.metadata()
        ensure_clean(self.repo)
        base_sha = rev_parse(self.repo, self.spec.base_ref)
        if current_branch(self.repo) in {"main", "master"}:
            pass
        discovery = discover_codex(self.codex_executable)
        if not discovery.ok or not discovery.executable:
            raise RuntimeError("Codex CLI discovery failed: " + "; ".join(discovery.errors))
        client = CodexClient(
            discovery.executable,
            self.repo,
            self.process_runner,
            env=self.codex_environment,
            git_env_metadata=self.codex_git_env_metadata,
        )
        login = client.login_status()
        if login.exit_code != 0 or "ChatGPT" not in (login.stdout + login.stderr):
            raise RuntimeError("Codex login status did not confirm ChatGPT authentication")
        return base_sha, discovery.executable

    def run(self, *, publish: bool = False) -> LoopState:
        start = self.clock()
        base_sha, executable = self.validate_preflight()
        create_branch(self.repo, self.spec.branch, base_sha)
        state = LoopState(self.run_id, self.spec.task_id, self.task_hash, self.spec.base_ref, base_sha, self.spec.branch, "branch_ready")
        state.agents_runtime = initial_agents_runtime(self.repo)
        state.runtime_temp = initial_runtime_temp_state(self.repo / ".agent-loop" / "runs" / self.run_id)
        evidence = EvidenceStore(self.repo, self.run_id, self.clock)
        evidence.write_state(state, start_time=start)
        env, metadata = self.build_turn_environment(state, evidence)
        client = CodexClient(
            executable,
            self.repo,
            self.process_runner,
            evidence.run_dir,
            env=env,
            git_env_metadata=metadata,
        )
        return self._continue(state, client, evidence, start, base_sha, publish=publish, resume_first=False)

    def resume(self, state: LoopState, *, publish: bool = False) -> LoopState:
        start = self.clock()
        try:
            git_environment = build_git_scoped_environment(os.environ, self.repo)
        except ValueError as exc:
            raise RuntimeError(str(exc)) from exc
        self.codex_environment = git_environment.environment
        self.codex_git_env_metadata = git_environment.metadata()
        base_sha = rev_parse(self.repo, self.spec.base_ref)
        if base_sha != state.base_sha:
            raise RuntimeError("base SHA changed")
        if current_branch(self.repo) != self.spec.branch:
            raise RuntimeError("current branch does not match checkpoint")
        discovery = discover_codex(self.codex_executable)
        if not discovery.ok or not discovery.executable:
            raise RuntimeError("Codex CLI discovery failed: " + "; ".join(discovery.errors))
        login_client = CodexClient(
            discovery.executable,
            self.repo,
            self.process_runner,
            env=self.codex_environment,
            git_env_metadata=self.codex_git_env_metadata,
        )
        login = login_client.login_status()
        if login.exit_code != 0 or "ChatGPT" not in (login.stdout + login.stderr):
            raise RuntimeError("Codex login status did not confirm ChatGPT authentication")
        evidence = EvidenceStore(self.repo, self.run_id, self.clock)
        if not state.agents_runtime:
            state.agents_runtime = initial_agents_runtime(self.repo)
        if not state.runtime_temp:
            state.runtime_temp = initial_runtime_temp_state(evidence.run_dir)
        env, metadata = self.build_turn_environment(state, evidence)
        client = CodexClient(
            discovery.executable,
            self.repo,
            self.process_runner,
            evidence.run_dir,
            env=env,
            git_env_metadata=metadata,
        )
        return self._continue(state, client, evidence, start, base_sha, publish=publish, resume_first=bool(state.writer_session_id))

    def build_turn_environment(self, state: LoopState, evidence: EvidenceStore) -> tuple[dict[str, str], dict[str, Any]]:
        base_env = self.codex_environment if self.codex_environment is not None else build_git_scoped_environment(os.environ, self.repo).environment
        evidence.run_dir.mkdir(parents=True, exist_ok=True)
        try:
            runtime = build_runtime_temp_environment(base_env, self.repo, evidence.run_dir, state.runtime_temp)
        except ValueError as exc:
            raise RuntimeError(str(exc)) from exc
        state.runtime_temp = runtime.runtime_state
        metadata = dict(self.codex_git_env_metadata)
        metadata.update(runtime.metadata())
        return runtime.environment, metadata

    def _continue(
        self,
        state: LoopState,
        client: CodexClient,
        evidence: EvidenceStore,
        start: float,
        base_sha: str,
        *,
        publish: bool,
        resume_first: bool,
    ) -> LoopState:
        reviewer_schema = evidence.run_dir / "review.schema.json"
        write_review_schema(reviewer_schema)
        writer_schema = evidence.run_dir / "writer.schema.json"
        atomic_write_json(writer_schema, WRITER_SCHEMA)
        last_signature: str | None = None
        repeat_count = 0

        try:
            while True:
                if state.iteration >= self.spec.budgets.max_iterations:
                    state.status = "budget_exhausted"
                    state.errors.append("max_iterations reached")
                    break
                if state.codex_turns >= self.spec.budgets.max_codex_turns:
                    state.status = "budget_exhausted"
                    state.errors.append("max_codex_turns reached")
                    break
                if self.clock() - start > self.spec.budgets.max_wall_minutes * 60:
                    state.status = "budget_exhausted"
                    state.errors.append("max_wall_minutes reached")
                    break
                state.iteration += 1
                state.status = "implementing"
                evidence.write_state(state, start_time=start)
                before_fingerprint, before_error = self.safe_fingerprint(base_sha)
                if before_error:
                    state.status = "tool_failure"
                    state.errors.append(before_error)
                    break
                prompt = self.writer_prompt(state)
                turn_index = state.codex_turns + 1
                resumed = bool(state.writer_session_id)
                if resumed:
                    turn = client.resume(state.writer_session_id, prompt, sandbox="workspace-write", output_schema=writer_schema, timeout_seconds=600)
                    resume_first = False
                else:
                    turn = client.exec(prompt, sandbox="workspace-write", output_schema=writer_schema, timeout_seconds=600)
                state.codex_turns += 1
                state.usage.add(turn.usage.to_json())
                if turn.session_id:
                    state.writer_session_id = turn.session_id
                writer_result = parse_writer_result(turn.final_message)
                state.agents_runtime, agents_error = reconcile_agents_runtime(state.agents_runtime, inspect_agents_directory(self.repo))
                audit, audit_error = self.safe_audit(base_sha)
                if audit is not None:
                    state.changed_files = list(audit.changed_files)
                    state.fingerprint = audit.fingerprint
                    progress = before_fingerprint != audit.fingerprint
                else:
                    state.changed_files = []
                    state.fingerprint = before_fingerprint
                    progress = False
                claimed_paths = writer_result["changed_paths_claimed"]
                tool_failures = list(turn.tool_failures)
                self.observe_post_turn_budgets(state)
                evidence.write_turn_evidence(
                    turn_index,
                    "writer",
                    {
                        "sandbox": "workspace-write",
                        "cwd": "<repo>",
                        "resumed": resumed,
                        "exit_code": turn.exit_code,
                        "event_counts": turn.event_counts or {},
                        "item_type_counts": turn.item_type_counts or {},
                        "changed_paths_real": list(state.changed_files),
                        "changed_paths_claimed": claimed_paths,
                        "tool_failures": tool_failures,
                        "turn_errors": list(turn.errors),
                        "progress": progress,
                        "fingerprint_before": before_fingerprint,
                        "fingerprint_after": state.fingerprint,
                        "claim_discrepancy": claimed_paths != list(state.changed_files),
                        "needs_input": writer_result["needs_input"],
                        "blocker": writer_result["blocker"],
                        "failed_checks_delivered": [check_summary(check) for check in state.checks if check.status != "PASS"],
                        "agents_runtime": dict(state.agents_runtime),
                        "runtime_temp": dict(state.runtime_temp),
                        "budget_observations": dict(state.budget_observations),
                    },
                )
                if audit_error:
                    state.status = "tool_failure"
                    state.errors.append(audit_error)
                    break
                if writer_result["needs_input"]:
                    state.status = "needs_input"
                    state.errors.append(writer_result["blocker"] or "writer requested human input")
                    break
                if agents_error:
                    state.status = "scope_violation"
                    state.errors.append(f"runtime_artifact_violation: {agents_error}")
                    break
                if not audit.ok:
                    state.status = "scope_violation"
                    state.errors.extend(audit.violations)
                    break
                if not turn.ok:
                    state.status = classify_codex_error(turn.errors)
                    state.errors.extend(turn.errors)
                    break
                if tool_failures:
                    if not progress:
                        state.status = "tool_failure"
                        state.errors.append("writer_no_changes: writer tool failures produced no working-tree changes")
                        for failure in tool_failures[:5]:
                            state.errors.append(f"writer_tool_failure: {format_tool_failure(failure)}")
                        break
                    if not tool_failures_are_recoverable(tool_failures):
                        state.status = "tool_failure"
                        for failure in tool_failures[:5]:
                            state.errors.append(f"writer_tool_failure: {format_tool_failure(failure)}")
                        break
                state.status = "checking"
                evidence.write_state(state, start_time=start)
                state.checks = self.run_checks()
                audit, audit_error = self.safe_audit(base_sha)
                if audit is not None:
                    state.changed_files = list(audit.changed_files)
                    state.fingerprint = audit.fingerprint
                state.agents_runtime, agents_error = reconcile_agents_runtime(state.agents_runtime, inspect_agents_directory(self.repo))
                if audit_error:
                    state.status = "tool_failure"
                    state.errors.append(audit_error)
                    break
                if agents_error:
                    state.status = "scope_violation"
                    state.errors.append(f"runtime_artifact_violation: {agents_error}")
                    break
                if not audit.ok:
                    state.status = "scope_violation"
                    state.errors.extend(audit.violations)
                    break
                failed = [check for check in state.checks if check.status != "PASS"]
                signature = self.failure_signature(state, failed)
                if failed:
                    if not progress:
                        state.errors.append(f"writer_no_changes: iteration {state.iteration} produced no working-tree changes")
                    if budget_turn_limit_hit(state):
                        state.status = "budget_exhausted"
                        state.errors.append("post-turn token budget exceeded before another writer turn could begin")
                        break
                    if signature == last_signature:
                        repeat_count += 1
                    else:
                        repeat_count = 1
                        last_signature = signature
                    if repeat_count >= self.spec.budgets.max_repeated_failure:
                        state.status = "budget_exhausted"
                        state.errors.append("same failure repeated without progress")
                        break
                    continue
                if budget_turn_limit_hit(state) and self.spec.review.enabled:
                    state.status = "budget_exhausted"
                    state.errors.append("post-turn token budget exceeded before reviewer turn could begin")
                    break
                if self.spec.review.enabled:
                    if state.review_turns >= self.spec.budgets.max_review_turns:
                        state.status = "budget_exhausted"
                        state.errors.append("max_review_turns reached")
                        break
                    try:
                        state.status = "reviewing"
                        evidence.write_state(state, start_time=start)
                        findings, _review_session = review_diff(client, self.spec, base_sha, reviewer_schema, 600)
                        state.review_turns += 1
                        state.findings = findings
                    except ReviewError as exc:
                        state.status = "tool_failure"
                        state.errors.append(str(exc))
                        break
                    state.agents_runtime, agents_error = reconcile_agents_runtime(state.agents_runtime, inspect_agents_directory(self.repo))
                    if agents_error:
                        state.status = "scope_violation"
                        state.errors.append(f"runtime_artifact_violation: {agents_error}")
                        break
                    if any(not finding.in_scope for finding in findings):
                        state.status = "needs_input"
                        state.errors.append("review finding is outside scope")
                        break
                    blockers = [finding for finding in findings if finding.severity in self.spec.review.blocking_severities]
                    if blockers:
                        continue
                if not state.changed_files:
                    state.status = "needs_input"
                    state.errors.append("writer produced no changes; human confirmation is required before treating task as already satisfied")
                    break
                state.status = "passed"
                break
        finally:
            state.runtime_temp, runtime_temp_error = cleanup_runtime_temp_directory(state.runtime_temp, self.repo, evidence.run_dir)
            if runtime_temp_error:
                state.errors.append(runtime_temp_error)
                if state.status == "passed":
                    state.status = "tool_failure"
            state.agents_runtime, cleanup_error = cleanup_agents_directory(state.agents_runtime, self.repo)
            if cleanup_error:
                state.errors.append(cleanup_error)
                if state.status == "passed":
                    state.status = "tool_failure"

        if state.status == "passed" and publish:
            try:
                audit = audit_scope(self.repo, self.spec, base_sha=base_sha, expected_branch=self.spec.branch)
                if not audit.ok:
                    state.status = "scope_violation"
                    state.errors.extend(audit.violations)
                    evidence.write_state(state, start_time=start)
                    return state
                state.changed_files = list(audit.changed_files)
                state.pr_url = Publisher(self.repo, self.process_runner).publish(self.spec, state, publish_flag=True)
            except PublicationError as exc:
                state.status = "publication_failed"
                state.errors.append(str(exc))
        evidence.write_state(state, start_time=start)
        return state

    def safe_audit(self, base_sha: str) -> tuple[ScopeAudit | None, str | None]:
        try:
            return audit_scope(self.repo, self.spec, base_sha=base_sha, expected_branch=self.spec.branch), None
        except (OSError, subprocess.SubprocessError, RuntimeError) as exc:
            return None, f"git_audit_failed: {type(exc).__name__}: {exc}"

    def safe_fingerprint(self, base_sha: str) -> tuple[str | None, str | None]:
        try:
            return fingerprint_worktree(self.repo, base_sha=base_sha), None
        except (OSError, subprocess.SubprocessError, RuntimeError) as exc:
            return None, f"git_audit_failed: {type(exc).__name__}: {exc}"

    def observe_post_turn_budgets(self, state: LoopState) -> None:
        observations = dict(state.budget_observations or {})
        observations["usage_telemetry_is_post_turn"] = True
        violations = list(observations.get("violations") or [])
        for key, limit, observed in (
            ("input_tokens", self.spec.budgets.max_input_tokens, state.usage.input_tokens),
            ("output_tokens", self.spec.budgets.max_output_tokens, state.usage.output_tokens),
        ):
            if limit is None or observed is None or observed <= limit:
                continue
            violation = {"kind": key, "limit": limit, "observed": observed, "observed_post_turn": True}
            if violation not in violations:
                violations.append(violation)
                state.errors.append(f"budget_observed_post_turn: {key} observed {observed} exceeded limit {limit}")
        observations["violations"] = violations
        state.budget_observations = observations

    def run_checks(self) -> list[CheckResult]:
        results: list[CheckResult] = []
        for check in self.spec.checks:
            argv = expand_argv(check.argv, self.spec, self.repo)
            result = self.process_runner.run(argv, self.repo, check.timeout_seconds)
            results.append(
                CheckResult(
                    check.id,
                    tuple(argv),
                    "PASS" if result.exit_code == 0 else "FAIL",
                    result.exit_code,
                    result.stdout,
                    result.stderr,
                )
            )
        return results

    def writer_prompt(self, state: LoopState) -> str:
        findings = "\n".join(f"- {f.severity}: {f.title}: {f.suggested_fix or f.evidence}" for f in state.findings) or "none"
        failed = [check for check in state.checks if check.status != "PASS"]
        failed_text = "\n".join(format_check_failure(check) for check in failed) or "none"
        diagnostics = "\n".join(f"- {error}" for error in state.errors[-10:]) or "none"
        real_changes = ", ".join(state.changed_files) if state.changed_files else "none"
        remaining_iterations = max(0, self.spec.budgets.max_iterations - state.iteration)
        remaining_turns = max(0, self.spec.budgets.max_codex_turns - state.codex_turns)
        no_changes_note = ""
        if state.codex_turns > 0 and not state.changed_files:
            no_changes_note = "\nNo working-tree changes were detected after the previous writer turn. Correct that now by editing the allowed files."
        return (
            "You are the sole implementation writer for this bounded task. Inspect the repository and implement the requested change now. "
            "You are authorized and expected to create, modify, rename, or delete only the exact allowed paths. "
            "Do not merely propose a plan, summarize, or describe hypothetical edits.\n"
            "The supervisor controls Git publication. You must edit the working tree, but you must not commit, push, create a PR, merge, mark ready, or modify the task/supervisor.\n"
            "Official supervisor checks run after your turn outside the sandbox. Do not run the full unittest discovery suite, do not run scripts/run_checks.py, do not run repository wrappers, and do not repeat the supervisor checks inside Codex.\n"
            "Do not run ACL, ownership, TEMP, sandbox, or security diagnostics. On Windows the shell is PowerShell, not Bash; do not use POSIX heredocs.\n"
            f"Task ID: {self.spec.task_id}\nGoal: {self.spec.goal}\nDone when: {'; '.join(self.spec.done_when)}\n"
            f"Base SHA: {state.base_sha}\nBranch: {self.spec.branch}\nAllowed paths: {', '.join(self.spec.allowed_paths)}\n"
            f"Protected paths: {', '.join(self.spec.protected_paths)}\n"
            f"Failed checks from the previous turn:\n{failed_text}\n"
            f"Supervisor diagnostics from previous turns:\n{diagnostics}\n"
            f"Pending review findings:\n{findings}\n"
            f"Real changed files currently observed by Git: {real_changes}{no_changes_note}\n"
            f"Remaining budget: iterations={remaining_iterations}, codex_turns={remaining_turns}, review_turns={max(0, self.spec.budgets.max_review_turns - state.review_turns)}.\n"
            "Return final JSON matching the supplied schema after making edits. changed_paths_claimed must list paths you believe you changed; Git remains the authority."
        )

    def failure_signature(self, state: LoopState, failed: list[CheckResult]) -> str:
        parts = [state.fingerprint or ""]
        parts.extend(f"{check.id}:{check.exit_code}:{check.stderr[-200:]}" for check in failed)
        parts.extend(f"{finding.id}:{finding.severity}:{finding.title}" for finding in state.findings)
        return "|".join(parts)


def classify_codex_error(errors: tuple[str, ...]) -> str:
    text = "\n".join(errors).lower()
    if "usage limit" in text:
        return "usage_limit_reached"
    if "rate limit" in text:
        return "tool_failure"
    if any(
        marker in text
        for marker in (
            "authentication",
            "not logged",
            "login required",
            "login status did not confirm",
            "api key",
            "unauthenticated",
            "authorization failed",
        )
    ):
        return "tool_failure"
    return "tool_failure"


def parse_writer_result(final_message: str) -> dict[str, Any]:
    fallback = {"status": "", "summary": "", "changed_paths_claimed": [], "needs_input": False, "blocker": None}
    try:
        data = json.loads(final_message) if final_message else {}
    except json.JSONDecodeError:
        return fallback
    if not isinstance(data, dict):
        return fallback
    claimed = data.get("changed_paths_claimed")
    if not isinstance(claimed, list):
        claimed_paths: list[str] = []
    else:
        claimed_paths = [item.replace("\\", "/") for item in claimed if isinstance(item, str)]
    blocker = data.get("blocker")
    return {
        "status": data.get("status") if isinstance(data.get("status"), str) else "",
        "summary": data.get("summary") if isinstance(data.get("summary"), str) else "",
        "changed_paths_claimed": claimed_paths,
        "needs_input": bool(data.get("needs_input")),
        "blocker": blocker if isinstance(blocker, str) else None,
    }


def format_check_failure(check: CheckResult) -> str:
    return (
        f"- {check.id}: status={check.status} exit_code={check.exit_code}\n"
        f"  stdout_tail={tail(check.stdout)}\n"
        f"  stderr_tail={tail(check.stderr)}"
    )


def check_summary(check: CheckResult) -> dict[str, Any]:
    return {
        "id": check.id,
        "status": check.status,
        "exit_code": check.exit_code,
        "stdout_tail": tail(check.stdout),
        "stderr_tail": tail(check.stderr),
    }


def format_tool_failure(failure: dict[str, Any]) -> str:
    parts = [
        f"item_type={failure.get('item_type')}",
        f"event_index={failure.get('event_index')}",
    ]
    if "exit_code" in failure:
        parts.append(f"exit_code={failure['exit_code']}")
    if failure.get("tool"):
        parts.append(f"tool={failure['tool']}")
    if failure.get("command"):
        parts.append(f"command={failure['command']}")
    if failure.get("error"):
        parts.append(f"error={failure['error']}")
    return " ".join(parts)


def tail(text: str, limit: int = 2000) -> str:
    if len(text) <= limit:
        return text
    return text[-limit:]


def tool_failures_are_recoverable(tool_failures: list[dict[str, Any]]) -> bool:
    return bool(tool_failures) and all(failure.get("item_type") == "command_execution" for failure in tool_failures)


def budget_turn_limit_hit(state: LoopState) -> bool:
    return bool((state.budget_observations or {}).get("violations"))
