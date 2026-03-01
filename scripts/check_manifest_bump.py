#!/usr/bin/env python3
"""Enforcement de versionado de content pack en CI.

Reglas aplicadas:
- Si cambian archivos en Assets/StreamingAssets/content/** (excepto manifest.json),
  entonces manifest.json debe cambiar.
- Si cambian archivos de contenido (excepto manifest), content_pack_version debe incrementar.
- Si cambia content_schema_version, también debe incrementar content_pack_version.

Uso:
  python3 scripts/check_manifest_bump.py --base <sha> --head <sha>
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


def changed_files(base: str, head: str) -> list[str]:
    out = run_git(["diff", "--name-only", f"{base}...{head}"])
    return [line for line in out.splitlines() if line]


def load_manifest_at(ref: str) -> dict[str, Any]:
    content = run_git(["show", f"{ref}:{MANIFEST_PATH}"])
    return json.loads(content)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--base", required=True)
    parser.add_argument("--head", required=True)
    args = parser.parse_args()

    files = changed_files(args.base, args.head)
    content_changed = [f for f in files if f.startswith(f"{CONTENT_ROOT}/")]
    non_manifest_content_changed = [f for f in content_changed if f != MANIFEST_PATH]
    manifest_changed = MANIFEST_PATH in files

    if not content_changed:
        print("OK: no hubo cambios en content pack; no aplica enforcement de manifest.")
        return 0

    if non_manifest_content_changed and not manifest_changed:
        print("ERROR: hubo cambios en contenido sin actualizar manifest.json")
        for f in non_manifest_content_changed:
            print(f"- {f}")
        return 1

    # Si solo cambió manifest, sigue siendo válido, pero verificamos coherencia de versions si aplica.
    old_manifest = load_manifest_at(args.base)
    new_manifest = load_manifest_at(args.head)

    old_pack = int(old_manifest.get("content_pack_version", -1))
    new_pack = int(new_manifest.get("content_pack_version", -1))
    old_schema = int(old_manifest.get("content_schema_version", -1))
    new_schema = int(new_manifest.get("content_schema_version", -1))

    if non_manifest_content_changed and new_pack <= old_pack:
        print(
            "ERROR: cambió contenido (distinto de manifest) pero content_pack_version no incrementó "
            f"(old={old_pack}, new={new_pack})."
        )
        return 1

    if new_schema > old_schema and new_pack <= old_pack:
        print(
            "ERROR: content_schema_version incrementó pero content_pack_version no incrementó "
            f"(schema {old_schema}->{new_schema}, pack {old_pack}->{new_pack})."
        )
        return 1

    print(
        "OK: manifest/versionado consistente "
        f"(pack {old_pack}->{new_pack}, schema {old_schema}->{new_schema})."
    )
    return 0


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
