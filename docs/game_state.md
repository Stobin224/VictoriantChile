# Game State Baseline

PR 5 adds the first deterministic initial-state layer. It does not add effects, ticks, scheduling, aggregation, persistence, UI, or scene integration.

## Assembly Boundary

- `VictoriantChile.Simulation.Core` owns pure state models, the closed initial-target registry, and clout normalization.
- `VictoriantChile.Content` adapts a validated `ContentPack` into a `GameState`.
- Core does not reference Content, Newtonsoft, Unity APIs, files, JSON, hashes, clocks, or machine state.

## GameState Shape

`GameState` is immutable after construction and uses defensive snapshots for public collections.

- `StateSchemaVersion = 1`
- `Tick = 0`
- `RngSeed` is provided explicitly by the caller
- `ContentMetadata`
- national metrics
- internal domains and components
- regions
- interest groups
- movements

The state intentionally does not copy static content definitions such as names, tags, macrozones, weights, regional capacity fields, or population fields.

## Initial Targets

The initial state uses a closed registry of concrete `TargetPath` values. It does not discover state by expanding arbitrary wildcards.

- 10 national metrics
- 4 mutable regional values per region
- 2 mutable interest-group values per IG
- 2 mutable movement values per movement
- 38 internal components

Every concrete target is resolved through the `TargetConfigCatalog` loaded from the Content Pack. The initial value is exactly the winning `TargetConfig.DefaultS`. Missing configs or incompatible defaults fail closed.

## Clout Normalization

IG clout starts from `igs.*.clout` defaults and requires `normalize_group = igs.clout_sum_100`.

Normalization is integer-only:

1. Sort IGs by ID using ordinal comparison.
2. Reject empty input, empty IDs, duplicates, negative values, zero total, and overflow.
3. Compute each base value with floor: `raw_i * 10000 / raw_total`.
4. Compute residue: `10000 - sum(base_i)`.
5. Assign all residue to the IG with the greatest raw clout; ties use the ordinal-smallest IG ID.
6. Verify the final sum is exactly `10000`.

For the current real pack, nine raw values of `1111` produce eight `1111` values and one `1112`; the residue goes to `ig_ambiental_regionalista`.

## Content Metadata

The state stores the identity of the content used to create it:

- content pack version
- content schema version
- minimum game schema version
- default language
- all manifest file path/hash pairs

Files are sorted by relative path with ordinal comparison. The state stores canonical hashes already verified by the loader and does not store physical directories, timestamps, machine names, or load times.

## Determinism

Equivalent packs and the same explicit seed produce structurally equivalent states. Changing the seed only changes `RngSeed` in this PR because no random generation exists yet.

All public state collections are snapshots ordered by ordinal ID/domain. The factory does not depend on dictionary iteration order, culture, clocks, or generated IDs.

## Out of Scope

- GameState persistence
- effects and mutation traces
- scheduler or ticks
- legislation, events, crises, aggregation, or reforms
- JSON loading inside Core
- Unity scene or UI integration
- .NET fast path
