from __future__ import annotations

import json
import tempfile
import unittest
from pathlib import Path

from scripts.agent_loop.task_spec import TaskSpecError, expand_argv, load_task_spec, parse_task_spec


def valid_spec() -> dict:
    return {
        "schema_version": 1,
        "task_id": "effects-engine-v1",
        "title": "feat: effects engine",
        "goal": "Implement bounded behavior",
        "base_ref": "origin/main",
        "branch": "feat/effects-engine-v1",
        "allowed_paths": ["scripts/example.py", "tests/python/"],
        "protected_paths": ["Assets/StreamingAssets/content/", "Packages/", "ProjectSettings/"],
        "done_when": ["done", "checks pass"],
        "checks": [{"id": "repo", "argv": ["{python}", "scripts/run_checks.py", "--base-ref", "{base_ref}"], "timeout_seconds": 30}],
        "budgets": {
            "max_iterations": 3,
            "max_codex_turns": 6,
            "max_review_turns": 2,
            "max_wall_minutes": 90,
            "max_repeated_failure": 2,
        },
        "review": {"enabled": True, "blocking_severities": ["critical", "high", "medium"], "allow_internal_subagents": False},
        "publication": {"commit": False, "push": False, "draft_pr": False, "mark_ready": False, "merge": False},
    }


class AgentLoopTaskSpecTest(unittest.TestCase):
    def test_valid_spec_and_placeholder_expansion(self) -> None:
        spec = parse_task_spec(valid_spec())
        argv = expand_argv(spec.checks[0].argv, spec, Path("repo"))
        self.assertEqual("origin/main", argv[-1])
        self.assertTrue(argv[0].endswith("python.exe") or "python" in Path(argv[0]).name.lower())

    def test_malformed_json_and_duplicate_property_fail(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            bad = Path(tmp) / "bad.json"
            bad.write_text("{not json", encoding="utf-8")
            with self.assertRaises(TaskSpecError):
                load_task_spec(bad)
            dup = Path(tmp) / "dup.json"
            dup.write_text('{"schema_version":1,"schema_version":1}', encoding="utf-8")
            with self.assertRaisesRegex(TaskSpecError, "duplicate"):
                load_task_spec(dup)

    def test_unknown_property_and_forbidden_api_or_cost_fields_fail(self) -> None:
        for key in ("unknown", "OPENAI_API_KEY", "max_cost_usd"):
            data = valid_spec()
            data[key] = "x"
            with self.assertRaises(TaskSpecError, msg=key):
                parse_task_spec(data)

    def test_bool_as_int_and_non_positive_budgets_fail(self) -> None:
        data = valid_spec()
        data["schema_version"] = True
        with self.assertRaises(TaskSpecError):
            parse_task_spec(data)
        data = valid_spec()
        data["budgets"]["max_iterations"] = 0
        with self.assertRaises(TaskSpecError):
            parse_task_spec(data)

    def test_main_master_branches_fail(self) -> None:
        for branch in ("main", "master", "feature/main"):
            data = valid_spec()
            data["branch"] = branch
            with self.assertRaises(TaskSpecError):
                parse_task_spec(data)

    def test_unsafe_paths_fail(self) -> None:
        for path in ("C:/x", "/tmp/x", "../x", "a\\b", ".git/config"):
            data = valid_spec()
            data["allowed_paths"] = [path]
            with self.assertRaises(TaskSpecError, msg=path):
                parse_task_spec(data)

    def test_check_shell_string_unknown_placeholder_and_empty_command_fail(self) -> None:
        data = valid_spec()
        data["checks"][0]["argv"] = "python scripts/run_checks.py"
        with self.assertRaises(TaskSpecError):
            parse_task_spec(data)
        data = valid_spec()
        data["checks"][0]["argv"] = ["{missing}"]
        with self.assertRaisesRegex(TaskSpecError, "unknown placeholder"):
            parse_task_spec(data)
        data = valid_spec()
        data["checks"][0]["argv"] = []
        with self.assertRaises(TaskSpecError):
            parse_task_spec(data)

    def test_merge_and_ready_are_forbidden(self) -> None:
        for key in ("merge", "mark_ready"):
            data = valid_spec()
            data["publication"][key] = True
            with self.assertRaises(TaskSpecError):
                parse_task_spec(data)


if __name__ == "__main__":
    unittest.main()
