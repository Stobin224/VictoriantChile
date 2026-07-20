from __future__ import annotations

import json
import re
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
JSON_PATH = ROOT / "docs" / "mvp_contract_decisions.json"
MARKDOWN_PATH = ROOT / "docs" / "mvp_contract_decisions.md"
MOVEMENTS_PATH = ROOT / "Assets" / "StreamingAssets" / "content" / "core" / "movements.json"

WINDOWS_ABSOLUTE_PATH_RE = re.compile(r"[A-Za-z]:[\\/]")
POSIX_ABSOLUTE_PATH_RE = re.compile(r"(^|[^A-Za-z0-9_./-])/(Users|home|tmp|var|etc|opt|mnt|srv|run)(/|$)")
TIMESTAMP_RE = re.compile(r"\d{4}-\d{2}-\d{2}[T ][0-9:.+-]*")
SECTION_HEADING_RE = re.compile(r"^## (MVP-[0-9]{3}-[a-z0-9-]+)\r?$", re.MULTILINE)
CANONICAL_JSON_BLOCK_RE = re.compile(r"## Canonical JSON\s+```json\s+(.*?)\s+```", re.DOTALL)

FORBIDDEN_TEXT_SNIPPETS = (
    "thread_id",
    "session_id",
    "\"author\"",
    "\"timestamp\"",
    ".agent-loop",
    "pending_human_decision",
    "awaiting_human_decisions",
    "TBD",
    "TODO",
    "to be decided",
)
FORBIDDEN_AMBIGUOUS_PHRASES = (
    "preferentemente",
    "podria",
    "podría",
    "por decidir",
)
DECISION_KEYS = ("id", "topic", "question", "status", "resolution", "rationale")
TOP_LEVEL_KEYS = ("schema_version", "register_status", "decisions")
ACTIVE_MOVEMENT_IDS = [
    "mov_trabajo_huelgas",
    "mov_seguridad_mano_dura",
    "mov_salud_crisis_atencion",
    "mov_descentralizacion_regionalista",
]
DISABLED_MOVEMENT_IDS = [
    "mov_educacion_paros",
    "mov_pensiones_presion_reforma",
    "mov_institucional_reforma",
    "mov_constitucional_proceso",
    "mov_pueblos_originarios_autonomia",
]
ALL_CONTENT_PACK_MOVEMENT_IDS = ACTIVE_MOVEMENT_IDS + DISABLED_MOVEMENT_IDS

EXPECTED_DOCUMENT = {
        'schema_version': 2,
        'register_status': 'frozen',
        'decisions': [
            {
                'id': 'MVP-001-social-tension-sign',
                'topic': 'Social tension sign convention',
                'question': 'Does a higher social_tension value represent worse tension, and which effect direction increases or reduces tension?',
                'status': 'approved',
                'resolution': {
                    'target': 'metrics.social_tension',
                    'higher_is_worse': True,
                    'alert_threshold': {
                        'op': '>',
                        'valueS': 7000
                    },
                    'driver_weight_scale': 'weight_ppm',
                    'drivers': [
                        {
                            'target': 'internals.tension.cost_of_living',
                            'weight_ppm': 350000
                        },
                        {
                            'target': 'internals.tension.polarization',
                            'weight_ppm': 250000
                        },
                        {
                            'target': 'internals.tension.protest_activity',
                            'weight_ppm': 250000
                        },
                        {
                            'target': 'internals.tension.institutional_trust',
                            'weight_ppm': -150000
                        }
                    ],
                    'content_alignment': {
                        'content_pack_change_in_pr8': False,
                        'content_pack_change_pr': 9
                    }
                },
                'rationale': 'The metric now has one sign convention and exact integer drivers for later loader alignment.'
            },
            {
                'id': 'MVP-002-cause-category',
                'topic': 'Closed causal-report categories',
                'question': 'Which closed set of cause categories may appear in causal reports?',
                'status': 'approved',
                'resolution': {
                    'ordered_categories': ['DECISION', 'EVENT', 'REFORM', 'MOVEMENT', 'MODIFIER', 'SYSTEM'],
                    'modifier_parent_categories': ['DECISION', 'EVENT', 'REFORM', 'MOVEMENT'],
                    'system_causes': {
                        'clamp': 'SYSTEM:CLAMP',
                        'rounding': 'SYSTEM:ROUNDING',
                        'ig_clout_normalize': 'SYSTEM:IG_CLOUT_NORMALIZE'
                    },
                    'allow_additional_categories': False
                },
                'rationale': 'A closed ordered category set is required for stable causal serialization and reporting.'
            },
            {
                'id': 'MVP-003-effect-order',
                'topic': 'Effect ordering',
                'question': 'What exact deterministic order applies multiple effects targeting the same value within one tick?',
                'status': 'approved',
                'resolution': {
                    'set_handling': {
                        'winner_order': ['priority_desc', 'effect_instance_id_ordinal_asc'],
                        'apply_winning_set': True,
                        'ignore_add_and_mul_for_same_target': True
                    },
                    'phase_order_without_set': ['ADD', 'MUL'],
                    'add_order': ['priority_desc', 'effect_instance_id_ordinal_asc'],
                    'mul_order': ['priority_desc', 'effect_instance_id_ordinal_asc'],
                    'mul_rounding': 'HALF_AWAY_FROM_ZERO',
                    'post_processing': ['final_clamp', 'final_normalization', 'system_residue'],
                    'forbid': ['load_order', 'dictionary_order', 'grouped_mul_product', 'floats']
                },
                'rationale': 'Deterministic per-target ordering is required for replay, testing, and causal explanations.'
            },
            {
                'id': 'MVP-004-tick-order',
                'topic': 'Tick phase ordering',
                'question': 'What exact ordered phases constitute one canonical simulation tick?',
                'status': 'approved',
                'resolution': {
                    'phases': [
                        'increment_tick',
                        'expire_effects',
                        'execute_scheduled_actions',
                        'apply_start_instant_modifiers',
                        'apply_per_tick_modifiers',
                        'revert_internals',
                        'derive_internals',
                        'aggregate_national_metrics',
                        'drift_national_to_regions',
                        'pull_regions_to_internals',
                        'update_movements',
                        'advance_reforms',
                        'resolve_events_and_crises',
                        'apply_final_clamps_and_normalizations',
                        'close_causal_report',
                        'detect_and_publish_blocking_decision'
                    ],
                    'regional_feedback_latency_ticks': 1
                },
                'rationale': 'One canonical phase order prevents hidden feedback loops and makes reports reproducible.'
            },
            {
                'id': 'MVP-005-rng-contract',
                'topic': 'RNG contract',
                'question': 'What RNG algorithm, seed ownership, draw ordering, and serialized state define deterministic randomness?',
                'status': 'approved',
                'resolution': {
                    'algorithm': 'pcg32-xsh-rr',
                    'contract_version': 'pcg32-v1',
                    'multiplier_u64_decimal': '6364136223846793005',
                    'state_width_bits': 64,
                    'output_width_bits': 32,
                    'arithmetic': 'wrapping_modulo_2pow64_only_where_required_by_pcg32',
                    'warmup_draws': 0,
                    'byte_order': 'little_endian',
                    'serialized_fields': [
                        {
                            'name': 'state_u64',
                            'format': 'hex_lowercase_16'
                        },
                        {
                            'name': 'stream_u64',
                            'format': 'hex_lowercase_16',
                            'must_be_odd': True
                        },
                        {
                            'name': 'draw_count_u64',
                            'format': 'hex_lowercase_16'
                        }
                    ],
                    'forbid': ['System.Random', 'GetHashCode', 'implicit_global_rng'],
                    'consumption_rule': 'sequential_only_in_closed_order_systems',
                    'sequential_seed_initialization': {
                        'seed_type': 'int64_signed',
                        'seed_encoding': 'twos_complement_little_endian',
                        'preimage': {
                            'domain_tag': 'VictoriantChile/pcg32-v1/init',
                            'separator_byte_hex': '00',
                            'field_order': ['domain_tag_ascii', 'separator_byte', 'seed_i64_le']
                        },
                        'derivation': 'sha-256',
                        'digest_extraction': {
                            'state_u64': {
                                'offset_bytes': [0, 7],
                                'byte_order': 'little_endian'
                            },
                            'stream_u64_pre_oddify': {
                                'offset_bytes': [8, 15],
                                'byte_order': 'little_endian'
                            },
                            'stream_u64_post_process': 'bitwise_or_1'
                        },
                        'draw_count_u64_initial_hex': '0000000000000000'
                    },
                    'draw_transition': {
                        'old_state_source': 'state_u64',
                        'new_state_formula': 'old_state * multiplier + stream_u64 modulo 2^64',
                        'xorshifted_formula': 'uint32((((old_state >> 18) xor old_state) >> 27))',
                        'rotation_formula': 'uint32(old_state >> 59)',
                        'output_formula': 'rotate_right_32(xorshifted, rotation)',
                        'post_success': ['state_u64 = new_state', 'draw_count_u64 += 1']
                    },
                    'counter_exhaustion': {
                        'when': 'draw_count_u64 == uint64_max before draw',
                        'behavior': 'fail_closed_without_state_stream_or_counter_change'
                    },
                    'bounded_draw': {
                        'bound_must_be_positive': True,
                        'algorithm': 'rejection_sampling_without_modulo_bias',
                        'threshold_formula': '(2^32 - bound) mod bound',
                        'rejected_raw_draws_increment_counter': True,
                        'invalid_bound_consumes_rng': False
                    },
                    'event_selector_keying': {
                        'enabled': True,
                        'encoding': 'utf-8',
                        'key_parts': ['seed', 'tick', 'system', 'template', 'slot'],
                        'field_types': {
                            'seed': 'int64_signed',
                            'tick': 'uint64',
                            'system': 'ascii_identifier',
                            'template': 'ascii_identifier',
                            'slot': 'uint64'
                        },
                        'string_validation': {
                            'pattern': '[a-z0-9][a-z0-9._-]*',
                            'forbid': ['empty', 'whitespace', 'control_chars', 'newlines', 'unicode', 'nul_bytes']
                        },
                        'framing': {
                            'domain_tag': 'VictoriantChile/pcg32-v1/event-selector',
                            'separator_byte_hex': '00',
                            'length_unit': 'utf8_bytes',
                            'integer_byte_order': 'little_endian',
                            'field_order': [
                                'domain_tag_ascii',
                                'separator_byte',
                                'seed_i64_le',
                                'tick_u64_le',
                                'system_len_u32_le',
                                'system_utf8',
                                'template_len_u32_le',
                                'template_utf8',
                                'slot_u64_le'
                            ]
                        },
                        'derivation': 'sha-256',
                        'digest_extraction': {
                            'keyed_state_u64': {
                                'offset_bytes': [0, 7],
                                'byte_order': 'little_endian'
                            },
                            'keyed_stream_u64_pre_oddify': {
                                'offset_bytes': [8, 15],
                                'byte_order': 'little_endian'
                            },
                            'keyed_stream_u64_post_process': 'bitwise_or_1'
                        },
                        'keyed_draw': 'first_pcg32_output_from_derived_state',
                        'global_state_consumption': False,
                        'warmup_draws': 0,
                        'final_tie_break': 'id_ordinal_asc',
                        'determinism_rule': 'same_state_and_actions_same_draws_and_hashes'
                    }
                },
                'rationale': 'A human-approved PR 13 amendment completes pcg32-v1 byte-for-byte without creating pcg32-v2.'
            },
            {
                'id': 'MVP-006-vertical-slice-duration',
                'topic': 'Vertical-slice duration',
                'question': 'How many turns or simulated months does the MVP vertical slice contain, and what exact condition ends it?',
                'status': 'approved',
                'resolution': {
                    'duration_weeks': 26,
                    'target_session_minutes_min': 30,
                    'target_session_minutes_max': 60,
                    'early_end_conditions': ['victory', 'defeat']
                },
                'rationale': 'The slice now has a fixed campaign length with explicit early terminal exits.'
            },
            {
                'id': 'MVP-007-initial-scenario',
                'topic': 'Initial scenario',
                'question': 'Which single initial historical scenario and starting date belong to the MVP?',
                'status': 'approved',
                'resolution': {
                    'scenario_id': 'scenario_constitutional_reform_mvp',
                    'start_date': '2030-03-11',
                    'setting': 'fictional_contemporary_chile',
                    'government_profile': 'new_reformist_coalition_government',
                    'legislature_balance': {
                        'lower_chamber': 'narrow_government_majority',
                        'upper_chamber': 'government_minority'
                    },
                    'core_metric_defaults': {
                        'metrics.legitimacy': 5500,
                        'other_core_metrics_defaultS': 5000
                    },
                    'primary_reform': {
                        'route': 'A',
                        'kind': 'SPECIAL_CONSTITUTIONAL',
                        'count': 1
                    },
                    'open_crises_at_tick0': 0,
                    'content_pack_change_in_pr8': False
                },
                'rationale': 'The MVP now starts from one fixed fictional scenario with one constitutional Route A agenda.'
            },
            {
                'id': 'MVP-008-primary-objective',
                'topic': 'Primary player objective',
                'question': 'What is the primary player objective, including explicit success and failure conditions?',
                'status': 'approved',
                'resolution': {
                    'victory_condition': {
                        'target': 'special_constitutional_route_a_reform',
                        'must_be_approved': True,
                        'must_be_applied': True,
                        'deadline_week_inclusive': 26
                    },
                    'defeat_conditions': [
                        {
                            'type': 'deadline',
                            'week': 26,
                            'requires_approved_and_applied': True
                        },
                        {
                            'type': 'terminal_reform_state',
                            'target': 'special_constitutional_route_a_reform',
                            'state': 'FAILED'
                        },
                        {
                            'type': 'metric_threshold',
                            'target': 'metrics.legitimacy',
                            'op': '<',
                            'valueS': 2000
                        },
                        {
                            'type': 'blocking_crisis_expiry',
                            'state': 'unresolved_expired'
                        }
                    ],
                    'out_of_slice': ['presidential_election', 'opposition_neutralization', 'full_constituent_route_b', 'four_year_campaign']
                },
                'rationale': 'Victory and defeat are now tied to one Route A constitutional slice with explicit terminal conditions.'
            },
            {
                'id': 'MVP-009-cadre-schema-and-roles',
                'topic': 'Cadre schema and roles',
                'question': 'What minimum cadre data schema and playable cadre roles belong to the MVP?',
                'status': 'approved',
                'resolution': {
                    'initial_cadre_count': 6,
                    'roles': ['LEGISLATIVE_WHIP', 'TERRITORIAL_ORGANIZER', 'SPOKESPERSON'],
                    'cadres_per_role': 2,
                    'cadre_def_fields': ['id', 'localization_key', 'role', 'tags'],
                    'cadre_state_fields': ['competenceS', 'loyaltyS', 'ambitionS', 'networksS', 'scandal_riskS', 'assignment_id', 'available'],
                    'metric_rangeS': {
                        'min': 0,
                        'max': 10000
                    },
                    'assignment_id_nullable': True,
                    'generic_xp': False,
                    'complex_progression_in_slice': False,
                    'stable_ids': True,
                    'definition_storage': 'content_static_outside_save',
                    'state_storage': 'gamestate_save'
                },
                'rationale': 'Cadres now have a minimal frozen schema, fixed roles, and a clear static-versus-save boundary.'
            },
            {
                'id': 'MVP-010-turn-report-top-n',
                'topic': 'Turn-report causal limit',
                'question': 'How many causal contributors appear in each turn report, and how are ties ordered deterministically?',
                'status': 'approved',
                'resolution': {
                    'visible_targets': {
                        'max_count': 10,
                        'filter': 'delta_totalS != 0',
                        'order': ['abs(delta_totalS)_desc', 'target_path_ordinal_asc']
                    },
                    'causes_per_target': {
                        'max_count': 3,
                        'order': ['abs(delta_totalS)_desc', 'CauseKey_ordinal_asc']
                    },
                    'other_deltaS': 'exact_remainder',
                    'alerts': {
                        'separate_section': True,
                        'consume_top_slots': False
                    },
                    'zero_fill': False
                },
                'rationale': 'The turn report now has fixed limits, deterministic tie-breaks, and exact remainder accounting.'
            },
            {
                'id': 'MVP-011-active-movements',
                'topic': 'Active movements',
                'question': 'Which movements are enabled at MVP start, and what activation rules apply to them?',
                'status': 'approved',
                'resolution': {
                    'direction_scope': 'scenario_state',
                    'active_movements': [
                        {
                            'theme': 'trabajo/costo de vida',
                            'movement_id': 'mov_trabajo_huelgas',
                            'enabled': True,
                            'initial_intensityS': 3000,
                            'initial_direction': 1,
                            'last_addressed_tick': 0
                        },
                        {
                            'theme': 'seguridad/orden',
                            'movement_id': 'mov_seguridad_mano_dura',
                            'enabled': True,
                            'initial_intensityS': 3000,
                            'initial_direction': 1,
                            'last_addressed_tick': 0
                        },
                        {
                            'theme': 'servicios públicos',
                            'movement_id': 'mov_salud_crisis_atencion',
                            'enabled': True,
                            'initial_intensityS': 3000,
                            'initial_direction': 1,
                            'last_addressed_tick': 0
                        },
                        {
                            'theme': 'regional',
                            'movement_id': 'mov_descentralizacion_regionalista',
                            'enabled': True,
                            'initial_intensityS': 3000,
                            'initial_direction': 1,
                            'last_addressed_tick': 0
                        }
                    ],
                    'disabled_movements': [
                        {
                            'movement_id': 'mov_educacion_paros',
                            'enabled': False,
                            'initial_intensityS': 0,
                            'excluded_from_update': True,
                            'excluded_from_event_selection': True,
                            'excluded_from_crisis_selection': True,
                            'exclusion_reason': 'avoid_overlap_with_labor_strikes'
                        },
                        {
                            'movement_id': 'mov_pensiones_presion_reforma',
                            'enabled': False,
                            'initial_intensityS': 0,
                            'excluded_from_update': True,
                            'excluded_from_event_selection': True,
                            'excluded_from_crisis_selection': True,
                            'exclusion_reason': 'avoid_a_second_primary_legislative_reform'
                        },
                        {
                            'movement_id': 'mov_institucional_reforma',
                            'enabled': False,
                            'initial_intensityS': 0,
                            'excluded_from_update': True,
                            'excluded_from_event_selection': True,
                            'excluded_from_crisis_selection': True,
                            'exclusion_reason': 'avoid_duplication_with_route_a_constitutional_reform'
                        },
                        {
                            'movement_id': 'mov_constitucional_proceso',
                            'enabled': False,
                            'initial_intensityS': 0,
                            'excluded_from_update': True,
                            'excluded_from_event_selection': True,
                            'excluded_from_crisis_selection': True,
                            'exclusion_reason': 'route_b_pressure_is_outside_mvp'
                        },
                        {
                            'movement_id': 'mov_pueblos_originarios_autonomia',
                            'enabled': False,
                            'initial_intensityS': 0,
                            'excluded_from_update': True,
                            'excluded_from_event_selection': True,
                            'excluded_from_crisis_selection': True,
                            'exclusion_reason': 'deferred_to_post_slice_territorial_expansion'
                        }
                    ],
                    'environmental_representation': {
                        'interest_group_id': 'ig_ambiental_regionalista',
                        'movement_id': None,
                        'representation_level': 'interest_group_only'
                    },
                    'direction_semantics': 'favorable_to_own_demand',
                    'escalation_rules': {
                        'base_increment': {
                            'condition': 'tick - last_addressed_tick >= 4',
                            'deltaS': 100
                        },
                        'high_tension_bonus': {
                            'condition': 'metrics.social_tension > 7000',
                            'deltaS': 100
                        },
                        'post_update_clamp': {
                            'minS': 0,
                            'maxS': 10000
                        },
                        'alert_threshold': {
                            'op': '>',
                            'valueS': 7000
                        },
                        'blocking_crisis_eligibility_threshold': {
                            'op': '>',
                            'valueS': 8500
                        },
                        'matching_address_action': 'last_addressed_tick = current_tick'
                    },
                    'disabled_movements_activate_dynamically': False
                },
                'rationale': 'The slice now has four active movements, five fully disabled movements, and exact escalation state rules.'
            },
            {
                'id': 'MVP-012-national-aggregation',
                'topic': 'National aggregation contract',
                'question': 'What exact fixed-point, phase, snapshot, cap, rounding, and causal-allocation rules define national aggregation?',
                'status': 'approved',
                'resolution': {
                    'numeric_domain': {
                        'scale': 100,
                        'S': 100,
                        'midS': 5000,
                        'minS': 0,
                        'maxS': 10000,
                        'ppm_denominator': 1000000,
                        'intermediate_arithmetic': 'long_checked',
                        'stored_type': 'int',
                        'rounding': 'HALF_AWAY_FROM_ZERO',
                        'forbidden': ['float', 'double', 'decimal', 'dictionary_order', 'componentwise_rounding', 'culture_dependent_rounding', 'silent_overflow']
                    },
                    'phase_dispatch': {
                        'internal_reversion_phase': 6,
                        'derived_internals_phase': 7,
                        'aggregate_national_metrics_phase': 8,
                        'reversion_input_snapshot': 'post_apply_per_tick_modifiers',
                        'reversion_output_snapshot': 'post_reversion',
                        'derived_input_snapshot': 'post_reversion',
                        'aggregation_input_snapshot': 'post_derived_internals_before_aggregation',
                        'dispatch_rule': 'scheduler_dispatches_by_type_not_array_position',
                        'phase_8_order': [
                            {
                                'pass': 'METRIC_AGGREGATION',
                                'metrics_count': 9,
                                'note': 'nine primary metrics'
                            },
                            {
                                'pass': 'METRIC_AGGREGATION',
                                'metrics_count': 1,
                                'metric': 'metrics.legitimacy',
                                'note': 'legitimacy after derived reads pre-aggregation metrics'
                            }
                        ]
                    },
                    'pass_execution_semantics': {
                        'input_snapshot': 'immutable_snapshot_at_pass_start',
                        'planning': 'all_outputs_and_causal_contributions_planned_before_publication',
                        'publication': 'single_atomic_batch',
                        'next_pass_visibility': 'complete_output_of_previous_pass',
                        'failure_behavior': 'fail_closed_without_partial_state_or_causal_publication',
                        'rule_order': 'config_order',
                        'dictionary_order_forbidden': True,
                        'duplicate_target_policy': 'fail_closed_before_publication',
                        'overlapping_reversion_group_policy': 'fail_closed_before_publication',
                        'internal_reversion': {
                            'snapshot_rule': 'immutable_at_pass_start',
                            'cross_observation_forbidden': True,
                            'plan_before_publication': True,
                            'atomic_batch': True,
                            'overlapping_group_fail_closed': True
                        },
                        'derived_internals': {
                            'snapshot_rule': 'post_reversion_immutable',
                            'cross_observation_forbidden': True,
                            'plan_before_publication': True,
                            'atomic_batch': True,
                            'duplicate_target_fail_closed': True
                        },
                        'metric_aggregation': {
                            'pass_level_snapshot': True,
                            'cross_metric_observation_within_pass_forbidden': True,
                            'next_pass_sees_complete_state': True,
                            'plan_before_publication': True,
                            'atomic_batch': True
                        },
                        'fail_closed_triggers': ['missing_target', 'duplicate_target', 'overlapping_reversion_group', 'arithmetic_overflow', 'out_of_range_conversion', 'invalid_cause_prefix', 'causal_accounting_mismatch', 'ledger_rejection'],
                        'fail_closed_guarantees': ['zero_partial_state', 'zero_partial_internals', 'zero_partial_metrics', 'zero_partial_causal_contributions']
                    },
                    'reversion_formula': {
                        'distanceS': 'midS - currentS',
                        'reversion_deltaS': 'RoundHalfAwayFromZero(distanceS * alpha_ppm / 1000000)',
                        'pre_clampS': 'currentS + reversion_deltaS',
                        'finalS': 'clamp(pre_clampS, TargetConfig.minS, TargetConfig.maxS)',
                        'order': ['subtract_current_from_midpoint', 'multiply_by_alpha_ppm', 'single_division_rounding', 'add_to_current', 'final_clamp'],
                        'arithmetic': 'long_checked_before_int_cast',
                        'no_extra_weekly_cap': True,
                        'skip_targets': ['internals.legitimacy.performance', 'internals.legitimacy.social_tension_load']
                    },
                    'derived_formulas': {
                        'internals.legitimacy.performance': {
                            'op': 'SET',
                            'expression': 'AVG(metrics.economy, metrics.security, metrics.governability)',
                            'sum_arithmetic': 'long_checked',
                            'div_rounding': 'HALF_AWAY_FROM_ZERO',
                            'reads': 'pre_aggregation_metrics_current_tick'
                        },
                        'internals.legitimacy.social_tension_load': {
                            'op': 'SET',
                            'expression': 'COPY(metrics.social_tension)',
                            'reads': 'pre_aggregation_metrics_current_tick'
                        },
                        'legitimacy_latency_note': 'legitimacy aggregation in phase 8 sees pre-aggregation economy, security, governability, social_tension; legitimacy has one-tick latency for structural changes from aggregation of those metrics in same tick'
                    },
                    'metric_aggregation_formula': {
                        'weighted_offset_numerator': 'SUM(weight_ppm[i] * (componentS[i] - midS))',
                        'weighted_offsetS': 'RoundHalfAwayFromZero(weighted_offset_numerator / 1000000)',
                        'target_unclampedS': 'midS + weighted_offsetS',
                        'targetS': 'clamp(target_unclampedS, TargetConfig.minS, TargetConfig.maxS)',
                        'elastic_distance': 'targetS - current_metricS',
                        'elastic_numerator': 'elastic_distance * alpha_ppm',
                        'elastic_deltaS': 'RoundHalfAwayFromZero(elastic_numerator / 1000000)',
                        'capped_deltaS': 'clamp(elastic_deltaS, -cap_per_weekS, +cap_per_weekS)',
                        'pre_finalS': 'current_metricS + capped_deltaS',
                        'final_metricS': 'clamp(pre_finalS, TargetConfig.minS, TargetConfig.maxS)',
                        'delta_totalS': 'final_metricS - current_metricS',
                        'pipeline_order': ['sum_weighted_offsets', 'single_rounding_for_weighted_offset', 'target_clamp', 'compute_distance_to_target', 'elasticity_rounding', 'weekly_cap', 'add_to_current_metric', 'final_clamp'],
                        'forbidden': ['swap_cap_and_rounding', 'apply_cap_to_weighted_target_instead_of_delta']
                    },
                    'causal_algorithm': {
                        'name': 'ordered_prefix_counterfactual_marginal_v1',
                        'description': 'For each metric define F(vector) as the full aggregation pipeline returning final_metricS',
                        'V0': 'all components replaced by midS',
                        'Vi': 'first i components real, remaining at midS',
                        'Vn': 'all components real',
                        'base_deltaS': 'F(V0) - current_metricS',
                        'base_cause': 'SYSTEM:AGG.<metric>',
                        'component_deltaS': 'F(Vi) - F(Vi-1)',
                        'component_cause': 'SYSTEM:AGG.<metric>.<component>',
                        'telescopic_identity': 'F(Vn) - current_metricS == base_deltaS + SUM(component_deltaS)',
                        'component_order': 'exact_config_order',
                        'zero_contributions_omitted_from_ledger': True,
                        'forbidden_methods': ['proportional_split', 'largest_remainder', 'shapley', 'dictionary_order', 'invented_residual_to_balance'],
                        'no_additional_system_causes': 'SYSTEM:ROUNDING and SYSTEM:CLAMP reserved for other systems; do not emit within aggregation marginal attribution'
                    },
                    'cause_key_grammar': {
                        'format': "CATEGORY + ':' + ID",
                        'id_forbidden_chars': [':', '|', 'whitespace', 'control_chars', 'non_ascii_unicode'],
                        'id_separator': '.',
                        'permitted_prefixes_for_pr14': ['AGG.', 'REVERSION.', 'DERIVED.'],
                        'examples': ['SYSTEM:AGG.metrics.economy', 'SYSTEM:AGG.metrics.economy.internals.economy.growth', 'SYSTEM:REVERSION.internals.economy.growth', 'SYSTEM:DERIVED.internals.legitimacy.performance'],
                        'forbidden_ambiguous_forms': ['SYSTEM:AGG:metrics.economy', 'SYSTEM:AGG:metrics.economy:internals.economy.growth']
                    },
                    'cause_prefix_materialization': {
                        'source_format': 'CATEGORY:BASE_ID',
                        'separator_count': 1,
                        'required_category': 'SYSTEM',
                        'allowed_base_ids': ['AGG', 'REVERSION', 'DERIVED'],
                        'target_separator': '.',
                        'target_path_preserved_verbatim': True,
                        'invalid_prefix_behavior': 'fail_closed_before_publication',
                        'materializations': {
                            'aggregation_base': {
                                'cause_prefix': 'SYSTEM:AGG',
                                'metric': 'metrics.economy',
                                'expected_cause_ref': {
                                    'category': 'CauseCategory.System',
                                    'id': 'AGG.metrics.economy'
                                },
                                'canonical_key': 'SYSTEM:AGG.metrics.economy'
                            },
                            'aggregation_component': {
                                'cause_prefix': 'SYSTEM:AGG',
                                'metric': 'metrics.economy',
                                'component': 'internals.economy.growth',
                                'expected_cause_ref': {
                                    'category': 'CauseCategory.System',
                                    'id': 'AGG.metrics.economy.internals.economy.growth'
                                },
                                'canonical_key': 'SYSTEM:AGG.metrics.economy.internals.economy.growth'
                            },
                            'reversion': {
                                'cause_prefix': 'SYSTEM:REVERSION',
                                'target': 'internals.economy.growth',
                                'expected_cause_ref': {
                                    'category': 'CauseCategory.System',
                                    'id': 'REVERSION.internals.economy.growth'
                                },
                                'canonical_key': 'SYSTEM:REVERSION.internals.economy.growth'
                            },
                            'derived': {
                                'cause_prefix': 'SYSTEM:DERIVED',
                                'target': 'internals.legitimacy.performance',
                                'expected_cause_ref': {
                                    'category': 'CauseCategory.System',
                                    'id': 'DERIVED.internals.legitimacy.performance'
                                },
                                'canonical_key': 'SYSTEM:DERIVED.internals.legitimacy.performance'
                            }
                        },
                        'must_fail_prefixes': ['AGG', 'SYSTEM:AGG:EXTRA', 'EVENT:AGG', 'SYSTEM:', 'SYSTEM:UNKNOWN', 'system:AGG', 'SYSTEM:AGG.']
                    },
                    'hidden_internal_policy': {
                        'internals_remain_hidden': True,
                        'no_public_target_catalog_entry': True,
                        'no_TickCausalBuffer_own_row': True,
                        'no_top_n_slot_consumption': True,
                        'no_accidental_TurnReport_exposure': True,
                        'reversion_provenance': 'SYSTEM:REVERSION.<internal_target>',
                        'derived_provenance': 'SYSTEM:DERIVED.<internal_target>',
                        'public_influence_through': 'SYSTEM:AGG.<metric>.<internal_target>',
                        'no_double_counting': True,
                        'documentation_only_in_pr14_1': True,
                        'provenance_scope': 'ephemeral_execution_plan_only',
                        'provenance_serialized': False,
                        'provenance_stored_in_game_state': False,
                        'provenance_stored_in_public_ledger': False,
                        'provenance_exposed_in_turn_report': False,
                        'provenance_lifetime': 'current_pass_only',
                        'provenance_clarifications': {
                            'reversion_labels': 'ephemeral_plan_provenance_no_serialization',
                            'derived_labels': 'ephemeral_plan_provenance_no_serialization',
                            'no_game_state_schema_change': True,
                            'no_second_ledger': True,
                            'no_hidden_ledger_rows': True,
                            'no_top_n_appearance': True,
                            'no_turn_report_appearance': True,
                            'lifetime': 'current_pass_only',
                            'purpose': 'structured_diagnostic_and_traceability_during_planning_validation_only',
                            'public_influence_only_through': 'SYSTEM:AGG.<metric>.<internal_target>',
                            'single_registration_rule': 'do_not_register_same_influence_as_REVERSION_DERIVED_AND_AGG_simultaneously'
                        }
                    },
                    'vectors': {
                        'reversion_6000_to_5974': {
                            'currentS': 6000,
                            'midS': 5000,
                            'alpha_ppm': 26307,
                            'distanceS': -1000,
                            'rounded_deltaS': -26,
                            'finalS': 5974
                        },
                        'economy': {
                            'current_metricS': 5000,
                            'components': [
                                {
                                    'target': 'internals.economy.growth',
                                    'componentS': 6000,
                                    'weight_ppm': 350000
                                },
                                {
                                    'target': 'internals.economy.unemployment',
                                    'componentS': 4000,
                                    'weight_ppm': -250000
                                },
                                {
                                    'target': 'internals.economy.inflation',
                                    'componentS': 5000,
                                    'weight_ppm': -250000
                                },
                                {
                                    'target': 'internals.economy.fiscal_stability',
                                    'componentS': 6000,
                                    'weight_ppm': 150000
                                }
                            ],
                            'alpha_ppm': 82996,
                            'cap_per_weekS': 200,
                            'weighted_offsetS': 750,
                            'targetS': 5750,
                            'elastic_deltaS': 62,
                            'capped_deltaS': 62,
                            'finalS': 5062,
                            'delta_totalS': 62
                        },
                        'social_tension': {
                            'current_metricS': 5000,
                            'components': [
                                {
                                    'target': 'internals.tension.cost_of_living',
                                    'componentS': 6000,
                                    'weight_ppm': 350000
                                },
                                {
                                    'target': 'internals.tension.polarization',
                                    'componentS': 6000,
                                    'weight_ppm': 250000
                                },
                                {
                                    'target': 'internals.tension.protest_activity',
                                    'componentS': 4000,
                                    'weight_ppm': 250000
                                },
                                {
                                    'target': 'internals.tension.institutional_trust',
                                    'componentS': 6000,
                                    'weight_ppm': -150000
                                }
                            ],
                            'alpha_ppm': 159104,
                            'cap_per_weekS': 400,
                            'weighted_offsetS': 200,
                            'targetS': 5200,
                            'elastic_deltaS': 32,
                            'capped_deltaS': 32,
                            'finalS': 5032,
                            'delta_totalS': 32
                        },
                        'cap_weekly': {
                            'currentS': 5000,
                            'targetS': 10000,
                            'alpha_ppm': 292893,
                            'cap_per_weekS': 600,
                            'elastic_deltaS': 1464,
                            'capped_deltaS': 600,
                            'finalS': 5600
                        },
                        'rounding_half_away_from_zero': [
                            {
                                'numerator': 500000,
                                'denominator': 1000000,
                                'result': 1
                            },
                            {
                                'numerator': -500000,
                                'denominator': 1000000,
                                'result': -1
                            }
                        ]
                    },
                    'causal_vectors': {
                        'economy_prefix_deltas': {
                            'F(V0)': 5000,
                            'F(V1)': 5029,
                            'F(V2)': 5050,
                            'F(V3)': 5050,
                            'F(V4)': 5062,
                            'base_deltaS': 0,
                            'component_deltas': [
                                {
                                    'component': 'internals.economy.growth',
                                    'deltaS': 29
                                },
                                {
                                    'component': 'internals.economy.unemployment',
                                    'deltaS': 21
                                },
                                {
                                    'component': 'internals.economy.inflation',
                                    'deltaS': 0,
                                    'omitted': True
                                },
                                {
                                    'component': 'internals.economy.fiscal_stability',
                                    'deltaS': 12
                                }
                            ],
                            'sum_component_deltas': 62,
                            'telescopic_check': '62 == 0 + 29 + 21 + 0 + 12'
                        },
                        'social_tension_prefix_deltas': {
                            'F(V0)': 5000,
                            'F(V1)': 5056,
                            'F(V2)': 5095,
                            'F(V3)': 5056,
                            'F(V4)': 5032,
                            'base_deltaS': 0,
                            'component_deltas': [
                                {
                                    'component': 'internals.tension.cost_of_living',
                                    'deltaS': 56
                                },
                                {
                                    'component': 'internals.tension.polarization',
                                    'deltaS': 39
                                },
                                {
                                    'component': 'internals.tension.protest_activity',
                                    'deltaS': -39
                                },
                                {
                                    'component': 'internals.tension.institutional_trust',
                                    'deltaS': -24
                                }
                            ],
                            'sum_component_deltas': 32,
                            'telescopic_check': '32 == 0 + 56 + 39 + (-39) + (-24)'
                        }
                    }
                },
                'rationale': 'National aggregation now has one fixed-point execution order, one pre-aggregation derived snapshot, one exact telescoping causal allocation, pass-execution semantics for atomic snapshots and fail-closed guarantees, materialization rules for cause_prefix, and ephemeral provenance scope for hidden internals.'
            }
        ]
    }


def read_json_bytes() -> bytes:
    return JSON_PATH.read_bytes()


def read_json_document() -> dict:
    return json.loads(read_json_bytes().decode("utf-8"))


def read_markdown_bytes() -> bytes:
    return MARKDOWN_PATH.read_bytes()


def read_markdown_text() -> str:
    return read_markdown_bytes().decode("utf-8")


def read_movements_document() -> dict:
    return json.loads(MOVEMENTS_PATH.read_text(encoding="utf-8"))


class MvpContractDecisionsTest(unittest.TestCase):
    def test_files_exist(self) -> None:
        self.assertTrue(JSON_PATH.exists(), JSON_PATH)
        self.assertTrue(MARKDOWN_PATH.exists(), MARKDOWN_PATH)
        self.assertTrue(MOVEMENTS_PATH.exists(), MOVEMENTS_PATH)

    def test_json_file_encoding_and_newlines(self) -> None:
        raw = read_json_bytes()
        raw.decode("utf-8")
        self.assertFalse(raw.startswith(b"\xef\xbb\xbf"))
        self.assertNotIn(b"\r", raw)
        self.assertTrue(raw.endswith(b"\n"))
        self.assertFalse(raw.endswith(b"\n\n"))

    def test_markdown_file_encoding_and_newlines(self) -> None:
        raw = read_markdown_bytes()
        raw.decode("utf-8")
        self.assertFalse(raw.startswith(b"\xef\xbb\xbf"))
        self.assertNotIn(b"\r", raw)
        self.assertTrue(raw.endswith(b"\n"))
        self.assertFalse(raw.endswith(b"\n\n"))

    def test_json_is_deterministically_indented(self) -> None:
        expected = json.dumps(EXPECTED_DOCUMENT, indent=2, ensure_ascii=False) + "\n"
        self.assertEqual(read_json_bytes().decode("utf-8"), expected)

    def test_json_schema_and_contract_are_exact(self) -> None:
        data = read_json_document()
        self.assertEqual(tuple(data.keys()), TOP_LEVEL_KEYS)
        self.assertIs(type(data["schema_version"]), int)
        self.assertEqual(data["schema_version"], 2)
        self.assertEqual(data["register_status"], "frozen")
        self.assertEqual(data, EXPECTED_DOCUMENT)
        self.assertEqual(len(data["decisions"]), 12)

        for decision in data["decisions"]:
            self.assertEqual(tuple(decision.keys()), DECISION_KEYS)
            self.assertEqual(decision["status"], "approved")
            self.assertIsInstance(decision["resolution"], dict)
            self.assertIsInstance(decision["rationale"], str)
            self.assertTrue(decision["rationale"])

    def test_mvp_002_categories_are_exact(self) -> None:
        resolution = read_json_document()["decisions"][1]["resolution"]
        self.assertEqual(
            resolution["ordered_categories"],
            ["DECISION", "EVENT", "REFORM", "MOVEMENT", "MODIFIER", "SYSTEM"],
        )
        self.assertEqual(
            resolution["modifier_parent_categories"],
            ["DECISION", "EVENT", "REFORM", "MOVEMENT"],
        )

    def test_mvp_003_effect_order_is_exact(self) -> None:
        resolution = read_json_document()["decisions"][2]["resolution"]
        self.assertEqual(
            resolution["set_handling"]["winner_order"],
            ["priority_desc", "effect_instance_id_ordinal_asc"],
        )
        self.assertEqual(resolution["phase_order_without_set"], ["ADD", "MUL"])
        self.assertEqual(
            resolution["post_processing"],
            ["final_clamp", "final_normalization", "system_residue"],
        )

    def test_mvp_004_tick_order_is_exact(self) -> None:
        resolution = read_json_document()["decisions"][3]["resolution"]
        self.assertEqual(
            resolution["phases"],
            [
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
                "detect_and_publish_blocking_decision",
            ],
        )
        self.assertEqual(resolution["regional_feedback_latency_ticks"], 1)

    def test_mvp_005_rng_contract_is_exact(self) -> None:
        resolution = read_json_document()["decisions"][4]["resolution"]
        self.assertEqual(resolution["algorithm"], "pcg32-xsh-rr")
        self.assertEqual(resolution["contract_version"], "pcg32-v1")
        self.assertEqual(resolution["multiplier_u64_decimal"], "6364136223846793005")
        self.assertEqual(resolution["state_width_bits"], 64)
        self.assertEqual(resolution["output_width_bits"], 32)
        self.assertEqual(resolution["warmup_draws"], 0)
        self.assertEqual(resolution["byte_order"], "little_endian")
        self.assertEqual(
            [field["name"] for field in resolution["serialized_fields"]],
            ["state_u64", "stream_u64", "draw_count_u64"],
        )
        self.assertTrue(resolution["serialized_fields"][1]["must_be_odd"])
        self.assertEqual(
            resolution["sequential_seed_initialization"]["preimage"],
            {
                "domain_tag": "VictoriantChile/pcg32-v1/init",
                "separator_byte_hex": "00",
                "field_order": [
                    "domain_tag_ascii",
                    "separator_byte",
                    "seed_i64_le",
                ],
            },
        )
        self.assertEqual(
            resolution["sequential_seed_initialization"]["digest_extraction"],
            {
                "state_u64": {"offset_bytes": [0, 7], "byte_order": "little_endian"},
                "stream_u64_pre_oddify": {"offset_bytes": [8, 15], "byte_order": "little_endian"},
                "stream_u64_post_process": "bitwise_or_1",
            },
        )
        self.assertEqual(
            resolution["counter_exhaustion"],
            {
                "when": "draw_count_u64 == uint64_max before draw",
                "behavior": "fail_closed_without_state_stream_or_counter_change",
            },
        )
        self.assertEqual(
            resolution["bounded_draw"],
            {
                "bound_must_be_positive": True,
                "algorithm": "rejection_sampling_without_modulo_bias",
                "threshold_formula": "(2^32 - bound) mod bound",
                "rejected_raw_draws_increment_counter": True,
                "invalid_bound_consumes_rng": False,
            },
        )
        self.assertEqual(
            resolution["event_selector_keying"]["key_parts"],
            ["seed", "tick", "system", "template", "slot"],
        )
        self.assertEqual(resolution["event_selector_keying"]["encoding"], "utf-8")
        self.assertEqual(resolution["event_selector_keying"]["derivation"], "sha-256")
        self.assertEqual(
            resolution["event_selector_keying"]["framing"]["field_order"],
            [
                "domain_tag_ascii",
                "separator_byte",
                "seed_i64_le",
                "tick_u64_le",
                "system_len_u32_le",
                "system_utf8",
                "template_len_u32_le",
                "template_utf8",
                "slot_u64_le",
            ],
        )
        self.assertEqual(
            resolution["event_selector_keying"]["digest_extraction"],
            {
                "keyed_state_u64": {"offset_bytes": [0, 7], "byte_order": "little_endian"},
                "keyed_stream_u64_pre_oddify": {"offset_bytes": [8, 15], "byte_order": "little_endian"},
                "keyed_stream_u64_post_process": "bitwise_or_1",
            },
        )
        self.assertFalse(resolution["event_selector_keying"]["global_state_consumption"])

    def test_mvp_006_007_008_009_010_are_exact(self) -> None:
        decisions = read_json_document()["decisions"]
        self.assertEqual(decisions[5]["resolution"]["duration_weeks"], 26)
        self.assertEqual(decisions[5]["resolution"]["target_session_minutes_min"], 30)
        self.assertEqual(decisions[5]["resolution"]["target_session_minutes_max"], 60)
        self.assertEqual(decisions[6]["resolution"]["scenario_id"], "scenario_constitutional_reform_mvp")
        self.assertEqual(decisions[6]["resolution"]["start_date"], "2030-03-11")
        self.assertEqual(decisions[7]["resolution"]["victory_condition"]["deadline_week_inclusive"], 26)
        self.assertEqual(decisions[8]["resolution"]["initial_cadre_count"], 6)
        self.assertEqual(
            decisions[8]["resolution"]["roles"],
            ["LEGISLATIVE_WHIP", "TERRITORIAL_ORGANIZER", "SPOKESPERSON"],
        )
        self.assertEqual(decisions[9]["resolution"]["visible_targets"]["max_count"], 10)
        self.assertEqual(decisions[9]["resolution"]["causes_per_target"]["max_count"], 3)
        self.assertEqual(decisions[9]["resolution"]["other_deltaS"], "exact_remainder")

    def test_mvp_011_movement_contract_matches_content_pack(self) -> None:
        decision = read_json_document()["decisions"][10]
        resolution = decision["resolution"]
        movements = read_movements_document()["movements"]
        movement_ids = [movement["id"] for movement in movements]
        movement_tags = {movement["id"]: movement["tags"] for movement in movements}

        self.assertEqual(
            movement_ids,
            [
                "mov_seguridad_mano_dura",
                "mov_trabajo_huelgas",
                "mov_salud_crisis_atencion",
                "mov_educacion_paros",
                "mov_pensiones_presion_reforma",
                "mov_institucional_reforma",
                "mov_constitucional_proceso",
                "mov_descentralizacion_regionalista",
                "mov_pueblos_originarios_autonomia",
            ],
        )
        self.assertEqual(
            [item["movement_id"] for item in resolution["active_movements"]],
            ACTIVE_MOVEMENT_IDS,
        )
        self.assertEqual(
            [item["movement_id"] for item in resolution["disabled_movements"]],
            DISABLED_MOVEMENT_IDS,
        )
        self.assertEqual(
            [item["theme"] for item in resolution["active_movements"]],
            [
                "trabajo/costo de vida",
                "seguridad/orden",
                "servicios públicos",
                "regional",
            ],
        )
        self.assertEqual(
            {
                item["theme"]: item["movement_id"]
                for item in resolution["active_movements"]
            },
            {
                "trabajo/costo de vida": "mov_trabajo_huelgas",
                "seguridad/orden": "mov_seguridad_mano_dura",
                "servicios públicos": "mov_salud_crisis_atencion",
                "regional": "mov_descentralizacion_regionalista",
            },
        )
        self.assertEqual(movement_tags["mov_trabajo_huelgas"], ["theme.trabajo"])
        self.assertEqual(movement_tags["mov_seguridad_mano_dura"], ["theme.seguridad"])
        self.assertEqual(movement_tags["mov_salud_crisis_atencion"], ["theme.salud"])
        self.assertEqual(
            movement_tags["mov_descentralizacion_regionalista"],
            ["theme.descentralizacion"],
        )
        self.assertEqual(len(ACTIVE_MOVEMENT_IDS), 4)
        self.assertEqual(len(DISABLED_MOVEMENT_IDS), 5)
        self.assertEqual(set(ACTIVE_MOVEMENT_IDS) | set(DISABLED_MOVEMENT_IDS), set(movement_ids))
        self.assertEqual(set(ACTIVE_MOVEMENT_IDS) & set(DISABLED_MOVEMENT_IDS), set())

        for active in resolution["active_movements"]:
            self.assertTrue(active["enabled"])
            self.assertEqual(active["initial_intensityS"], 3000)
            self.assertEqual(active["initial_direction"], 1)
            self.assertEqual(active["last_addressed_tick"], 0)

        for disabled in resolution["disabled_movements"]:
            self.assertFalse(disabled["enabled"])
            self.assertEqual(disabled["initial_intensityS"], 0)
            self.assertTrue(disabled["excluded_from_update"])
            self.assertTrue(disabled["excluded_from_event_selection"])
            self.assertTrue(disabled["excluded_from_crisis_selection"])

        self.assertEqual(resolution["direction_scope"], "scenario_state")
        self.assertEqual(resolution["direction_semantics"], "favorable_to_own_demand")
        self.assertEqual(
            resolution["environmental_representation"],
            {
                "interest_group_id": "ig_ambiental_regionalista",
                "movement_id": None,
                "representation_level": "interest_group_only",
            },
        )
        self.assertNotIn("ig_ambiental_regionalista", movement_ids)
        self.assertFalse(any("ambiental" in movement_id for movement_id in movement_ids))
        self.assertFalse(resolution["disabled_movements_activate_dynamically"])

    def test_mvp_012_national_aggregation_is_exact(self) -> None:
        decision = read_json_document()["decisions"][11]
        self.assertEqual(decision["id"], "MVP-012-national-aggregation")
        self.assertEqual(decision["status"], "approved")
        resolution = decision["resolution"]
        self.assertEqual(resolution["numeric_domain"]["rounding"], "HALF_AWAY_FROM_ZERO")
        self.assertEqual(resolution["numeric_domain"]["midS"], 5000)
        self.assertEqual(resolution["numeric_domain"]["minS"], 0)
        self.assertEqual(resolution["numeric_domain"]["maxS"], 10000)
        self.assertEqual(resolution["numeric_domain"]["ppm_denominator"], 1000000)
        self.assertEqual(resolution["phase_dispatch"]["internal_reversion_phase"], 6)
        self.assertEqual(resolution["phase_dispatch"]["derived_internals_phase"], 7)
        self.assertEqual(resolution["phase_dispatch"]["aggregate_national_metrics_phase"], 8)
        self.assertEqual(resolution["causal_algorithm"]["name"], "ordered_prefix_counterfactual_marginal_v1")
        self.assertIn("SYSTEM:AGG.metrics.economy", resolution["cause_key_grammar"]["examples"])
        self.assertNotIn("SYSTEM:AGG:metrics.economy", resolution["cause_key_grammar"]["examples"])
        self.assertTrue(resolution["hidden_internal_policy"]["internals_remain_hidden"])
        self.assertTrue(resolution["hidden_internal_policy"]["no_double_counting"])

    def test_mvp_012_pipeline_order_is_exact(self) -> None:
        resolution = read_json_document()["decisions"][11]["resolution"]
        self.assertEqual(
            resolution["metric_aggregation_formula"]["pipeline_order"],
            [
                "sum_weighted_offsets",
                "single_rounding_for_weighted_offset",
                "target_clamp",
                "compute_distance_to_target",
                "elasticity_rounding",
                "weekly_cap",
                "add_to_current_metric",
                "final_clamp",
            ],
        )

    def test_mvp_012_reversion_order_is_exact(self) -> None:
        resolution = read_json_document()["decisions"][11]["resolution"]
        self.assertEqual(
            resolution["reversion_formula"]["order"],
            [
                "subtract_current_from_midpoint",
                "multiply_by_alpha_ppm",
                "single_division_rounding",
                "add_to_current",
                "final_clamp",
            ],
        )
        self.assertEqual(
            resolution["reversion_formula"]["skip_targets"],
            [
                "internals.legitimacy.performance",
                "internals.legitimacy.social_tension_load",
            ],
        )

    def test_mvp_012_causal_vectors_are_exact(self) -> None:
        resolution = read_json_document()["decisions"][11]["resolution"]
        eco = resolution["causal_vectors"]["economy_prefix_deltas"]
        self.assertEqual(eco["F(V0)"], 5000)
        self.assertEqual(eco["F(V1)"], 5029)
        self.assertEqual(eco["F(V2)"], 5050)
        self.assertEqual(eco["F(V3)"], 5050)
        self.assertEqual(eco["F(V4)"], 5062)
        self.assertEqual(eco["base_deltaS"], 0)
        self.assertEqual(eco["sum_component_deltas"], 62)

        st = resolution["causal_vectors"]["social_tension_prefix_deltas"]
        self.assertEqual(st["F(V0)"], 5000)
        self.assertEqual(st["F(V1)"], 5056)
        self.assertEqual(st["F(V2)"], 5095)
        self.assertEqual(st["F(V3)"], 5056)
        self.assertEqual(st["F(V4)"], 5032)
        self.assertEqual(st["base_deltaS"], 0)
        self.assertEqual(st["sum_component_deltas"], 32)

    def test_mvp_012_economy_vectors_are_exact(self) -> None:
        resolution = read_json_document()["decisions"][11]["resolution"]
        eco = resolution["vectors"]["economy"]
        self.assertEqual(eco["current_metricS"], 5000)
        self.assertEqual(eco["weighted_offsetS"], 750)
        self.assertEqual(eco["targetS"], 5750)
        self.assertEqual(eco["elastic_deltaS"], 62)
        self.assertEqual(eco["capped_deltaS"], 62)
        self.assertEqual(eco["finalS"], 5062)
        self.assertEqual(eco["delta_totalS"], 62)

    def test_mvp_012_social_tension_vectors_are_exact(self) -> None:
        resolution = read_json_document()["decisions"][11]["resolution"]
        st = resolution["vectors"]["social_tension"]
        self.assertEqual(st["current_metricS"], 5000)
        self.assertEqual(st["weighted_offsetS"], 200)
        self.assertEqual(st["targetS"], 5200)
        self.assertEqual(st["elastic_deltaS"], 32)
        self.assertEqual(st["capped_deltaS"], 32)
        self.assertEqual(st["finalS"], 5032)
        self.assertEqual(st["delta_totalS"], 32)

    def test_mvp_012_reversion_vector_is_exact(self) -> None:
        resolution = read_json_document()["decisions"][11]["resolution"]
        rev = resolution["vectors"]["reversion_6000_to_5974"]
        self.assertEqual(rev["currentS"], 6000)
        self.assertEqual(rev["midS"], 5000)
        self.assertEqual(rev["alpha_ppm"], 26307)
        self.assertEqual(rev["distanceS"], -1000)
        self.assertEqual(rev["rounded_deltaS"], -26)
        self.assertEqual(rev["finalS"], 5974)

    def test_mvp_012_cap_weekly_vector_is_exact(self) -> None:
        resolution = read_json_document()["decisions"][11]["resolution"]
        cap = resolution["vectors"]["cap_weekly"]
        self.assertEqual(cap["currentS"], 5000)
        self.assertEqual(cap["targetS"], 10000)
        self.assertEqual(cap["alpha_ppm"], 292893)
        self.assertEqual(cap["cap_per_weekS"], 600)
        self.assertEqual(cap["elastic_deltaS"], 1464)
        self.assertEqual(cap["capped_deltaS"], 600)
        self.assertEqual(cap["finalS"], 5600)

    def test_mvp_012_rounding_vectors_are_exact(self) -> None:
        rounding_vectors = read_json_document()["decisions"][11]["resolution"]["vectors"]["rounding_half_away_from_zero"]
        self.assertEqual(len(rounding_vectors), 2)
        self.assertEqual(rounding_vectors[0]["numerator"], 500000)
        self.assertEqual(rounding_vectors[0]["result"], 1)
        self.assertEqual(rounding_vectors[1]["numerator"], -500000)
        self.assertEqual(rounding_vectors[1]["result"], -1)

    def test_mvp_012_forbidden_formats_are_excluded(self) -> None:
        resolution = read_json_document()["decisions"][11]["resolution"]
        forbidden = resolution["cause_key_grammar"]["forbidden_ambiguous_forms"]
        self.assertIn("SYSTEM:AGG:metrics.economy", forbidden)
        self.assertIn("SYSTEM:AGG:metrics.economy:internals.economy.growth", forbidden)

    def test_markdown_contains_authoritative_contract_and_json_parity(self) -> None:
        markdown = read_markdown_text()
        self.assertIn("human-readable authoritative MVP contract freeze", markdown)
        self.assertIn("machine-readable canonical representation", markdown)
        self.assertIn("PR 8 is documentation, contract, and test only.", markdown)
        self.assertIn("PR 9 aligns the aggregation contract and the effects loader", markdown)
        self.assertIn("PR 11 implements the causal ledger", markdown)
        self.assertIn("PR 12 implements effect execution", markdown)
        self.assertIn("PR 13 implements the canonical tick, RNG contract, and scheduler ordering", markdown)
        self.assertIn("PR 16 implements political state, scenario state, and cadre runtime/save boundaries", markdown)
        self.assertIn("PR 17 implements movement state, escalation, and crisis eligibility rules", markdown)
        self.assertIn("PR 20 implements the TurnReport limits, tie-breaks, alerts, and exact remainder accounting", markdown)
        self.assertIn("mov_constitucional_proceso", markdown)
        self.assertIn("Route B", markdown)

        match = CANONICAL_JSON_BLOCK_RE.search(markdown)
        self.assertIsNotNone(match)
        embedded = json.loads(match.group(1))
        self.assertEqual(embedded, EXPECTED_DOCUMENT)
        self.assertEqual(embedded, read_json_document())

    def test_markdown_summary_and_section_order_are_exact(self) -> None:
        markdown = read_markdown_text()
        summary_rows = []
        in_summary = False
        for line in markdown.splitlines():
            if line == "## Summary":
                in_summary = True
                continue
            if in_summary and line.startswith("## "):
                break
            if in_summary and line.startswith("| MVP-"):
                cells = [cell.strip() for cell in line.strip().strip("|").split("|")]
                summary_rows.append(cells)

        self.assertEqual(
            summary_rows,
            [[decision["id"], decision["topic"], "approved"] for decision in EXPECTED_DOCUMENT["decisions"]],
        )
        self.assertEqual(
            SECTION_HEADING_RE.findall(markdown),
            [decision["id"] for decision in EXPECTED_DOCUMENT["decisions"]],
        )

    def test_json_and_markdown_do_not_contain_environmental_data_or_pending_terms(self) -> None:
        json_text = read_json_bytes().decode("utf-8")
        markdown_text = read_markdown_text()
        lowercase_texts = (json_text.lower(), markdown_text.lower())

        for text in (json_text, markdown_text):
            self.assertIsNone(WINDOWS_ABSOLUTE_PATH_RE.search(text))
            self.assertIsNone(POSIX_ABSOLUTE_PATH_RE.search(text))
            self.assertIsNone(TIMESTAMP_RE.search(text))
            for snippet in FORBIDDEN_TEXT_SNIPPETS:
                self.assertNotIn(snippet, text)

        for text in lowercase_texts:
            for snippet in FORBIDDEN_AMBIGUOUS_PHRASES:
                self.assertNotIn(snippet, text)


if __name__ == "__main__":
    unittest.main()
