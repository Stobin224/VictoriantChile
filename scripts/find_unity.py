#!/usr/bin/env python3
"""Resolve the exact Unity Editor required by this project."""

from __future__ import annotations

import argparse
import json
import os
import re
import stat
import subprocess
import sys
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Callable, Mapping, Sequence

ROOT = Path(__file__).resolve().parents[1]
PROJECT_VERSION_PATH = ROOT / "ProjectSettings" / "ProjectVersion.txt"
VERSION_RE = re.compile(r"^m_EditorVersion:\s*(?P<version>\S+)\s*$", re.MULTILINE)


@dataclass(frozen=True)
class UnityResolution:
    ok: bool
    expected_version: str
    editor_path: str | None
    detected_version: str | None
    method: str | None
    checked_paths: list[str]
    errors: list[str]

    def to_json(self) -> dict[str, Any]:
        return {
            "ok": self.ok,
            "expected_version": self.expected_version,
            "editor_path": self.editor_path,
            "detected_version": self.detected_version,
            "method": self.method,
            "checked_paths": self.checked_paths,
            "errors": self.errors,
        }


def read_project_version(project_version_path: Path = PROJECT_VERSION_PATH) -> str:
    text = project_version_path.read_text(encoding="utf-8")
    match = VERSION_RE.search(text)
    if not match:
        raise ValueError(f"could not find m_EditorVersion in {project_version_path}")
    return match.group("version")


def standard_unity_paths(version: str, platform: str | None = None, home: Path | None = None) -> list[Path]:
    platform = platform or sys.platform
    if platform.startswith("win"):
        return [Path(rf"C:\Program Files\Unity\Hub\Editor\{version}\Editor\Unity.exe")]
    if platform == "darwin":
        return [Path(f"/Applications/Unity/Hub/Editor/{version}/Unity.app/Contents/MacOS/Unity")]
    home = home or Path.home()
    return [
        Path(f"/opt/unity/editor/{version}/Editor/Unity"),
        Path(f"/opt/Unity/Hub/Editor/{version}/Editor/Unity"),
        home / "Unity" / "Hub" / "Editor" / version / "Editor" / "Unity",
    ]


def is_executable_file(path: Path, platform: str | None = None) -> bool:
    platform = platform or sys.platform
    if not path.is_file():
        return False
    if platform.startswith("win"):
        return path.suffix.lower() == ".exe"
    mode = path.stat().st_mode
    return bool(mode & (stat.S_IXUSR | stat.S_IXGRP | stat.S_IXOTH))


def parse_unity_version_output(output: str) -> str | None:
    match = re.search(r"\b\d{4}\.\d+\.\d+[abfp]\d+\b", output)
    return match.group(0) if match else None


def read_editor_version(editor_path: Path, timeout_seconds: int = 20) -> tuple[str | None, str | None]:
    try:
        result = subprocess.run(
            [str(editor_path), "-version"],
            capture_output=True,
            text=True,
            shell=False,
            timeout=timeout_seconds,
        )
    except (OSError, subprocess.TimeoutExpired) as exc:
        return None, str(exc)
    detected = parse_unity_version_output((result.stdout or "") + "\n" + (result.stderr or ""))
    if detected:
        return detected, None
    return None, f"could not parse Unity version from exit={result.returncode}"


def candidate_paths(
    expected_version: str,
    *,
    unity_editor: str | None,
    env: Mapping[str, str],
    platform: str | None = None,
    standard_paths: Sequence[Path] | None = None,
    home: Path | None = None,
) -> list[tuple[str, Path]]:
    if unity_editor:
        return [("cli", Path(unity_editor))]
    env_path = env.get("UNITY_EDITOR_PATH")
    if env_path:
        return [("env", Path(env_path))]
    paths = list(standard_paths) if standard_paths is not None else standard_unity_paths(expected_version, platform, home)
    return [("standard", path) for path in paths]


def resolve_unity_editor(
    *,
    project_version_path: Path = PROJECT_VERSION_PATH,
    unity_editor: str | None = None,
    env: Mapping[str, str] | None = None,
    platform: str | None = None,
    standard_paths: Sequence[Path] | None = None,
    home: Path | None = None,
    version_reader: Callable[[Path], tuple[str | None, str | None]] = read_editor_version,
) -> UnityResolution:
    env = env if env is not None else os.environ
    errors: list[str] = []
    checked_paths: list[str] = []
    try:
        expected_version = read_project_version(project_version_path)
    except (OSError, ValueError) as exc:
        return UnityResolution(False, "", None, None, None, [], [str(exc)])

    for method, path in candidate_paths(
        expected_version,
        unity_editor=unity_editor,
        env=env,
        platform=platform,
        standard_paths=standard_paths,
        home=home,
    ):
        checked_paths.append(str(path))
        if not is_executable_file(path, platform):
            errors.append(f"{method}: Unity editor path is not an executable file: {path}")
            continue
        detected_version, version_error = version_reader(path)
        if version_error:
            errors.append(f"{method}: could not verify Unity version at {path}: {version_error}")
            continue
        if detected_version != expected_version:
            errors.append(
                f"{method}: Unity version mismatch at {path}; expected {expected_version}, got {detected_version}"
            )
            continue
        return UnityResolution(True, expected_version, str(path), detected_version, method, checked_paths, [])

    errors.append(
        "Unity Editor not found for expected version "
        f"{expected_version}. Checked paths: {checked_paths}. "
        "Pass --unity-editor <path> or set UNITY_EDITOR_PATH."
    )
    return UnityResolution(False, expected_version, None, None, None, checked_paths, errors)


def print_human(resolution: UnityResolution) -> None:
    if resolution.ok:
        print(f"OK: Unity {resolution.detected_version} found via {resolution.method}: {resolution.editor_path}")
        return
    print("ERROR: Unity Editor discovery failed")
    print(f"- expected_version: {resolution.expected_version or 'unknown'}")
    for path in resolution.checked_paths:
        print(f"- checked: {path}")
    for error in resolution.errors:
        print(f"- {error}")
    print("- Use --unity-editor <path> to pass an explicit editor.")
    print("- Or define UNITY_EDITOR_PATH with the exact Unity executable path.")


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--unity-editor")
    parser.add_argument("--json", action="store_true")
    args = parser.parse_args(argv)

    resolution = resolve_unity_editor(unity_editor=args.unity_editor)
    if args.json:
        print(json.dumps(resolution.to_json(), indent=2, sort_keys=True))
    else:
        print_human(resolution)
    return 0 if resolution.ok else 1


if __name__ == "__main__":
    sys.exit(main())
