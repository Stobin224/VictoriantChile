#!/usr/bin/env python3
from __future__ import annotations

import argparse
import json
import os
import shutil
import sys
import time
import uuid
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
if str(ROOT) not in sys.path:
    sys.path.insert(0, str(ROOT))

from scripts.agent_loop.codex_client import discover_codex  # noqa: E402
from scripts.agent_loop.evidence import state_to_json  # noqa: E402
from scripts.agent_loop.git_guard import current_branch, ensure_clean, fingerprint_worktree, rev_parse  # noqa: E402
from scripts.agent_loop.git_scope import build_git_scoped_environment  # noqa: E402
from scripts.agent_loop.io_utils import atomic_write_json  # noqa: E402
from scripts.agent_loop.models import EXIT_CODES, LoopState, RESUMABLE_STATUSES, Usage  # noqa: E402
from scripts.agent_loop.runner import LoopRunner  # noqa: E402
from scripts.agent_loop.task_spec import TaskSpecError, load_task_spec  # noqa: E402


def write_output(path: Path | None, data: dict) -> None:
    if path:
        target = path if path.is_absolute() else (Path.cwd() / path).resolve()
        atomic_write_json(target, data)


def result_from_error(status: str, errors: list[str], task_id: str = "", run_id: str = "") -> dict:
    state = LoopState(run_id or "", task_id or "", "", "", "", "", status, usage=Usage(), errors=errors)
    return state_to_json(state, elapsed_seconds=0, resume_command=None)


def command_validate(args: argparse.Namespace) -> int:
    try:
        spec, task_hash = load_task_spec(args.task)
    except TaskSpecError as exc:
        data = result_from_error("checks_failed", [str(exc)])
        write_output(args.json_output, data)
        print(str(exc))
        return 2
    data = {
        "schema_version": 1,
        "status": "passed",
        "task_id": spec.task_id,
        "task_hash": task_hash,
        "errors": [],
    }
    write_output(args.json_output, data)
    print(f"Task spec valid: {spec.task_id}")
    return 0


def command_preflight(args: argparse.Namespace) -> int:
    try:
        spec, task_hash = load_task_spec(args.task)
        build_git_scoped_environment(os.environ, ROOT)
        ensure_clean(ROOT)
        base_sha = rev_parse(ROOT, spec.base_ref)
        discovery = discover_codex(args.codex_executable)
        if not discovery.ok:
            raise RuntimeError("Codex discovery failed: " + "; ".join(discovery.errors))
        data = {
            "schema_version": 1,
            "status": "passed",
            "task_id": spec.task_id,
            "task_hash": task_hash,
            "base_sha": base_sha,
            "branch": spec.branch,
            "codex": {"method": discovery.method, "version": discovery.version},
            "errors": [],
        }
        write_output(args.json_output, data)
        print(f"Preflight passed: {spec.task_id}")
        return 0
    except (TaskSpecError, RuntimeError, ValueError) as exc:
        data = result_from_error("tool_failure", [str(exc)])
        write_output(args.json_output, data)
        print(str(exc))
        return 6


def command_run(args: argparse.Namespace) -> int:
    start = time.time()
    try:
        spec, task_hash = load_task_spec(args.task)
        run_id = uuid.uuid4().hex
        run_dir = ROOT / ".agent-loop" / "runs" / run_id
        run_dir.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(args.task, run_dir / "task.json")
        runner = LoopRunner(ROOT, spec, task_hash, codex_executable=args.codex_executable, run_id=run_id)
        state = runner.run(publish=args.publish)
        resume = f"python scripts/run_agent_loop.py resume --run-id {state.run_id}" if state.status in RESUMABLE_STATUSES else None
        data = state_to_json(state, elapsed_seconds=int(time.time() - start), resume_command=resume)
        write_output(args.json_output, data)
        print(f"Agent loop status: {state.status}")
        return EXIT_CODES.get(state.status, 6)
    except (TaskSpecError, RuntimeError) as exc:
        data = result_from_error("tool_failure", [str(exc)])
        write_output(args.json_output, data)
        print(str(exc))
        return 6


def command_resume(args: argparse.Namespace) -> int:
    run_dir = ROOT / ".agent-loop" / "runs" / args.run_id
    state_file = run_dir / "state.json"
    if not state_file.exists():
        data = result_from_error("tool_failure", [f"run state not found: {args.run_id}"], run_id=args.run_id)
        write_output(args.json_output, data)
        print(f"run state not found: {args.run_id}")
        return 6
    try:
        state_data = json.loads(state_file.read_text(encoding="utf-8"))
        task_path = run_dir / "task.json"
        if not task_path.exists():
            raise RuntimeError("task snapshot is missing")
        spec, task_hash = load_task_spec(task_path)
        build_git_scoped_environment(os.environ, ROOT)
        if task_hash != state_data.get("task_hash"):
            raise RuntimeError("task hash changed")
        if current_branch(ROOT) != state_data.get("branch"):
            raise RuntimeError("current branch does not match checkpoint")
        if rev_parse(ROOT, spec.base_ref) != state_data.get("base_sha"):
            raise RuntimeError("base SHA changed")
        if state_data.get("status") in {"passed", "needs_input", "scope_violation", "checks_failed", "budget_exhausted", "usage_limit_reached", "tool_failure", "publication_failed"}:
            raise RuntimeError("run is already terminal")
        if state_data.get("fingerprint") and fingerprint_worktree(ROOT) != state_data.get("fingerprint"):
            raise RuntimeError("working tree fingerprint does not match checkpoint")
        if state_data.get("status") != "branch_ready" and not state_data.get("writer_session_id"):
            raise RuntimeError("writer session id is missing")
        state = loop_state_from_json(state_data)
        runner = LoopRunner(ROOT, spec, task_hash, codex_executable=args.codex_executable, run_id=args.run_id)
        resumed = runner.resume(state, publish=args.publish)
        data = state_to_json(
            resumed,
            elapsed_seconds=0,
            resume_command=f"python scripts/run_agent_loop.py resume --run-id {args.run_id}" if resumed.status in RESUMABLE_STATUSES else None,
        )
        write_output(args.json_output, data)
        print(f"Agent loop status: {resumed.status}")
        return EXIT_CODES.get(resumed.status, 6)
    except (OSError, json.JSONDecodeError, RuntimeError, TaskSpecError, ValueError) as exc:
        data = result_from_error("tool_failure", [str(exc)], run_id=args.run_id)
        write_output(args.json_output, data)
        print(str(exc))
        return 6


def loop_state_from_json(data: dict) -> LoopState:
    usage_data = data.get("usage") if isinstance(data.get("usage"), dict) else {}
    usage = Usage(
        usage_data.get("input_tokens") if type(usage_data.get("input_tokens")) is int else None,
        usage_data.get("cached_input_tokens") if type(usage_data.get("cached_input_tokens")) is int else None,
        usage_data.get("output_tokens") if type(usage_data.get("output_tokens")) is int else None,
        usage_data.get("reasoning_output_tokens") if type(usage_data.get("reasoning_output_tokens")) is int else None,
    )
    state = LoopState(
        str(data.get("run_id") or ""),
        str(data.get("task_id") or ""),
        str(data.get("task_hash") or ""),
        str(data.get("base_ref") or ""),
        str(data.get("base_sha") or ""),
        str(data.get("branch") or ""),
        str(data.get("status") or ""),
        int(data.get("iteration") or 0),
        int(data.get("codex_turns") or 0),
        int(data.get("review_turns") or 0),
        usage,
        data.get("writer_session_id") if isinstance(data.get("writer_session_id"), str) else None,
    )
    state.fingerprint = data.get("fingerprint") if isinstance(data.get("fingerprint"), str) else None
    state.changed_files = list(data.get("changed_files") or [])
    state.errors = list(data.get("errors") or [])
    state.agents_runtime = dict(data.get("agents_runtime") or {})
    return state


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser()
    sub = parser.add_subparsers(dest="command", required=True)
    for name in ("validate", "preflight", "run"):
        p = sub.add_parser(name)
        p.add_argument("--task", type=Path, required=True)
        p.add_argument("--json-output", type=Path)
        p.add_argument("--codex-executable")
        if name == "run":
            p.add_argument("--publish", action="store_true")
    r = sub.add_parser("resume")
    r.add_argument("--run-id", required=True)
    r.add_argument("--json-output", type=Path)
    r.add_argument("--codex-executable")
    r.add_argument("--publish", action="store_true")
    args = parser.parse_args(argv)
    if args.command == "validate":
        return command_validate(args)
    if args.command == "preflight":
        return command_preflight(args)
    if args.command == "run":
        return command_run(args)
    if args.command == "resume":
        return command_resume(args)
    raise AssertionError(args.command)


if __name__ == "__main__":
    sys.exit(main())
