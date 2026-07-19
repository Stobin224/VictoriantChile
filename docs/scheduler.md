# Scheduler and Time Advancement

PR 13 introduces the deterministic scheduler kernel that advances weekly time without implementing future gameplay systems.

## Frozen Tick Order

The canonical phase sequence is:

1. `increment_tick`
2. `expire_effects`
3. `execute_scheduled_actions`
4. `apply_start_instant_modifiers`
5. `apply_per_tick_modifiers`
6. `revert_internals`
7. `derive_internals`
8. `aggregate_national_metrics`
9. `drift_national_to_regions`
10. `pull_regions_to_internals`
11. `update_movements`
12. `advance_reforms`
13. `resolve_events_and_crises`
14. `apply_final_clamps_and_normalizations`
15. `close_causal_report`
16. `detect_and_publish_blocking_decision`

PR 13 executes phases 1, 2, 3, 4, 5, 15, and 16. Phases 6 through 14 remain explicit deterministic no-op hooks until later PRs implement those systems.

Regional feedback remains frozen at `regional_feedback_latency_ticks = 1`. PR 13 only preserves that hook boundary; it does not implement the feedback system itself.

## ScheduledAction

Each scheduled action is immutable and persisted in `GameState` with:

- `id`
- `run_tick`
- `priority`
- `type`
- `source`
- `payload`

Queue order is deterministic:

1. `run_tick` ascending
2. `priority` descending
3. `id` ordinal ascending

Due actions selected for the current tick execute in:

1. `priority` descending
2. `id` ordinal ascending

An overdue action (`run_tick < current tick`) fails closed.

Actions created during a tick never reenter the same tick.

## Blocking Decisions and Deadlines

Deadlines are modeled as scheduled actions. Their handlers may emit a `BlockingDecision`.

Blocking decisions persist in `GameState` and become effective only at the final phase of the tick, after the causal report closes.

If a state is already blocked, no new tick starts. `AdvanceWeeks(1|4|12)` stops at the first completed tick that publishes a block.

## Effect Engine Integration

The scheduler does not reimplement effect math.

For each tick it:

- increments `GameState.Tick`;
- removes expired effects where `end_tick_exclusive <= current tick`;
- executes due actions;
- applies start instants exactly once;
- applies active per-tick effects;
- relies on the effect engine for SET/ADD/MUL order, rounding, clamp residue, and IG clout normalization.

An action that registers an effect in phase 3 can still trigger that effect's start-instant phase in phase 4 of the same tick.

## Causal Accounting

Each tick creates one `TickCausalBuffer` and audits the full MVP visible target surface. The public invariant remains:

```text
final_value - initial_value = exact sum of causal contributions
```

PR 13 intentionally rejects scheduled direct mutations against visible targets. Visible changes must flow through the effect engine so clamp, rounding, and normalization residues stay explicit and exact. Hidden targets may still be mutated by scheduled actions.

## RNG Contract

PR 13 materializes the frozen RNG contract with:

- algorithm: `pcg32-xsh-rr`
- contract version: `pcg32-v1`
- multiplier: `6364136223846793005`
- serialized fields:
  - `state_u64`
  - `stream_u64`
  - `draw_count_u64`
- byte order: `little-endian`
- warm-up draws: `0`

All three fields serialize as lowercase fixed-width hexadecimal (`hex_lowercase_16`).

The persisted stream is always odd.

### Global Sequential Draws

Sequential initialization uses this exact preimage:

```text
ASCII("VictoriantChile/pcg32-v1/init")
+ byte 0x00
+ INT64_LE_TWOS_COMPLEMENT(seed)
```

The SHA-256 digest maps to:

- bytes `0..7` -> `state_u64` (little-endian)
- bytes `8..15` -> `stream_u64` (little-endian), then `| 1`
- `draw_count_u64` starts at `0`

The persisted `Pcg32State` is the only sequential runtime RNG state. A successful sequential draw advances:

- `state_u64`
- `draw_count_u64`

Each raw draw uses:

```text
old_state = state_u64
new_state = old_state * 6364136223846793005 + stream_u64 (mod 2^64)
xorshifted = uint32((((old_state >> 18) xor old_state) >> 27))
rotation = uint32(old_state >> 59)
output = rotate_right_32(xorshifted, rotation)
```

If `draw_count_u64` is already `UINT64_MAX` before a draw, the runtime fails closed without changing `state_u64`, `stream_u64`, or the counter.

The draw count does not advance on failed ticks because failed ticks never publish a new `GameState`.

Bounded draws use rejection sampling with:

```text
threshold = (2^32 - bound) mod bound
```

Invalid bounds fail before consuming RNG. Rejected raw draws still increment `draw_count_u64`.

### Keyed Draws

Keyed selector draws are derived independently from:

- seed
- tick
- system
- template
- slot

Encoding is UTF-8. Derivation uses SHA-256. PR 13 materializes the frozen `byte_order = documented_explicitly` requirement as:

```text
ASCII("VictoriantChile/pcg32-v1/event-selector")
+ byte 0x00
+ INT64_LE_TWOS_COMPLEMENT(seed)
+ UINT64_LE(tick)
+ UINT32_LE(byte_length(system_utf8))
+ system_utf8
+ UINT32_LE(byte_length(template_utf8))
+ template_utf8
+ UINT64_LE(slot)
```

`system` and `template` must match `[a-z0-9][a-z0-9._-]*` and remain strict ASCII. Empty values, whitespace, control characters, newlines, Unicode, and NULs are rejected.

The keyed SHA-256 digest maps to:

- bytes `0..7` -> little-endian `keyed_state_u64`
- bytes `8..15` -> little-endian `keyed_stream_u64`, then `| 1`
- first `pcg32-xsh-rr` output from that derived state -> keyed draw

There is no warm-up and no digest-to-tie-break helper in v1. Final selector ties remain `id` ordinal ascending as frozen in PR 8.

Keyed draws do not consume the persisted global sequential counter. This preserves the frozen requirement that unrelated keyed selectors do not perturb each other.

## GameState Schema

PR 13 bumps `state_schema_version` to `3` and adds canonical persisted fields for:

- `rng`
- `scheduled_actions`
- `blocking_decision`

`save_version` remains untouched. PR 13 only changes the canonical simulation-state schema and hash surface.

## ScenarioRunner Boundary

`ScenarioRunner` gains the engineering-only `ADVANCE` command for exactly `1`, `4`, or `12` weeks.

This is not a future gameplay action API. It exists only to exercise deterministic time advancement and replay evidence.
