from __future__ import annotations

import json
import hashlib
import os
import re
import subprocess
import sys
import tempfile
import time
import unittest
from copy import deepcopy
from pathlib import Path
from unittest import mock

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT))

import scripts.run_scenario as run_scenario
from scripts.find_unity import UnityResolution

EXPECTED_STATE_HASH = "sha256:1f39c5fdfb920f31532e52646c3ceca468a667aa485e49202ff2f0c357fe6aef"


def re_unfiltered_catch_exception(text: str) -> bool:
    return re.search(r"catch\s*\(\s*Exception\b[^)]*\)\s*\{", text) is not None


class ScenarioRunnerScriptTest(unittest.TestCase):
    def test_expected_json_bytes_are_stable_and_environment_free(self) -> None:
        path = ROOT / "tests" / "scenarios" / "smoke_v1.expected.json"
        raw = path.read_bytes()

        self.assertFalse(raw.startswith(b"\xef\xbb\xbf"))
        self.assertNotIn(b"\r", raw)
        self.assertTrue(raw.endswith(b"\n"))
        self.assertFalse(raw.endswith(b"\n\n"))
        text = raw.decode("utf-8")
        for forbidden in ("C:\\", "/Users/", "/home/", "/tmp/", "AppData", ".log", ".xml", "editor_path", "project_path", "timestamp", "elapsed", "duration", "pid"):
            self.assertNotIn(forbidden, text)
        for line in text.splitlines()[1:]:
            if line:
                leading = len(line) - len(line.lstrip(" "))
                self.assertEqual(0, leading % 2, line)

    def test_python_oracle_recomputes_state_hash_independently(self) -> None:
        data = self.expected_data()
        state = data["state"]

        self.assertEqual(EXPECTED_STATE_HASH, data["state_hash"])
        self.assert_state_order(state)
        computed = self.canonical_hash(state)
        self.assertEqual(EXPECTED_STATE_HASH, computed)

    def test_python_oracle_hash_changes_for_seed_value_clout_or_content_hash(self) -> None:
        base = self.expected_data()["state"]
        base_hash = self.canonical_hash(base)
        self.assertEqual(EXPECTED_STATE_HASH, base_hash)

        changed_seed = deepcopy(base)
        changed_seed["rng_seed"] += 1
        self.assertNotEqual(base_hash, self.canonical_hash(changed_seed))

        changed_metric = deepcopy(base)
        changed_metric["metrics"][0]["value_s"] += 1
        self.assertNotEqual(base_hash, self.canonical_hash(changed_metric))

        changed_clout = deepcopy(base)
        changed_clout["interest_groups"][0]["clout_s"] += 1
        self.assertNotEqual(base_hash, self.canonical_hash(changed_clout))

        changed_content_hash = deepcopy(base)
        old_hash = changed_content_hash["content"]["files"][0]["hash"]
        changed_content_hash["content"]["files"][0]["hash"] = old_hash[:-1] + ("0" if old_hash[-1] != "0" else "1")
        self.assertNotEqual(base_hash, self.canonical_hash(changed_content_hash))

    def test_build_command_uses_execute_method_and_absolute_paths(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            editor = root / "Unity With Spaces.exe"
            scenario = root / "scenario with spaces.json"
            content = root / "content root"
            output = root / "out.json"
            log = root / "scenario.log"

            command = run_scenario.build_command(str(editor), scenario, content, output, log)

            self.assertEqual(str(editor), command[0])
            self.assertIn("-batchmode", command)
            self.assertIn("-nographics", command)
            self.assertIn("-executeMethod", command)
            self.assertEqual(
                "VictoriantChile.Simulation.Runner.Editor.ScenarioRunnerCommand.Run",
                command[command.index("-executeMethod") + 1],
            )
            self.assertEqual(str(scenario), command[command.index("--scenario") + 1])
            self.assertEqual(str(content), command[command.index("--content-root") + 1])
            self.assertEqual(str(output), command[command.index("--json-output") + 1])
            self.assertNotIn("-accept-apiupdate", command)
            self.assertNotIn("-quit", command)

    def test_validate_result_json_accepts_pass_and_fail_shapes(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "result.json"
            passed = {
                "result_schema_version": 1,
                "status": "passed",
                "scenario_schema_version": 1,
                "seed": 1,
                "command_count": 0,
                "commands": [],
                "state_hash": "sha256:" + "a" * 64,
                "state": {},
                "diagnostics": [],
            }
            path.write_text(json.dumps(passed), encoding="utf-8")
            self.assertEqual([], run_scenario.validate_result_json(path))

            failed = dict(passed)
            failed["status"] = "failed"
            failed["state_hash"] = None
            failed["state"] = None
            failed["diagnostics"] = [{"code": "x"}]
            path.write_text(json.dumps(failed), encoding="utf-8")
            self.assertEqual([], run_scenario.validate_result_json(path))

    def test_validate_result_json_rejects_bad_schema_and_hash(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "result.json"
            data = {
                "status": "passed",
                "result_schema_version": 1,
                "scenario_schema_version": 1,
                "seed": 1,
                "command_count": 1,
                "commands": [],
                "state_hash": "bad",
                "state": {},
                "diagnostics": [],
            }
            path.write_text(json.dumps(data), encoding="utf-8")
            errors = run_scenario.validate_result_json(path)
            self.assertIn("stable schema order", "\n".join(errors))
            self.assertIn("state_hash", "\n".join(errors))

    def test_validate_result_json_rejects_wrong_command_count(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "result.json"
            data = {
                "result_schema_version": 1,
                "status": "passed",
                "scenario_schema_version": 1,
                "seed": 1,
                "command_count": 2,
                "commands": [],
                "state_hash": "sha256:" + "a" * 64,
                "state": {},
                "diagnostics": [],
            }
            path.write_text(json.dumps(data), encoding="utf-8")
            self.assertIn("command_count", "\n".join(run_scenario.validate_result_json(path)))

    def test_run_rejects_missing_stale_and_malformed_output(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            output = Path(tmp) / "scenario.json"
            args = self.args(output)

            result = self.run_with_fake_unity(args, lambda _path: None)
            self.assertEqual("FAIL", result["status"])
            self.assertIn("missing or stale", "\n".join(result["errors"]))

            old_payload = b"{not json"
            output.write_bytes(old_payload)
            os.utime(output, (time.time() - 100, time.time() - 100))
            result = self.run_with_fake_unity(args, lambda _path: None)
            self.assertEqual("FAIL", result["status"])
            self.assertIn("missing or stale", "\n".join(result["errors"]))

            result = self.run_with_fake_unity(args, lambda path: path.write_text("{not json", encoding="utf-8"))
            self.assertEqual("FAIL", result["status"])
            self.assertIn("invalid", "\n".join(result["errors"]))

    def test_run_accepts_fresh_valid_output(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            output = Path(tmp) / "scenario.json"

            def writer(path: Path) -> None:
                data = {
                    "result_schema_version": 1,
                    "status": "passed",
                    "scenario_schema_version": 1,
                    "seed": 1,
                    "command_count": 0,
                    "commands": [],
                    "state_hash": "sha256:" + "b" * 64,
                    "state": {},
                    "diagnostics": [],
                }
                path.write_text(json.dumps(data), encoding="utf-8")
                os.utime(path, (time.time() + 1, time.time() + 1))

            result = self.run_with_fake_unity(self.args(output), writer)
            self.assertEqual("PASS", result["status"], result["errors"])
            self.assertEqual(0, result["exit_code"])

    def test_run_returns_failure_for_valid_failed_scenario_json(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            output = Path(tmp) / "missing" / "parent" / "scenario.json"

            def writer(path: Path) -> None:
                data = {
                    "result_schema_version": 1,
                    "status": "failed",
                    "scenario_schema_version": 1,
                    "seed": 1,
                    "command_count": 1,
                    "commands": [],
                    "state_hash": None,
                    "state": None,
                    "diagnostics": [{"code": "target.read_only", "target": "x", "message": "x"}],
                }
                path.write_text(json.dumps(data), encoding="utf-8")
                os.utime(path, (time.time() + 1, time.time() + 1))

            result = self.run_with_fake_unity(self.args(output), writer, returncode=2)
            self.assertEqual("FAIL", result["status"])
            self.assertEqual(2, result["exit_code"])
            self.assertTrue(output.parent.is_dir())
            self.assertIn("reported failed", "\n".join(result["errors"]))

    def test_run_rejects_non_positive_timeout_without_launching(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            args = self.args(Path(tmp) / "scenario.json")
            args.timeout_seconds = 0
            with mock.patch.object(run_scenario, "resolve_unity_editor") as resolve:
                result = run_scenario.run(args)
            self.assertEqual("FAIL", result["status"])
            self.assertIn("timeout must be positive", "\n".join(result["errors"]))
            resolve.assert_not_called()

    def test_run_works_from_different_cwd_for_discovery_errors(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            output = Path(tmp) / "out.json"
            result = subprocess.run(
                [
                    sys.executable,
                    str(ROOT / "scripts" / "run_scenario.py"),
                    "--unity-editor",
                    str(Path(tmp) / "missing" / "Unity.exe"),
                    "--json-output",
                    str(output),
                ],
                cwd=tmp,
                capture_output=True,
                text=True,
                shell=False,
            )
            self.assertNotEqual(0, result.returncode)
            self.assertIn("Unity Editor discovery failed", result.stdout)
            self.assertNotIn("Traceback", result.stdout + result.stderr)

    def test_run_checks_scenario_flag_requires_include(self) -> None:
        result = subprocess.run(
            [
                sys.executable,
                str(ROOT / "scripts" / "run_checks.py"),
                "--scenario",
                str(ROOT / "tests" / "scenarios" / "smoke_v1.json"),
            ],
            cwd=ROOT,
            capture_output=True,
            text=True,
            shell=False,
        )
        self.assertNotEqual(0, result.returncode)
        self.assertIn("--scenario requires --include-unity-scenario", result.stderr)

    def test_editor_host_writes_json_via_temp_file_and_stable_unexpected_failure(self) -> None:
        host = (ROOT / "Assets" / "VictoriantChile" / "Simulation" / "Runner" / "Editor" / "ScenarioRunnerCommand.cs").read_text(encoding="utf-8")
        self.assertIn('".tmp-"', host)
        self.assertIn("stream.Flush(true)", host)
        self.assertIn("File.Replace(tempPath, fullPath, null)", host)
        self.assertIn("bool hasScenario", host)
        self.assertIn("bool hasContentRoot", host)
        self.assertIn("bool hasJsonOutput", host)
        self.assertIn("runner.unexpected", host)
        self.assertIn("Debug.LogException(ex)", host)
        self.assertNotIn("ex.Message", host)
        self.assertEqual(1, host.count("catch (Exception ex)"))

    def test_catch_exception_is_limited_to_editor_boundary(self) -> None:
        for path in (ROOT / "Assets" / "VictoriantChile").rglob("*.cs"):
            text = path.read_text(encoding="utf-8")
            if re_unfiltered_catch_exception(text):
                self.assertEqual(
                    "ScenarioRunnerCommand.cs",
                    path.name,
                    f"unexpected catch-all outside editor boundary: {path}",
                )

    def args(self, output: Path):
        class Args:
            scenario = ROOT / "tests" / "scenarios" / "smoke_v1.json"
            content_root = ROOT / "Assets" / "StreamingAssets" / "content"
            json_output = output
            unity_editor = "Unity.exe"
            timeout_seconds = 1

        return Args()

    def run_with_fake_unity(self, args, writer, returncode: int = 0):
        resolution = UnityResolution(
            True,
            "6000.3.10f1",
            "Unity.exe",
            "6000.3.10f1",
            "cli",
            ["Unity.exe"],
            [],
        )

        def fake_process(_command, _cwd, _timeout):
            writer(args.json_output)
            return subprocess.CompletedProcess([], returncode, "", "")

        with mock.patch.object(run_scenario, "resolve_unity_editor", return_value=resolution), mock.patch.object(
            run_scenario,
            "run_unity_process",
            side_effect=fake_process,
        ):
            return run_scenario.run(args)

    def expected_data(self):
        return json.loads((ROOT / "tests" / "scenarios" / "smoke_v1.expected.json").read_text(encoding="utf-8"))

    def canonical_hash(self, state: dict) -> str:
        text = json.dumps(state, ensure_ascii=True, separators=(",", ":"))
        digest = hashlib.sha256(text.encode("utf-8")).hexdigest()
        return "sha256:" + digest

    def assert_state_order(self, state: dict) -> None:
        self.assertEqual(
            [
                "state_schema_version",
                "tick",
                "rng_seed",
                "content",
                "metrics",
                "internals",
                "regions",
                "interest_groups",
                "movements",
                "active_effects",
            ],
            list(state.keys()),
        )
        self.assertEqual(
            ["content_pack_version", "content_schema_version", "min_game_schema_version", "default_language", "files"],
            list(state["content"].keys()),
        )
        self.assert_sorted_by(state["content"]["files"], "path")
        for item in state["content"]["files"]:
            self.assertEqual(["path", "hash"], list(item.keys()))
            self.assertRegex(item["hash"], r"^sha256:[0-9a-f]{64}$")
        self.assert_sorted_by(state["metrics"], "id")
        for item in state["metrics"]:
            self.assertEqual(["id", "value_s"], list(item.keys()))
        self.assert_sorted_by(state["internals"], "domain")
        for domain in state["internals"]:
            self.assertEqual(["domain", "components"], list(domain.keys()))
            self.assert_sorted_by(domain["components"], "id")
            for component in domain["components"]:
                self.assertEqual(["id", "value_s"], list(component.keys()))
        self.assert_sorted_by(state["regions"], "id")
        for item in state["regions"]:
            self.assertEqual(["id", "support_s", "tension_s", "organization_s", "rival_presence_s"], list(item.keys()))
        self.assert_sorted_by(state["interest_groups"], "id")
        for item in state["interest_groups"]:
            self.assertEqual(["id", "clout_s", "approval_s"], list(item.keys()))
        self.assert_sorted_by(state["movements"], "id")
        for item in state["movements"]:
            self.assertEqual(["id", "intensity_s", "direction"], list(item.keys()))
        self.assertEqual([], state["active_effects"])

    def assert_sorted_by(self, values: list[dict], key: str) -> None:
        ids = [item[key] for item in values]
        self.assertEqual(sorted(ids), ids)


if __name__ == "__main__":
    unittest.main()
