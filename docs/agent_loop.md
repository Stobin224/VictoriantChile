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

## Codex JSONL Framing

`codex exec --json` is parsed as a byte stream, not as pre-split text lines. The supervisor uses an incremental strict UTF-8 JSONL decoder so JSON records may arrive split across pipe chunks, inside strings, or inside multibyte UTF-8 characters. Multiple records may also arrive in one chunk, and the final record may omit a trailing newline.

Framing policy:

- LF and CRLF delimit records.
- A single trailing record without newline is accepted if it is complete JSON.
- Completely empty lines are ignored.
- Non-empty non-JSON lines fail closed.
- A UTF-8 BOM is rejected.
- Each JSONL root must be an object.
- stdout and stderr are captured separately; stderr is never parsed as JSONL.

The parser enforces bounded stdout size, line size, and event count. On parse failure, public state records only a stable diagnostic with line/offset, stdout size and SHA-256, and a short excerpt. Raw stdout/stderr are retained only as ignored local evidence under `.agent-loop/runs/<run-id>/`.

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

## Writer And Reviewer Execution

The writer is the only write-capable model turn. Initial writer turns run with `workspace-write` and the repository root as the explicit working root. Correction turns resume the same writer thread and keep the same write-capable workspace. The writer prompt must ask for implementation now, not a plan, while making clear that Git publication remains controlled by the supervisor.

The writer is told explicitly that repository checks belong to the supervisor, not to the model turn. The writer must not run the full Python suite, `scripts/run_checks.py`, Unity, wrappers, repository-wide diagnostics, or sandbox/ACL/TEMP investigations inside Codex. Failed official checks are summarized back into correction turns as actionable diagnostics, but the writer is still instructed to implement fixes rather than reproduce the checks under the sandbox. On Windows the prompt also states that the interactive shell is PowerShell and that POSIX heredocs are not valid there.

The reviewer is a separate read-only turn with structured output. Reviewer prompts explicitly forbid modifying files, staging, committing, pushing, or opening PRs.

The supervisor treats Git as the source of truth. A writer's final response may claim changed paths, but the supervisor compares those claims against the actual working tree. Git reconciliation happens after every writer turn before terminal state is persisted, even when the turn ends with recoverable command failures, `needs_input`, or another non-successful model result. Real changed files, scope violations, and claim discrepancies are therefore recorded from Git rather than inferred from the model response.

If a writer produces only internal `command_execution` failures but Git shows valid in-scope progress, the supervisor still runs the authoritative host-side checks and can continue to reviewer. If there is no real progress, or if failures involve unsafe tool categories, invalid JSONL, launch failure, timeout, usage limit, protected-path writes, or an unauditable Git state, the loop fails closed.

Each Codex turn also receives a private runtime temp directory under `.agent-loop/runs/<run-id>/runtime-tmp` through process-scoped `TEMP`, `TMP`, and `TMPDIR` overrides. This temp root is validated as an ordinary directory under the run evidence root, never written into user or system environment settings, and never inherited by the host-side supervisor checks. If the runtime temp is created by the current run and remains empty, the supervisor removes it with a single `os.rmdir(...)` at terminal cleanup. Non-empty content is preserved as ignored local evidence instead of being deleted recursively.

On Windows with Codex sandbox `unelevated`, Git may reject host-created repositories as dubious ownership because the restricted token sees `Administrators` as deny-only. The supervisor injects an exact process-scoped `safe.directory` entry for the current repo into the Codex process environment by extending `GIT_CONFIG_COUNT/GIT_CONFIG_KEY_n/GIT_CONFIG_VALUE_n`. This applies only to the Codex process tree, preserves pre-existing valid `GIT_CONFIG_*` entries, never uses `safe.directory=*`, and never modifies `.git/config` or Git global/system configuration. Git may still consult the user's normal configuration files; the supervisor only guarantees that it does not edit them. `GIT_CONFIG_PARAMETERS` is treated as unauditable ambient override state and fails closed before Codex launches.

Codex may also create an empty `.agents/` directory inside the task repository as runtime infrastructure before any tool work. The supervisor snapshots `<repo>/.agents` before the first writer turn, allows it only while it remains the exact empty ordinary directory created during the run, and removes it at terminal cleanup only with a single `os.rmdir(<repo>/.agents)` if it still exists, is empty, and is not a symlink or reparse point. Any non-empty or unsafe `.agents` becomes a fail-closed runtime artifact violation.

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

Optional task-spec token limits may be recorded for `input_tokens` and `output_tokens`, but Codex only reports that usage after a turn completes. Those limits are therefore post-turn guardrails, not intraturn interrupt mechanisms. The truly hard controls remain process timeout, max turns, max iterations, and repeated-failure limits. If a post-turn token limit is exceeded, the supervisor records that overrun explicitly and will not start another writer or reviewer turn afterward, even though the completed turn's effects and host-side checks are still reconciled and persisted.

## Publication

Publication requires both:

1. the task spec sets `publication.commit`, `publication.push`, and `publication.draft_pr` to `true`;
2. the operator passes `--publish`.

The supervisor stages an explicit audited file list, commits, pushes, and opens or updates a draft PR. It never merges, marks ready, force-pushes, rebases, deletes branches, or modifies branch protection.

## Evidence

Local resumable state is written atomically under `.agent-loop/runs/<run-id>/state.json`. Public PR evidence avoids auth paths, tokens, machine-specific paths and unlimited logs.
