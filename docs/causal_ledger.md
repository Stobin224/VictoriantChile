# Causal Ledger v1

`PR 11` introduces the engine-level causal ledger that records and verifies exact deltas for visible simulation targets. It does not execute effects, aggregation, scheduler actions, events, reforms, or the final narrative turn report.

## Scope

The causal ledger records only realized deltas that have already happened. It does not infer intent, it does not recompute effect math, and it does not invent balancing residues to make accounting pass.

The public v1 surface is:

- `CauseCategory`
- `CauseRef`
- MVP visible-target catalog
- `TickCausalBuffer`
- immutable tick and period snapshots
- deterministic Top-N projection
- helper for explicit `SYSTEM:IG_CLOUT_NORMALIZE` deltas

## Cause Categories

The category set is closed and ordered exactly as frozen in `MVP-002`:

1. `DECISION`
2. `EVENT`
3. `REFORM`
4. `MOVEMENT`
5. `MODIFIER`
6. `SYSTEM`

`MODIFIER` causes require a single parent cause whose category is exactly one of:

- `DECISION`
- `EVENT`
- `REFORM`
- `MOVEMENT`

No other category may carry a parent in v1.

The visible display text is `{CATEGORY}:{id}`. The canonical key used for deterministic ordering includes the full parent chain when present, so two modifier causes with the same modifier ID but different parents remain distinct.

Reserved system identities exposed by the ledger are:

- `SYSTEM:CLAMP`
- `SYSTEM:ROUNDING`
- `SYSTEM:IG_CLOUT_NORMALIZE`

The ledger does not restrict all future system IDs to those three constants. It only reserves those exact identities.

## Visible Targets

The MVP-visible target surface is explicit and path-based:

- `metrics.*`
- `regions.{region_id}.support`
- `regions.{region_id}.tension`
- `regions.{region_id}.organization`
- `regions.{region_id}.rival_presence`
- `igs.{ig_id}.clout`
- `igs.{ig_id}.approval`
- `movements.{movement_id}.intensity`
- `movements.{movement_id}.direction`

Not visible in v1:

- all `internals.*`
- static regional resources:
  - `admin_capS`
  - `industry_capS`
  - `extractive_capS`
  - `social_capS`
  - `populationS`

Visibility is resolved from exact `TargetPath` instances, not from substring heuristics or `TargetConfig` UI metadata.

`CreateForMvp(...)` consumes explicit canonical ID order from the caller, which is the path used when the Content Pack order is available. `CreateCanonicalFromState()` is the stable fallback when only a `GameState` is available; it derives the same visible surface but orders dynamic IDs ordinally so the result never depends on incidental dictionary or collection ordering.

## Tick Buffer Lifecycle

`TickCausalBuffer` is mutable only while the tick is open:

1. create the buffer for a non-negative tick;
2. `TrackTarget(target, initialValueS)` once per visible target;
3. `RecordContribution(target, cause, realizedDeltaS)` zero or more times;
4. `CloseTarget(target, finalValueS)` once;
5. `Seal()`.

The buffer fails closed when:

- a target is not visible;
- a baseline is duplicated;
- a contribution arrives before the baseline;
- a contribution or second close arrives after close;
- any target remains unclosed at seal time;
- arithmetic overflows;
- `finalValueS - initialValueS` does not equal the exact summed contributions.

The ledger never invents residual causes. The mutating system that performs clamp, rounding, or clout normalization must record those deltas explicitly.

### Clamp Example

Given:

- start value: `9000`
- modifier contribution: `+2000`
- configured clamp to `10000`

The ledger must receive:

- modifier cause: `+2000`
- `SYSTEM:CLAMP`: `-1000`

That produces:

- observed delta: `+1000`
- summed causal delta: `+1000`

If the `SYSTEM:CLAMP` entry is missing, the ledger rejects the target as an accounting mismatch.

### Audited vs. Changed Targets

Tick and period snapshots preserve two deterministic views:

- `AuditedTargets`: every target that was explicitly audited for continuity;
- `ChangedTargets`: the public filtered view whose `delta_totalS != 0`.

That separation is required so an audited zero-delta tick can preserve continuity without polluting the public Top-N surface.

### Zero-Delta Targets

The public v1 `ChangedTargets` view omits targets whose net delta is zero. That includes cases where non-zero contributing causes cancel to zero. The sealed snapshot still preserves those targets inside `AuditedTargets`, so later period accumulation can verify continuity across zero-delta ticks and fail closed if a previously audited target disappears from the audited surface.

## Tick and Period Snapshots

Tick snapshots are immutable and ordered deterministically:

- targets by ordinal `TargetPath`
- contributions by ordinal `CauseRef`

Period accumulation combines strictly consecutive tick snapshots. It validates:

- at least one tick;
- no gaps;
- no duplicate ticks;
- target continuity across repeated appearances;
- exact accounting after accumulation;
- continuity through audited zero-delta ticks;
- fail-closed rejection if a previously audited target disappears before the period ends.

If a target does not change during an intermediate tick, the accumulator preserves its audited baseline/final pair internally and keeps it out of the public changed-target projection. If a target was never audited earlier in the period, it may appear later for the first time. Once a target has been audited, however, it must remain present in every subsequent audited tick or the period fails closed.

## Top-N Projection

The engine-level Top-N projection matches `MVP-010`:

- include only visible targets with `delta_totalS != 0`;
- at most 10 targets;
- target order:
  1. `abs(delta_totalS)` descending
  2. `target_path` ordinal ascending
- per target, at most 3 causes;
- cause order:
  1. `abs(delta)` descending
  2. canonical `CauseKey` ordinal ascending
- `other_deltaS` is the exact sum of all omitted causes.

The ledger computes magnitudes safely and does not rely on `Math.Abs(int.MinValue)`.

## Boundaries with Later PRs

This PR does not execute or schedule anything. Planned integration remains:

- `PR 12`: effect engine records realized ADD/MUL/SET deltas plus `SYSTEM:CLAMP` and `SYSTEM:ROUNDING`
- `PR 14`: aggregation engine records its visible causal outputs
- `PR 15`: territory drift and pullback
- `PR 17`: movement updates
- `PR 20`: final `TurnReport` presentation and alert projection

The ledger is the fail-closed accounting core. Future systems must conform to it; the ledger will not bend to compensate for incomplete causal registration.
