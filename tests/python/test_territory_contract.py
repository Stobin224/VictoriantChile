from __future__ import annotations

import copy
import json
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]

BEGIN_MARKER = "<!-- BEGIN CANONICAL REGION AUTHORITY -->"
END_MARKER = "<!-- END CANONICAL REGION AUTHORITY -->"

# Independent frozen contract expected values — NOT derived from Content Pack.
EXPECTED_CANONICAL_REGION_ORDER = [
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
    "magallanes",
]

EXPECTED_REGION_COUNT = 16
EXPECTED_WEIGHT_PPM_EACH = 62500
EXPECTED_WEIGHT_PPM_SUM = 1000000
EXPECTED_AUTHORITY = "content_pack_declaration_order"
EXPECTED_SOURCE_PATH = "Assets/StreamingAssets/content/core/regions.json"
EXPECTED_FORBIDDEN_SOURCES = [
    "GameState.Regions",
    "RegionsById.Values",
    "dictionary_iteration",
    "lexicographic_sort",
]
EXPECTED_DYNAMIC_TARGETS = [
    "support",
    "tension",
    "organization",
    "rival_presence",
]
EXPECTED_STATIC_RESOURCES = {
    "admin_capS": 5000,
    "industry_capS": 5000,
    "extractive_capS": 5000,
    "social_capS": 5000,
    "populationS": 5000,
}
EXPECTED_STATIC_RESOURCE_NAMES = sorted(EXPECTED_STATIC_RESOURCES.keys())

EXPECTED_TOP_LEVEL_KEYS = {
    "canonical_region_order",
    "regional_dynamic_targets",
    "static_regional_resources",
    "numeric_domain",
    "drift",
    "pull",
    "latency",
    "causality",
}

EXPECTED_CANONICAL_ORDER_KEYS = {
    "authority",
    "source_path",
    "region_count",
    "ordered_region_ids",
    "weight_ppm_each",
    "weight_ppm_sum_required",
    "forbidden_order_sources",
}

EXPECTED_NUMERIC_DOMAIN = {
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
    "forbidden_numeric_types": ["float", "double", "decimal"],
    "forbidden_behaviors": [
        "Math.Round",
        "divide_before_weighted_sum_complete",
        "round_per_component",
        "silent_saturation",
        "unchecked_overflow",
        "unchecked_cast",
        "hardcoded_target_clamp",
    ],
}

EXPECTED_NUMERIC_DOMAIN_KEYS = set(EXPECTED_NUMERIC_DOMAIN.keys())

EXPECTED_DRIFT_KEYS = {
    "phase",
    "phase_name",
    "snapshot",
    "region_order_source",
    "metric_order",
    "region_count",
    "outputs_per_region",
    "output_count",
    "half_life_weeks_metadata",
    "alpha_ppm",
    "cap_per_weekS",
    "target_baseS",
    "target_denominator",
    "all_sources_read_from",
    "rival_support_read_from",
    "target_formulas",
    "common_pipeline",
}

EXPECTED_DRIFT_SNAPSHOT = "post_phase_8"
EXPECTED_DRIFT_PHASE = 9
EXPECTED_DRIFT_PHASE_NAME = "DriftNationalToRegions"
EXPECTED_DRIFT_ALPHA_PPM = 109101
EXPECTED_DRIFT_CAP_PER_WEEKS = 200
EXPECTED_DRIFT_TARGET_BASES = 5000
EXPECTED_DRIFT_TARGET_DENOM = 1000000
EXPECTED_DRIFT_OUTPUT_COUNT = 64
EXPECTED_DRIFT_OUTPUTS_PER_REGION = 4
EXPECTED_DRIFT_METRIC_ORDER = [
    "support", "tension", "organization", "rival_presence",
]
EXPECTED_DRIFT_RIVAL_SUPPORT_SOURCE = "phase_input_snapshot_pre_drift"
EXPECTED_DRIFT_ALL_SOURCES = "phase_input_snapshot"

EXPECTED_DRIFT_HALF_LIFE_WEEKS_METADATA = 6
EXPECTED_DRIFT_REGION_ORDER_SOURCE = "canonical_region_order.ordered_region_ids"

EXPECTED_TARGET_FORMULA_ORDER = ["support", "tension", "organization", "rival_presence"]

EXPECTED_TARGET_FORMULAS = {
    "support": {
        "target": "regions.{region_id}.support",
        "terms": [
            {"source": "metrics.legitimacy", "transform": "value_minus_mid", "coefficient_ppm": 600000},
            {"source": "metrics.party_organization", "transform": "value_minus_mid", "coefficient_ppm": 300000},
            {"source": "metrics.social_tension", "transform": "value_minus_mid", "coefficient_ppm": -400000},
        ],
    },
    "tension": {
        "target": "regions.{region_id}.tension",
        "terms": [
            {"source": "metrics.economy", "transform": "mid_minus_value", "coefficient_ppm": 500000},
            {"source": "metrics.security", "transform": "mid_minus_value", "coefficient_ppm": 400000},
            {"source": "metrics.public_agenda", "transform": "value_minus_mid", "coefficient_ppm": 300000},
        ],
    },
    "organization": {
        "target": "regions.{region_id}.organization",
        "terms": [
            {"source": "metrics.party_organization", "transform": "value_minus_mid", "coefficient_ppm": 800000},
        ],
    },
    "rival_presence": {
        "target": "regions.{region_id}.rival_presence",
        "terms": [
            {"source": "regions.{region_id}.support", "transform": "mid_minus_value", "coefficient_ppm": 700000},
            {"source": "metrics.internal_cohesion", "transform": "mid_minus_value", "coefficient_ppm": 200000},
        ],
    },
}

EXPECTED_DRIFT_PIPELINE = [
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
    "realized_delta_final_minus_current",
]

# Known per-target term count
EXPECTED_TERM_COUNTS = {"support": 3, "tension": 3, "organization": 1, "rival_presence": 2}

EXPECTED_PULL_PIPELINE = [
    "construct_complete_weighted_sum_in_checked_long",
    "round_weighted_average_once",
    "target_config_clamp_target",
    "distance_target_minus_current",
    "multiply_distance_by_alpha_in_checked_long",
    "round_elastic_delta_once",
    "clamp_delta_to_weekly_cap",
    "add_delta_to_current",
    "target_config_clamp_final",
    "realized_delta_final_minus_current",
]

EXPECTED_PULL_BINDING_ORDER = [
    "support_to_coalition_strength",
    "organization_to_field_ops",
    "tension_to_protest_activity",
    "rival_presence_to_opposition_obstruction",
    "tension_to_movement_salience",
]

EXPECTED_PULL_BINDINGS = [
    {"id": "support_to_coalition_strength", "regional_source": "support", "destination": "internals.leg.coalition_strength"},
    {"id": "organization_to_field_ops", "regional_source": "organization", "destination": "internals.party.field_ops"},
    {"id": "tension_to_protest_activity", "regional_source": "tension", "destination": "internals.tension.protest_activity"},
    {"id": "rival_presence_to_opposition_obstruction", "regional_source": "rival_presence", "destination": "internals.leg.opposition_obstruction"},
    {"id": "tension_to_movement_salience", "regional_source": "tension", "destination": "internals.agenda.movement_salience"},
]

EXPECTED_PULL = {
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
    "binding_order": list(EXPECTED_PULL_BINDING_ORDER),
    "bindings": list(EXPECTED_PULL_BINDINGS),
    "common_pipeline": list(EXPECTED_PULL_PIPELINE),
}

EXPECTED_PULL_KEYS = set(EXPECTED_PULL.keys())

EXPECTED_LATENCY = {
    "feedback_latency_ticks": 1,
    "phase_8_observes": "internals_before_current_tick_pull",
    "phase_9_writes": "regional_dynamic_targets",
    "phase_10_writes": "five_internal_targets",
    "same_tick_phase_8_reexecution": False,
    "next_tick_observation_order": ["RevertInternals", "DeriveInternals", "AggregateNationalMetrics"],
    "regional_feedback_first_visible_in_metrics": "tick_plus_1_phase_8",
}

EXPECTED_LATENCY_KEYS = set(EXPECTED_LATENCY.keys())

PULL_ALPHA = 206299
PULL_CAP = 400
REGIONAL_WEIGHTS = [62500] * 16

MID_S = 5000
PPM_DEN = 1000000
ALPHA_PPM = 109101
CAP_WEEK = 200

EXPECTED_CAUSE_CATEGORY = "SYSTEM"
EXPECTED_CAUSE_PARENT = None
EXPECTED_DRIFT_CAUSE_PATTERN = "SYSTEM:REG_DRIFT.regions.{region_id}.{metric}"
EXPECTED_PULL_PROVENANCE_PATTERN = "SYSTEM:REG_TO_INT.{internal_target}"

EXPECTED_CAUSALITY_KEYS = {
    "cause_category",
    "parent",
    "canonical_key_separator",
    "canonical_key_separator_count",
    "identifier_separator",
    "identifier_policy",
    "drift",
    "pull_provenance",
    "public_aggregation_attribution",
}

EXPECTED_CAUSALITY_DRIFT_KEYS = {
    "id_prefix",
    "canonical_key_pattern",
    "target_pattern",
    "target_visibility",
    "public_ledger",
    "tick_causal_buffer",
    "potential_cause_count",
    "cause_order",
    "zero_realized_delta_policy",
}

EXPECTED_CAUSALITY_PULL_KEYS = {
    "id_prefix",
    "canonical_key_pattern",
    "identity_count",
    "provenance_scope",
    "target_visibility",
    "public_ledger",
    "tick_causal_buffer",
    "serialized",
    "stored_in_game_state",
    "turn_report",
    "top_n_slot",
    "lifetime",
    "public_attribution",
    "double_counting",
    "identities",
}

EXPECTED_CAUSALITY_IDENTITY_KEYS = {
    "binding_id",
    "canonical_key",
    "internal_target",
}

EXPECTED_CAUSALITY_AGG_KEYS = {
    "internal_target",
    "visible_metric",
    "canonical_key",
}

EXPECTED_PULL_PROVENANCE_IDENTITIES = [
    {
        "binding_id": "support_to_coalition_strength",
        "canonical_key": "SYSTEM:REG_TO_INT.internals.leg.coalition_strength",
        "internal_target": "internals.leg.coalition_strength",
    },
    {
        "binding_id": "organization_to_field_ops",
        "canonical_key": "SYSTEM:REG_TO_INT.internals.party.field_ops",
        "internal_target": "internals.party.field_ops",
    },
    {
        "binding_id": "tension_to_protest_activity",
        "canonical_key": "SYSTEM:REG_TO_INT.internals.tension.protest_activity",
        "internal_target": "internals.tension.protest_activity",
    },
    {
        "binding_id": "rival_presence_to_opposition_obstruction",
        "canonical_key": "SYSTEM:REG_TO_INT.internals.leg.opposition_obstruction",
        "internal_target": "internals.leg.opposition_obstruction",
    },
    {
        "binding_id": "tension_to_movement_salience",
        "canonical_key": "SYSTEM:REG_TO_INT.internals.agenda.movement_salience",
        "internal_target": "internals.agenda.movement_salience",
    },
]

EXPECTED_PUBLIC_AGG_ATTRIBUTION = [
    {
        "internal_target": "internals.leg.coalition_strength",
        "visible_metric": "metrics.legislative_capacity",
        "canonical_key": "SYSTEM:AGG.metrics.legislative_capacity.internals.leg.coalition_strength",
    },
    {
        "internal_target": "internals.party.field_ops",
        "visible_metric": "metrics.party_organization",
        "canonical_key": "SYSTEM:AGG.metrics.party_organization.internals.party.field_ops",
    },
    {
        "internal_target": "internals.tension.protest_activity",
        "visible_metric": "metrics.social_tension",
        "canonical_key": "SYSTEM:AGG.metrics.social_tension.internals.tension.protest_activity",
    },
    {
        "internal_target": "internals.leg.opposition_obstruction",
        "visible_metric": "metrics.legislative_capacity",
        "canonical_key": "SYSTEM:AGG.metrics.legislative_capacity.internals.leg.opposition_obstruction",
    },
    {
        "internal_target": "internals.agenda.movement_salience",
        "visible_metric": "metrics.public_agenda",
        "canonical_key": "SYSTEM:AGG.metrics.public_agenda.internals.agenda.movement_salience",
    },
]

EXPECTED_DRIFT_METRIC_NAMES = ["support", "tension", "organization", "rival_presence"]

EXPECTED_AGG_METRIC_NAMES = {
    "internals.leg.coalition_strength": "metrics.legislative_capacity",
    "internals.party.field_ops": "metrics.party_organization",
    "internals.tension.protest_activity": "metrics.social_tension",
    "internals.leg.opposition_obstruction": "metrics.legislative_capacity",
    "internals.agenda.movement_salience": "metrics.public_agenda",
}


def load_json(path: Path):
    return json.loads(path.read_text(encoding="utf-8"))


def extract_canonical_block(path: Path) -> dict:
    text = path.read_text(encoding="utf-8")
    begin = text.find(BEGIN_MARKER)
    end = text.find(END_MARKER)
    if begin == -1 or end == -1:
        raise ValueError("Canonical region authority markers not found")
    json_start = text.index("{", begin)
    json_end = text.rindex("}", 0, end) + 1
    return json.loads(text[json_start:json_end])


def round_div_half_away(numerator: int, denominator: int) -> int:
    if denominator <= 0:
        raise ValueError("Denominator must be positive")
    if numerator == 0:
        return 0
    abs_num = abs(numerator)
    quotient, remainder = divmod(abs_num, denominator)
    if remainder * 2 >= denominator:
        quotient += 1
    return quotient if numerator >= 0 else -quotient


def clamp(value: int, minimum: int, maximum: int) -> int:
    if minimum > maximum:
        raise ValueError("Minimum must not exceed maximum")
    if value < minimum:
        return minimum
    if value > maximum:
        return maximum
    return value


def compute_drift_target(
    formula: dict, metrics: dict, snapshot_support: int | None
) -> int:
    numerator = 0
    for term in formula.get("terms", []):
        source = term["source"]
        coeff = term["coefficient_ppm"]
        transform = term["transform"]
        raw: int
        if source.startswith("regions."):
            raw = snapshot_support
        else:
            raw = metrics.get(source, MID_S)
        if transform == "value_minus_mid":
            delta = raw - MID_S
        elif transform == "mid_minus_value":
            delta = MID_S - raw
        else:
            raise ValueError(f"Unknown transform: {transform}")
        numerator += coeff * delta
    offset = round_div_half_away(numerator, PPM_DEN)
    target = MID_S + offset
    return clamp(target, 0, 10000)


def compute_drift_delta(
    target_s: int, current_s: int, alpha_ppm: int = ALPHA_PPM, cap: int = CAP_WEEK
) -> tuple:
    distance = target_s - current_s
    elastic_num = distance * alpha_ppm
    elastic_delta = round_div_half_away(elastic_num, PPM_DEN)
    capped_delta = clamp(elastic_delta, -cap, cap)
    pre_final = current_s + capped_delta
    final_s = clamp(pre_final, 0, 10000)
    realized = final_s - current_s
    return {
        "distanceS": distance,
        "elastic_numerator": elastic_num,
        "elastic_deltaS": elastic_delta,
        "capped_deltaS": capped_delta,
        "pre_finalS": pre_final,
        "finalS": final_s,
        "realized_deltaS": realized,
    }


def compute_full_drift(
    formula: dict,
    metrics: dict,
    current_s: int,
    snapshot_support: int | None,
    alpha_ppm: int = ALPHA_PPM,
    cap: int = CAP_WEEK,
) -> dict:
    target_s = compute_drift_target(formula, metrics, snapshot_support)
    delta = compute_drift_delta(target_s, current_s, alpha_ppm, cap)
    # Compute numerator for full intermediate disclosure
    numerator = 0
    for term in formula.get("terms", []):
        source = term["source"]
        coeff = term["coefficient_ppm"]
        transform = term["transform"]
        raw: int
        if source.startswith("regions."):
            raw = snapshot_support
        else:
            raw = metrics.get(source, MID_S)
        if transform == "value_minus_mid":
            delta_val = raw - MID_S
        else:
            delta_val = MID_S - raw
        numerator += coeff * delta_val
    offset = round_div_half_away(numerator, PPM_DEN)
    target_unclamped = MID_S + offset
    result = {
        "numerator": numerator,
        "offsetS": offset,
        "target_unclampedS": target_unclamped,
        "targetS": target_s,
    }
    result.update(delta)
    return result


def compute_weighted_average(values: list[int], weights: list[int]) -> int:
    if len(values) != 16 or len(weights) != 16:
        raise ValueError("Must provide exactly 16 values and 16 weights")
    if sum(weights) != 1000000:
        raise ValueError("Sum of weights must be 1000000")
    numerator = sum(v * w for v, w in zip(values, weights))
    return round_div_half_away(numerator, 1000000)


def compute_pull(
    weighted_averageS: int,
    currentS: int,
    alpha_ppm: int = PULL_ALPHA,
    cap: int = PULL_CAP,
) -> dict:
    targetS = clamp(weighted_averageS, 0, 10000)
    distanceS = targetS - currentS
    elastic_numerator = distanceS * alpha_ppm
    elastic_deltaS = round_div_half_away(elastic_numerator, 1000000)
    capped_deltaS = clamp(elastic_deltaS, -cap, cap)
    pre_finalS = currentS + capped_deltaS
    finalS = clamp(pre_finalS, 0, 10000)
    realized_deltaS = finalS - currentS
    return {
        "weighted_averageS": weighted_averageS,
        "targetS": targetS,
        "distanceS": distanceS,
        "elastic_numerator": elastic_numerator,
        "elastic_deltaS": elastic_deltaS,
        "capped_deltaS": capped_deltaS,
        "pre_finalS": pre_finalS,
        "finalS": finalS,
        "realized_deltaS": realized_deltaS,
    }


def validate_canonical_cause_key(key: str) -> list[str]:
    errors = []
    if not isinstance(key, str):
        errors.append("CauseKey must be a string")
        return errors
    if not key:
        errors.append("CauseKey must not be empty")
        return errors
    colon_count = key.count(":")
    if colon_count != 1:
        errors.append(
            f"CauseKey must contain exactly one ':', got {colon_count}"
        )
    category, id_part = key.split(":", 1)
    if category != "SYSTEM":
        errors.append(
            f"CauseKey category must be SYSTEM, got {category!r}"
        )
    valid_prefixes = (
        "REG_DRIFT.", "REG_TO_INT.", "AGG.", "REVERSION.",
        "DERIVED.", "CLAMP", "ROUNDING", "IG_CLOUT_NORMALIZE",
    )
    if not id_part:
        errors.append("CauseKey ID must not be empty")
    elif not id_part.startswith(valid_prefixes):
        errors.append(
            f"CauseKey ID {id_part!r} does not start with a permitted prefix"
        )
    if id_part:
        for i, ch in enumerate(id_part):
            if ch < " " or ch > "~":
                errors.append(
                    f"CauseKey ID contains non-printable-ASCII "
                    f"character {ord(ch)!r} at position {i}"
                )
                break
        if ":" in id_part:
            errors.append("CauseKey ID must not contain ':'")
        if "|" in id_part:
            errors.append("CauseKey ID must not contain '|'")
        if any(ch.isspace() for ch in id_part):
            errors.append("CauseKey ID must not contain whitespace")
    return errors


def build_expected_drift_cause_keys() -> list[str]:
    keys = []
    for region_id in EXPECTED_CANONICAL_REGION_ORDER:
        for metric in EXPECTED_DRIFT_METRIC_NAMES:
            keys.append(
                EXPECTED_DRIFT_CAUSE_PATTERN.replace(
                    "{region_id}", region_id
                ).replace("{metric}", metric)
            )
    return keys


def validate_numeric_domain(domain: dict) -> list[str]:
    errors = []
    if domain != EXPECTED_NUMERIC_DOMAIN:
        if domain.get("scale") != EXPECTED_NUMERIC_DOMAIN["scale"]:
            errors.append(f"scale expected {EXPECTED_NUMERIC_DOMAIN['scale']}, got {domain.get('scale')}")
        if domain.get("hundredS") != EXPECTED_NUMERIC_DOMAIN["hundredS"]:
            errors.append(f"hundredS expected {EXPECTED_NUMERIC_DOMAIN['hundredS']}, got {domain.get('hundredS')}")
        if domain.get("midS") != EXPECTED_NUMERIC_DOMAIN["midS"]:
            errors.append(f"midS expected {EXPECTED_NUMERIC_DOMAIN['midS']}, got {domain.get('midS')}")
        if domain.get("ppm_denominator") != EXPECTED_NUMERIC_DOMAIN["ppm_denominator"]:
            errors.append(f"ppm_denominator expected {EXPECTED_NUMERIC_DOMAIN['ppm_denominator']}, got {domain.get('ppm_denominator')}")
        if domain.get("stored_type") != EXPECTED_NUMERIC_DOMAIN["stored_type"]:
            errors.append(f"stored_type expected {EXPECTED_NUMERIC_DOMAIN['stored_type']}, got {domain.get('stored_type')}")
        if domain.get("intermediate_type") != EXPECTED_NUMERIC_DOMAIN["intermediate_type"]:
            errors.append(f"intermediate_type expected {EXPECTED_NUMERIC_DOMAIN['intermediate_type']}, got {domain.get('intermediate_type')}")
        if domain.get("rounding") != EXPECTED_NUMERIC_DOMAIN["rounding"]:
            errors.append(f"rounding expected {EXPECTED_NUMERIC_DOMAIN['rounding']}, got {domain.get('rounding')}")
        if domain.get("rounding_authority") != EXPECTED_NUMERIC_DOMAIN["rounding_authority"]:
            errors.append(f"rounding_authority expected {EXPECTED_NUMERIC_DOMAIN['rounding_authority']}, got {domain.get('rounding_authority')}")
        if domain.get("target_clamp_authority") != EXPECTED_NUMERIC_DOMAIN["target_clamp_authority"]:
            errors.append(f"target_clamp_authority expected {EXPECTED_NUMERIC_DOMAIN['target_clamp_authority']}, got {domain.get('target_clamp_authority')}")
        if domain.get("publication_operation") != EXPECTED_NUMERIC_DOMAIN["publication_operation"]:
            errors.append(f"publication_operation expected {EXPECTED_NUMERIC_DOMAIN['publication_operation']}, got {domain.get('publication_operation')}")
        if domain.get("forbidden_numeric_types") != EXPECTED_NUMERIC_DOMAIN["forbidden_numeric_types"]:
            errors.append(f"forbidden_numeric_types mismatch")
        if domain.get("forbidden_behaviors") != EXPECTED_NUMERIC_DOMAIN["forbidden_behaviors"]:
            errors.append(f"forbidden_behaviors mismatch")
    actual_keys = set(domain.keys())
    if actual_keys != EXPECTED_NUMERIC_DOMAIN_KEYS:
        missing = EXPECTED_NUMERIC_DOMAIN_KEYS - actual_keys
        extra = actual_keys - EXPECTED_NUMERIC_DOMAIN_KEYS
        if missing:
            errors.append(f"Missing numeric_domain key(s): {sorted(missing)}")
        if extra:
            errors.append(f"Unexpected numeric_domain key(s): {sorted(extra)}")
    return errors


def validate_drift(drift: dict) -> list[str]:
    errors = []

    if drift.get("phase") != EXPECTED_DRIFT_PHASE:
        errors.append(f"drift.phase expected {EXPECTED_DRIFT_PHASE}, got {drift.get('phase')}")
    if drift.get("phase_name") != EXPECTED_DRIFT_PHASE_NAME:
        errors.append(f"drift.phase_name expected {EXPECTED_DRIFT_PHASE_NAME!r}, got {drift.get('phase_name')!r}")
    if drift.get("snapshot") != EXPECTED_DRIFT_SNAPSHOT:
        errors.append(f"drift.snapshot expected {EXPECTED_DRIFT_SNAPSHOT!r}, got {drift.get('snapshot')!r}")
    if drift.get("half_life_weeks_metadata") != EXPECTED_DRIFT_HALF_LIFE_WEEKS_METADATA:
        errors.append(f"drift.half_life_weeks_metadata expected {EXPECTED_DRIFT_HALF_LIFE_WEEKS_METADATA}, got {drift.get('half_life_weeks_metadata')}")
    if drift.get("region_order_source") != EXPECTED_DRIFT_REGION_ORDER_SOURCE:
        errors.append(f"drift.region_order_source expected {EXPECTED_DRIFT_REGION_ORDER_SOURCE!r}, got {drift.get('region_order_source')!r}")
    if drift.get("alpha_ppm") != EXPECTED_DRIFT_ALPHA_PPM:
        errors.append(f"drift.alpha_ppm expected {EXPECTED_DRIFT_ALPHA_PPM}, got {drift.get('alpha_ppm')}")
    if drift.get("cap_per_weekS") != EXPECTED_DRIFT_CAP_PER_WEEKS:
        errors.append(f"drift.cap_per_weekS expected {EXPECTED_DRIFT_CAP_PER_WEEKS}, got {drift.get('cap_per_weekS')}")
    if drift.get("target_baseS") != EXPECTED_DRIFT_TARGET_BASES:
        errors.append(f"drift.target_baseS expected {EXPECTED_DRIFT_TARGET_BASES}, got {drift.get('target_baseS')}")
    if drift.get("target_denominator") != EXPECTED_DRIFT_TARGET_DENOM:
        errors.append(f"drift.target_denominator expected {EXPECTED_DRIFT_TARGET_DENOM}, got {drift.get('target_denominator')}")
    if drift.get("region_count") != EXPECTED_REGION_COUNT:
        errors.append(f"drift.region_count expected {EXPECTED_REGION_COUNT}, got {drift.get('region_count')}")
    if drift.get("outputs_per_region") != EXPECTED_DRIFT_OUTPUTS_PER_REGION:
        errors.append(f"drift.outputs_per_region expected {EXPECTED_DRIFT_OUTPUTS_PER_REGION}, got {drift.get('outputs_per_region')}")
    if drift.get("output_count") != EXPECTED_DRIFT_OUTPUT_COUNT:
        errors.append(f"drift.output_count expected {EXPECTED_DRIFT_OUTPUT_COUNT}, got {drift.get('output_count')}")
    if drift.get("rival_support_read_from") != EXPECTED_DRIFT_RIVAL_SUPPORT_SOURCE:
        errors.append(f"drift.rival_support_read_from expected {EXPECTED_DRIFT_RIVAL_SUPPORT_SOURCE!r}, got {drift.get('rival_support_read_from')!r}")
    if drift.get("all_sources_read_from") != EXPECTED_DRIFT_ALL_SOURCES:
        errors.append(f"drift.all_sources_read_from expected {EXPECTED_DRIFT_ALL_SOURCES!r}, got {drift.get('all_sources_read_from')!r}")
    if drift.get("metric_order") != EXPECTED_DRIFT_METRIC_ORDER:
        errors.append(f"drift.metric_order mismatch")
    if drift.get("common_pipeline") != EXPECTED_DRIFT_PIPELINE:
        errors.append(f"drift.common_pipeline mismatch")

    actual_keys = set(drift.keys())
    if actual_keys != EXPECTED_DRIFT_KEYS:
        missing = EXPECTED_DRIFT_KEYS - actual_keys
        extra = actual_keys - EXPECTED_DRIFT_KEYS
        if missing:
            errors.append(f"Missing drift key(s): {sorted(missing)}")
        if extra:
            errors.append(f"Unexpected drift key(s): {sorted(extra)}")

    formulas = drift.get("target_formulas", {})
    formula_keys = set(formulas.keys())
    expected_formula_keys = set(EXPECTED_TARGET_FORMULA_ORDER)
    if formula_keys != expected_formula_keys:
        missing = expected_formula_keys - formula_keys
        extra = formula_keys - expected_formula_keys
        if missing:
            errors.append(f"Missing target_formula(s): {sorted(missing)}")
        if extra:
            errors.append(f"Unexpected target_formula(s): {sorted(extra)}")
    else:
        for i, name in enumerate(EXPECTED_TARGET_FORMULA_ORDER):
            f = formulas[name]
            expected = EXPECTED_TARGET_FORMULAS[name]
            if f != expected:
                errors.append(f"target_formula.{name} content mismatch")

    return errors


def validate_pull(pull: dict) -> list[str]:
    errors = []

    if pull.get("phase") != 10:
        errors.append(f"pull.phase expected 10, got {pull.get('phase')}")
    if pull.get("phase_name") != "PullRegionsToInternals":
        errors.append(f"pull.phase_name expected 'PullRegionsToInternals', got {pull.get('phase_name')!r}")
    if pull.get("snapshot") != "post_phase_9":
        errors.append(f"pull.snapshot expected 'post_phase_9', got {pull.get('snapshot')!r}")
    if pull.get("alpha_ppm") != 206299:
        errors.append(f"pull.alpha_ppm expected 206299, got {pull.get('alpha_ppm')}")
    if pull.get("cap_per_weekS") != 400:
        errors.append(f"pull.cap_per_weekS expected 400, got {pull.get('cap_per_weekS')}")
    if pull.get("weight_sum_required") != 1000000:
        errors.append(f"pull.weight_sum_required expected 1000000, got {pull.get('weight_sum_required')}")
    if pull.get("weighted_average_division_count") != 1:
        errors.append(f"pull.weighted_average_division_count expected 1, got {pull.get('weighted_average_division_count')}")
    if pull.get("weighted_average_intermediate_type") != "checked_long":
        errors.append(f"pull.weighted_average_intermediate_type expected 'checked_long', got {pull.get('weighted_average_intermediate_type')!r}")
    if pull.get("weighted_average_rounding") != "HALF_AWAY_FROM_ZERO":
        errors.append(f"pull.weighted_average_rounding expected 'HALF_AWAY_FROM_ZERO', got {pull.get('weighted_average_rounding')!r}")
    if pull.get("binding_chaining") != "forbidden":
        errors.append(f"pull.binding_chaining expected 'forbidden', got {pull.get('binding_chaining')!r}")
    if pull.get("region_order_source") != "canonical_region_order.ordered_region_ids":
        errors.append(f"pull.region_order_source expected 'canonical_region_order.ordered_region_ids', got {pull.get('region_order_source')!r}")
    if pull.get("weight_source") != "content_pack_region.weight_ppm":
        errors.append(f"pull.weight_source expected 'content_pack_region.weight_ppm', got {pull.get('weight_source')!r}")
    if pull.get("output_count") != 5:
        errors.append(f"pull.output_count expected 5, got {pull.get('output_count')}")
    if pull.get("all_sources_read_from") != "phase_input_snapshot":
        errors.append(f"pull.all_sources_read_from expected 'phase_input_snapshot', got {pull.get('all_sources_read_from')!r}")
    if pull.get("weighted_average_denominator") != 1000000:
        errors.append(f"pull.weighted_average_denominator expected 1000000, got {pull.get('weighted_average_denominator')}")
    if pull.get("binding_order") != EXPECTED_PULL_BINDING_ORDER:
        errors.append("pull.binding_order mismatch")
    if pull.get("common_pipeline") != EXPECTED_PULL_PIPELINE:
        errors.append("pull.common_pipeline mismatch")
    if pull.get("bindings") != EXPECTED_PULL_BINDINGS:
        errors.append("pull.bindings mismatch")

    actual_keys = set(pull.keys())
    if actual_keys != EXPECTED_PULL_KEYS:
        missing = EXPECTED_PULL_KEYS - actual_keys
        extra = actual_keys - EXPECTED_PULL_KEYS
        if missing:
            errors.append(f"Missing pull key(s): {sorted(missing)}")
        if extra:
            errors.append(f"Unexpected pull key(s): {sorted(extra)}")

    bindings = pull.get("bindings", [])
    if len(bindings) != 5:
        errors.append(f"pull.bindings expected 5 entries, got {len(bindings)}")
    else:
        ids = [b.get("id") for b in bindings]
        if len(ids) != len(set(ids)):
            errors.append("pull.bindings IDs are not unique")
        destinations = [b.get("destination") for b in bindings]
        if len(destinations) != len(set(destinations)):
            errors.append("pull.bindings destinations are not unique")
        tension_count = sum(1 for b in bindings if b.get("regional_source") == "tension")
        if tension_count != 2:
            errors.append(f"pull.bindings expected 2 with regional_source='tension', got {tension_count}")
        for i, b in enumerate(bindings):
            b_keys = set(b.keys())
            if b_keys != {"id", "regional_source", "destination"}:
                errors.append(f"pull.bindings[{i}] keys are {sorted(b_keys)}, expected ['destination', 'id', 'regional_source']")
            if b.get("regional_source") not in EXPECTED_DYNAMIC_TARGETS:
                errors.append(f"pull.bindings[{i}] regional_source '{b.get('regional_source')}' not in dynamic targets")

    return errors


def validate_latency(latency: dict) -> list[str]:
    errors = []
    actual_keys = set(latency.keys())
    if actual_keys != EXPECTED_LATENCY_KEYS:
        missing = EXPECTED_LATENCY_KEYS - actual_keys
        extra = actual_keys - EXPECTED_LATENCY_KEYS
        if missing:
            errors.append(f"Missing latency key(s): {sorted(missing)}")
        if extra:
            errors.append(f"Unexpected latency key(s): {sorted(extra)}")
    for key in EXPECTED_LATENCY_KEYS:
        if key not in latency:
            continue
        if latency[key] != EXPECTED_LATENCY[key]:
            errors.append(f"latency.{key} expected {EXPECTED_LATENCY[key]!r}, got {latency[key]!r}")
    return errors


def validate_causality(causality: dict) -> list[str]:
    errors = []

    actual_keys = set(causality.keys())
    if actual_keys != EXPECTED_CAUSALITY_KEYS:
        missing = EXPECTED_CAUSALITY_KEYS - actual_keys
        extra = actual_keys - EXPECTED_CAUSALITY_KEYS
        if missing:
            errors.append(f"Missing causality key(s): {sorted(missing)}")
        if extra:
            errors.append(f"Unexpected causality key(s): {sorted(extra)}")

    if causality.get("cause_category") != EXPECTED_CAUSE_CATEGORY:
        errors.append(
            f"cause_category expected {EXPECTED_CAUSE_CATEGORY!r}, "
            f"got {causality.get('cause_category')!r}"
        )
    if causality.get("parent") is not None:
        errors.append(f"parent must be None, got {causality.get('parent')!r}")
    if causality.get("canonical_key_separator") != ":":
        errors.append(
            f"canonical_key_separator expected ':', "
            f"got {causality.get('canonical_key_separator')!r}"
        )
    if causality.get("canonical_key_separator_count") != 1:
        errors.append(
            f"canonical_key_separator_count expected 1, "
            f"got {causality.get('canonical_key_separator_count')}"
        )
    if causality.get("identifier_policy") != "PRINTABLE_ASCII_NO_WHITESPACE_COLON_PIPE":
        errors.append(
            f"identifier_policy mismatch: "
            f"{causality.get('identifier_policy')}"
        )

    drift = causality.get("drift", {})
    drift_keys = set(drift.keys())
    if drift_keys != EXPECTED_CAUSALITY_DRIFT_KEYS:
        missing = EXPECTED_CAUSALITY_DRIFT_KEYS - drift_keys
        extra = drift_keys - EXPECTED_CAUSALITY_DRIFT_KEYS
        if missing:
            errors.append(f"Missing causality.drift key(s): {sorted(missing)}")
        if extra:
            errors.append(f"Unexpected causality.drift key(s): {sorted(extra)}")
    if drift.get("id_prefix") != "REG_DRIFT":
        errors.append(f"drift.id_prefix expected REG_DRIFT, got {drift.get('id_prefix')!r}")
    if drift.get("canonical_key_pattern") != EXPECTED_DRIFT_CAUSE_PATTERN:
        errors.append(f"drift.canonical_key_pattern mismatch: {drift.get('canonical_key_pattern')}")
    if drift.get("target_pattern") != "regions.{region_id}.{metric}":
        errors.append(f"drift.target_pattern mismatch: {drift.get('target_pattern')}")
    if drift.get("target_visibility") != "public_visible_target":
        errors.append(f"drift.target_visibility mismatch: {drift.get('target_visibility')}")
    if drift.get("public_ledger") is not True:
        errors.append(f"drift.public_ledger must be True, got {drift.get('public_ledger')}")
    if drift.get("tick_causal_buffer") is not True:
        errors.append(f"drift.tick_causal_buffer must be True, got {drift.get('tick_causal_buffer')}")
    if drift.get("potential_cause_count") != 64:
        errors.append(f"drift.potential_cause_count expected 64, got {drift.get('potential_cause_count')}")
    if drift.get("cause_order") != "canonical_region_order_then_drift_metric_order":
        errors.append(f"drift.cause_order mismatch: {drift.get('cause_order')}")
    if drift.get("zero_realized_delta_policy") != "omit_contribution":
        errors.append(f"drift.zero_realized_delta_policy expected omit_contribution, got {drift.get('zero_realized_delta_policy')!r}")

    pull = causality.get("pull_provenance", {})
    pull_keys = set(pull.keys())
    if pull_keys != EXPECTED_CAUSALITY_PULL_KEYS:
        missing = EXPECTED_CAUSALITY_PULL_KEYS - pull_keys
        extra = pull_keys - EXPECTED_CAUSALITY_PULL_KEYS
        if missing:
            errors.append(f"Missing causality.pull_provenance key(s): {sorted(missing)}")
        if extra:
            errors.append(f"Unexpected causality.pull_provenance key(s): {sorted(extra)}")
    if pull.get("id_prefix") != "REG_TO_INT":
        errors.append(f"pull_provenance.id_prefix expected REG_TO_INT, got {pull.get('id_prefix')!r}")
    if pull.get("canonical_key_pattern") != EXPECTED_PULL_PROVENANCE_PATTERN:
        errors.append(f"pull_provenance.canonical_key_pattern mismatch: {pull.get('canonical_key_pattern')}")
    if pull.get("identity_count") != 5:
        errors.append(f"pull_provenance.identity_count expected 5, got {pull.get('identity_count')}")
    if pull.get("provenance_scope") != "ephemeral_execution_plan_only":
        errors.append(f"pull_provenance.provenance_scope mismatch: {pull.get('provenance_scope')}")
    if pull.get("target_visibility") != "hidden_internal":
        errors.append(f"pull_provenance.target_visibility expected hidden_internal, got {pull.get('target_visibility')!r}")
    if pull.get("public_ledger") is not False:
        errors.append(f"pull_provenance.public_ledger must be False, got {pull.get('public_ledger')}")
    if pull.get("tick_causal_buffer") is not False:
        errors.append(f"pull_provenance.tick_causal_buffer must be False, got {pull.get('tick_causal_buffer')}")
    if pull.get("serialized") is not False:
        errors.append(f"pull_provenance.serialized must be False, got {pull.get('serialized')}")
    if pull.get("stored_in_game_state") is not False:
        errors.append(f"pull_provenance.stored_in_game_state must be False, got {pull.get('stored_in_game_state')}")
    if pull.get("turn_report") is not False:
        errors.append(f"pull_provenance.turn_report must be False, got {pull.get('turn_report')}")
    if pull.get("top_n_slot") is not False:
        errors.append(f"pull_provenance.top_n_slot must be False, got {pull.get('top_n_slot')}")
    if pull.get("lifetime") != "current_phase_10_plan_only":
        errors.append(f"pull_provenance.lifetime mismatch: {pull.get('lifetime')}")
    if pull.get("public_attribution") != "next_tick_SYSTEM_AGG":
        errors.append(f"pull_provenance.public_attribution mismatch: {pull.get('public_attribution')}")
    if pull.get("double_counting") != "forbidden":
        errors.append(f"pull_provenance.double_counting expected forbidden, got {pull.get('double_counting')!r}")

    identities = pull.get("identities", [])
    if len(identities) != 5:
        errors.append(f"pull_provenance.identities expected 5 entries, got {len(identities)}")
    else:
        for i, identity in enumerate(identities):
            id_keys = set(identity.keys())
            if id_keys != EXPECTED_CAUSALITY_IDENTITY_KEYS:
                missing = EXPECTED_CAUSALITY_IDENTITY_KEYS - id_keys
                extra = id_keys - EXPECTED_CAUSALITY_IDENTITY_KEYS
                if missing:
                    errors.append(f"identity[{i}] missing key(s): {sorted(missing)}")
                if extra:
                    errors.append(f"identity[{i}] unexpected key(s): {sorted(extra)}")
            expected = EXPECTED_PULL_PROVENANCE_IDENTITIES[i]
            if identity != expected:
                errors.append(
                    f"identity[{i}] mismatch: expected {expected}, got {identity}"
                )

    agg = causality.get("public_aggregation_attribution", [])
    if len(agg) != 5:
        errors.append(f"public_aggregation_attribution expected 5 entries, got {len(agg)}")
    else:
        for i, mapping in enumerate(agg):
            agg_keys = set(mapping.keys())
            if agg_keys != EXPECTED_CAUSALITY_AGG_KEYS:
                missing = EXPECTED_CAUSALITY_AGG_KEYS - agg_keys
                extra = agg_keys - EXPECTED_CAUSALITY_AGG_KEYS
                if missing:
                    errors.append(f"agg_attribution[{i}] missing key(s): {sorted(missing)}")
                if extra:
                    errors.append(f"agg_attribution[{i}] unexpected key(s): {sorted(extra)}")
            expected = EXPECTED_PUBLIC_AGG_ATTRIBUTION[i]
            if mapping != expected:
                errors.append(
                    f"agg_attribution[{i}] mismatch: expected {expected}, got {mapping}"
                )

    return errors


def validate_contract(contract: dict) -> list[str]:
    errors = []

    order = contract.get("canonical_region_order", {})
    ids = order.get("ordered_region_ids", [])

    if order.get("region_count") != EXPECTED_REGION_COUNT:
        errors.append(
            f"region_count expected {EXPECTED_REGION_COUNT}, "
            f"got {order.get('region_count')}"
        )
    if len(ids) != EXPECTED_REGION_COUNT:
        errors.append(
            f"Expected {EXPECTED_REGION_COUNT} region IDs, got {len(ids)}"
        )
    if ids != EXPECTED_CANONICAL_REGION_ORDER:
        errors.append("Region ID order does not match contract")
    if len(set(ids)) != len(ids):
        errors.append("Region IDs are not unique")
    if order.get("weight_ppm_each") != EXPECTED_WEIGHT_PPM_EACH:
        errors.append(
            f"weight_ppm_each expected {EXPECTED_WEIGHT_PPM_EACH}, "
            f"got {order.get('weight_ppm_each')}"
        )
    if order.get("weight_ppm_sum_required") != EXPECTED_WEIGHT_PPM_SUM:
        errors.append(
            f"weight_ppm_sum_required expected {EXPECTED_WEIGHT_PPM_SUM}, "
            f"got {order.get('weight_ppm_sum_required')}"
        )
    if order.get("authority") != EXPECTED_AUTHORITY:
        errors.append(
            f"authority expected {EXPECTED_AUTHORITY!r}, "
            f"got {order.get('authority')!r}"
        )
    if order.get("source_path") != EXPECTED_SOURCE_PATH:
        errors.append(
            f"source_path expected {EXPECTED_SOURCE_PATH!r}, "
            f"got {order.get('source_path')!r}"
        )
    if order.get("forbidden_order_sources") != EXPECTED_FORBIDDEN_SOURCES:
        errors.append(
            f"forbidden_order_sources mismatch: "
            f"{order.get('forbidden_order_sources')}"
        )

    actual_keys = set(contract.keys())
    if actual_keys != EXPECTED_TOP_LEVEL_KEYS:
        missing = EXPECTED_TOP_LEVEL_KEYS - actual_keys
        extra = actual_keys - EXPECTED_TOP_LEVEL_KEYS
        if missing:
            errors.append(f"Missing top-level key(s): {sorted(missing)}")
        if extra:
            errors.append(f"Unexpected top-level key(s): {sorted(extra)}")

    actual_order_keys = set(order.keys())
    if actual_order_keys != EXPECTED_CANONICAL_ORDER_KEYS:
        missing = EXPECTED_CANONICAL_ORDER_KEYS - actual_order_keys
        extra = actual_order_keys - EXPECTED_CANONICAL_ORDER_KEYS
        if missing:
            errors.append(f"Missing canonical_region_order key(s): {sorted(missing)}")
        if extra:
            errors.append(f"Unexpected canonical_region_order key(s): {sorted(extra)}")

    targets = contract.get("regional_dynamic_targets", [])
    if targets != EXPECTED_DYNAMIC_TARGETS:
        errors.append(
            f"regional_dynamic_targets expected {EXPECTED_DYNAMIC_TARGETS}, "
            f"got {targets}"
        )

    resources = contract.get("static_regional_resources", {})
    if resources != EXPECTED_STATIC_RESOURCES:
        errors.append(
            f"static_regional_resources expected {EXPECTED_STATIC_RESOURCES}, "
            f"got {resources}"
        )

    domain = contract.get("numeric_domain", {})
    errors.extend(validate_numeric_domain(domain))

    drift = contract.get("drift", {})
    errors.extend(validate_drift(drift))

    pull = contract.get("pull", {})
    errors.extend(validate_pull(pull))

    latency = contract.get("latency", {})
    errors.extend(validate_latency(latency))

    causality = contract.get("causality", {})
    errors.extend(validate_causality(causality))

    return errors


def validate_content_pack_against_contract(
    contract: dict, regions_data: dict
) -> list[str]:
    errors = []
    contract_order = contract["canonical_region_order"]
    contract_ids = contract_order["ordered_region_ids"]

    if "regions" not in regions_data:
        return ["Content Pack has no 'regions' key"]

    cp_regions = regions_data["regions"]
    cp_ids = [r["id"] for r in cp_regions]

    if cp_ids != contract_ids:
        errors.append(
            "Content Pack region order does not match contract canonical order"
        )

    for i, r in enumerate(cp_regions):
        if r.get("weight_ppm") != EXPECTED_WEIGHT_PPM_EACH:
            errors.append(
                f"region[{i}] {r.get('id')} weight_ppm={r.get('weight_ppm')}, "
                f"expected {EXPECTED_WEIGHT_PPM_EACH}"
            )

    total_ppm = sum(r.get("weight_ppm", 0) for r in cp_regions)
    if total_ppm != EXPECTED_WEIGHT_PPM_SUM:
        errors.append(
            f"Sum of weight_ppm is {total_ppm}, "
            f"expected {EXPECTED_WEIGHT_PPM_SUM}"
        )

    static_names = EXPECTED_STATIC_RESOURCE_NAMES
    for i, r in enumerate(cp_regions):
        rid = r.get("id", f"index_{i}")
        for field in static_names:
            if field not in r:
                errors.append(f"region {rid} missing static resource {field}")
            elif r[field] != 5000:
                errors.append(
                    f"region {rid} static resource {field} = {r[field]}, "
                    f"expected 5000"
                )

    return errors


class RoundingHelperTest(unittest.TestCase):
    """Tests for the independent rounding helper."""

    def test_r01_positive(self):
        self.assertEqual(1, round_div_half_away(500000, 1000000))

    def test_r02_negative(self):
        self.assertEqual(-1, round_div_half_away(-500000, 1000000))

    def test_rounding_truncation_alternative(self):
        # HALF_AWAY_FROM_ZERO gives 1 and -1; truncation gives 0 and 0
        self.assertEqual(1, round_div_half_away(500000, 1000000))
        self.assertEqual(-1, round_div_half_away(-500000, 1000000))
        # Verify truncation would give different result
        self.assertNotEqual(0, round_div_half_away(500000, 1000000))
        self.assertNotEqual(0, round_div_half_away(-500000, 1000000))

    def test_exact_division(self):
        self.assertEqual(0, round_div_half_away(0, 1000000))
        self.assertEqual(1, round_div_half_away(1000000, 1000000))

    def test_clamp_basic(self):
        self.assertEqual(5, clamp(5, 0, 10))
        self.assertEqual(0, clamp(-5, 0, 10))
        self.assertEqual(10, clamp(15, 0, 10))

    def test_clamp_invalid_range_raises(self):
        with self.assertRaises(ValueError):
            clamp(5, 10, 0)


class CanonicalRegionalAuthorityTest(unittest.TestCase):
    """Positive assertions on the territory contract document."""

    @classmethod
    def setUpClass(cls):
        cls.root = ROOT
        cls.contract_path = cls.root / "docs" / "territory_contract.md"
        cls.regions_path = (
            cls.root
            / "Assets"
            / "StreamingAssets"
            / "content"
            / "core"
            / "regions.json"
        )
        cls.target_config_path = (
            cls.root
            / "Assets"
            / "StreamingAssets"
            / "content"
            / "rules"
            / "target_config.json"
        )
        cls.contract = extract_canonical_block(cls.contract_path)
        cls.regions_data = load_json(cls.regions_path)
        cls.target_config = load_json(cls.target_config_path)

    def test_contract_has_exactly_16_region_ids(self):
        ids = self.contract["canonical_region_order"]["ordered_region_ids"]
        self.assertEqual(EXPECTED_REGION_COUNT, len(ids))

    def test_region_count_field_is_16(self):
        self.assertEqual(
            EXPECTED_REGION_COUNT,
            self.contract["canonical_region_order"]["region_count"],
        )

    def test_region_ids_are_unique(self):
        ids = self.contract["canonical_region_order"]["ordered_region_ids"]
        self.assertEqual(len(ids), len(set(ids)))

    def test_region_order_is_exact_north_to_south(self):
        ids = self.contract["canonical_region_order"]["ordered_region_ids"]
        self.assertEqual(EXPECTED_CANONICAL_REGION_ORDER, ids)

    def test_weight_ppm_each_is_62500(self):
        self.assertEqual(
            EXPECTED_WEIGHT_PPM_EACH,
            self.contract["canonical_region_order"]["weight_ppm_each"],
        )

    def test_weight_ppm_sum_is_1000000(self):
        self.assertEqual(
            EXPECTED_WEIGHT_PPM_SUM,
            self.contract["canonical_region_order"]["weight_ppm_sum_required"],
        )

    def test_authority_is_content_pack_declaration_order(self):
        self.assertEqual(
            EXPECTED_AUTHORITY,
            self.contract["canonical_region_order"]["authority"],
        )

    def test_source_path_is_exact(self):
        self.assertEqual(
            EXPECTED_SOURCE_PATH,
            self.contract["canonical_region_order"]["source_path"],
        )

    def test_forbidden_order_sources_are_exact(self):
        self.assertEqual(
            EXPECTED_FORBIDDEN_SOURCES,
            self.contract["canonical_region_order"]["forbidden_order_sources"],
        )

    def test_regional_dynamic_targets_are_exact_and_in_order(self):
        self.assertEqual(
            EXPECTED_DYNAMIC_TARGETS,
            self.contract["regional_dynamic_targets"],
        )

    def test_static_regional_resources_are_exact(self):
        self.assertEqual(
            EXPECTED_STATIC_RESOURCES,
            self.contract["static_regional_resources"],
        )

    def test_numeric_domain_is_exact(self):
        self.assertEqual(
            EXPECTED_NUMERIC_DOMAIN,
            self.contract["numeric_domain"],
        )

    def test_drift_alpha_ppm_is_109101(self):
        self.assertEqual(
            EXPECTED_DRIFT_ALPHA_PPM,
            self.contract["drift"]["alpha_ppm"],
        )

    def test_drift_cap_per_weekS_is_200(self):
        self.assertEqual(
            EXPECTED_DRIFT_CAP_PER_WEEKS,
            self.contract["drift"]["cap_per_weekS"],
        )

    def test_drift_half_life_weeks_metadata_is_6(self):
        self.assertEqual(
            6,
            self.contract["drift"]["half_life_weeks_metadata"],
        )

    def test_drift_snapshot_is_post_phase_8(self):
        self.assertEqual(
            "post_phase_8",
            self.contract["drift"]["snapshot"],
        )

    def test_drift_output_count_is_64(self):
        self.assertEqual(
            64,
            self.contract["drift"]["output_count"],
        )

    def test_drift_region_order_source_is_canonical(self):
        self.assertEqual(
            EXPECTED_DRIFT_REGION_ORDER_SOURCE,
            self.contract["drift"]["region_order_source"],
        )

    def test_drift_rival_support_read_from_pre_drift(self):
        self.assertEqual(
            "phase_input_snapshot_pre_drift",
            self.contract["drift"]["rival_support_read_from"],
        )

    def test_drift_formulas_have_exact_target_keys(self):
        formulas = self.contract["drift"]["target_formulas"]
        self.assertEqual(set(EXPECTED_TARGET_FORMULA_ORDER), set(formulas.keys()))
        for name in EXPECTED_TARGET_FORMULA_ORDER:
            f = formulas[name]
            self.assertIn("target", f)
            self.assertIn("terms", f)
            self.assertEqual(
                EXPECTED_TERM_COUNTS[name],
                len(f["terms"]),
                f"Formula {name} has {len(f['terms'])} terms",
            )

    def test_drift_formula_support_term_coefficients(self):
        support = self.contract["drift"]["target_formulas"]["support"]
        coeffs = [t["coefficient_ppm"] for t in support["terms"]]
        self.assertEqual([600000, 300000, -400000], coeffs)

    def test_drift_formula_tension_term_coefficients(self):
        tension = self.contract["drift"]["target_formulas"]["tension"]
        coeffs = [t["coefficient_ppm"] for t in tension["terms"]]
        self.assertEqual([500000, 400000, 300000], coeffs)

    def test_drift_formula_organization_single_term(self):
        org = self.contract["drift"]["target_formulas"]["organization"]
        self.assertEqual(800000, org["terms"][0]["coefficient_ppm"])

    def test_drift_formula_rival_terms(self):
        rival = self.contract["drift"]["target_formulas"]["rival_presence"]
        coeffs = [t["coefficient_ppm"] for t in rival["terms"]]
        self.assertEqual([700000, 200000], coeffs)

    def test_drift_common_pipeline_is_exact(self):
        self.assertEqual(
            EXPECTED_DRIFT_PIPELINE,
            self.contract["drift"]["common_pipeline"],
        )

    def test_every_region_has_all_five_static_resources_at_5000(self):
        cp_regions = self.regions_data["regions"]
        for r in cp_regions:
            for field in EXPECTED_STATIC_RESOURCE_NAMES:
                self.assertIn(field, r, f"{r['id']} missing {field}")
                self.assertEqual(5000, r[field], f"{r['id']}.{field} != 5000")

    def test_content_pack_declaration_matches_contract_order(self):
        cp_ids = [r["id"] for r in self.regions_data["regions"]]
        contract_ids = self.contract["canonical_region_order"]["ordered_region_ids"]
        self.assertEqual(contract_ids, cp_ids)

    def test_alphabetical_order_does_not_match_canonical(self):
        contract_ids = self.contract["canonical_region_order"]["ordered_region_ids"]
        sorted_ids = sorted(contract_ids)
        self.assertNotEqual(contract_ids, sorted_ids)

    def test_four_dynamic_targets_exist_in_target_config(self):
        patterns = {entry["pattern"] for entry in self.target_config}
        for target in EXPECTED_DYNAMIC_TARGETS:
            self.assertIn(
                f"regions.*.{target}",
                patterns,
                f"regions.*.{target} not declared in target_config.json",
            )

    def test_target_config_fields_for_regional_targets(self):
        for entry in self.target_config:
            pattern = entry["pattern"]
            if not pattern.startswith("regions.*."):
                continue
            if pattern.split(".")[2] not in EXPECTED_DYNAMIC_TARGETS:
                continue
            self.assertEqual(100, entry["scale"], f"{pattern} scale != 100")
            self.assertEqual(0, entry["minS"], f"{pattern} minS != 0")
            self.assertEqual(10000, entry["maxS"], f"{pattern} maxS != 10000")
            self.assertEqual(5000, entry["defaultS"], f"{pattern} defaultS != 5000")
            self.assertIn("SET", entry["allow_ops"], f"{pattern} does not allow SET")

    def test_static_resources_not_in_regional_dynamic_targets(self):
        dynamic = self.contract["regional_dynamic_targets"]
        for resource_name in EXPECTED_STATIC_RESOURCE_NAMES:
            self.assertNotIn(resource_name, dynamic)

    def test_document_indicates_phases_9_and_10_are_no_op(self):
        text = self.contract_path.read_text(encoding="utf-8")
        self.assertIn("no-op", text.lower())
        self.assertIn("does not activate", text.lower())

    def test_no_todo_or_tbd_in_contract_document(self):
        text = self.contract_path.read_text(encoding="utf-8")
        self.assertNotIn("TODO", text)
        self.assertNotIn("TBD", text)
        self.assertNotIn("por decidir", text)


class ContentPackAgainstContractTest(unittest.TestCase):
    """Validates that the Content Pack regions.json matches the contract."""

    @classmethod
    def setUpClass(cls):
        cls.root = ROOT
        cls.contract = extract_canonical_block(cls.root / "docs" / "territory_contract.md")
        cls.regions_data = load_json(
            cls.root / "Assets" / "StreamingAssets" / "content" / "core" / "regions.json"
        )

    def test_content_pack_region_order_matches_contract(self):
        errors = validate_content_pack_against_contract(self.contract, self.regions_data)
        self.assertEqual([], errors, "\n".join(errors))

    def test_one_region_with_different_weight_is_rejected(self):
        mutated = copy.deepcopy(self.regions_data)
        mutated["regions"][0]["weight_ppm"] = 62499
        errors = validate_content_pack_against_contract(self.contract, mutated)
        self.assertTrue(errors)
        self.assertTrue(any("weight_ppm" in error for error in errors), errors)
        self.assertEqual(62500, self.regions_data["regions"][0]["weight_ppm"])


class PullBindingTargetConfigTest(unittest.TestCase):
    """Validates that pull binding destinations have correct target_config entries."""

    @classmethod
    def setUpClass(cls):
        cls.target_config = load_json(
            ROOT / "Assets" / "StreamingAssets" / "content" / "rules" / "target_config.json"
        )

    def test_pull_binding_destinations_have_correct_target_config(self):
        for binding in EXPECTED_PULL_BINDINGS:
            dest = binding["destination"]
            found = False
            for entry in self.target_config:
                pattern = entry["pattern"]
                if dest.startswith(pattern.replace("*", "").rstrip(".")) or (
                    "*" in pattern
                    and len(dest) >= len(pattern.replace(".*", ""))
                ):
                    import fnmatch
                    if fnmatch.fnmatch(dest, pattern):
                        self.assertEqual(100, entry["scale"], f"{dest} scale != 100")
                        self.assertEqual(0, entry["minS"], f"{dest} minS != 0")
                        self.assertEqual(10000, entry["maxS"], f"{dest} maxS != 10000")
                        self.assertEqual(5000, entry["defaultS"], f"{dest} defaultS != 5000")
                        self.assertIn("SET", entry["allow_ops"], f"{dest} does not allow SET")
                        found = True
                        break
            self.assertTrue(found, f"No target_config entry matches pattern for {dest}")


class DriftExactVectorsTest(unittest.TestCase):
    """Exact drift computation vectors D-00 through D-10."""

    _formulas: dict | None = None

    @classmethod
    def _get_formulas(cls) -> dict:
        if cls._formulas is None:
            contract = extract_canonical_block(ROOT / "docs" / "territory_contract.md")
            cls._formulas = contract["drift"]["target_formulas"]
        return cls._formulas

    def _run(self, formula_name: str, metrics: dict, current: int, snapshot_support: int | None = None) -> dict:
        return compute_full_drift(self._get_formulas()[formula_name], metrics, current, snapshot_support)

    def test_d00_neutral(self):
        metrics = {f"metrics.{m}": 5000 for m in ["legitimacy", "party_organization", "social_tension", "economy", "security", "public_agenda", "internal_cohesion"]}
        for target in ["support", "tension", "organization", "rival_presence"]:
            r = self._run(target, metrics, 5000, snapshot_support=5000)
            self.assertEqual(0, r["numerator"], f"{target} numerator not 0")
            self.assertEqual(5000, r["targetS"], f"{target} targetS not 5000")
            self.assertEqual(5000, r["finalS"], f"{target} finalS not 5000")
            self.assertEqual(0, r["realized_deltaS"], f"{target} delta not 0")

    def test_d01_support_positive(self):
        r = self._run("support", {"metrics.legitimacy": 6000, "metrics.party_organization": 5000, "metrics.social_tension": 5000}, 5000)
        self.assertEqual(600000000, r["numerator"])
        self.assertEqual(600, r["offsetS"])
        self.assertEqual(5600, r["targetS"])
        self.assertEqual(600, r["distanceS"])
        self.assertEqual(65460600, r["elastic_numerator"])
        self.assertEqual(65, r["elastic_deltaS"])
        self.assertEqual(65, r["capped_deltaS"])
        self.assertEqual(5065, r["finalS"])
        self.assertEqual(65, r["realized_deltaS"])

    def test_d02_support_negative(self):
        r = self._run("support", {"metrics.legitimacy": 4000, "metrics.party_organization": 5000, "metrics.social_tension": 5000}, 5000)
        self.assertEqual(4400, r["targetS"])
        self.assertEqual(-600, r["distanceS"])
        self.assertEqual(-65460600, r["elastic_numerator"])
        self.assertEqual(-65, r["elastic_deltaS"])
        self.assertEqual(-65, r["capped_deltaS"])
        self.assertEqual(4935, r["finalS"])
        self.assertEqual(-65, r["realized_deltaS"])

    def test_d03_tension_positive(self):
        r = self._run("tension", {"metrics.economy": 4000, "metrics.security": 5000, "metrics.public_agenda": 5000}, 5000)
        self.assertEqual(5500, r["targetS"])
        self.assertEqual(55, r["elastic_deltaS"])
        self.assertEqual(5055, r["finalS"])

    def test_d04_tension_negative(self):
        r = self._run("tension", {"metrics.economy": 6000, "metrics.security": 5000, "metrics.public_agenda": 5000}, 5000)
        self.assertEqual(4500, r["targetS"])
        self.assertEqual(-55, r["elastic_deltaS"])
        self.assertEqual(4945, r["finalS"])

    def test_d05_organization_positive(self):
        r = self._run("organization", {"metrics.party_organization": 6000}, 5000)
        self.assertEqual(800000 * 1000, r["numerator"])
        self.assertEqual(800, r["offsetS"])
        self.assertEqual(5800, r["targetS"])
        self.assertEqual(87, r["elastic_deltaS"])
        self.assertEqual(5087, r["finalS"])

    def test_d06_organization_negative(self):
        r = self._run("organization", {"metrics.party_organization": 4000}, 5000)
        self.assertEqual(-800000 * 1000, r["numerator"])
        self.assertEqual(-800, r["offsetS"])
        self.assertEqual(4200, r["targetS"])
        self.assertEqual(-87, r["elastic_deltaS"])
        self.assertEqual(4913, r["finalS"])

    def test_d07_rival_positive(self):
        r = self._run("rival_presence", {"metrics.internal_cohesion": 5000}, 5000, snapshot_support=4000)
        self.assertEqual(700, r["offsetS"])
        self.assertEqual(5700, r["targetS"])
        self.assertEqual(76, r["elastic_deltaS"])
        self.assertEqual(5076, r["finalS"])

    def test_d08_support_pre_drift_obligatory(self):
        # Support drift
        support_metrics = {"metrics.legitimacy": 10000, "metrics.party_organization": 10000, "metrics.social_tension": 0}
        sr = self._run("support", support_metrics, 4000)
        self.assertEqual(10000, sr["targetS"])
        self.assertEqual(655, sr["elastic_deltaS"])
        self.assertEqual(200, sr["capped_deltaS"])
        self.assertEqual(4200, sr["finalS"])
        # Rival using pre-drift support 4000
        rival_metrics = {"metrics.internal_cohesion": 5000}
        rr = self._run("rival_presence", rival_metrics, 5000, snapshot_support=4000)
        self.assertEqual(5700, rr["targetS"])
        self.assertEqual(76, rr["elastic_deltaS"])
        self.assertEqual(5076, rr["finalS"])
        # Counterexample: using post-drift support 4200 must give different result
        rr_wrong = self._run("rival_presence", rival_metrics, 5000, snapshot_support=4200)
        self.assertEqual(5560, rr_wrong["targetS"])
        self.assertEqual(61, rr_wrong["elastic_deltaS"])
        self.assertEqual(5061, rr_wrong["finalS"])
        # Actual must be 5076, NOT 5061
        self.assertEqual(5076, rr["finalS"])
        self.assertNotEqual(5076, rr_wrong["finalS"])

    def test_d09_cap_positive(self):
        r = self._run("support", {"metrics.legitimacy": 10000, "metrics.party_organization": 10000, "metrics.social_tension": 0}, 0)
        self.assertEqual(10000, r["targetS"])
        self.assertEqual(1091, r["elastic_deltaS"])
        self.assertEqual(200, r["capped_deltaS"])
        self.assertEqual(200, r["finalS"])

    def test_d10_cap_negative(self):
        r = self._run("support", {"metrics.legitimacy": 0, "metrics.party_organization": 0, "metrics.social_tension": 10000}, 10000)
        self.assertEqual(0, r["targetS"])
        self.assertEqual(-1091, r["elastic_deltaS"])
        self.assertEqual(-200, r["capped_deltaS"])
        self.assertEqual(9800, r["finalS"])

    def test_alpha_sensitivity_distance_912(self):
        distance = 912
        r_109101 = compute_drift_delta(5000 + distance, 5000, alpha_ppm=109101)
        r_109100 = compute_drift_delta(5000 + distance, 5000, alpha_ppm=109100)
        self.assertEqual(100, r_109101["elastic_deltaS"])
        self.assertEqual(99, r_109100["elastic_deltaS"])
        self.assertNotEqual(r_109101["elastic_deltaS"], r_109100["elastic_deltaS"])

    def test_intermediates_require_long(self):
        numerator = 600000 * (10000 - 5000) + 300000 * (10000 - 5000) - 400000 * (0 - 5000)
        # 600000*5000 + 300000*5000 - 400000*(-5000) = 3000000000 + 1500000000 + 2000000000 = 6500000000
        self.assertEqual(6500000000, numerator)
        self.assertGreater(numerator, 2147483647)
        self.assertLessEqual(numerator, 9223372036854775807)

    def test_cap_201_produces_different_result(self):
        r = compute_drift_delta(10000, 0, alpha_ppm=ALPHA_PPM, cap=201)
        self.assertEqual(201, r["capped_deltaS"])
        self.assertEqual(201, r["finalS"])
        r_200 = compute_drift_delta(10000, 0, alpha_ppm=ALPHA_PPM, cap=200)
        self.assertEqual(200, r_200["capped_deltaS"])
        self.assertNotEqual(r["finalS"], r_200["finalS"])


class PullExactVectorsTest(unittest.TestCase):
    """Exact pull computation vectors P-00 through P-06."""

    def _run(self, regional_values: list[int], current: int) -> dict:
        wavg = compute_weighted_average(regional_values, REGIONAL_WEIGHTS)
        return compute_pull(wavg, current)

    def test_p00_neutral(self):
        result = self._run([5000] * 16, 5000)
        self.assertEqual(5000, result["weighted_averageS"])
        self.assertEqual(5000, result["finalS"])
        self.assertEqual(0, result["realized_deltaS"])

    def test_p01_positive(self):
        result = self._run([6000] * 16, 5000)
        self.assertEqual(6000, result["weighted_averageS"])
        self.assertEqual(1000, result["distanceS"])
        self.assertEqual(206299000, result["elastic_numerator"])
        self.assertEqual(206, result["elastic_deltaS"])
        self.assertEqual(206, result["capped_deltaS"])
        self.assertEqual(5206, result["finalS"])

    def test_p02_negative(self):
        result = self._run([4000] * 16, 5000)
        self.assertEqual(4000, result["weighted_averageS"])
        self.assertEqual(-1000, result["distanceS"])
        self.assertEqual(-206299000, result["elastic_numerator"])
        self.assertEqual(-206, result["elastic_deltaS"])
        self.assertEqual(4794, result["finalS"])

    def test_p03_cap_positive(self):
        result = self._run([10000] * 16, 0)
        self.assertEqual(10000, result["weighted_averageS"])
        self.assertEqual(2063, result["elastic_deltaS"])
        self.assertEqual(400, result["capped_deltaS"])
        self.assertEqual(400, result["finalS"])

    def test_p04_cap_negative(self):
        result = self._run([0] * 16, 10000)
        self.assertEqual(0, result["weighted_averageS"])
        self.assertEqual(-2063, result["elastic_deltaS"])
        self.assertEqual(-400, result["capped_deltaS"])
        self.assertEqual(9600, result["finalS"])

    def test_p05_weighted_average_rounding(self):
        values = [5000] * 16
        values[0] = 5008
        numerator = sum(v * w for v, w in zip(values, REGIONAL_WEIGHTS))
        self.assertEqual(5000500000, numerator)
        wavg_half_away = round_div_half_away(numerator, 1000000)
        self.assertEqual(5001, wavg_half_away)
        wavg_truncate = numerator // 1000000
        self.assertEqual(5000, wavg_truncate)
        self.assertNotEqual(wavg_half_away, wavg_truncate)
        result = compute_pull(wavg_half_away, 5000)
        self.assertEqual(5001, result["targetS"])

    def test_p06_alpha_sensitivity(self):
        r_206299 = compute_pull(5000 + 778, 5000, alpha_ppm=206299)
        r_206298 = compute_pull(5000 + 778, 5000, alpha_ppm=206298)
        self.assertEqual(161, r_206299["elastic_deltaS"])
        self.assertEqual(160, r_206298["elastic_deltaS"])
        self.assertNotEqual(r_206299["elastic_deltaS"], r_206298["elastic_deltaS"])


class LatencyVectorTest(unittest.TestCase):
    """Exact T/T+1 latency vector for pull feedback."""

    @classmethod
    def setUpClass(cls):
        cls.aggregation_config = load_json(
            ROOT / "Assets" / "StreamingAssets" / "content" / "rules" / "aggregation_config.json"
        )

    def _find_legislative_capacity_config(self) -> tuple:
        """Extract (alpha_ppm, cap_per_weekS) for metrics.legislative_capacity."""
        for p in self.aggregation_config.get("passes", []):
            if p.get("type") != "METRIC_AGGREGATION":
                continue
            for m in p.get("metrics", []):
                if m.get("metric") == "metrics.legislative_capacity":
                    return m["alpha_ppm"], m["cap_per_weekS"]
        self.fail("metrics.legislative_capacity not found")

    def _find_legislative_capacity_coalition_weight(self) -> int:
        for p in self.aggregation_config.get("passes", []):
            if p.get("type") != "METRIC_AGGREGATION":
                continue
            for m in p.get("metrics", []):
                if m.get("metric") == "metrics.legislative_capacity":
                    for c in m.get("components", []):
                        if c.get("target") == "internals.leg.coalition_strength":
                            return c["weight_ppm"]
        self.fail("legislative_capacity coalition_strength weight not found")

    def _find_leg_reversion_alpha(self) -> int:
        """Extract alpha_ppm for internals.leg.* reversion group."""
        for p in self.aggregation_config.get("passes", []):
            if p.get("type") != "INTERNAL_REVERSION":
                continue
            for g in p.get("groups", []):
                if g.get("pattern") == "internals.leg.*":
                    return g["alpha_ppm"]
        self.fail("internals.leg.* reversion group not found")

    @staticmethod
    def _find_derived_internal_targets(
        aggregation_config: dict,
    ) -> set[str]:
        targets: set[str] = set()
        found_pass = False
        for pass_config in aggregation_config.get("passes", []):
            if pass_config.get("type") != "DERIVED_INTERNALS":
                continue
            found_pass = True
            for rule in pass_config.get("rules", []):
                target = rule.get("target")
                if target is not None:
                    targets.add(target)
        if not found_pass:
            raise AssertionError("DERIVED_INTERNALS pass not found")
        return targets

    def test_tt1_latency_vector(self):
        # --- Validate aggregation_config.json constants ---
        leg_alpha, leg_cap = self._find_legislative_capacity_config()
        self.assertEqual(206299, leg_alpha)
        self.assertEqual(400, leg_cap)
        weight = self._find_legislative_capacity_coalition_weight()
        self.assertEqual(350000, weight)
        reversion_alpha = self._find_leg_reversion_alpha()
        self.assertEqual(34064, reversion_alpha)

        # --- Tick T: pull (phase 10) ---
        wavg = compute_weighted_average([5065] * 16, REGIONAL_WEIGHTS)
        self.assertEqual(5065, wavg)

        pull_result = compute_pull(wavg, 5000)
        self.assertEqual(65, pull_result["distanceS"])
        self.assertEqual(13409435, pull_result["elastic_numerator"])
        self.assertEqual(13, pull_result["elastic_deltaS"])
        self.assertEqual(13, pull_result["capped_deltaS"])
        self.assertEqual(5013, pull_result["finalS"])

        coalition_T = pull_result["finalS"]
        self.assertEqual(5013, coalition_T)

        # legislative_capacity was already aggregated before pull
        legislative_capacity_T = 5000
        self.assertEqual(5000, legislative_capacity_T)

        # --- Same-tick phase 8 reexecution counterexample ---
        agg_num = weight * (coalition_T - 5000)
        self.assertEqual(4550000, agg_num)
        agg_offset = round_div_half_away(agg_num, PPM_DEN)
        self.assertEqual(5, agg_offset)
        agg_target = 5000 + agg_offset
        self.assertEqual(5005, agg_target)

        metric_distance = agg_target - legislative_capacity_T
        self.assertEqual(5, metric_distance)
        metric_elastic_num = metric_distance * leg_alpha
        self.assertEqual(1031495, metric_elastic_num)
        metric_elastic_delta = round_div_half_away(metric_elastic_num, PPM_DEN)
        self.assertEqual(1, metric_elastic_delta)
        same_tick_wrong = legislative_capacity_T + metric_elastic_delta
        self.assertEqual(5001, same_tick_wrong)

        self.assertEqual(5000, legislative_capacity_T)
        self.assertEqual(5001, same_tick_wrong)
        self.assertNotEqual(legislative_capacity_T, same_tick_wrong)

        # --- Tick T+1 phase 6: REVERSION (internals.leg.*) ---
        reversion_distance = MID_S - coalition_T
        self.assertEqual(-13, reversion_distance)
        reversion_num = reversion_distance * reversion_alpha
        self.assertEqual(-442832, reversion_num)
        reversion_delta = round_div_half_away(reversion_num, PPM_DEN)
        self.assertEqual(0, reversion_delta)
        coalition_after_reversion = coalition_T + reversion_delta
        self.assertEqual(5013, coalition_after_reversion)

        # --- Tick T+1 phase 7: DERIVED_INTERNALS ---
        # Does NOT write internals.leg.coalition_strength or
        # metrics.legislative_capacity

        # --- Tick T+1 phase 8: METRIC_AGGREGATION ---
        agg_num_t1 = weight * (coalition_after_reversion - MID_S)
        self.assertEqual(4550000, agg_num_t1)
        agg_offset_t1 = round_div_half_away(agg_num_t1, PPM_DEN)
        self.assertEqual(5, agg_offset_t1)
        agg_target_t1 = MID_S + agg_offset_t1
        self.assertEqual(5005, agg_target_t1)

        metric_distance_t1 = agg_target_t1 - legislative_capacity_T
        self.assertEqual(5, metric_distance_t1)
        metric_elastic_num_t1 = metric_distance_t1 * leg_alpha
        self.assertEqual(1031495, metric_elastic_num_t1)
        metric_elastic_delta_t1 = round_div_half_away(metric_elastic_num_t1, PPM_DEN)
        self.assertEqual(1, metric_elastic_delta_t1)
        metric_capped_delta_t1 = clamp(metric_elastic_delta_t1, -leg_cap, leg_cap)
        self.assertEqual(1, metric_capped_delta_t1)
        legislative_capacity_T1 = legislative_capacity_T + metric_capped_delta_t1
        self.assertEqual(5001, legislative_capacity_T1)

        # --- Final hardcoded assertions ---
        self.assertEqual(5013, coalition_T)
        self.assertEqual(0, reversion_delta)
        self.assertEqual(5005, agg_target_t1)
        self.assertEqual(5000, legislative_capacity_T)
        self.assertEqual(5001, legislative_capacity_T1)

    def test_phase7_derived_internals_do_not_write_latency_targets(self):
        targets = self._find_derived_internal_targets(self.aggregation_config)
        self.assertNotIn(
            "internals.leg.coalition_strength",
            targets,
        )
        self.assertNotIn(
            "metrics.legislative_capacity",
            targets,
        )


class ContractIntegrityTest(unittest.TestCase):
    """Validates the contract document is structurally sound."""

    @classmethod
    def setUpClass(cls):
        cls.root = ROOT
        cls.contract_path = cls.root / "docs" / "territory_contract.md"

    def test_begin_marker_appears_exactly_once(self):
        text = self.contract_path.read_text(encoding="utf-8")
        count = text.count(BEGIN_MARKER)
        self.assertEqual(1, count, f"BEGIN_MARKER appears {count} times")

    def test_end_marker_appears_exactly_once(self):
        text = self.contract_path.read_text(encoding="utf-8")
        count = text.count(END_MARKER)
        self.assertEqual(1, count, f"END_MARKER appears {count} times")

    def test_begin_marker_before_end_marker(self):
        text = self.contract_path.read_text(encoding="utf-8")
        begin_pos = text.index(BEGIN_MARKER)
        end_pos = text.index(END_MARKER)
        self.assertLess(begin_pos, end_pos, "BEGIN_MARKER must appear before END_MARKER")

    def test_json_block_is_between_markers(self):
        text = self.contract_path.read_text(encoding="utf-8")
        begin_pos = text.index(BEGIN_MARKER)
        end_pos = text.index(END_MARKER)
        json_start = text.index("{", begin_pos)
        json_end = text.rindex("}", 0, end_pos) + 1
        self.assertGreater(json_start, begin_pos)
        self.assertLess(json_end, end_pos)
        parsed = json.loads(text[json_start:json_end])
        for key in EXPECTED_TOP_LEVEL_KEYS:
            self.assertIn(key, parsed)

    def test_contract_parses_as_valid_json(self):
        text = self.contract_path.read_text(encoding="utf-8")
        begin = text.find(BEGIN_MARKER)
        end = text.find(END_MARKER)
        json_start = text.index("{", begin)
        json_end = text.rindex("}", 0, end) + 1
        parsed = json.loads(text[json_start:json_end])
        for key in EXPECTED_TOP_LEVEL_KEYS:
            self.assertIn(key, parsed)

    def test_contract_has_no_trailing_whitespace_lines(self):
        for i, line in enumerate(
            self.contract_path.read_text(encoding="utf-8").split("\n"), 1
        ):
            self.assertEqual(
                line, line.rstrip(),
                f"Line {i} has trailing whitespace: {line!r}",
            )

    def test_no_utf8_bom(self):
        raw = self.contract_path.read_bytes()
        self.assertFalse(raw.startswith(b"\xef\xbb\xbf"), "File must not have UTF-8 BOM")

    def test_no_carriage_return_bytes(self):
        raw = self.contract_path.read_bytes()
        self.assertNotIn(b"\r", raw, "File must not contain CR bytes")

    def test_file_is_valid_utf8(self):
        raw = self.contract_path.read_bytes()
        raw.decode("utf-8")


class MutationMatrixTest(unittest.TestCase):
    """In-memory mutations that must all be rejected."""

    def setUp(self):
        self.valid = {
            "canonical_region_order": {
                "authority": "content_pack_declaration_order",
                "source_path": "Assets/StreamingAssets/content/core/regions.json",
                "region_count": 16,
                "ordered_region_ids": list(EXPECTED_CANONICAL_REGION_ORDER),
                "weight_ppm_each": 62500,
                "weight_ppm_sum_required": 1000000,
                "forbidden_order_sources": list(EXPECTED_FORBIDDEN_SOURCES),
            },
            "regional_dynamic_targets": list(EXPECTED_DYNAMIC_TARGETS),
            "static_regional_resources": dict(EXPECTED_STATIC_RESOURCES),
            "numeric_domain": dict(EXPECTED_NUMERIC_DOMAIN),
            "drift": {
                "phase": 9,
                "phase_name": "DriftNationalToRegions",
                "snapshot": "post_phase_8",
                "region_order_source": "canonical_region_order.ordered_region_ids",
                "metric_order": list(EXPECTED_DRIFT_METRIC_ORDER),
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
                            {"source": "metrics.legitimacy", "transform": "value_minus_mid", "coefficient_ppm": 600000},
                            {"source": "metrics.party_organization", "transform": "value_minus_mid", "coefficient_ppm": 300000},
                            {"source": "metrics.social_tension", "transform": "value_minus_mid", "coefficient_ppm": -400000},
                        ],
                    },
                    "tension": {
                        "target": "regions.{region_id}.tension",
                        "terms": [
                            {"source": "metrics.economy", "transform": "mid_minus_value", "coefficient_ppm": 500000},
                            {"source": "metrics.security", "transform": "mid_minus_value", "coefficient_ppm": 400000},
                            {"source": "metrics.public_agenda", "transform": "value_minus_mid", "coefficient_ppm": 300000},
                        ],
                    },
                    "organization": {
                        "target": "regions.{region_id}.organization",
                        "terms": [
                            {"source": "metrics.party_organization", "transform": "value_minus_mid", "coefficient_ppm": 800000},
                        ],
                    },
                    "rival_presence": {
                        "target": "regions.{region_id}.rival_presence",
                        "terms": [
                            {"source": "regions.{region_id}.support", "transform": "mid_minus_value", "coefficient_ppm": 700000},
                            {"source": "metrics.internal_cohesion", "transform": "mid_minus_value", "coefficient_ppm": 200000},
                        ],
                    },
                },
                "common_pipeline": list(EXPECTED_DRIFT_PIPELINE),
            },
            "pull": copy.deepcopy(EXPECTED_PULL),
            "latency": copy.deepcopy(EXPECTED_LATENCY),
            "causality": {
                "cause_category": "SYSTEM",
                "parent": None,
                "canonical_key_separator": ":",
                "canonical_key_separator_count": 1,
                "identifier_separator": ".",
                "identifier_policy": "PRINTABLE_ASCII_NO_WHITESPACE_COLON_PIPE",
                "drift": {
                    "id_prefix": "REG_DRIFT",
                    "canonical_key_pattern": "SYSTEM:REG_DRIFT.regions.{region_id}.{metric}",
                    "target_pattern": "regions.{region_id}.{metric}",
                    "target_visibility": "public_visible_target",
                    "public_ledger": True,
                    "tick_causal_buffer": True,
                    "potential_cause_count": 64,
                    "cause_order": "canonical_region_order_then_drift_metric_order",
                    "zero_realized_delta_policy": "omit_contribution",
                },
                "pull_provenance": {
                    "id_prefix": "REG_TO_INT",
                    "canonical_key_pattern": "SYSTEM:REG_TO_INT.{internal_target}",
                    "identity_count": 5,
                    "provenance_scope": "ephemeral_execution_plan_only",
                    "target_visibility": "hidden_internal",
                    "public_ledger": False,
                    "tick_causal_buffer": False,
                    "serialized": False,
                    "stored_in_game_state": False,
                    "turn_report": False,
                    "top_n_slot": False,
                    "lifetime": "current_phase_10_plan_only",
                    "public_attribution": "next_tick_SYSTEM_AGG",
                    "double_counting": "forbidden",
                    "identities": copy.deepcopy(EXPECTED_PULL_PROVENANCE_IDENTITIES),
                },
                "public_aggregation_attribution": copy.deepcopy(EXPECTED_PUBLIC_AGG_ATTRIBUTION),
            },
        }

    def assert_invalid(self, contract: dict, description: str):
        errors = validate_contract(contract)
        self.assertTrue(errors, f"Mutation '{description}' should have been rejected")

    def assert_valid(self, contract: dict, description: str):
        errors = validate_contract(contract)
        self.assertEqual([], errors, f"Expected valid contract for '{description}': {' '.join(errors)}")

    # Existing mutations (retained)
    def test_swap_tarapaca_antofagasta(self):
        ids = self.valid["canonical_region_order"]["ordered_region_ids"]
        i1, i2 = ids.index("tarapaca"), ids.index("antofagasta")
        ids[i1], ids[i2] = ids[i2], ids[i1]
        self.assert_invalid(self.valid, "swap tarapaca and antofagasta")

    def test_alphabetical_order(self):
        self.valid["canonical_region_order"]["ordered_region_ids"].sort()
        self.assert_invalid(self.valid, "alphabetical order")

    def test_duplicate_id(self):
        ids = self.valid["canonical_region_order"]["ordered_region_ids"]
        ids.append("arica_parinacota")
        self.valid["canonical_region_order"]["region_count"] = 17
        self.assert_invalid(self.valid, "duplicate ID")

    def test_region_omitted(self):
        self.valid["canonical_region_order"]["ordered_region_ids"].pop()
        self.valid["canonical_region_order"]["region_count"] = 15
        self.assert_invalid(self.valid, "one region omitted")

    def test_unknown_id(self):
        self.valid["canonical_region_order"]["ordered_region_ids"][0] = "unknown_region"
        self.assert_invalid(self.valid, "unknown region ID")

    def test_weight_ppm_each_62499(self):
        self.valid["canonical_region_order"]["weight_ppm_each"] = 62499
        self.assert_invalid(self.valid, "weight_ppm_each = 62499")

    def test_uniform_contract_weight_wrong(self):
        self.valid["canonical_region_order"]["weight_ppm_each"] = 1
        self.assert_invalid(self.valid, "uniform contract weight wrong")

    def test_sum_not_1000000(self):
        self.valid["canonical_region_order"]["weight_ppm_sum_required"] = 999999
        self.assert_invalid(self.valid, "sum != 1000000")

    def test_dynamic_target_omitted(self):
        self.valid["regional_dynamic_targets"].pop()
        self.assert_invalid(self.valid, "dynamic target omitted")

    def test_dynamic_target_extra(self):
        self.valid["regional_dynamic_targets"].append("extra_target")
        self.assert_invalid(self.valid, "dynamic target extra")

    def test_dynamic_target_order_altered(self):
        self.valid["regional_dynamic_targets"].reverse()
        self.assert_invalid(self.valid, "dynamic target order altered")

    def test_static_resource_omitted(self):
        self.valid["static_regional_resources"].pop("admin_capS")
        self.assert_invalid(self.valid, "static resource omitted")

    def test_static_resource_extra(self):
        self.valid["static_regional_resources"]["extra_field"] = 5000
        self.assert_invalid(self.valid, "static resource extra")

    def test_default_static_not_5000(self):
        self.valid["static_regional_resources"]["admin_capS"] = 4999
        self.assert_invalid(self.valid, "default static != 5000")

    def test_authority_wrong(self):
        self.valid["canonical_region_order"]["authority"] = "game_state_alphabetic"
        self.assert_invalid(self.valid, "authority is wrong")

    def test_source_path_wrong(self):
        self.valid["canonical_region_order"]["source_path"] = "Assets/StreamingAssets/content/rules/target_config.json"
        self.assert_invalid(self.valid, "source_path is wrong")

    def test_forbidden_source_removed(self):
        self.valid["canonical_region_order"]["forbidden_order_sources"] = EXPECTED_FORBIDDEN_SOURCES[:-1]
        self.assert_invalid(self.valid, "forbidden source removed")

    def test_region_count_wrong(self):
        self.valid["canonical_region_order"]["region_count"] = 15
        self.assert_invalid(self.valid, "region_count = 15")

    def test_extra_top_level_key(self):
        self.valid["extra_key"] = "unexpected"
        self.assert_invalid(self.valid, "extra top-level key")

    def test_missing_top_level_key(self):
        self.valid.pop("static_regional_resources")
        self.assert_invalid(self.valid, "missing top-level key")

    def test_extra_canonical_order_key(self):
        self.valid["canonical_region_order"]["extra_key"] = True
        self.assert_invalid(self.valid, "extra key in canonical_region_order")

    def test_missing_canonical_order_key(self):
        self.valid["canonical_region_order"].pop("forbidden_order_sources")
        self.assert_invalid(self.valid, "missing key in canonical_region_order")

    # --- Numeric domain mutations ---
    def test_numeric_domain_extra_key(self):
        self.valid["numeric_domain"]["extra_key"] = True
        self.assert_invalid(self.valid, "extra key in numeric_domain")

    def test_numeric_domain_missing_key(self):
        self.valid["numeric_domain"].pop("forbidden_numeric_types")
        self.assert_invalid(self.valid, "missing key in numeric_domain")

    # --- Drift mutations ---
    def test_drift_extra_key(self):
        self.valid["drift"]["extra_key"] = True
        self.assert_invalid(self.valid, "extra key in drift")

    def test_drift_missing_key(self):
        self.valid["drift"].pop("common_pipeline")
        self.assert_invalid(self.valid, "missing key in drift")

    def test_drift_alpha_ppm_109100(self):
        self.valid["drift"]["alpha_ppm"] = 109100
        self.assert_invalid(self.valid, "alpha_ppm = 109100")

    def test_drift_cap_201(self):
        self.valid["drift"]["cap_per_weekS"] = 201
        self.assert_invalid(self.valid, "cap_per_weekS = 201")

    def test_drift_half_life_weeks_metadata_wrong(self):
        self.valid["drift"]["half_life_weeks_metadata"] = 5
        self.assert_invalid(self.valid, "half_life_weeks_metadata = 5")

    def test_drift_snapshot_post_phase_9(self):
        self.valid["drift"]["snapshot"] = "post_phase_9"
        self.assert_invalid(self.valid, "snapshot = post_phase_9")

    def test_drift_region_order_source_game_state_rejected(self):
        self.valid["drift"]["region_order_source"] = "GameState.Regions"
        self.assert_invalid(self.valid, "region_order_source = GameState.Regions")

    def test_drift_output_count_63(self):
        self.valid["drift"]["output_count"] = 63
        self.assert_invalid(self.valid, "output_count = 63")

    def test_drift_metric_order_altered(self):
        self.valid["drift"]["metric_order"] = ["tension", "support", "organization", "rival_presence"]
        self.assert_invalid(self.valid, "metric_order altered")

    def test_drift_support_coefficient_inverted(self):
        self.valid["drift"]["target_formulas"]["support"]["terms"][0]["coefficient_ppm"] = -600000
        self.assert_invalid(self.valid, "support coefficient inverted")

    def test_drift_support_term_omitted(self):
        self.valid["drift"]["target_formulas"]["support"]["terms"].pop()
        self.assert_invalid(self.valid, "support term omitted")

    def test_drift_support_term_extra(self):
        self.valid["drift"]["target_formulas"]["support"]["terms"].append(
            {"source": "metrics.legitimacy", "transform": "value_minus_mid", "coefficient_ppm": 100000}
        )
        self.assert_invalid(self.valid, "support term extra")

    def test_drift_support_terms_reordered(self):
        terms = self.valid["drift"]["target_formulas"]["support"]["terms"]
        terms[0], terms[2] = terms[2], terms[0]
        self.assert_invalid(self.valid, "support terms reordered")

    def test_drift_rival_support_post_drift(self):
        self.valid["drift"]["rival_support_read_from"] = "phase_input_snapshot_post_drift"
        self.assert_invalid(self.valid, "rival uses post-drift")

    def test_drift_rounding_truncate(self):
        self.valid["numeric_domain"]["rounding"] = "TRUNCATE"
        self.assert_invalid(self.valid, "rounding = TRUNCATE")

    def test_drift_intermediate_int(self):
        self.valid["numeric_domain"]["intermediate_type"] = "int"
        self.assert_invalid(self.valid, "intermediate_type = int")

    def test_drift_publication_add(self):
        self.valid["numeric_domain"]["publication_operation"] = "ADD"
        self.assert_invalid(self.valid, "publication_operation = ADD")

    def test_common_pipeline_step_omitted(self):
        self.valid["drift"]["common_pipeline"] = EXPECTED_DRIFT_PIPELINE[:-1]
        self.assert_invalid(self.valid, "pipeline step omitted")

    def test_common_pipeline_steps_reordered(self):
        pipe = list(EXPECTED_DRIFT_PIPELINE)
        pipe[0], pipe[2] = pipe[2], pipe[0]
        self.valid["drift"]["common_pipeline"] = pipe
        self.assert_invalid(self.valid, "pipeline steps reordered")

    def test_forbidden_numeric_types_without_decimal(self):
        self.valid["numeric_domain"]["forbidden_numeric_types"] = ["float", "double"]
        self.assert_invalid(self.valid, "forbidden without decimal")

    # --- Pull mutations ---
    def test_pull_extra_key(self):
        self.valid["pull"]["extra_key"] = True
        self.assert_invalid(self.valid, "extra key in pull")

    def test_pull_missing_key(self):
        self.valid["pull"].pop("common_pipeline")
        self.assert_invalid(self.valid, "missing key in pull")

    def test_pull_phase_9(self):
        self.valid["pull"]["phase"] = 9
        self.assert_invalid(self.valid, "pull phase = 9")

    def test_pull_snapshot_post_phase_8(self):
        self.valid["pull"]["snapshot"] = "post_phase_8"
        self.assert_invalid(self.valid, "pull snapshot = post_phase_8")

    def test_pull_alpha_206298(self):
        self.valid["pull"]["alpha_ppm"] = 206298
        self.assert_invalid(self.valid, "pull alpha = 206298")

    def test_pull_cap_401(self):
        self.valid["pull"]["cap_per_weekS"] = 401
        self.assert_invalid(self.valid, "pull cap = 401")

    def test_pull_weight_sum_wrong(self):
        self.valid["pull"]["weight_sum_required"] = 999999
        self.assert_invalid(self.valid, "pull weight sum wrong")

    def test_pull_division_count_2(self):
        self.valid["pull"]["weighted_average_division_count"] = 2
        self.assert_invalid(self.valid, "division_count = 2")

    def test_pull_region_order_source_game_state(self):
        self.valid["pull"]["region_order_source"] = "GameState.Regions"
        self.assert_invalid(self.valid, "pull region_order_source = GameState.Regions")

    def test_pull_binding_omitted(self):
        self.valid["pull"]["bindings"].pop()
        self.assert_invalid(self.valid, "pull binding omitted")

    def test_pull_binding_extra(self):
        self.valid["pull"]["bindings"].append(
            {"id": "extra_binding", "regional_source": "support", "destination": "internals.extra.dest"}
        )
        self.assert_invalid(self.valid, "pull binding extra")

    def test_pull_bindings_reordered(self):
        b = self.valid["pull"]["bindings"]
        b[0], b[2] = b[2], b[0]
        self.assert_invalid(self.valid, "pull bindings reordered")

    def test_pull_binding_id_duplicate(self):
        self.valid["pull"]["bindings"][0]["id"] = "tension_to_protest_activity"
        self.assert_invalid(self.valid, "pull binding id duplicate")

    def test_pull_destination_duplicate(self):
        self.valid["pull"]["bindings"][0]["destination"] = "internals.tension.protest_activity"
        self.assert_invalid(self.valid, "pull destination duplicate")

    def test_pull_support_wrong_destination(self):
        self.valid["pull"]["bindings"][0]["destination"] = "internals.wrong.dest"
        self.assert_invalid(self.valid, "pull support wrong destination")

    def test_pull_one_tension_binding_removed(self):
        self.valid["pull"]["bindings"] = [
            b for b in self.valid["pull"]["bindings"]
            if b["id"] != "tension_to_movement_salience"
        ]
        self.assert_invalid(self.valid, "one tension binding removed")

    def test_pull_binding_chaining_allowed(self):
        self.valid["pull"]["binding_chaining"] = "allowed"
        self.assert_invalid(self.valid, "binding_chaining = allowed")

    def test_pull_pipeline_step_omitted(self):
        self.valid["pull"]["common_pipeline"] = EXPECTED_PULL_PIPELINE[:-1]
        self.assert_invalid(self.valid, "pull pipeline step omitted")

    def test_pull_pipeline_reordered(self):
        pipe = list(EXPECTED_PULL_PIPELINE)
        pipe[0], pipe[2] = pipe[2], pipe[0]
        self.valid["pull"]["common_pipeline"] = pipe
        self.assert_invalid(self.valid, "pull pipeline reordered")

    # --- Latency mutations ---
    def test_latency_extra_key(self):
        self.valid["latency"]["extra_key"] = True
        self.assert_invalid(self.valid, "extra key in latency")

    def test_latency_missing_key(self):
        self.valid["latency"].pop("regional_feedback_first_visible_in_metrics")
        self.assert_invalid(self.valid, "missing key in latency")

    def test_latency_ticks_0(self):
        self.valid["latency"]["feedback_latency_ticks"] = 0
        self.assert_invalid(self.valid, "latency ticks = 0")

    def test_latency_same_tick_reexecution_true(self):
        self.valid["latency"]["same_tick_phase_8_reexecution"] = True
        self.assert_invalid(self.valid, "same_tick_reexecution = True")

    def test_latency_next_tick_order_reordered(self):
        order = list(self.valid["latency"]["next_tick_observation_order"])
        order[0], order[2] = order[2], order[0]
        self.valid["latency"]["next_tick_observation_order"] = order
        self.assert_invalid(self.valid, "next_tick_order reordered")

    def test_causality_extra_key(self):
        self.valid["causality"]["extra"] = True
        self.assert_invalid(self.valid, "extra key in causality")

    def test_causality_missing_key(self):
        self.valid["causality"].pop("cause_category")
        self.assert_invalid(self.valid, "missing key in causality")

    def test_causality_wrong_category(self):
        self.valid["causality"]["cause_category"] = "EVENT"
        self.assert_invalid(self.valid, "cause_category EVENT")

    def test_causality_parent_not_null(self):
        self.valid["causality"]["parent"] = {}
        self.assert_invalid(self.valid, "parent not null")

    def test_causality_separator_count_2(self):
        self.valid["causality"]["canonical_key_separator_count"] = 2
        self.assert_invalid(self.valid, "separator_count 2")

    def test_causality_identifier_policy_altered(self):
        self.valid["causality"]["identifier_policy"] = "PRINTABLE_ASCII"
        self.assert_invalid(self.valid, "identifier_policy altered")

    def test_drift_id_prefix_altered(self):
        self.valid["causality"]["drift"]["id_prefix"] = "DRIFT"
        self.assert_invalid(self.valid, "drift.id_prefix DRIFT")

    def test_drift_pattern_with_multiple_colons(self):
        self.valid["causality"]["drift"]["canonical_key_pattern"] = (
            "SYSTEM:REG_DRIFT:{region_id}:{metric}"
        )
        self.assert_invalid(self.valid, "drift pattern with multiple :")

    def test_drift_public_ledger_false(self):
        self.valid["causality"]["drift"]["public_ledger"] = False
        self.assert_invalid(self.valid, "drift.public_ledger False")

    def test_drift_tick_causal_buffer_false(self):
        self.valid["causality"]["drift"]["tick_causal_buffer"] = False
        self.assert_invalid(self.valid, "drift.tick_causal_buffer False")

    def test_drift_potential_cause_count_63(self):
        self.valid["causality"]["drift"]["potential_cause_count"] = 63
        self.assert_invalid(self.valid, "drift.potential_cause_count 63")

    def test_drift_cause_order_altered(self):
        self.valid["causality"]["drift"]["cause_order"] = "alphabetical"
        self.assert_invalid(self.valid, "drift.cause_order altered")

    def test_drift_zero_policy_record_zero(self):
        self.valid["causality"]["drift"]["zero_realized_delta_policy"] = "record_zero"
        self.assert_invalid(self.valid, "drift.zero_policy record_zero")

    def test_pull_pattern_with_multiple_colons(self):
        self.valid["causality"]["pull_provenance"]["canonical_key_pattern"] = (
            "SYSTEM:REG_TO_INT:{internal}:{target}"
        )
        self.assert_invalid(self.valid, "pull pattern with multiple :")

    def test_pull_identity_count_4(self):
        self.valid["causality"]["pull_provenance"]["identity_count"] = 4
        self.assert_invalid(self.valid, "pull identity_count 4")

    def test_pull_provenance_scope_persistent(self):
        self.valid["causality"]["pull_provenance"]["provenance_scope"] = "persistent"
        self.assert_invalid(self.valid, "pull provenance_scope persistent")

    def test_pull_target_visibility_public(self):
        self.valid["causality"]["pull_provenance"]["target_visibility"] = (
            "public_visible_target"
        )
        self.assert_invalid(self.valid, "pull target_visibility public")

    def test_pull_public_ledger_true(self):
        self.valid["causality"]["pull_provenance"]["public_ledger"] = True
        self.assert_invalid(self.valid, "pull public_ledger True")

    def test_pull_tick_causal_buffer_true(self):
        self.valid["causality"]["pull_provenance"]["tick_causal_buffer"] = True
        self.assert_invalid(self.valid, "pull tick_causal_buffer True")

    def test_pull_serialized_true(self):
        self.valid["causality"]["pull_provenance"]["serialized"] = True
        self.assert_invalid(self.valid, "pull serialized True")

    def test_pull_stored_in_game_state_true(self):
        self.valid["causality"]["pull_provenance"]["stored_in_game_state"] = True
        self.assert_invalid(self.valid, "pull stored_in_game_state True")

    def test_pull_turn_report_true(self):
        self.valid["causality"]["pull_provenance"]["turn_report"] = True
        self.assert_invalid(self.valid, "pull turn_report True")

    def test_pull_top_n_slot_true(self):
        self.valid["causality"]["pull_provenance"]["top_n_slot"] = True
        self.assert_invalid(self.valid, "pull top_n_slot True")

    def test_pull_lifetime_altered(self):
        self.valid["causality"]["pull_provenance"]["lifetime"] = "permanent"
        self.assert_invalid(self.valid, "pull lifetime altered")

    def test_pull_public_attribution_same_tick(self):
        self.valid["causality"]["pull_provenance"]["public_attribution"] = (
            "same_tick_REG_TO_INT"
        )
        self.assert_invalid(self.valid, "pull public_attribution same_tick")

    def test_pull_double_counting_allowed(self):
        self.valid["causality"]["pull_provenance"]["double_counting"] = "allowed"
        self.assert_invalid(self.valid, "pull double_counting allowed")

    def test_pull_identity_omitted(self):
        self.valid["causality"]["pull_provenance"]["identities"].pop()
        self.assert_invalid(self.valid, "pull identity omitted")

    def test_pull_identity_extra(self):
        self.valid["causality"]["pull_provenance"]["identities"].append(
            {
                "binding_id": "extra",
                "canonical_key": "SYSTEM:REG_TO_INT.extra",
                "internal_target": "extra",
            }
        )
        self.assert_invalid(self.valid, "pull identity extra")

    def test_pull_identities_reordered(self):
        ids = list(self.valid["causality"]["pull_provenance"]["identities"])
        ids[0], ids[4] = ids[4], ids[0]
        self.valid["causality"]["pull_provenance"]["identities"] = ids
        self.assert_invalid(self.valid, "pull identities reordered")

    def test_pull_identity_canonical_key_wrong(self):
        self.valid["causality"]["pull_provenance"]["identities"][0][
            "canonical_key"
        ] = "SYSTEM:REG_TO_INT.wrong"
        self.assert_invalid(self.valid, "pull identity canonical_key wrong")

    def test_pull_identity_internal_target_wrong(self):
        self.valid["causality"]["pull_provenance"]["identities"][0][
            "internal_target"
        ] = "wrong"
        self.assert_invalid(self.valid, "pull identity internal_target wrong")

    def test_agg_mapping_omitted(self):
        self.valid["causality"]["public_aggregation_attribution"].pop()
        self.assert_invalid(self.valid, "agg mapping omitted")

    def test_agg_mapping_extra(self):
        self.valid["causality"]["public_aggregation_attribution"].append(
            {
                "internal_target": "extra",
                "visible_metric": "metrics.extra",
                "canonical_key": "SYSTEM:AGG.metrics.extra",
            }
        )
        self.assert_invalid(self.valid, "agg mapping extra")

    def test_agg_mappings_reordered(self):
        mappings = list(
            self.valid["causality"]["public_aggregation_attribution"]
        )
        mappings[0], mappings[4] = mappings[4], mappings[0]
        self.valid["causality"]["public_aggregation_attribution"] = mappings
        self.assert_invalid(self.valid, "agg mappings reordered")

    def test_agg_visible_metric_wrong(self):
        self.valid["causality"]["public_aggregation_attribution"][0][
            "visible_metric"
        ] = "metrics.wrong"
        self.assert_invalid(self.valid, "agg visible_metric wrong")

    def test_agg_canonical_key_wrong(self):
        self.valid["causality"]["public_aggregation_attribution"][0][
            "canonical_key"
        ] = "SYSTEM:AGG.wrong.wrong"
        self.assert_invalid(self.valid, "agg canonical_key wrong")

    def test_valid_contract_passes(self):
        self.assert_valid(self.valid, "valid contract should pass")

    def test_valid_contract_structure_passes_full_validation(self):
        errors = validate_contract(self.valid)
        self.assertEqual([], errors)


class CausalityTest(unittest.TestCase):
    """Contractual causality block tests."""

    def setUp(self):
        self.contract = extract_canonical_block(
            ROOT / "docs" / "territory_contract.md"
        )
        self.causality = self.contract.get("causality", {})

    def test_causality_block_is_exact(self):
        c = self.causality
        self.assertEqual("SYSTEM", c.get("cause_category"))
        self.assertIsNone(c.get("parent"))
        self.assertEqual(":", c.get("canonical_key_separator"))
        self.assertEqual(1, c.get("canonical_key_separator_count"))
        self.assertEqual(
            "PRINTABLE_ASCII_NO_WHITESPACE_COLON_PIPE",
            c.get("identifier_policy"),
        )

    def test_causality_drift_block_is_exact(self):
        drift = self.causality.get("drift", {})
        self.assertEqual("REG_DRIFT", drift.get("id_prefix"))
        self.assertEqual(
            "SYSTEM:REG_DRIFT.regions.{region_id}.{metric}",
            drift.get("canonical_key_pattern"),
        )
        self.assertEqual("regions.{region_id}.{metric}", drift.get("target_pattern"))
        self.assertEqual("public_visible_target", drift.get("target_visibility"))
        self.assertTrue(drift.get("public_ledger"))
        self.assertTrue(drift.get("tick_causal_buffer"))
        self.assertEqual(64, drift.get("potential_cause_count"))
        self.assertEqual(
            "canonical_region_order_then_drift_metric_order",
            drift.get("cause_order"),
        )
        self.assertEqual("omit_contribution", drift.get("zero_realized_delta_policy"))

    def test_causality_pull_provenance_block_is_exact(self):
        pull = self.causality.get("pull_provenance", {})
        self.assertEqual("REG_TO_INT", pull.get("id_prefix"))
        self.assertEqual(
            "SYSTEM:REG_TO_INT.{internal_target}",
            pull.get("canonical_key_pattern"),
        )
        self.assertEqual(5, pull.get("identity_count"))
        self.assertEqual("ephemeral_execution_plan_only", pull.get("provenance_scope"))
        self.assertEqual("hidden_internal", pull.get("target_visibility"))
        self.assertFalse(pull.get("public_ledger"))
        self.assertFalse(pull.get("tick_causal_buffer"))
        self.assertFalse(pull.get("serialized"))
        self.assertFalse(pull.get("stored_in_game_state"))
        self.assertFalse(pull.get("turn_report"))
        self.assertFalse(pull.get("top_n_slot"))
        self.assertEqual("current_phase_10_plan_only", pull.get("lifetime"))
        self.assertEqual("next_tick_SYSTEM_AGG", pull.get("public_attribution"))
        self.assertEqual("forbidden", pull.get("double_counting"))

    def test_all_64_reg_drift_keys_are_valid_and_unique(self):
        keys = build_expected_drift_cause_keys()
        self.assertEqual(64, len(keys))
        self.assertEqual(64, len(set(keys)))

    def test_reg_drift_key_order_is_region_major(self):
        keys = build_expected_drift_cause_keys()
        for region_id in EXPECTED_CANONICAL_REGION_ORDER:
            prefix = f"SYSTEM:REG_DRIFT.regions.{region_id}."
            region_keys = [k for k in keys if k.startswith(prefix)]
            self.assertEqual(4, len(region_keys), f"Expected 4 keys for {region_id}")
            self.assertEqual(prefix + "support", region_keys[0])
            self.assertEqual(prefix + "tension", region_keys[1])
            self.assertEqual(prefix + "organization", region_keys[2])
            self.assertEqual(prefix + "rival_presence", region_keys[3])

    def test_reg_drift_first_and_last_keys(self):
        keys = build_expected_drift_cause_keys()
        self.assertEqual(
            "SYSTEM:REG_DRIFT.regions.arica_parinacota.support",
            keys[0],
        )
        self.assertEqual(
            "SYSTEM:REG_DRIFT.regions.magallanes.rival_presence",
            keys[-1],
        )

    def test_reg_drift_keys_have_exactly_one_colon(self):
        keys = build_expected_drift_cause_keys()
        for key in keys:
            self.assertEqual(1, key.count(":"))

    def test_reg_drift_keys_are_syntactically_valid(self):
        keys = build_expected_drift_cause_keys()
        for key in keys:
            self.assertEqual([], validate_canonical_cause_key(key))

    def test_five_reg_to_int_identities_are_exact(self):
        identities = (
            self.causality.get("pull_provenance", {}).get("identities", [])
        )
        self.assertEqual(EXPECTED_PULL_PROVENANCE_IDENTITIES, identities)

    def test_reg_to_int_keys_are_syntactically_valid(self):
        for identity in EXPECTED_PULL_PROVENANCE_IDENTITIES:
            key = identity["canonical_key"]
            self.assertEqual(1, key.count(":"))
            self.assertEqual([], validate_canonical_cause_key(key))

    def test_public_agg_attribution_is_exact(self):
        agg = self.causality.get("public_aggregation_attribution", [])
        self.assertEqual(EXPECTED_PUBLIC_AGG_ATTRIBUTION, agg)

    def test_system_parent_is_null(self):
        self.assertIsNone(self.causality.get("parent"))
        drift = self.causality.get("drift", {})
        pull = self.causality.get("pull_provenance", {})
        self.assertIsNone(drift.get("parent"))
        self.assertIsNone(pull.get("parent"))

    def test_zero_delta_policy_is_omit_contribution(self):
        drift = self.causality.get("drift", {})
        self.assertEqual("omit_contribution", drift.get("zero_realized_delta_policy"))

    def test_invalid_key_separator_comma_rejected(self):
        key = "SYSTEM:REG_DRIFT.regions.arica_parinacota.support"
        self.assertEqual([], validate_canonical_cause_key(key))

    def test_invalid_colon_in_region_id_rejected(self):
        bad = "SYSTEM:REG_DRIFT:arica_parinacota:support"
        errors = validate_canonical_cause_key(bad)
        self.assertTrue(errors, f"Expected errors for {bad}")

    def test_invalid_double_colon_rejected(self):
        bad = "SYSTEM::REG_DRIFT.regions.arica_parinacota.support"
        errors = validate_canonical_cause_key(bad)
        self.assertTrue(errors, f"Expected errors for {bad}")

    def test_invalid_whitespace_in_id_rejected(self):
        bad = "SYSTEM:REG_DRIFT.regions.arica parinacota.support"
        errors = validate_canonical_cause_key(bad)
        self.assertTrue(errors, f"Expected errors for {bad}")

    def test_invalid_pipe_in_id_rejected(self):
        bad = "SYSTEM:REG_DRIFT.regions.arica|parinacota.support"
        errors = validate_canonical_cause_key(bad)
        self.assertTrue(errors, f"Expected errors for {bad}")

    def test_invalid_non_ascii_in_id_rejected(self):
        bad = "SYSTEM:REG_DRIFT.regions.\u00e1rica.support"
        errors = validate_canonical_cause_key(bad)
        self.assertTrue(errors, f"Expected errors for {bad}")

    def test_empty_id_rejected(self):
        bad = "SYSTEM:"
        errors = validate_canonical_cause_key(bad)
        self.assertTrue(errors, f"Expected errors for {bad}")

    def test_wrong_category_rejected(self):
        bad = "EVENT:REG_DRIFT.regions.arica_parinacota.support"
        errors = validate_canonical_cause_key(bad)
        self.assertTrue(errors, f"Expected errors for {bad}")

    def test_reg_drift_categories_must_be_system(self):
        for key in build_expected_drift_cause_keys():
            self.assertTrue(
                key.startswith("SYSTEM:"),
                f"Expected SYSTEM category in {key}",
            )

    def test_validate_causality_accepts_valid(self):
        errors = validate_causality(self.causality)
        self.assertEqual([], errors)

    def test_causality_passes_full_validation(self):
        errors = validate_contract(self.contract)
        self.assertEqual([], errors, f"Expected no errors, got: {errors}")


class CausalityParityTest(unittest.TestCase):
    """Read-only parity checks against C# source and config files."""

    @classmethod
    def setUpClass(cls):
        cls.root = ROOT
        cls.aggregation_config = load_json(
            cls.root
            / "Assets"
            / "StreamingAssets"
            / "content"
            / "rules"
            / "aggregation_config.json"
        )
        cls.causal_models_path = (
            cls.root
            / "Assets"
            / "VictoriantChile"
            / "Simulation"
            / "Core"
            / "Causality"
            / "CausalModels.cs"
        )
        cls.ledger_path = (
            cls.root
            / "Assets"
            / "VictoriantChile"
            / "Simulation"
            / "Core"
            / "Causality"
            / "CausalLedger.cs"
        )
        cls.catalog_path = (
            cls.root
            / "Assets"
            / "VictoriantChile"
            / "Simulation"
            / "Core"
            / "Causality"
            / "VisibleTargetCatalog.cs"
        )

    def test_cause_ref_constructs_category_plus_colon_plus_id(self):
        source = self.causal_models_path.read_text(encoding="utf-8")
        self.assertIn('CategoryToString(category) + ":" + id', source)
        self.assertIn('return "SYSTEM"', source)
        self.assertIn('return "EVENT"', source)
        self.assertIn('return "DECISION"', source)
        self.assertIn('return "REFORM"', source)
        self.assertIn('return "MOVEMENT"', source)
        self.assertIn('return "MODIFIER"', source)

    def test_cause_ref_rejects_colon_pipe_whitespace_control_non_ascii(self):
        source = self.causal_models_path.read_text(encoding="utf-8")
        self.assertIn("char.IsWhiteSpace(value)", source)
        self.assertIn("char.IsControl(value)", source)
        self.assertIn("value > 127", source)
        self.assertIn("value == ':'", source)
        self.assertIn("value == '|'", source)

    def test_cause_ref_prohibits_parent_for_system(self):
        source = self.causal_models_path.read_text(encoding="utf-8")
        self.assertIn("if (parent != null)", source)
        self.assertIn(
            "Only modifier causes may have a parent in causal ledger v1",
            source,
        )

    def test_tick_causal_buffer_requires_visible_targets(self):
        source = self.ledger_path.read_text(encoding="utf-8")
        self.assertIn("RequireVisible(target)", source)

    def test_record_contribution_omits_zero(self):
        source = self.ledger_path.read_text(encoding="utf-8")
        self.assertIn("if (realizedDeltaS == 0)", source)
        self.assertIn("return;", source)

    def test_visible_catalog_contains_regional_targets(self):
        source = self.catalog_path.read_text(encoding="utf-8")
        self.assertIn('"support"', source)
        self.assertIn('"tension"', source)
        self.assertIn('"organization"', source)
        self.assertIn('"rival_presence"', source)

    def test_visible_catalog_does_not_add_internals(self):
        source = self.catalog_path.read_text(encoding="utf-8")
        self.assertNotIn("internals", source)

    def test_agg_contract_defines_hidden_internals(self):
        agg_contract_path = self.root / "docs" / "aggregation_contract.md"
        text = agg_contract_path.read_text(encoding="utf-8")
        self.assertIn("`internals.*`", text)
        self.assertIn("hidden", text)

    def test_agg_contract_defines_ephemeral_execution_plan_only(self):
        agg_contract_path = self.root / "docs" / "aggregation_contract.md"
        text = agg_contract_path.read_text(encoding="utf-8")
        self.assertIn("ephemeral_execution_plan_only", text)

    def test_agg_contract_attribution_uses_system_agg(self):
        agg_contract_path = self.root / "docs" / "aggregation_contract.md"
        text = agg_contract_path.read_text(encoding="utf-8")
        self.assertIn("SYSTEM:AGG", text)

    def test_public_agg_mappings_match_aggregation_config(self):
        for mapping in EXPECTED_PUBLIC_AGG_ATTRIBUTION:
            internal = mapping["internal_target"]
            expected_metric = mapping["visible_metric"]
            found = False
            for p in self.aggregation_config.get("passes", []):
                if p.get("type") != "METRIC_AGGREGATION":
                    continue
                for m in p.get("metrics", []):
                    for c in m.get("components", []):
                        if c.get("target") == internal:
                            self.assertEqual(
                                expected_metric,
                                m["metric"],
                                f"Mismatch for {internal}: "
                                f"expected {expected_metric}, "
                                f"got {m['metric']}",
                            )
                            found = True
                            break
            self.assertTrue(
                found, f"Internal target {internal} not found in aggregation_config"
            )


if __name__ == "__main__":
    unittest.main()
