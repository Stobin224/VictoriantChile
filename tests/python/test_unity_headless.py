from __future__ import annotations

import json
import os
import shutil
import stat
import subprocess
import sys
import tempfile
import time
import unittest
from unittest import mock
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT))

import scripts.run_unity_editmode as unity_runner
import scripts.find_unity as find_unity
from scripts.find_unity import read_project_version, resolve_unity_editor, standard_unity_paths
from scripts.run_unity_editmode import (
    build_unity_command,
    default_output_paths,
    parse_results_xml,
    run_editmode,
    run_unity_process,
    terminate_process_tree,
)


def write_project_version(root: Path, version: str = "6000.3.10f1") -> Path:
    settings = root / "ProjectSettings"
    settings.mkdir(parents=True)
    path = settings / "ProjectVersion.txt"
    path.write_text(f"m_EditorVersion: {version}\nm_EditorVersionWithRevision: {version} (test)\n", encoding="utf-8")
    return path


def make_executable(path: Path) -> Path:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text("mock editor\n", encoding="utf-8")
    path.chmod(path.stat().st_mode | stat.S_IXUSR)
    return path


class FindUnityTest(unittest.TestCase):
    def setUp(self) -> None:
        self.tmp = tempfile.TemporaryDirectory()
        self.root = Path(self.tmp.name)
        self.version_path = write_project_version(self.root)

    def tearDown(self) -> None:
        self.tmp.cleanup()

    def version_reader(self, expected: str = "6000.3.10f1"):
        return lambda _path: (expected, None)

    def test_parse_project_version(self) -> None:
        self.assertEqual("6000.3.10f1", read_project_version(self.version_path))

    def test_cli_precedes_env_and_standard(self) -> None:
        cli = make_executable(self.root / "With Spaces" / "Unity.exe")
        env = make_executable(self.root / "env" / "Unity.exe")
        standard = make_executable(self.root / "standard" / "Unity.exe")
        result = resolve_unity_editor(
            project_version_path=self.version_path,
            unity_editor=str(cli),
            env={"UNITY_EDITOR_PATH": str(env)},
            platform="win32",
            standard_paths=[standard],
            version_reader=self.version_reader(),
        )
        self.assertTrue(result.ok, result.errors)
        self.assertEqual(str(cli), result.editor_path)
        self.assertEqual("cli", result.method)

    def test_env_precedes_standard(self) -> None:
        env = make_executable(self.root / "env" / "Unity.exe")
        standard = make_executable(self.root / "standard" / "Unity.exe")
        result = resolve_unity_editor(
            project_version_path=self.version_path,
            env={"UNITY_EDITOR_PATH": str(env)},
            platform="win32",
            standard_paths=[standard],
            version_reader=self.version_reader(),
        )
        self.assertTrue(result.ok, result.errors)
        self.assertEqual(str(env), result.editor_path)
        self.assertEqual("env", result.method)

    def test_standard_path_can_resolve(self) -> None:
        standard = make_executable(self.root / "standard" / "Unity.exe")
        result = resolve_unity_editor(
            project_version_path=self.version_path,
            env={},
            platform="win32",
            standard_paths=[standard],
            version_reader=self.version_reader(),
        )
        self.assertTrue(result.ok, result.errors)
        self.assertEqual("standard", result.method)

    def test_version_mismatch_fails(self) -> None:
        editor = make_executable(self.root / "Unity.exe")
        result = resolve_unity_editor(
            project_version_path=self.version_path,
            unity_editor=str(editor),
            env={},
            platform="win32",
            standard_paths=[],
            version_reader=self.version_reader("6000.3.9f1"),
        )
        self.assertFalse(result.ok)
        self.assertIn("version mismatch", "\n".join(result.errors))

    def test_version_reader_failure_is_actionable(self) -> None:
        editor = make_executable(self.root / "Unity.exe")
        result = resolve_unity_editor(
            project_version_path=self.version_path,
            unity_editor=str(editor),
            env={},
            platform="win32",
            standard_paths=[],
            version_reader=lambda _path: (None, "mock process failure"),
        )
        self.assertFalse(result.ok)
        self.assertIn("could not verify Unity version", "\n".join(result.errors))
        self.assertNotIn("Traceback", "\n".join(result.errors))

    def test_nonexistent_path_fails(self) -> None:
        result = resolve_unity_editor(
            project_version_path=self.version_path,
            unity_editor=str(self.root / "missing" / "Unity.exe"),
            env={},
            platform="win32",
            standard_paths=[],
            version_reader=self.version_reader(),
        )
        self.assertFalse(result.ok)
        self.assertIn("not an executable file", "\n".join(result.errors))

    def test_no_installation_found_reports_guidance(self) -> None:
        result = resolve_unity_editor(
            project_version_path=self.version_path,
            env={},
            platform="win32",
            standard_paths=[],
            version_reader=self.version_reader(),
        )
        self.assertFalse(result.ok)
        self.assertIn("--unity-editor", "\n".join(result.errors))

    def test_standard_paths_by_platform(self) -> None:
        version = "6000.3.10f1"
        self.assertIn(r"C:\Program Files\Unity\Hub\Editor\6000.3.10f1\Editor\Unity.exe", str(standard_unity_paths(version, "win32")[0]))
        self.assertEqual(Path("/Applications/Unity/Hub/Editor/6000.3.10f1/Unity.app/Contents/MacOS/Unity"), standard_unity_paths(version, "darwin")[0])
        linux_home = self.root / "home with spaces"
        linux_paths = standard_unity_paths(version, "linux", linux_home)
        self.assertIn(Path("/opt/unity/editor/6000.3.10f1/Editor/Unity"), linux_paths)
        self.assertIn(Path("/opt/Unity/Hub/Editor/6000.3.10f1/Editor/Unity"), linux_paths)
        self.assertIn(linux_home / "Unity" / "Hub" / "Editor" / version / "Editor" / "Unity", linux_paths)

    def test_linux_home_standard_path_can_resolve_with_injected_home(self) -> None:
        editor = make_executable(self.root / "home" / "Unity" / "Hub" / "Editor" / "6000.3.10f1" / "Editor" / "Unity")
        with mock.patch.object(find_unity, "is_executable_file", side_effect=lambda path, platform=None: Path(path) == editor):
            result = resolve_unity_editor(
                project_version_path=self.version_path,
                env={},
                platform="linux",
                home=self.root / "home",
                version_reader=self.version_reader(),
            )
        self.assertTrue(result.ok, result.errors)
        self.assertEqual(str(editor), result.editor_path)
        self.assertEqual("standard", result.method)

    def test_find_unity_cli_json_error_works_from_different_cwd(self) -> None:
        result = subprocess.run(
            [sys.executable, str(ROOT / "scripts" / "find_unity.py"), "--unity-editor", str(self.root / "missing" / "Unity.exe"), "--json"],
            cwd=self.root,
            capture_output=True,
            text=True,
            shell=False,
        )
        self.assertNotEqual(0, result.returncode)
        data = json.loads(result.stdout)
        self.assertFalse(data["ok"])
        self.assertEqual("6000.3.10f1", data["expected_version"])
        self.assertNotIn("Traceback", result.stdout + result.stderr)


class RunUnityEditModeTest(unittest.TestCase):
    def setUp(self) -> None:
        self.tmp = tempfile.TemporaryDirectory()
        self.root = Path(self.tmp.name)
        write_project_version(self.root)
        self.editor = make_executable(self.root / "Unity.exe")
        self.result_path = self.root / "Temp" / "HeadlessTests" / "results.xml"
        self.log_path = self.root / "Temp" / "HeadlessTests" / "editmode.log"

    def tearDown(self) -> None:
        self.tmp.cleanup()

    def resolver(self, ok: bool = True):
        errors = [] if ok else ["Unity Editor not found for expected version 6000.3.10f1"]
        return lambda **_kwargs: __import__("scripts.find_unity", fromlist=["UnityResolution"]).UnityResolution(
            ok,
            "6000.3.10f1",
            str(self.editor) if ok else None,
            "6000.3.10f1" if ok else None,
            "cli" if ok else None,
            [str(self.editor)],
            errors,
        )

    def write_xml(self, total: int, passed: int, failed: int, skipped: int = 0) -> None:
        self.result_path.parent.mkdir(parents=True, exist_ok=True)
        self.result_path.write_text(
            f'<test-run total="{total}" passed="{passed}" failed="{failed}" skipped="{skipped}" duration="0.01" />',
            encoding="utf-8",
        )
        current_time = time.time() + 1
        os.utime(self.result_path, (current_time, current_time))

    def test_build_command_contains_required_flags(self) -> None:
        command = build_unity_command(str(self.editor), self.root, self.result_path, self.log_path)
        self.assertIn("-batchmode", command)
        self.assertIn("-nographics", command)
        self.assertIn("-runTests", command)
        self.assertIn("-testPlatform", command)
        self.assertIn("editmode", command)
        self.assertNotIn("-accept-apiupdate", command)
        self.assertNotIn("-quit", command)
        self.assertEqual(str(self.result_path.resolve()), command[command.index("-testResults") + 1])
        self.assertEqual(str(self.log_path.resolve()), command[command.index("-logFile") + 1])

    def test_default_output_directory_is_outside_project_root(self) -> None:
        output_dir, result_path, log_path, json_path = default_output_paths("test-run")
        self.assertFalse(str(output_dir).startswith(str(ROOT.resolve())))
        self.assertEqual(output_dir / "editmode-results.xml", result_path)
        self.assertEqual(output_dir / "editmode.log", log_path)
        self.assertEqual(output_dir / "editmode-run.json", json_path)

    def test_command_uses_absolute_paths_with_spaces(self) -> None:
        root_with_spaces = self.root / "Project With Spaces"
        result_with_spaces = self.root / "Output With Spaces" / "results.xml"
        log_with_spaces = self.root / "Output With Spaces" / "editmode.log"
        command = build_unity_command(str(self.editor), root_with_spaces, result_with_spaces, log_with_spaces)
        self.assertEqual(str(root_with_spaces.resolve()), command[command.index("-projectPath") + 1])
        self.assertEqual(str(result_with_spaces.resolve()), command[command.index("-testResults") + 1])
        self.assertEqual(str(log_with_spaces.resolve()), command[command.index("-logFile") + 1])

    def test_parse_xml_pass_and_fail(self) -> None:
        self.write_xml(3, 3, 0)
        parsed, errors = parse_results_xml(self.result_path)
        self.assertEqual([], errors)
        self.assertEqual(3, parsed["total"])
        self.write_xml(3, 2, 1)
        parsed, errors = parse_results_xml(self.result_path)
        self.assertIn("failed tests", "\n".join(errors))
        self.assertEqual(1, parsed["failed"])

    def test_malformed_xml_fails(self) -> None:
        self.result_path.parent.mkdir(parents=True, exist_ok=True)
        self.result_path.write_text("<test-run", encoding="utf-8")
        parsed, errors = parse_results_xml(self.result_path)
        self.assertIsNone(parsed)
        self.assertIn("invalid", "\n".join(errors))

    def test_wrong_root_fails(self) -> None:
        self.result_path.parent.mkdir(parents=True, exist_ok=True)
        self.result_path.write_text('<test-suite total="1" passed="1" failed="0" skipped="0" />', encoding="utf-8")
        parsed, errors = parse_results_xml(self.result_path)
        self.assertIsNone(parsed)
        self.assertIn("root must be test-run", "\n".join(errors))

    def test_required_counter_missing_fails(self) -> None:
        for xml in (
            '<test-run passed="1" failed="0" skipped="0" />',
            '<test-run total="1" failed="0" skipped="0" />',
            '<test-run total="1" passed="1" skipped="0" />',
            '<test-run total="1" passed="1" failed="0" />',
        ):
            with self.subTest(xml=xml):
                self.result_path.parent.mkdir(parents=True, exist_ok=True)
                self.result_path.write_text(xml, encoding="utf-8")
                parsed, errors = parse_results_xml(self.result_path)
                self.assertIsNotNone(parsed)
                self.assertIn("missing required attribute", "\n".join(errors))

    def test_non_integer_counter_fails(self) -> None:
        self.result_path.parent.mkdir(parents=True, exist_ok=True)
        self.result_path.write_text('<test-run total="one" passed="1" failed="0" skipped="0" />', encoding="utf-8")
        parsed, errors = parse_results_xml(self.result_path)
        self.assertIsNotNone(parsed)
        self.assertIn("not an integer", "\n".join(errors))

    def test_negative_counter_fails(self) -> None:
        self.result_path.parent.mkdir(parents=True, exist_ok=True)
        self.result_path.write_text('<test-run total="1" passed="-1" failed="0" skipped="0" />', encoding="utf-8")
        parsed, errors = parse_results_xml(self.result_path)
        self.assertIsNotNone(parsed)
        self.assertIn("negative", "\n".join(errors))

    def test_contradictory_totals_fail(self) -> None:
        self.result_path.parent.mkdir(parents=True, exist_ok=True)
        self.result_path.write_text('<test-run total="1" passed="1" failed="1" skipped="0" />', encoding="utf-8")
        parsed, errors = parse_results_xml(self.result_path)
        self.assertIsNotNone(parsed)
        self.assertIn("incomplete or contradictory", "\n".join(errors))

    def test_incomplete_totals_fail(self) -> None:
        self.result_path.parent.mkdir(parents=True, exist_ok=True)
        self.result_path.write_text('<test-run total="3" passed="1" failed="0" skipped="0" />', encoding="utf-8")
        parsed, errors = parse_results_xml(self.result_path)
        self.assertIsNotNone(parsed)
        self.assertIn("incomplete or contradictory", "\n".join(errors))

    def test_inconclusive_counter_fails(self) -> None:
        self.result_path.parent.mkdir(parents=True, exist_ok=True)
        self.result_path.write_text('<test-run total="1" passed="0" failed="0" skipped="0" inconclusive="1" />', encoding="utf-8")
        parsed, errors = parse_results_xml(self.result_path)
        self.assertEqual(1, parsed["inconclusive"])
        self.assertIn("inconclusive tests", "\n".join(errors))

    def test_inconclusive_result_fails(self) -> None:
        self.result_path.parent.mkdir(parents=True, exist_ok=True)
        self.result_path.write_text('<test-run total="1" passed="1" failed="0" skipped="0" result="Inconclusive" />', encoding="utf-8")
        parsed, errors = parse_results_xml(self.result_path)
        self.assertIsNotNone(parsed)
        self.assertIn("result=Inconclusive", "\n".join(errors))

    def test_error_result_fails(self) -> None:
        self.result_path.parent.mkdir(parents=True, exist_ok=True)
        self.result_path.write_text('<test-run total="1" passed="1" failed="0" skipped="0" result="Error" />', encoding="utf-8")
        parsed, errors = parse_results_xml(self.result_path)
        self.assertIsNotNone(parsed)
        self.assertIn("result=Error", "\n".join(errors))

    def test_cancelled_or_error_result_fails_even_without_failed_count(self) -> None:
        for value in ("Cancelled", "Canceled"):
            with self.subTest(value=value):
                self.result_path.parent.mkdir(parents=True, exist_ok=True)
                self.result_path.write_text(f'<test-run total="1" passed="1" failed="0" skipped="0" result="{value}" />', encoding="utf-8")
                parsed, errors = parse_results_xml(self.result_path)
                self.assertEqual(0, parsed["failed"])
                self.assertIn(value, "\n".join(errors))

    def test_valid_xml_with_all_counters_passes(self) -> None:
        self.result_path.parent.mkdir(parents=True, exist_ok=True)
        self.result_path.write_text(
            '<test-run total="3" passed="2" failed="0" skipped="1" inconclusive="0" asserts="5" duration="0.1" />',
            encoding="utf-8",
        )
        parsed, errors = parse_results_xml(self.result_path)
        self.assertEqual([], errors)
        self.assertEqual({"total": 3, "passed": 2, "failed": 0, "skipped": 1, "inconclusive": 0, "asserts": 5, "duration": "0.1"}, parsed)

    def test_missing_xml_fails(self) -> None:
        result = run_editmode(
            project_root=self.root,
            unity_editor=str(self.editor),
            result_path=self.result_path,
            log_path=self.log_path,
            timeout_seconds=1,
            resolver=self.resolver(),
            process_runner=lambda *_args, **_kwargs: subprocess.CompletedProcess([], 0, "", ""),
        )
        self.assertEqual("FAIL", result["overall_status"])
        self.assertIn("not generated", "\n".join(result["errors"]))

    def test_parent_directories_are_created(self) -> None:
        nested_result = self.root / "missing" / "nested" / "results.xml"
        nested_log = self.root / "missing" / "nested" / "editmode.log"

        def runner(*_args, **_kwargs):
            nested_result.write_text('<test-run total="1" passed="1" failed="0" skipped="0" />', encoding="utf-8")
            current_time = time.time() + 1
            os.utime(nested_result, (current_time, current_time))
            return subprocess.CompletedProcess([], 0, "", "")

        result = run_editmode(
            project_root=self.root,
            unity_editor=str(self.editor),
            result_path=nested_result,
            log_path=nested_log,
            timeout_seconds=1,
            resolver=self.resolver(),
            process_runner=runner,
        )
        self.assertEqual("PASS", result["overall_status"], result["errors"])
        self.assertTrue(nested_result.parent.is_dir())

    def test_old_xml_is_rejected(self) -> None:
        self.write_xml(1, 1, 0)
        old_time = self.result_path.stat().st_mtime - 100
        os.utime(self.result_path, (old_time, old_time))
        min_mtime = self.result_path.stat().st_mtime + 1
        parsed, errors = parse_results_xml(self.result_path, min_mtime_epoch=min_mtime)
        self.assertIsNone(parsed)
        self.assertIn("older than the current run", "\n".join(errors))

        result = run_editmode(
            project_root=self.root,
            unity_editor=str(self.editor),
            result_path=self.result_path,
            log_path=self.log_path,
            timeout_seconds=1,
            resolver=self.resolver(),
            process_runner=lambda *_args, **_kwargs: subprocess.CompletedProcess([], 0, "", ""),
        )
        self.assertEqual("FAIL", result["overall_status"])
        self.assertIn("older than the current run", "\n".join(result["errors"]))

    def test_current_valid_xml_is_accepted(self) -> None:
        def runner(*_args, **_kwargs):
            self.write_xml(1, 1, 0)
            return subprocess.CompletedProcess([], 0, "", "")

        result = run_editmode(
            project_root=self.root,
            unity_editor=str(self.editor),
            result_path=self.result_path,
            log_path=self.log_path,
            timeout_seconds=1,
            resolver=self.resolver(),
            process_runner=runner,
        )
        self.assertEqual("PASS", result["overall_status"], result["errors"])
        self.assertEqual(1, result["results"]["total"])

    def test_zero_tests_fails(self) -> None:
        def runner(*_args, **_kwargs):
            self.write_xml(0, 0, 0)
            return subprocess.CompletedProcess([], 0, "", "")

        result = run_editmode(
            project_root=self.root,
            unity_editor=str(self.editor),
            result_path=self.result_path,
            log_path=self.log_path,
            timeout_seconds=1,
            resolver=self.resolver(),
            process_runner=runner,
        )
        self.assertEqual("FAIL", result["overall_status"])
        self.assertIn("zero tests", "\n".join(result["errors"]))

    def test_failed_test_fails(self) -> None:
        def runner(*_args, **_kwargs):
            self.write_xml(2, 1, 1)
            self.log_path.write_text("Failed test example\n", encoding="utf-8")
            return subprocess.CompletedProcess([], 0, "", "")

        result = run_editmode(
            project_root=self.root,
            unity_editor=str(self.editor),
            result_path=self.result_path,
            log_path=self.log_path,
            timeout_seconds=1,
            resolver=self.resolver(),
            process_runner=runner,
        )
        self.assertEqual("FAIL", result["overall_status"])
        self.assertIn("failed tests", "\n".join(result["errors"]))
        self.assertIn("Failed test example", result["log_excerpt"])

    def test_timeout_fails(self) -> None:
        def runner(*_args, **_kwargs):
            raise subprocess.TimeoutExpired(cmd=["Unity"], timeout=1, output="out", stderr="err")

        result = run_editmode(
            project_root=self.root,
            unity_editor=str(self.editor),
            result_path=self.result_path,
            log_path=self.log_path,
            timeout_seconds=1,
            resolver=self.resolver(),
            process_runner=runner,
        )
        self.assertEqual("FAIL", result["overall_status"])
        self.assertTrue(result["timed_out"])
        self.assertIn("timed out", "\n".join(result["errors"]))

    def test_non_positive_timeout_fails_without_launching(self) -> None:
        called = False

        def runner(*_args, **_kwargs):
            nonlocal called
            called = True
            return subprocess.CompletedProcess([], 0, "", "")

        result = run_editmode(
            project_root=self.root,
            unity_editor=str(self.editor),
            result_path=self.result_path,
            log_path=self.log_path,
            timeout_seconds=0,
            resolver=self.resolver(),
            process_runner=runner,
        )
        self.assertEqual("FAIL", result["overall_status"])
        self.assertFalse(called)
        self.assertIn("timeout_seconds must be positive", "\n".join(result["errors"]))

    def test_discovery_error_fails_without_launching(self) -> None:
        called = False

        def runner(*_args, **_kwargs):
            nonlocal called
            called = True
            return subprocess.CompletedProcess([], 0, "", "")

        result = run_editmode(
            project_root=self.root,
            unity_editor=str(self.editor),
            result_path=self.result_path,
            log_path=self.log_path,
            timeout_seconds=1,
            resolver=self.resolver(False),
            process_runner=runner,
        )
        self.assertEqual("FAIL", result["overall_status"])
        self.assertFalse(called)

    def test_run_unity_process_normal_completion(self) -> None:
        class FakePopen:
            pid = 123
            returncode = 0

            def communicate(self, timeout=None):
                self.timeout = timeout
                return "out", "err"

        fake = FakePopen()
        with mock.patch.object(unity_runner.subprocess, "Popen", return_value=fake):
            result = run_unity_process(["Unity"], self.root, 9)
        self.assertEqual(0, result.returncode)
        self.assertEqual("out", result.stdout)
        self.assertEqual(9, fake.timeout)

    def test_posix_timeout_exits_during_grace_period(self) -> None:
        class FakeProcess:
            pid = 321

            def wait(self, timeout=None):
                self.wait_timeout = timeout

        sent: list[tuple[int, int]] = []
        fake = FakeProcess()
        with mock.patch.object(unity_runner.sys, "platform", "linux"), mock.patch.object(unity_runner.os, "killpg", side_effect=lambda pid, sig: sent.append((pid, sig)), create=True):
            messages = terminate_process_tree(fake, grace_seconds=2, final_wait_seconds=2)
        self.assertEqual([], messages)
        self.assertEqual([(321, unity_runner.signal.SIGTERM)], sent)
        self.assertEqual(2, fake.wait_timeout)

    def test_posix_timeout_escalates_to_sigkill(self) -> None:
        class FakeProcess:
            pid = 654

            def wait(self, timeout=None):
                raise subprocess.TimeoutExpired(cmd=["Unity"], timeout=timeout)

        sent: list[tuple[int, int]] = []
        with (
            mock.patch.object(unity_runner.sys, "platform", "linux"),
            mock.patch.object(unity_runner.signal, "SIGKILL", 9, create=True),
            mock.patch.object(unity_runner.os, "killpg", side_effect=lambda pid, sig: sent.append((pid, sig)), create=True),
        ):
            messages = terminate_process_tree(FakeProcess(), grace_seconds=1, final_wait_seconds=1)
        self.assertIn((654, unity_runner.signal.SIGTERM), sent)
        self.assertIn((654, 9), sent)
        self.assertIn("SIGKILL", "\n".join(messages))

    def test_windows_taskkill_failure_uses_targeted_fallback(self) -> None:
        class FakeProcess:
            pid = 987
            terminated = False

            def poll(self):
                return None

            def terminate(self):
                self.terminated = True

        fake = FakeProcess()
        failed_taskkill = subprocess.CompletedProcess(["taskkill"], 1, "", "denied")
        with mock.patch.object(unity_runner.sys, "platform", "win32"), mock.patch.object(unity_runner.subprocess, "run", return_value=failed_taskkill) as run:
            messages = terminate_process_tree(fake, grace_seconds=1, final_wait_seconds=1)
        self.assertEqual(["taskkill", "/PID", "987", "/T", "/F"], run.call_args.args[0])
        self.assertTrue(fake.terminated)
        self.assertIn("taskkill failed", "\n".join(messages))

    def test_timeout_final_collection_is_bounded(self) -> None:
        class FakePopen:
            pid = 111
            returncode = None

            def __init__(self):
                self.timeouts: list[int] = []
                self.calls = 0

            def communicate(self, timeout=None):
                self.timeouts.append(timeout)
                self.calls += 1
                if self.calls == 1:
                    raise subprocess.TimeoutExpired(cmd=["Unity"], timeout=timeout)
                return "late out", "late err"

        fake = FakePopen()
        with mock.patch.object(unity_runner.subprocess, "Popen", return_value=fake), mock.patch.object(unity_runner, "terminate_process_tree", return_value=["terminated"]):
            with self.assertRaises(subprocess.TimeoutExpired) as raised:
                run_unity_process(["Unity"], self.root, 3)
        self.assertEqual([3, 5], fake.timeouts)
        self.assertEqual("late out", raised.exception.stdout)
        self.assertIn("terminated", raised.exception.stderr)


class RunChecksHeadlessOptionsTest(unittest.TestCase):
    def make_repo_fixture(self) -> tempfile.TemporaryDirectory:
        tmp = tempfile.TemporaryDirectory()
        repo = Path(tmp.name) / "repo"
        shutil.copytree(ROOT / "scripts", repo / "scripts")
        shutil.copytree(ROOT / "Assets" / "StreamingAssets" / "content", repo / "Assets" / "StreamingAssets" / "content")
        write_project_version(repo)
        (repo / "tests" / "python").mkdir(parents=True)
        (repo / "tests" / "python" / "test_placeholder.py").write_text(
            "import unittest\n\nclass Placeholder(unittest.TestCase):\n    def test_ok(self):\n        self.assertTrue(True)\n",
            encoding="utf-8",
        )
        return tmp

    def test_include_dotnet_fails_because_fast_path_not_implemented(self) -> None:
        tmp = self.make_repo_fixture()
        try:
            repo = Path(tmp.name) / "repo"
            result = subprocess.run(
                [sys.executable, str(repo / "scripts" / "run_checks.py"), "--include-dotnet"],
                cwd=repo,
                capture_output=True,
                text=True,
                shell=False,
            )
            self.assertNotEqual(0, result.returncode)
            self.assertIn("dotnet_test: FAIL", result.stdout)
        finally:
            tmp.cleanup()

    def test_include_unity_missing_path_fails_with_json(self) -> None:
        tmp = self.make_repo_fixture()
        try:
            repo = Path(tmp.name) / "repo"
            output = Path(tmp.name) / "checks.json"
            result = subprocess.run(
                [
                    sys.executable,
                    str(repo / "scripts" / "run_checks.py"),
                    "--include-unity-editmode",
                    "--unity-editor",
                    str(Path(tmp.name) / "missing" / "Unity.exe"),
                    "--json-output",
                    str(output),
                ],
                cwd=repo,
                capture_output=True,
                text=True,
                shell=False,
            )
            self.assertNotEqual(0, result.returncode)
            data = json.loads(output.read_text(encoding="utf-8"))
            failed = {step["name"]: step for step in data["steps"] if step["status"] == "FAIL"}
            self.assertIn("unity_editmode", failed)
            self.assertIn("Unity Editor discovery failed", failed["unity_editmode"]["stdout"])
            self.assertTrue(data["errors"])
        finally:
            tmp.cleanup()

    def test_unity_editor_without_include_fails_immediately(self) -> None:
        tmp = self.make_repo_fixture()
        try:
            repo = Path(tmp.name) / "repo"
            result = subprocess.run(
                [sys.executable, str(repo / "scripts" / "run_checks.py"), "--unity-editor", str(Path(tmp.name) / "Unity.exe")],
                cwd=repo,
                capture_output=True,
                text=True,
                shell=False,
            )
            self.assertNotEqual(0, result.returncode)
            self.assertIn("--unity-editor requires --include-unity-editmode", result.stderr)
            self.assertNotIn("Overall: PASS", result.stdout)
        finally:
            tmp.cleanup()

    def test_unity_failure_json_has_required_schema(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            repo = Path(tmp) / "repo"
            shutil.copytree(ROOT / "scripts", repo / "scripts")
            write_project_version(repo)
            output = Path(tmp) / "unity.json"
            result = subprocess.run(
                [
                    sys.executable,
                    str(repo / "scripts" / "run_unity_editmode.py"),
                    "--unity-editor",
                    str(Path(tmp) / "missing" / "Unity.exe"),
                    "--json-output",
                    str(output),
                ],
                cwd=repo,
                capture_output=True,
                text=True,
                shell=False,
            )
            self.assertNotEqual(0, result.returncode)
            data = json.loads(output.read_text(encoding="utf-8"))
            for key in (
                "status",
                "exit_code",
                "duration_ms",
                "editor_path",
                "expected_version",
                "detected_version",
                "discovery_source",
                "command",
                "results_path",
                "log_path",
                "total",
                "passed",
                "failed",
                "skipped",
                "stdout",
                "stderr",
                "errors",
            ):
                self.assertIn(key, data)


if __name__ == "__main__":
    unittest.main()
