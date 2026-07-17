# Agent Contract

This repository is prepared for small, reviewable agent tasks. Follow these rules exactly.

## Operating Rules

- Do not work directly on `main`.
- Keep tasks and PRs small.
- Do not mix content, engine, and UI changes in one task unless the task explicitly authorizes it.
- Inspect the real repository state before modifying files.
- Do not run destructive commands without explicit authorization.
- Do not incidentally modify `Packages/`, `ProjectSettings/`, generated Unity files, Plastic files, scenes, or Unity assets.
- Preserve `.meta` files whenever Unity assets are intentionally moved, created, or deleted.
- Do not add Unity AI Assistant, agent frameworks, or external packages unless the active task explicitly authorizes it.
- Do not commit, push, open a PR, or merge unless the task explicitly authorizes it.
- Never auto-merge.

## Content Changes

Content changes require:

- JSON syntax validation.
- Non-destructive manifest hash verification.
- Semantic content validation.
- Content/runtime contract smoke.
- Manifest version bump when required by the content versioning policy.

Use the canonical local command:

```bash
python scripts/run_checks.py
```

Use `python scripts/recompute_manifest_hashes.py` only when intentionally updating content hashes. It modifies `manifest.json`; it is not a validation check.

## Bounded Agent Loop

- `python scripts/run_agent_loop.py` is the local supervisor for future bounded tasks.
- It runs one task on one branch and never merges or marks a PR ready.
- It uses local Codex CLI authentication; do not add API keys, direct API calls, or monetary cost accounting.
- Do not run the loop recursively or use internal subagents unless a later task explicitly authorizes it.

## Simulation Contract

- Future persisted engine state must not use `float` or `double`; use integer fixed-point values.
- The general simulation scale is `S=100`, but ranges belong to `TargetConfig`. Do not assume every target is `0..10000`.
- Every future visible mutation must have a traceable cause.

## Agent Delivery

Final delivery must include:

- Files changed.
- Commands executed.
- Results.
- Checks skipped and why.
- Risks and remaining work.
