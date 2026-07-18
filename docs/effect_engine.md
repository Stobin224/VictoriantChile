# Effect Engine

PR 12 introduces the deterministic effect engine that mutates `GameState` while preserving exact causal accounting.

## Scope

This engine:

- registers and stores active `EffectInstance` records;
- resolves stacking deterministically;
- applies start-instant and per-tick modifiers for an explicit tick;
- executes `SET`, `ADD`, and `MUL` in the frozen MVP order;
- applies final clamp residuals and IG clout normalization residuals explicitly;
- records exact visible deltas into the causal ledger.

This engine does not:

- advance `GameState.Tick`;
- schedule actions or deadlines;
- drive events, reforms, territory, aggregation, or movements;
- generate the narrative `TurnReport`.

PR 13 will orchestrate tick order and scheduler phases around these APIs.

## EffectInstance

Each runtime instance is immutable and caller-addressed:

- `Id`
- `TemplateId`
- `Origin`
- `StartTick`
- `EndTickExclusive`
- `StackKey`
- `StackMode`
- `StackLimitN`
- `Priority`
- `StartInstantApplied`

The engine never invents GUIDs, timestamps, or ambient IDs.

Visible causal identity is always:

`MODIFIER:{TemplateId}` with `Parent = Origin`

That means two instances with the same template but different parents remain distinct causes, while repeated applications from the same template and parent aggregate under one causal key.

## Stacking

Supported stack modes:

- `STACK`
- `REPLACE`
- `REFRESH`
- `MAX`
- `STACK_LIMIT_N`

All active instances sharing one `StackKey` must also share the same stack mode. `STACK_LIMIT_N` instances must also share the same limit.

### STACK

Adds the new instance without disturbing existing ones.

### REPLACE

Removes every active instance with the same `StackKey`, then adds the new one. Already-realized instant effects are not reverted.

### REFRESH

If no instance exists for the key, registers the new instance. Otherwise exactly one instance must exist and the engine preserves:

- existing instance ID;
- template ID;
- origin;
- start tick;
- priority.

Only `EndTickExclusive` is refreshed, using the later expiration. Refresh never shortens duration and never replays a start-instant modifier.

### MAX

V1 only accepts unambiguous comparisons:

- exactly one modifier per participating template;
- same target;
- same operation;
- operation `ADD` or `MUL`.

`SET` is rejected for `MAX`.

Strength:

- `ADD`: `abs(valueS)`
- `MUL`: `abs(valueS - 10000)`

Winner tiebreak:

1. greater strength
2. greater priority
3. lower instance ID ordinal

### STACK_LIMIT_N

Adds the new instance, then keeps at most `N` instances by evicting the oldest:

1. lower `StartTick`
2. lower instance ID ordinal

Already-realized instant effects remain realized.

## Explicit lifecycle

The engine exposes four explicit operations:

`RegisterEffect -> ApplyStartInstantModifiers -> ApplyPerTickModifiers -> RemoveExpiredEffects`

The caller supplies the tick explicitly. The engine never advances time by itself.

### RegisterEffect

Validates the instance, trims instances already expired at `GameState.Tick`, resolves stacking, and returns a new immutable `GameState`. It does not apply modifiers.

This keeps expired instances from winning `MAX`, blocking `REFRESH`, or consuming `STACK_LIMIT_N` slots. PR 13 must keep `GameState.Tick` aligned with the registration phase it is orchestrating.

### ApplyStartInstantModifiers

Applies modifiers with:

- `IsPerTick == false`
- `StartTick == tick`
- active instance
- `StartInstantApplied == false`

On success the returned state marks those instances as applied. Re-running the same phase against that returned state is idempotent.

### ApplyPerTickModifiers

Applies modifiers with:

- `IsPerTick == true`
- `StartTick <= tick`
- `tick < EndTickExclusive`, when present

### RemoveExpiredEffects

Removes active instances where:

`EndTickExclusive != null && EndTickExclusive <= tick`

It only trims the registry. It does not execute any modifier.

## Frozen mathematical order

Modifiers are grouped by target and sorted by:

1. priority descending
2. effect instance ID ordinal ascending
3. modifier index ascending

### If a SET exists

The winning `SET` is the first modifier in that order among `SET` modifiers only. Every `ADD` and `MUL` for the same target is suppressed and produces no contribution.

The winning modifier contribution is:

`raw_set_value - initial_target_value`

Then the engine applies one final clamp and records clamp residue separately as `SYSTEM:CLAMP`.

### If no SET exists

The engine applies:

1. every `ADD` sequentially in sorted order;
2. every `MUL` sequentially in sorted order;
3. one final clamp;
4. IG clout normalization when that target family was modified.

The engine does not group all adds into one opaque sum and does not multiply factors as one grouped product. Each realized modifier stays individually attributable.

## Rounding

`10000 == 1.00`

For each `MUL`:

`numerator = current * factorS`

Then:

- modifier contribution: `truncated - current`
- rounding residue: `rounded - truncated`
- system cause for residue: `SYSTEM:ROUNDING`

The engine uses the existing production fixed-point rounding primitive and does not use floats or decimals.

## Clamps

Global target bounds always apply.

Modifier-local clamps may only narrow that range. They can never widen it.

- one winning `SET` uses its own optional clamp;
- `ADD/MUL` mode uses the intersection of every participating local clamp with the global clamp.

If the clamp intersection is empty, the engine fails closed.

The engine applies one final clamp per target and records only the final residue as `SYSTEM:CLAMP`.

## Causal contract

For every visible target touched by the engine:

`final_value - initial_value = exact sum of causal contributions`

The effect engine does not seal the `TickCausalBuffer`. The caller must:

1. track visible baselines before the phase;
2. call the engine;
3. close targets after all tick phases;
4. seal later.

If a visible target lacks a tracked baseline, the engine fails before mutating state.

Hidden targets can still be mutated, but they do not create visible ledger entries in PR 12.

## IG clout normalization

If any `igs.*.clout` target changes:

1. raw effect results are applied;
2. final clamps are applied;
3. the full clout vector is normalized to an exact sum of `10000`;
4. every normalization delta is recorded as `SYSTEM:IG_CLOUT_NORMALIZE`.

The engine requires every visible clout target to be tracked before performing that batch. It never records partial normalization.

## Example

Starting value `9000`, visible target clamp `0..10000`:

- modifier cause adds `+2000`
- raw value becomes `11000`
- final clamp reduces it to `10000`

Causal result:

- modifier contribution: `+2000`
- `SYSTEM:CLAMP`: `-1000`
- observed delta: `+1000`

The ledger remains exact:

`10000 - 9000 = 2000 + (-1000)`

## Boundary with later PRs

- PR 13 will schedule and order tick phases.
- PR 14 will feed aggregation outputs into visible metrics.
- PR 15 will connect territory systems.
- PR 17 will connect movement runtime.
- PR 18 and PR 19 will connect events and reforms.
- PR 20 will project this exact ledger into the final `TurnReport`.
