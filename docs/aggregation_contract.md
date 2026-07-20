# Aggregation Contract v1

PR 14 freezes the mathematical, temporal, causal, runtime-plan, and execution contract for national aggregation.

## Scope and Non-Scope

**In scope:**
- Fixed-point arithmetic domain.
- Phase dispatch: reversion (phase 6), derived internals (phase 7), metric aggregation (phase 8).
- Snapshot definitions.
- Reversion formula.
- Derived internal formulas.
- Metric aggregation formula (weighted target, elasticity, weekly cap, final clamp).
- Causal allocation algorithm (`ordered_prefix_counterfactual_marginal_v1`).
- Cause key grammar.
- Hidden internal policy.
- C# runtime plan, Content -> Core bridge, executor binding, Scheduler integration, and scheduler golden evidence.

**Not in scope:**
- New CausalLedger APIs.
- Content Pack data changes.
- Manifest changes.
- Non-scheduler golden changes.
- Phases 9 through 14, which remain explicit no-op hooks.

## Phase Dispatch

```
post-effects state (after phase 5: apply_per_tick_modifiers)
  |
  v
Phase 6: internal reversion  (INTERNAL_REVERSION passes)
  |   snapshot: post_reversion
  v
Phase 7: derived internals   (DERIVED_INTERNALS passes)
  |   reads post_reversion, writes pre-aggregation derived values
  v
Phase 8a: METRIC_AGGREGATION of nine primary metrics
  |   (economy, security, social_tension, public_agenda,
  |    information_quality, governability, legislative_capacity,
  |    party_organization, internal_cohesion)
  v
Phase 8b: METRIC_AGGREGATION of metrics.legitimacy
  |
  v
Phase 9-14: later phases
  ...
  v
Phase 15: close_causal_report
```

Content parses the editorial `AggregationConfig` once and compiles it into `AggregationRuntimePlan` when constructing `ContentPack`. Core and Scheduler consume only that runtime plan and the one-time `AggregationEngine` binding. Pass position within the config array does NOT define execution order: the bridge classifies semantically into reversion, derived, primary metrics, and legitimacy.

The two metric passes are not interchangeable by physical order. Primary contains exactly the nine non-legitimacy metrics; legitimacy contains exactly `metrics.legitimacy`. Execution order is always reversion -> derived -> primary -> legitimacy.

## Numeric Domain

| Property | Value |
|---|---|
| Scale S | 100 |
| midS | 5000 |
| minS | 0 |
| maxS | 10000 |
| PPM denominator | 1,000,000 |
| Intermediate arithmetic | `long` checked |
| Stored type | `int` |
| Rounding | `HALF_AWAY_FROM_ZERO` |
| `half_life_weeks` | Human/editorial metadata only |

**Forbidden:** `float`, `double`, `decimal`, dictionary order, componentwise rounding, culture-dependent rounding, silent overflow.

### Rounding Vectors

`RoundHalfAwayFromZero(+500000 / 1000000) = +1`
`RoundHalfAwayFromZero(-500000 / 1000000) = -1`

## Snapshots

- Phase 6 receives the state after `apply_per_tick_modifiers`.
- Reversion produces the `post_reversion` snapshot.
- Phase 7 reads `post_reversion`.
- `DERIVED_INTERNALS` publishes its results before phase 8 begins.
- Derived expressions read pre-aggregation metrics of the current tick.
- They do not read metrics that will be calculated later in phase 8.

## Pass Execution Atomicity

Each pass in phases 6, 7, and 8 executes under atomic semantics:

1. **Immutable snapshot:** The pass reads a frozen snapshot of its inputs captured at pass start. No mutation of external state occurs during planning.
2. **Full planning:** All outputs and causal contributions are computed and validated against the snapshot before any state is published.
3. **Single atomic publication:** State mutations and causal contributions are committed as one batch. External observers never see a partial pass.
4. **Fail-closed:** If validation or execution fails — duplicate target, overlapping reversion group, arithmetic overflow, invalid cause prefix, causal accounting mismatch, or ledger rejection — zero partial state, internals, metrics, or causal contributions are published.
   > **PR 14.2 scope:** Compile-time validation rejects only *exactly duplicate* pattern strings by canonical `TargetPattern` equality. Arbitrary pattern overlap resolution (e.g. `internals.economy.*` vs `internals.economy.growth`) is not attempted by PR 14.2 and is deferred to the future executor. The executor must expand patterns against concrete targets and reject any real overlap before atomic publication.
5. **Complete output for next pass:** The next pass in the phase order (6 → 7 → 8a → 8b) observes the full output of the previous pass.
6. **No cross-observation within pass:** Within the same pass, no rule observes partial outputs of another rule. Rule order is config order; dictionary order is forbidden.
7. **Fail-closed triggers:** missing target, duplicate target, overlapping reversion group (executor must resolve pattern overlaps beyond exact duplicates), arithmetic overflow, out-of-range conversion, invalid cause prefix, causal accounting mismatch, ledger rejection.
8. **Fail-closed guarantees:** zero partial state, zero partial internals, zero partial metrics, zero partial causal contributions.

## Reversion Formula

For each internal targeted by the corresponding group:

```
distanceS = midS - currentS

reversion_deltaS =
    RoundHalfAwayFromZero(
        distanceS * alpha_ppm
        / 1_000_000
    )

pre_clampS = currentS + reversion_deltaS

finalS = clamp(
    pre_clampS,
    TargetConfig.minS,
    TargetConfig.maxS
)
```

**Order:** subtract from midpoint, multiply by alpha, single rounding, add, final clamp.

**Rules:**
- `long` checked arithmetic before `int` cast.
- No extra weekly cap on reversion.
- `skip_targets` are excluded (currently `internals.legitimacy.performance` and `internals.legitimacy.social_tension_load`).
- Reversion patterns are expanded once against the 38 concrete `InitialTargetRegistry.Internals` targets. A zero-match pattern, concrete overlap, unmatched skip, uncovered internal, or missing target config fails closed before execution.

### Reversion Vector

`currentS = 6000`, `midS = 5000`, `alpha_ppm = 26307`
- `distanceS = -1000`
- `rounded_deltaS = -26`
- `finalS = 5974`

## Derived Internals

### `internals.legitimacy.performance`

- Operation: `SET`
- Expression: `AVG(metrics.economy, metrics.security, metrics.governability)`
- Uses post-effects, pre-aggregation values of the current tick.
- Sum uses `long` checked arithmetic.
- Division `AVG` applies `HALF_AWAY_FROM_ZERO` once.

### `internals.legitimacy.social_tension_load`

- Operation: `SET`
- Expression: `COPY(metrics.social_tension)`
- Uses the post-effects, pre-aggregation value of the current tick.

### Latency Consequence

Legitimacy has one-tick latency for structural changes that aggregation of economy, security, governability, or social_tension produces in the same tick. This is intentional and derives from the frozen order 6 -> 7 -> 8.

## Metric Aggregation Formula

```
weighted_offset_numerator =
    SUM(weight_ppm[i] * (componentS[i] - midS))

weighted_offsetS =
    RoundHalfAwayFromZero(
        weighted_offset_numerator
        / 1_000_000
    )

target_unclampedS =
    midS + weighted_offsetS

targetS =
    clamp(
        target_unclampedS,
        TargetConfig.minS,
        TargetConfig.maxS
    )
```

### Elasticity, Cap, and Final Clamp

```
distance_to_targetS =
    targetS - current_metricS

elastic_numerator =
    distance_to_targetS * alpha_ppm

elastic_deltaS =
    RoundHalfAwayFromZero(
        elastic_numerator
        / 1_000_000
    )

capped_deltaS =
    clamp(
        elastic_deltaS,
        -cap_per_weekS,
        +cap_per_weekS
    )

pre_finalS =
    current_metricS + capped_deltaS

final_metricS =
    clamp(
        pre_finalS,
        TargetConfig.minS,
        TargetConfig.maxS
    )

delta_totalS =
    final_metricS - current_metricS
```

**Pipeline order:**
1. Sum weighted offsets (all numerators before rounding)
2. Single rounding for weighted offset
3. Target clamp
4. Compute distance to target
5. Elasticity rounding
6. Weekly cap
7. Add to current metric
8. Final clamp

**Forbidden:**
- Swap cap and rounding.
- Apply cap to weighted target instead of delta.

### Economy Vector

`current metrics.economy = 5000`

| Component | Value | Weight |
|---|---|---|
| growth | 6000 | +350000 |
| unemployment | 4000 | -250000 |
| inflation | 5000 | -250000 |
| fiscal_stability | 6000 | +150000 |

- `weighted_offsetS = 750`
- `targetS = 5750`
- `elastic_deltaS = 62`
- `capped_deltaS = 62`
- `finalS = 5062`
- `delta_totalS = +62`

### Social Tension Vector

`current metrics.social_tension = 5000`

| Component | Value | Weight |
|---|---|---|
| cost_of_living | 6000 | +350000 |
| polarization | 6000 | +250000 |
| protest_activity | 4000 | +250000 |
| institutional_trust | 6000 | -150000 |

- `weighted_offsetS = 200`
- `targetS = 5200`
- `elastic_deltaS = 32`
- `finalS = 5032`
- `delta_totalS = +32`

### Weekly Cap Vector

`currentS = 5000`, `targetS = 10000`, `alpha_ppm = 292893`, `cap_per_weekS = 600`
- `elastic_deltaS = 1464`
- `capped_deltaS = 600`
- `finalS = 5600`

## Causal Algorithm: `ordered_prefix_counterfactual_marginal_v1`

For each metric define F(vector) as the full aggregation pipeline returning `final_metricS`.

Let:
- V0 = all components replaced by midS
- V1 = component 1 real, rest at midS
- V2 = components 1,2 real, rest at midS
- ...
- Vn = all components real

**Base contribution:**
`base_deltaS = F(V0) - current_metricS`
Cause: `SYSTEM:AGG.<metric>`

**Component contribution:**
`component_deltaS = F(Vi) - F(Vi-1)`
Cause: `SYSTEM:AGG.<metric>.<component>`

**Telescopic identity (required):**
`F(Vn) - current_metricS == base_deltaS + SUM(component_deltaS)`

**Rules:**
- Component order = exact config order.
- Zero contributions omitted from the ledger.
- No proportional split, largest remainder, Shapley, dictionary order, or invented residual.
- Rounding, target clamp, elasticity, and cap are part of F.
- Do NOT emit SYSTEM:ROUNDING or SYSTEM:CLAMP within aggregation marginal attribution.

### Economy Causal Vectors

| Call | Result |
|---|---|
| F(V0) | 5000 |
| F(V1) | 5029 |
| F(V2) | 5050 |
| F(V3) | 5050 |
| F(V4) | 5062 |

| Component | Delta |
|---|---|
| `SYSTEM:AGG.metrics.economy` (base) | 0 |
| `SYSTEM:AGG.metrics.economy.internals.economy.growth` | +29 |
| `SYSTEM:AGG.metrics.economy.internals.economy.unemployment` | +21 |
| `internals.economy.inflation` | 0 (omitted) |
| `SYSTEM:AGG.metrics.economy.internals.economy.fiscal_stability` | +12 |
| **Sum** | **+62** |

### Social Tension Causal Vectors

| Call | Result |
|---|---|
| F(V0) | 5000 |
| F(V1) | 5056 |
| F(V2) | 5095 |
| F(V3) | 5056 |
| F(V4) | 5032 |

| Component | Delta |
|---|---|
| base | 0 |
| `cost_of_living` | +56 |
| `polarization` | +39 |
| `protest_activity` | -39 |
| `institutional_trust` | -24 |
| **Sum** | **+32** |

### Execution Vector File

The versioned executable vector set is `tests/aggregation/aggregation_execution_v1_vectors.json`; `tests/python/test_aggregation_execution_contract.py` recalculates its expected values independently of Unity/C#.

## CauseKey Grammar

CauseRef builds the key as:

```
CATEGORY + ":" + ID
```

The ID must not contain: `:`, `|`, whitespace, control characters, or non-ASCII Unicode.

**Permitted prefixes for PR 14:** `AGG.`, `REVERSION.`, `DERIVED.`

**Examples:**
- `SYSTEM:AGG.metrics.economy`
- `SYSTEM:AGG.metrics.economy.internals.economy.growth`
- `SYSTEM:REVERSION.internals.economy.growth`
- `SYSTEM:DERIVED.internals.legitimacy.performance`

**Forbidden ambiguous forms:**
- `SYSTEM:AGG:metrics.economy`
- `SYSTEM:AGG:metrics.economy:internals.economy.growth`

Do not modify CauseRef in PR 14.1.

## Hidden Internals and Causality

- `internals.*` remains hidden:
  - No public target catalog entry.
  - No own row in TickCausalBuffer.
  - No Top-N slot consumption.
  - No accidental TurnReport exposure.
- Reversion and derived internal provenance is recorded internally:
  - `SYSTEM:REVERSION.<internal_target>`
  - `SYSTEM:DERIVED.<internal_target>`
- When an internal influences a visible metric, the public influence appears via:
  - `SYSTEM:AGG.<metric>.<internal_target>`
- No double counting: the same influence is NOT registered as REVERSION, DERIVED, AND AGG simultaneously.
- Provenance scope: `ephemeral_execution_plan_only`
- Provenance is not serialized to any persistent store.
- Not stored in GameState.
- Not stored in the public ledger.
- Not exposed in the turn report.
- Lifetime is limited to the current pass only.
- Public influence appears solely through the causal contribution of the visible metric it moves.
- Single registration rule: do not register the same influence as REVERSION, DERIVED, AND AGG simultaneously.

## Invariants

1. Final value - initial value = exact sum of all causal contributions (telescopic property).
2. No float or double used anywhere in aggregation math.
3. Exactly one rounding per weighted offset (not per component).
4. Weekly cap applied after elasticity rounding, not before.
5. Cap applied to delta, not to target.
6. Component order in causal allocation = config order.
7. Zero contributions omitted from ledger.
8. Derived expressions read pre-aggregation metrics of the current tick.
9. Legitimacy has one-tick latency for same-tick structural changes.

## Implementation Status

- Runtime models and the Content -> Core bridge are implemented and immutable.
- `AggregationEngine` implements phase 6 reversion, phase 7 derived internals, and phase 8a/8b metric aggregation.
- `SchedulerEngine` requires an `AggregationRuntimePlan`, binds the executor once, and executes phases 6, 7, and 8.
- `ScenarioRunner` passes `ContentPack.AggregationRuntimePlan` directly.
- Phases 9 through 14 remain explicit no-op hooks.
- Scheduler golden evidence is updated for aggregation-visible metric deltas; smoke and content hashes are unchanged.

## Content Pack Configuration

The runtime contract is defined in:

- `Assets/StreamingAssets/content/rules/aggregation_config.json`:
  - Schema v1.
  - 4 passes: INTERNAL_REVERSION, METRIC_AGGREGATION (9 metrics), DERIVED_INTERNALS, METRIC_AGGREGATION (legitimacy).
  - Dispatch by type maps to phases: INTERNAL_REVERSION -> phase 6, DERIVED_INTERNALS -> phase 7, METRIC_AGGREGATION -> phase 8.
  - Within phase 8, the nine-metric pass precedes legitimacy.
- `Assets/StreamingAssets/content/rules/target_config.json`:
  - `metrics.*` scale = 100, minS = 0, maxS = 10000, defaultS = 5000.
  - `internals.*.*` scale = 100, minS = 0, maxS = 10000, defaultS = 5000.
  - `weight_ppm` must sum absolute values to 1,000,000.
