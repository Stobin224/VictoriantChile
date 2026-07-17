# Bounded Agent Loop

This repository can run one bounded local development loop after a human provides a strict JSON task spec.

The loop does not wake itself up, does not read a backlog, and does not run in CI. It starts only when an operator runs `python scripts/run_agent_loop.py`.

## Scope

Version 1 supports:

- one task;
- one branch;
- one writer Codex session;
- a separate read-only review turn;
- deterministic external checks;
- scope auditing after each write-capable turn;
- resumable local evidence under `.agent-loop/runs/<run-id>/`;
- optional publication only with double authorization.

It does not support multi-task planning, recursive subagents, automatic issue selection, scheduled work, merge, mark-ready, force-push, destructive rebase, branch deletion, gameplay work, UI work, effects, scheduler, events, reforms or persistence.

## Codex CLI

The supervisor reuses the local Codex CLI authentication, normally ChatGPT login. It does not use `OPENAI_API_KEY`, `CODEX_API_KEY`, direct API calls, or cost accounting.

Codex executable discovery order:

1. `--codex-executable`;
2. `CODEX_EXECUTABLE`;
3. `PATH`;
4. standalone Windows install under `%LOCALAPPDATA%/Programs/OpenAI/Codex/bin/codex.exe`;
5. common standalone Unix locations.

Every candidate is verified with `--version`. The private WindowsApps application executable is rejected because it is not a usable external CLI from PowerShell.

The validated CLI for this baseline supports:

- `codex exec`;
- `codex exec --json` JSONL events;
- `codex exec --sandbox read-only|workspace-write|danger-full-access`;
- `codex exec --output-schema <file>`;
- `codex exec resume [SESSION_ID] [PROMPT]`;
- `codex review`, although structured review uses `codex exec` because `codex review` does not expose JSON/output-schema flags.

## Commands

Validate a task spec without mutation:

```bash
python scripts/run_agent_loop.py validate --task docs/agent_tasks/examples/bounded_loop_smoke.json
```

Preflight without invoking a model:

```bash
python scripts/run_agent_loop.py preflight --task docs/agent_tasks/examples/bounded_loop_smoke.json
```

Run a loop:

```bash
python scripts/run_agent_loop.py run --task path/to/task.json --json-output path/to/result.json
```

Run and publish only if the task spec also authorizes commit, push and draft PR:

```bash
python scripts/run_agent_loop.py run --task path/to/task.json --publish --json-output path/to/result.json
```

Resume a compatible checkpoint:

```bash
python scripts/run_agent_loop.py resume --run-id <run-id> --json-output path/to/result.json
```

## Task Contract

Task specs are JSON only. Unknown properties, duplicate properties, shell-string checks, unknown placeholders, API keys, monetary cost fields, `merge=true`, and `mark_ready=true` are rejected.

Allowed placeholders in check argv:

- `{python}`;
- `{repo}`;
- `{base_ref}`;
- `{branch}`.

Paths are relative to the repository and use `/`. A path ending in `/` means subtree. A path without trailing `/` means exact file. Protected paths win over allowed paths.

## States

Terminal states:

- `passed`;
- `needs_input`;
- `scope_violation`;
- `checks_failed`;
- `budget_exhausted`;
- `usage_limit_reached`;
- `tool_failure`;
- `publication_failed`.

Exit codes:

- `0`: passed;
- `2`: task/checks/review incomplete;
- `3`: needs input;
- `4`: scope violation;
- `5`: budget exhausted;
- `6`: tool or publication failure;
- `7`: usage limit reached.

## Budgets

The loop enforces hard limits for:

- iterations;
- Codex turns;
- review turns;
- wall time;
- repeated identical failure signatures.

Token telemetry from Codex JSONL is recorded when available. It is not converted to dollars and is not treated as remaining ChatGPT Plus quota.

## Publication

Publication requires both:

1. the task spec sets `publication.commit`, `publication.push`, and `publication.draft_pr` to `true`;
2. the operator passes `--publish`.

The supervisor stages an explicit audited file list, commits, pushes, and opens or updates a draft PR. It never merges, marks ready, force-pushes, rebases, deletes branches, or modifies branch protection.

## Evidence

Local resumable state is written atomically under `.agent-loop/runs/<run-id>/state.json`. Public PR evidence avoids auth paths, tokens, machine-specific paths and unlimited logs.
