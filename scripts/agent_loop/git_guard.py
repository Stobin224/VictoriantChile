from __future__ import annotations

import hashlib
import subprocess
from dataclasses import dataclass
from pathlib import Path

from .models import TaskSpec


@dataclass(frozen=True)
class ScopeAudit:
    ok: bool
    changed_files: tuple[str, ...]
    violations: tuple[str, ...]
    fingerprint: str


def git(repo: Path, *args: str, check: bool = True) -> subprocess.CompletedProcess[str]:
    return subprocess.run(["git", *args], cwd=repo, capture_output=True, text=True, shell=False, check=check)


def current_branch(repo: Path) -> str:
    return git(repo, "branch", "--show-current").stdout.strip()


def rev_parse(repo: Path, ref: str) -> str:
    return git(repo, "rev-parse", ref).stdout.strip()


def ensure_clean(repo: Path) -> None:
    status = git(repo, "status", "--short").stdout
    if status.strip():
        raise RuntimeError(f"working tree is not clean:\n{status}")


def create_branch(repo: Path, branch: str, base_sha: str) -> None:
    if branch in {"main", "master"}:
        raise RuntimeError("refusing to create main/master task branch")
    git(repo, "switch", "-c", branch, base_sha)


def changed_files(repo: Path, *, base_sha: str | None = None) -> list[str]:
    names: set[str] = set()
    if base_sha:
        diff = git(repo, "diff", "--name-status", f"{base_sha}...HEAD").stdout
        for line in diff.splitlines():
            if not line.strip():
                continue
            parts = line.split("\t")
            for path in parts[1:]:
                if path == ".agent-loop" or path.startswith(".agent-loop/"):
                    continue
                names.add(path.replace("\\", "/"))
    porcelain = git(repo, "status", "--porcelain=v1", "--untracked-files=all", "-z").stdout
    if porcelain:
        records = porcelain.split("\0")
        index = 0
        while index < len(records):
            entry = records[index]
            index += 1
            if not entry:
                continue
            code = entry[:2]
            path = entry[3:]
            if path == ".agent-loop" or path.startswith(".agent-loop/"):
                continue
            if code.startswith("R") or code.startswith("C"):
                names.add(path.replace("\\", "/"))
                if index < len(records) and records[index]:
                    rename_path = records[index].replace("\\", "/")
                    if rename_path != ".agent-loop" and not rename_path.startswith(".agent-loop/"):
                        names.add(rename_path)
                    index += 1
            else:
                names.add(path.replace("\\", "/"))
    return sorted(names)


def path_matches(rule: str, path: str) -> bool:
    rule = rule.replace("\\", "/")
    path = path.replace("\\", "/")
    if rule.endswith("/"):
        return path.startswith(rule)
    return path == rule


def audit_scope(repo: Path, spec: TaskSpec, *, base_sha: str | None = None, expected_branch: str | None = None) -> ScopeAudit:
    files = changed_files(repo, base_sha=base_sha)
    violations: list[str] = []
    if expected_branch and current_branch(repo) != expected_branch:
        violations.append(f"current branch is {current_branch(repo)}, expected {expected_branch}")
    for path in files:
        if path == ".git" or path.startswith(".git/"):
            violations.append(f"{path}: .git is never allowed")
            continue
        protected = any(path_matches(rule, path) for rule in spec.protected_paths)
        allowed = any(path_matches(rule, path) for rule in spec.allowed_paths)
        if protected:
            violations.append(f"{path}: protected path")
        elif not allowed:
            violations.append(f"{path}: outside allowed paths")
    fingerprint = fingerprint_worktree(repo, files)
    return ScopeAudit(not violations, tuple(files), tuple(violations), fingerprint)


def fingerprint_worktree(repo: Path, files: list[str] | None = None, *, base_sha: str | None = None) -> str:
    files = changed_files(repo, base_sha=base_sha) if files is None else files
    digest = hashlib.sha256()
    for path in sorted(files):
        digest.update(path.encode("utf-8"))
        full = repo / path
        if full.exists() and full.is_file():
            digest.update(hashlib.sha256(full.read_bytes()).digest())
        else:
            digest.update(b"<missing>")
    return digest.hexdigest()


def explicit_stage(repo: Path, files: list[str]) -> None:
    if not files:
        return
    git(repo, "add", "--", *files)
