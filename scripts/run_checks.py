#!/usr/bin/env python3
"""Canonical cross-platform repository checks.

Run from anywhere:
  python scripts/run_checks.py
"""

from __future__ import annotations

import argparse
import json
import subprocess
import sys
import tempfile
import time
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

ROOT = Path(__file__).resolve().parents[1]
CONTENT_DIR = ROOT / "Assets" / "StreamingAssets" / "content"


def utc_now() -> str:
    return datetime.now(timezone.utc).isoformat(timespec="milliseconds").replace("+00:00", "Z")


def duration_ms(start: float) -> int:
    return int((time.perf_counter() - start) * 1000)


def check_json_syntax() -> tuple[int, str, str]:
    errors: list[str] = []
    for path in sorted(CONTENT_DIR.rglob("*.json")):
        try:
            json.loads(path.read_text(encoding="utf-8"))
        except Exception as exc:  # noqa: BLE001 - keep runner dependency-free and exhaustive.
            errors.append(f"{path.relative_to(ROOT).as_posix()}: {exc}")
    stdout = "OK: JSON syntax valid." if not errors else "\n".join(errors)
    return (0 if not errors else 1, stdout, "")


def run_process(args: list[str]) -> tuple[int | None, str, str]:
    try:
        result = subprocess.run(args, cwd=ROOT, capture_output=True, text=True, shell=False)
    except OSError as exc:
        return None, "", str(exc)
    return result.returncode, result.stdout, result.stderr


def make_step(name: str) -> dict[str, Any]:
    return {"name": name, "status": "PENDING", "exit_code": None, "duration_ms": 0, "stdout": "", "stderr": ""}


def finish_step(step: dict[str, Any], exit_code: int | None, start: float, *, stdout: str = "", stderr: str = "") -> dict[str, Any]:
    step["exit_code"] = exit_code
    step["duration_ms"] = duration_ms(start)
    step["status"] = "PASS" if exit_code == 0 else "FAIL"
    step["stdout"] = stdout or ""
    step["stderr"] = stderr or ""
    return step


def append_process_error(errors: list[str], name: str, exit_code: int | None, stderr: str) -> int:
    if exit_code is None:
        errors.append(f"{name}: could not start process: {stderr}")
        return 1
    if exit_code != 0:
        errors.append(f"{name}: exited with code {exit_code}")
    return exit_code


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--base-ref")
    parser.add_argument("--head-ref")
    parser.add_argument("--working-tree", action="store_true")
    parser.add_argument("--include-dotnet", action="store_true")
    parser.add_argument("--include-unity-editmode", action="store_true")
    parser.add_argument("--include-unity-scenario", action="store_true")
    parser.add_argument("--scenario", type=Path)
    parser.add_argument("--unity-editor")
    parser.add_argument("--json-output", type=Path)
    args = parser.parse_args(argv)
    if args.working_tree and not args.base_ref:
        parser.error("--working-tree requires --base-ref")
    if args.working_tree and args.head_ref:
        parser.error("--working-tree cannot be combined with --head-ref")
    if args.scenario and not args.include_unity_scenario:
        parser.error("--scenario requires --include-unity-scenario")
    if args.unity_editor and not (args.include_unity_editmode or args.include_unity_scenario):
        parser.error("--unity-editor requires --include-unity-editmode or --include-unity-scenario")

    started_at = utc_now()
    overall_start = time.perf_counter()
    steps: list[dict[str, Any]] = []
    skipped_checks: list[dict[str, str]] = []
    errors: list[str] = []

    step = make_step("json_syntax")
    start = time.perf_counter()
    code, stdout, stderr = check_json_syntax()
    if code != 0:
        errors.append(f"json_syntax: exited with code {code}")
    steps.append(finish_step(step, code, start, stdout=stdout, stderr=stderr))

    commands: list[tuple[str, list[str]]] = [
        ("verify_manifest_hashes", [sys.executable, str(ROOT / "scripts" / "verify_manifest_hashes.py")]),
        ("validate_content", [sys.executable, str(ROOT / "scripts" / "validate_content.py")]),
        ("smoke_simulation", [sys.executable, str(ROOT / "scripts" / "smoke_simulation.py")]),
        ("python_unittest", [sys.executable, "-m", "unittest", "discover", "-s", str(ROOT / "tests" / "python"), "-p", "test_*.py"]),
    ]

    for name, command in commands:
        step = make_step(name)
        start = time.perf_counter()
        code, stdout, stderr = run_process(command)
        code = append_process_error(errors, name, code, stderr)
        steps.append(finish_step(step, code, start, stdout=stdout, stderr=stderr))

    if args.base_ref:
        head_ref = args.head_ref or "HEAD"
        step = make_step("check_manifest_bump")
        start = time.perf_counter()
        command = [sys.executable, str(ROOT / "scripts" / "check_manifest_bump.py"), "--base", args.base_ref]
        if args.working_tree:
            command.append("--working-tree")
        else:
            command.extend(["--head", head_ref])
        code, stdout, stderr = run_process(command)
        code = append_process_error(errors, "check_manifest_bump", code, stderr)
        steps.append(finish_step(step, code, start, stdout=stdout, stderr=stderr))
    else:
        skipped_checks.append({"name": "check_manifest_bump", "reason": "--base-ref was not provided"})

    if args.include_dotnet:
        step = make_step("dotnet_test")
        start = time.perf_counter()
        stdout = "NOT IMPLEMENTED: .NET fast path was not added because dotnet SDK was unavailable during PR 2 implementation."
        code = 1
        errors.append("dotnet_test: .NET fast path is not implemented in this branch")
        steps.append(finish_step(step, code, start, stdout=stdout, stderr=""))
    else:
        skipped_checks.append({"name": "dotnet_test", "reason": "--include-dotnet was not provided"})

    if args.include_unity_editmode:
        step = make_step("unity_editmode")
        start = time.perf_counter()
        command = [sys.executable, str(ROOT / "scripts" / "run_unity_editmode.py")]
        if args.unity_editor:
            command.extend(["--unity-editor", args.unity_editor])
        code, stdout, stderr = run_process(command)
        code = append_process_error(errors, "unity_editmode", code, stderr)
        steps.append(finish_step(step, code, start, stdout=stdout, stderr=stderr))
    else:
        skipped_checks.append({"name": "unity_editmode", "reason": "--include-unity-editmode was not provided"})

    if args.include_unity_scenario:
        step = make_step("unity_scenario")
        start = time.perf_counter()
        scenario = args.scenario or (ROOT / "tests" / "scenarios" / "smoke_v1.json")
        output = Path(tempfile.gettempdir()) / "VictoriantChile" / "ScenarioRunner" / "run-checks-scenario.json"
        command = [sys.executable, str(ROOT / "scripts" / "run_scenario.py"), "--scenario", str(scenario), "--json-output", str(output)]
        if args.unity_editor:
            command.extend(["--unity-editor", args.unity_editor])
        code, stdout, stderr = run_process(command)
        code = append_process_error(errors, "unity_scenario", code, stderr)
        steps.append(finish_step(step, code, start, stdout=stdout, stderr=stderr))
    else:
        skipped_checks.append({"name": "unity_scenario", "reason": "--include-unity-scenario was not provided"})

    mandatory_failed = any(step["status"] != "PASS" for step in steps if step["name"] != "check_manifest_bump")
    optional_failed = any(step["name"] == "check_manifest_bump" and step["status"] != "PASS" for step in steps)
    overall_status = "FAIL" if mandatory_failed or optional_failed or errors else "PASS"

    result: dict[str, Any] = {
        "overall_status": overall_status,
        "started_at": started_at,
        "duration_ms": duration_ms(overall_start),
        "steps": [
            {
                "name": step["name"],
                "status": step["status"],
                "exit_code": step["exit_code"],
                "duration_ms": step["duration_ms"],
                "stdout": step["stdout"],
                "stderr": step["stderr"],
            }
            for step in steps
        ],
        "skipped_checks": skipped_checks,
        "errors": errors,
    }

    print("Repository checks")
    for step in steps:
        print(f"- {step['name']}: {step['status']} ({step['duration_ms']} ms)")
        if step["status"] != "PASS":
            if step.get("stdout"):
                print(step["stdout"])
            if step.get("stderr"):
                print(step["stderr"])
    for skipped in skipped_checks:
        print(f"- {skipped['name']}: SKIP ({skipped['reason']})")
    print(f"Overall: {overall_status}")

    if args.json_output:
        output_path = args.json_output
        if not output_path.is_absolute():
            output_path = (Path.cwd() / output_path).resolve()
        output_path.parent.mkdir(parents=True, exist_ok=True)
        output_path.write_text(json.dumps(result, indent=2, sort_keys=True) + "\n", encoding="utf-8")

    return 0 if overall_status == "PASS" else 1


if __name__ == "__main__":
    sys.exit(main())
