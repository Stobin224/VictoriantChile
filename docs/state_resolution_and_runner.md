# State Resolution And Runner

PR 6 adds a deterministic runtime surface for reading and transforming the initial simulation state. It does not add effects, scheduling, ticks, aggregation, events, reforms, persistence, UI, or scene integration.

## Architecture

Dependency direction is fixed:

- `VictoriantChile.Simulation.Core`: target reader contracts, functional mutation, clamps, and invariant validation.
- `VictoriantChile.Content`: read-only static target source backed by a validated `ContentPack`.
- `VictoriantChile.Simulation.Runner`: scenario parsing, canonical state JSON, `state_hash`, and stable result JSON.
- `VictoriantChile.Simulation.Runner.Editor`: minimal Unity `-executeMethod` host for file IO and process exit codes.

`Core` does not reference Content, Runner, Unity, JSON, IO, or hashing. Unity APIs are limited to the Editor host.

## Target Reads

Runtime target reads are concrete `TargetPath` values only. Wildcards remain configuration selectors.

Dynamic state targets are resolved from `GameState`:

- `metrics.{metric}`
- `internals.{domain}.{component}`
- `regions.{region_id}.support`
- `regions.{region_id}.tension`
- `regions.{region_id}.organization`
- `regions.{region_id}.rival_presence`
- `igs.{ig_id}.clout`
- `igs.{ig_id}.approval`
- `movements.{movement_id}.intensity`
- `movements.{movement_id}.direction`

Regional static resources are read-only and resolved from `ContentPack` only:

- `regions.{region_id}.admin_capS`
- `regions.{region_id}.industry_capS`
- `regions.{region_id}.extractive_capS`
- `regions.{region_id}.social_capS`
- `regions.{region_id}.populationS`

For `regions`, dynamic fields are checked first, then the closed static-resource set. Names, tags, macrozone, and weights are not target-readable.

## Mutations

Mutations are functional. A successful mutation returns a new `GameState`; the original snapshot is unchanged. A failed mutation returns no state and at least one diagnostic.

Supported operations are controlled by the resolved `TargetConfig`:

- `ADD`: `current + valueS` using checked integer arithmetic, followed by explicit clamp.
- `MUL`: `current * factorS / 10000`, rounded HALF_AWAY_FROM_ZERO, followed by explicit clamp.
- `SET`: requested value, followed by explicit clamp.

`TargetConfig.Scale` is not the multiplier base. `FixedMath.MultiplierBaseS == 10000` is always used for `MUL`.

Static regional resources are rejected with `target.read_only`. Movement direction must be exactly `-1` or `+1`; `0` is invalid even though the range is `-1..1`.

## Clout

Mutating `igs.*.clout` applies the requested operation to one raw value, then normalizes the whole `igs.clout_sum_100` group atomically with `CloutNormalizer`.

The normalizer:

- sorts IDs with `StringComparer.Ordinal`;
- computes `floor(raw_i * 10000 / total)` using integer arithmetic;
- gives all residue to the highest raw clout;
- breaks ties by ordinal smallest ID;
- verifies the final sum is exactly `10000`.

Unknown normalization groups fail closed.

## Invariants

`GameStateInvariantValidator` checks:

- supported state schema;
- `Tick >= 0`;
- valid content metadata;
- exact initial target registries;
- target config resolution for every dynamic target;
- values within config ranges;
- clout sum exactly `10000`;
- movement direction exactly `-1` or `+1`;
- deterministic, ordinally identifiable collections.

`GameStateFactory` runs this validator before returning an initial state.

## Scenario Schema V1

Scenario JSON is strict UTF-8 and rejects malformed JSON, duplicate properties, comments, trailing content, unknown properties, non-canonical targets, bool-as-int, and future schema versions.

Example:

```json
{
  "scenario_schema_version": 1,
  "seed": 424242,
  "commands": [
    {
      "id": "read_legitimacy",
      "type": "READ",
      "target": "metrics.legitimacy"
    },
    {
      "id": "raise_legitimacy",
      "type": "MUTATE",
      "target": "metrics.legitimacy",
      "op": "ADD",
      "value_s": 6000
    }
  ]
}
```

Command IDs are ASCII lowercase snake_case and unique. `READ` has no `op` or `value_s`; `MUTATE` requires both.

## Result JSON

The result schema is stable and always includes:

- `result_schema_version`
- `status`
- `scenario_schema_version`
- `seed`
- `command_count`
- `commands`
- `state_hash`
- `state`
- `diagnostics`

On success, `state` and `state_hash` are present. On failure, both are `null` and diagnostics are non-empty. Result JSON excludes timestamps, durations, physical paths, editor paths, process IDs, and machine data.

## Canonical State And Hash

`state_hash` is `sha256:<64 lowercase hex>` over compact UTF-8 canonical state JSON without BOM. It includes:

- state schema;
- tick;
- seed;
- content identity and manifest hashes;
- all dynamic state values.

It does not include the `state_hash` itself. Pretty result JSON uses two-space indentation and exactly one final LF.

## CLI

Run the smoke scenario through Unity:

```bash
python scripts/run_scenario.py --scenario tests/scenarios/smoke_v1.json --json-output "%TEMP%/VictoriantChile/scenario.json"
```

Optional flags:

- `--unity-editor <path>`: use an explicit Unity executable.
- `--content-root <path>`: override the Content Pack root. Defaults to `Assets/StreamingAssets/content`.
- `--timeout-seconds <seconds>`: finite process timeout.

Repository checks keep Unity opt-in:

```bash
python scripts/run_checks.py --include-unity-scenario --unity-editor "C:\Program Files\Unity\Hub\Editor\6000.3.10f1\Editor\Unity.exe"
```

Exit codes:

- `0`: scenario passed.
- `2`: scenario, content, command, or invariant failure.
- `3`: unexpected runner infrastructure failure.
