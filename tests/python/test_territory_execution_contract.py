from __future__ import annotations

import copy
import inspect
import json
import os
import re
import unittest
from pathlib import Path
from typing import Any


ROOT = Path(__file__).resolve().parents[2]
FIXTURE_PATH = ROOT / "tests" / "territory" / "territory_execution_v1_vectors.json"

CANONICAL_REGION_ORDER = ["arica_parinacota", "tarapaca", "antofagasta", "atacama", "coquimbo", "valparaiso", "metropolitana", "ohiggins", "maule", "nuble", "biobio", "araucania", "los_rios", "los_lagos", "aysen", "magallanes"]
ALPHABETICAL_REGION_ORDER = ["antofagasta", "araucania", "arica_parinacota", "atacama", "aysen", "biobio", "coquimbo", "los_lagos", "los_rios", "magallanes", "maule", "metropolitana", "nuble", "ohiggins", "tarapaca", "valparaiso"]
CANONICAL_REGION_VALUES = {r: 1001 + i for i, r in enumerate(CANONICAL_REGION_ORDER)}

EXPECTED_TOP_LEVEL_KEYS = ["schema_version", "fixture_id", "authority_decision_id", "numeric_domain", "canonical_region_order", "regions", "constants", "cause_contract", "vector_registry", "vectors"]
EXPECTED_VECTOR_GROUP_KEYS = ["rounding", "drift", "pull", "latency", "ordering"]
EXPECTED_VECTOR_IDS = {"rounding": ["R-01", "R-02"], "drift": ["D-00", "D-01", "D-02", "D-03", "D-04", "D-05", "D-06", "D-07", "D-08", "D-08-WRONG", "D-09", "D-10"], "pull": ["P-00", "P-01", "P-02", "P-03", "P-04", "P-05"], "latency": ["L-01-T", "L-01-T1-R", "L-01-T1-A", "L-01-CAUSE"], "ordering": ["O-01", "O-02", "O-03", "O-04", "O-05"]}
EXPECTED_DRIFT_OUTPUT_KEYS = ["region_id", "metric", "target_path", "cause_key", "input", "expected"]
EXPECTED_DRIFT_EXPECTED_KEYS = ["numerator", "offsetS", "target_unclampedS", "targetS", "distanceS", "elastic_numerator", "elastic_deltaS", "capped_deltaS", "pre_finalS", "finalS", "realized_deltaS", "expected_contributions"]
EXPECTED_PULL_KEYS = ["id", "binding_id", "regional_source", "destination", "provenance_key", "ordered_region_values", "ordered_weights", "input_currentS", "expected"]
EXPECTED_PULL_EXPECTED_KEYS = ["weighted_numerator", "weighted_averageS", "targetS", "distanceS", "elastic_numerator", "elastic_deltaS", "capped_deltaS", "pre_finalS", "finalS", "realized_deltaS", "public_contributions"]
EXPECTED_BINDING_ORDER = ["support_to_coalition_strength", "organization_to_field_ops", "tension_to_protest_activity", "rival_presence_to_opposition_obstruction", "tension_to_movement_salience"]
EXPECTED_REG_TO_INT_KEYS = ["binding_id", "canonical_key", "internal_target"]
EXPECTED_SYSTEM_AGG_KEYS = ["internal_target", "visible_metric", "canonical_key"]
FORBIDDEN_IMPLICIT_KEYS = ["copy_from", "same_as", "inherit", "inherits", "template", "defaults", "derive", "derived_from", "compute", "formula", "generated", "repeat", "cross_product"]
EXPECTED_REGION_KEYS = ["region_id", "weight_ppm", "admin_capS", "industry_capS", "extractive_capS", "social_capS", "populationS"]
EXPECTED_CONSTANTS_KEYS = ["drift", "pull", "latency_ticks"]
EXPECTED_DRIFT_CONSTANT_KEYS = ["alpha_ppm", "cap_per_weekS", "target_baseS", "denominator"]
EXPECTED_PULL_CONSTANT_KEYS = ["alpha_ppm", "cap_per_weekS", "denominator"]
EXPECTED_CAUSE_CONTRACT_KEYS = ["cause_key_grammar", "hidden_pull_provenance"]
EXPECTED_VECTOR_REGISTRY_KEYS = ["rounding", "drift", "pull", "latency", "ordering", "fixture_owner", "oracle_owner"]
EXPECTED_ROUNDING_KEYS = ["id", "numerator", "denominator", "expected_result"]
EXPECTED_VALID_DRIFT_VECTOR_KEYS = ["id", "valid", "description", "outputs"]
EXPECTED_INVALID_DRIFT_VECTOR_KEYS = ["id", "valid", "description", "rejection_reason", "counterexample", "must_differ_from"]
EXPECTED_DRIFT_INPUT_KEYS = ["currentS", "metrics", "region_snapshot"]
EXPECTED_DRIFT_METRIC_KEYS = ["legitimacyS", "economyS", "securityS", "party_organizationS", "social_tensionS", "public_agendaS", "internal_cohesionS"]
EXPECTED_REGION_SNAPSHOT_KEYS = ["supportS", "tensionS", "organizationS", "rival_presenceS"]
EXPECTED_CONTRIBUTION_KEYS = ["target", "cause_key", "deltaS"]
EXPECTED_D08_WRONG_COUNTEREXAMPLE_KEYS = ["numerator", "offsetS", "target_unclampedS", "targetS", "currentS", "distanceS", "elastic_numerator", "elastic_deltaS", "capped_deltaS", "pre_finalS", "finalS", "realized_deltaS"]
EXPECTED_D08_WRONG_DIFFERENCE_KEYS = ["vector_id", "field", "valid_value", "counterexample_value"]
EXPECTED_LATENCY_KEYS_BY_ID = {
    "L-01-T": ["id", "description", "drift_inputs", "current_supportS", "drift_outputs", "pull_input_currentS", "pull_expected", "pull_legislative_capacity_during_tick_T", "same_tick_phase_8_reexecution"],
    "L-01-T1-R": ["id", "description", "pass", "target", "currentS", "midS", "alpha_ppm", "distanceS", "elastic_numerator", "rounded_deltaS", "finalS"],
    "L-01-T1-A": ["id", "description", "pass", "target", "coalition_strengthS", "party_disciplineS", "opposition_obstructionS", "senate_inertiaS", "coalition_weight_ppm", "weighted_offset_numerator", "weighted_offsetS", "targetS", "current_metricS", "alpha_ppm", "distanceS", "elastic_numerator", "elastic_deltaS", "capped_deltaS", "finalS", "delta_totalS"],
    "L-01-CAUSE": ["id", "description", "target", "internal_target", "cause_key", "deltaS", "public"],
}
EXPECTED_ORDERING_KEYS_BY_ID = {
    "O-01": ["id", "description", "canonical_region_order", "stored_alphabetical_order", "values_by_region", "stored_alphabetical_pairs", "expected_order", "expected_ordered_pairs", "expected_result_independent_of_stored_order"],
    "O-02": ["id", "description", "insertion_order_a", "insertion_order_b", "canonical_region_order", "expected_ordered_pairs", "expected_canonical_bytes_equal"],
    "O-03": ["id", "description", "binding_order"],
    "O-04": ["id", "description", "source", "outputs", "expected_output_count", "collapsed"],
    "O-05": ["id", "description", "input_a", "input_b", "canonical_serialization", "expected_bytes_equal"],
}
FORBIDDEN_ACTIVE_REFORM_TERMS = ["active_reform_bias", "reform_bias", "REG_REFORM_BIAS", "effective_clout", "support_bias", "tension_bias", "PR_19_4"]

# ── Oracle constants ──────────────────────────────────────────────────────────
# Independently defined; NOT read from fixture["constants"].
ORACLE_MIN_S = 0
ORACLE_MAX_S = 10000
ORACLE_MID_S = 5000
ORACLE_PPM_DENOMINATOR = 1000000
ORACLE_I64_MIN = -9223372036854775808
ORACLE_I64_MAX = 9223372036854775807
ORACLE_DRIFT_ALPHA_PPM = 109101
ORACLE_DRIFT_CAP_S = 200
ORACLE_PULL_ALPHA_PPM = 206299
ORACLE_PULL_CAP_S = 400
ORACLE_REVERSION_LEG_ALPHA_PPM = 34064
ORACLE_LEGISLATIVE_ALPHA_PPM = 206299
ORACLE_LEGISLATIVE_CAP_S = 400
ORACLE_LEGISLATIVE_WEIGHTS: dict[str, int] = {
    "coalition_strengthS": 350000,
    "party_disciplineS": 350000,
    "opposition_obstructionS": -200000,
    "senate_inertiaS": -100000,
}
ORACLE_DRIFT_METRIC_ORDER = [
    "support", "tension", "organization", "rival_presence",
]
ORACLE_DRIFT_INPUT_KEYS = [
    "legitimacyS", "economyS", "securityS",
    "party_organizationS", "social_tensionS",
    "public_agendaS", "internal_cohesionS",
]
ORACLE_PULL_BINDINGS: list[dict[str, str]] = [
    {"id": "support_to_coalition_strength", "regional_source": "support", "destination": "internals.leg.coalition_strength"},
    {"id": "organization_to_field_ops", "regional_source": "organization", "destination": "internals.party.field_ops"},
    {"id": "tension_to_protest_activity", "regional_source": "tension", "destination": "internals.tension.protest_activity"},
    {"id": "rival_presence_to_opposition_obstruction", "regional_source": "rival_presence", "destination": "internals.leg.opposition_obstruction"},
    {"id": "tension_to_movement_salience", "regional_source": "tension", "destination": "internals.agenda.movement_salience"},
]
ORACLE_CANONICAL_JSON_CONFIG: dict[str, object] = {
    "ensure_ascii": False,
    "sort_keys": True,
    "separators": [",", ":"],
    "allow_nan": False,
    "newline_terminated": True,
}


def oracle_canonical_json(value: object) -> str:
    config = ORACLE_CANONICAL_JSON_CONFIG
    serialized = json.dumps(
        value,
        ensure_ascii=config["ensure_ascii"],
        sort_keys=config["sort_keys"],
        separators=tuple(config["separators"]),
        allow_nan=config["allow_nan"],
    )
    if config["newline_terminated"]:
        serialized += "\n"
    return serialized


def validate_drift_cause_key(region_id: str, metric: str, cause_key: str) -> list[str]:
    errors = []
    if not isinstance(cause_key, str):
        errors.append("EXEC_DRIFT_CAUSE_GRAMMAR")
        return errors
    colon_count = cause_key.count(":")
    if colon_count != 1:
        errors.append("EXEC_DRIFT_CAUSE_GRAMMAR")
        return errors
    if ".regions." not in cause_key:
        errors.append("EXEC_DRIFT_CAUSE_GRAMMAR")
        return errors
    cat, rest = cause_key.split(":", 1)
    if cat != "SYSTEM":
        errors.append("EXEC_DRIFT_CAUSE_GRAMMAR")
        return errors
    expected_prefix = f"SYSTEM:REG_DRIFT.regions.{region_id}.{metric}"
    if cause_key != expected_prefix:
        errors.append("EXEC_DRIFT_CAUSE_GRAMMAR")
    return errors


def collect_execution_fixture_errors(fixture: dict) -> list[str]:
    errors = []
    constants = fixture.get("constants", {})
    drift_const = constants.get("drift", {})
    pull_const = constants.get("pull", {})

    if drift_const.get("alpha_ppm") != 109101:
        errors.append("EXEC_DRIFT_ALPHA")
    if drift_const.get("cap_per_weekS") != 200:
        errors.append("EXEC_DRIFT_CAP")
    if pull_const.get("alpha_ppm") != 206299:
        errors.append("EXEC_PULL_ALPHA")
    if pull_const.get("cap_per_weekS") != 400:
        errors.append("EXEC_PULL_CAP")

    cro = fixture.get("canonical_region_order", [])
    if cro != CANONICAL_REGION_ORDER:
        errors.append("EXEC_REGION_ORDER")

    regions = fixture.get("regions", [])
    actual_ids = [r.get("region_id", "") for r in regions]
    if actual_ids != CANONICAL_REGION_ORDER:
        errors.append("EXEC_REGION_ORDER")

    total_ppm = sum(r.get("weight_ppm", 0) for r in regions)
    if total_ppm != 1000000:
        errors.append("WEIGHT_SUM")

    vectors = fixture.get("vectors", {})
    drift_vectors = vectors.get("drift", [])
    valid_drift_vectors = [dv for dv in drift_vectors if dv.get("valid")]
    for dv in valid_drift_vectors:
        for out in dv.get("outputs", []):
            ce = validate_drift_cause_key(out.get("region_id", ""), out.get("metric", ""), out.get("cause_key", ""))
            if ce:
                errors.extend(ce)

    d08 = None
    for dv in valid_drift_vectors:
        if dv.get("id") == "D-08":
            d08 = dv
            break
    if d08:
        outputs = d08.get("outputs", [])
        for out in outputs:
            if out.get("metric") == "support":
                if out.get("expected", {}).get("finalS") != 4200:
                    errors.append("EXEC_PRE_DRIFT_SNAPSHOT")
            if out.get("metric") == "rival_presence":
                snap = out.get("input", {}).get("region_snapshot", {})
                if snap.get("supportS") != 4000:
                    errors.append("EXEC_PRE_DRIFT_SNAPSHOT")
                if out.get("expected", {}).get("finalS") != 5076:
                    errors.append("EXEC_PRE_DRIFT_SNAPSHOT")

    cause_contract = fixture.get("cause_contract", {})
    if "cause_key_grammar" not in cause_contract:
        errors.append("EXEC_DRIFT_CAUSE_GRAMMAR")
    if "hidden_pull_provenance" not in cause_contract:
        errors.append("PULL_PROVENANCE_MISSING")

    vector_registry = fixture.get("vector_registry", {})
    if set(vector_registry.keys()) != {"rounding", "drift", "pull", "latency", "ordering", "fixture_owner", "oracle_owner"}:
        errors.append("VECTOR_REGISTRY_KEYS")

    pull_prov = cause_contract.get("hidden_pull_provenance", {}).get("pull_provenance", {})
    if pull_prov.get("public_ledger") is not False:
        errors.append("EXEC_PULL_PROVENANCE_PUBLIC")
    if pull_prov.get("tick_causal_buffer") is not False:
        errors.append("EXEC_PULL_PROVENANCE_PUBLIC")

    pull_vectors = vectors.get("pull", [])
    for pv in pull_vectors:
        pub = pv.get("expected", {}).get("public_contributions", None)
        if pub is not None and pub != []:
            errors.append("EXEC_PULL_PROVENANCE_PUBLIC")
        if pv.get("id") == "P-05":
            actual_weighted_averageS = oracle_pull(
                pv.get("ordered_region_values", []),
                pv.get("ordered_weights", []),
                pv.get("input_currentS", 0),
            )["weighted_averageS"]
            if pv.get("expected", {}).get("weighted_averageS") != actual_weighted_averageS:
                errors.append("EXEC_WEIGHTED_ROUNDING_STAGE")

    for region in regions:
        if "active_reform_bias" in region:
            errors.append("EXEC_ACTIVE_REFORM_PRESENT")

    return errors


def mutant_truncating_divide(numerator: int, denominator: int) -> int:
    if denominator <= 0:
        raise ValueError("denominator must be positive")
    quotient, _ = oracle_truncating_divmod_positive_denominator(numerator, denominator)
    return quotient


def mutant_per_region_weighted_rounding(values: list[int], weights: list[int]) -> int:
    if len(values) != 16 or len(weights) != 16:
        raise ValueError("need 16 values and weights")
    components = []
    for v, w in zip(values, weights):
        term = (v * w) // 1000000
        rounded = term + 1 if (v * w) % 1000000 >= 500000 else term
        components.append(rounded)
    return sum(components)


def reject_duplicate_pairs(pairs):
    keys = [p[0] for p in pairs]
    if len(keys) != len(set(keys)):
        dupes = {k for k in keys if keys.count(k) > 1}
        raise ValueError(f"Duplicate keys: {dupes}")
    return dict(pairs)


def reject_nonfinite_constant(value):
    raise ValueError(f"Non-finite JSON constant: {value}")


def strict_json_loads(text: str):
    return json.loads(
        text,
        object_pairs_hook=reject_duplicate_pairs,
        parse_constant=reject_nonfinite_constant,
    )


def load_fixture() -> dict:
    raw = FIXTURE_PATH.read_bytes()
    return strict_json_loads(raw.decode("utf-8"))


def collect_keys_and_strings(obj, keys, strings):
    if isinstance(obj, dict):
        for k, v in obj.items():
            keys.add(k)
            collect_keys_and_strings(v, keys, strings)
    elif isinstance(obj, list):
        for item in obj:
            collect_keys_and_strings(item, keys, strings)
    elif isinstance(obj, str):
        strings.add(obj)
    elif isinstance(obj, (int, float)):
        pass


def find_forbidden_terms(obj, forbidden_terms):
    matches = []
    terms_cf = [t.casefold() for t in forbidden_terms]

    def _walk(o, path):
        if isinstance(o, dict):
            for k, v in o.items():
                k_cf = k.casefold()
                for i, tc in enumerate(terms_cf):
                    if tc == k_cf or tc in k_cf:
                        matches.append(f"{path}.{k}")
                        break
                _walk(v, f"{path}.{k}")
        elif isinstance(o, list):
            for idx, item in enumerate(o):
                _walk(item, f"{path}[{idx}]")
        elif isinstance(o, str):
            s_cf = o.casefold()
            for i, tc in enumerate(terms_cf):
                if tc == s_cf or tc in s_cf:
                    matches.append(f'{path}="{o}"')
                    break

    _walk(obj, "$")
    return matches


# ── Oracle helpers (independent; no reference to "expected", load_fixture, or production code) ──

def oracle_checked_i64(value: Any, context: str = "") -> int:
    if isinstance(value, bool):
        raise TypeError(f"{context}: Expected int, got bool")
    if not isinstance(value, int):
        raise TypeError(f"{context}: Expected int, got {type(value).__name__}")
    if value < ORACLE_I64_MIN:
        raise OverflowError(f"{context}: {value} < ORACLE_I64_MIN")
    if value > ORACLE_I64_MAX:
        raise OverflowError(f"{context}: {value} > ORACLE_I64_MAX")
    return value


def oracle_checked_add_i64(left: int, right: int, context: str = "") -> int:
    l = oracle_checked_i64(left, f"{context}.left")
    r = oracle_checked_i64(right, f"{context}.right")
    result = l + r
    return oracle_checked_i64(result, f"{context}.result")


def oracle_checked_sub_i64(left: int, right: int, context: str = "") -> int:
    l = oracle_checked_i64(left, f"{context}.left")
    r = oracle_checked_i64(right, f"{context}.right")
    result = l - r
    return oracle_checked_i64(result, f"{context}.result")


def oracle_checked_mul_i64(left: int, right: int, context: str = "") -> int:
    l = oracle_checked_i64(left, f"{context}.left")
    r = oracle_checked_i64(right, f"{context}.right")
    result = l * r
    return oracle_checked_i64(result, f"{context}.result")


def oracle_round_divide_half_away_from_zero(numerator: int, denominator: int) -> int:
    quotient, remainder = oracle_truncating_divmod_positive_denominator(numerator, denominator)
    if remainder == 0:
        return quotient
    remainder_magnitude = (
        remainder if remainder >= 0
        else oracle_checked_sub_i64(0, remainder, "round_divide.remainder_magnitude")
    )
    half = denominator // 2
    round_away = remainder_magnitude > half or (denominator % 2 == 0 and remainder_magnitude == half)
    if not round_away:
        return quotient
    if numerator > 0:
        return oracle_checked_add_i64(quotient, 1, "round_divide.round_positive")
    return oracle_checked_sub_i64(quotient, 1, "round_divide.round_negative")


def oracle_clamp_int(value: int, minimum: int, maximum: int) -> int:
    if value < minimum:
        return minimum
    if value > maximum:
        return maximum
    return value


def oracle_truncating_divmod_positive_denominator(
    numerator: int,
    denominator: int,
) -> tuple[int, int]:
    oracle_checked_i64(numerator, "truncating_divmod.numerator")
    oracle_checked_i64(denominator, "truncating_divmod.denominator")
    if denominator <= 0:
        raise ValueError(f"truncating_divmod: denominator must be positive, got {denominator}")
    floor_quotient, nonnegative_remainder = divmod(numerator, denominator)
    if numerator >= 0 or nonnegative_remainder == 0:
        quotient = floor_quotient
        remainder = nonnegative_remainder
    else:
        quotient = oracle_checked_add_i64(
            floor_quotient,
            1,
            "truncating_divmod.quotient",
        )
        remainder = oracle_checked_sub_i64(
            nonnegative_remainder,
            denominator,
            "truncating_divmod.remainder",
        )
    oracle_checked_i64(quotient, "truncating_divmod.quotient_out")
    oracle_checked_i64(remainder, "truncating_divmod.remainder_out")
    return (quotient, remainder)


def oracle_drift_target_numerator(metric: str, metrics: dict[str, int], region_snapshot: dict[str, int]) -> int:
    leg = oracle_checked_i64(metrics["legitimacyS"], "metrics.legitimacyS")
    eco = oracle_checked_i64(metrics["economyS"], "metrics.economyS")
    sec = oracle_checked_i64(metrics["securityS"], "metrics.securityS")
    po = oracle_checked_i64(metrics["party_organizationS"], "metrics.party_organizationS")
    st = oracle_checked_i64(metrics["social_tensionS"], "metrics.social_tensionS")
    pa = oracle_checked_i64(metrics["public_agendaS"], "metrics.public_agendaS")
    ic = oracle_checked_i64(metrics["internal_cohesionS"], "metrics.internal_cohesionS")
    sup = oracle_checked_i64(region_snapshot["supportS"], "snapshot.supportS")
    if metric == "support":
        d_leg = oracle_checked_sub_i64(leg, ORACLE_MID_S, "support.leg")
        t1 = oracle_checked_mul_i64(600000, d_leg, "support.leg_term")
        d_po = oracle_checked_sub_i64(po, ORACLE_MID_S, "support.po")
        t2 = oracle_checked_mul_i64(300000, d_po, "support.po_term")
        d_st = oracle_checked_sub_i64(st, ORACLE_MID_S, "support.st")
        t3 = oracle_checked_mul_i64(-400000, d_st, "support.st_term")
        s = oracle_checked_add_i64(t1, t2, "support.t1_t2")
        return oracle_checked_add_i64(s, t3, "support.total")
    if metric == "tension":
        d_eco = oracle_checked_sub_i64(ORACLE_MID_S, eco, "tension.eco")
        t1 = oracle_checked_mul_i64(500000, d_eco, "tension.eco_term")
        d_sec = oracle_checked_sub_i64(ORACLE_MID_S, sec, "tension.sec")
        t2 = oracle_checked_mul_i64(400000, d_sec, "tension.sec_term")
        d_pa = oracle_checked_sub_i64(pa, ORACLE_MID_S, "tension.pa")
        t3 = oracle_checked_mul_i64(300000, d_pa, "tension.pa_term")
        s = oracle_checked_add_i64(t1, t2, "tension.t1_t2")
        return oracle_checked_add_i64(s, t3, "tension.total")
    if metric == "organization":
        d_po = oracle_checked_sub_i64(po, ORACLE_MID_S, "org.po")
        return oracle_checked_mul_i64(800000, d_po, "org.total")
    if metric == "rival_presence":
        d_sup = oracle_checked_sub_i64(ORACLE_MID_S, sup, "rival.sup")
        t1 = oracle_checked_mul_i64(700000, d_sup, "rival.sup_term")
        d_ic = oracle_checked_sub_i64(ORACLE_MID_S, ic, "rival.ic")
        t2 = oracle_checked_mul_i64(200000, d_ic, "rival.ic_term")
        return oracle_checked_add_i64(t1, t2, "rival.total")
    raise ValueError(f"Unknown drift metric: {metric}")


def oracle_drift_output(metric: str, currentS: int, metrics: dict[str, int], region_snapshot: dict[str, int], target_path: str | None = None, cause_key: str | None = None) -> dict[str, int]:
    numerator = oracle_drift_target_numerator(metric, metrics, region_snapshot)
    offsetS = oracle_round_divide_half_away_from_zero(numerator, ORACLE_PPM_DENOMINATOR)
    target_unclampedS = oracle_checked_add_i64(ORACLE_MID_S, offsetS, "drift.target_unclamped")
    targetS = oracle_clamp_int(target_unclampedS, ORACLE_MIN_S, ORACLE_MAX_S)
    distanceS = oracle_checked_sub_i64(targetS, currentS, "drift.distance")
    elastic_numerator = oracle_checked_mul_i64(distanceS, ORACLE_DRIFT_ALPHA_PPM, "drift.elastic")
    elastic_deltaS = oracle_round_divide_half_away_from_zero(elastic_numerator, ORACLE_PPM_DENOMINATOR)
    capped_deltaS = oracle_clamp_int(elastic_deltaS, -ORACLE_DRIFT_CAP_S, ORACLE_DRIFT_CAP_S)
    pre_finalS = oracle_checked_add_i64(currentS, capped_deltaS, "drift.prefinal")
    finalS = oracle_clamp_int(pre_finalS, ORACLE_MIN_S, ORACLE_MAX_S)
    realized_deltaS = oracle_checked_sub_i64(finalS, currentS, "drift.realized_delta")
    contributions: list[dict[str, object]] = []
    if realized_deltaS != 0 and target_path is not None and cause_key is not None:
        contributions = [{"target": target_path, "cause_key": cause_key, "deltaS": realized_deltaS}]
    return {
        "numerator": numerator,
        "offsetS": offsetS,
        "target_unclampedS": target_unclampedS,
        "targetS": targetS,
        "distanceS": distanceS,
        "elastic_numerator": elastic_numerator,
        "elastic_deltaS": elastic_deltaS,
        "capped_deltaS": capped_deltaS,
        "pre_finalS": pre_finalS,
        "finalS": finalS,
        "realized_deltaS": realized_deltaS,
        "expected_contributions": contributions,
    }


def oracle_pull(ordered_region_values: list[int], ordered_weights: list[int], input_currentS: int) -> dict[str, Any]:
    if len(ordered_region_values) != 16:
        raise ValueError(f"Expected 16 values, got {len(ordered_region_values)}")
    if len(ordered_weights) != 16:
        raise ValueError(f"Expected 16 weights, got {len(ordered_weights)}")
    weighted_numerator = 0
    for i in range(16):
        v = oracle_checked_i64(ordered_region_values[i], f"pull.values[{i}]")
        w = oracle_checked_i64(ordered_weights[i], f"pull.weights[{i}]")
        term = oracle_checked_mul_i64(v, w, f"pull.term[{i}]")
        weighted_numerator = oracle_checked_add_i64(weighted_numerator, term, f"pull.accum[{i}]")
    weighted_averageS = oracle_round_divide_half_away_from_zero(weighted_numerator, ORACLE_PPM_DENOMINATOR)
    targetS = oracle_clamp_int(weighted_averageS, ORACLE_MIN_S, ORACLE_MAX_S)
    distanceS = oracle_checked_sub_i64(targetS, input_currentS, "pull.distance")
    elastic_numerator = oracle_checked_mul_i64(distanceS, ORACLE_PULL_ALPHA_PPM, "pull.elastic")
    elastic_deltaS = oracle_round_divide_half_away_from_zero(elastic_numerator, ORACLE_PPM_DENOMINATOR)
    capped_deltaS = oracle_clamp_int(elastic_deltaS, -ORACLE_PULL_CAP_S, ORACLE_PULL_CAP_S)
    pre_finalS = oracle_checked_add_i64(input_currentS, capped_deltaS, "pull.prefinal")
    finalS = oracle_clamp_int(pre_finalS, ORACLE_MIN_S, ORACLE_MAX_S)
    realized_deltaS = oracle_checked_sub_i64(finalS, input_currentS, "pull.realized_delta")
    return {
        "weighted_numerator": weighted_numerator,
        "weighted_averageS": weighted_averageS,
        "targetS": targetS,
        "distanceS": distanceS,
        "elastic_numerator": elastic_numerator,
        "elastic_deltaS": elastic_deltaS,
        "capped_deltaS": capped_deltaS,
        "pre_finalS": pre_finalS,
        "finalS": finalS,
        "realized_deltaS": realized_deltaS,
        "public_contributions": [],
    }


def oracle_reversion(currentS: int, midS: int, alpha_ppm: int) -> dict[str, int]:
    distanceS = oracle_checked_sub_i64(midS, currentS, "reversion.distance")
    elastic_numerator = oracle_checked_mul_i64(distanceS, alpha_ppm, "reversion.elastic")
    rounded_deltaS = oracle_round_divide_half_away_from_zero(elastic_numerator, ORACLE_PPM_DENOMINATOR)
    pre_finalS = oracle_checked_add_i64(currentS, rounded_deltaS, "reversion.prefinal")
    finalS = oracle_clamp_int(pre_finalS, ORACLE_MIN_S, ORACLE_MAX_S)
    return {
        "distanceS": distanceS,
        "elastic_numerator": elastic_numerator,
        "rounded_deltaS": rounded_deltaS,
        "finalS": finalS,
    }


def oracle_legislative_capacity_t1(
    coalition_strengthS: int,
    party_disciplineS: int,
    opposition_obstructionS: int,
    senate_inertiaS: int,
    current_metricS: int,
) -> dict[str, int]:
    w = ORACLE_LEGISLATIVE_WEIGHTS
    d_cs = oracle_checked_sub_i64(coalition_strengthS, ORACLE_MID_S, "leg.cs")
    t1 = oracle_checked_mul_i64(w["coalition_strengthS"], d_cs, "leg.cs_term")
    d_pd = oracle_checked_sub_i64(party_disciplineS, ORACLE_MID_S, "leg.pd")
    t2 = oracle_checked_mul_i64(w["party_disciplineS"], d_pd, "leg.pd_term")
    d_oo = oracle_checked_sub_i64(opposition_obstructionS, ORACLE_MID_S, "leg.oo")
    t3 = oracle_checked_mul_i64(w["opposition_obstructionS"], d_oo, "leg.oo_term")
    d_si = oracle_checked_sub_i64(senate_inertiaS, ORACLE_MID_S, "leg.si")
    t4 = oracle_checked_mul_i64(w["senate_inertiaS"], d_si, "leg.si_term")
    t12 = oracle_checked_add_i64(t1, t2, "leg.t1_t2")
    t34 = oracle_checked_add_i64(t3, t4, "leg.t3_t4")
    weighted_offset_numerator = oracle_checked_add_i64(t12, t34, "leg.total")
    weighted_offsetS = oracle_round_divide_half_away_from_zero(weighted_offset_numerator, ORACLE_PPM_DENOMINATOR)
    targetS = oracle_clamp_int(oracle_checked_add_i64(ORACLE_MID_S, weighted_offsetS, "leg.target_base"), ORACLE_MIN_S, ORACLE_MAX_S)
    distanceS = oracle_checked_sub_i64(targetS, current_metricS, "leg.distance")
    elastic_numerator = oracle_checked_mul_i64(distanceS, ORACLE_LEGISLATIVE_ALPHA_PPM, "legislative.elastic")
    elastic_deltaS = oracle_round_divide_half_away_from_zero(elastic_numerator, ORACLE_PPM_DENOMINATOR)
    capped_deltaS = oracle_clamp_int(elastic_deltaS, -ORACLE_LEGISLATIVE_CAP_S, ORACLE_LEGISLATIVE_CAP_S)
    finalS = oracle_clamp_int(oracle_checked_add_i64(current_metricS, capped_deltaS, "leg.final"), ORACLE_MIN_S, ORACLE_MAX_S)
    delta_totalS = oracle_checked_sub_i64(finalS, current_metricS, "leg.delta_total")
    return {
        "coalition_weight_ppm": w["coalition_strengthS"],
        "weighted_offset_numerator": weighted_offset_numerator,
        "weighted_offsetS": weighted_offsetS,
        "targetS": targetS,
        "distanceS": distanceS,
        "elastic_numerator": elastic_numerator,
        "elastic_deltaS": elastic_deltaS,
        "capped_deltaS": capped_deltaS,
        "finalS": finalS,
        "delta_totalS": delta_totalS,
    }


class TerritoryExecutionV1FixtureTest(unittest.TestCase):

    def assert_drift_output_schema_and_contributions(self, output: dict, context: str) -> None:
        self.assertEqual(list(output.keys()), EXPECTED_DRIFT_OUTPUT_KEYS, f"{context} output keys")
        inp = output["input"]
        self.assertEqual(list(inp.keys()), EXPECTED_DRIFT_INPUT_KEYS, f"{context} input keys")
        self.assertEqual(list(inp["metrics"].keys()), EXPECTED_DRIFT_METRIC_KEYS, f"{context} metrics keys")
        self.assertEqual(list(inp["region_snapshot"].keys()), EXPECTED_REGION_SNAPSHOT_KEYS, f"{context} snapshot keys")
        exp = output["expected"]
        self.assertEqual(list(exp.keys()), EXPECTED_DRIFT_EXPECTED_KEYS, f"{context} expected keys")
        contributions = exp["expected_contributions"]
        if exp["realized_deltaS"] == 0:
            self.assertEqual(contributions, [], f"{context} zero-delta contributions")
        else:
            self.assertEqual(len(contributions), 1, f"{context} expected 1 contribution")
            c = contributions[0]
            self.assertEqual(list(c.keys()), EXPECTED_CONTRIBUTION_KEYS, f"{context} contribution keys")
            self.assertEqual(c["target"], output["target_path"], f"{context} contribution target")
            self.assertEqual(c["cause_key"], output["cause_key"], f"{context} contribution cause_key")
            self.assertEqual(c["deltaS"], exp["realized_deltaS"], f"{context} contribution deltaS")

    def test_strict_json_rejects_duplicates_and_nonfinite_numbers(self):
        for text, label in [
            ('{"a": 1, "a": 2}', "duplicate keys"),
            ('{"x": NaN}', "NaN"),
            ('{"x": Infinity}', "Infinity"),
            ('{"x": -Infinity}', "-Infinity"),
        ]:
            with self.subTest(case=label):
                with self.assertRaises((ValueError, json.JSONDecodeError)):
                    strict_json_loads(text)
        load_fixture()

    def test_fixture_exists_and_is_utf8_lf_without_bom(self):
        self.assertTrue(FIXTURE_PATH.exists())
        raw = FIXTURE_PATH.read_bytes()
        self.assertFalse(raw.startswith(b"\xef\xbb\xbf"), "BOM detected")
        self.assertNotIn(b"\r\n", raw, "CRLF detected")
        self.assertNotIn(b"\r", raw, "CR detected")
        raw.decode("utf-8")

    def test_fixture_top_level_keys_are_exact_and_ordered(self):
        data = load_fixture()
        self.assertEqual(list(data.keys()), EXPECTED_TOP_LEVEL_KEYS)

    def test_fixture_identity_and_schema_are_exact(self):
        data = load_fixture()
        self.assertEqual(data["schema_version"], 1)
        self.assertEqual(data["fixture_id"], "territory_execution_v1")
        self.assertEqual(data["authority_decision_id"], "MVP-013-territory-feedback")

    def test_fixture_matches_mvp_013_vector_registry(self):
        data = load_fixture()
        vr = data["vector_registry"]
        self.assertEqual(vr["fixture_owner"], "PR_15_1_J")
        self.assertEqual(vr["oracle_owner"], "PR_15_1_K")
        for grp in EXPECTED_VECTOR_GROUP_KEYS:
            self.assertEqual(vr[grp], EXPECTED_VECTOR_IDS[grp], f"Registry mismatch: {grp}")

    def test_canonical_region_order_is_exact(self):
        data = load_fixture()
        self.assertEqual(data["canonical_region_order"], CANONICAL_REGION_ORDER)

    def test_regions_are_explicit_unique_and_complete(self):
        data = load_fixture()
        regions = data["regions"]
        self.assertEqual(len(regions), 16)
        seen = set()
        for i, r in enumerate(regions):
            rid = r["region_id"]
            self.assertEqual(rid, CANONICAL_REGION_ORDER[i], f"Region {i} order mismatch")
            self.assertNotIn(rid, seen, f"Duplicate region_id: {rid}")
            seen.add(rid)
            for k in ("admin_capS", "industry_capS", "extractive_capS", "social_capS", "populationS"):
                self.assertIn(k, r, f"Region {rid} missing {k}")
                self.assertEqual(r[k], 5000, f"Region {rid} {k} != 5000")

    def test_region_weights_sum_to_one_million(self):
        data = load_fixture()
        total = sum(r["weight_ppm"] for r in data["regions"])
        self.assertEqual(total, 1000000)
        for r in data["regions"]:
            self.assertEqual(r["weight_ppm"], 62500)

    def test_static_resources_are_explicit_and_exact(self):
        data = load_fixture()
        for r in data["regions"]:
            for k in ("admin_capS", "industry_capS", "extractive_capS", "social_capS", "populationS"):
                self.assertEqual(r[k], 5000, f"{r['region_id']}.{k}")

    def test_numeric_domain_matches_mvp_013(self):
        data = load_fixture()
        nd = data["numeric_domain"]
        for k, v in [("scale", 100), ("hundredS", 10000), ("midS", 5000), ("ppm_denominator", 1000000), ("stored_type", "int"), ("intermediate_type", "checked_long"), ("rounding", "HALF_AWAY_FROM_ZERO"), ("rounding_authority", "FixedMath.RoundDivide"), ("target_clamp_authority", "TargetConfig"), ("publication_operation", "SET")]:
            self.assertEqual(nd[k], v, f"numeric_domain.{k}")
        self.assertEqual(nd["forbidden_numeric_types"], ["float", "double", "decimal"])
        self.assertEqual(nd["forbidden_behaviors"], ["Math.Round", "divide_before_weighted_sum_complete", "round_per_component", "silent_saturation", "unchecked_overflow", "unchecked_cast", "hardcoded_target_clamp"])

    def test_constants_match_mvp_013(self):
        data = load_fixture()
        c = data["constants"]
        self.assertEqual(c["drift"]["alpha_ppm"], 109101)
        self.assertEqual(c["drift"]["cap_per_weekS"], 200)
        self.assertEqual(c["drift"]["target_baseS"], 5000)
        self.assertEqual(c["drift"]["denominator"], 1000000)
        self.assertEqual(c["pull"]["alpha_ppm"], 206299)
        self.assertEqual(c["pull"]["cap_per_weekS"], 400)
        self.assertEqual(c["pull"]["denominator"], 1000000)
        self.assertEqual(c["latency_ticks"], 1)

    def test_cause_contract_exact_keys_and_mvp_013_parity(self):
        data = load_fixture()
        cc = data["cause_contract"]
        self.assertEqual(list(cc.keys()), EXPECTED_CAUSE_CONTRACT_KEYS)
        with open(ROOT / "docs" / "mvp_contract_decisions.json") as f:
            decisions = json.load(f)
        mvp013 = None
        for d in decisions["decisions"]:
            if d["id"] == "MVP-013-territory-feedback":
                mvp013 = d["resolution"]
                break
        self.assertIsNotNone(mvp013, "MVP-013-territory-feedback not found")
        self.assertEqual(cc["cause_key_grammar"], mvp013["cause_key_grammar"], "cause_key_grammar must match MVP-013")
        self.assertEqual(cc["hidden_pull_provenance"], mvp013["hidden_pull_provenance"], "hidden_pull_provenance must match MVP-013")

    def test_all_vector_ids_are_exact_unique_and_ordered(self):
        data = load_fixture()
        for grp in EXPECTED_VECTOR_GROUP_KEYS:
            actual_ids = [v["id"] for v in data["vectors"][grp]]
            self.assertEqual(actual_ids, EXPECTED_VECTOR_IDS[grp], f"IDs mismatch for {grp}")
            self.assertEqual(len(set(actual_ids)), len(actual_ids), f"Duplicate IDs in {grp}")

    def test_rounding_vectors_are_explicit(self):
        data = load_fixture()
        r01 = data["vectors"]["rounding"][0]
        self.assertEqual(r01["id"], "R-01")
        self.assertEqual(r01["numerator"], 500000)
        self.assertEqual(r01["denominator"], 1000000)
        self.assertEqual(r01["expected_result"], 1)
        r02 = data["vectors"]["rounding"][1]
        self.assertEqual(r02["id"], "R-02")
        self.assertEqual(r02["numerator"], -500000)
        self.assertEqual(r02["denominator"], 1000000)
        self.assertEqual(r02["expected_result"], -1)

    def test_schema_closed_regions_and_constants(self):
        data = load_fixture()
        for r in data["regions"]:
            self.assertEqual(list(r.keys()), EXPECTED_REGION_KEYS, f"Region keys: {r['region_id']}")
        c = data["constants"]
        self.assertEqual(list(c.keys()), EXPECTED_CONSTANTS_KEYS)
        self.assertEqual(list(c["drift"].keys()), EXPECTED_DRIFT_CONSTANT_KEYS)
        self.assertEqual(list(c["pull"].keys()), EXPECTED_PULL_CONSTANT_KEYS)
        self.assertEqual(list(data["vector_registry"].keys()), EXPECTED_VECTOR_REGISTRY_KEYS)

    def test_schema_closed_cause_contract(self):
        data = load_fixture()
        self.assertEqual(list(data["cause_contract"].keys()), EXPECTED_CAUSE_CONTRACT_KEYS)

    def test_schema_closed_rounding_and_drift(self):
        data = load_fixture()
        for v in data["vectors"]["rounding"]:
            self.assertEqual(list(v.keys()), EXPECTED_ROUNDING_KEYS, f"Rounding keys: {v['id']}")
        for v in data["vectors"]["drift"]:
            if v["valid"]:
                self.assertEqual(list(v.keys()), EXPECTED_VALID_DRIFT_VECTOR_KEYS, f"Drift keys: {v['id']}")
            else:
                self.assertEqual(list(v.keys()), EXPECTED_INVALID_DRIFT_VECTOR_KEYS, f"Drift keys: {v['id']}")
            if not v["valid"]:
                continue
            for out in v["outputs"]:
                self.assertEqual(list(out.keys()), EXPECTED_DRIFT_OUTPUT_KEYS, f"Output keys: {v['id']}")
                self.assertEqual(list(out["expected"].keys()), EXPECTED_DRIFT_EXPECTED_KEYS, f"Expected keys: {v['id']}")
                self.assertEqual(list(out["input"].keys()), EXPECTED_DRIFT_INPUT_KEYS, f"Input keys: {v['id']}")
                self.assertEqual(list(out["input"]["metrics"].keys()), EXPECTED_DRIFT_METRIC_KEYS, f"Metrics keys: {v['id']}")
                self.assertEqual(list(out["input"]["region_snapshot"].keys()), EXPECTED_REGION_SNAPSHOT_KEYS, f"Snapshot keys: {v['id']}")

    def test_schema_closed_pull_latency_ordering(self):
        data = load_fixture()
        for v in data["vectors"]["pull"]:
            self.assertEqual(list(v.keys()), EXPECTED_PULL_KEYS, f"Pull keys: {v['id']}")
            self.assertEqual(list(v["expected"].keys()), EXPECTED_PULL_EXPECTED_KEYS, f"Pull expected keys: {v['id']}")
        for v in data["vectors"]["latency"]:
            self.assertIn(v["id"], EXPECTED_LATENCY_KEYS_BY_ID, f"Unknown latency: {v['id']}")
            self.assertEqual(list(v.keys()), EXPECTED_LATENCY_KEYS_BY_ID[v["id"]], f"Latency keys: {v['id']}")
        for v in data["vectors"]["ordering"]:
            self.assertIn(v["id"], EXPECTED_ORDERING_KEYS_BY_ID, f"Unknown ordering: {v['id']}")
            self.assertEqual(list(v.keys()), EXPECTED_ORDERING_KEYS_BY_ID[v["id"]], f"Ordering keys: {v['id']}")

    def test_schema_closed_d08_wrong(self):
        data = load_fixture()
        wrong = [v for v in data["vectors"]["drift"] if v["id"] == "D-08-WRONG"][0]
        self.assertEqual(list(wrong["counterexample"].keys()), EXPECTED_D08_WRONG_COUNTEREXAMPLE_KEYS)
        self.assertEqual(list(wrong["must_differ_from"].keys()), EXPECTED_D08_WRONG_DIFFERENCE_KEYS)

    def test_d00_materializes_all_64_outputs(self):
        data = load_fixture()
        d00 = data["vectors"]["drift"][0]
        self.assertEqual(d00["id"], "D-00")
        self.assertTrue(d00["valid"])
        expected_pairs = [
            (region_id, metric)
            for region_id in CANONICAL_REGION_ORDER
            for metric in ["support", "tension", "organization", "rival_presence"]
        ]
        self.assertEqual(len(d00["outputs"]), 64)
        actual_pairs = [(o["region_id"], o["metric"]) for o in d00["outputs"]]
        self.assertEqual(actual_pairs, expected_pairs, "D-00 must be exact 16x4 cartesian product")
        seen = set()
        for out in d00["outputs"]:
            key = (out["region_id"], out["metric"])
            self.assertNotIn(key, seen, f"Duplicate: {key}")
            seen.add(key)
            self.assertEqual(out["expected"]["finalS"], 5000)
            self.assertEqual(out["expected"]["realized_deltaS"], 0)
            self.assertEqual(out["expected"]["expected_contributions"], [])
            expected_target = f"regions.{out['region_id']}.{out['metric']}"
            expected_cause = f"SYSTEM:REG_DRIFT.regions.{out['region_id']}.{out['metric']}"
            self.assertEqual(out["target_path"], expected_target, f"target_path: {key}")
            self.assertEqual(out["cause_key"], expected_cause, f"cause_key: {key}")

    def test_drift_intermediates_and_contributions(self):
        data = load_fixture()
        for dv in data["vectors"]["drift"]:
            if not dv["valid"]:
                continue
            for out in dv["outputs"]:
                with self.subTest(vector=dv["id"], region=out["region_id"], metric=out["metric"]):
                    self.assert_drift_output_schema_and_contributions(out, f"{dv['id']}.{out['region_id']}.{out['metric']}")

    def test_d08_uses_pre_drift_support(self):
        data = load_fixture()
        d08 = [d for d in data["vectors"]["drift"] if d["id"] == "D-08"][0]
        self.assertTrue(d08["valid"])
        self.assertEqual(len(d08["outputs"]), 2)
        self.assertEqual(d08["outputs"][0]["metric"], "support")
        self.assertEqual(d08["outputs"][0]["expected"]["finalS"], 4200)
        self.assertEqual(d08["outputs"][1]["metric"], "rival_presence")
        self.assertEqual(d08["outputs"][1]["expected"]["finalS"], 5076)
        self.assertEqual(d08["outputs"][1]["input"]["region_snapshot"]["supportS"], 4000)

    def test_d08_wrong_is_rejected_counterexample(self):
        data = load_fixture()
        wrong = [d for d in data["vectors"]["drift"] if d["id"] == "D-08-WRONG"][0]
        self.assertFalse(wrong["valid"])
        self.assertEqual(wrong["rejection_reason"], "uses_post_drift_support_for_rival_presence")
        self.assertEqual(wrong["counterexample"]["finalS"], 5061)
        self.assertEqual(wrong["must_differ_from"]["vector_id"], "D-08")
        self.assertEqual(wrong["must_differ_from"]["field"], "rival_presence.finalS")
        self.assertEqual(wrong["must_differ_from"]["valid_value"], 5076)
        self.assertEqual(wrong["must_differ_from"]["counterexample_value"], 5061)

    def test_pull_vectors_explicit(self):
        data = load_fixture()
        for pv in data["vectors"]["pull"]:
            self.assertEqual(len(pv["ordered_region_values"]), 16, f"{pv['id']} values")
            self.assertEqual(len(pv["ordered_weights"]), 16, f"{pv['id']} weights")
            for w in pv["ordered_weights"]:
                self.assertEqual(w, 62500)
            self.assertEqual(pv["expected"]["public_contributions"], [], f"{pv['id']} public_contributions")

    def test_p05_freezes_single_rounding_case(self):
        p05 = [p for p in load_fixture()["vectors"]["pull"] if p["id"] == "P-05"][0]
        self.assertEqual(p05["expected"]["weighted_averageS"], 5001)
        self.assertEqual(p05["expected"]["elastic_deltaS"], 0)
        self.assertEqual(p05["expected"]["finalS"], 5000)
        self.assertEqual(p05["expected"]["realized_deltaS"], 0)

    def test_latency_vectors_are_explicit_and_exact(self):
        data = load_fixture()
        lat = data["vectors"]["latency"]
        self.assertEqual(len(lat), 4)
        l01t = lat[0]
        self.assertEqual(l01t["id"], "L-01-T")
        self.assertEqual(len(l01t["drift_outputs"]), 16)
        self.assertEqual(l01t["pull_expected"]["finalS"], 5013)
        self.assertEqual(l01t["pull_legislative_capacity_during_tick_T"], 5000)
        self.assertFalse(l01t["same_tick_phase_8_reexecution"])
        self.assertEqual(lat[1]["id"], "L-01-T1-R")
        self.assertEqual(lat[1]["finalS"], 5013)
        self.assertEqual(lat[2]["id"], "L-01-T1-A")
        self.assertEqual(lat[2]["finalS"], 5001)
        self.assertEqual(lat[2]["delta_totalS"], 1)
        self.assertEqual(lat[3]["id"], "L-01-CAUSE")
        self.assertTrue(lat[3]["public"])

    def test_latency_t_has_sixteen_drift_contributions(self):
        data = load_fixture()
        l01t = data["vectors"]["latency"][0]
        for i, out in enumerate(l01t["drift_outputs"]):
            with self.subTest(drift_output=i, region=out["region_id"]):
                self.assert_drift_output_schema_and_contributions(out, f"L-01-T.drift_outputs[{i}]")
                self.assertEqual(out["expected"]["realized_deltaS"], 65)

    def test_latency_cause_is_public_system_agg_only(self):
        l01c = load_fixture()["vectors"]["latency"][3]
        self.assertEqual(l01c["cause_key"], "SYSTEM:AGG.metrics.legislative_capacity.internals.leg.coalition_strength")
        self.assertTrue(l01c["public"])
        self.assertNotIn("REG_TO_INT", l01c["cause_key"])

    def test_ordering_vectors_are_complete(self):
        canonical_pairs = [[r, CANONICAL_REGION_VALUES[r]] for r in CANONICAL_REGION_ORDER]
        alphabetical_pairs = [[r, CANONICAL_REGION_VALUES[r]] for r in ALPHABETICAL_REGION_ORDER]
        ordv = load_fixture()["vectors"]["ordering"]
        self.assertEqual(len(ordv), 5)
        o01 = ordv[0]
        self.assertEqual(o01["id"], "O-01")
        self.assertEqual(list(o01.keys()), EXPECTED_ORDERING_KEYS_BY_ID["O-01"])
        self.assertEqual(o01["canonical_region_order"], CANONICAL_REGION_ORDER)
        self.assertEqual(o01["stored_alphabetical_order"], ALPHABETICAL_REGION_ORDER)
        self.assertEqual(o01["values_by_region"], canonical_pairs)
        self.assertEqual(o01["stored_alphabetical_pairs"], alphabetical_pairs)
        self.assertEqual(o01["expected_order"], CANONICAL_REGION_ORDER)
        self.assertEqual(o01["expected_ordered_pairs"], canonical_pairs)
        self.assertIs(o01["expected_result_independent_of_stored_order"], True)
        o02 = ordv[1]
        self.assertEqual(o02["id"], "O-02")
        self.assertEqual(list(o02.keys()), EXPECTED_ORDERING_KEYS_BY_ID["O-02"])
        self.assertEqual(o02["canonical_region_order"], CANONICAL_REGION_ORDER)
        self.assertEqual(o02["insertion_order_a"], canonical_pairs)
        self.assertEqual(o02["insertion_order_b"], list(reversed(canonical_pairs)))
        self.assertEqual(o02["expected_ordered_pairs"], canonical_pairs)
        self.assertEqual(dict(o02["insertion_order_a"]), dict(o02["insertion_order_b"]), "O-02 same mapping")
        self.assertNotEqual(o02["insertion_order_a"], o02["insertion_order_b"], "O-02 orders differ")
        self.assertIs(o02["expected_canonical_bytes_equal"], True)
        o03 = ordv[2]
        self.assertEqual(o03["id"], "O-03")
        self.assertEqual(list(o03.keys()), EXPECTED_ORDERING_KEYS_BY_ID["O-03"])
        self.assertEqual(o03["binding_order"], EXPECTED_BINDING_ORDER)
        o04 = ordv[3]
        self.assertEqual(o04["id"], "O-04")
        self.assertEqual(list(o04.keys()), EXPECTED_ORDERING_KEYS_BY_ID["O-04"])
        self.assertEqual(o04["expected_output_count"], 2)
        self.assertFalse(o04["collapsed"])
        o05 = ordv[4]
        self.assertEqual(o05["id"], "O-05")
        self.assertEqual(list(o05.keys()), EXPECTED_ORDERING_KEYS_BY_ID["O-05"])
        self.assertTrue(o05["expected_bytes_equal"])

    def test_fixture_contains_no_implicit_expected_keys(self):
        data = load_fixture()
        keys = set()
        strings = set()
        collect_keys_and_strings(data, keys, strings)
        forbidden_lower = {k.lower() for k in FORBIDDEN_IMPLICIT_KEYS}
        for key in keys:
            self.assertNotIn(key.lower(), forbidden_lower, f"Forbidden key: {key}")

    def test_active_reform_scan_is_case_insensitive(self):
        cases = [
            ({"REG_REFORM_BIAS": True}, "key exact upper"),
            ({"key": "pr_19_4"}, "string literal lower"),
            ({"nested": [{"Support_Bias": 1}]}, "nested key mixed case"),
            ({"ACTIVE_REFORM_BIAS": 0}, "key upper exact"),
            ({"x": "reg_reform_bias"}, "string lower exact"),
            ({"x": "PR_19_4"}, "string upper exact"),
            ({"x": "effective_clout"}, "string lower exact"),
            ({"x": "support_bias"}, "string lower exact"),
            ({"x": "tension_bias"}, "string lower exact"),
        ]
        for obj, label in cases:
            with self.subTest(case=label):
                matches = find_forbidden_terms(obj, FORBIDDEN_ACTIVE_REFORM_TERMS)
                self.assertGreater(len(matches), 0, f"No match for {label}: {obj}")

    def test_fixture_contains_no_active_reform_bias(self):
        self.assertEqual(
            find_forbidden_terms(load_fixture(), FORBIDDEN_ACTIVE_REFORM_TERMS),
            [],
        )

    def test_fixture_contains_no_null_p06_or_paths(self):
        data = load_fixture()
        for grp in EXPECTED_VECTOR_GROUP_KEYS:
            for v in data["vectors"][grp]:
                self._check_no_null(v, f"{grp}.{v.get('id', '?')}")
        for pv in data["vectors"]["pull"]:
            self.assertNotEqual(pv["id"], "P-06")
        self.assertNotIn("P-06", data["vector_registry"]["pull"])
        keys = set()
        strings = set()
        collect_keys_and_strings(data, keys, strings)
        for s in strings:
            if isinstance(s, str):
                self.assertFalse("Assets/" in s or s.startswith("/"), f"Path: {s}")

    def _check_no_null(self, obj, path):
        if isinstance(obj, dict):
            for k, v in obj.items():
                self._check_no_null(v, f"{path}.{k}")
        elif isinstance(obj, list):
            for i, v in enumerate(obj):
                self._check_no_null(v, f"{path}[{i}]")
        elif obj is None:
            raise AssertionError(f"Null at {path}")


    # ══════════════════════════════════════════════════════════════════════════
    # Oracle tests (15.1-K) – independent recalculation from inputs
    # ══════════════════════════════════════════════════════════════════════════

    def test_oracle_rounding_vectors_match_fixture(self):
        data = load_fixture()
        for rv in data["vectors"]["rounding"]:
            with self.subTest(vector=rv["id"]):
                actual = oracle_round_divide_half_away_from_zero(rv["numerator"], rv["denominator"])
                self.assertEqual(actual, rv["expected_result"])
        # Boundary cases
        for num, den, exp in [
            (500000, 1000000, 1),
            (-500000, 1000000, -1),
            (499999, 1000000, 0),
            (-499999, 1000000, 0),
        ]:
            with self.subTest(boundary=f"{num}/{den}"):
                self.assertEqual(oracle_round_divide_half_away_from_zero(num, den), exp)
        # Reject non-positive denominator
        with self.subTest(edge="denominator_zero"):
            with self.assertRaises(ValueError):
                oracle_round_divide_half_away_from_zero(1, 0)
        with self.subTest(edge="denominator_negative"):
            with self.assertRaises(ValueError):
                oracle_round_divide_half_away_from_zero(1, -1)
        # int64 boundaries
        with self.subTest(edge="i64_min_ok"):
            oracle_round_divide_half_away_from_zero(ORACLE_I64_MIN, 1)
        with self.subTest(edge="i64_max_ok"):
            oracle_round_divide_half_away_from_zero(ORACLE_I64_MAX, 1)
        with self.subTest(edge="i64_min_minus_one"):
            with self.assertRaises(OverflowError):
                oracle_checked_i64(ORACLE_I64_MIN - 1, "test")
        with self.subTest(edge="i64_max_plus_one"):
            with self.assertRaises(OverflowError):
                oracle_checked_i64(ORACLE_I64_MAX + 1, "test")
        with self.subTest(edge="i64_max_add_overflow"):
            with self.assertRaises(OverflowError):
                oracle_checked_add_i64(ORACLE_I64_MAX, 1, "test")
        with self.subTest(edge="i64_max_mul_overflow"):
            with self.assertRaises(OverflowError):
                oracle_checked_mul_i64(ORACLE_I64_MAX, 2, "test")
        with self.subTest(edge="i64_min_neg_overflow"):
            with self.assertRaises(OverflowError):
                oracle_checked_mul_i64(ORACLE_I64_MIN, -1, "test")
        # long.MinValue division boundaries
        with self.subTest(edge="i64_min_div_1"):
            self.assertEqual(oracle_round_divide_half_away_from_zero(ORACLE_I64_MIN, 1), ORACLE_I64_MIN)
        with self.subTest(edge="i64_max_div_1"):
            self.assertEqual(oracle_round_divide_half_away_from_zero(ORACLE_I64_MAX, 1), ORACLE_I64_MAX)
        with self.subTest(edge="i64_min_div_3_rounded"):
            self.assertEqual(oracle_round_divide_half_away_from_zero(ORACLE_I64_MIN, 3), -3074457345618258603)
        with self.subTest(edge="i64_max_div_3_rounded"):
            self.assertEqual(oracle_round_divide_half_away_from_zero(ORACLE_I64_MAX, 3), 3074457345618258602)
        with self.subTest(edge="i64_min_div_3_divmod"):
            qt, rm = oracle_truncating_divmod_positive_denominator(ORACLE_I64_MIN, 3)
            self.assertEqual(qt, -3074457345618258602)
            self.assertEqual(rm, -2)
            reconstructed = oracle_checked_add_i64(
                oracle_checked_mul_i64(qt, 3, "i64_min_div3_recon_mul"),
                rm,
                "i64_min_div3_recon_add",
            )
            self.assertEqual(reconstructed, ORACLE_I64_MIN)
        # Source-inspect rounding helper for forbidden patterns
        rnd_src = inspect.getsource(oracle_round_divide_half_away_from_zero)
        self.assertNotIn("abs(", rnd_src, "rounding uses abs(")
        self.assertNotIn("abs_num", rnd_src, "rounding uses abs_num")
        self.assertNotIn("-numerator", rnd_src, "rounding uses -numerator")

    def test_oracle_all_valid_drift_vectors_match_every_expected_field(self):
        data = load_fixture()
        recalculated_output_count = 0
        for dv in data["vectors"]["drift"]:
            if not dv["valid"]:
                continue
            for out in dv["outputs"]:
                inp = out["input"]
                with self.subTest(vector=dv["id"], region=out["region_id"], metric=out["metric"]):
                    o_result = oracle_drift_output(out["metric"], inp["currentS"], inp["metrics"], inp["region_snapshot"], out["target_path"], out["cause_key"])
                    self.assertEqual(o_result, out["expected"])
                    self.assertEqual(o_result["finalS"], out["expected"]["finalS"])
                    recalculated_output_count += 1
        self.assertEqual(recalculated_output_count, 75)

    def test_oracle_d08_wrong_recomputes_and_rejects_counterexample(self):
        data = load_fixture()
        d08 = [d for d in data["vectors"]["drift"] if d["id"] == "D-08"][0]
        wrong = [d for d in data["vectors"]["drift"] if d["id"] == "D-08-WRONG"][0]
        rival_out = d08["outputs"][1]
        rival_input = rival_out["input"]
        valid_o = oracle_drift_output("rival_presence", rival_input["currentS"], rival_input["metrics"], rival_input["region_snapshot"], rival_out["target_path"], rival_out["cause_key"])
        self.assertEqual(valid_o, rival_out["expected"])
        self.assertEqual(valid_o["finalS"], rival_out["expected"]["finalS"])
        wrong_metrics = dict(rival_input["metrics"])
        wrong_snapshot = dict(rival_input["region_snapshot"])
        wrong_snapshot["supportS"] = 4200
        ce_o = oracle_drift_output("rival_presence", 5000, wrong_metrics, wrong_snapshot)
        skip_keys = {"expected_contributions", "currentS"}
        for key in wrong["counterexample"]:
            if key in skip_keys:
                continue
            self.assertEqual(ce_o[key], wrong["counterexample"][key], f"counterexample field {key}")
        self.assertEqual(ce_o["finalS"], wrong["counterexample"]["finalS"])
        self.assertNotEqual(ce_o["finalS"], valid_o["finalS"])
        self.assertEqual(wrong["rejection_reason"], "uses_post_drift_support_for_rival_presence")
        self.assertEqual(wrong["must_differ_from"]["vector_id"], "D-08")
        self.assertEqual(wrong["must_differ_from"]["field"], "rival_presence.finalS")
        self.assertEqual(wrong["must_differ_from"]["valid_value"], valid_o["finalS"])
        self.assertEqual(wrong["must_differ_from"]["counterexample_value"], ce_o["finalS"])

    def test_oracle_all_pull_vectors_match_every_expected_field(self):
        data = load_fixture()
        for pv in data["vectors"]["pull"]:
            with self.subTest(vector=pv["id"]):
                o_result = oracle_pull(pv["ordered_region_values"], pv["ordered_weights"], pv["input_currentS"])
                self.assertEqual(o_result, pv["expected"])
                self.assertEqual(o_result["public_contributions"], [])

    def test_oracle_latency_tick_t_matches_drift_and_pull(self):
        data = load_fixture()
        l01t = data["vectors"]["latency"][0]
        self.assertEqual(l01t["id"], "L-01-T")
        di = l01t["drift_inputs"]
        metrics = {k: di[k] for k in ORACLE_DRIFT_INPUT_KEYS}
        region_snapshot = {"supportS": 5000, "tensionS": 5000, "organizationS": 5000, "rival_presenceS": 5000}
        drift_final_values = []
        for i, expected_out in enumerate(l01t["drift_outputs"]):
            with self.subTest(drift_output=i, region=expected_out["region_id"]):
                o_result = oracle_drift_output("support", 5000, metrics, region_snapshot, expected_out["target_path"], expected_out["cause_key"])
                self.assertEqual(o_result, expected_out["expected"])
                drift_final_values.append(o_result["finalS"])
        weights = [62500] * 16
        o_pull = oracle_pull(drift_final_values, weights, l01t["pull_input_currentS"])
        self.assertEqual(o_pull, l01t["pull_expected"])
        self.assertEqual(o_pull["finalS"], l01t["pull_expected"]["finalS"])

    def test_oracle_latency_t1_reversion_matches_every_intermediate(self):
        data = load_fixture()
        r = data["vectors"]["latency"][1]
        self.assertEqual(r["id"], "L-01-T1-R")
        self.assertEqual(r["alpha_ppm"], ORACLE_REVERSION_LEG_ALPHA_PPM)
        o_result = oracle_reversion(r["currentS"], r["midS"], ORACLE_REVERSION_LEG_ALPHA_PPM)
        T1R_PROJ_KEYS = ["distanceS", "elastic_numerator", "rounded_deltaS", "finalS"]
        fixture_proj = {k: r[k] for k in T1R_PROJ_KEYS}
        result_proj = {k: o_result[k] for k in T1R_PROJ_KEYS}
        self.assertEqual(result_proj, fixture_proj)

    def test_oracle_latency_t1_aggregation_matches_every_intermediate(self):
        data = load_fixture()
        a = data["vectors"]["latency"][2]
        self.assertEqual(a["id"], "L-01-T1-A")
        self.assertEqual(a["alpha_ppm"], ORACLE_LEGISLATIVE_ALPHA_PPM)
        self.assertEqual(a["coalition_weight_ppm"], ORACLE_LEGISLATIVE_WEIGHTS["coalition_strengthS"])
        T1A_PROJ_KEYS = [
            "coalition_weight_ppm", "weighted_offset_numerator", "weighted_offsetS",
            "targetS", "distanceS", "elastic_numerator", "elastic_deltaS",
            "capped_deltaS", "finalS", "delta_totalS",
        ]
        fixture_proj = {k: a[k] for k in T1A_PROJ_KEYS}
        o_result = oracle_legislative_capacity_t1(a["coalition_strengthS"], a["party_disciplineS"], a["opposition_obstructionS"], a["senate_inertiaS"], a["current_metricS"])
        result_proj = {k: o_result[k] for k in T1A_PROJ_KEYS}
        self.assertEqual(result_proj, fixture_proj)

    def test_oracle_latency_public_cause_matches_recalculated_delta(self):
        data = load_fixture()
        c = data["vectors"]["latency"][3]
        a = data["vectors"]["latency"][2]
        o_t1a = oracle_legislative_capacity_t1(a["coalition_strengthS"], a["party_disciplineS"], a["opposition_obstructionS"], a["senate_inertiaS"], a["current_metricS"])
        oracle_cause = {
            "target": "metrics.legislative_capacity",
            "internal_target": "internals.leg.coalition_strength",
            "cause_key": "SYSTEM:AGG.metrics.legislative_capacity.internals.leg.coalition_strength",
            "deltaS": o_t1a["delta_totalS"],
            "public": True,
        }
        CAUSE_KEYS = ["target", "internal_target", "cause_key", "deltaS", "public"]
        fixture_proj = {k: c[k] for k in CAUSE_KEYS}
        self.assertEqual(oracle_cause, fixture_proj)
        self.assertNotIn("REG_TO_INT", oracle_cause["cause_key"])

    def test_oracle_ordering_vectors_match_independent_calculation(self):
        data = load_fixture()
        ordv = data["vectors"]["ordering"]
        canonical_pairs = [[r, CANONICAL_REGION_VALUES[r]] for r in CANONICAL_REGION_ORDER]

        # O-01: reconstruct from stored alphabetical pairs
        o01 = ordv[0]
        stored_pairs = o01["stored_alphabetical_pairs"]
        pair_map = {p[0]: p[1] for p in stored_pairs}
        reconstructed_pairs = [[rid, pair_map[rid]] for rid in CANONICAL_REGION_ORDER]
        reconstructed_ids = [rid for rid, _ in reconstructed_pairs]
        self.assertEqual(reconstructed_pairs, o01["expected_ordered_pairs"])
        self.assertEqual(reconstructed_ids, o01["expected_order"])
        self.assertEqual(pair_map, dict(o01["values_by_region"]))
        self.assertNotEqual([p[0] for p in stored_pairs], CANONICAL_REGION_ORDER)
        self.assertIs(o01["expected_result_independent_of_stored_order"], True)

        # O-02: canonical serialization
        o02 = ordv[1]
        mapping_a = dict(o02["insertion_order_a"])
        mapping_b = dict(o02["insertion_order_b"])
        self.assertEqual(mapping_a, mapping_b)
        self.assertNotEqual(o02["insertion_order_a"], o02["insertion_order_b"])
        serialized_a = oracle_canonical_json(mapping_a)
        serialized_b = oracle_canonical_json(mapping_b)
        self.assertEqual(serialized_a, serialized_b)
        reconstructed_pairs_o2 = [[rid, mapping_a[rid]] for rid in CANONICAL_REGION_ORDER]
        self.assertEqual(reconstructed_pairs_o2, o02["expected_ordered_pairs"])
        is_bytes_equal = (serialized_a == serialized_b)
        self.assertEqual(is_bytes_equal, o02["expected_canonical_bytes_equal"])

        # O-03: binding order from independent bindings
        o03 = ordv[2]
        oracle_binding_ids = [b["id"] for b in ORACLE_PULL_BINDINGS]
        self.assertEqual(oracle_binding_ids, o03["binding_order"])

        # O-04: derived from tension bindings
        o04 = ordv[3]
        derived_outputs = [
            {"binding": b["id"], "destination": b["destination"]}
            for b in ORACLE_PULL_BINDINGS
            if b["regional_source"] == "tension"
        ]
        self.assertEqual(derived_outputs, o04["outputs"])
        self.assertEqual(len(derived_outputs), o04["expected_output_count"])
        self.assertFalse(o04["collapsed"])

        # O-05: canonical JSON config
        o05 = ordv[4]
        self.assertEqual(o05["canonical_serialization"], ORACLE_CANONICAL_JSON_CONFIG)
        serialized_a = oracle_canonical_json(o05["input_a"])
        serialized_b = oracle_canonical_json(o05["input_b"])
        self.assertEqual(serialized_a == serialized_b, o05["expected_bytes_equal"])

    def test_oracle_is_deterministic(self):
        data = load_fixture()
        metrics = {"legitimacyS": 6000, "economyS": 5000, "securityS": 5000, "party_organizationS": 5000, "social_tensionS": 5000, "public_agendaS": 5000, "internal_cohesionS": 5000}
        snap = {"supportS": 5000, "tensionS": 5000, "organizationS": 5000, "rival_presenceS": 5000}
        values = [5000] * 16
        weights = [62500] * 16
        for _ in range(5):
            d1 = oracle_drift_output("support", 5000, metrics, snap)
            d2 = oracle_drift_output("support", 5000, metrics, snap)
            self.assertEqual(d1, d2)
            p1 = oracle_pull(values, weights, 5000)
            p2 = oracle_pull(values, weights, 5000)
            self.assertEqual(p1, p2)
            r1 = oracle_reversion(5013, 5000, ORACLE_REVERSION_LEG_ALPHA_PPM)
            r2 = oracle_reversion(5013, 5000, ORACLE_REVERSION_LEG_ALPHA_PPM)
            self.assertEqual(r1, r2)
            la1 = oracle_legislative_capacity_t1(5013, 5000, 5000, 5000, 5000)
            la2 = oracle_legislative_capacity_t1(5013, 5000, 5000, 5000, 5000)
            self.assertEqual(la1, la2)

    def test_oracle_rejects_mutated_expected_values(self):
        data = load_fixture()
        metrics = {"legitimacyS": 6000, "economyS": 5000, "securityS": 5000, "party_organizationS": 5000, "social_tensionS": 5000, "public_agendaS": 5000, "internal_cohesionS": 5000}
        snap = {"supportS": 5000, "tensionS": 5000, "organizationS": 5000, "rival_presenceS": 5000}
        # Anti-tautology: source inspect helpers
        oracle_funcs = [
            oracle_drift_output, oracle_pull, oracle_reversion,
            oracle_legislative_capacity_t1, oracle_drift_target_numerator,
            oracle_round_divide_half_away_from_zero, oracle_clamp_int,
            oracle_checked_i64, oracle_checked_add_i64, oracle_checked_sub_i64,
            oracle_checked_mul_i64, oracle_truncating_divmod_positive_denominator,
            oracle_canonical_json,
        ]
        for func in oracle_funcs:
            for pname in inspect.signature(func).parameters:
                self.assertNotEqual(pname, "expected", f"{func.__name__} has 'expected' parameter")
            src = inspect.getsource(func)
            self.assertNotIn("load_fixture", src, f"{func.__name__} calls load_fixture")
            self.assertNotIn("copy.deepcopy", src, f"{func.__name__} uses copy.deepcopy")
            self.assertNotIn('"expected"', src, f'{func.__name__} reads literal "expected"')
            self.assertNotIn("'expected'", src, f"{func.__name__} reads literal 'expected'")
        # 13 mutations: mutate fixture copy, recalculate from inputs, assert inequality
        with self.subTest(mutation="R-01_expected_result"):
            mutated = copy.deepcopy(data["vectors"]["rounding"][0])
            mutated["expected_result"] = 999
            actual = oracle_round_divide_half_away_from_zero(data["vectors"]["rounding"][0]["numerator"], data["vectors"]["rounding"][0]["denominator"])
            self.assertNotEqual(actual, mutated["expected_result"])
        with self.subTest(mutation="D-01_numerator"):
            d01_out = data["vectors"]["drift"][1]["outputs"][0]
            mutated = copy.deepcopy(d01_out["expected"])
            mutated["numerator"] = 0
            o = oracle_drift_output(d01_out["metric"], d01_out["input"]["currentS"], d01_out["input"]["metrics"], d01_out["input"]["region_snapshot"], d01_out["target_path"], d01_out["cause_key"])
            self.assertNotEqual(o, mutated)
        with self.subTest(mutation="D-01_finalS"):
            d01_out = data["vectors"]["drift"][1]["outputs"][0]
            mutated = copy.deepcopy(d01_out["expected"])
            mutated["finalS"] = 9999
            o = oracle_drift_output(d01_out["metric"], d01_out["input"]["currentS"], d01_out["input"]["metrics"], d01_out["input"]["region_snapshot"], d01_out["target_path"], d01_out["cause_key"])
            self.assertNotEqual(o, mutated)
        with self.subTest(mutation="D-01_contribution_deltaS"):
            d01_out = data["vectors"]["drift"][1]["outputs"][0]
            mutated = copy.deepcopy(d01_out["expected"])
            mutated["expected_contributions"][0]["deltaS"] = 999
            o = oracle_drift_output(d01_out["metric"], d01_out["input"]["currentS"], d01_out["input"]["metrics"], d01_out["input"]["region_snapshot"], d01_out["target_path"], d01_out["cause_key"])
            self.assertNotEqual(o, mutated)
        with self.subTest(mutation="D-08_rival_finalS"):
            d08 = [d for d in data["vectors"]["drift"] if d["id"] == "D-08"][0]
            rival = d08["outputs"][1]
            mutated = copy.deepcopy(rival["expected"])
            mutated["finalS"] = 9999
            o = oracle_drift_output("rival_presence", rival["input"]["currentS"], rival["input"]["metrics"], rival["input"]["region_snapshot"], rival["target_path"], rival["cause_key"])
            self.assertNotEqual(o, mutated)
        with self.subTest(mutation="P-01_weighted_numerator"):
            p01 = data["vectors"]["pull"][1]
            mutated = copy.deepcopy(p01["expected"])
            mutated["weighted_numerator"] = 0
            o = oracle_pull(p01["ordered_region_values"], p01["ordered_weights"], p01["input_currentS"])
            self.assertNotEqual(o, mutated)
        with self.subTest(mutation="P-05_weighted_averageS"):
            p05 = data["vectors"]["pull"][5]
            mutated = copy.deepcopy(p05["expected"])
            mutated["weighted_averageS"] = 9999
            o = oracle_pull(p05["ordered_region_values"], p05["ordered_weights"], p05["input_currentS"])
            self.assertNotEqual(o, mutated)
        with self.subTest(mutation="L-01-T_pull_finalS"):
            l01t = data["vectors"]["latency"][0]
            mutated = copy.deepcopy(l01t["pull_expected"])
            mutated["finalS"] = 9999
            metrics_lt = {k: l01t["drift_inputs"][k] for k in ORACLE_DRIFT_INPUT_KEYS}
            snap_lt = {"supportS": 5000, "tensionS": 5000, "organizationS": 5000, "rival_presenceS": 5000}
            drift_finals = []
            for _ in range(16):
                o = oracle_drift_output("support", 5000, metrics_lt, snap_lt)
                drift_finals.append(o["finalS"])
            o_pull = oracle_pull(drift_finals, [62500] * 16, l01t["pull_input_currentS"])
            self.assertNotEqual(o_pull, mutated)
        with self.subTest(mutation="L-01-T1-R_rounded_deltaS"):
            r = data["vectors"]["latency"][1]
            T1R_KEYS = ["distanceS", "elastic_numerator", "rounded_deltaS", "finalS"]
            fixture_proj = {k: r[k] for k in T1R_KEYS}
            mutated = copy.deepcopy(fixture_proj)
            mutated["rounded_deltaS"] = 999
            o = oracle_reversion(r["currentS"], r["midS"], ORACLE_REVERSION_LEG_ALPHA_PPM)
            o_proj = {k: o[k] for k in T1R_KEYS}
            self.assertNotEqual(o_proj, mutated)
        with self.subTest(mutation="L-01-T1-A_finalS"):
            a = data["vectors"]["latency"][2]
            T1A_KEYS = [
                "coalition_weight_ppm", "weighted_offset_numerator",
                "weighted_offsetS", "targetS", "distanceS",
                "elastic_numerator", "elastic_deltaS", "capped_deltaS",
                "finalS", "delta_totalS",
            ]
            fixture_proj = {k: a[k] for k in T1A_KEYS}
            mutated = copy.deepcopy(fixture_proj)
            mutated["finalS"] = 9999
            o = oracle_legislative_capacity_t1(a["coalition_strengthS"], a["party_disciplineS"], a["opposition_obstructionS"], a["senate_inertiaS"], a["current_metricS"])
            o_proj = {k: o[k] for k in T1A_KEYS}
            self.assertNotEqual(o_proj, mutated)
        with self.subTest(mutation="L-01-CAUSE_deltaS"):
            c = data["vectors"]["latency"][3]
            a = data["vectors"]["latency"][2]
            CAUSE_KEYS = ["target", "internal_target", "cause_key", "deltaS", "public"]
            fixture_cause_projection = {k: c[k] for k in CAUSE_KEYS}
            o_t1a = oracle_legislative_capacity_t1(a["coalition_strengthS"], a["party_disciplineS"], a["opposition_obstructionS"], a["senate_inertiaS"], a["current_metricS"])
            oracle_cause = {
                "target": "metrics.legislative_capacity",
                "internal_target": "internals.leg.coalition_strength",
                "cause_key": "SYSTEM:AGG.metrics.legislative_capacity.internals.leg.coalition_strength",
                "deltaS": o_t1a["delta_totalS"],
                "public": True,
            }
            self.assertEqual(oracle_cause, fixture_cause_projection)
            mutated_fixture_cause = copy.deepcopy(fixture_cause_projection)
            mutated_fixture_cause["deltaS"] = 999
            self.assertNotEqual(oracle_cause, mutated_fixture_cause)
        with self.subTest(mutation="O-01_expected_ordered_pairs"):
            o01 = data["vectors"]["ordering"][0]
            stored_pairs = o01["stored_alphabetical_pairs"]
            pair_map = {p[0]: p[1] for p in stored_pairs}
            reconstructed = [[rid, pair_map[rid]] for rid in CANONICAL_REGION_ORDER]
            mutated = copy.deepcopy(o01["expected_ordered_pairs"])
            mutated[0][1] = 9999
            self.assertNotEqual(reconstructed, mutated)
        with self.subTest(mutation="O-02_expected_canonical_bytes_equal"):
            o02 = data["vectors"]["ordering"][1]
            mapping_a = dict(o02["insertion_order_a"])
            mapping_b = dict(o02["insertion_order_b"])
            is_bytes_equal = oracle_canonical_json(mapping_a) == oracle_canonical_json(mapping_b)
            self.assertEqual(is_bytes_equal, o02["expected_canonical_bytes_equal"])
            mutated_o02 = copy.deepcopy(o02)
            mutated_o02["expected_canonical_bytes_equal"] = not mutated_o02["expected_canonical_bytes_equal"]
            self.assertNotEqual(is_bytes_equal, mutated_o02["expected_canonical_bytes_equal"])

    # ══════════════════════════════════════════════════════════════════════════
    # Territory parity and negative tests (15.1-L)
    # ══════════════════════════════════════════════════════════════════════════

    def test_l_parity_baseline(self):
        fixture = load_fixture()
        errors = collect_execution_fixture_errors(fixture)
        self.assertEqual(errors, [])

    def test_l_mutation_matrix(self):
        fixture = load_fixture()
        execution_killed = []

        with self.subTest(mutation="L-M01-DRIFT-ALPHA"):
            mutated = copy.deepcopy(fixture)
            mutated["constants"]["drift"]["alpha_ppm"] = 109100
            errors = collect_execution_fixture_errors(mutated)
            self.assertIn("EXEC_DRIFT_ALPHA", errors)
            execution_killed.append("L-M01-DRIFT-ALPHA")

        with self.subTest(mutation="L-M02-PULL-ALPHA"):
            mutated = copy.deepcopy(fixture)
            mutated["constants"]["pull"]["alpha_ppm"] = 206300
            errors = collect_execution_fixture_errors(mutated)
            self.assertIn("EXEC_PULL_ALPHA", errors)
            execution_killed.append("L-M02-PULL-ALPHA")

        with self.subTest(mutation="L-M03-DRIFT-CAP"):
            mutated = copy.deepcopy(fixture)
            mutated["constants"]["drift"]["cap_per_weekS"] = 201
            errors = collect_execution_fixture_errors(mutated)
            self.assertIn("EXEC_DRIFT_CAP", errors)
            execution_killed.append("L-M03-DRIFT-CAP")

        with self.subTest(mutation="L-M04-PULL-CAP"):
            mutated = copy.deepcopy(fixture)
            mutated["constants"]["pull"]["cap_per_weekS"] = 401
            errors = collect_execution_fixture_errors(mutated)
            self.assertIn("EXEC_PULL_CAP", errors)
            execution_killed.append("L-M04-PULL-CAP")

        with self.subTest(mutation="L-M06-POST-DRIFT-SUPPORT"):
            mutated = copy.deepcopy(fixture)
            for dv in mutated["vectors"]["drift"]:
                if dv["id"] == "D-08":
                    for out in dv["outputs"]:
                        if out["metric"] == "rival_presence":
                            expected = copy.deepcopy(out["expected"])
                            out["input"]["region_snapshot"]["supportS"] = 4200
                            self.assertEqual(expected["finalS"], 5076)
                            mutant_result = oracle_drift_output(
                                out["metric"],
                                out["input"]["currentS"],
                                out["input"]["metrics"],
                                out["input"]["region_snapshot"],
                                out["target_path"],
                                out["cause_key"],
                            )
                            self.assertEqual(mutant_result["finalS"], 5061)
                            self.assertNotEqual(mutant_result, expected)
            errors = collect_execution_fixture_errors(mutated)
            self.assertIn("EXEC_PRE_DRIFT_SNAPSHOT", errors)
            execution_killed.append("L-M06-POST-DRIFT-SUPPORT")

        with self.subTest(mutation="L-M07A-ALPHABETICAL-CANONICAL-ONLY"):
            mutated = copy.deepcopy(fixture)
            mutated["canonical_region_order"] = ALPHABETICAL_REGION_ORDER
            errors = collect_execution_fixture_errors(mutated)
            self.assertIn("EXEC_REGION_ORDER", errors)

        with self.subTest(mutation="L-M07B-ALPHABETICAL-REGION-ORDER"):
            mutated = copy.deepcopy(fixture)
            mutated["canonical_region_order"] = ALPHABETICAL_REGION_ORDER
            mutated["regions"] = sorted(mutated["regions"], key=lambda region: region["region_id"])
            errors = collect_execution_fixture_errors(mutated)
            self.assertIn("EXEC_REGION_ORDER", errors)
            execution_killed.append("L-M07-ALPHABETICAL-REGION-ORDER")

        with self.subTest(mutation="L-M08-TRUNCATING-ROUNDING"):
            mutated = copy.deepcopy(fixture)
            r01 = fixture["vectors"]["rounding"][0]
            r02 = fixture["vectors"]["rounding"][1]
            contractual_outputs = [
                oracle_round_divide_half_away_from_zero(r01["numerator"], r01["denominator"]),
                oracle_round_divide_half_away_from_zero(r02["numerator"], r02["denominator"]),
            ]
            mutant_outputs = [
                mutant_truncating_divide(r01["numerator"], r01["denominator"]),
                mutant_truncating_divide(r02["numerator"], r02["denominator"]),
            ]
            errors = []
            if mutant_outputs != contractual_outputs:
                errors.append("EXEC_ROUNDING_MODE")
            self.assertEqual(mutant_outputs, [0, 0])
            self.assertEqual(contractual_outputs, [1, -1])
            self.assertIn("EXEC_ROUNDING_MODE", errors)
            execution_killed.append("L-M08-TRUNCATING-ROUNDING")

        with self.subTest(mutation="L-M09-PER-REGION-WEIGHTED-ROUNDING"):
            mutated = copy.deepcopy(fixture)
            p05 = mutated["vectors"]["pull"][5]
            contractual_outputs = [oracle_pull(p05["ordered_region_values"], p05["ordered_weights"], p05["input_currentS"])["weighted_averageS"]]
            mutant_outputs = [mutant_per_region_weighted_rounding(p05["ordered_region_values"], p05["ordered_weights"])]
            errors = []
            if mutant_outputs != contractual_outputs:
                errors.append("EXEC_WEIGHTED_ROUNDING_STAGE")
            self.assertEqual(mutant_outputs, [5008])
            self.assertEqual(contractual_outputs, [5001])
            self.assertIn("EXEC_WEIGHTED_ROUNDING_STAGE", errors)
            execution_killed.append("L-M09-PER-REGION-WEIGHTED-ROUNDING")

        with self.subTest(mutation="L-M10-COLON-DRIFT-CAUSE"):
            mutated = copy.deepcopy(fixture)
            for dv in mutated["vectors"]["drift"]:
                if dv["id"] == "D-00":
                    for out in dv["outputs"]:
                        if out["metric"] == "support":
                            out["cause_key"] = "SYSTEM:REG_DRIFT:arica_parinacota:support"
                            break
                    break
            errors = collect_execution_fixture_errors(mutated)
            self.assertIn("EXEC_DRIFT_CAUSE_GRAMMAR", errors)
            execution_killed.append("L-M10-COLON-DRIFT-CAUSE")

        with self.subTest(mutation="L-M11-PUBLIC-PULL-PROVENANCE"):
            mutated = copy.deepcopy(fixture)
            mutated["cause_contract"]["hidden_pull_provenance"]["pull_provenance"]["public_ledger"] = True
            errors = collect_execution_fixture_errors(mutated)
            self.assertIn("EXEC_PULL_PROVENANCE_PUBLIC", errors)
            execution_killed.append("L-M11-PUBLIC-PULL-PROVENANCE")

        with self.subTest(mutation="L-M11B-PULL-VECTOR-NONEMPTY"):
            mutated = copy.deepcopy(fixture)
            mutated["vectors"]["pull"][1]["expected"]["public_contributions"] = [{"deltaS": 1}]
            errors = collect_execution_fixture_errors(mutated)
            self.assertIn("EXEC_PULL_PROVENANCE_PUBLIC", errors)

        with self.subTest(mutation="L-M11C-PROVENANCE-TICK-BUFFER"):
            mutated = copy.deepcopy(fixture)
            mutated["cause_contract"]["hidden_pull_provenance"]["pull_provenance"]["tick_causal_buffer"] = True
            errors = collect_execution_fixture_errors(mutated)
            self.assertIn("EXEC_PULL_PROVENANCE_PUBLIC", errors)

        with self.subTest(mutation="L-M12-ACTIVE-REFORM-BIAS"):
            mutated = copy.deepcopy(fixture)
            mutated["regions"][0]["active_reform_bias"] = {"enabled": True}
            errors = collect_execution_fixture_errors(mutated)
            self.assertIn("EXEC_ACTIVE_REFORM_PRESENT", errors)

        self.assertEqual(execution_killed, [
            "L-M01-DRIFT-ALPHA",
            "L-M02-PULL-ALPHA",
            "L-M03-DRIFT-CAP",
            "L-M04-PULL-CAP",
            "L-M06-POST-DRIFT-SUPPORT",
            "L-M07-ALPHABETICAL-REGION-ORDER",
            "L-M08-TRUNCATING-ROUNDING",
            "L-M09-PER-REGION-WEIGHTED-ROUNDING",
            "L-M10-COLON-DRIFT-CAUSE",
            "L-M11-PUBLIC-PULL-PROVENANCE",
        ])

    def test_l_validator_anti_tautology(self):
        src = inspect.getsource(collect_execution_fixture_errors)
        self.assertNotIn("read_json_document", src, "validator calls read_json_document")
        self.assertNotIn("read_markdown_text", src, "validator calls read_markdown_text")
        self.assertNotIn("read_contract_text", src, "validator calls read_contract_text")
        self.assertNotIn("read_mvp_013_resolution", src, "validator calls read_mvp_013_resolution")
        self.assertNotIn("load_fixture", src, "validator calls load_fixture")
        self.assertNotIn("copy.deepcopy", src, "validator uses copy.deepcopy")
        self.assertNotIn("assertNotEqual", src, "validator uses assertNotEqual")
        self.assertNotIn("EXPECTED_DOCUMENT", src, "validator uses EXPECTED_DOCUMENT")
        for pname in inspect.signature(collect_execution_fixture_errors).parameters:
            self.assertNotEqual(pname, "expected", "validator has 'expected' parameter")


if __name__ == "__main__":
    unittest.main()
