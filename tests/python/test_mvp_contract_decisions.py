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
SECTION_HEADING_RE = re.compile(r"^## (MVP-[0-9]{3}-[a-z0-9-]+)$", re.MULTILINE)
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
                "higher_is_worse": True,
                "alert_threshold": {"op": ">", "valueS": 7000},
                "driver_weight_scale": "weight_ppm",
                "drivers": [
                    {"target": "internals.tension.cost_of_living", "weight_ppm": 350000},
                    {"target": "internals.tension.polarization", "weight_ppm": 250000},
                    {"target": "internals.tension.protest_activity", "weight_ppm": 250000},
                    {"target": "internals.tension.institutional_trust", "weight_ppm": -150000},
                ],
                "content_alignment": {
                    "content_pack_change_in_pr8": False,
                    "content_pack_change_pr": 9,
                },
            },
            "rationale": "The metric now has one sign convention and exact integer drivers for later loader alignment.",
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
                    "SYSTEM",
                ],
                "modifier_parent_categories": [
                    "DECISION",
                    "EVENT",
                    "REFORM",
                    "MOVEMENT",
                ],
                "system_causes": {
                    "clamp": "SYSTEM:CLAMP",
                    "rounding": "SYSTEM:ROUNDING",
                    "ig_clout_normalize": "SYSTEM:IG_CLOUT_NORMALIZE",
                },
                "allow_additional_categories": False,
            },
            "rationale": "A closed ordered category set is required for stable causal serialization and reporting.",
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
                        "effect_instance_id_ordinal_asc",
                    ],
                    "apply_winning_set": True,
                    "ignore_add_and_mul_for_same_target": True,
                },
                "phase_order_without_set": ["ADD", "MUL"],
                "add_order": [
                    "priority_desc",
                    "effect_instance_id_ordinal_asc",
                ],
                "mul_order": [
                    "priority_desc",
                    "effect_instance_id_ordinal_asc",
                ],
                "mul_rounding": "HALF_AWAY_FROM_ZERO",
                "post_processing": [
                    "final_clamp",
                    "final_normalization",
                    "system_residue",
                ],
                "forbid": [
                    "load_order",
                    "dictionary_order",
                    "grouped_mul_product",
                    "floats",
                ],
            },
            "rationale": "Deterministic per-target ordering is required for replay, testing, and causal explanations.",
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
                    "detect_and_publish_blocking_decision",
                ],
                "regional_feedback_latency_ticks": 1,
            },
            "rationale": "One canonical phase order prevents hidden feedback loops and makes reports reproducible.",
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
                    {"name": "state_u64", "format": "hex_lowercase_16"},
                    {
                        "name": "stream_u64",
                        "format": "hex_lowercase_16",
                        "must_be_odd": True,
                    },
                    {"name": "draw_count_u64", "format": "hex_lowercase_16"},
                ],
                "forbid": [
                    "System.Random",
                    "GetHashCode",
                    "implicit_global_rng",
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
                            "seed_i64_le",
                        ],
                    },
                    "derivation": "sha-256",
                    "digest_extraction": {
                        "state_u64": {
                            "offset_bytes": [0, 7],
                            "byte_order": "little_endian",
                        },
                        "stream_u64_pre_oddify": {
                            "offset_bytes": [8, 15],
                            "byte_order": "little_endian",
                        },
                        "stream_u64_post_process": "bitwise_or_1",
                    },
                    "draw_count_u64_initial_hex": "0000000000000000",
                },
                "draw_transition": {
                    "old_state_source": "state_u64",
                    "new_state_formula": "old_state * multiplier + stream_u64 modulo 2^64",
                    "xorshifted_formula": "uint32((((old_state >> 18) xor old_state) >> 27))",
                    "rotation_formula": "uint32(old_state >> 59)",
                    "output_formula": "rotate_right_32(xorshifted, rotation)",
                    "post_success": [
                        "state_u64 = new_state",
                        "draw_count_u64 += 1",
                    ],
                },
                "counter_exhaustion": {
                    "when": "draw_count_u64 == uint64_max before draw",
                    "behavior": "fail_closed_without_state_stream_or_counter_change",
                },
                "bounded_draw": {
                    "bound_must_be_positive": True,
                    "algorithm": "rejection_sampling_without_modulo_bias",
                    "threshold_formula": "(2^32 - bound) mod bound",
                    "rejected_raw_draws_increment_counter": True,
                    "invalid_bound_consumes_rng": False,
                },
                "event_selector_keying": {
                    "enabled": True,
                    "encoding": "utf-8",
                    "key_parts": [
                        "seed",
                        "tick",
                        "system",
                        "template",
                        "slot",
                    ],
                    "field_types": {
                        "seed": "int64_signed",
                        "tick": "uint64",
                        "system": "ascii_identifier",
                        "template": "ascii_identifier",
                        "slot": "uint64",
                    },
                    "string_validation": {
                        "pattern": "[a-z0-9][a-z0-9._-]*",
                        "forbid": [
                            "empty",
                            "whitespace",
                            "control_chars",
                            "newlines",
                            "unicode",
                            "nul_bytes",
                        ],
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
                            "slot_u64_le",
                        ],
                    },
                    "derivation": "sha-256",
                    "digest_extraction": {
                        "keyed_state_u64": {
                            "offset_bytes": [0, 7],
                            "byte_order": "little_endian",
                        },
                        "keyed_stream_u64_pre_oddify": {
                            "offset_bytes": [8, 15],
                            "byte_order": "little_endian",
                        },
                        "keyed_stream_u64_post_process": "bitwise_or_1",
                    },
                    "keyed_draw": "first_pcg32_output_from_derived_state",
                    "global_state_consumption": False,
                    "warmup_draws": 0,
                    "final_tie_break": "id_ordinal_asc",
                    "determinism_rule": "same_state_and_actions_same_draws_and_hashes",
                },
            },
            "rationale": "A human-approved PR 13 amendment completes pcg32-v1 byte-for-byte without creating pcg32-v2.",
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
                "early_end_conditions": ["victory", "defeat"],
            },
            "rationale": "The slice now has a fixed campaign length with explicit early terminal exits.",
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
                    "upper_chamber": "government_minority",
                },
                "core_metric_defaults": {
                    "metrics.legitimacy": 5500,
                    "other_core_metrics_defaultS": 5000,
                },
                "primary_reform": {
                    "route": "A",
                    "kind": "SPECIAL_CONSTITUTIONAL",
                    "count": 1,
                },
                "open_crises_at_tick0": 0,
                "content_pack_change_in_pr8": False,
            },
            "rationale": "The MVP now starts from one fixed fictional scenario with one constitutional Route A agenda.",
        },
        {
            "id": "MVP-008-primary-objective",
            "topic": "Primary player objective",
            "question": "What is the primary player objective, including explicit success and failure conditions?",
            "status": "approved",
            "resolution": {
                "victory_condition": {
                    "target": "special_constitutional_route_a_reform",
                    "must_be_approved": True,
                    "must_be_applied": True,
                    "deadline_week_inclusive": 26,
                },
                "defeat_conditions": [
                    {
                        "type": "deadline",
                        "week": 26,
                        "requires_approved_and_applied": True,
                    },
                    {
                        "type": "terminal_reform_state",
                        "target": "special_constitutional_route_a_reform",
                        "state": "FAILED",
                    },
                    {
                        "type": "metric_threshold",
                        "target": "metrics.legitimacy",
                        "op": "<",
                        "valueS": 2000,
                    },
                    {
                        "type": "blocking_crisis_expiry",
                        "state": "unresolved_expired",
                    },
                ],
                "out_of_slice": [
                    "presidential_election",
                    "opposition_neutralization",
                    "full_constituent_route_b",
                    "four_year_campaign",
                ],
            },
            "rationale": "Victory and defeat are now tied to one Route A constitutional slice with explicit terminal conditions.",
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
                    "SPOKESPERSON",
                ],
                "cadres_per_role": 2,
                "cadre_def_fields": [
                    "id",
                    "localization_key",
                    "role",
                    "tags",
                ],
                "cadre_state_fields": [
                    "competenceS",
                    "loyaltyS",
                    "ambitionS",
                    "networksS",
                    "scandal_riskS",
                    "assignment_id",
                    "available",
                ],
                "metric_rangeS": {"min": 0, "max": 10000},
                "assignment_id_nullable": True,
                "generic_xp": False,
                "complex_progression_in_slice": False,
                "stable_ids": True,
                "definition_storage": "content_static_outside_save",
                "state_storage": "gamestate_save",
            },
            "rationale": "Cadres now have a minimal frozen schema, fixed roles, and a clear static-versus-save boundary.",
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
                        "target_path_ordinal_asc",
                    ],
                },
                "causes_per_target": {
                    "max_count": 3,
                    "order": [
                        "abs(delta_totalS)_desc",
                        "CauseKey_ordinal_asc",
                    ],
                },
                "other_deltaS": "exact_remainder",
                "alerts": {
                    "separate_section": True,
                    "consume_top_slots": False,
                },
                "zero_fill": False,
            },
            "rationale": "The turn report now has fixed limits, deterministic tie-breaks, and exact remainder accounting.",
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
                        "enabled": True,
                        "initial_intensityS": 3000,
                        "initial_direction": 1,
                        "last_addressed_tick": 0,
                    },
                    {
                        "theme": "seguridad/orden",
                        "movement_id": "mov_seguridad_mano_dura",
                        "enabled": True,
                        "initial_intensityS": 3000,
                        "initial_direction": 1,
                        "last_addressed_tick": 0,
                    },
                    {
                        "theme": "servicios públicos",
                        "movement_id": "mov_salud_crisis_atencion",
                        "enabled": True,
                        "initial_intensityS": 3000,
                        "initial_direction": 1,
                        "last_addressed_tick": 0,
                    },
                    {
                        "theme": "regional",
                        "movement_id": "mov_descentralizacion_regionalista",
                        "enabled": True,
                        "initial_intensityS": 3000,
                        "initial_direction": 1,
                        "last_addressed_tick": 0,
                    },
                ],
                "disabled_movements": [
                    {
                        "movement_id": "mov_educacion_paros",
                        "enabled": False,
                        "initial_intensityS": 0,
                        "excluded_from_update": True,
                        "excluded_from_event_selection": True,
                        "excluded_from_crisis_selection": True,
                        "exclusion_reason": "avoid_overlap_with_labor_strikes",
                    },
                    {
                        "movement_id": "mov_pensiones_presion_reforma",
                        "enabled": False,
                        "initial_intensityS": 0,
                        "excluded_from_update": True,
                        "excluded_from_event_selection": True,
                        "excluded_from_crisis_selection": True,
                        "exclusion_reason": "avoid_a_second_primary_legislative_reform",
                    },
                    {
                        "movement_id": "mov_institucional_reforma",
                        "enabled": False,
                        "initial_intensityS": 0,
                        "excluded_from_update": True,
                        "excluded_from_event_selection": True,
                        "excluded_from_crisis_selection": True,
                        "exclusion_reason": "avoid_duplication_with_route_a_constitutional_reform",
                    },
                    {
                        "movement_id": "mov_constitucional_proceso",
                        "enabled": False,
                        "initial_intensityS": 0,
                        "excluded_from_update": True,
                        "excluded_from_event_selection": True,
                        "excluded_from_crisis_selection": True,
                        "exclusion_reason": "route_b_pressure_is_outside_mvp",
                    },
                    {
                        "movement_id": "mov_pueblos_originarios_autonomia",
                        "enabled": False,
                        "initial_intensityS": 0,
                        "excluded_from_update": True,
                        "excluded_from_event_selection": True,
                        "excluded_from_crisis_selection": True,
                        "exclusion_reason": "deferred_to_post_slice_territorial_expansion",
                    },
                ],
                "environmental_representation": {
                    "interest_group_id": "ig_ambiental_regionalista",
                    "movement_id": None,
                    "representation_level": "interest_group_only",
                },
                "direction_semantics": "favorable_to_own_demand",
                "escalation_rules": {
                    "base_increment": {
                        "condition": "tick - last_addressed_tick >= 4",
                        "deltaS": 100,
                    },
                    "high_tension_bonus": {
                        "condition": "metrics.social_tension > 7000",
                        "deltaS": 100,
                    },
                    "post_update_clamp": {"minS": 0, "maxS": 10000},
                    "alert_threshold": {"op": ">", "valueS": 7000},
                    "blocking_crisis_eligibility_threshold": {"op": ">", "valueS": 8500},
                    "matching_address_action": "last_addressed_tick = current_tick",
                },
                "disabled_movements_activate_dynamically": False,
            },
            "rationale": "The slice now has four active movements, five fully disabled movements, and exact escalation state rules.",
        },
    ],
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
        self.assertEqual(len(data["decisions"]), 11)

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
        self.assertEqual(
            resolution["escalation_rules"]["base_increment"],
            {"condition": "tick - last_addressed_tick >= 4", "deltaS": 100},
        )
        self.assertEqual(
            resolution["escalation_rules"]["high_tension_bonus"],
            {"condition": "metrics.social_tension > 7000", "deltaS": 100},
        )
        self.assertEqual(
            resolution["escalation_rules"]["alert_threshold"],
            {"op": ">", "valueS": 7000},
        )
        self.assertEqual(
            resolution["escalation_rules"]["blocking_crisis_eligibility_threshold"],
            {"op": ">", "valueS": 8500},
        )

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
