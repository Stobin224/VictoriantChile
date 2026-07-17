from __future__ import annotations

import time
import uuid
from pathlib import Path

from .codex_client import CodexClient, discover_codex
from .evidence import EvidenceStore
from .git_guard import audit_scope, create_branch, current_branch, ensure_clean, rev_parse
from .models import CheckResult, Finding, LoopState, TaskSpec
from .process_runner import ProcessRunner
from .publisher import PublicationError, Publisher
from .reviewer import ReviewError, review_diff, write_review_schema
from .task_spec import expand_argv


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

    def validate_preflight(self) -> tuple[str, str]:
        ensure_clean(self.repo)
        base_sha = rev_parse(self.repo, self.spec.base_ref)
        if current_branch(self.repo) in {"main", "master"}:
            pass
        discovery = discover_codex(self.codex_executable)
        if not discovery.ok or not discovery.executable:
            raise RuntimeError("Codex CLI discovery failed: " + "; ".join(discovery.errors))
        client = CodexClient(discovery.executable, self.repo, self.process_runner)
        login = client.login_status()
        if login.exit_code != 0 or "ChatGPT" not in (login.stdout + login.stderr):
            raise RuntimeError("Codex login status did not confirm ChatGPT authentication")
        return base_sha, discovery.executable

    def run(self, *, publish: bool = False) -> LoopState:
        start = self.clock()
        base_sha, executable = self.validate_preflight()
        create_branch(self.repo, self.spec.branch, base_sha)
        state = LoopState(self.run_id, self.spec.task_id, self.task_hash, self.spec.base_ref, base_sha, self.spec.branch, "branch_ready")
        evidence = EvidenceStore(self.repo, self.run_id, self.clock)
        evidence.write_state(state, start_time=start)
        client = CodexClient(executable, self.repo, self.process_runner, evidence.run_dir)
        return self._continue(state, client, evidence, start, base_sha, publish=publish, resume_first=False)

    def resume(self, state: LoopState, *, publish: bool = False) -> LoopState:
        start = self.clock()
        base_sha = rev_parse(self.repo, self.spec.base_ref)
        if base_sha != state.base_sha:
            raise RuntimeError("base SHA changed")
        if current_branch(self.repo) != self.spec.branch:
            raise RuntimeError("current branch does not match checkpoint")
        discovery = discover_codex(self.codex_executable)
        if not discovery.ok or not discovery.executable:
            raise RuntimeError("Codex CLI discovery failed: " + "; ".join(discovery.errors))
        login_client = CodexClient(discovery.executable, self.repo, self.process_runner)
        login = login_client.login_status()
        if login.exit_code != 0 or "ChatGPT" not in (login.stdout + login.stderr):
            raise RuntimeError("Codex login status did not confirm ChatGPT authentication")
        evidence = EvidenceStore(self.repo, self.run_id, self.clock)
        client = CodexClient(discovery.executable, self.repo, self.process_runner, evidence.run_dir)
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
        last_signature: str | None = None
        repeat_count = 0

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
            prompt = self.writer_prompt(state)
            if resume_first and state.writer_session_id:
                turn = client.resume(state.writer_session_id, prompt, output_schema=None, timeout_seconds=600)
                resume_first = False
            else:
                turn = client.exec(prompt, sandbox="workspace-write", output_schema=None, timeout_seconds=600)
            state.codex_turns += 1
            state.usage.add(turn.usage.to_json())
            if turn.session_id:
                state.writer_session_id = turn.session_id
            if not turn.ok:
                state.status = classify_codex_error(turn.errors)
                state.errors.extend(turn.errors)
                break
            audit = audit_scope(self.repo, self.spec, base_sha=base_sha, expected_branch=self.spec.branch)
            state.changed_files = list(audit.changed_files)
            state.fingerprint = audit.fingerprint
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
            if not audit.ok:
                state.status = "scope_violation"
                state.errors.extend(audit.violations)
                break
            failed = [check for check in state.checks if check.status != "PASS"]
            signature = self.failure_signature(state, failed)
            if failed:
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
                if any(not finding.in_scope for finding in findings):
                    state.status = "needs_input"
                    state.errors.append("review finding is outside scope")
                    break
                blockers = [finding for finding in findings if finding.severity in self.spec.review.blocking_severities]
                if blockers:
                    continue
            state.status = "passed"
            break

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
        return (
            f"Task ID: {self.spec.task_id}\nGoal: {self.spec.goal}\nDone when: {'; '.join(self.spec.done_when)}\n"
            f"Base SHA: {state.base_sha}\nBranch: {self.spec.branch}\nAllowed paths: {', '.join(self.spec.allowed_paths)}\n"
            f"Protected paths: {', '.join(self.spec.protected_paths)}\nChecks:\n{checks}\nPending findings:\n{findings}\n"
            "Do not commit, push, open PR, merge, mark ready, start another task, or modify the task spec/supervisor to escape scope."
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
