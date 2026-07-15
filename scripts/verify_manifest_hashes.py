#!/usr/bin/env python3
"""Non-destructive manifest hash verification for the content pack."""

from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path
from typing import Any

ROOT = Path(__file__).resolve().parents[1]
DEFAULT_CONTENT_DIR = ROOT / "Assets" / "StreamingAssets" / "content"
SHA_RE = re.compile(r"^sha256:[0-9a-f]{64}$")

if str(Path(__file__).resolve().parent) not in sys.path:
    sys.path.insert(0, str(Path(__file__).resolve().parent))

from content_hash import canonical_json_sha256_file  # noqa: E402


def safe_manifest_path(content_dir: Path, rel_path: str) -> Path | None:
    if not rel_path or Path(rel_path).is_absolute():
        return None
    resolved = (content_dir / rel_path).resolve()
    try:
        resolved.relative_to(content_dir.resolve())
    except ValueError:
        return None
    return resolved


def verify(content_dir: Path = DEFAULT_CONTENT_DIR) -> list[str]:
    content_dir = content_dir.resolve()
    manifest_path = content_dir / "manifest.json"
    errors: list[str] = []

    try:
        manifest: dict[str, Any] = json.loads(manifest_path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exc:
        return [f"manifest.json could not be read: {exc}"]

    files = manifest.get("files")
    if not isinstance(files, dict) or not files:
        return ["manifest.json files must be a non-empty object"]

    declared_paths: set[str] = set()
    for rel_path, expected in files.items():
        if not isinstance(rel_path, str):
            errors.append(f"manifest path must be a string: {rel_path!r}")
            continue
        target = safe_manifest_path(content_dir, rel_path)
        if target is None:
            errors.append(f"manifest path escapes content dir or is unsafe: {rel_path}")
            continue
        declared_paths.add(rel_path.replace("\\", "/"))
        if not isinstance(expected, str) or not SHA_RE.fullmatch(expected):
            errors.append(f"{rel_path}: invalid digest format {expected!r}; expected sha256:<64 hex>")
            continue
        if not target.exists():
            errors.append(f"{rel_path}: declared file does not exist")
            continue
        actual = f"sha256:{canonical_json_sha256_file(target)}"
        if actual != expected:
            errors.append(f"{rel_path}: hash mismatch (manifest={expected}, actual={actual})")

    for path in sorted(content_dir.rglob("*.json")):
        rel = path.relative_to(content_dir).as_posix()
        if rel == "manifest.json":
            continue
        if rel not in declared_paths:
            errors.append(f"{rel}: JSON file is not declared in manifest.json")

    return errors


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--content-dir", type=Path, default=DEFAULT_CONTENT_DIR)
    args = parser.parse_args(argv)

    errors = verify(args.content_dir)
    if errors:
        print("ERROR: manifest hash verification failed")
        for error in errors:
            print(f"- {error}")
        return 1
    print("OK: manifest hashes verified.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
