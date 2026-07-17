from __future__ import annotations

from pathlib import Path
import json
import subprocess

from .git_guard import explicit_stage, git
from .models import LoopState, TaskSpec
from .process_runner import ProcessRunner


class PublicationError(RuntimeError):
    pass


class Publisher:
    def __init__(self, repo: Path, process_runner: ProcessRunner | None = None) -> None:
        self.repo = repo
        self.process_runner = process_runner or ProcessRunner()

    def publish(self, spec: TaskSpec, state: LoopState, *, publish_flag: bool) -> str | None:
        try:
            return self._publish(spec, state, publish_flag=publish_flag)
        except subprocess.CalledProcessError as exc:
            raise PublicationError(exc.stderr or exc.stdout or str(exc)) from exc

    def _publish(self, spec: TaskSpec, state: LoopState, *, publish_flag: bool) -> str | None:
        if not publish_flag:
            return None
        if not (spec.publication.commit and spec.publication.push and spec.publication.draft_pr):
            return None
        if spec.publication.merge or spec.publication.mark_ready:
            raise PublicationError("merge and mark-ready are forbidden")
        if state.status != "passed":
            raise PublicationError("cannot publish a non-passed run")
        files = sorted(state.changed_files)
        explicit_stage(self.repo, files)
        git(self.repo, "diff", "--cached", "--check")
        git(self.repo, "commit", "-m", spec.title)
        git(self.repo, "push", "-u", "origin", spec.branch)
        existing = self.process_runner.run(["gh", "pr", "view", spec.branch, "--json", "url,isDraft"], self.repo, 60)
        body = self.build_pr_body(spec, state)
        body_path = self.repo / ".agent-loop" / "runs" / state.run_id / "pr-body.md"
        body_path.parent.mkdir(parents=True, exist_ok=True)
        body_path.write_text(body, encoding="utf-8", newline="\n")
        if existing.exit_code == 0:
            try:
                pr_data = json.loads(existing.stdout)
            except json.JSONDecodeError as exc:
                raise PublicationError("existing PR metadata was not JSON") from exc
            if pr_data.get("isDraft") is not True:
                raise PublicationError("existing PR is not draft; refusing to update")
            update = self.process_runner.run(["gh", "pr", "edit", spec.branch, "--body-file", str(body_path)], self.repo, 60)
            if update.exit_code != 0:
                raise PublicationError(update.stderr or update.stdout)
            return str(pr_data.get("url") or "")
        base_branch = spec.base_ref.removeprefix("origin/")
        create = self.process_runner.run(
            ["gh", "pr", "create", "--draft", "--base", base_branch, "--head", spec.branch, "--title", spec.title, "--body-file", str(body_path)],
            self.repo,
            120,
        )
        if create.exit_code != 0:
            raise PublicationError(create.stderr or create.stdout)
        return create.stdout.strip()

    def build_pr_body(self, spec: TaskSpec, state: LoopState) -> str:
        checks = "\n".join(f"- {check.id}: {check.status}" for check in state.checks) or "- none"
        findings = "\n".join(f"- {finding.severity}: {finding.title}" for finding in state.findings) or "- none"
        return (
            f"## Task\n- ID: {spec.task_id}\n- Goal: {spec.goal}\n- Base SHA: {state.base_sha}\n- Head branch: {spec.branch}\n\n"
            f"## Scope\n" + "\n".join(f"- {path}" for path in spec.allowed_paths) + "\n\n"
            f"## Iterations\n- iterations: {state.iteration}\n- codex turns: {state.codex_turns}\n- review turns: {state.review_turns}\n\n"
            f"## Checks\n{checks}\n\n## Findings\n{findings}\n\n"
            "## Policy\n- Draft PR only.\n- No auto-merge.\n- No automatic mark-ready.\n"
        )
