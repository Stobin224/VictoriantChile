from __future__ import annotations

import hashlib
import json
import re
import sys
from pathlib import Path
from typing import Any

from .models import Budgets, CheckSpec, PublicationConfig, ReviewConfig, TaskSpec


ID_RE = re.compile(r"^[a-z][a-z0-9_-]*$")
BRANCH_RE = re.compile(r"^[A-Za-z0-9._/-]+$")
PLACEHOLDER_RE = re.compile(r"\{([a-zA-Z0-9_]+)\}")
ALLOWED_PLACEHOLDERS = {"python", "repo", "base_ref", "branch"}

TASK_KEYS = {
    "schema_version",
    "task_id",
    "title",
    "goal",
    "base_ref",
    "branch",
    "allowed_paths",
    "protected_paths",
    "done_when",
    "checks",
    "budgets",
    "review",
    "publication",
}
REQUIRED_BUDGET_KEYS = {"max_iterations", "max_codex_turns", "max_review_turns", "max_wall_minutes", "max_repeated_failure"}
OPTIONAL_BUDGET_KEYS = {"max_input_tokens", "max_output_tokens"}
BUDGET_KEYS = REQUIRED_BUDGET_KEYS | OPTIONAL_BUDGET_KEYS
CHECK_KEYS = {"id", "argv", "timeout_seconds"}
REVIEW_KEYS = {"enabled", "blocking_severities", "allow_internal_subagents"}
PUBLICATION_KEYS = {"commit", "push", "draft_pr", "mark_ready", "merge"}
FORBIDDEN_KEYS = {"api_key", "openai_api_key", "codex_api_key", "access_token", "max" + "_cost_usd", "cost" + "_usd"}
SEVERITIES = {"critical", "high", "medium", "low"}


class TaskSpecError(ValueError):
    pass


def no_duplicates_object_pairs_hook(pairs: list[tuple[str, Any]]) -> dict[str, Any]:
    result: dict[str, Any] = {}
    for key, value in pairs:
        if key in result:
            raise TaskSpecError(f"duplicate JSON property: {key}")
        result[key] = value
    return result


def load_task_spec(path: Path) -> tuple[TaskSpec, str]:
    try:
        raw = path.read_bytes()
        text = raw.decode("utf-8")
        data = json.loads(text, object_pairs_hook=no_duplicates_object_pairs_hook)
    except TaskSpecError:
        raise
    except (OSError, UnicodeDecodeError, json.JSONDecodeError) as exc:
        raise TaskSpecError(f"invalid task spec JSON: {exc}") from exc
    spec = parse_task_spec(data)
    return spec, hashlib.sha256(raw).hexdigest()


def parse_task_spec(data: Any) -> TaskSpec:
    if not isinstance(data, dict):
        raise TaskSpecError("task spec root must be an object")
    reject_forbidden_keys(data)
    unknown = set(data) - TASK_KEYS
    if unknown:
        raise TaskSpecError(f"unknown task spec properties: {sorted(unknown)}")
    required = TASK_KEYS
    missing = required - set(data)
    if missing:
        raise TaskSpecError(f"missing required task spec properties: {sorted(missing)}")
    schema_version = plain_positive_int(data["schema_version"], "schema_version")
    if schema_version != 1:
        raise TaskSpecError("schema_version must be 1")
    task_id = required_string(data["task_id"], "task_id")
    if not ID_RE.match(task_id):
        raise TaskSpecError("task_id must be ASCII lowercase id")
    title = required_string(data["title"], "title")
    goal = required_string(data["goal"], "goal")
    base_ref = required_string(data["base_ref"], "base_ref")
    branch = required_string(data["branch"], "branch")
    if branch in {"main", "master"} or branch.endswith("/main") or branch.endswith("/master"):
        raise TaskSpecError("branch cannot be main or master")
    if not BRANCH_RE.match(branch):
        raise TaskSpecError("branch contains invalid characters")
    allowed_paths = parse_paths(data["allowed_paths"], "allowed_paths")
    protected_paths = parse_paths(data["protected_paths"], "protected_paths")
    done_when = parse_string_list(data["done_when"], "done_when")
    checks = parse_checks(data["checks"])
    budgets = parse_budgets(data["budgets"])
    review = parse_review(data["review"])
    publication = parse_publication(data["publication"])
    return TaskSpec(
        schema_version,
        task_id,
        title,
        goal,
        base_ref,
        branch,
        tuple(allowed_paths),
        tuple(protected_paths),
        tuple(done_when),
        tuple(checks),
        budgets,
        review,
        publication,
    )


def reject_forbidden_keys(value: Any) -> None:
    if isinstance(value, dict):
        for key, child in value.items():
            lowered = key.lower()
            if lowered in FORBIDDEN_KEYS or "api_key" in lowered or "cost" in lowered:
                raise TaskSpecError(f"forbidden property: {key}")
            reject_forbidden_keys(child)
    elif isinstance(value, list):
        for child in value:
            reject_forbidden_keys(child)


def required_string(value: Any, field: str) -> str:
    if not isinstance(value, str) or not value:
        raise TaskSpecError(f"{field} must be a non-empty string")
    return value


def plain_positive_int(value: Any, field: str) -> int:
    if type(value) is not int:
        raise TaskSpecError(f"{field} must be a positive integer")
    if value <= 0:
        raise TaskSpecError(f"{field} must be positive")
    return value


def parse_string_list(value: Any, field: str) -> list[str]:
    if not isinstance(value, list) or not value:
        raise TaskSpecError(f"{field} must be a non-empty array")
    return [required_string(item, field) for item in value]


def normalize_task_path(path: str) -> str:
    if not path or path.startswith("/") or re.match(r"^[A-Za-z]:[\\/]", path):
        raise TaskSpecError(f"unsafe path: {path}")
    if "\\" in path:
        raise TaskSpecError(f"paths must use forward slashes: {path}")
    if path.startswith(".git/") or path == ".git":
        raise TaskSpecError(".git is not an allowed task path")
    parts = path.rstrip("/").split("/")
    if any(part in {"", ".", ".."} for part in parts):
        raise TaskSpecError(f"unsafe path segment: {path}")
    return path


def parse_paths(value: Any, field: str) -> list[str]:
    return [normalize_task_path(item) for item in parse_string_list(value, field)]


def parse_checks(value: Any) -> list[CheckSpec]:
    if not isinstance(value, list) or not value:
        raise TaskSpecError("checks must be a non-empty array")
    checks: list[CheckSpec] = []
    ids: set[str] = set()
    for index, item in enumerate(value):
        if not isinstance(item, dict):
            raise TaskSpecError(f"checks[{index}] must be an object")
        unknown = set(item) - CHECK_KEYS
        if unknown:
            raise TaskSpecError(f"checks[{index}] has unknown properties: {sorted(unknown)}")
        check_id = required_string(item.get("id"), f"checks[{index}].id")
        if check_id in ids:
            raise TaskSpecError(f"duplicate check id: {check_id}")
        ids.add(check_id)
        argv = item.get("argv")
        if isinstance(argv, str):
            raise TaskSpecError("check argv must be an array, not a shell string")
        if not isinstance(argv, list) or not argv:
            raise TaskSpecError("check argv must be a non-empty array")
        parsed_argv = []
        for arg in argv:
            arg = required_string(arg, "check argv")
            for placeholder in PLACEHOLDER_RE.findall(arg):
                if placeholder not in ALLOWED_PLACEHOLDERS:
                    raise TaskSpecError(f"unknown placeholder: {placeholder}")
            parsed_argv.append(arg)
        timeout = plain_positive_int(item.get("timeout_seconds"), f"checks[{index}].timeout_seconds")
        checks.append(CheckSpec(check_id, tuple(parsed_argv), timeout))
    return checks


def parse_budgets(value: Any) -> Budgets:
    if not isinstance(value, dict):
        raise TaskSpecError("budgets must be an object")
    unknown = set(value) - BUDGET_KEYS
    if unknown:
        raise TaskSpecError(f"budgets has unknown properties: {sorted(unknown)}")
    missing = REQUIRED_BUDGET_KEYS - set(value)
    if missing:
        raise TaskSpecError(f"budgets missing properties: {sorted(missing)}")
    return Budgets(
        plain_positive_int(value["max_iterations"], "max_iterations"),
        plain_positive_int(value["max_codex_turns"], "max_codex_turns"),
        plain_positive_int(value["max_review_turns"], "max_review_turns"),
        plain_positive_int(value["max_wall_minutes"], "max_wall_minutes"),
        plain_positive_int(value["max_repeated_failure"], "max_repeated_failure"),
        plain_positive_int(value["max_input_tokens"], "max_input_tokens") if "max_input_tokens" in value else None,
        plain_positive_int(value["max_output_tokens"], "max_output_tokens") if "max_output_tokens" in value else None,
    )


def parse_review(value: Any) -> ReviewConfig:
    if not isinstance(value, dict):
        raise TaskSpecError("review must be an object")
    unknown = set(value) - REVIEW_KEYS
    if unknown:
        raise TaskSpecError(f"review has unknown properties: {sorted(unknown)}")
    enabled = value.get("enabled")
    if type(enabled) is not bool:
        raise TaskSpecError("review.enabled must be boolean")
    blocking = parse_string_list(value.get("blocking_severities"), "review.blocking_severities")
    for severity in blocking:
        if severity not in SEVERITIES:
            raise TaskSpecError(f"unknown review severity: {severity}")
    allow_subagents = value.get("allow_internal_subagents", False)
    if type(allow_subagents) is not bool:
        raise TaskSpecError("review.allow_internal_subagents must be boolean")
    return ReviewConfig(enabled, tuple(blocking), allow_subagents)


def parse_publication(value: Any) -> PublicationConfig:
    if not isinstance(value, dict):
        raise TaskSpecError("publication must be an object")
    unknown = set(value) - PUBLICATION_KEYS
    if unknown:
        raise TaskSpecError(f"publication has unknown properties: {sorted(unknown)}")
    values: dict[str, bool] = {}
    for key in PUBLICATION_KEYS:
        if key not in value or type(value[key]) is not bool:
            raise TaskSpecError(f"publication.{key} must be boolean")
        values[key] = value[key]
    if values["merge"]:
        raise TaskSpecError("publication.merge must be false")
    if values["mark_ready"]:
        raise TaskSpecError("publication.mark_ready must be false")
    return PublicationConfig(
        values["commit"],
        values["push"],
        values["draft_pr"],
        values["mark_ready"],
        values["merge"],
    )


def expand_argv(argv: tuple[str, ...], spec: TaskSpec, repo: Path) -> list[str]:
    values = {
        "python": sys.executable,
        "repo": str(repo),
        "base_ref": spec.base_ref,
        "branch": spec.branch,
    }
    expanded = []
    for arg in argv:
        expanded.append(PLACEHOLDER_RE.sub(lambda match: values[match.group(1)], arg))
    return expanded
