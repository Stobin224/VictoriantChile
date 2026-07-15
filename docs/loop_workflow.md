# Loop Workflow

This repository starts with one agent. Multi-agent coordination is out of scope for now.

## Flow

Task defined -> isolated branch -> inspection -> small change -> `run_checks` -> failure analysis -> scoped repair -> evidence -> human review -> PR -> manual merge.

## Iterations

Each task must set a configurable maximum iteration count. Start with `3` iterations until the checks and review process are proven reliable.

An iteration is one cycle of change, checks, analysis, and scoped repair.

## Stop Conditions

Stop and report if:

- The same failure repeats without new evidence.
- The fix requires expanding the authorized scope.
- A design decision is missing.
- A mandatory check cannot be executed.
- Unrelated changes appear in the worktree.
- Credentials or unauthorized external actions would be required.

Do not auto-merge. Do not run destructive auto-rebase or reset flows.

## Readiness Levels

`agent-ready` means an agent can work from a bounded task, isolated branch, and reproducible checks with structured feedback.

`loop-testable` means repeated autonomous iterations can be tested against known fixtures and failure modes.

`autonomous loop` means a loop can select, repair, validate, and hand off tasks under policy without human direction for each step.

This baseline only targets `agent-ready` with structured validation. It does not implement autonomous loops or multi-agent work.

## Required Evidence

Every agent delivery must include changed files, commands, results, skipped checks with reasons, risks, and remaining work.
