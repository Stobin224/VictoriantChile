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
| MVP-012-national-aggregation | National aggregation contract | approved |

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
        "multiplier_u64_decimal": "6364136223846793005",
        "state_width_bits": 64,
        "output_width_bits": 32,
        "arithmetic": "wrapping_modulo_2pow64_only_where_required_by_pcg32",
        "warmup_draws": 0,
        "byte_order": "little_endian",
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
        "sequential_seed_initialization": {
          "seed_type": "int64_signed",
          "seed_encoding": "twos_complement_little_endian",
          "preimage": {
            "domain_tag": "VictoriantChile/pcg32-v1/init",
            "separator_byte_hex": "00",
            "field_order": [
              "domain_tag_ascii",
              "separator_byte",
              "seed_i64_le"
            ]
          },
          "derivation": "sha-256",
          "digest_extraction": {
            "state_u64": {
              "offset_bytes": [
                0,
                7
              ],
              "byte_order": "little_endian"
            },
            "stream_u64_pre_oddify": {
              "offset_bytes": [
                8,
                15
              ],
              "byte_order": "little_endian"
            },
            "stream_u64_post_process": "bitwise_or_1"
          },
          "draw_count_u64_initial_hex": "0000000000000000"
        },
        "draw_transition": {
          "old_state_source": "state_u64",
          "new_state_formula": "old_state * multiplier + stream_u64 modulo 2^64",
          "xorshifted_formula": "uint32((((old_state >> 18) xor old_state) >> 27))",
          "rotation_formula": "uint32(old_state >> 59)",
          "output_formula": "rotate_right_32(xorshifted, rotation)",
          "post_success": [
            "state_u64 = new_state",
            "draw_count_u64 += 1"
          ]
        },
        "counter_exhaustion": {
          "when": "draw_count_u64 == uint64_max before draw",
          "behavior": "fail_closed_without_state_stream_or_counter_change"
        },
        "bounded_draw": {
          "bound_must_be_positive": true,
          "algorithm": "rejection_sampling_without_modulo_bias",
          "threshold_formula": "(2^32 - bound) mod bound",
          "rejected_raw_draws_increment_counter": true,
          "invalid_bound_consumes_rng": false
        },
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
          "field_types": {
            "seed": "int64_signed",
            "tick": "uint64",
            "system": "ascii_identifier",
            "template": "ascii_identifier",
            "slot": "uint64"
          },
          "string_validation": {
            "pattern": "[a-z0-9][a-z0-9._-]*",
            "forbid": [
              "empty",
              "whitespace",
              "control_chars",
              "newlines",
              "unicode",
              "nul_bytes"
            ]
          },
          "framing": {
            "domain_tag": "VictoriantChile/pcg32-v1/event-selector",
            "separator_byte_hex": "00",
            "length_unit": "utf8_bytes",
            "integer_byte_order": "little_endian",
            "field_order": [
              "domain_tag_ascii",
              "separator_byte",
              "seed_i64_le",
              "tick_u64_le",
              "system_len_u32_le",
              "system_utf8",
              "template_len_u32_le",
              "template_utf8",
              "slot_u64_le"
            ]
          },
          "derivation": "sha-256",
          "digest_extraction": {
            "keyed_state_u64": {
              "offset_bytes": [
                0,
                7
              ],
              "byte_order": "little_endian"
            },
            "keyed_stream_u64_pre_oddify": {
              "offset_bytes": [
                8,
                15
              ],
              "byte_order": "little_endian"
            },
            "keyed_stream_u64_post_process": "bitwise_or_1"
          },
          "keyed_draw": "first_pcg32_output_from_derived_state",
          "global_state_consumption": false,
          "warmup_draws": 0,
          "final_tie_break": "id_ordinal_asc",
          "determinism_rule": "same_state_and_actions_same_draws_and_hashes"
        }
      },
      "rationale": "A human-approved PR 13 amendment completes pcg32-v1 byte-for-byte without creating pcg32-v2."
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
    },
    {
      "id": "MVP-012-national-aggregation",
      "topic": "National aggregation contract",
      "question": "What exact fixed-point, phase, snapshot, cap, rounding, and causal-allocation rules define national aggregation?",
      "status": "approved",
      "resolution": {
        "numeric_domain": {
          "scale": 100,
          "S": 100,
          "midS": 5000,
          "minS": 0,
          "maxS": 10000,
          "ppm_denominator": 1000000,
          "intermediate_arithmetic": "long_checked",
          "stored_type": "int",
          "rounding": "HALF_AWAY_FROM_ZERO",
          "forbidden": [
            "float",
            "double",
            "decimal",
            "dictionary_order",
            "componentwise_rounding",
            "culture_dependent_rounding",
            "silent_overflow"
          ]
        },
        "phase_dispatch": {
          "internal_reversion_phase": 6,
          "derived_internals_phase": 7,
          "aggregate_national_metrics_phase": 8,
          "reversion_input_snapshot": "post_apply_per_tick_modifiers",
          "reversion_output_snapshot": "post_reversion",
          "derived_input_snapshot": "post_reversion",
          "aggregation_input_snapshot": "post_derived_internals_before_aggregation",
          "dispatch_rule": "scheduler_dispatches_by_type_not_array_position",
          "phase_8_order": [
            {
              "pass": "METRIC_AGGREGATION",
              "metrics_count": 9,
              "note": "nine primary metrics"
            },
            {
              "pass": "METRIC_AGGREGATION",
              "metrics_count": 1,
              "metric": "metrics.legitimacy",
              "note": "legitimacy after derived reads pre-aggregation metrics"
            }
          ]
        },
        "pass_execution_semantics": {
          "input_snapshot": "immutable_snapshot_at_pass_start",
          "planning": "all_outputs_and_causal_contributions_planned_before_publication",
          "publication": "single_atomic_batch",
          "next_pass_visibility": "complete_output_of_previous_pass",
          "failure_behavior": "fail_closed_without_partial_state_or_causal_publication",
          "rule_order": "config_order",
          "dictionary_order_forbidden": true,
          "duplicate_target_policy": "fail_closed_before_publication",
          "overlapping_reversion_group_policy": "fail_closed_before_publication",
          "internal_reversion": {
            "snapshot_rule": "immutable_at_pass_start",
            "cross_observation_forbidden": true,
            "plan_before_publication": true,
            "atomic_batch": true,
            "overlapping_group_fail_closed": true
          },
          "derived_internals": {
            "snapshot_rule": "post_reversion_immutable",
            "cross_observation_forbidden": true,
            "plan_before_publication": true,
            "atomic_batch": true,
            "duplicate_target_fail_closed": true
          },
          "metric_aggregation": {
            "pass_level_snapshot": true,
            "cross_metric_observation_within_pass_forbidden": true,
            "next_pass_sees_complete_state": true,
            "plan_before_publication": true,
            "atomic_batch": true
          },
          "fail_closed_triggers": [
            "missing_target",
            "duplicate_target",
            "overlapping_reversion_group",
            "arithmetic_overflow",
            "out_of_range_conversion",
            "invalid_cause_prefix",
            "causal_accounting_mismatch",
            "ledger_rejection"
          ],
          "fail_closed_guarantees": [
            "zero_partial_state",
            "zero_partial_internals",
            "zero_partial_metrics",
            "zero_partial_causal_contributions"
          ]
        },
        "reversion_formula": {
          "distanceS": "midS - currentS",
          "reversion_deltaS": "RoundHalfAwayFromZero(distanceS * alpha_ppm / 1000000)",
          "pre_clampS": "currentS + reversion_deltaS",
          "finalS": "clamp(pre_clampS, TargetConfig.minS, TargetConfig.maxS)",
          "order": [
            "subtract_current_from_midpoint",
            "multiply_by_alpha_ppm",
            "single_division_rounding",
            "add_to_current",
            "final_clamp"
          ],
          "arithmetic": "long_checked_before_int_cast",
          "no_extra_weekly_cap": true,
          "skip_targets": [
            "internals.legitimacy.performance",
            "internals.legitimacy.social_tension_load"
          ]
        },
        "derived_formulas": {
          "internals.legitimacy.performance": {
            "op": "SET",
            "expression": "AVG(metrics.economy, metrics.security, metrics.governability)",
            "sum_arithmetic": "long_checked",
            "div_rounding": "HALF_AWAY_FROM_ZERO",
            "reads": "pre_aggregation_metrics_current_tick"
          },
          "internals.legitimacy.social_tension_load": {
            "op": "SET",
            "expression": "COPY(metrics.social_tension)",
            "reads": "pre_aggregation_metrics_current_tick"
          },
          "legitimacy_latency_note": "legitimacy aggregation in phase 8 sees pre-aggregation economy, security, governability, social_tension; legitimacy has one-tick latency for structural changes from aggregation of those metrics in same tick"
        },
        "metric_aggregation_formula": {
          "weighted_offset_numerator": "SUM(weight_ppm[i] * (componentS[i] - midS))",
          "weighted_offsetS": "RoundHalfAwayFromZero(weighted_offset_numerator / 1000000)",
          "target_unclampedS": "midS + weighted_offsetS",
          "targetS": "clamp(target_unclampedS, TargetConfig.minS, TargetConfig.maxS)",
          "elastic_distance": "targetS - current_metricS",
          "elastic_numerator": "elastic_distance * alpha_ppm",
          "elastic_deltaS": "RoundHalfAwayFromZero(elastic_numerator / 1000000)",
          "capped_deltaS": "clamp(elastic_deltaS, -cap_per_weekS, +cap_per_weekS)",
          "pre_finalS": "current_metricS + capped_deltaS",
          "final_metricS": "clamp(pre_finalS, TargetConfig.minS, TargetConfig.maxS)",
          "delta_totalS": "final_metricS - current_metricS",
          "pipeline_order": [
            "sum_weighted_offsets",
            "single_rounding_for_weighted_offset",
            "target_clamp",
            "compute_distance_to_target",
            "elasticity_rounding",
            "weekly_cap",
            "add_to_current_metric",
            "final_clamp"
          ],
          "forbidden": [
            "swap_cap_and_rounding",
            "apply_cap_to_weighted_target_instead_of_delta"
          ]
        },
        "causal_algorithm": {
          "name": "ordered_prefix_counterfactual_marginal_v1",
          "description": "For each metric define F(vector) as the full aggregation pipeline returning final_metricS",
          "V0": "all components replaced by midS",
          "Vi": "first i components real, remaining at midS",
          "Vn": "all components real",
          "base_deltaS": "F(V0) - current_metricS",
          "base_cause": "SYSTEM:AGG.<metric>",
          "component_deltaS": "F(Vi) - F(Vi-1)",
          "component_cause": "SYSTEM:AGG.<metric>.<component>",
          "telescopic_identity": "F(Vn) - current_metricS == base_deltaS + SUM(component_deltaS)",
          "component_order": "exact_config_order",
          "zero_contributions_omitted_from_ledger": true,
          "forbidden_methods": [
            "proportional_split",
            "largest_remainder",
            "shapley",
            "dictionary_order",
            "invented_residual_to_balance"
          ],
          "no_additional_system_causes": "SYSTEM:ROUNDING and SYSTEM:CLAMP reserved for other systems; do not emit within aggregation marginal attribution"
        },
        "cause_key_grammar": {
          "format": "CATEGORY + ':' + ID",
          "id_forbidden_chars": [
            ":",
            "|",
            "whitespace",
            "control_chars",
            "non_ascii_unicode"
          ],
          "id_separator": ".",
          "permitted_prefixes_for_pr14": [
            "AGG.",
            "REVERSION.",
            "DERIVED."
          ],
          "examples": [
            "SYSTEM:AGG.metrics.economy",
            "SYSTEM:AGG.metrics.economy.internals.economy.growth",
            "SYSTEM:REVERSION.internals.economy.growth",
            "SYSTEM:DERIVED.internals.legitimacy.performance"
          ],
          "forbidden_ambiguous_forms": [
            "SYSTEM:AGG:metrics.economy",
            "SYSTEM:AGG:metrics.economy:internals.economy.growth"
          ]
        },
        "cause_prefix_materialization": {
          "source_format": "CATEGORY:BASE_ID",
          "separator_count": 1,
          "required_category": "SYSTEM",
          "allowed_base_ids": [
            "AGG",
            "REVERSION",
            "DERIVED"
          ],
          "target_separator": ".",
          "target_path_preserved_verbatim": true,
          "invalid_prefix_behavior": "fail_closed_before_publication",
          "materializations": {
            "aggregation_base": {
              "cause_prefix": "SYSTEM:AGG",
              "metric": "metrics.economy",
              "expected_cause_ref": {
                "category": "CauseCategory.System",
                "id": "AGG.metrics.economy"
              },
              "canonical_key": "SYSTEM:AGG.metrics.economy"
            },
            "aggregation_component": {
              "cause_prefix": "SYSTEM:AGG",
              "metric": "metrics.economy",
              "component": "internals.economy.growth",
              "expected_cause_ref": {
                "category": "CauseCategory.System",
                "id": "AGG.metrics.economy.internals.economy.growth"
              },
              "canonical_key": "SYSTEM:AGG.metrics.economy.internals.economy.growth"
            },
            "reversion": {
              "cause_prefix": "SYSTEM:REVERSION",
              "target": "internals.economy.growth",
              "expected_cause_ref": {
                "category": "CauseCategory.System",
                "id": "REVERSION.internals.economy.growth"
              },
              "canonical_key": "SYSTEM:REVERSION.internals.economy.growth"
            },
            "derived": {
              "cause_prefix": "SYSTEM:DERIVED",
              "target": "internals.legitimacy.performance",
              "expected_cause_ref": {
                "category": "CauseCategory.System",
                "id": "DERIVED.internals.legitimacy.performance"
              },
              "canonical_key": "SYSTEM:DERIVED.internals.legitimacy.performance"
            }
          },
          "must_fail_prefixes": [
            "AGG",
            "SYSTEM:AGG:EXTRA",
            "EVENT:AGG",
            "SYSTEM:",
            "SYSTEM:UNKNOWN",
            "system:AGG",
            "SYSTEM:AGG."
          ]
        },
        "hidden_internal_policy": {
          "internals_remain_hidden": true,
          "no_public_target_catalog_entry": true,
          "no_TickCausalBuffer_own_row": true,
          "no_top_n_slot_consumption": true,
          "no_accidental_TurnReport_exposure": true,
          "reversion_provenance": "SYSTEM:REVERSION.<internal_target>",
          "derived_provenance": "SYSTEM:DERIVED.<internal_target>",
          "public_influence_through": "SYSTEM:AGG.<metric>.<internal_target>",
          "no_double_counting": true,
          "documentation_only_in_pr14_1": true,
          "provenance_scope": "ephemeral_execution_plan_only",
          "provenance_serialized": false,
          "provenance_stored_in_game_state": false,
          "provenance_stored_in_public_ledger": false,
          "provenance_exposed_in_turn_report": false,
          "provenance_lifetime": "current_pass_only",
          "provenance_clarifications": {
            "reversion_labels": "ephemeral_plan_provenance_no_serialization",
            "derived_labels": "ephemeral_plan_provenance_no_serialization",
            "no_game_state_schema_change": true,
            "no_second_ledger": true,
            "no_hidden_ledger_rows": true,
            "no_top_n_appearance": true,
            "no_turn_report_appearance": true,
            "lifetime": "current_pass_only",
            "purpose": "structured_diagnostic_and_traceability_during_planning_validation_only",
            "public_influence_only_through": "SYSTEM:AGG.<metric>.<internal_target>",
            "single_registration_rule": "do_not_register_same_influence_as_REVERSION_DERIVED_AND_AGG_simultaneously"
          }
        },
        "vectors": {
          "reversion_6000_to_5974": {
            "currentS": 6000,
            "midS": 5000,
            "alpha_ppm": 26307,
            "distanceS": -1000,
            "rounded_deltaS": -26,
            "finalS": 5974
          },
          "economy": {
            "current_metricS": 5000,
            "components": [
              {
                "target": "internals.economy.growth",
                "componentS": 6000,
                "weight_ppm": 350000
              },
              {
                "target": "internals.economy.unemployment",
                "componentS": 4000,
                "weight_ppm": -250000
              },
              {
                "target": "internals.economy.inflation",
                "componentS": 5000,
                "weight_ppm": -250000
              },
              {
                "target": "internals.economy.fiscal_stability",
                "componentS": 6000,
                "weight_ppm": 150000
              }
            ],
            "alpha_ppm": 82996,
            "cap_per_weekS": 200,
            "weighted_offsetS": 750,
            "targetS": 5750,
            "elastic_deltaS": 62,
            "capped_deltaS": 62,
            "finalS": 5062,
            "delta_totalS": 62
          },
          "social_tension": {
            "current_metricS": 5000,
            "components": [
              {
                "target": "internals.tension.cost_of_living",
                "componentS": 6000,
                "weight_ppm": 350000
              },
              {
                "target": "internals.tension.polarization",
                "componentS": 6000,
                "weight_ppm": 250000
              },
              {
                "target": "internals.tension.protest_activity",
                "componentS": 4000,
                "weight_ppm": 250000
              },
              {
                "target": "internals.tension.institutional_trust",
                "componentS": 6000,
                "weight_ppm": -150000
              }
            ],
            "alpha_ppm": 159104,
            "cap_per_weekS": 400,
            "weighted_offsetS": 200,
            "targetS": 5200,
            "elastic_deltaS": 32,
            "capped_deltaS": 32,
            "finalS": 5032,
            "delta_totalS": 32
          },
          "cap_weekly": {
            "currentS": 5000,
            "targetS": 10000,
            "alpha_ppm": 292893,
            "cap_per_weekS": 600,
            "elastic_deltaS": 1464,
            "capped_deltaS": 600,
            "finalS": 5600
          },
          "rounding_half_away_from_zero": [
            {
              "numerator": 500000,
              "denominator": 1000000,
              "result": 1
            },
            {
              "numerator": -500000,
              "denominator": 1000000,
              "result": -1
            }
          ]
        },
        "causal_vectors": {
          "economy_prefix_deltas": {
            "F(V0)": 5000,
            "F(V1)": 5029,
            "F(V2)": 5050,
            "F(V3)": 5050,
            "F(V4)": 5062,
            "base_deltaS": 0,
            "component_deltas": [
              {
                "component": "internals.economy.growth",
                "deltaS": 29
              },
              {
                "component": "internals.economy.unemployment",
                "deltaS": 21
              },
              {
                "component": "internals.economy.inflation",
                "deltaS": 0,
                "omitted": true
              },
              {
                "component": "internals.economy.fiscal_stability",
                "deltaS": 12
              }
            ],
            "sum_component_deltas": 62,
            "telescopic_check": "62 == 0 + 29 + 21 + 0 + 12"
          },
          "social_tension_prefix_deltas": {
            "F(V0)": 5000,
            "F(V1)": 5056,
            "F(V2)": 5095,
            "F(V3)": 5056,
            "F(V4)": 5032,
            "base_deltaS": 0,
            "component_deltas": [
              {
                "component": "internals.tension.cost_of_living",
                "deltaS": 56
              },
              {
                "component": "internals.tension.polarization",
                "deltaS": 39
              },
              {
                "component": "internals.tension.protest_activity",
                "deltaS": -39
              },
              {
                "component": "internals.tension.institutional_trust",
                "deltaS": -24
              }
            ],
            "sum_component_deltas": 32,
            "telescopic_check": "32 == 0 + 56 + 39 + (-39) + (-24)"
          }
        }
      },
      "rationale": "National aggregation now has one fixed-point execution order, one pre-aggregation derived snapshot, one exact telescoping causal allocation, pass-execution semantics for atomic snapshots and fail-closed guarantees, materialization rules for cause_prefix, and ephemeral provenance scope for hidden internals."
    }
  ]
}
```

## Downstream Implementation Map

- PR 9 aligns the aggregation contract and the effects loader to this frozen contract.
- PR 11 implements the causal ledger with the frozen cause categories and report boundaries.
- PR 12 implements effect execution with the frozen SET, ADD, MUL, clamp, and normalization order.
- PR 13 implements the canonical tick, RNG contract, and scheduler ordering.
- PR 14.1 freezes the aggregation contract: numeric domain, phase dispatch, reversion, derived internals, metric formula, cap, rounding, causal algorithm, cause key grammar, and hidden internal policy.
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
  - multiplier_u64_decimal: `6364136223846793005`
  - state_width_bits: `64`
  - output_width_bits: `32`
  - arithmetic: `wrapping_modulo_2pow64_only_where_required_by_pcg32`
  - warmup_draws: `0`
  - byte_order: `little_endian`
  - serialized_fields:
    - `state_u64` as `hex_lowercase_16`
    - `stream_u64` as `hex_lowercase_16` and odd
    - `draw_count_u64` as `hex_lowercase_16`
  - forbid:
    - `System.Random`
    - `GetHashCode`
    - `implicit_global_rng`
  - consumption_rule: `sequential_only_in_closed_order_systems`
  - sequential_seed_initialization:
    - seed_type: `int64_signed`
    - seed_encoding: `twos_complement_little_endian`
    - preimage:
      - domain_tag: `VictoriantChile/pcg32-v1/init`
      - separator_byte_hex: `00`
      - field_order:
        - `domain_tag_ascii`
        - `separator_byte`
        - `seed_i64_le`
    - derivation: `sha-256`
    - digest_extraction:
      - `state_u64`: bytes `0..7`, little-endian
      - `stream_u64_pre_oddify`: bytes `8..15`, little-endian
      - `stream_u64_post_process`: `bitwise_or_1`
    - draw_count_u64_initial_hex: `0000000000000000`
  - draw_transition:
    - old_state_source: `state_u64`
    - new_state_formula: `old_state * multiplier + stream_u64 modulo 2^64`
    - xorshifted_formula: `uint32((((old_state >> 18) xor old_state) >> 27))`
    - rotation_formula: `uint32(old_state >> 59)`
    - output_formula: `rotate_right_32(xorshifted, rotation)`
    - post_success:
      - `state_u64 = new_state`
      - `draw_count_u64 += 1`
  - counter_exhaustion:
    - when: `draw_count_u64 == uint64_max before draw`
    - behavior: `fail_closed_without_state_stream_or_counter_change`
  - bounded_draw:
    - bound_must_be_positive: `true`
    - algorithm: `rejection_sampling_without_modulo_bias`
    - threshold_formula: `(2^32 - bound) mod bound`
    - rejected_raw_draws_increment_counter: `true`
    - invalid_bound_consumes_rng: `false`
  - event_selector_keying:
    - enabled: `true`
    - encoding: `utf-8`
    - key_parts:
      - `seed`
      - `tick`
      - `system`
      - `template`
      - `slot`
    - field_types:
      - `seed`: `int64_signed`
      - `tick`: `uint64`
      - `system`: `ascii_identifier`
      - `template`: `ascii_identifier`
      - `slot`: `uint64`
    - string_validation:
      - pattern: `[a-z0-9][a-z0-9._-]*`
      - forbid:
        - `empty`
        - `whitespace`
        - `control_chars`
        - `newlines`
        - `unicode`
        - `nul_bytes`
    - framing:
      - domain_tag: `VictoriantChile/pcg32-v1/event-selector`
      - separator_byte_hex: `00`
      - length_unit: `utf8_bytes`
      - integer_byte_order: `little_endian`
      - field_order:
        - `domain_tag_ascii`
        - `separator_byte`
        - `seed_i64_le`
        - `tick_u64_le`
        - `system_len_u32_le`
        - `system_utf8`
        - `template_len_u32_le`
        - `template_utf8`
        - `slot_u64_le`
    - derivation: `sha-256`
    - digest_extraction:
      - `keyed_state_u64`: bytes `0..7`, little-endian
      - `keyed_stream_u64_pre_oddify`: bytes `8..15`, little-endian
      - `keyed_stream_u64_post_process`: `bitwise_or_1`
    - keyed_draw: `first_pcg32_output_from_derived_state`
    - global_state_consumption: `false`
    - warmup_draws: `0`
    - final_tie_break: `id_ordinal_asc`
    - determinism_rule: `same_state_and_actions_same_draws_and_hashes`
- Rationale: A human-approved PR 13 amendment completes pcg32-v1 byte-for-byte without creating pcg32-v2.
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

## MVP-012-national-aggregation

- Topic: National aggregation contract
- Question: What exact fixed-point, phase, snapshot, cap, rounding, and causal-allocation rules define national aggregation?
- Status: approved
- Resolution:
  - numeric_domain:
    - scale: `100`
    - S: `100`
    - midS: `5000`
    - minS: `0`, maxS: `10000`
    - ppm_denominator: `1000000`
    - intermediate_arithmetic: `long_checked`
    - stored_type: `int`
    - rounding: `HALF_AWAY_FROM_ZERO`
    - forbidden: `float`, `double`, `decimal`, `dictionary_order`, `componentwise_rounding`, `culture_dependent_rounding`, `silent_overflow`
  - phase_dispatch:
    - `revert_internals` -> phase 6, reads `post_apply_per_tick_modifiers`, produces `post_reversion`
    - `derive_internals` -> phase 7, reads `post_reversion`, produces pre-aggregation derived values
    - `aggregate_national_metrics` -> phase 8
    - dispatch_rule: `scheduler_dispatches_by_type_not_array_position`
    - phase 8 order:
      - first pass: `METRIC_AGGREGATION` of nine primary metrics
      - second pass: `METRIC_AGGREGATION` of `metrics.legitimacy`
  - reversion_formula:
    - `distanceS = midS - currentS`
    - `reversion_deltaS = RoundHalfAwayFromZero(distanceS * alpha_ppm / 1000000)`
    - `pre_clampS = currentS + reversion_deltaS`
    - `finalS = clamp(pre_clampS, TargetConfig.minS, TargetConfig.maxS)`
    - order: subtract from midpoint, multiply by alpha, single rounding, add, final clamp
    - arithmetic: `long_checked_before_int_cast`
    - no extra weekly cap
    - skip_targets: `internals.legitimacy.performance`, `internals.legitimacy.social_tension_load`
  - derived_formulas:
    - `internals.legitimacy.performance` = `AVG(metrics.economy, metrics.security, metrics.governability)` with `long_checked` sum, `HALF_AWAY_FROM_ZERO` division
    - `internals.legitimacy.social_tension_load` = `COPY(metrics.social_tension)`
    - both read pre-aggregation metrics of the current tick
    - legitimacy has one-tick latency for structural changes from aggregation of economy, security, governability, social_tension
  - metric_aggregation_formula:
    - `weighted_offset_numerator = SUM(weight_ppm[i] * (componentS[i] - midS))`
    - `weighted_offsetS = RoundHalfAwayFromZero(weighted_offset_numerator / 1000000)`
    - `target_unclampedS = midS + weighted_offsetS`
    - `targetS = clamp(target_unclampedS, TargetConfig.minS, TargetConfig.maxS)`
    - `elastic_distance = targetS - current_metricS`
    - `elastic_numerator = elastic_distance * alpha_ppm`
    - `elastic_deltaS = RoundHalfAwayFromZero(elastic_numerator / 1000000)`
    - `capped_deltaS = clamp(elastic_deltaS, -cap_per_weekS, +cap_per_weekS)`
    - `pre_finalS = current_metricS + capped_deltaS`
    - `final_metricS = clamp(pre_finalS, TargetConfig.minS, TargetConfig.maxS)`
    - `delta_totalS = final_metricS - current_metricS`
    - pipeline order: weighted sum, single offset rounding, target clamp, distance, elasticity rounding, weekly cap, add, final clamp
    - forbidden: swap cap and rounding, apply cap to weighted target instead of delta
  - causal_algorithm:
    - `ordered_prefix_counterfactual_marginal_v1`
    - `F(vector)` = full aggregation pipeline returning `final_metricS`
    - V0 = all components at midS, Vi = first i real, Vn = all real
    - `base_deltaS = F(V0) - current_metricS`, cause: `SYSTEM:AGG.<metric>`
    - `component_deltaS = F(Vi) - F(Vi-1)`, cause: `SYSTEM:AGG.<metric>.<component>`
    - telescopic: `F(Vn) - current_metricS == base_deltaS + SUM(component_deltaS)`
    - component order = exact config order
    - zero contributions omitted from ledger
    - forbidden: proportional split, largest remainder, Shapley, dictionary order, invented residual
    - no additional SYSTEM:ROUNDING or SYSTEM:CLAMP within aggregation marginal attribution
  - cause_key_grammar:
    - format: `CATEGORY + ':' + ID`
    - ID forbidden: `:`, `|`, whitespace, controls, non-ASCII Unicode
    - ID separator: `.`
    - permitted prefixes for PR 14: `AGG.`, `REVERSION.`, `DERIVED.`
    - examples: `SYSTEM:AGG.metrics.economy`, `SYSTEM:AGG.metrics.economy.internals.economy.growth`, `SYSTEM:REVERSION.internals.economy.growth`, `SYSTEM:DERIVED.internals.legitimacy.performance`
    - forbidden ambiguous: `SYSTEM:AGG:metrics.economy`, `SYSTEM:AGG:metrics.economy:internals.economy.growth`
  - pass_execution_semantics:
    - each pass reads an immutable snapshot taken at pass start
    - all outputs and causal contributions are planned before publication
    - validation completes before publication
    - state and causality are published as a single atomic batch
    - a failure does not publish partial state or partial causality
    - the next pass observes the complete output of the previous pass
    - within the same pass, no rule observes partial outputs of another rule
    - `rule_order = config_order`
    - dictionary order is forbidden
    - duplicate targets fail before publication
    - overlapping reversion groups fail before publication
  - cause_prefix_materialization:
    - source format: `CATEGORY:BASE_ID`
    - exactly one `:`
    - required category: `SYSTEM`
    - allowed base IDs: `AGG`, `REVERSION`, `DERIVED`
    - `.` concatenated with the unmodified target path
    - any invalid prefix fails before publication
    - valid examples:
      - `SYSTEM:AGG` + `metrics.economy` → CauseRef(SYSTEM, `AGG.metrics.economy`) → `SYSTEM:AGG.metrics.economy`
      - `SYSTEM:REVERSION` + `internals.economy.growth` → CauseRef(SYSTEM, `REVERSION.internals.economy.growth`) → `SYSTEM:REVERSION.internals.economy.growth`
      - `SYSTEM:DERIVED` + `internals.legitimacy.performance` → CauseRef(SYSTEM, `DERIVED.internals.legitimacy.performance`) → `SYSTEM:DERIVED.internals.legitimacy.performance`
    - forbidden examples:
      - `SYSTEM:AGG:metrics.economy` (double colon)
      - `SYSTEM::AGG` (empty base ID)
      - category other than `SYSTEM`
      - unknown base ID
      - empty target
      - prefix with more than one `:`
  - hidden_internal_policy:
    - internals remain hidden from public target catalog, TickCausalBuffer rows, Top-N slots, TurnReport
    - reversion provenance: `SYSTEM:REVERSION.<internal_target>`
    - derived provenance: `SYSTEM:DERIVED.<internal_target>`
    - public influence through: `SYSTEM:AGG.<metric>.<internal_target>`
    - no double counting
    - provenance scope: `ephemeral_execution_plan_only`
    - provenance serialized: `false`
    - stored in GameState: `false`
    - stored in public ledger: `false`
    - exposed in turn report: `false`
    - lifetime: `current_pass_only`
    - single registration rule: do not register the same influence as REVERSION, DERIVED, and AGG simultaneously
  - vectors:
    - reversion: `6000 -> 5974` with `alpha_ppm = 26307`
    - economy: `weighted_offsetS = 750`, `targetS = 5750`, `elastic_deltaS = 62`, `finalS = 5062`
    - social_tension: `weighted_offsetS = 200`, `targetS = 5200`, `elastic_deltaS = 32`, `finalS = 5032`
    - cap_weekly: `currentS = 5000`, `targetS = 10000`, `alpha_ppm = 292893`, `cap_per_weekS = 600`, `elastic_deltaS = 1464`, `capped_deltaS = 600`, `finalS = 5600`
    - rounding_half_away_from_zero: `+500000/1000000 = +1`, `-500000/1000000 = -1`
  - causal_vectors:
    - economy F(V0..4): `5000, 5029, 5050, 5050, 5062`, deltas: `0, +29, +21, 0(omit), +12`, sum = 62
    - social_tension F(V0..4): `5000, 5056, 5095, 5056, 5032`, deltas: `0, +56, +39, -39, -24`, sum = 32
- Rationale: National aggregation now has one fixed-point execution order, one pre-aggregation derived snapshot, one exact telescoping causal allocation, pass-execution semantics for atomic snapshots and fail-closed guarantees, materialization rules for cause_prefix, and ephemeral provenance scope for hidden internals.
- Downstream PRs: `PR 14.2+ (implementation)`

## No-MVP Boundary

- `mov_constitucional_proceso` remains outside the MVP because it represents Route B pressure.
- The environmental dimension remains represented by `ig_ambiental_regionalista`.
- No movement ID was invented for an environmental-only movement.
- PR 8 remains contract, documentation, and test only.
