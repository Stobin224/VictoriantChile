#!/usr/bin/env python3
"""Run Unity EditMode tests in batch/headless mode and parse the result XML."""

from __future__ import annotations

import argparse
import json
import os
import signal
import subprocess
import sys
import tempfile
import time
import uuid
import xml.etree.ElementTree as ET
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Callable

ROOT = Path(__file__).resolve().parents[1]
DEFAULT_OUTPUT_BASE = Path(tempfile.gettempdir()) / "VictoriantChile" / "HeadlessTests"

if str(ROOT) not in sys.path:
    sys.path.insert(0, str(ROOT))

from scripts.find_unity import UnityResolution, resolve_unity_editor  # noqa: E402


def utc_now() -> str:
    return datetime.now(timezone.utc).isoformat(timespec="milliseconds").replace("+00:00", "Z")


def duration_ms(start: float) -> int:
    return int((time.perf_counter() - start) * 1000)


def build_unity_command(editor_path: str, project_root: Path, result_path: Path, log_path: Path) -> list[str]:
    return [
        editor_path,
        "-batchmode",
        "-nographics",
        "-projectPath",
        str(project_root.resolve()),
        "-runTests",
        "-testPlatform",
        "editmode",
        "-testResults",
        str(result_path.resolve()),
        "-logFile",
        str(log_path.resolve()),
    ]


def make_run_id() -> str:
    timestamp = datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%S%fZ")
    return f"{timestamp}-{uuid.uuid4().hex[:8]}"


def default_output_paths(run_id: str | None = None) -> tuple[Path, Path, Path, Path]:
    run_id = run_id or make_run_id()
    output_dir = (DEFAULT_OUTPUT_BASE / run_id).resolve()
    return (
        output_dir,
        output_dir / "editmode-results.xml",
        output_dir / "editmode.log",
        output_dir / "editmode-run.json",
    )


def parse_int_attr(node: ET.Element, names: tuple[str, ...]) -> int | None:
    for name in names:
        value = node.attrib.get(name)
        if value is not None:
            try:
                return int(value)
            except ValueError:
                return None
    return None


def parse_float_attr(node: ET.Element, names: tuple[str, ...]) -> str | None:
    for name in names:
        value = node.attrib.get(name)
        if value is not None:
            return value
    return None


def parse_results_xml(path: Path, *, min_mtime_epoch: float | None = None) -> tuple[dict[str, Any] | None, list[str]]:
    if not path.exists():
        return None, [f"test result XML was not generated: {path}"]
    if min_mtime_epoch is not None and path.stat().st_mtime < min_mtime_epoch:
        return None, [f"test result XML is older than the current run: {path}"]
    try:
        root = ET.parse(path).getroot()
    except ET.ParseError as exc:
        return None, [f"test result XML is invalid: {exc}"]

    errors: list[str] = []
    total = parse_int_attr(root, ("total", "testcasecount"))
    passed = parse_int_attr(root, ("passed",))
    failed = parse_int_attr(root, ("failed", "failures"))
    skipped = parse_int_attr(root, ("skipped", "ignored"))
    inconclusive = parse_int_attr(root, ("inconclusive",)) or 0
    asserts = parse_int_attr(root, ("asserts",)) or 0
    duration = parse_float_attr(root, ("duration", "time"))
    result_state = root.attrib.get("result", "")
    label = root.attrib.get("label", "")

    if total is None:
        passed_cases = len(root.findall(".//test-case[@result='Passed']"))
        failed_cases = len(root.findall(".//test-case[@result='Failed']"))
        error_cases = len(root.findall(".//test-case[@result='Error']"))
        cancelled_cases = len(root.findall(".//test-case[@result='Cancelled']"))
        inconclusive_cases = len(root.findall(".//test-case[@result='Inconclusive']"))
        skipped_cases = len(root.findall(".//test-case[@result='Skipped']")) + len(root.findall(".//test-case[@result='Ignored']"))
        total = passed_cases + failed_cases + skipped_cases
        passed = passed if passed is not None else passed_cases
        failed = failed if failed is not None else failed_cases + error_cases + cancelled_cases
        skipped = skipped if skipped is not None else skipped_cases
        inconclusive = inconclusive_cases

    total = total if total is not None else 0
    passed = passed if passed is not None else 0
    failed = failed if failed is not None else 0
    skipped = skipped if skipped is not None else 0
    if result_state in {"Failed", "Error", "Cancelled"} and failed == 0:
        errors.append(f"Unity test run result is {result_state} with failed=0")
        failed = 1
    if label in {"Cancelled", "Error"} and failed == 0:
        errors.append(f"Unity test run label is {label} with failed=0")
        failed = 1
    if any(value < 0 for value in (total, passed, failed, skipped, inconclusive)):
        errors.append("test result XML contains negative counters")
    if total > 0 and passed + failed + skipped + inconclusive > total:
        errors.append(
            "test result XML counters are contradictory: "
            f"total={total}, passed={passed}, failed={failed}, skipped={skipped}, inconclusive={inconclusive}"
        )

    return {
        "total": total if total is not None else 0,
        "passed": passed,
        "failed": failed,
        "skipped": skipped + inconclusive,
        "inconclusive": inconclusive,
        "asserts": asserts,
        "duration": duration or "",
    }, errors


def log_excerpt(path: Path, max_lines: int = 60) -> str:
    if not path.exists():
        return ""
    lines = path.read_text(encoding="utf-8", errors="replace").splitlines()
    interesting = [
        line
        for line in lines
        if any(marker in line.lower() for marker in ("error", "exception", "failed", "compilation"))
    ]
    selected = (interesting[-max_lines:] if interesting else lines[-max_lines:])
    return "\n".join(selected)


def make_result(started_at: str, start: float) -> dict[str, Any]:
    return {
        "overall_status": "PENDING",
        "status": "PENDING",
        "started_at": started_at,
        "duration_ms": duration_ms(start),
        "unity_editor": None,
        "editor_path": None,
        "expected_version": None,
        "detected_version": None,
        "discovery_source": None,
        "command": [],
        "exit_code": None,
        "timed_out": False,
        "output_dir": "",
        "json_path": "",
        "results": {"total": 0, "passed": 0, "failed": 0, "skipped": 0, "inconclusive": 0, "asserts": 0, "duration": ""},
        "xml_path": "",
        "results_path": "",
        "log_path": "",
        "total": 0,
        "passed": 0,
        "failed": 0,
        "skipped": 0,
        "stdout": "",
        "stderr": "",
        "errors": [],
        "log_excerpt": "",
    }


def run_editmode(
    *,
    project_root: Path,
    unity_editor: str | None,
    result_path: Path,
    log_path: Path,
    timeout_seconds: int,
    resolver: Callable[..., UnityResolution] = resolve_unity_editor,
    process_runner: Callable[[list[str], Path, int], subprocess.CompletedProcess[str]] | None = None,
) -> dict[str, Any]:
    started_at = utc_now()
    start = time.perf_counter()
    result = make_result(started_at, start)
    result["output_dir"] = str(result_path.resolve().parent)
    result["xml_path"] = str(result_path.resolve())
    result["results_path"] = str(result_path.resolve())
    result["log_path"] = str(log_path.resolve())
    if timeout_seconds <= 0:
        result["errors"].append("timeout_seconds must be positive")
        result["overall_status"] = "FAIL"
        result["status"] = "FAIL"
        result["duration_ms"] = duration_ms(start)
        return result

    resolution = resolver(unity_editor=unity_editor)
    result["expected_version"] = resolution.expected_version
    result["unity_editor"] = resolution.editor_path
    result["editor_path"] = resolution.editor_path
    result["detected_version"] = resolution.detected_version
    result["discovery_source"] = resolution.method
    if not resolution.ok or not resolution.editor_path:
        result["errors"].append("Unity Editor discovery failed")
        result["errors"].extend(resolution.errors)
        result["overall_status"] = "FAIL"
        result["status"] = "FAIL"
        result["duration_ms"] = duration_ms(start)
        return result

    result_path.parent.mkdir(parents=True, exist_ok=True)
    log_path.parent.mkdir(parents=True, exist_ok=True)
    command = build_unity_command(resolution.editor_path, project_root, result_path, log_path)
    result["command"] = command

    launch_start_epoch = time.time()
    try:
        runner = process_runner or run_unity_process
        completed = runner(command, project_root, timeout_seconds)
        result["exit_code"] = completed.returncode
        result["stdout"] = completed.stdout or ""
        result["stderr"] = completed.stderr or ""
        if completed.returncode != 0:
            result["errors"].append(f"Unity exited with code {completed.returncode}")
    except subprocess.TimeoutExpired as exc:
        result["timed_out"] = True
        result["exit_code"] = None
        result["stdout"] = exc.stdout or ""
        result["stderr"] = exc.stderr or ""
        result["errors"].append(f"Unity EditMode run timed out after {timeout_seconds} seconds")
    except OSError as exc:
        result["exit_code"] = None
        result["errors"].append(f"Unity could not start: {exc}")

    parsed, parse_errors = parse_results_xml(result_path, min_mtime_epoch=launch_start_epoch)
    if parse_errors:
        result["errors"].extend(parse_errors)
    elif parsed:
        result["results"] = parsed
        result["total"] = parsed["total"]
        result["passed"] = parsed["passed"]
        result["failed"] = parsed["failed"]
        result["skipped"] = parsed["skipped"]
        if parsed["total"] <= 0:
            result["errors"].append("Unity EditMode run executed zero tests")
        if parsed["failed"] > 0:
            result["errors"].append(f"Unity EditMode run had failed tests: {parsed['failed']}")

    result["log_excerpt"] = log_excerpt(log_path)
    result["overall_status"] = "FAIL" if result["errors"] else "PASS"
    result["status"] = result["overall_status"]
    result["duration_ms"] = duration_ms(start)
    return result


def terminate_process_tree(pid: int) -> None:
    if sys.platform.startswith("win"):
        subprocess.run(["taskkill", "/PID", str(pid), "/T", "/F"], capture_output=True, text=True, shell=False)
        return
    try:
        os.killpg(pid, signal.SIGTERM)
    except OSError:
        pass


def run_unity_process(command: list[str], cwd: Path, timeout_seconds: int) -> subprocess.CompletedProcess[str]:
    if timeout_seconds <= 0:
        raise ValueError("timeout_seconds must be positive")
    popen_kwargs: dict[str, Any] = {
        "cwd": cwd,
        "stdout": subprocess.PIPE,
        "stderr": subprocess.PIPE,
        "text": True,
        "shell": False,
    }
    if sys.platform.startswith("win"):
        popen_kwargs["creationflags"] = subprocess.CREATE_NEW_PROCESS_GROUP
    else:
        popen_kwargs["start_new_session"] = True
    process = subprocess.Popen(command, **popen_kwargs)
    try:
        stdout, stderr = process.communicate(timeout=timeout_seconds)
        return subprocess.CompletedProcess(command, process.returncode, stdout, stderr)
    except subprocess.TimeoutExpired as exc:
        terminate_process_tree(process.pid)
        stdout, stderr = process.communicate()
        exc.stdout = stdout
        exc.stderr = stderr
        raise exc


def print_human(result: dict[str, Any]) -> None:
    print(f"Unity EditMode: {result['overall_status']}")
    print(f"- Output dir: {result['output_dir']}")
    print(f"- XML: {result['xml_path']}")
    print(f"- Log: {result['log_path']}")
    if result.get("json_path"):
        print(f"- JSON: {result['json_path']}")
    print(
        "- Results: "
        f"total={result['results']['total']} "
        f"passed={result['results']['passed']} "
        f"failed={result['results']['failed']} "
        f"skipped={result['results']['skipped']} "
        f"duration={result['results']['duration']}"
    )
    for error in result["errors"]:
        print(f"- ERROR: {error}")
    if result["overall_status"] != "PASS" and result["log_excerpt"]:
        print("Log excerpt:")
        print(result["log_excerpt"])


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--unity-editor")
    parser.add_argument("--timeout-seconds", type=int, default=600)
    parser.add_argument("--test-results", type=Path)
    parser.add_argument("--log-file", type=Path)
    parser.add_argument("--json-output", type=Path)
    args = parser.parse_args(argv)

    output_dir, default_result_path, default_log_path, default_json_path = default_output_paths()
    result_path = (args.test_results or default_result_path).resolve()
    log_path = (args.log_file or default_log_path).resolve()
    json_path = (args.json_output or default_json_path).resolve()

    print("Unity EditMode effective paths")
    print(f"- Output dir: {output_dir if not args.test_results and not args.log_file else result_path.parent}")
    print(f"- XML: {result_path}")
    print(f"- Log: {log_path}")
    print(f"- JSON: {json_path}")

    result = run_editmode(
        project_root=ROOT,
        unity_editor=args.unity_editor,
        result_path=result_path,
        log_path=log_path,
        timeout_seconds=args.timeout_seconds,
    )
    result["json_path"] = str(json_path)
    print_human(result)

    json_path.parent.mkdir(parents=True, exist_ok=True)
    json_path.write_text(json.dumps(result, indent=2, sort_keys=True) + "\n", encoding="utf-8")

    return 0 if result["overall_status"] == "PASS" else 1


if __name__ == "__main__":
    sys.exit(main())
