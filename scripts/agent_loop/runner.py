from __future__ import annotations

import json
import os
import time
import uuid
from pathlib import Path
from typing import Any

from .codex_client import CodexClient, discover_codex
from .evidence import EvidenceStore
from .git_guard import audit_scope, create_branch, current_branch, ensure_clean, fingerprint_worktree, rev_parse
from .git_scope import build_git_scoped_environment, cleanup_agents_directory, initial_agents_runtime, inspect_agents_directory, reconcile_agents_runtime
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
        evidence = EvidenceStore(self.repo, self.run_id, self.clock)
        evidence.write_state(state, start_time=start)
        client = CodexClient(
            executable,
            self.repo,
            self.process_runner,
            evidence.run_dir,
            env=self.codex_environment,
            git_env_metadata=self.codex_git_env_metadata,
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
        client = CodexClient(
            discovery.executable,
            self.repo,
            self.process_runner,
            evidence.run_dir,
            env=self.codex_environment,
            git_env_metadata=self.codex_git_env_metadata,
        )
        return self._continue(state, client, evidence, start, base_sha, publish=publish, resume_first=bool(state.writer_session_id))

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
                before_fingerprint = fingerprint_worktree(self.repo, base_sha=base_sha)
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
                if not turn.ok:
                    state.status = classify_codex_error(turn.errors)
                    state.errors.extend(turn.errors)
                    break
                if agents_error:
                    state.status = "scope_violation"
                    state.errors.append(f"runtime_artifact_violation: {agents_error}")
                    break
                audit = audit_scope(self.repo, self.spec, base_sha=base_sha, expected_branch=self.spec.branch)
                state.changed_files = list(audit.changed_files)
                state.fingerprint = audit.fingerprint
                progress = before_fingerprint != audit.fingerprint
                claimed_paths = writer_result["changed_paths_claimed"]
                tool_failures = list(turn.tool_failures)
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
                        "changed_paths_real": list(audit.changed_files),
                        "changed_paths_claimed": claimed_paths,
                        "tool_failures": tool_failures,
                        "progress": progress,
                        "fingerprint_before": before_fingerprint,
                        "fingerprint_after": audit.fingerprint,
                        "claim_discrepancy": bool(claimed_paths and not audit.changed_files),
                        "needs_input": writer_result["needs_input"],
                        "blocker": writer_result["blocker"],
                        "failed_checks_delivered": [check_summary(check) for check in state.checks if check.status != "PASS"],
                        "agents_runtime": dict(state.agents_runtime),
                    },
                )
                if tool_failures and not progress:
                    for failure in tool_failures[:5]:
                        state.errors.append(f"writer_tool_failure: {format_tool_failure(failure)}")
                if writer_result["needs_input"]:
                    state.status = "needs_input"
                    state.errors.append(writer_result["blocker"] or "writer requested human input")
                    break
                if not audit.ok:
                    state.status = "scope_violation"
                    state.errors.extend(audit.violations)
                    break
                state.status = "checking"
                evidence.write_state(state, start_time=start)
                state.checks = self.run_checks()
                audit = audit_scope(self.repo, self.spec, base_sha=base_sha, expected_branch=self.spec.branch)
                state.changed_files = list(audit.changed_files)
                state.fingerprint = audit.fingerprint
                state.agents_runtime, agents_error = reconcile_agents_runtime(state.agents_runtime, inspect_agents_directory(self.repo))
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
        checks = "\n".join(" ".join(check.argv) for check in self.spec.checks)
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
            f"Task ID: {self.spec.task_id}\nGoal: {self.spec.goal}\nDone when: {'; '.join(self.spec.done_when)}\n"
            f"Base SHA: {state.base_sha}\nBranch: {self.spec.branch}\nAllowed paths: {', '.join(self.spec.allowed_paths)}\n"
            f"Protected paths: {', '.join(self.spec.protected_paths)}\nChecks:\n{checks}\n"
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
    if "auth" in text or "rate limit" in text:
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
