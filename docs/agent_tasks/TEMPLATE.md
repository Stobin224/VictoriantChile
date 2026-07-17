# Agent Task Template

## Task ID

Required. Use a stable ID such as `loop-contract-001`.

## Objective

Required. State one bounded, falsifiable outcome. Avoid open-ended tasks such as "improve the game".

## Minimum Context

Required. List the facts, files, and design constraints needed to start.

## Branch/Base

Required. Name the base ref and expected working branch.

## Allowed Files or Areas

Required. List exact paths or path prefixes the task may change.

## Forbidden Areas

Required. List paths and categories that must not be changed.

## Verifiable Acceptance Criteria

Required. Each criterion must be observable by diff, command output, or review.

## Mandatory Commands

Required. Include commands the agent must run before delivery.

## Expected Output

Required. Describe successful command output or artifacts precisely enough to compare.

## Maximum Iterations

Required. Set a numeric loop limit.

## Stop Conditions

Required. Include conditions that require stopping and reporting instead of widening scope.

## Known Risks

Required. List likely failure modes or unclear assumptions.

## Final Evidence

Required. Specify the evidence the agent must provide at the end.

## Status

Required. One of: `pending`, `running`, `blocked`, `passed`, `failed`.

## JSON Contract

For bounded loop execution, use `docs/agent_tasks/TEMPLATE.json`. JSON task specs are strict: no duplicate properties, no unknown properties, no shell-string checks, no API keys or monetary budget fields, no `merge=true`, and no `mark_ready=true`.
