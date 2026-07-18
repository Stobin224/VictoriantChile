from __future__ import annotations

import os
import stat
from dataclasses import dataclass
from pathlib import Path
from typing import Mapping


MAX_GIT_CONFIG_COUNT = 128
MAX_GIT_CONFIG_COUNT_DIGITS = len(str(MAX_GIT_CONFIG_COUNT))
AGENTS_DIR_NAME = ".agents"


@dataclass(frozen=True)
class GitScopedEnvironment:
    environment: dict[str, str]
    safe_directory: str
    preserved_entries: int
    injected: bool

    def metadata(self) -> dict[str, int | bool | str]:
        return {
            "git_safe_directory_injected": self.injected,
            "git_safe_directory_preserved_entries": self.preserved_entries,
            "git_safe_directory_repo": "<repo>",
        }


@dataclass(frozen=True)
class AgentsDirectorySnapshot:
    path: Path
    exists: bool
    is_directory: bool
    is_symlink: bool
    is_reparse_point: bool
    entry_count: int

    @property
    def is_ordinary_empty_directory(self) -> bool:
        return self.exists and self.is_directory and not self.is_symlink and not self.is_reparse_point and self.entry_count == 0


def git_safe_directory_value(repo_root: Path) -> str:
    resolved = _canonical_repo_root(repo_root)
    if os.name == "nt":
        return resolved.as_posix()
    return str(resolved)


def build_git_scoped_environment(base_env: Mapping[str, str], repo_root: Path) -> GitScopedEnvironment:
    safe_directory = git_safe_directory_value(repo_root)
    environment = dict(base_env)
    if _get_normalized_env(environment, "GIT_CONFIG_PARAMETERS") is not None:
        raise ValueError("codex.invalid_git_config_environment: GIT_CONFIG_PARAMETERS is not supported")
    count_text = _get_normalized_env(environment, "GIT_CONFIG_COUNT")
    if count_text is None:
        environment["GIT_CONFIG_COUNT"] = "1"
        environment["GIT_CONFIG_KEY_0"] = "safe.directory"
        environment["GIT_CONFIG_VALUE_0"] = safe_directory
        return GitScopedEnvironment(environment, safe_directory, 0, True)

    count = _parse_git_config_count(count_text)
    safe_directory_seen = False
    for index in range(count):
        key_name = f"GIT_CONFIG_KEY_{index}"
        value_name = f"GIT_CONFIG_VALUE_{index}"
        key = _get_normalized_env(environment, key_name)
        value = _get_normalized_env(environment, value_name)
        if key is None or value is None or not key:
            raise ValueError("codex.invalid_git_config_environment: incomplete GIT_CONFIG_* entry")
        if key == "safe.directory":
            if value == "*":
                raise ValueError("codex.invalid_git_config_environment: wildcard safe.directory is not allowed")
            if _normalize_safe_directory(value) == _normalize_safe_directory(safe_directory):
                safe_directory_seen = True
    if safe_directory_seen:
        return GitScopedEnvironment(environment, safe_directory, count, False)
    if count >= MAX_GIT_CONFIG_COUNT:
        raise ValueError("codex.invalid_git_config_environment: GIT_CONFIG_COUNT exceeds supported limit")
    environment["GIT_CONFIG_COUNT"] = str(count + 1)
    environment[f"GIT_CONFIG_KEY_{count}"] = "safe.directory"
    environment[f"GIT_CONFIG_VALUE_{count}"] = safe_directory
    return GitScopedEnvironment(environment, safe_directory, count, True)


def inspect_agents_directory(repo_root: Path) -> AgentsDirectorySnapshot:
    repo = _canonical_repo_root(repo_root)
    path = repo / AGENTS_DIR_NAME
    try:
        stat_result = os.lstat(path)
    except FileNotFoundError:
        return AgentsDirectorySnapshot(path, False, False, False, False, 0)
    is_symlink = path.is_symlink()
    file_attributes = getattr(stat_result, "st_file_attributes", 0)
    is_reparse_point = bool(file_attributes & getattr(stat, "FILE_ATTRIBUTE_REPARSE_POINT", 0))
    is_directory = path.is_dir()
    entry_count = 0
    if is_directory and not is_symlink and not is_reparse_point:
        with os.scandir(path) as entries:
            for _entry in entries:
                entry_count += 1
    return AgentsDirectorySnapshot(path, True, is_directory, is_symlink, is_reparse_point, entry_count)


def initial_agents_runtime(repo_root: Path) -> dict[str, bool | int | str]:
    snapshot = inspect_agents_directory(repo_root)
    return {
        "path": AGENTS_DIR_NAME,
        "existed_before_run": snapshot.exists,
        "created_by_run": False,
        "cleanup_pending": False,
        "classification": "preexisting" if snapshot.exists else "absent",
        "exists_now": snapshot.exists,
        "entry_count": snapshot.entry_count,
        "is_directory": snapshot.is_directory,
        "is_symlink": snapshot.is_symlink,
        "is_reparse_point": snapshot.is_reparse_point,
        "removed": False,
    }


def reconcile_agents_runtime(
    runtime_state: Mapping[str, bool | int | str] | None,
    snapshot: AgentsDirectorySnapshot,
) -> tuple[dict[str, bool | int | str], str | None]:
    existed_before = bool((runtime_state or {}).get("existed_before_run"))
    created_by_run = bool((runtime_state or {}).get("created_by_run"))
    state = dict(runtime_state or initial_agents_runtime(snapshot.path.parent))
    state.update(
        {
            "path": AGENTS_DIR_NAME,
            "exists_now": snapshot.exists,
            "entry_count": snapshot.entry_count,
            "is_directory": snapshot.is_directory,
            "is_symlink": snapshot.is_symlink,
            "is_reparse_point": snapshot.is_reparse_point,
            "removed": False,
        }
    )
    if not snapshot.exists:
        state["classification"] = "preexisting_removed" if existed_before else "absent"
        state["cleanup_pending"] = False
        return state, None
    if snapshot.is_symlink or snapshot.is_reparse_point or not snapshot.is_directory:
        state["classification"] = "unsafe_type"
        state["cleanup_pending"] = False
        return state, "runtime .agents is not an ordinary directory"
    if existed_before:
        state["classification"] = "preexisting"
        state["cleanup_pending"] = False
        return state, None
    state["created_by_run"] = True
    if snapshot.entry_count == 0:
        state["classification"] = "ephemeral_runtime_artifact"
        state["cleanup_pending"] = True
        return state, None
    state["classification"] = "ephemeral_runtime_artifact_nonempty"
    state["cleanup_pending"] = True
    return state, "runtime .agents contains entries"


def cleanup_agents_directory(
    runtime_state: Mapping[str, bool | int | str] | None,
    repo_root: Path,
) -> tuple[dict[str, bool | int | str], str | None]:
    state = dict(runtime_state or initial_agents_runtime(repo_root))
    snapshot = inspect_agents_directory(repo_root)
    state.update(
        {
            "exists_now": snapshot.exists,
            "entry_count": snapshot.entry_count,
            "is_directory": snapshot.is_directory,
            "is_symlink": snapshot.is_symlink,
            "is_reparse_point": snapshot.is_reparse_point,
            "removed": False,
        }
    )
    if not bool(state.get("created_by_run")):
        state["cleanup_pending"] = False
        return state, None
    if not snapshot.exists:
        state["cleanup_pending"] = False
        state["removed"] = True
        state["classification"] = "ephemeral_runtime_artifact_removed"
        return state, None
    if snapshot.is_symlink or snapshot.is_reparse_point or not snapshot.is_directory:
        state["classification"] = "ephemeral_cleanup_unsafe_type"
        state["cleanup_pending"] = True
        return state, "ephemeral_cleanup_failed: .agents is not an ordinary directory"
    if snapshot.entry_count != 0:
        state["classification"] = "ephemeral_runtime_artifact_nonempty"
        state["cleanup_pending"] = True
        return state, "ephemeral_cleanup_failed: .agents is not empty"
    final_snapshot = inspect_agents_directory(repo_root)
    if (
        not final_snapshot.exists
        or final_snapshot.is_symlink
        or final_snapshot.is_reparse_point
        or not final_snapshot.is_directory
        or final_snapshot.entry_count != 0
        or final_snapshot.path != snapshot.path
    ):
        state["classification"] = "ephemeral_cleanup_failed"
        state["cleanup_pending"] = True
        return state, "ephemeral_cleanup_failed: .agents changed before removal"
    try:
        os.rmdir(final_snapshot.path)
    except OSError as exc:
        state["classification"] = "ephemeral_cleanup_failed"
        state["cleanup_pending"] = True
        return state, f"ephemeral_cleanup_failed: {exc}"
    state["classification"] = "ephemeral_runtime_artifact_removed"
    state["cleanup_pending"] = False
    state["removed"] = True
    state["exists_now"] = False
    state["entry_count"] = 0
    return state, None


def _canonical_repo_root(repo_root: Path) -> Path:
    repo = Path(repo_root)
    if not repo:
        raise ValueError("codex.invalid_git_config_environment: repository path is empty")
    if not repo.is_absolute():
        raise ValueError("codex.invalid_git_config_environment: repository path must be absolute")
    resolved = repo.resolve(strict=False)
    text = str(resolved)
    if not text or text in {"\\", "/"} or resolved.parent == resolved:
        raise ValueError("codex.invalid_git_config_environment: repository path is too broad")
    if os.name == "nt":
        posix = resolved.as_posix()
        if text.startswith("\\\\") or posix.startswith("//"):
            raise ValueError("codex.invalid_git_config_environment: UNC repository paths are not supported")
    if not resolved.exists() or not resolved.is_dir():
        raise ValueError("codex.invalid_git_config_environment: repository path must exist")
    if not (resolved / ".git").exists():
        raise ValueError("codex.invalid_git_config_environment: repository path must point to a Git repository")
    if "*" in text:
        raise ValueError("codex.invalid_git_config_environment: wildcard repository path is not allowed")
    return resolved


def _parse_git_config_count(value: str) -> int:
    if value is None or value == "":
        raise ValueError("codex.invalid_git_config_environment: GIT_CONFIG_COUNT must be a canonical non-negative integer")
    if len(value) > MAX_GIT_CONFIG_COUNT_DIGITS:
        raise ValueError("codex.invalid_git_config_environment: GIT_CONFIG_COUNT exceeds supported limit")
    if not value.isascii() or not value.isdigit():
        raise ValueError("codex.invalid_git_config_environment: GIT_CONFIG_COUNT must be a canonical non-negative integer")
    if value != "0" and value.startswith("0"):
        raise ValueError("codex.invalid_git_config_environment: GIT_CONFIG_COUNT must be canonical")
    count = int(value)
    if count < 0 or count > MAX_GIT_CONFIG_COUNT:
        raise ValueError("codex.invalid_git_config_environment: GIT_CONFIG_COUNT exceeds supported limit")
    return count


def _normalize_safe_directory(value: str) -> str:
    return value.replace("\\", "/").rstrip("/").casefold() if os.name == "nt" else value.rstrip("/")


def _get_normalized_env(environment: dict[str, str], canonical_name: str) -> str | None:
    if os.name != "nt":
        return environment.get(canonical_name)
    matches = [key for key in environment if key.casefold() == canonical_name.casefold()]
    if len(matches) > 1:
        raise ValueError(f"codex.invalid_git_config_environment: ambiguous environment entries for {canonical_name}")
    if not matches:
        return None
    key = matches[0]
    value = environment[key]
    if key != canonical_name:
        del environment[key]
        environment[canonical_name] = value
    return value
