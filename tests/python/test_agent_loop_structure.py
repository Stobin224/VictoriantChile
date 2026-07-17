from __future__ import annotations

import json
import re
import subprocess
import sys
import tempfile
import unittest
import xml.etree.ElementTree as ET
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]


class AgentLoopStructureTest(unittest.TestCase):
    def test_no_new_dependencies_api_keys_or_cost_accounting(self) -> None:
        files = [*list((ROOT / "scripts" / "agent_loop").rglob("*.py")), ROOT / "scripts" / "run_agent_loop.py"]
        text = "\n".join(path.read_text(encoding="utf-8") for path in files)
        self.assertNotIn("OPENAI_API_KEY", text)
        self.assertNotIn("CODEX_API_KEY", text)
        self.assertNotIn("max_cost_usd", text)
        self.assertNotIn("Responses", text)
        self.assertNotIn("requests", text)
        self.assertNotIn("shell=True", text)

    def test_no_merge_ready_force_push_or_codex_github_action(self) -> None:
        text = "\n".join(path.read_text(encoding="utf-8") for path in (ROOT / "scripts" / "agent_loop").rglob("*.py"))
        self.assertNotIn("gh pr merge", text)
        self.assertNotIn("gh pr ready", text)
        self.assertNotIn("--force", text)
        workflow_text = "\n".join(path.read_text(encoding="utf-8") for path in (ROOT / ".github" / "workflows").glob("*.yml"))
        self.assertNotIn("codex", workflow_text.lower())

    def test_docs_templates_ignore_and_examples_exist(self) -> None:
        self.assertTrue((ROOT / "docs" / "agent_loop.md").exists())
        self.assertTrue((ROOT / "docs" / "agent_tasks" / "TEMPLATE.json").exists())
        self.assertTrue((ROOT / "docs" / "agent_tasks" / "examples" / "bounded_loop_smoke.json").exists())
        self.assertIn(".agent-loop/", (ROOT / ".gitignore").read_text(encoding="utf-8"))

    def test_modules_import_from_different_cwd_and_validate_cli_writes_json(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            out = Path(tmp) / "out" / "result.json"
            result = subprocess.run(
                [
                    sys.executable,
                    str(ROOT / "scripts" / "run_agent_loop.py"),
                    "validate",
                    "--task",
                    str(ROOT / "docs" / "agent_tasks" / "examples" / "bounded_loop_smoke.json"),
                    "--json-output",
                    str(out),
                ],
                cwd=tmp,
                capture_output=True,
                text=True,
                shell=False,
            )
            self.assertEqual(0, result.returncode, result.stderr)
            raw = out.read_bytes()
            self.assertFalse(raw.startswith(b"\xef\xbb\xbf"))
            self.assertTrue(raw.endswith(b"\n"))
            self.assertEqual("passed", json.loads(raw.decode("utf-8"))["status"])

    def test_no_artifacts_or_csproj_present(self) -> None:
        result = subprocess.run(["git", "ls-files", "--others", "--exclude-standard"], cwd=ROOT, capture_output=True, text=True, shell=False, check=True)
        for line in result.stdout.splitlines():
            self.assertFalse(line.endswith((".log", ".xml", ".csproj")))
            self.assertNotIn("TestResults", line)
            self.assertNotIn("Temp/", line)

    def test_solution_still_contains_expected_projects_only(self) -> None:
        tree = ET.parse(ROOT / "Victoriant Chile.slnx")
        projects = {element.attrib["Path"] for element in tree.getroot().findall("Project")}
        self.assertEqual(
            {
                "VictoriantChile.Content.csproj",
                "VictoriantChile.Simulation.Core.csproj",
                "VictoriantChile.Simulation.Runner.csproj",
                "VictoriantChile.Simulation.Runner.Editor.csproj",
                "VictoriantChile.Simulation.Tests.EditMode.csproj",
            },
            projects,
        )


if __name__ == "__main__":
    unittest.main()
