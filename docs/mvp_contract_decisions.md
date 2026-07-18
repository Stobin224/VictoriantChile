# MVP Contract Freeze Register

This document is the human-readable authoritative MVP contract freeze.

The machine-readable canonical representation is `docs/mvp_contract_decisions.json`.

Both artifacts define the same frozen MVP contract.

Schema version: `2`

Register status: `frozen`

No pending recommendations remain in this register.

PR 8 is documentation, contract, and test only. It does not implement gameplay, runtime state, content, or loaders.

## Summary

| ID | Topic | Status |
| --- | --- | --- |
| MVP-001-social-tension-sign | Social tension sign convention | approved |
| MVP-002-cause-category | Closed causal-report categories | approved |
| MVP-003-effect-order | Effect ordering | approved |
| MVP-004-tick-order | Tick phase ordering | approved |
| MVP-005-rng-contract | RNG contract | approved |
| MVP-006-vertical-slice-duration | Vertical-slice duration | approved |
| MVP-007-initial-scenario | Initial scenario | approved |
| MVP-008-primary-objective | Primary player objective | approved |
| MVP-009-cadre-schema-and-roles | Cadre schema and roles | approved |
| MVP-010-turn-report-top-n | Turn-report causal limit | approved |
| MVP-011-active-movements | Active movements | approved |

## Canonical JSON

```json
{
  "schema_version": 2,
  "register_status": "frozen",
  "decisions": [
    {
      "id": "MVP-001-social-tension-sign",
      "topic": "Social tension sign convention",
      "question": "Does a higher social_tension value represent worse tension, and which effect direction increases or reduces tension?",
      "status": "approved",
      "resolution": {
        "target": "metrics.social_tension",
        "higher_is_worse": true,
        "alert_threshold": {
          "op": ">",
          "valueS": 7000
        },
        "driver_weight_scale": "weight_ppm",
        "drivers": [
          {
            "target": "internals.tension.cost_of_living",
            "weight_ppm": 350000
          },
          {
            "target": "internals.tension.polarization",
            "weight_ppm": 250000
          },
          {
            "target": "internals.tension.protest_activity",
            "weight_ppm": 250000
          },
          {
            "target": "internals.tension.institutional_trust",
            "weight_ppm": -150000
          }
        ],
        "content_alignment": {
          "content_pack_change_in_pr8": false,
          "content_pack_change_pr": 9
        }
      },
      "rationale": "The metric now has one sign convention and exact integer drivers for later loader alignment."
    },
    {
      "id": "MVP-002-cause-category",
      "topic": "Closed causal-report categories",
      "question": "Which closed set of cause categories may appear in causal reports?",
      "status": "approved",
      "resolution": {
        "ordered_categories": [
          "DECISION",
          "EVENT",
          "REFORM",
          "MOVEMENT",
          "MODIFIER",
          "SYSTEM"
        ],
        "modifier_parent_categories": [
          "DECISION",
          "EVENT",
          "REFORM",
          "MOVEMENT"
        ],
        "system_causes": {
          "clamp": "SYSTEM:CLAMP",
          "rounding": "SYSTEM:ROUNDING",
          "ig_clout_normalize": "SYSTEM:IG_CLOUT_NORMALIZE"
        },
        "allow_additional_categories": false
      },
      "rationale": "A closed ordered category set is required for stable causal serialization and reporting."
    },
    {
      "id": "MVP-003-effect-order",
      "topic": "Effect ordering",
      "question": "What exact deterministic order applies multiple effects targeting the same value within one tick?",
      "status": "approved",
      "resolution": {
        "set_handling": {
          "winner_order": [
            "priority_desc",
            "effect_instance_id_ordinal_asc"
          ],
          "apply_winning_set": true,
          "ignore_add_and_mul_for_same_target": true
        },
        "phase_order_without_set": [
          "ADD",
          "MUL"
        ],
        "add_order": [
          "priority_desc",
          "effect_instance_id_ordinal_asc"
        ],
        "mul_order": [
          "priority_desc",
          "effect_instance_id_ordinal_asc"
        ],
        "mul_rounding": "HALF_AWAY_FROM_ZERO",
        "post_processing": [
          "final_clamp",
          "final_normalization",
          "system_residue"
        ],
        "forbid": [
          "load_order",
          "dictionary_order",
          "grouped_mul_product",
          "floats"
        ]
      },
      "rationale": "Deterministic per-target ordering is required for replay, testing, and causal explanations."
    },
    {
      "id": "MVP-004-tick-order",
      "topic": "Tick phase ordering",
      "question": "What exact ordered phases constitute one canonical simulation tick?",
      "status": "approved",
      "resolution": {
        "phases": [
          "increment_tick",
          "expire_effects",
          "execute_scheduled_actions",
          "apply_start_instant_modifiers",
          "apply_per_tick_modifiers",
          "revert_internals",
          "derive_internals",
          "aggregate_national_metrics",
          "drift_national_to_regions",
          "pull_regions_to_internals",
          "update_movements",
          "advance_reforms",
          "resolve_events_and_crises",
          "apply_final_clamps_and_normalizations",
          "close_causal_report",
          "detect_and_publish_blocking_decision"
        ],
        "regional_feedback_latency_ticks": 1
      },
      "rationale": "One canonical phase order prevents hidden feedback loops and makes reports reproducible."
    },
    {
      "id": "MVP-005-rng-contract",
      "topic": "RNG contract",
      "question": "What RNG algorithm, seed ownership, draw ordering, and serialized state define deterministic randomness?",
      "status": "approved",
      "resolution": {
        "algorithm": "pcg32-xsh-rr",
        "contract_version": "pcg32-v1",
        "serialized_fields": [
          {
            "name": "state_u64",
            "format": "hex_lowercase_16"
          },
          {
            "name": "stream_u64",
            "format": "hex_lowercase_16",
            "must_be_odd": true
          },
          {
            "name": "draw_count_u64",
            "format": "hex_lowercase_16"
          }
        ],
        "forbid": [
          "System.Random",
          "GetHashCode",
          "implicit_global_rng"
        ],
        "consumption_rule": "sequential_only_in_closed_order_systems",
        "event_selector_keying": {
          "enabled": true,
          "encoding": "utf-8",
          "key_parts": [
            "seed",
            "tick",
            "system",
            "template",
            "slot"
          ],
          "derivation": "sha-256",
          "byte_order": "documented_explicitly",
          "final_tie_break": "id_ordinal_asc",
          "determinism_rule": "same_state_and_actions_same_draws_and_hashes"
        }
      },
      "rationale": "The RNG contract is frozen now so later runtime work can be deterministic by construction."
    },
    {
      "id": "MVP-006-vertical-slice-duration",
      "topic": "Vertical-slice duration",
      "question": "How many turns or simulated months does the MVP vertical slice contain, and what exact condition ends it?",
      "status": "approved",
      "resolution": {
        "duration_weeks": 26,
        "target_session_minutes_min": 30,
        "target_session_minutes_max": 60,
        "early_end_conditions": [
          "victory",
          "defeat"
        ]
      },
      "rationale": "The slice now has a fixed campaign length with explicit early terminal exits."
    },
    {
      "id": "MVP-007-initial-scenario",
      "topic": "Initial scenario",
      "question": "Which single initial historical scenario and starting date belong to the MVP?",
      "status": "approved",
      "resolution": {
        "scenario_id": "scenario_constitutional_reform_mvp",
        "start_date": "2030-03-11",
        "setting": "fictional_contemporary_chile",
        "government_profile": "new_reformist_coalition_government",
        "legislature_balance": {
          "lower_chamber": "narrow_government_majority",
          "upper_chamber": "government_minority"
        },
        "core_metric_defaults": {
          "metrics.legitimacy": 5500,
          "other_core_metrics_defaultS": 5000
        },
        "primary_reform": {
          "route": "A",
          "kind": "SPECIAL_CONSTITUTIONAL",
          "count": 1
        },
        "open_crises_at_tick0": 0,
        "content_pack_change_in_pr8": false
      },
      "rationale": "The MVP now starts from one fixed fictional scenario with one constitutional Route A agenda."
    },
    {
      "id": "MVP-008-primary-objective",
      "topic": "Primary player objective",
      "question": "What is the primary player objective, including explicit success and failure conditions?",
      "status": "approved",
      "resolution": {
        "victory_condition": {
          "target": "special_constitutional_route_a_reform",
          "must_be_approved": true,
          "must_be_applied": true,
          "deadline_week_inclusive": 26
        },
        "defeat_conditions": [
          {
            "type": "deadline",
            "week": 26,
            "requires_approved_and_applied": true
          },
          {
            "type": "terminal_reform_state",
            "target": "special_constitutional_route_a_reform",
            "state": "FAILED"
          },
          {
            "type": "metric_threshold",
            "target": "metrics.legitimacy",
            "op": "<",
            "valueS": 2000
          },
          {
            "type": "blocking_crisis_expiry",
            "state": "unresolved_expired"
          }
        ],
        "out_of_slice": [
          "presidential_election",
          "opposition_neutralization",
          "full_constituent_route_b",
          "four_year_campaign"
        ]
      },
      "rationale": "Victory and defeat are now tied to one Route A constitutional slice with explicit terminal conditions."
    },
    {
      "id": "MVP-009-cadre-schema-and-roles",
      "topic": "Cadre schema and roles",
      "question": "What minimum cadre data schema and playable cadre roles belong to the MVP?",
      "status": "approved",
      "resolution": {
        "initial_cadre_count": 6,
        "roles": [
          "LEGISLATIVE_WHIP",
          "TERRITORIAL_ORGANIZER",
          "SPOKESPERSON"
        ],
        "cadres_per_role": 2,
        "cadre_def_fields": [
          "id",
          "localization_key",
          "role",
          "tags"
        ],
        "cadre_state_fields": [
          "competenceS",
          "loyaltyS",
          "ambitionS",
          "networksS",
          "scandal_riskS",
          "assignment_id",
          "available"
        ],
        "metric_rangeS": {
          "min": 0,
          "max": 10000
        },
        "assignment_id_nullable": true,
        "generic_xp": false,
        "complex_progression_in_slice": false,
        "stable_ids": true,
        "definition_storage": "content_static_outside_save",
        "state_storage": "gamestate_save"
      },
      "rationale": "Cadres now have a minimal frozen schema, fixed roles, and a clear static-versus-save boundary."
    },
    {
      "id": "MVP-010-turn-report-top-n",
      "topic": "Turn-report causal limit",
      "question": "How many causal contributors appear in each turn report, and how are ties ordered deterministically?",
      "status": "approved",
      "resolution": {
        "visible_targets": {
          "max_count": 10,
          "filter": "delta_totalS != 0",
          "order": [
            "abs(delta_totalS)_desc",
            "target_path_ordinal_asc"
          ]
        },
        "causes_per_target": {
          "max_count": 3,
          "order": [
            "abs(delta_totalS)_desc",
            "CauseKey_ordinal_asc"
          ]
        },
        "other_deltaS": "exact_remainder",
        "alerts": {
          "separate_section": true,
          "consume_top_slots": false
        },
        "zero_fill": false
      },
      "rationale": "The turn report now has fixed limits, deterministic tie-breaks, and exact remainder accounting."
    },
    {
      "id": "MVP-011-active-movements",
      "topic": "Active movements",
      "question": "Which movements are enabled at MVP start, and what activation rules apply to them?",
      "status": "approved",
      "resolution": {
        "direction_scope": "scenario_state",
        "active_movements": [
          {
            "theme": "trabajo/costo de vida",
            "movement_id": "mov_trabajo_huelgas",
            "enabled": true,
            "initial_intensityS": 3000,
            "initial_direction": 1,
            "last_addressed_tick": 0
          },
          {
            "theme": "seguridad/orden",
            "movement_id": "mov_seguridad_mano_dura",
            "enabled": true,
            "initial_intensityS": 3000,
            "initial_direction": 1,
            "last_addressed_tick": 0
          },
          {
            "theme": "servicios públicos",
            "movement_id": "mov_salud_crisis_atencion",
            "enabled": true,
            "initial_intensityS": 3000,
            "initial_direction": 1,
            "last_addressed_tick": 0
          },
          {
            "theme": "regional",
            "movement_id": "mov_descentralizacion_regionalista",
            "enabled": true,
            "initial_intensityS": 3000,
            "initial_direction": 1,
            "last_addressed_tick": 0
          }
        ],
        "disabled_movements": [
          {
            "movement_id": "mov_educacion_paros",
            "enabled": false,
            "initial_intensityS": 0,
            "excluded_from_update": true,
            "excluded_from_event_selection": true,
            "excluded_from_crisis_selection": true,
            "exclusion_reason": "avoid_overlap_with_labor_strikes"
          },
          {
            "movement_id": "mov_pensiones_presion_reforma",
            "enabled": false,
            "initial_intensityS": 0,
            "excluded_from_update": true,
            "excluded_from_event_selection": true,
            "excluded_from_crisis_selection": true,
            "exclusion_reason": "avoid_a_second_primary_legislative_reform"
          },
          {
            "movement_id": "mov_institucional_reforma",
            "enabled": false,
            "initial_intensityS": 0,
            "excluded_from_update": true,
            "excluded_from_event_selection": true,
            "excluded_from_crisis_selection": true,
            "exclusion_reason": "avoid_duplication_with_route_a_constitutional_reform"
          },
          {
            "movement_id": "mov_constitucional_proceso",
            "enabled": false,
            "initial_intensityS": 0,
            "excluded_from_update": true,
            "excluded_from_event_selection": true,
            "excluded_from_crisis_selection": true,
            "exclusion_reason": "route_b_pressure_is_outside_mvp"
          },
          {
            "movement_id": "mov_pueblos_originarios_autonomia",
            "enabled": false,
            "initial_intensityS": 0,
            "excluded_from_update": true,
            "excluded_from_event_selection": true,
            "excluded_from_crisis_selection": true,
            "exclusion_reason": "deferred_to_post_slice_territorial_expansion"
          }
        ],
        "environmental_representation": {
          "interest_group_id": "ig_ambiental_regionalista",
          "movement_id": null,
          "representation_level": "interest_group_only"
        },
        "direction_semantics": "favorable_to_own_demand",
        "escalation_rules": {
          "base_increment": {
            "condition": "tick - last_addressed_tick >= 4",
            "deltaS": 100
          },
          "high_tension_bonus": {
            "condition": "metrics.social_tension > 7000",
            "deltaS": 100
          },
          "post_update_clamp": {
            "minS": 0,
            "maxS": 10000
          },
          "alert_threshold": {
            "op": ">",
            "valueS": 7000
          },
          "blocking_crisis_eligibility_threshold": {
            "op": ">",
            "valueS": 8500
          },
          "matching_address_action": "last_addressed_tick = current_tick"
        },
        "disabled_movements_activate_dynamically": false
      },
      "rationale": "The slice now has four active movements, five fully disabled movements, and exact escalation state rules."
    }
  ]
}
```

## Downstream Implementation Map

- PR 9 aligns the aggregation contract and the effects loader to this frozen contract.
- PR 11 implements the causal ledger with the frozen cause categories and report boundaries.
- PR 12 implements effect execution with the frozen SET, ADD, MUL, clamp, and normalization order.
- PR 13 implements the canonical tick, RNG contract, and scheduler ordering.
- PR 16 implements political state, scenario state, and cadre runtime/save boundaries.
- PR 17 implements movement state, escalation, and crisis eligibility rules.
- PR 20 implements the TurnReport limits, tie-breaks, alerts, and exact remainder accounting.

## MVP Boundary

### In MVP

- Route A constitutional reform as the primary objective.
- 26-week vertical slice.
- Four active movements from tick 0.
- Six cadres in three exact roles.
- Top-10 target reporting and Top-3 causes per target.

### Not in MVP

- Route B constituent process resolution.
- Presidential election resolution.
- Four-year campaign arc.
- Dynamic activation of the five disabled movements.
- Content Pack edits in PR 8.
- Runtime implementation in PR 8.

## MVP-001-social-tension-sign

- Topic: Social tension sign convention
- Question: Does a higher social_tension value represent worse tension, and which effect direction increases or reduces tension?
- Status: approved
- Resolution:
  - target: `metrics.social_tension`
  - higher_is_worse: `true`
  - alert_threshold: `valueS > 7000`
  - driver_weight_scale: `weight_ppm`
  - drivers:
    - `internals.tension.cost_of_living = +350000`
    - `internals.tension.polarization = +250000`
    - `internals.tension.protest_activity = +250000`
    - `internals.tension.institutional_trust = -150000`
  - content_alignment:
    - `content_pack_change_in_pr8 = false`
    - `content_pack_change_pr = 9`
- Rationale: The metric now has one sign convention and exact integer drivers for later loader alignment.
- Downstream PRs: `PR 9`, `PR 12`, `PR 20`

## MVP-002-cause-category

- Topic: Closed causal-report categories
- Question: Which closed set of cause categories may appear in causal reports?
- Status: approved
- Resolution:
  - ordered_categories:
    - `DECISION`
    - `EVENT`
    - `REFORM`
    - `MOVEMENT`
    - `MODIFIER`
    - `SYSTEM`
  - modifier_parent_categories:
    - `DECISION`
    - `EVENT`
    - `REFORM`
    - `MOVEMENT`
  - system_causes:
    - clamp: `SYSTEM:CLAMP`
    - rounding: `SYSTEM:ROUNDING`
    - ig_clout_normalize: `SYSTEM:IG_CLOUT_NORMALIZE`
  - allow_additional_categories: `false`
- Rationale: A closed ordered category set is required for stable causal serialization and reporting.
- Downstream PRs: `PR 11`, `PR 20`

## MVP-003-effect-order

- Topic: Effect ordering
- Question: What exact deterministic order applies multiple effects targeting the same value within one tick?
- Status: approved
- Resolution:
  - set_handling:
    - winner_order:
      - `priority_desc`
      - `effect_instance_id_ordinal_asc`
    - apply_winning_set: `true`
    - ignore_add_and_mul_for_same_target: `true`
  - phase_order_without_set:
    - `ADD`
    - `MUL`
  - add_order:
    - `priority_desc`
    - `effect_instance_id_ordinal_asc`
  - mul_order:
    - `priority_desc`
    - `effect_instance_id_ordinal_asc`
  - mul_rounding: `HALF_AWAY_FROM_ZERO`
  - post_processing:
    - `final_clamp`
    - `final_normalization`
    - `system_residue`
  - forbid:
    - `load_order`
    - `dictionary_order`
    - `grouped_mul_product`
    - `floats`
- Rationale: Deterministic per-target ordering is required for replay, testing, and causal explanations.
- Downstream PRs: `PR 12`, `PR 13`, `PR 20`

## MVP-004-tick-order

- Topic: Tick phase ordering
- Question: What exact ordered phases constitute one canonical simulation tick?
- Status: approved
- Resolution:
  - phases:
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
  - regional_feedback_latency_ticks: `1`
- Rationale: One canonical phase order prevents hidden feedback loops and makes reports reproducible.
- Downstream PRs: `PR 13`, `PR 17`, `PR 20`

## MVP-005-rng-contract

- Topic: RNG contract
- Question: What RNG algorithm, seed ownership, draw ordering, and serialized state define deterministic randomness?
- Status: approved
- Resolution:
  - algorithm: `pcg32-xsh-rr`
  - contract_version: `pcg32-v1`
  - serialized_fields:
    - `state_u64` as `hex_lowercase_16`
    - `stream_u64` as `hex_lowercase_16` and odd
    - `draw_count_u64` as `hex_lowercase_16`
  - forbid:
    - `System.Random`
    - `GetHashCode`
    - `implicit_global_rng`
  - consumption_rule: `sequential_only_in_closed_order_systems`
  - event_selector_keying:
    - enabled: `true`
    - encoding: `utf-8`
    - key_parts:
      - `seed`
      - `tick`
      - `system`
      - `template`
      - `slot`
    - derivation: `sha-256`
    - byte_order: `documented_explicitly`
    - final_tie_break: `id_ordinal_asc`
    - determinism_rule: `same_state_and_actions_same_draws_and_hashes`
- Rationale: The RNG contract is frozen now so later runtime work can be deterministic by construction.
- Downstream PRs: `PR 13`

## MVP-006-vertical-slice-duration

- Topic: Vertical-slice duration
- Question: How many turns or simulated months does the MVP vertical slice contain, and what exact condition ends it?
- Status: approved
- Resolution:
  - duration_weeks: `26`
  - target_session_minutes_min: `30`
  - target_session_minutes_max: `60`
  - early_end_conditions:
    - `victory`
    - `defeat`
- Rationale: The slice now has a fixed campaign length with explicit early terminal exits.
- Downstream PRs: `PR 16`, `PR 20`

## MVP-007-initial-scenario

- Topic: Initial scenario
- Question: Which single initial historical scenario and starting date belong to the MVP?
- Status: approved
- Resolution:
  - scenario_id: `scenario_constitutional_reform_mvp`
  - start_date: `2030-03-11`
  - setting: `fictional_contemporary_chile`
  - government_profile: `new_reformist_coalition_government`
  - legislature_balance:
    - lower_chamber: `narrow_government_majority`
    - upper_chamber: `government_minority`
  - core_metric_defaults:
    - `metrics.legitimacy = 5500`
    - `other_core_metrics_defaultS = 5000`
  - primary_reform:
    - route: `A`
    - kind: `SPECIAL_CONSTITUTIONAL`
    - count: `1`
  - open_crises_at_tick0: `0`
  - content_pack_change_in_pr8: `false`
- Rationale: The MVP now starts from one fixed fictional scenario with one constitutional Route A agenda.
- Downstream PRs: `PR 16`, `PR 17`

## MVP-008-primary-objective

- Topic: Primary player objective
- Question: What is the primary player objective, including explicit success and failure conditions?
- Status: approved
- Resolution:
  - victory_condition:
    - target: `special_constitutional_route_a_reform`
    - must_be_approved: `true`
    - must_be_applied: `true`
    - deadline_week_inclusive: `26`
  - defeat_conditions:
    - week 26 without approved and applied Route A reform
    - terminal reform state `FAILED`
    - `metrics.legitimacy < 2000`
    - unresolved blocking crisis expiry
  - out_of_slice:
    - `presidential_election`
    - `opposition_neutralization`
    - `full_constituent_route_b`
    - `four_year_campaign`
- Rationale: Victory and defeat are now tied to one Route A constitutional slice with explicit terminal conditions.
- Downstream PRs: `PR 16`, `PR 17`, `PR 20`

## MVP-009-cadre-schema-and-roles

- Topic: Cadre schema and roles
- Question: What minimum cadre data schema and playable cadre roles belong to the MVP?
- Status: approved
- Resolution:
  - initial_cadre_count: `6`
  - roles:
    - `LEGISLATIVE_WHIP`
    - `TERRITORIAL_ORGANIZER`
    - `SPOKESPERSON`
  - cadres_per_role: `2`
  - cadre_def_fields:
    - `id`
    - `localization_key`
    - `role`
    - `tags`
  - cadre_state_fields:
    - `competenceS`
    - `loyaltyS`
    - `ambitionS`
    - `networksS`
    - `scandal_riskS`
    - `assignment_id`
    - `available`
  - metric_rangeS: `0..10000`
  - assignment_id_nullable: `true`
  - generic_xp: `false`
  - complex_progression_in_slice: `false`
  - stable_ids: `true`
  - definition_storage: `content_static_outside_save`
  - state_storage: `gamestate_save`
- Rationale: Cadres now have a minimal frozen schema, fixed roles, and a clear static-versus-save boundary.
- Downstream PRs: `PR 16`

## MVP-010-turn-report-top-n

- Topic: Turn-report causal limit
- Question: How many causal contributors appear in each turn report, and how are ties ordered deterministically?
- Status: approved
- Resolution:
  - visible_targets:
    - max_count: `10`
    - filter: `delta_totalS != 0`
    - order:
      - `abs(delta_totalS)_desc`
      - `target_path_ordinal_asc`
  - causes_per_target:
    - max_count: `3`
    - order:
      - `abs(delta_totalS)_desc`
      - `CauseKey_ordinal_asc`
  - other_deltaS: `exact_remainder`
  - alerts:
    - separate_section: `true`
    - consume_top_slots: `false`
  - zero_fill: `false`
- Rationale: The turn report now has fixed limits, deterministic tie-breaks, and exact remainder accounting.
- Downstream PRs: `PR 11`, `PR 20`

## MVP-011-active-movements

- Topic: Active movements
- Question: Which movements are enabled at MVP start, and what activation rules apply to them?
- Status: approved
- Resolution:
  - direction_scope: `scenario_state`
  - active_movements:
    - `trabajo/costo de vida -> mov_trabajo_huelgas`
    - `seguridad/orden -> mov_seguridad_mano_dura`
    - `servicios públicos -> mov_salud_crisis_atencion`
    - `regional -> mov_descentralizacion_regionalista`
  - active_initial_state:
    - enabled: `true`
    - initial_intensityS: `3000`
    - initial_direction: `1`
    - last_addressed_tick: `0`
  - disabled_movements:
    - `mov_educacion_paros`
    - `mov_pensiones_presion_reforma`
    - `mov_institucional_reforma`
    - `mov_constitucional_proceso`
    - `mov_pueblos_originarios_autonomia`
  - disabled_initial_state:
    - enabled: `false`
    - initial_intensityS: `0`
    - excluded_from_update: `true`
    - excluded_from_event_selection: `true`
    - excluded_from_crisis_selection: `true`
  - environmental_representation:
    - interest_group_id: `ig_ambiental_regionalista`
    - movement_id: `null`
    - representation_level: `interest_group_only`
  - direction_semantics: `favorable_to_own_demand`
  - escalation_rules:
    - base_increment: `tick - last_addressed_tick >= 4 -> +100`
    - high_tension_bonus: `metrics.social_tension > 7000 -> +100`
    - post_update_clamp: `0..10000`
    - alert_threshold: `> 7000`
    - blocking_crisis_eligibility_threshold: `> 8500`
    - matching_address_action: `last_addressed_tick = current_tick`
  - disabled_movements_activate_dynamically: `false`
- Rationale: The slice now has four active movements, five fully disabled movements, and exact escalation state rules.
- Downstream PRs: `PR 16`, `PR 17`, `PR 20`

## No-MVP Boundary

- `mov_constitucional_proceso` remains outside the MVP because it represents Route B pressure.
- The environmental dimension remains represented by `ig_ambiental_regionalista`.
- No movement ID was invented for an environmental-only movement.
- PR 8 remains contract, documentation, and test only.
