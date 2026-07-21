# Territory Feedback Contract

## Contract status

The machine-readable authority for the territory feedback contract is
`MVP-013-territory-feedback`, registered in
`docs/mvp_contract_decisions.json`. This document is its
self-contained human representation.

This revision freezes the canonical regional authority, ordering rules,
numeric domain, phase 9 drift formulas defined by PR 15.1-C and
PR 15.1-D, and the phase 10 regional pull mechanics, bindings, and
one-tick latency defined by PR 15.1-E. It does not activate scheduler
phases 9 or 10. Runtime implementation is deferred to PR 15.2 through
PR 15.4. Active reform bias remains excluded until PR 19.4.

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
  },
  "causality": {
    "cause_category": "SYSTEM",
    "parent": null,
    "canonical_key_separator": ":",
    "canonical_key_separator_count": 1,
    "identifier_separator": ".",
    "identifier_policy": "PRINTABLE_ASCII_NO_WHITESPACE_COLON_PIPE",
    "drift": {
      "id_prefix": "REG_DRIFT",
      "canonical_key_pattern": "SYSTEM:REG_DRIFT.regions.{region_id}.{metric}",
      "target_pattern": "regions.{region_id}.{metric}",
      "target_visibility": "public_visible_target",
      "public_ledger": true,
      "tick_causal_buffer": true,
      "potential_cause_count": 64,
      "cause_order": "canonical_region_order_then_drift_metric_order",
      "zero_realized_delta_policy": "omit_contribution"
    },
    "pull_provenance": {
      "id_prefix": "REG_TO_INT",
      "canonical_key_pattern": "SYSTEM:REG_TO_INT.{internal_target}",
      "identity_count": 5,
      "provenance_scope": "ephemeral_execution_plan_only",
      "target_visibility": "hidden_internal",
      "public_ledger": false,
      "tick_causal_buffer": false,
      "serialized": false,
      "stored_in_game_state": false,
      "turn_report": false,
      "top_n_slot": false,
      "lifetime": "current_phase_10_plan_only",
      "public_attribution": "next_tick_SYSTEM_AGG",
      "double_counting": "forbidden",
      "identities": [
        {
          "binding_id": "support_to_coalition_strength",
          "canonical_key": "SYSTEM:REG_TO_INT.internals.leg.coalition_strength",
          "internal_target": "internals.leg.coalition_strength"
        },
        {
          "binding_id": "organization_to_field_ops",
          "canonical_key": "SYSTEM:REG_TO_INT.internals.party.field_ops",
          "internal_target": "internals.party.field_ops"
        },
        {
          "binding_id": "tension_to_protest_activity",
          "canonical_key": "SYSTEM:REG_TO_INT.internals.tension.protest_activity",
          "internal_target": "internals.tension.protest_activity"
        },
        {
          "binding_id": "rival_presence_to_opposition_obstruction",
          "canonical_key": "SYSTEM:REG_TO_INT.internals.leg.opposition_obstruction",
          "internal_target": "internals.leg.opposition_obstruction"
        },
        {
          "binding_id": "tension_to_movement_salience",
          "canonical_key": "SYSTEM:REG_TO_INT.internals.agenda.movement_salience",
          "internal_target": "internals.agenda.movement_salience"
        }
      ]
    },
    "public_aggregation_attribution": [
      {
        "internal_target": "internals.leg.coalition_strength",
        "visible_metric": "metrics.legislative_capacity",
        "canonical_key": "SYSTEM:AGG.metrics.legislative_capacity.internals.leg.coalition_strength"
      },
      {
        "internal_target": "internals.party.field_ops",
        "visible_metric": "metrics.party_organization",
        "canonical_key": "SYSTEM:AGG.metrics.party_organization.internals.party.field_ops"
      },
      {
        "internal_target": "internals.tension.protest_activity",
        "visible_metric": "metrics.social_tension",
        "canonical_key": "SYSTEM:AGG.metrics.social_tension.internals.tension.protest_activity"
      },
      {
        "internal_target": "internals.leg.opposition_obstruction",
        "visible_metric": "metrics.legislative_capacity",
        "canonical_key": "SYSTEM:AGG.metrics.legislative_capacity.internals.leg.opposition_obstruction"
      },
      {
        "internal_target": "internals.agenda.movement_salience",
        "visible_metric": "metrics.public_agenda",
        "canonical_key": "SYSTEM:AGG.metrics.public_agenda.internals.agenda.movement_salience"
      }
    ]
  },
  "atomicity": {
    "scope": {
      "phase_9_and_phase_10": "separate_atomic_passes",
      "pass_atomicity": "executor_responsibility",
      "observable_tick_atomicity": "scheduler_result_boundary",
      "runtime_tick_fail_closed_verification": "PR_15_4"
    },
    "execution_sequence": [
      "capture_immutable_snapshot",
      "validate_all_bindings_and_inputs",
      "compute_all_outputs",
      "compute_all_applicable_causes_or_provenance",
      "validate_deltas_ranges_and_causal_accounting",
      "construct_complete_candidate",
      "publish_once"
    ],
    "phase_9_drift": {
      "phase": 9,
      "snapshot": "post_phase_8_immutable",
      "planned_output_count": 64,
      "candidate": "complete_regional_state_and_visible_causal_batch",
      "publication": "single_atomic_state_and_causal_batch",
      "partial_publication": false,
      "cross_output_observation": false,
      "failure_behavior": "discard_candidate_and_publish_nothing"
    },
    "phase_10_pull": {
      "phase": 10,
      "snapshot": "post_phase_9_immutable",
      "planned_output_count": 5,
      "candidate": "complete_internal_state_with_ephemeral_provenance",
      "publication": "single_atomic_internal_state_batch",
      "public_causal_publication": "none",
      "ephemeral_provenance_publication": "none",
      "partial_publication": false,
      "binding_chaining": false,
      "failure_behavior": "discard_candidate_and_publish_nothing"
    },
    "fail_closed_triggers": [
      "missing_region",
      "duplicate_region",
      "inconsistent_canonical_order",
      "region_count_mismatch",
      "weight_sum_mismatch",
      "non_positive_weight",
      "missing_regional_field",
      "missing_internal_destination",
      "missing_target_config",
      "set_not_allowed",
      "invalid_cause_ref",
      "duplicate_output",
      "duplicate_coupling",
      "multiplication_overflow",
      "sum_overflow",
      "addition_overflow",
      "long_to_int_out_of_range",
      "invalid_denominator",
      "invalid_clamp_or_invariant",
      "causal_batch_rejection",
      "visible_delta_causal_sum_mismatch"
    ],
    "fail_closed_guarantees": [
      "zero_partial_game_state",
      "zero_partial_regions",
      "zero_partial_internals",
      "zero_partial_causal_contributions"
    ],
    "observable_tick_failure": {
      "scenario": "phase_9_succeeds_phase_10_fails",
      "advance_one_tick_result": "not_returned",
      "original_game_state": "unchanged",
      "phase_9_snapshot_exposed": false,
      "partial_causal_ledger_exposed": false,
      "sealed_causal_ledger_exposed": false,
      "scheduler_working_state": "local_only",
      "scheduler_causal_buffer": "local_only_until_phase_15_seal",
      "runtime_test_owner": "PR_15_4"
    },
    "non_guarantees": [
      "phases_9_and_10_are_not_one_super_pass",
      "PR_15_1_does_not_implement_runtime_transactions",
      "PR_15_1_does_not_activate_scheduler_phases_9_or_10"
    ]
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

- PR 15.1-H registers MVP-013-territory-feedback as the machine-readable
  authority.
- PR 15.1-I completes its self-contained human representation with
  exact correspondence tables, phase order, snapshot semantics,
  active reform bias exclusion, vector registry, and scope boundaries.
- PR 15.1-J creates the fixture.
- PR 15.1-K creates the oracle.
- PR 15.1-L completes parity and negative tests.
- PR 15.2 through 15.4 implement the productive runtime plan.
- PR 19.4 implements active reform bias.

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

## Territorial causality and hidden provenance

Causal attribution of territorial changes follows a two-tier system:
public REG_DRIFT causes for visible regional targets, and ephemeral
REG_TO_INT provenance for hidden internal writes.

**CauseRef grammar**: Every `CauseRef` combines a `CauseCategory` and
an `ID` separated by exactly one colon (`:`) — `CATEGORY:ID`. The
category is one of `DECISION`, `EVENT`, `REFORM`, `MOVEMENT`,
`MODIFIER`, or `SYSTEM`. The ID must be printable ASCII without
whitespace, control characters, colons, or pipes. Only `MODIFIER`
causes may have a non-null parent. All SYSTEM causes have `parent =
null`.

**64 potential REG_DRIFT causes**: Each territorial drift output
generates a cause key of the form
`SYSTEM:REG_DRIFT.regions.{region_id}.{metric}`. With 16 regions and
4 metrics (support, tension, organization, rival_presence), there are
64 unique potential cause identities. A potential cause does not imply
an emitted entry: if a drift output has `realized_deltaS == 0`, the
contribution is omitted from the causal ledger.

**Five ephemeral REG_TO_INT identities**: The phase 10 pull bindings
produce exactly five transient identities of the form
`SYSTEM:REG_TO_INT.{internal_target}`. These exist only during the
current phase 10 execution plan. They are:

- `SYSTEM:REG_TO_INT.internals.leg.coalition_strength`
- `SYSTEM:REG_TO_INT.internals.party.field_ops`
- `SYSTEM:REG_TO_INT.internals.tension.protest_activity`
- `SYSTEM:REG_TO_INT.internals.leg.opposition_obstruction`
- `SYSTEM:REG_TO_INT.internals.agenda.movement_salience`

REG_TO_INT identities:

- are syntactically valid SYSTEM CauseKeys with `parent = null`;
- target `internals.*`, which is excluded from the public visible
  target catalog;
- do not enter `TickCausalBuffer`;
- are not serialized;
- are not stored in `GameState`;
- do not appear in `TurnReport`;
- do not consume Top-N projection slots;
- have a lifetime limited to the current phase 10 plan;
- are never registered publicly alongside `SYSTEM:AGG`.

**Public attribution through SYSTEM:AGG**: When a hidden internal
influences a visible metric in tick T+1 phase 8, the visible
attribution appears as:

`SYSTEM:AGG.{visible_metric}.{internal_target}`

For example, `SYSTEM:REG_TO_INT.internals.leg.coalition_strength`
remains ephemeral; the public causal record shows
`SYSTEM:AGG.metrics.legislative_capacity.internals.leg.coalition_strength`
instead.

**Double-counting prohibition**: The same influence must not be
registered as both REG_TO_INT and SYSTEM:AGG simultaneously. The
ephemeral REG_TO_INT exists only during phase 10; phase 8 in the next
tick records the same influence under SYSTEM:AGG attribution.

**Zero contributions**: If `realized_deltaS == 0`, the contribution is
omitted from the causal ledger entirely (no record, no throw).

**Scheduler phases**: Phases 9 and 10 remain no-op in the product;
this contract defines their causal semantics but does not activate
runtime execution.

## Territorial atomicity and fail-closed semantics

This contract freezes the territorial executor shape without activating the runtime:

1. Phase 9 and phase 10 are separate atomic passes.
2. Each pass follows snapshot, validate, compute, causal attribution, validation, candidate construction, and single publication.
3. Phase 9 plans exactly 64 outputs before publication and publishes regional state plus visible causality as one batch.
4. Phase 10 plans exactly five outputs before publication and publishes five internals as one batch while keeping REG_TO_INT provenance ephemeral and non-public.
5. Fail-closed handling discards the entire candidate on any trigger, with no partial GameState, regions, internals, or causal publication.
6. Atomicity inside a pass is executor responsibility; observable tick atomicity is the scheduler result boundary.
7. A phase 9 success followed by a phase 10 failure does not produce `TickAdvanceResult`, leaves the original `GameState` intact, and exposes neither snapshot nor ledger partials.

### Exact fail-closed triggers

1. `missing_region`
2. `duplicate_region`
3. `inconsistent_canonical_order`
4. `region_count_mismatch`
5. `weight_sum_mismatch`
6. `non_positive_weight`
7. `missing_regional_field`
8. `missing_internal_destination`
9. `missing_target_config`
10. `set_not_allowed`
11. `invalid_cause_ref`
12. `duplicate_output`
13. `duplicate_coupling`
14. `multiplication_overflow`
15. `sum_overflow`
16. `addition_overflow`
17. `long_to_int_out_of_range`
18. `invalid_denominator`
19. `invalid_clamp_or_invariant`
20. `causal_batch_rejection`
21. `visible_delta_causal_sum_mismatch`

### Exact fail-closed guarantees

1. `zero_partial_game_state`
2. `zero_partial_regions`
3. `zero_partial_internals`
4. `zero_partial_causal_contributions`

Phases 9 and 10 remain separate passes, phase 9 still plans 64 outputs, phase 10 still plans five outputs, no `TickAdvanceResult` is produced on a phase 10 failure, the original `GameState` remains intact, no snapshot or ledger partial is exposed, the runtime test stays deferred to PR 15.4, and phases 9 and 10 remain no-op in the runtime.

8. The observable rollback case is deferred to PR 15.4.
9. Phases 9 and 10 remain no-op in the runtime.
10. Implementation and runtime testing remain deferred to PRs 15.2 through 15.4.

## MVP-013 human-readable correspondence

The following table maps each of the 16 ordered keys of
`MVP-013-territory-feedback.resolution` to the corresponding human
section in this document.

| Resolution key | Human section |
|---|---|
| `canonical_region_order` | `Canonical regional authority` |
| `regional_dynamic_targets` | `Normative rules` |
| `static_regional_resources` | `Normative rules` |
| `numeric_domain` | `Numeric domain and phase 9 drift` |
| `drift` | `Numeric domain and phase 9 drift` |
| `pull` | `Phase 10 regional pull and one-tick latency` |
| `phase_order` | `Phase order and snapshot semantics` |
| `snapshot_semantics` | `Phase order and snapshot semantics` |
| `latency` | `Phase 10 regional pull and one-tick latency` |
| `cause_key_grammar` | `Territorial causality and hidden provenance` |
| `hidden_pull_provenance` | `Territorial causality and hidden provenance` |
| `pass_execution_semantics` | `Territorial atomicity and fail-closed semantics` |
| `active_reform_bias_exclusion` | `Active reform bias exclusion` |
| `vectors` | `Execution vector registry` |
| `scope` | `Scope and non-scope` |
| `non_scope` | `Scope and non-scope` |

## Phase order and snapshot semantics

| Resolution key | Phase |
|---|---:|
| `aggregate_national_metrics` | 8 |
| `drift_national_to_regions` | 9 |
| `pull_regions_to_internals` | 10 |
| `close_causal_report` | 15 |
| `detect_and_publish_blocking_decision` | 16 |

| Snapshot rule | Contract value |
|---|---|
| `phase_9_snapshot` | `post_phase_8_immutable` |
| `phase_9_all_outputs_share_snapshot` | `true` |
| `phase_9_rival_support_source` | `phase_input_snapshot_pre_drift` |
| `phase_10_snapshot` | `post_phase_9_immutable` |
| `phase_10_all_bindings_share_snapshot` | `true` |
| `phase_10_binding_chaining` | `false` |

Phase 9 and phase 10 are separate passes. The 64 outputs of phase 9
share a single immutable post-phase-8 snapshot. The five bindings of
phase 10 share a single immutable post-phase-9 snapshot.
`rival_presence` reads support from the pre-drift snapshot
(`phase_input_snapshot_pre_drift`), not the post-drift computed value.
No binding chaining exists within phase 10.

## Active reform bias exclusion

| Field | Value |
|---|---|
| `included_in_pr_15_x` | `false` |
| `runtime_hook` | `false` |
| `placeholder` | `false` |
| `neutral_branch` | `false` |
| `cause_key` | `null` |
| `implementation_owner` | `PR_19_4` |

PR 15.x does not contain active territorial reform bias. No runtime
hook exists. No placeholder exists. No neutral branch exists. No
CauseKey is reserved. The implementation belongs exclusively to
PR 19.4.

## Execution vector registry

### Rounding

- `R-01`
- `R-02`

### Drift

- `D-00`
- `D-01`
- `D-02`
- `D-03`
- `D-04`
- `D-05`
- `D-06`
- `D-07`
- `D-08`
- `D-08-WRONG`
- `D-09`
- `D-10`

### Pull

- `P-00`
- `P-01`
- `P-02`
- `P-03`
- `P-04`
- `P-05`

### Latency

- `L-01-T`
- `L-01-T1-R`
- `L-01-T1-A`
- `L-01-CAUSE`

### Ordering

- `O-01`
- `O-02`
- `O-03`
- `O-04`
- `O-05`

### Ownership

- fixture owner: `PR_15_1_J`
- oracle owner: `PR_15_1_K`

This section registers the exact vector IDs and their ownership. It
does not create expected values, fixtures, or oracles.

## Scope and non-scope

### Scope

1. `machine_readable_territory_contract`
2. `human_readable_territory_contract`
3. `execution_vectors`
4. `independent_python_oracle`
5. `contract_parity_and_negative_tests`

### Non-scope

1. `runtime_csharp_implementation`
2. `scheduler_phase_activation`
3. `game_state_schema_changes`
4. `content_pack_changes`
5. `persistence_or_migrations`
6. `active_reform_bias_before_PR_19_4`
7. `ui_or_turn_report_changes`

The scope describes the complete PR 15.1 contract, not only commit I.
Fixture, oracle, and negative tests complete in PR 15.1-J through
PR 15.1-L. Non-scope items remain prohibited during PR 15.1.
