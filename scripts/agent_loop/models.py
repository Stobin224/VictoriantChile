from __future__ import annotations

from dataclasses import dataclass, field
from typing import Any


SCHEMA_VERSION = 1

TERMINAL_STATUSES = {
    "passed",
    "needs_input",
    "scope_violation",
    "checks_failed",
    "budget_exhausted",
    "usage_limit_reached",
    "tool_failure",
    "publication_failed",
}

RESUMABLE_STATUSES = {"branch_ready", "implementing", "checking", "reviewing", "fixing"}

EXIT_CODES = {
    "passed": 0,
    "checks_failed": 2,
    "needs_input": 3,
    "scope_violation": 4,
    "budget_exhausted": 5,
    "tool_failure": 6,
    "publication_failed": 6,
    "usage_limit_reached": 7,
}


@dataclass(frozen=True)
class CheckSpec:
    id: str
    argv: tuple[str, ...]
    timeout_seconds: int


@dataclass(frozen=True)
class Budgets:
    max_iterations: int
    max_codex_turns: int
    max_review_turns: int
    max_wall_minutes: int
    max_repeated_failure: int
    max_input_tokens: int | None = None
    max_output_tokens: int | None = None


@dataclass(frozen=True)
class ReviewConfig:
    enabled: bool
    blocking_severities: tuple[str, ...]
    allow_internal_subagents: bool = False


@dataclass(frozen=True)
class PublicationConfig:
    commit: bool
    push: bool
    draft_pr: bool
    mark_ready: bool
    merge: bool


@dataclass(frozen=True)
class TaskSpec:
    schema_version: int
    task_id: str
    title: str
    goal: str
    base_ref: str
    branch: str
    allowed_paths: tuple[str, ...]
    protected_paths: tuple[str, ...]
    done_when: tuple[str, ...]
    checks: tuple[CheckSpec, ...]
    budgets: Budgets
    review: ReviewConfig
    publication: PublicationConfig


@dataclass(frozen=True)
class ProcessResult:
    argv: tuple[str, ...]
    exit_code: int | None
    stdout: str
    stderr: str
    timed_out: bool = False


@dataclass(frozen=True)
class RawProcessResult:
    argv: tuple[str, ...]
    exit_code: int | None
    stdout: bytes
    stderr: bytes
    timed_out: bool = False
    stdout_limited: bool = False
    stderr_limited: bool = False


@dataclass(frozen=True)
class CheckResult:
    id: str
    argv: tuple[str, ...]
    status: str
    exit_code: int | None
    stdout: str
    stderr: str


@dataclass(frozen=True)
class Finding:
    id: str
    severity: str
    title: str
    evidence: str
    path: str | None = None
    line: int | None = None
    in_scope: bool = True
    suggested_fix: str | None = None


@dataclass
class Usage:
    input_tokens: int | None = None
    cached_input_tokens: int | None = None
    output_tokens: int | None = None
    reasoning_output_tokens: int | None = None

    def add(self, other: dict[str, Any] | None) -> None:
        if not isinstance(other, dict):
            return
        mapping = {
            "input_tokens": "input_tokens",
            "cached_input_tokens": "cached_input_tokens",
            "output_tokens": "output_tokens",
            "reasoning_output_tokens": "reasoning_output_tokens",
        }
        for source, attr in mapping.items():
            value = other.get(source)
            if isinstance(value, int) and not isinstance(value, bool):
                current = getattr(self, attr)
                setattr(self, attr, value if current is None else current + value)

    def to_json(self) -> dict[str, int | None]:
        return {
            "input_tokens": self.input_tokens,
            "cached_input_tokens": self.cached_input_tokens,
            "output_tokens": self.output_tokens,
            "reasoning_output_tokens": self.reasoning_output_tokens,
        }


@dataclass
class LoopState:
    run_id: str
    task_id: str
    task_hash: str
    base_ref: str
    base_sha: str
    branch: str
    status: str
    iteration: int = 0
    codex_turns: int = 0
    review_turns: int = 0
    usage: Usage = field(default_factory=Usage)
    writer_session_id: str | None = None
    checks: list[CheckResult] = field(default_factory=list)
    findings: list[Finding] = field(default_factory=list)
    errors: list[str] = field(default_factory=list)
    changed_files: list[str] = field(default_factory=list)
    fingerprint: str | None = None
    pr_url: str | None = None
    agents_runtime: dict[str, Any] = field(default_factory=dict)
    runtime_temp: dict[str, Any] = field(default_factory=dict)
    budget_observations: dict[str, Any] = field(default_factory=dict)
