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
    "podrÃ­a",
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
                    'ordered_categories': [
                        'DECISION',
                        'EVENT',
                        'REFORM',
                        'MOVEMENT',
                        'MODIFIER',
                        'SYSTEM'
                    ],
                    'modifier_parent_categories': [
                        'DECISION',
                        'EVENT',
                        'REFORM',
                        'MOVEMENT'
                    ],
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
                        'winner_order': [
                            'priority_desc',
                            'effect_instance_id_ordinal_asc'
                        ],
                        'apply_winning_set': True,
                        'ignore_add_and_mul_for_same_target': True
                    },
                    'phase_order_without_set': [
                        'ADD',
                        'MUL'
                    ],
                    'add_order': [
                        'priority_desc',
                        'effect_instance_id_ordinal_asc'
                    ],
                    'mul_order': [
                        'priority_desc',
                        'effect_instance_id_ordinal_asc'
                    ],
                    'mul_rounding': 'HALF_AWAY_FROM_ZERO',
                    'post_processing': [
                        'final_clamp',
                        'final_normalization',
                        'system_residue'
                    ],
                    'forbid': [
                        'load_order',
                        'dictionary_order',
                        'grouped_mul_product',
                        'floats'
                    ]
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
                    'forbid': [
                        'System.Random',
                        'GetHashCode',
                        'implicit_global_rng'
                    ],
                    'consumption_rule': 'sequential_only_in_closed_order_systems',
                    'sequential_seed_initialization': {
                        'seed_type': 'int64_signed',
                        'seed_encoding': 'twos_complement_little_endian',
                        'preimage': {
                            'domain_tag': 'VictoriantChile/pcg32-v1/init',
                            'separator_byte_hex': '00',
                            'field_order': [
                                'domain_tag_ascii',
                                'separator_byte',
                                'seed_i64_le'
                            ]
                        },
                        'derivation': 'sha-256',
                        'digest_extraction': {
                            'state_u64': {
                                'offset_bytes': [
                                    0,
                                    7
                                ],
                                'byte_order': 'little_endian'
                            },
                            'stream_u64_pre_oddify': {
                                'offset_bytes': [
                                    8,
                                    15
                                ],
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
                        'post_success': [
                            'state_u64 = new_state',
                            'draw_count_u64 += 1'
                        ]
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
                        'key_parts': [
                            'seed',
                            'tick',
                            'system',
                            'template',
                            'slot'
                        ],
                        'field_types': {
                            'seed': 'int64_signed',
                            'tick': 'uint64',
                            'system': 'ascii_identifier',
                            'template': 'ascii_identifier',
                            'slot': 'uint64'
                        },
                        'string_validation': {
                            'pattern': '[a-z0-9][a-z0-9._-]*',
                            'forbid': [
                                'empty',
                                'whitespace',
                                'control_chars',
                                'newlines',
                                'unicode',
                                'nul_bytes'
                            ]
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
                                'offset_bytes': [
                                    0,
                                    7
                                ],
                                'byte_order': 'little_endian'
                            },
                            'keyed_stream_u64_pre_oddify': {
                                'offset_bytes': [
                                    8,
                                    15
                                ],
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
                    'early_end_conditions': [
                        'victory',
                        'defeat'
                    ]
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
                    'out_of_slice': [
                        'presidential_election',
                        'opposition_neutralization',
                        'full_constituent_route_b',
                        'four_year_campaign'
                    ]
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
                    'roles': [
                        'LEGISLATIVE_WHIP',
                        'TERRITORIAL_ORGANIZER',
                        'SPOKESPERSON'
                    ],
                    'cadres_per_role': 2,
                    'cadre_def_fields': [
                        'id',
                        'localization_key',
                        'role',
                        'tags'
                    ],
                    'cadre_state_fields': [
                        'competenceS',
                        'loyaltyS',
                        'ambitionS',
                        'networksS',
                        'scandal_riskS',
                        'assignment_id',
                        'available'
                    ],
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
                        'order': [
                            'abs(delta_totalS)_desc',
                            'target_path_ordinal_asc'
                        ]
                    },
                    'causes_per_target': {
                        'max_count': 3,
                        'order': [
                            'abs(delta_totalS)_desc',
                            'CauseKey_ordinal_asc'
                        ]
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
                        'forbidden': [
                            'float',
                            'double',
                            'decimal',
                            'dictionary_order',
                            'componentwise_rounding',
                            'culture_dependent_rounding',
                            'silent_overflow'
                        ]
                    },
                    'implementation_status': {
                        'pr14_1_contract': 'closed',
                        'pr14_2_runtime_plan_bridge': 'implemented',
                        'pr14_phases_6_7_8': 'implemented',
                        'phases_9_14': 'explicit_no_op_hooks'
                    },
                    'execution_vectors': {
                        'version': 'aggregation_execution_v1',
                        'path': 'tests/aggregation/aggregation_execution_v1_vectors.json',
                        'oracle': 'tests/python/test_aggregation_execution_contract.py'
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
                        'fail_closed_triggers': [
                            'missing_target',
                            'duplicate_target',
                            'overlapping_reversion_group',
                            'arithmetic_overflow',
                            'out_of_range_conversion',
                            'invalid_cause_prefix',
                            'causal_accounting_mismatch',
                            'ledger_rejection'
                        ],
                        'fail_closed_guarantees': [
                            'zero_partial_state',
                            'zero_partial_internals',
                            'zero_partial_metrics',
                            'zero_partial_causal_contributions'
                        ]
                    },
                    'reversion_formula': {
                        'distanceS': 'midS - currentS',
                        'reversion_deltaS': 'RoundHalfAwayFromZero(distanceS * alpha_ppm / 1000000)',
                        'pre_clampS': 'currentS + reversion_deltaS',
                        'finalS': 'clamp(pre_clampS, TargetConfig.minS, TargetConfig.maxS)',
                        'order': [
                            'subtract_current_from_midpoint',
                            'multiply_by_alpha_ppm',
                            'single_division_rounding',
                            'add_to_current',
                            'final_clamp'
                        ],
                        'arithmetic': 'long_checked_before_int_cast',
                        'no_extra_weekly_cap': True,
                        'skip_targets': [
                            'internals.legitimacy.performance',
                            'internals.legitimacy.social_tension_load'
                        ]
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
                        'pipeline_order': [
                            'sum_weighted_offsets',
                            'single_rounding_for_weighted_offset',
                            'target_clamp',
                            'compute_distance_to_target',
                            'elasticity_rounding',
                            'weekly_cap',
                            'add_to_current_metric',
                            'final_clamp'
                        ],
                        'forbidden': [
                            'swap_cap_and_rounding',
                            'apply_cap_to_weighted_target_instead_of_delta'
                        ]
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
                        'forbidden_methods': [
                            'proportional_split',
                            'largest_remainder',
                            'shapley',
                            'dictionary_order',
                            'invented_residual_to_balance'
                        ],
                        'no_additional_system_causes': 'SYSTEM:ROUNDING and SYSTEM:CLAMP reserved for other systems; do not emit within aggregation marginal attribution'
                    },
                    'cause_key_grammar': {
                        'format': "CATEGORY + ':' + ID",
                        'id_forbidden_chars': [
                            ':',
                            '|',
                            'whitespace',
                            'control_chars',
                            'non_ascii_unicode'
                        ],
                        'id_separator': '.',
                        'permitted_prefixes_for_pr14': [
                            'AGG.',
                            'REVERSION.',
                            'DERIVED.'
                        ],
                        'examples': [
                            'SYSTEM:AGG.metrics.economy',
                            'SYSTEM:AGG.metrics.economy.internals.economy.growth',
                            'SYSTEM:REVERSION.internals.economy.growth',
                            'SYSTEM:DERIVED.internals.legitimacy.performance'
                        ],
                        'forbidden_ambiguous_forms': [
                            'SYSTEM:AGG:metrics.economy',
                            'SYSTEM:AGG:metrics.economy:internals.economy.growth'
                        ]
                    },
                    'cause_prefix_materialization': {
                        'source_format': 'CATEGORY:BASE_ID',
                        'separator_count': 1,
                        'required_category': 'SYSTEM',
                        'allowed_base_ids': [
                            'AGG',
                            'REVERSION',
                            'DERIVED'
                        ],
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
                        'must_fail_prefixes': [
                            'AGG',
                            'SYSTEM:AGG:EXTRA',
                            'EVENT:AGG',
                            'SYSTEM:',
                            'SYSTEM:UNKNOWN',
                            'system:AGG',
                            'SYSTEM:AGG.'
                        ]
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
            },
            {
                'id': 'MVP-013-territory-feedback',
                'topic': 'Territory feedback contract',
                'question': 'What exact region order, dynamic targets, static resources, numeric domain, drift formulas, pull mechanics, latency, causality grammar, hidden provenance, and atomicity rules define territory feedback?',
                'status': 'approved',
                'resolution': {
                    'canonical_region_order': {
                        'authority': 'content_pack_declaration_order',
                        'source_path': 'Assets/StreamingAssets/content/core/regions.json',
                        'region_count': 16,
                        'ordered_region_ids': [
                            'arica_parinacota',
                            'tarapaca',
                            'antofagasta',
                            'atacama',
                            'coquimbo',
                            'valparaiso',
                            'metropolitana',
                            'ohiggins',
                            'maule',
                            'nuble',
                            'biobio',
                            'araucania',
                            'los_rios',
                            'los_lagos',
                            'aysen',
                            'magallanes'
                        ],
                        'weight_ppm_each': 62500,
                        'weight_ppm_sum_required': 1000000,
                        'forbidden_order_sources': [
                            'GameState.Regions',
                            'RegionsById.Values',
                            'dictionary_iteration',
                            'lexicographic_sort'
                        ]
                    },
                    'regional_dynamic_targets': [
                        'support',
                        'tension',
                        'organization',
                        'rival_presence'
                    ],
                    'static_regional_resources': {
                        'admin_capS': 5000,
                        'industry_capS': 5000,
                        'extractive_capS': 5000,
                        'social_capS': 5000,
                        'populationS': 5000
                    },
                    'numeric_domain': {
                        'scale': 100,
                        'hundredS': 10000,
                        'midS': 5000,
                        'ppm_denominator': 1000000,
                        'stored_type': 'int',
                        'intermediate_type': 'checked_long',
                        'rounding': 'HALF_AWAY_FROM_ZERO',
                        'rounding_authority': 'FixedMath.RoundDivide',
                        'target_clamp_authority': 'TargetConfig',
                        'publication_operation': 'SET',
                        'forbidden_numeric_types': [
                            'float',
                            'double',
                            'decimal'
                        ],
                        'forbidden_behaviors': [
                            'Math.Round',
                            'divide_before_weighted_sum_complete',
                            'round_per_component',
                            'silent_saturation',
                            'unchecked_overflow',
                            'unchecked_cast',
                            'hardcoded_target_clamp'
                        ]
                    },
                    'drift': {
                        'phase': 9,
                        'phase_name': 'DriftNationalToRegions',
                        'snapshot': 'post_phase_8',
                        'region_order_source': 'canonical_region_order.ordered_region_ids',
                        'metric_order': [
                            'support',
                            'tension',
                            'organization',
                            'rival_presence'
                        ],
                        'region_count': 16,
                        'outputs_per_region': 4,
                        'output_count': 64,
                        'half_life_weeks_metadata': 6,
                        'alpha_ppm': 109101,
                        'cap_per_weekS': 200,
                        'target_baseS': 5000,
                        'target_denominator': 1000000,
                        'all_sources_read_from': 'phase_input_snapshot',
                        'rival_support_read_from': 'phase_input_snapshot_pre_drift',
                        'target_formulas': {
                            'support': {
                                'target': 'regions.{region_id}.support',
                                'terms': [
                                    {
                                        'source': 'metrics.legitimacy',
                                        'transform': 'value_minus_mid',
                                        'coefficient_ppm': 600000
                                    },
                                    {
                                        'source': 'metrics.party_organization',
                                        'transform': 'value_minus_mid',
                                        'coefficient_ppm': 300000
                                    },
                                    {
                                        'source': 'metrics.social_tension',
                                        'transform': 'value_minus_mid',
                                        'coefficient_ppm': -400000
                                    }
                                ]
                            },
                            'tension': {
                                'target': 'regions.{region_id}.tension',
                                'terms': [
                                    {
                                        'source': 'metrics.economy',
                                        'transform': 'mid_minus_value',
                                        'coefficient_ppm': 500000
                                    },
                                    {
                                        'source': 'metrics.security',
                                        'transform': 'mid_minus_value',
                                        'coefficient_ppm': 400000
                                    },
                                    {
                                        'source': 'metrics.public_agenda',
                                        'transform': 'value_minus_mid',
                                        'coefficient_ppm': 300000
                                    }
                                ]
                            },
                            'organization': {
                                'target': 'regions.{region_id}.organization',
                                'terms': [
                                    {
                                        'source': 'metrics.party_organization',
                                        'transform': 'value_minus_mid',
                                        'coefficient_ppm': 800000
                                    }
                                ]
                            },
                            'rival_presence': {
                                'target': 'regions.{region_id}.rival_presence',
                                'terms': [
                                    {
                                        'source': 'regions.{region_id}.support',
                                        'transform': 'mid_minus_value',
                                        'coefficient_ppm': 700000
                                    },
                                    {
                                        'source': 'metrics.internal_cohesion',
                                        'transform': 'mid_minus_value',
                                        'coefficient_ppm': 200000
                                    }
                                ]
                            }
                        },
                        'common_pipeline': [
                            'construct_target_numerator_in_checked_long',
                            'round_target_offset_once',
                            'add_mid',
                            'target_config_clamp_target',
                            'distance_target_minus_current',
                            'multiply_distance_by_alpha_in_checked_long',
                            'round_elastic_delta_once',
                            'clamp_delta_to_weekly_cap',
                            'add_delta_to_current',
                            'target_config_clamp_final',
                            'realized_delta_final_minus_current'
                        ]
                    },
                    'pull': {
                        'phase': 10,
                        'phase_name': 'PullRegionsToInternals',
                        'snapshot': 'post_phase_9',
                        'region_order_source': 'canonical_region_order.ordered_region_ids',
                        'weight_source': 'content_pack_region.weight_ppm',
                        'weight_sum_required': 1000000,
                        'weighted_average_denominator': 1000000,
                        'weighted_average_intermediate_type': 'checked_long',
                        'weighted_average_rounding': 'HALF_AWAY_FROM_ZERO',
                        'weighted_average_division_count': 1,
                        'all_sources_read_from': 'phase_input_snapshot',
                        'binding_chaining': 'forbidden',
                        'alpha_ppm': 206299,
                        'cap_per_weekS': 400,
                        'output_count': 5,
                        'binding_order': [
                            'support_to_coalition_strength',
                            'organization_to_field_ops',
                            'tension_to_protest_activity',
                            'rival_presence_to_opposition_obstruction',
                            'tension_to_movement_salience'
                        ],
                        'bindings': [
                            {
                                'id': 'support_to_coalition_strength',
                                'regional_source': 'support',
                                'destination': 'internals.leg.coalition_strength'
                            },
                            {
                                'id': 'organization_to_field_ops',
                                'regional_source': 'organization',
                                'destination': 'internals.party.field_ops'
                            },
                            {
                                'id': 'tension_to_protest_activity',
                                'regional_source': 'tension',
                                'destination': 'internals.tension.protest_activity'
                            },
                            {
                                'id': 'rival_presence_to_opposition_obstruction',
                                'regional_source': 'rival_presence',
                                'destination': 'internals.leg.opposition_obstruction'
                            },
                            {
                                'id': 'tension_to_movement_salience',
                                'regional_source': 'tension',
                                'destination': 'internals.agenda.movement_salience'
                            }
                        ],
                        'common_pipeline': [
                            'construct_complete_weighted_sum_in_checked_long',
                            'round_weighted_average_once',
                            'target_config_clamp_target',
                            'distance_target_minus_current',
                            'multiply_distance_by_alpha_in_checked_long',
                            'round_elastic_delta_once',
                            'clamp_delta_to_weekly_cap',
                            'add_delta_to_current',
                            'target_config_clamp_final',
                            'realized_delta_final_minus_current'
                        ]
                    },
                    'phase_order': {
                        'aggregate_national_metrics': 8,
                        'drift_national_to_regions': 9,
                        'pull_regions_to_internals': 10,
                        'close_causal_report': 15,
                        'detect_and_publish_blocking_decision': 16
                    },
                    'snapshot_semantics': {
                        'phase_9_snapshot': 'post_phase_8_immutable',
                        'phase_9_all_outputs_share_snapshot': True,
                        'phase_9_rival_support_source': 'phase_input_snapshot_pre_drift',
                        'phase_10_snapshot': 'post_phase_9_immutable',
                        'phase_10_all_bindings_share_snapshot': True,
                        'phase_10_binding_chaining': False
                    },
                    'latency': {
                        'feedback_latency_ticks': 1,
                        'phase_8_observes': 'internals_before_current_tick_pull',
                        'phase_9_writes': 'regional_dynamic_targets',
                        'phase_10_writes': 'five_internal_targets',
                        'same_tick_phase_8_reexecution': False,
                        'next_tick_observation_order': [
                            'RevertInternals',
                            'DeriveInternals',
                            'AggregateNationalMetrics'
                        ],
                        'regional_feedback_first_visible_in_metrics': 'tick_plus_1_phase_8'
                    },
                    'cause_key_grammar': {
                        'cause_category': 'SYSTEM',
                        'parent': None,
                        'canonical_key_separator': ':',
                        'canonical_key_separator_count': 1,
                        'identifier_separator': '.',
                        'identifier_policy': 'PRINTABLE_ASCII_NO_WHITESPACE_COLON_PIPE',
                        'drift': {
                            'id_prefix': 'REG_DRIFT',
                            'canonical_key_pattern': 'SYSTEM:REG_DRIFT.regions.{region_id}.{metric}',
                            'target_pattern': 'regions.{region_id}.{metric}',
                            'target_visibility': 'public_visible_target',
                            'public_ledger': True,
                            'tick_causal_buffer': True,
                            'potential_cause_count': 64,
                            'cause_order': 'canonical_region_order_then_drift_metric_order',
                            'zero_realized_delta_policy': 'omit_contribution'
                        }
                    },
                    'hidden_pull_provenance': {
                        'pull_provenance': {
                            'id_prefix': 'REG_TO_INT',
                            'canonical_key_pattern': 'SYSTEM:REG_TO_INT.{internal_target}',
                            'identity_count': 5,
                            'provenance_scope': 'ephemeral_execution_plan_only',
                            'target_visibility': 'hidden_internal',
                            'public_ledger': False,
                            'tick_causal_buffer': False,
                            'serialized': False,
                            'stored_in_game_state': False,
                            'turn_report': False,
                            'top_n_slot': False,
                            'lifetime': 'current_phase_10_plan_only',
                            'public_attribution': 'next_tick_SYSTEM_AGG',
                            'double_counting': 'forbidden',
                            'identities': [
                                {
                                    'binding_id': 'support_to_coalition_strength',
                                    'canonical_key': 'SYSTEM:REG_TO_INT.internals.leg.coalition_strength',
                                    'internal_target': 'internals.leg.coalition_strength'
                                },
                                {
                                    'binding_id': 'organization_to_field_ops',
                                    'canonical_key': 'SYSTEM:REG_TO_INT.internals.party.field_ops',
                                    'internal_target': 'internals.party.field_ops'
                                },
                                {
                                    'binding_id': 'tension_to_protest_activity',
                                    'canonical_key': 'SYSTEM:REG_TO_INT.internals.tension.protest_activity',
                                    'internal_target': 'internals.tension.protest_activity'
                                },
                                {
                                    'binding_id': 'rival_presence_to_opposition_obstruction',
                                    'canonical_key': 'SYSTEM:REG_TO_INT.internals.leg.opposition_obstruction',
                                    'internal_target': 'internals.leg.opposition_obstruction'
                                },
                                {
                                    'binding_id': 'tension_to_movement_salience',
                                    'canonical_key': 'SYSTEM:REG_TO_INT.internals.agenda.movement_salience',
                                    'internal_target': 'internals.agenda.movement_salience'
                                }
                            ]
                        },
                        'public_aggregation_attribution': [
                            {
                                'internal_target': 'internals.leg.coalition_strength',
                                'visible_metric': 'metrics.legislative_capacity',
                                'canonical_key': 'SYSTEM:AGG.metrics.legislative_capacity.internals.leg.coalition_strength'
                            },
                            {
                                'internal_target': 'internals.party.field_ops',
                                'visible_metric': 'metrics.party_organization',
                                'canonical_key': 'SYSTEM:AGG.metrics.party_organization.internals.party.field_ops'
                            },
                            {
                                'internal_target': 'internals.tension.protest_activity',
                                'visible_metric': 'metrics.social_tension',
                                'canonical_key': 'SYSTEM:AGG.metrics.social_tension.internals.tension.protest_activity'
                            },
                            {
                                'internal_target': 'internals.leg.opposition_obstruction',
                                'visible_metric': 'metrics.legislative_capacity',
                                'canonical_key': 'SYSTEM:AGG.metrics.legislative_capacity.internals.leg.opposition_obstruction'
                            },
                            {
                                'internal_target': 'internals.agenda.movement_salience',
                                'visible_metric': 'metrics.public_agenda',
                                'canonical_key': 'SYSTEM:AGG.metrics.public_agenda.internals.agenda.movement_salience'
                            }
                        ]
                    },
                    'pass_execution_semantics': {
                        'scope': {
                            'phase_9_and_phase_10': 'separate_atomic_passes',
                            'pass_atomicity': 'executor_responsibility',
                            'observable_tick_atomicity': 'scheduler_result_boundary',
                            'runtime_tick_fail_closed_verification': 'PR_15_4'
                        },
                        'execution_sequence': [
                            'capture_immutable_snapshot',
                            'validate_all_bindings_and_inputs',
                            'compute_all_outputs',
                            'compute_all_applicable_causes_or_provenance',
                            'validate_deltas_ranges_and_causal_accounting',
                            'construct_complete_candidate',
                            'publish_once'
                        ],
                        'phase_9_drift': {
                            'phase': 9,
                            'snapshot': 'post_phase_8_immutable',
                            'planned_output_count': 64,
                            'candidate': 'complete_regional_state_and_visible_causal_batch',
                            'publication': 'single_atomic_state_and_causal_batch',
                            'partial_publication': False,
                            'cross_output_observation': False,
                            'failure_behavior': 'discard_candidate_and_publish_nothing'
                        },
                        'phase_10_pull': {
                            'phase': 10,
                            'snapshot': 'post_phase_9_immutable',
                            'planned_output_count': 5,
                            'candidate': 'complete_internal_state_with_ephemeral_provenance',
                            'publication': 'single_atomic_internal_state_batch',
                            'public_causal_publication': 'none',
                            'ephemeral_provenance_publication': 'none',
                            'partial_publication': False,
                            'binding_chaining': False,
                            'failure_behavior': 'discard_candidate_and_publish_nothing'
                        },
                        'fail_closed_triggers': [
                            'missing_region',
                            'duplicate_region',
                            'inconsistent_canonical_order',
                            'region_count_mismatch',
                            'weight_sum_mismatch',
                            'non_positive_weight',
                            'missing_regional_field',
                            'missing_internal_destination',
                            'missing_target_config',
                            'set_not_allowed',
                            'invalid_cause_ref',
                            'duplicate_output',
                            'duplicate_coupling',
                            'multiplication_overflow',
                            'sum_overflow',
                            'addition_overflow',
                            'long_to_int_out_of_range',
                            'invalid_denominator',
                            'invalid_clamp_or_invariant',
                            'causal_batch_rejection',
                            'visible_delta_causal_sum_mismatch'
                        ],
                        'fail_closed_guarantees': [
                            'zero_partial_game_state',
                            'zero_partial_regions',
                            'zero_partial_internals',
                            'zero_partial_causal_contributions'
                        ],
                        'observable_tick_failure': {
                            'scenario': 'phase_9_succeeds_phase_10_fails',
                            'advance_one_tick_result': 'not_returned',
                            'original_game_state': 'unchanged',
                            'phase_9_snapshot_exposed': False,
                            'partial_causal_ledger_exposed': False,
                            'sealed_causal_ledger_exposed': False,
                            'scheduler_working_state': 'local_only',
                            'scheduler_causal_buffer': 'local_only_until_phase_15_seal',
                            'runtime_test_owner': 'PR_15_4'
                        },
                        'non_guarantees': [
                            'phases_9_and_10_are_not_one_super_pass',
                            'PR_15_1_does_not_implement_runtime_transactions',
                            'PR_15_1_does_not_activate_scheduler_phases_9_or_10'
                        ]
                    },
                    'active_reform_bias_exclusion': {
                        'included_in_pr_15_x': False,
                        'runtime_hook': False,
                        'placeholder': False,
                        'neutral_branch': False,
                        'cause_key': None,
                        'implementation_owner': 'PR_19_4'
                    },
                    'vectors': {
                        'rounding': [
                            'R-01',
                            'R-02'
                        ],
                        'drift': [
                            'D-00',
                            'D-01',
                            'D-02',
                            'D-03',
                            'D-04',
                            'D-05',
                            'D-06',
                            'D-07',
                            'D-08',
                            'D-08-WRONG',
                            'D-09',
                            'D-10'
                        ],
                        'pull': [
                            'P-00',
                            'P-01',
                            'P-02',
                            'P-03',
                            'P-04',
                            'P-05'
                        ],
                        'latency': [
                            'L-01-T',
                            'L-01-T1-R',
                            'L-01-T1-A',
                            'L-01-CAUSE'
                        ],
                        'ordering': [
                            'O-01',
                            'O-02',
                            'O-03',
                            'O-04',
                            'O-05'
                        ],
                        'fixture_owner': 'PR_15_1_J',
                        'oracle_owner': 'PR_15_1_K'
                    },
                    'scope': [
                        'machine_readable_territory_contract',
                        'human_readable_territory_contract',
                        'execution_vectors',
                        'independent_python_oracle',
                        'contract_parity_and_negative_tests'
                    ],
                    'non_scope': [
                        'runtime_csharp_implementation',
                        'scheduler_phase_activation',
                        'game_state_schema_changes',
                        'content_pack_changes',
                        'persistence_or_migrations',
                        'active_reform_bias_before_PR_19_4',
                        'ui_or_turn_report_changes'
                    ]
                },
                'rationale': 'The territory feedback contract now freezes the complete regional authority, drift, pull, latency, causality, and atomicity rules from the canonical territory_contract.md. The cause_key_grammar and hidden_pull_provenance are structural copies of territory_contract.causality without reinterpretation. pass_execution_semantics is territory.atomicity. active_reform_bias is explicitly excluded and owned by PR 19.4. Execution vectors are assigned to PR 15.1-J (fixture) and PR 15.1-K (oracle).'
            }
        ]
    }


EXPECTED_MVP_013 = {
    'id': 'MVP-013-territory-feedback',
    'topic': 'Territory feedback contract',
    'question': 'What exact region order, dynamic targets, static resources, numeric domain, drift formulas, pull mechanics, latency, causality grammar, hidden provenance, and atomicity rules define territory feedback?',
    'status': 'approved',
    'resolution': {
        'canonical_region_order': {
            'authority': 'content_pack_declaration_order',
            'source_path': 'Assets/StreamingAssets/content/core/regions.json',
            'region_count': 16,
            'ordered_region_ids': [
                'arica_parinacota',
                'tarapaca',
                'antofagasta',
                'atacama',
                'coquimbo',
                'valparaiso',
                'metropolitana',
                'ohiggins',
                'maule',
                'nuble',
                'biobio',
                'araucania',
                'los_rios',
                'los_lagos',
                'aysen',
                'magallanes',
            ],
            'weight_ppm_each': 62500,
            'weight_ppm_sum_required': 1000000,
            'forbidden_order_sources': ['GameState.Regions', 'RegionsById.Values', 'dictionary_iteration', 'lexicographic_sort']
        },
        'regional_dynamic_targets': ['support', 'tension', 'organization', 'rival_presence'],
        'static_regional_resources': {
            'admin_capS': 5000,
            'industry_capS': 5000,
            'extractive_capS': 5000,
            'social_capS': 5000,
            'populationS': 5000
        },
        'numeric_domain': {
            'scale': 100,
            'hundredS': 10000,
            'midS': 5000,
            'ppm_denominator': 1000000,
            'stored_type': 'int',
            'intermediate_type': 'checked_long',
            'rounding': 'HALF_AWAY_FROM_ZERO',
            'rounding_authority': 'FixedMath.RoundDivide',
            'target_clamp_authority': 'TargetConfig',
            'publication_operation': 'SET',
            'forbidden_numeric_types': ['float', 'double', 'decimal'],
            'forbidden_behaviors': [
                'Math.Round',
                'divide_before_weighted_sum_complete',
                'round_per_component',
                'silent_saturation',
                'unchecked_overflow',
                'unchecked_cast',
                'hardcoded_target_clamp',
            ]
        },
        'drift': {
            'phase': 9,
            'phase_name': 'DriftNationalToRegions',
            'snapshot': 'post_phase_8',
            'region_order_source': 'canonical_region_order.ordered_region_ids',
            'metric_order': ['support', 'tension', 'organization', 'rival_presence'],
            'region_count': 16,
            'outputs_per_region': 4,
            'output_count': 64,
            'half_life_weeks_metadata': 6,
            'alpha_ppm': 109101,
            'cap_per_weekS': 200,
            'target_baseS': 5000,
            'target_denominator': 1000000,
            'all_sources_read_from': 'phase_input_snapshot',
            'rival_support_read_from': 'phase_input_snapshot_pre_drift',
            'target_formulas': {
                'support': {
                    'target': 'regions.{region_id}.support',
                    'terms': [
                        {
                            'source': 'metrics.legitimacy',
                            'transform': 'value_minus_mid',
                            'coefficient_ppm': 600000
                        },
                        {
                            'source': 'metrics.party_organization',
                            'transform': 'value_minus_mid',
                            'coefficient_ppm': 300000
                        },
                        {
                            'source': 'metrics.social_tension',
                            'transform': 'value_minus_mid',
                            'coefficient_ppm': -400000
                        },
                    ]
                },
                'tension': {
                    'target': 'regions.{region_id}.tension',
                    'terms': [
                        {
                            'source': 'metrics.economy',
                            'transform': 'mid_minus_value',
                            'coefficient_ppm': 500000
                        },
                        {
                            'source': 'metrics.security',
                            'transform': 'mid_minus_value',
                            'coefficient_ppm': 400000
                        },
                        {
                            'source': 'metrics.public_agenda',
                            'transform': 'value_minus_mid',
                            'coefficient_ppm': 300000
                        },
                    ]
                },
                'organization': {
                    'target': 'regions.{region_id}.organization',
                    'terms': [
                        {
                            'source': 'metrics.party_organization',
                            'transform': 'value_minus_mid',
                            'coefficient_ppm': 800000
                        },
                    ]
                },
                'rival_presence': {
                    'target': 'regions.{region_id}.rival_presence',
                    'terms': [
                        {
                            'source': 'regions.{region_id}.support',
                            'transform': 'mid_minus_value',
                            'coefficient_ppm': 700000
                        },
                        {
                            'source': 'metrics.internal_cohesion',
                            'transform': 'mid_minus_value',
                            'coefficient_ppm': 200000
                        },
                    ]
                }
            },
            'common_pipeline': [
                'construct_target_numerator_in_checked_long',
                'round_target_offset_once',
                'add_mid',
                'target_config_clamp_target',
                'distance_target_minus_current',
                'multiply_distance_by_alpha_in_checked_long',
                'round_elastic_delta_once',
                'clamp_delta_to_weekly_cap',
                'add_delta_to_current',
                'target_config_clamp_final',
                'realized_delta_final_minus_current',
            ]
        },
        'pull': {
            'phase': 10,
            'phase_name': 'PullRegionsToInternals',
            'snapshot': 'post_phase_9',
            'region_order_source': 'canonical_region_order.ordered_region_ids',
            'weight_source': 'content_pack_region.weight_ppm',
            'weight_sum_required': 1000000,
            'weighted_average_denominator': 1000000,
            'weighted_average_intermediate_type': 'checked_long',
            'weighted_average_rounding': 'HALF_AWAY_FROM_ZERO',
            'weighted_average_division_count': 1,
            'all_sources_read_from': 'phase_input_snapshot',
            'binding_chaining': 'forbidden',
            'alpha_ppm': 206299,
            'cap_per_weekS': 400,
            'output_count': 5,
            'binding_order': [
                'support_to_coalition_strength',
                'organization_to_field_ops',
                'tension_to_protest_activity',
                'rival_presence_to_opposition_obstruction',
                'tension_to_movement_salience',
            ],
            'bindings': [
                {
                    'id': 'support_to_coalition_strength',
                    'regional_source': 'support',
                    'destination': 'internals.leg.coalition_strength'
                },
                {
                    'id': 'organization_to_field_ops',
                    'regional_source': 'organization',
                    'destination': 'internals.party.field_ops'
                },
                {
                    'id': 'tension_to_protest_activity',
                    'regional_source': 'tension',
                    'destination': 'internals.tension.protest_activity'
                },
                {
                    'id': 'rival_presence_to_opposition_obstruction',
                    'regional_source': 'rival_presence',
                    'destination': 'internals.leg.opposition_obstruction'
                },
                {
                    'id': 'tension_to_movement_salience',
                    'regional_source': 'tension',
                    'destination': 'internals.agenda.movement_salience'
                },
            ],
            'common_pipeline': [
                'construct_complete_weighted_sum_in_checked_long',
                'round_weighted_average_once',
                'target_config_clamp_target',
                'distance_target_minus_current',
                'multiply_distance_by_alpha_in_checked_long',
                'round_elastic_delta_once',
                'clamp_delta_to_weekly_cap',
                'add_delta_to_current',
                'target_config_clamp_final',
                'realized_delta_final_minus_current',
            ]
        },
        'phase_order': {
            'aggregate_national_metrics': 8,
            'drift_national_to_regions': 9,
            'pull_regions_to_internals': 10,
            'close_causal_report': 15,
            'detect_and_publish_blocking_decision': 16
        },
        'snapshot_semantics': {
            'phase_9_snapshot': 'post_phase_8_immutable',
            'phase_9_all_outputs_share_snapshot': True,
            'phase_9_rival_support_source': 'phase_input_snapshot_pre_drift',
            'phase_10_snapshot': 'post_phase_9_immutable',
            'phase_10_all_bindings_share_snapshot': True,
            'phase_10_binding_chaining': False
        },
        'latency': {
            'feedback_latency_ticks': 1,
            'phase_8_observes': 'internals_before_current_tick_pull',
            'phase_9_writes': 'regional_dynamic_targets',
            'phase_10_writes': 'five_internal_targets',
            'same_tick_phase_8_reexecution': False,
            'next_tick_observation_order': ['RevertInternals', 'DeriveInternals', 'AggregateNationalMetrics'],
            'regional_feedback_first_visible_in_metrics': 'tick_plus_1_phase_8'
        },
        'cause_key_grammar': {
            'cause_category': 'SYSTEM',
            'parent': None,
            'canonical_key_separator': ':',
            'canonical_key_separator_count': 1,
            'identifier_separator': '.',
            'identifier_policy': 'PRINTABLE_ASCII_NO_WHITESPACE_COLON_PIPE',
            'drift': {
                'id_prefix': 'REG_DRIFT',
                'canonical_key_pattern': 'SYSTEM:REG_DRIFT.regions.{region_id}.{metric}',
                'target_pattern': 'regions.{region_id}.{metric}',
                'target_visibility': 'public_visible_target',
                'public_ledger': True,
                'tick_causal_buffer': True,
                'potential_cause_count': 64,
                'cause_order': 'canonical_region_order_then_drift_metric_order',
                'zero_realized_delta_policy': 'omit_contribution'
            }
        },
        'hidden_pull_provenance': {
            'pull_provenance': {
                'id_prefix': 'REG_TO_INT',
                'canonical_key_pattern': 'SYSTEM:REG_TO_INT.{internal_target}',
                'identity_count': 5,
                'provenance_scope': 'ephemeral_execution_plan_only',
                'target_visibility': 'hidden_internal',
                'public_ledger': False,
                'tick_causal_buffer': False,
                'serialized': False,
                'stored_in_game_state': False,
                'turn_report': False,
                'top_n_slot': False,
                'lifetime': 'current_phase_10_plan_only',
                'public_attribution': 'next_tick_SYSTEM_AGG',
                'double_counting': 'forbidden',
                'identities': [
                    {
                        'binding_id': 'support_to_coalition_strength',
                        'canonical_key': 'SYSTEM:REG_TO_INT.internals.leg.coalition_strength',
                        'internal_target': 'internals.leg.coalition_strength'
                    },
                    {
                        'binding_id': 'organization_to_field_ops',
                        'canonical_key': 'SYSTEM:REG_TO_INT.internals.party.field_ops',
                        'internal_target': 'internals.party.field_ops'
                    },
                    {
                        'binding_id': 'tension_to_protest_activity',
                        'canonical_key': 'SYSTEM:REG_TO_INT.internals.tension.protest_activity',
                        'internal_target': 'internals.tension.protest_activity'
                    },
                    {
                        'binding_id': 'rival_presence_to_opposition_obstruction',
                        'canonical_key': 'SYSTEM:REG_TO_INT.internals.leg.opposition_obstruction',
                        'internal_target': 'internals.leg.opposition_obstruction'
                    },
                    {
                        'binding_id': 'tension_to_movement_salience',
                        'canonical_key': 'SYSTEM:REG_TO_INT.internals.agenda.movement_salience',
                        'internal_target': 'internals.agenda.movement_salience'
                    },
                ]
            },
            'public_aggregation_attribution': [
                {
                    'internal_target': 'internals.leg.coalition_strength',
                    'visible_metric': 'metrics.legislative_capacity',
                    'canonical_key': 'SYSTEM:AGG.metrics.legislative_capacity.internals.leg.coalition_strength'
                },
                {
                    'internal_target': 'internals.party.field_ops',
                    'visible_metric': 'metrics.party_organization',
                    'canonical_key': 'SYSTEM:AGG.metrics.party_organization.internals.party.field_ops'
                },
                {
                    'internal_target': 'internals.tension.protest_activity',
                    'visible_metric': 'metrics.social_tension',
                    'canonical_key': 'SYSTEM:AGG.metrics.social_tension.internals.tension.protest_activity'
                },
                {
                    'internal_target': 'internals.leg.opposition_obstruction',
                    'visible_metric': 'metrics.legislative_capacity',
                    'canonical_key': 'SYSTEM:AGG.metrics.legislative_capacity.internals.leg.opposition_obstruction'
                },
                {
                    'internal_target': 'internals.agenda.movement_salience',
                    'visible_metric': 'metrics.public_agenda',
                    'canonical_key': 'SYSTEM:AGG.metrics.public_agenda.internals.agenda.movement_salience'
                },
            ]
        },
        'pass_execution_semantics': {
            'scope': {
                'phase_9_and_phase_10': 'separate_atomic_passes',
                'pass_atomicity': 'executor_responsibility',
                'observable_tick_atomicity': 'scheduler_result_boundary',
                'runtime_tick_fail_closed_verification': 'PR_15_4'
            },
            'execution_sequence': [
                'capture_immutable_snapshot',
                'validate_all_bindings_and_inputs',
                'compute_all_outputs',
                'compute_all_applicable_causes_or_provenance',
                'validate_deltas_ranges_and_causal_accounting',
                'construct_complete_candidate',
                'publish_once',
            ],
            'phase_9_drift': {
                'phase': 9,
                'snapshot': 'post_phase_8_immutable',
                'planned_output_count': 64,
                'candidate': 'complete_regional_state_and_visible_causal_batch',
                'publication': 'single_atomic_state_and_causal_batch',
                'partial_publication': False,
                'cross_output_observation': False,
                'failure_behavior': 'discard_candidate_and_publish_nothing'
            },
            'phase_10_pull': {
                'phase': 10,
                'snapshot': 'post_phase_9_immutable',
                'planned_output_count': 5,
                'candidate': 'complete_internal_state_with_ephemeral_provenance',
                'publication': 'single_atomic_internal_state_batch',
                'public_causal_publication': 'none',
                'ephemeral_provenance_publication': 'none',
                'partial_publication': False,
                'binding_chaining': False,
                'failure_behavior': 'discard_candidate_and_publish_nothing'
            },
            'fail_closed_triggers': [
                'missing_region',
                'duplicate_region',
                'inconsistent_canonical_order',
                'region_count_mismatch',
                'weight_sum_mismatch',
                'non_positive_weight',
                'missing_regional_field',
                'missing_internal_destination',
                'missing_target_config',
                'set_not_allowed',
                'invalid_cause_ref',
                'duplicate_output',
                'duplicate_coupling',
                'multiplication_overflow',
                'sum_overflow',
                'addition_overflow',
                'long_to_int_out_of_range',
                'invalid_denominator',
                'invalid_clamp_or_invariant',
                'causal_batch_rejection',
                'visible_delta_causal_sum_mismatch',
            ],
            'fail_closed_guarantees': ['zero_partial_game_state', 'zero_partial_regions', 'zero_partial_internals', 'zero_partial_causal_contributions'],
            'observable_tick_failure': {
                'scenario': 'phase_9_succeeds_phase_10_fails',
                'advance_one_tick_result': 'not_returned',
                'original_game_state': 'unchanged',
                'phase_9_snapshot_exposed': False,
                'partial_causal_ledger_exposed': False,
                'sealed_causal_ledger_exposed': False,
                'scheduler_working_state': 'local_only',
                'scheduler_causal_buffer': 'local_only_until_phase_15_seal',
                'runtime_test_owner': 'PR_15_4'
            },
            'non_guarantees': [
                'phases_9_and_10_are_not_one_super_pass',
                'PR_15_1_does_not_implement_runtime_transactions',
                'PR_15_1_does_not_activate_scheduler_phases_9_or_10',
            ]
        },
        'active_reform_bias_exclusion': {
            'included_in_pr_15_x': False,
            'runtime_hook': False,
            'placeholder': False,
            'neutral_branch': False,
            'cause_key': None,
            'implementation_owner': 'PR_19_4'
        },
        'vectors': {
            'rounding': ['R-01', 'R-02'],
            'drift': ['D-00', 'D-01', 'D-02', 'D-03', 'D-04', 'D-05', 'D-06', 'D-07', 'D-08', 'D-08-WRONG', 'D-09', 'D-10'],
            'pull': ['P-00', 'P-01', 'P-02', 'P-03', 'P-04', 'P-05'],
            'latency': ['L-01-T', 'L-01-T1-R', 'L-01-T1-A', 'L-01-CAUSE'],
            'ordering': ['O-01', 'O-02', 'O-03', 'O-04', 'O-05'],
            'fixture_owner': 'PR_15_1_J',
            'oracle_owner': 'PR_15_1_K'
        },
        'scope': [
            'machine_readable_territory_contract',
            'human_readable_territory_contract',
            'execution_vectors',
            'independent_python_oracle',
            'contract_parity_and_negative_tests',
        ],
        'non_scope': [
            'runtime_csharp_implementation',
            'scheduler_phase_activation',
            'game_state_schema_changes',
            'content_pack_changes',
            'persistence_or_migrations',
            'active_reform_bias_before_PR_19_4',
            'ui_or_turn_report_changes',
        ]
    },
    'rationale': 'The territory feedback contract now freezes the complete regional authority, drift, pull, latency, causality, and atomicity rules from the canonical territory_contract.md. The cause_key_grammar and hidden_pull_provenance are structural copies of territory_contract.causality without reinterpretation. pass_execution_semantics is territory.atomicity. active_reform_bias is explicitly excluded and owned by PR 19.4. Execution vectors are assigned to PR 15.1-J (fixture) and PR 15.1-K (oracle).'
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
        self.assertEqual(len(data["decisions"]), 13)

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
                "servicios p\u00fablicos",
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
                "servicios p\u00fablicos": "mov_salud_crisis_atencion",
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


    def test_schema_version_remains_2(self) -> None:
        data = read_json_document()
        self.assertEqual(data["schema_version"], 2)
        self.assertIs(type(data["schema_version"]), int)

    def test_mvp_013_is_last_and_unique(self) -> None:
        data = read_json_document()
        ids = [d["id"] for d in data["decisions"]]
        self.assertEqual(ids[-1], "MVP-013-territory-feedback")
        self.assertEqual(ids.count("MVP-013-territory-feedback"), 1)

    def test_mvp_013_is_approved(self) -> None:
        decision = read_json_document()["decisions"][12]
        self.assertEqual(decision["status"], "approved")
        self.assertEqual(decision["id"], "MVP-013-territory-feedback")

    def test_mvp_013_resolution_keys_are_exact_and_ordered(self) -> None:
        resolution = read_json_document()["decisions"][12]["resolution"]
        expected_keys = [
            "canonical_region_order",
            "regional_dynamic_targets",
            "static_regional_resources",
            "numeric_domain",
            "drift",
            "pull",
            "phase_order",
            "snapshot_semantics",
            "latency",
            "cause_key_grammar",
            "hidden_pull_provenance",
            "pass_execution_semantics",
            "active_reform_bias_exclusion",
            "vectors",
            "scope",
            "non_scope",
        ]
        self.assertEqual(list(resolution.keys()), expected_keys)

    def test_mvp_013_matches_expected_exactly(self) -> None:
        decision = read_json_document()["decisions"][12]
        self.assertEqual(decision, EXPECTED_MVP_013)

    def test_previous_decisions_remain_exact(self) -> None:
        import subprocess
        result = subprocess.run(
            ["git", "show", "ecf080c2fd787b285698fcf49bdee9936f0b4658:docs/mvp_contract_decisions.json"],
            capture_output=True,
        )
        parent_doc = json.loads(result.stdout.decode("utf-8"))
        current_doc = read_json_document()
        self.assertEqual(len(parent_doc["decisions"]), 12)
        self.assertEqual(current_doc["decisions"][:12], parent_doc["decisions"])

    def test_json_document_matches_expected_document(self) -> None:
        data = read_json_document()
        self.assertEqual(data, EXPECTED_DOCUMENT)

    def test_markdown_canonical_json_matches_json_document(self) -> None:
        markdown = read_markdown_text()
        match = CANONICAL_JSON_BLOCK_RE.search(markdown)
        self.assertIsNotNone(match)
        embedded = json.loads(match.group(1))
        self.assertEqual(embedded, read_json_document())
        self.assertEqual(embedded, EXPECTED_DOCUMENT)

    def test_markdown_contains_single_canonical_block(self) -> None:
        markdown = read_markdown_text()
        matches = CANONICAL_JSON_BLOCK_RE.findall(markdown)
        self.assertEqual(len(matches), 1)

    def test_mvp_013_core_blocks_match_territory_contract(self) -> None:
        from pathlib import Path
        import re
        tc_path = Path(__file__).resolve().parents[2] / "docs" / "territory_contract.md"
        tc_text = tc_path.read_text("utf-8")
        mtc = re.search(
            r"<!-- BEGIN CANONICAL REGION AUTHORITY -->\s*```json\s*(.*?)\s*```\s*<!-- END CANONICAL REGION AUTHORITY -->",
            tc_text, re.DOTALL,
        )
        self.assertIsNotNone(mtc)
        tc_block = json.loads(mtc.group(1))
        resolution = read_json_document()["decisions"][12]["resolution"]
        self.assertEqual(resolution["canonical_region_order"], tc_block["canonical_region_order"])
        self.assertEqual(resolution["regional_dynamic_targets"], tc_block["regional_dynamic_targets"])
        self.assertEqual(resolution["static_regional_resources"], tc_block["static_regional_resources"])
        self.assertEqual(resolution["numeric_domain"], tc_block["numeric_domain"])
        self.assertEqual(resolution["drift"], tc_block["drift"])
        self.assertEqual(resolution["pull"], tc_block["pull"])
        self.assertEqual(resolution["latency"], tc_block["latency"])

    def test_mvp_013_causality_mapping_matches_territory_contract(self) -> None:
        from pathlib import Path
        import re
        tc_path = Path(__file__).resolve().parents[2] / "docs" / "territory_contract.md"
        tc_text = tc_path.read_text("utf-8")
        mtc = re.search(
            r"<!-- BEGIN CANONICAL REGION AUTHORITY -->\s*```json\s*(.*?)\s*```\s*<!-- END CANONICAL REGION AUTHORITY -->",
            tc_text, re.DOTALL,
        )
        tc_block = json.loads(mtc.group(1))
        c = tc_block["causality"]
        resolution = read_json_document()["decisions"][12]["resolution"]
        expected_grammar = {k: v for k, v in c.items() if k not in ("pull_provenance", "public_aggregation_attribution")}
        self.assertEqual(resolution["cause_key_grammar"], expected_grammar)
        expected_hidden = {
            "pull_provenance": c["pull_provenance"],
            "public_aggregation_attribution": c["public_aggregation_attribution"],
        }
        self.assertEqual(resolution["hidden_pull_provenance"], expected_hidden)
        pull_prov = resolution["hidden_pull_provenance"]["pull_provenance"]
        self.assertEqual(len(pull_prov["identities"]), 5)
        self.assertEqual(pull_prov["identity_count"], 5)
        self.assertEqual(pull_prov["id_prefix"], "REG_TO_INT")
        agg = resolution["hidden_pull_provenance"]["public_aggregation_attribution"]
        self.assertEqual(len(agg), 5)
        self.assertTrue(all("SYSTEM:AGG" in entry["canonical_key"] for entry in agg))

    def test_mvp_013_atomicity_matches_territory_contract(self) -> None:
        resolution = read_json_document()["decisions"][12]["resolution"]
        self.assertEqual(resolution["pass_execution_semantics"]["scope"]["pass_atomicity"], "executor_responsibility")
        self.assertEqual(resolution["pass_execution_semantics"]["scope"]["observable_tick_atomicity"], "scheduler_result_boundary")
        self.assertEqual(len(resolution["pass_execution_semantics"]["fail_closed_triggers"]), 21)
        self.assertEqual(len(resolution["pass_execution_semantics"]["fail_closed_guarantees"]), 4)
        self.assertEqual(resolution["pass_execution_semantics"]["observable_tick_failure"]["runtime_test_owner"], "PR_15_4")

    def test_mvp_013_phase_order_matches_mvp_004(self) -> None:
        resolution = read_json_document()["decisions"][12]["resolution"]
        po = resolution["phase_order"]
        self.assertEqual(po["aggregate_national_metrics"], 8)
        self.assertEqual(po["drift_national_to_regions"], 9)
        self.assertEqual(po["pull_regions_to_internals"], 10)
        self.assertEqual(po["close_causal_report"], 15)
        self.assertEqual(po["detect_and_publish_blocking_decision"], 16)
        phases = read_json_document()["decisions"][3]["resolution"]["phases"]
        self.assertEqual(phases[7], "aggregate_national_metrics")
        self.assertEqual(phases[8], "drift_national_to_regions")
        self.assertEqual(phases[9], "pull_regions_to_internals")
        self.assertEqual(phases[14], "close_causal_report")
        self.assertEqual(phases[15], "detect_and_publish_blocking_decision")

    def test_active_reform_bias_is_explicitly_excluded(self) -> None:
        arb = read_json_document()["decisions"][12]["resolution"]["active_reform_bias_exclusion"]
        self.assertFalse(arb["included_in_pr_15_x"])
        self.assertFalse(arb["runtime_hook"])
        self.assertFalse(arb["placeholder"])
        self.assertFalse(arb["neutral_branch"])
        self.assertIsNone(arb["cause_key"])
        self.assertEqual(arb["implementation_owner"], "PR_19_4")

    def test_mvp_013_vector_registry_is_exact(self) -> None:
        vectors = read_json_document()["decisions"][12]["resolution"]["vectors"]
        self.assertEqual(vectors["rounding"], ["R-01", "R-02"])
        self.assertEqual(vectors["drift"], ["D-00", "D-01", "D-02", "D-03", "D-04", "D-05", "D-06", "D-07", "D-08", "D-08-WRONG", "D-09", "D-10"])
        self.assertEqual(vectors["pull"], ["P-00", "P-01", "P-02", "P-03", "P-04", "P-05"])
        self.assertEqual(vectors["latency"], ["L-01-T", "L-01-T1-R", "L-01-T1-A", "L-01-CAUSE"])
        self.assertEqual(vectors["ordering"], ["O-01", "O-02", "O-03", "O-04", "O-05"])
        self.assertEqual(vectors["fixture_owner"], "PR_15_1_J")
        self.assertEqual(vectors["oracle_owner"], "PR_15_1_K")

    def test_mvp_013_mutation_matrix(self) -> None:
        import copy
        base = copy.deepcopy(read_json_document())
        dec13 = copy.deepcopy(base["decisions"][12])
        res13 = dec13["resolution"]
        with self.subTest("schema_version_3"):
            mutated = copy.deepcopy(base)
            mutated["schema_version"] = 3
            self.assertNotEqual(mutated, EXPECTED_DOCUMENT)
        with self.subTest("status_proposed"):
            mutated = copy.deepcopy(dec13)
            mutated["status"] = "proposed"
            self.assertNotEqual(mutated, EXPECTED_MVP_013)
        with self.subTest("mvp013_not_last"):
            mutated = copy.deepcopy(base)
            mutated["decisions"].append(copy.deepcopy(dec13))
            self.assertNotEqual(len(mutated["decisions"]), 13)
        with self.subTest("mvp013_duplicated"):
            mutated = copy.deepcopy(base)
            mutated["decisions"].append(copy.deepcopy(dec13))
            self.assertNotEqual(len(mutated["decisions"]), 13)
        with self.subTest("resolution_key_omitted"):
            mutated = copy.deepcopy(dec13)
            del mutated["resolution"]["canonical_region_order"]
            self.assertNotEqual(mutated, EXPECTED_MVP_013)
        with self.subTest("resolution_key_extra"):
            mutated = copy.deepcopy(dec13)
            mutated["resolution"]["extra_key"] = "test"
            self.assertNotEqual(mutated, EXPECTED_MVP_013)
        with self.subTest("keys_reordered"):
            mutated = copy.deepcopy(dec13)
            items = list(mutated["resolution"].items())
            mutated["resolution"] = dict(items[1:] + items[:1])
            expected_keys = list(EXPECTED_MVP_013["resolution"].keys())
            self.assertNotEqual(list(mutated["resolution"].keys()), expected_keys)
        with self.subTest("region_swapped"):
            mutated = copy.deepcopy(dec13)
            order = mutated["resolution"]["canonical_region_order"]["ordered_region_ids"]
            order[1], order[2] = order[2], order[1]
            self.assertNotEqual(mutated, EXPECTED_MVP_013)
        with self.subTest("drift_alpha_109100"):
            mutated = copy.deepcopy(dec13)
            mutated["resolution"]["drift"]["alpha_ppm"] = 109100
            self.assertNotEqual(mutated, EXPECTED_MVP_013)
        with self.subTest("pull_alpha_206298"):
            mutated = copy.deepcopy(dec13)
            mutated["resolution"]["pull"]["alpha_ppm"] = 206298
            self.assertNotEqual(mutated, EXPECTED_MVP_013)
        with self.subTest("reg_to_int_public_ledger_true"):
            mutated = copy.deepcopy(dec13)
            mutated["resolution"]["hidden_pull_provenance"]["pull_provenance"]["public_ledger"] = True
            self.assertNotEqual(mutated, EXPECTED_MVP_013)
        with self.subTest("active_reform_included_true"):
            mutated = copy.deepcopy(dec13)
            mutated["resolution"]["active_reform_bias_exclusion"]["included_in_pr_15_x"] = True
            self.assertNotEqual(mutated, EXPECTED_MVP_013)
        with self.subTest("runtime_owner_altered"):
            mutated = copy.deepcopy(dec13)
            mutated["resolution"]["active_reform_bias_exclusion"]["implementation_owner"] = "PR_15_4"
            self.assertNotEqual(mutated, EXPECTED_MVP_013)
        with self.subTest("vector_omitted"):
            mutated = copy.deepcopy(dec13)
            del mutated["resolution"]["vectors"]["rounding"]
            self.assertNotEqual(mutated, EXPECTED_MVP_013)
        with self.subTest("previous_decision_modified"):
            mutated = copy.deepcopy(base)
            mutated["decisions"][0]["resolution"]["target"] = "metrics.social_tension_modified"
            self.assertNotEqual(mutated, EXPECTED_DOCUMENT)
        with self.subTest("markdown_canonical_differs"):
            markdown = read_markdown_text()
            match = CANONICAL_JSON_BLOCK_RE.search(markdown)
            self.assertIsNotNone(match)
            embedded = json.loads(match.group(1))
            self.assertEqual(embedded, read_json_document())


if __name__ == "__main__":
    unittest.main()
