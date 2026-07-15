#!/usr/bin/env python3
"""Enforcement de versionado de content pack en CI.

Reglas aplicadas:
- Si cambian archivos en Assets/StreamingAssets/content/** (excepto manifest.json),
  entonces manifest.json debe cambiar.
- Si cambian archivos de contenido (excepto manifest), content_pack_version debe incrementar.
- Si cambia content_schema_version, también debe incrementar content_pack_version.

Uso:
  python3 scripts/check_manifest_bump.py --base <sha> --head <sha>
  python3 scripts/check_manifest_bump.py --base <sha> --working-tree
"""

from __future__ import annotations

import argparse
import json
import subprocess
import sys
from pathlib import Path
from typing import Any

CONTENT_ROOT = "Assets/StreamingAssets/content"
MANIFEST_PATH = f"{CONTENT_ROOT}/manifest.json"


def run_git(args: list[str]) -> str:
    result = subprocess.run(["git", *args], check=True, capture_output=True, text=True)
    return result.stdout.strip()


def ensure_commit_ref(ref: str, label: str) -> None:
    try:
        run_git(["rev-parse", "--verify", f"{ref}^{{commit}}"])
    except subprocess.CalledProcessError:
        raise SystemExit(f"ERROR: {label} ref not found or not a commit: {ref}")


def changed_files(base: str, head: str) -> list[str]:
    out = run_git(["diff", "--name-only", f"{base}...{head}"])
    return [line for line in out.splitlines() if line]


def changed_files_working_tree(base: str) -> list[str]:
    tracked = run_git(["diff", "--name-only", base])
    untracked = run_git(["ls-files", "--others", "--exclude-standard"])
    files = {line for line in tracked.splitlines() if line}
    files.update(line for line in untracked.splitlines() if line)
    return sorted(files)


def load_manifest_at(ref: str) -> dict[str, Any]:
    content = run_git(["show", f"{ref}:{MANIFEST_PATH}"])
    return json.loads(content)


def load_manifest_working_tree() -> dict[str, Any]:
    with Path(MANIFEST_PATH).open("r", encoding="utf-8") as f:
        return json.load(f)


def evaluate_manifest_policy(
    files: list[str],
    old_manifest: dict[str, Any],
    new_manifest: dict[str, Any],
) -> tuple[bool, list[str], str]:
    content_changed = [f for f in files if f.startswith(f"{CONTENT_ROOT}/")]
    non_manifest_content_changed = [f for f in content_changed if f != MANIFEST_PATH]
    manifest_changed = MANIFEST_PATH in files

    if not content_changed:
        return True, [], "OK: no hubo cambios en content pack; no aplica enforcement de manifest."

    if non_manifest_content_changed and not manifest_changed:
        return False, non_manifest_content_changed, "ERROR: hubo cambios en contenido sin actualizar manifest.json"

    old_pack = int(old_manifest.get("content_pack_version", -1))
    new_pack = int(new_manifest.get("content_pack_version", -1))
    old_schema = int(old_manifest.get("content_schema_version", -1))
    new_schema = int(new_manifest.get("content_schema_version", -1))

    if non_manifest_content_changed and new_pack <= old_pack:
        return (
            False,
            [],
            "ERROR: cambió contenido (distinto de manifest) pero content_pack_version no incrementó "
            f"(old={old_pack}, new={new_pack}).",
        )

    if new_schema > old_schema and new_pack <= old_pack:
        return (
            False,
            [],
            "ERROR: content_schema_version incrementó pero content_pack_version no incrementó "
            f"(schema {old_schema}->{new_schema}, pack {old_pack}->{new_pack}).",
        )

    return True, [], f"OK: manifest/versionado consistente (pack {old_pack}->{new_pack}, schema {old_schema}->{new_schema})."


def print_policy_result(ok: bool, details: list[str], message: str) -> int:
    print(message)
    for detail in details:
        print(f"- {detail}")
    return 0 if ok else 1


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--base", required=True)
    parser.add_argument("--head")
    parser.add_argument("--working-tree", action="store_true")
    args = parser.parse_args()

    if bool(args.head) == bool(args.working_tree):
        parser.error("provide exactly one of --head or --working-tree")

    ensure_commit_ref(args.base, "base")
    if args.head:
        ensure_commit_ref(args.head, "head")

    if args.working_tree:
        files = changed_files_working_tree(args.base)
        old_manifest = load_manifest_at(args.base)
        new_manifest = load_manifest_working_tree()
    else:
        files = changed_files(args.base, args.head)
        old_manifest = load_manifest_at(args.base)
        new_manifest = load_manifest_at(args.head)

    ok, details, message = evaluate_manifest_policy(files, old_manifest, new_manifest)
    return print_policy_result(ok, details, message)


if __name__ == "__main__":
    try:
        sys.exit(main())
    except subprocess.CalledProcessError as exc:
        print("ERROR: fallo ejecutando git:", exc)
        if exc.stdout:
            print(exc.stdout)
        if exc.stderr:
            print(exc.stderr)
        sys.exit(1)
