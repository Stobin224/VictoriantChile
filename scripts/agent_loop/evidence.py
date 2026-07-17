from __future__ import annotations

import time
from pathlib import Path
from typing import Any

from .io_utils import atomic_write_json
from .models import CheckResult, Finding, LoopState, RESUMABLE_STATUSES, SCHEMA_VERSION


def state_to_json(state: LoopState, *, elapsed_seconds: int, resume_command: str | None) -> dict[str, Any]:
    return {
        "schema_version": SCHEMA_VERSION,
        "run_id": state.run_id,
        "task_id": state.task_id,
        "task_hash": state.task_hash,
        "status": state.status,
        "base_ref": state.base_ref,
        "base_sha": state.base_sha,
        "branch": state.branch,
        "iteration": state.iteration,
        "codex_turns": state.codex_turns,
        "review_turns": state.review_turns,
        "elapsed_seconds": elapsed_seconds,
        "usage": state.usage.to_json(),
        "changed_files": sorted(state.changed_files),
        "checks": [check_to_json(check) for check in state.checks],
        "findings": [finding_to_json(finding) for finding in state.findings],
        "writer_session_id": state.writer_session_id,
        "fingerprint": state.fingerprint,
        "errors": list(state.errors),
        "pr_url": state.pr_url,
        "resume_command": resume_command,
    }


def check_to_json(check: CheckResult) -> dict[str, Any]:
    return {
        "id": check.id,
        "argv": list(check.argv),
        "status": check.status,
        "exit_code": check.exit_code,
        "stdout": check.stdout,
        "stderr": check.stderr,
    }


def finding_to_json(finding: Finding) -> dict[str, Any]:
    return {
        "id": finding.id,
        "severity": finding.severity,
        "title": finding.title,
        "evidence": finding.evidence,
        "path": finding.path,
        "line": finding.line,
        "in_scope": finding.in_scope,
        "suggested_fix": finding.suggested_fix,
    }


class EvidenceStore:
    def __init__(self, repo: Path, run_id: str, clock: callable = time.time) -> None:
        self.repo = repo
        self.run_id = run_id
        self.clock = clock
        self.run_dir = repo / ".agent-loop" / "runs" / run_id

    def write_state(self, state: LoopState, *, start_time: float) -> dict[str, Any]:
        resume = f"python scripts/run_agent_loop.py resume --run-id {self.run_id}" if state.status in RESUMABLE_STATUSES else None
        data = state_to_json(state, elapsed_seconds=int(self.clock() - start_time), resume_command=resume)
        atomic_write_json(self.run_dir / "state.json", data)
        return data

    def write_turn_evidence(self, turn_index: int, role: str, data: dict[str, Any]) -> None:
        payload = {"schema_version": SCHEMA_VERSION, "turn_index": turn_index, "role": role, **data}
        atomic_write_json(self.run_dir / f"{role}-turn-{turn_index:03d}.json", payload)
