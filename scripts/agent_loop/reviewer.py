from __future__ import annotations

import json
from pathlib import Path

from .codex_client import CodexClient
from .git_guard import fingerprint_worktree
from .models import Finding, TaskSpec


REVIEW_SCHEMA = {
    "type": "object",
    "additionalProperties": False,
    "required": ["verdict", "summary", "findings"],
    "properties": {
        "verdict": {"enum": ["pass", "changes_requested"]},
        "summary": {"type": "string"},
        "findings": {
            "type": "array",
            "items": {
                "type": "object",
                "additionalProperties": False,
                "required": ["id", "severity", "title", "evidence", "path", "line", "in_scope", "suggested_fix"],
                "properties": {
                    "id": {"type": "string"},
                    "severity": {"enum": ["critical", "high", "medium", "low"]},
                    "title": {"type": "string"},
                    "evidence": {"type": "string"},
                    "path": {"type": ["string", "null"]},
                    "line": {"type": ["integer", "null"]},
                    "in_scope": {"type": "boolean"},
                    "suggested_fix": {"type": ["string", "null"]},
                },
            },
        },
    },
}


def write_review_schema(path: Path) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(REVIEW_SCHEMA, indent=2) + "\n", encoding="utf-8")


class ReviewError(RuntimeError):
    pass


def review_diff(client: CodexClient, spec: TaskSpec, base_sha: str, schema_path: Path, timeout_seconds: int) -> tuple[list[Finding], str]:
    before = fingerprint_worktree(client.repo)
    prompt = (
        "Review the current repository diff against base SHA "
        f"{base_sha}. Return only JSON matching the supplied schema. "
        "Prioritize bugs, policy violations, unsafe git behavior, missing tests, and scope issues. "
        f"Allowed paths: {', '.join(spec.allowed_paths)}. Protected paths: {', '.join(spec.protected_paths)}."
    )
    result = client.exec(prompt, sandbox="read-only", output_schema=schema_path, timeout_seconds=timeout_seconds)
    after = fingerprint_worktree(client.repo)
    if before != after:
        raise ReviewError("working tree changed during read-only review")
    if not result.ok:
        raise ReviewError("; ".join(result.errors) or "review codex turn failed")
    try:
        data = json.loads(result.final_message or result.stdout.splitlines()[-1])
    except (IndexError, json.JSONDecodeError) as exc:
        raise ReviewError(f"review output was not valid JSON: {exc}") from exc
    return parse_review_output(data), result.session_id or ""


def parse_review_output(data: object) -> list[Finding]:
    if not isinstance(data, dict):
        raise ReviewError("review root must be object")
    if data.get("verdict") not in {"pass", "changes_requested"}:
        raise ReviewError("review verdict must be pass or changes_requested")
    findings = data.get("findings")
    if not isinstance(findings, list):
        raise ReviewError("review findings must be an array")
    parsed: list[Finding] = []
    for index, item in enumerate(findings):
        if not isinstance(item, dict):
            raise ReviewError(f"finding {index} must be object")
        severity = item.get("severity")
        if severity not in {"critical", "high", "medium", "low"}:
            raise ReviewError(f"finding {index} has invalid severity")
        parsed.append(
            Finding(
                str(item.get("id") or f"REV-{index+1:03d}"),
                severity,
                str(item.get("title") or ""),
                str(item.get("evidence") or ""),
                item.get("path") if isinstance(item.get("path"), str) else None,
                item.get("line") if type(item.get("line")) is int else None,
                bool(item.get("in_scope")),
                item.get("suggested_fix") if isinstance(item.get("suggested_fix"), str) else None,
            )
        )
    return sorted(parsed, key=lambda f: ({"critical": 0, "high": 1, "medium": 2, "low": 3}[f.severity], f.id))
