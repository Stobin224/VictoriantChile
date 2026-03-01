#!/usr/bin/env python3
"""Recalcula hashes SHA-256 declarados en content/manifest.json.

Uso:
  python3 scripts/recompute_manifest_hashes.py [--bump-pack]

- Actualiza manifest.files[*] con hash real de cada archivo referenciado.
- Opcionalmente incrementa content_pack_version en +1 con --bump-pack.
"""

from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
from typing import Any

ROOT = Path(__file__).resolve().parents[1]
CONTENT_DIR = ROOT / "Assets" / "StreamingAssets" / "content"
MANIFEST_PATH = CONTENT_DIR / "manifest.json"


def sha256_file(path: Path) -> str:
    h = hashlib.sha256()
    with path.open("rb") as f:
        for chunk in iter(lambda: f.read(1024 * 1024), b""):
            h.update(chunk)
    return h.hexdigest()


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--bump-pack", action="store_true", help="incrementa content_pack_version en +1")
    args = parser.parse_args()

    manifest: dict[str, Any] = json.loads(MANIFEST_PATH.read_text(encoding="utf-8"))

    files = manifest.get("files", {})
    if not isinstance(files, dict) or not files:
        raise SystemExit("ERROR: manifest.json no contiene sección files válida")

    for rel_path in files.keys():
        target = CONTENT_DIR / rel_path
        if not target.exists():
            raise SystemExit(f"ERROR: archivo no existe para hash: {rel_path}")
        files[rel_path] = f"sha256:{sha256_file(target)}"

    if args.bump_pack:
        old_pack = int(manifest.get("content_pack_version", 0))
        manifest["content_pack_version"] = old_pack + 1
        print(f"content_pack_version: {old_pack} -> {old_pack + 1}")

    MANIFEST_PATH.write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print("OK: manifest hashes recalculados")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
