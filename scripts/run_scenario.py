#!/usr/bin/env python3
"""Run a ScenarioRunner JSON scenario through Unity headless."""

from __future__ import annotations

import argparse
import json
import re
import subprocess
import sys
import tempfile
import time
import uuid
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

ROOT = Path(__file__).resolve().parents[1]
DEFAULT_OUTPUT_BASE = Path(tempfile.gettempdir()) / "VictoriantChile" / "ScenarioRunner"
DEFAULT_CONTENT_ROOT = ROOT / "Assets" / "StreamingAssets" / "content"
DEFAULT_SCENARIO = ROOT / "tests" / "scenarios" / "smoke_v1.json"

if str(ROOT) not in sys.path:
    sys.path.insert(0, str(ROOT))

from scripts.find_unity import resolve_unity_editor  # noqa: E402
from scripts.run_unity_editmode import run_unity_process  # noqa: E402


HASH_RE = re.compile(r"^sha256:[0-9a-f]{64}$")


def utc_now() -> str:
    return datetime.now(timezone.utc).isoformat(timespec="milliseconds").replace("+00:00", "Z")


def duration_ms(start: float) -> int:
    return int((time.perf_counter() - start) * 1000)


def make_run_id() -> str:
    return datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%S%fZ") + "-" + uuid.uuid4().hex[:8]


def resolve_repo_path(path: Path) -> Path:
    return path if path.is_absolute() else (ROOT / path).resolve()


def validate_result_json(path: Path) -> list[str]:
    errors: list[str] = []
    if not path.exists():
        return [f"Scenario JSON output was not generated: {path}"]
    try:
        text = path.read_text(encoding="utf-8")
        data = json.loads(text)
    except Exception as exc:  # noqa: BLE001
        return [f"Scenario JSON output is invalid: {exc}"]
    required = [
        "result_schema_version",
        "status",
        "scenario_schema_version",
        "seed",
        "command_count",
        "commands",
        "state_hash",
        "state",
        "diagnostics",
    ]
    if list(data.keys()) != required:
        errors.append("Scenario JSON top-level keys are not in the stable schema order")
    if data.get("result_schema_version") != 1:
        errors.append("Scenario result_schema_version must be 1")
    if data.get("status") not in {"passed", "failed"}:
        errors.append("Scenario status must be passed or failed")
    if not isinstance(data.get("commands"), list):
        errors.append("Scenario commands must be an array")
    if not isinstance(data.get("diagnostics"), list):
        errors.append("Scenario diagnostics must be an array")
    if isinstance(data.get("commands"), list) and isinstance(data.get("command_count"), int):
        if data["command_count"] != len(data["commands"]):
            errors.append("Scenario command_count must match commands length")
    if data.get("status") == "passed":
        if not HASH_RE.match(str(data.get("state_hash"))):
            errors.append("Scenario state_hash is missing or invalid")
        if not isinstance(data.get("state"), dict):
            errors.append("Scenario state must be an object on success")
    else:
        if data.get("state_hash") is not None:
            errors.append("Scenario state_hash must be null on failure")
        if data.get("state") is not None:
            errors.append("Scenario state must be null on failure")
        if not data.get("diagnostics"):
            errors.append("Scenario failure must include diagnostics")
    return errors


def read_scenario_status(path: Path) -> str | None:
    try:
        data = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError, UnicodeDecodeError):
        return None
    status = data.get("status")
    return status if status in {"passed", "failed"} else None


def build_command(editor: str, scenario: Path, content_root: Path, json_output: Path, log_path: Path) -> list[str]:
    return [
        editor,
        "-batchmode",
        "-nographics",
        "-projectPath",
        str(ROOT),
        "-executeMethod",
        "VictoriantChile.Simulation.Runner.Editor.ScenarioRunnerCommand.Run",
        "-logFile",
        str(log_path),
        "--scenario",
        str(scenario),
        "--content-root",
        str(content_root),
        "--json-output",
        str(json_output),
    ]


def run(args: argparse.Namespace) -> dict[str, Any]:
    started_at = utc_now()
    start = time.perf_counter()
    scenario = resolve_repo_path(args.scenario)
    content_root = resolve_repo_path(args.content_root)
    json_output = args.json_output if args.json_output.is_absolute() else (Path.cwd() / args.json_output).resolve()
    output_dir = DEFAULT_OUTPUT_BASE / make_run_id()
    log_path = output_dir / "scenario.log"
    result: dict[str, Any] = {
        "status": "PENDING",
        "started_at": started_at,
        "duration_ms": 0,
        "unity_editor": None,
        "expected_version": None,
        "detected_version": None,
        "discovery_source": None,
        "command": [],
        "exit_code": None,
        "scenario": str(scenario),
        "content_root": str(content_root),
        "json_output": str(json_output),
        "log_path": str(log_path),
        "stdout": "",
        "stderr": "",
        "errors": [],
    }
    if args.timeout_seconds <= 0:
        result["errors"].append("timeout must be positive")
        result["status"] = "FAIL"
        return result
    resolution = resolve_unity_editor(unity_editor=args.unity_editor)
    result["unity_editor"] = resolution.editor_path
    result["expected_version"] = resolution.expected_version
    result["detected_version"] = resolution.detected_version
    result["discovery_source"] = resolution.method
    if not resolution.ok or not resolution.editor_path:
        result["errors"].append("Unity Editor discovery failed")
        result["errors"].extend(resolution.errors)
        result["status"] = "FAIL"
        result["duration_ms"] = duration_ms(start)
        return result
    json_output.parent.mkdir(parents=True, exist_ok=True)
    log_path.parent.mkdir(parents=True, exist_ok=True)
    command = build_command(resolution.editor_path, scenario, content_root, json_output, log_path)
    result["command"] = command
    launch_start = time.time()
    try:
        completed = run_unity_process(command, ROOT, args.timeout_seconds)
        result["exit_code"] = completed.returncode
        result["stdout"] = completed.stdout or ""
        result["stderr"] = completed.stderr or ""
        if completed.returncode not in (0, 2):
            result["errors"].append(f"Unity scenario runner exited with code {completed.returncode}")
    except subprocess.TimeoutExpired as exc:
        result["exit_code"] = None
        result["stdout"] = exc.stdout or ""
        result["stderr"] = exc.stderr or ""
        result["errors"].append(f"Unity scenario runner timed out after {args.timeout_seconds} seconds")
    except OSError as exc:
        result["exit_code"] = None
        result["errors"].append(f"Unity scenario runner could not start: {exc}")
    if not json_output.exists() or json_output.stat().st_mtime < launch_start:
        result["errors"].append("Scenario JSON output is missing or stale")
    result["errors"].extend(validate_result_json(json_output))
    scenario_status = read_scenario_status(json_output) if json_output.exists() else None
    if completed_returned_failure(result["exit_code"], scenario_status):
        result["errors"].append(f"Scenario runner reported {scenario_status or 'unknown'} with exit code {result['exit_code']}")
    result["status"] = "FAIL" if result["errors"] else "PASS"
    result["duration_ms"] = duration_ms(start)
    return result


def completed_returned_failure(exit_code: int | None, scenario_status: str | None) -> bool:
    if exit_code is None:
        return False
    if exit_code == 0:
        return scenario_status == "failed"
    return True


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--scenario", type=Path, default=DEFAULT_SCENARIO)
    parser.add_argument("--content-root", type=Path, default=DEFAULT_CONTENT_ROOT)
    parser.add_argument("--json-output", type=Path, required=True)
    parser.add_argument("--unity-editor")
    parser.add_argument("--timeout-seconds", type=int, default=600)
    args = parser.parse_args(argv)
    result = run(args)
    print(f"Unity ScenarioRunner: {result['status']}")
    print(f"- Scenario: {result['scenario']}")
    print(f"- JSON: {result['json_output']}")
    print(f"- Log: {result['log_path']}")
    for error in result["errors"]:
        print(f"- ERROR: {error}")
    return 0 if result["status"] == "PASS" else (result["exit_code"] or 1)


if __name__ == "__main__":
    sys.exit(main())
