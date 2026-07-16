# Simulation Primitives

This baseline adds the first pure C# simulation primitives. It does not add `GameState`, effects, scheduler, persistence, JSON loading, or runtime ticks.

## Fixed Math

`FixedMath.Scale = 100` represents two decimal places. A visible value of `100.00` is stored as `10000`.

Multipliers use `FixedMath.MultiplierBaseS = 10000`, where `10000` means `1.00`. This is separate from `Scale`.

Division and scaled multiplication use `HALF_AWAY_FROM_ZERO` rounding. Overflow is not saturated silently: checked conversions and checked arithmetic throw `OverflowException`. `Clamp` is the only explicit saturating operation.

## Target Paths

`TargetPath` is a concrete mutation/read path. It accepts ASCII lowercase snake_case segments separated by dots, with no whitespace normalization and no wildcard.

Supported shapes:

- `metrics.<metric>`
- `regions.<region_id>.<field>`
- `igs.<ig_id>.<field>`
- `movements.<movement_id>.<field>`
- `internals.<group>.<field>`

`TargetPath` validates syntax, namespace, and arity only. It does not check whether a specific content id exists.

There is one inherited closed exception to lowercase snake_case: the read-only regional static fields `admin_capS`, `industry_capS`, `extractive_capS`, `social_capS`, and `populationS` are valid only as the third segment of `regions.<region_id>.<field>` paths or `regions.*.<field>` patterns. They are not `TargetConfig` entries and are not mutable targets.

## Target Patterns

`TargetPattern` uses the same namespace and arity rules as `TargetPath`, but allows `*` as a whole segment. A wildcard matches exactly one segment. Partial wildcards and globstar are invalid.

Examples:

- `metrics.*`
- `regions.*.support`
- `igs.*.approval`
- `internals.*.*`

## Target Config Matching

`TargetConfigCatalog` stores configs in load order and resolves a `TargetPath` by deterministic precedence:

1. Exact pattern before wildcard pattern.
2. More literal segments.
3. Longer canonical pattern text.
4. If still tied, earlier load order wins.

This mirrors the content validator's target config matching contract without loading JSON in C#.

## Out Of Scope

This PR does not implement the engine state, effect application, target mutation logic, scheduler, causal trace, persistence, JSON bootstrap, UI labels, qualitative bands, trends, or normalization behavior.
