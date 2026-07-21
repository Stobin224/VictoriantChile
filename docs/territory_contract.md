# Territory Feedback Contract

## Contract status

This revision freezes the canonical regional authority, ordering rules,
numeric domain, phase 9 drift formulas defined by PR 15.1-C and
PR 15.1-D, and the phase 10 regional pull mechanics, bindings, and
one-tick latency defined by PR 15.1-E. It does not activate scheduler
phases 9 or 10.

## Canonical regional authority

The following JSON block defines the binding regional authority and
numeric computation rules for all territorial computation in this
project. It is the single source of truth for region identity, count,
order, weight, dynamic targets, static resources, numeric domain,
phase 9 drift formulas, phase 10 pull mechanics, bindings, and
one-tick latency.

<!-- BEGIN CANONICAL REGION AUTHORITY -->
```json
{
  "canonical_region_order": {
    "authority": "content_pack_declaration_order",
    "source_path": "Assets/StreamingAssets/content/core/regions.json",
    "region_count": 16,
    "ordered_region_ids": [
      "arica_parinacota",
      "tarapaca",
      "antofagasta",
      "atacama",
      "coquimbo",
      "valparaiso",
      "metropolitana",
      "ohiggins",
      "maule",
      "nuble",
      "biobio",
      "araucania",
      "los_rios",
      "los_lagos",
      "aysen",
      "magallanes"
    ],
    "weight_ppm_each": 62500,
    "weight_ppm_sum_required": 1000000,
    "forbidden_order_sources": [
      "GameState.Regions",
      "RegionsById.Values",
      "dictionary_iteration",
      "lexicographic_sort"
    ]
  },
  "regional_dynamic_targets": [
    "support",
    "tension",
    "organization",
    "rival_presence"
  ],
  "static_regional_resources": {
    "admin_capS": 5000,
    "industry_capS": 5000,
    "extractive_capS": 5000,
    "social_capS": 5000,
    "populationS": 5000
  },
  "numeric_domain": {
    "scale": 100,
    "hundredS": 10000,
    "midS": 5000,
    "ppm_denominator": 1000000,
    "stored_type": "int",
    "intermediate_type": "checked_long",
    "rounding": "HALF_AWAY_FROM_ZERO",
    "rounding_authority": "FixedMath.RoundDivide",
    "target_clamp_authority": "TargetConfig",
    "publication_operation": "SET",
    "forbidden_numeric_types": [
      "float",
      "double",
      "decimal"
    ],
    "forbidden_behaviors": [
      "Math.Round",
      "divide_before_weighted_sum_complete",
      "round_per_component",
      "silent_saturation",
      "unchecked_overflow",
      "unchecked_cast",
      "hardcoded_target_clamp"
    ]
  },
  "drift": {
    "phase": 9,
    "phase_name": "DriftNationalToRegions",
    "snapshot": "post_phase_8",
    "region_order_source": "canonical_region_order.ordered_region_ids",
    "metric_order": [
      "support",
      "tension",
      "organization",
      "rival_presence"
    ],
    "region_count": 16,
    "outputs_per_region": 4,
    "output_count": 64,
    "half_life_weeks_metadata": 6,
    "alpha_ppm": 109101,
    "cap_per_weekS": 200,
    "target_baseS": 5000,
    "target_denominator": 1000000,
    "all_sources_read_from": "phase_input_snapshot",
    "rival_support_read_from": "phase_input_snapshot_pre_drift",
    "target_formulas": {
      "support": {
        "target": "regions.{region_id}.support",
        "terms": [
          {
            "source": "metrics.legitimacy",
            "transform": "value_minus_mid",
            "coefficient_ppm": 600000
          },
          {
            "source": "metrics.party_organization",
            "transform": "value_minus_mid",
            "coefficient_ppm": 300000
          },
          {
            "source": "metrics.social_tension",
            "transform": "value_minus_mid",
            "coefficient_ppm": -400000
          }
        ]
      },
      "tension": {
        "target": "regions.{region_id}.tension",
        "terms": [
          {
            "source": "metrics.economy",
            "transform": "mid_minus_value",
            "coefficient_ppm": 500000
          },
          {
            "source": "metrics.security",
            "transform": "mid_minus_value",
            "coefficient_ppm": 400000
          },
          {
            "source": "metrics.public_agenda",
            "transform": "value_minus_mid",
            "coefficient_ppm": 300000
          }
        ]
      },
      "organization": {
        "target": "regions.{region_id}.organization",
        "terms": [
          {
            "source": "metrics.party_organization",
            "transform": "value_minus_mid",
            "coefficient_ppm": 800000
          }
        ]
      },
      "rival_presence": {
        "target": "regions.{region_id}.rival_presence",
        "terms": [
          {
            "source": "regions.{region_id}.support",
            "transform": "mid_minus_value",
            "coefficient_ppm": 700000
          },
          {
            "source": "metrics.internal_cohesion",
            "transform": "mid_minus_value",
            "coefficient_ppm": 200000
          }
        ]
      }
    },
    "common_pipeline": [
      "construct_target_numerator_in_checked_long",
      "round_target_offset_once",
      "add_mid",
      "target_config_clamp_target",
      "distance_target_minus_current",
      "multiply_distance_by_alpha_in_checked_long",
      "round_elastic_delta_once",
      "clamp_delta_to_weekly_cap",
      "add_delta_to_current",
      "target_config_clamp_final",
      "realized_delta_final_minus_current"
    ]
  },
  "pull": {
    "phase": 10,
    "phase_name": "PullRegionsToInternals",
    "snapshot": "post_phase_9",
    "region_order_source": "canonical_region_order.ordered_region_ids",
    "weight_source": "content_pack_region.weight_ppm",
    "weight_sum_required": 1000000,
    "weighted_average_denominator": 1000000,
    "weighted_average_intermediate_type": "checked_long",
    "weighted_average_rounding": "HALF_AWAY_FROM_ZERO",
    "weighted_average_division_count": 1,
    "all_sources_read_from": "phase_input_snapshot",
    "binding_chaining": "forbidden",
    "alpha_ppm": 206299,
    "cap_per_weekS": 400,
    "output_count": 5,
    "binding_order": [
      "support_to_coalition_strength",
      "organization_to_field_ops",
      "tension_to_protest_activity",
      "rival_presence_to_opposition_obstruction",
      "tension_to_movement_salience"
    ],
    "bindings": [
      {
        "id": "support_to_coalition_strength",
        "regional_source": "support",
        "destination": "internals.leg.coalition_strength"
      },
      {
        "id": "organization_to_field_ops",
        "regional_source": "organization",
        "destination": "internals.party.field_ops"
      },
      {
        "id": "tension_to_protest_activity",
        "regional_source": "tension",
        "destination": "internals.tension.protest_activity"
      },
      {
        "id": "rival_presence_to_opposition_obstruction",
        "regional_source": "rival_presence",
        "destination": "internals.leg.opposition_obstruction"
      },
      {
        "id": "tension_to_movement_salience",
        "regional_source": "tension",
        "destination": "internals.agenda.movement_salience"
      }
    ],
    "common_pipeline": [
      "construct_complete_weighted_sum_in_checked_long",
      "round_weighted_average_once",
      "target_config_clamp_target",
      "distance_target_minus_current",
      "multiply_distance_by_alpha_in_checked_long",
      "round_elastic_delta_once",
      "clamp_delta_to_weekly_cap",
      "add_delta_to_current",
      "target_config_clamp_final",
      "realized_delta_final_minus_current"
    ]
  },
  "latency": {
    "feedback_latency_ticks": 1,
    "phase_8_observes": "internals_before_current_tick_pull",
    "phase_9_writes": "regional_dynamic_targets",
    "phase_10_writes": "five_internal_targets",
    "same_tick_phase_8_reexecution": false,
    "next_tick_observation_order": [
      "RevertInternals",
      "DeriveInternals",
      "AggregateNationalMetrics"
    ],
    "regional_feedback_first_visible_in_metrics": "tick_plus_1_phase_8"
  }
}
```
<!-- END CANONICAL REGION AUTHORITY -->

### Normative rules

1. **Order preservation**: The canonical region order (north to south)
   is defined by the declaration order in `regions.json`. This order
   must be preserved even though `GameState` stores regions in ordinal
   (alphabetical) order via `StateCollection.SnapshotSorted`.

2. **Dictionary non-authority**: `GameState.RegionsById` and similar
   dictionaries provide O(1) lookup by region ID but are not authority
   for iteration sequence. Iterating a dictionary without explicit
   ordering produces implementation-defined results and must not be
   used for territorial computation.

3. **Dynamic targets**: The four regional dynamic targets —
   `regions.{id}.support`, `regions.{id}.tension`,
   `regions.{id}.organization`, `regions.{id}.rival_presence` —
   represent mutable state that will be written by the territory
   feedback system. They are declared in `target_config.json` and
   registered in the visible target catalog via
   `CreateForMvp(..., orderedRegionIds, ...)`.

4. **Static resources**: The five static fields —
   `admin_capS`, `industry_capS`, `extractive_capS`, `social_capS`,
   `populationS` — are Content Pack data at value `5000` per region.
   They are not dynamic targets, not part of the visible causal
   surface, and have no territorial writer in PR 15.x. Their
   immutability guarantee is limited to this contract and the scope
   of PR 15.x; future contracts may revise this.

5. **Uniform weights**: Every region carries `weight_ppm = 62500`,
   producing a sum of `1000000`. Weights are uniform under this
   contract. Demographic, political, or economic weighting would
   require a new contract revision.

6. **Out of scope**: UI display names, polygon data, demographic data,
   and any non-simulation state are outside the scope of this
   contract.

7. **Numeric domain**: All territorial computation uses integer
   fixed-point arithmetic with `Scale = 100`, `HundredS = 10000`,
   `MidS = 5000`, `PpmDenominator = 1000000`. Intermediate results
   use `checked long`. Rounding uses `HALF_AWAY_FROM_ZERO` via
   `FixedMath.RoundDivide`. No `float`, `double`, or `Decimal` types
   are permitted. Target clamping is delegated to `TargetConfig.Clamp`.
   Future territorial publication uses the `SET` operation.

8. **Scheduler phases**: Scheduler phases 9
   (`DriftNationalToRegions`) and 10 (`PullRegionsToInternals`)
   remain no-op. This contract does not activate or implement them.
   The pull mechanics, bindings, and latency are contractually frozen
   in this revision but are not executed at runtime.

## Contract boundaries with later PRs

- PR 15.1-D freezes drift formulas (support, tension,
  organization, rival_presence), `alpha_ppm`, caps, rounding, and
  snapshot semantics. This revision completes that freeze.
- PR 15.1-E freezes pull mechanics, weighted average regional,
  bindings, and latency. This revision completes that freeze.
- PR 15.1-F will freeze causal keys `REG_DRIFT` and ephemeral
  `REG_TO_INT` identities.
- PR 15.1-G will freeze atomicity and fail-closed contractual rules.
- PR 15.2 through 15.4 will implement the productive runtime plan.

## Numeric domain and phase 9 drift

### Support

```
numerator =
      600000 * (metrics.legitimacy - 5000)
    + 300000 * (metrics.party_organization - 5000)
    - 400000 * (metrics.social_tension - 5000)

offsetS =
    FixedMath.RoundDivide(numerator, 1000000)

target_unclampedS =
    5000 + offsetS

targetS =
    TargetConfig(regions.{region_id}.support).Clamp(target_unclampedS)
```

### Tension

```
numerator =
      500000 * (5000 - metrics.economy)
    + 400000 * (5000 - metrics.security)
    + 300000 * (metrics.public_agenda - 5000)

offsetS =
    FixedMath.RoundDivide(numerator, 1000000)

target_unclampedS =
    5000 + offsetS

targetS =
    TargetConfig(regions.{region_id}.tension).Clamp(target_unclampedS)
```

### Organization

```
numerator =
    800000 * (metrics.party_organization - 5000)

offsetS =
    FixedMath.RoundDivide(numerator, 1000000)

target_unclampedS =
    5000 + offsetS

targetS =
    TargetConfig(regions.{region_id}.organization).Clamp(target_unclampedS)
```

### Rival presence

```
numerator =
      700000 * (5000 - snapshot.regions[{region_id}].support)
    + 200000 * (5000 - metrics.internal_cohesion)

offsetS =
    FixedMath.RoundDivide(numerator, 1000000)

target_unclampedS =
    5000 + offsetS

targetS =
    TargetConfig(regions.{region_id}.rival_presence).Clamp(target_unclampedS)
```

`snapshot.regions[{region_id}].support` is the pre-drift value from the
post-phase-8 snapshot. It must not be replaced by the planned or published
support value from the same phase 9 execution.

### Common pipeline

```
distanceS =
    targetS - currentS

elastic_numerator =
    distanceS * 109101

elastic_deltaS =
    FixedMath.RoundDivide(elastic_numerator, 1000000)

capped_deltaS =
    clamp(elastic_deltaS, -200, +200)

pre_finalS =
    currentS + capped_deltaS

finalS =
    TargetConfig(target).Clamp(pre_finalS)

realized_deltaS =
    finalS - currentS
```

Contractual pipeline order:

1. construct numerator complete in checked long;
2. round once to obtain offsetS;
3. add MID_S;
4. apply target clamp;
5. calculate distanceS;
6. multiply by alpha in checked long;
7. round once the elasticity;
8. apply weekly cap;
9. add to current value;
10. apply final clamp;
11. calculate realized_deltaS.

No causes, contributions, or causal publication are part of this pipeline
definition.

## Phase 10 regional pull and one-tick latency

### Weighted average

For each regional source, the weighted average is computed as a single
sum over all 16 regions before dividing:

```
numerator =
    sum(regions[region_id].source * region.weight_ppm)

weighted_averageS =
    FixedMath.RoundDivide(numerator, 1000000)
```

The complete sum is constructed in `checked long` before any division.
No per-region rounding occurs. Exactly one division is performed per
weighted average.

### Bindings

| ID | Regional source | Destination |
|---|---|---|
| support_to_coalition_strength | support | internals.leg.coalition_strength |
| organization_to_field_ops | organization | internals.party.field_ops |
| tension_to_protest_activity | tension | internals.tension.protest_activity |
| rival_presence_to_opposition_obstruction | rival_presence | internals.leg.opposition_obstruction |
| tension_to_movement_salience | tension | internals.agenda.movement_salience |

- All five bindings read the same post-phase-9 snapshot.
- No binding can observe the output of another binding (chaining
  is forbidden).
- The two bindings that read `tension` (protest_activity and
  movement_salience) remain separate outputs.
- All destinations exist in `target_config.json` under the pattern
  `internals.*.*` with `scale=100`, `minS=0`, `maxS=10000`,
  `defaultS=5000`, and `SET` permitted.
- The five destinations are planned before any future publication.

### Pull math per binding

```
targetS =
    TargetConfig(destination).Clamp(weighted_averageS)

distanceS =
    targetS - current_internalS

elastic_numerator =
    distanceS * 206299

elastic_deltaS =
    FixedMath.RoundDivide(elastic_numerator, 1000000)

capped_deltaS =
    clamp(elastic_deltaS, -400, +400)

pre_finalS =
    current_internalS + capped_deltaS

finalS =
    TargetConfig(destination).Clamp(pre_finalS)

realized_deltaS =
    finalS - current_internalS
```

### Tick T / T+1 latency

The simulation tick order relevant to territorial feedback:

1. At tick T, phase 8 (metrics aggregation) executes before the
   territorial phases.
2. Phase 9 (drift) updates the four regional dynamic targets.
3. Phase 10 (pull) reads the post-phase-9 snapshot and writes the
   five internal targets.
4. National metrics are not re-aggregated during T after the
   territorial phases complete.
5. At tick T+1, phases 6-8 observe the internals produced during T:
   - RevertInternals resets internal deltas to zero;
   - DeriveInternals recomputes derived targets from the updated
     internals;
   - AggregateNationalMetrics recomputes national metrics (e.g.,
     legislative_capacity) from the updated targets.
6. Phase 8 is not re-executed in the same tick after phases 9 and 10.
   Re-executing phase 8 in T would incorrectly publish T+1 metrics
   one tick early.
7. Scheduler phases 9 and 10 remain no-op in the product; this
   contract defines their semantics but does not activate runtime
   execution.
