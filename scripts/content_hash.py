#!/usr/bin/env python3
"""Canonical content hashing helpers."""

from __future__ import annotations

import hashlib
from pathlib import Path


def normalize_json_line_endings(data: bytes) -> bytes:
    return data.replace(b"\r\n", b"\n").replace(b"\r", b"\n")


def canonical_json_sha256_file(path: Path) -> str:
    """Hash JSON bytes after normalizing CRLF/CR line endings to LF."""
    return hashlib.sha256(normalize_json_line_endings(path.read_bytes())).hexdigest()
