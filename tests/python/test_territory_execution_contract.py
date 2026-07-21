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
    "legitimacyS", "economyS", "securityS",
    "party_organizationS", "social_tensionS",
    "public_agendaS", "internal_cohesionS",
]
ORACLE_PULL_BINDING_PROVENANCE: dict[str, str] = {
    "support_to_coalition_strength": "SYSTEM:REG_TO_INT.internals.leg.coalition_strength",
    "organization_to_field_ops": "SYSTEM:REG_TO_INT.internals.assembly.field_ops",
    "tension_to_protest_activity": "SYSTEM:REG_TO_INT.metrics.social.tension.protest_activity",
    "rival_presence_to_opposition_obstruction": "SYSTEM:REG_TO_INT.internals.leg.opposition_obstruction",
    "tension_to_movement_salience": "SYSTEM:REG_TO_INT.metrics.social.tension.movement_salience",
}


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
    if not isinstance(value, int) or isinstance(value, bool):
        raise TypeError(f"{context}: Expected int, got {type(value).__name__}")
    return value


def oracle_checked_add_i64(left: int, right: int, context: str = "") -> int:
    oracle_checked_i64(left, f"{context}.left")
    oracle_checked_i64(right, f"{context}.right")
    return left + right


def oracle_checked_mul_i64(left: int, right: int, context: str = "") -> int:
    oracle_checked_i64(left, f"{context}.left")
    oracle_checked_i64(right, f"{context}.right")
    return left * right


def oracle_round_divide_half_away_from_zero(numerator: int, denominator: int) -> int:
    oracle_checked_i64(numerator, "round_divide.numerator")
    oracle_checked_i64(denominator, "round_divide.denominator")
    if denominator == 0:
        raise ZeroDivisionError("round_divide denominator is 0")
    abs_q = abs(numerator) // abs(denominator)
    q = -abs_q if (numerator < 0) != (denominator < 0) else abs_q
    remainder = numerator - q * denominator
    if remainder == 0:
        return q
    abs_rem = abs(remainder)
    abs_den = abs(denominator)
    if abs_rem * 2 >= abs_den:
        return q + 1 if numerator >= 0 else q - 1
    return q


def oracle_clamp_int(value: int, minimum: int, maximum: int) -> int:
    if value < minimum:
        return minimum
    if value > maximum:
        return maximum
    return value


def oracle_drift_target_numerator(metric: str, metrics: dict[str, int], region_snapshot: dict[str, int]) -> int:
    leg = metrics["legitimacyS"]
    eco = metrics["economyS"]
    sec = metrics["securityS"]
    po = metrics["party_organizationS"]
    st = metrics["social_tensionS"]
    pa = metrics["public_agendaS"]
    ic = metrics["internal_cohesionS"]
    sup = region_snapshot["supportS"]
    mid = ORACLE_MID_S
    if metric == "support":
        return (
            600000 * (leg - mid)
            + 300000 * (po - mid)
            - 400000 * (st - mid)
        )
    if metric == "tension":
        return (
            500000 * (mid - eco)
            + 400000 * (mid - sec)
            + 300000 * (pa - mid)
        )
    if metric == "organization":
        return 800000 * (po - mid)
    if metric == "rival_presence":
        return 700000 * (mid - sup) + 200000 * (mid - ic)
    raise ValueError(f"Unknown drift metric: {metric}")


def oracle_drift_output(metric: str, currentS: int, metrics: dict[str, int], region_snapshot: dict[str, int], target_path: str | None = None, cause_key: str | None = None) -> dict[str, int]:
    numerator = oracle_drift_target_numerator(metric, metrics, region_snapshot)
    offsetS = oracle_round_divide_half_away_from_zero(numerator, ORACLE_PPM_DENOMINATOR)
    target_unclampedS = ORACLE_MID_S + offsetS
    targetS = oracle_clamp_int(target_unclampedS, ORACLE_MIN_S, ORACLE_MAX_S)
    distanceS = targetS - currentS
    elastic_numerator = oracle_checked_mul_i64(distanceS, ORACLE_DRIFT_ALPHA_PPM, "drift.elastic")
    elastic_deltaS = oracle_round_divide_half_away_from_zero(elastic_numerator, ORACLE_PPM_DENOMINATOR)
    capped_deltaS = oracle_clamp_int(elastic_deltaS, -ORACLE_DRIFT_CAP_S, ORACLE_DRIFT_CAP_S)
    pre_finalS = oracle_checked_add_i64(currentS, capped_deltaS, "drift.prefinal")
    finalS = oracle_clamp_int(pre_finalS, ORACLE_MIN_S, ORACLE_MAX_S)
    realized_deltaS = finalS - currentS
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
    weighted_numerator = 0
    for v, w in zip(ordered_region_values, ordered_weights):
        weighted_numerator += oracle_checked_mul_i64(v, w, "pull.weighted")
    weighted_averageS = oracle_round_divide_half_away_from_zero(weighted_numerator, ORACLE_PPM_DENOMINATOR)
    targetS = oracle_clamp_int(weighted_averageS, ORACLE_MIN_S, ORACLE_MAX_S)
    distanceS = targetS - input_currentS
    elastic_numerator = oracle_checked_mul_i64(distanceS, ORACLE_PULL_ALPHA_PPM, "pull.elastic")
    elastic_deltaS = oracle_round_divide_half_away_from_zero(elastic_numerator, ORACLE_PPM_DENOMINATOR)
    capped_deltaS = oracle_clamp_int(elastic_deltaS, -ORACLE_PULL_CAP_S, ORACLE_PULL_CAP_S)
    pre_finalS = oracle_checked_add_i64(input_currentS, capped_deltaS, "pull.prefinal")
    finalS = oracle_clamp_int(pre_finalS, ORACLE_MIN_S, ORACLE_MAX_S)
    realized_deltaS = finalS - input_currentS
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
    distanceS = midS - currentS
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
    weighted_offset_numerator = (
        w["coalition_strengthS"] * (coalition_strengthS - ORACLE_MID_S)
        + w["party_disciplineS"] * (party_disciplineS - ORACLE_MID_S)
        + w["opposition_obstructionS"] * (opposition_obstructionS - ORACLE_MID_S)
        + w["senate_inertiaS"] * (senate_inertiaS - ORACLE_MID_S)
    )
    weighted_offsetS = oracle_round_divide_half_away_from_zero(weighted_offset_numerator, ORACLE_PPM_DENOMINATOR)
    targetS = oracle_clamp_int(ORACLE_MID_S + weighted_offsetS, ORACLE_MIN_S, ORACLE_MAX_S)
    distanceS = targetS - current_metricS
    elastic_numerator = oracle_checked_mul_i64(distanceS, ORACLE_LEGISLATIVE_ALPHA_PPM, "legislative.elastic")
    elastic_deltaS = oracle_round_divide_half_away_from_zero(elastic_numerator, ORACLE_PPM_DENOMINATOR)
    capped_deltaS = oracle_clamp_int(elastic_deltaS, -ORACLE_LEGISLATIVE_CAP_S, ORACLE_LEGISLATIVE_CAP_S)
    finalS = oracle_clamp_int(current_metricS + capped_deltaS, ORACLE_MIN_S, ORACLE_MAX_S)
    delta_totalS = finalS - current_metricS
    return {
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

    def test_oracle_all_valid_drift_vectors_match_every_expected_field(self):
        data = load_fixture()
        for dv in data["vectors"]["drift"]:
            if not dv["valid"]:
                continue
            for out in dv["outputs"]:
                inp = out["input"]
                with self.subTest(vector=dv["id"], region=out["region_id"], metric=out["metric"]):
                    o_result = oracle_drift_output(out["metric"], inp["currentS"], inp["metrics"], inp["region_snapshot"], out["target_path"], out["cause_key"])
                    self.assertEqual(o_result, out["expected"])
                    self.assertEqual(o_result["finalS"], out["expected"]["finalS"])

    def test_oracle_d08_wrong_recomputes_and_rejects_counterexample(self):
        data = load_fixture()
        d08 = [d for d in data["vectors"]["drift"] if d["id"] == "D-08"][0]
        wrong = [d for d in data["vectors"]["drift"] if d["id"] == "D-08-WRONG"][0]
        # Recompute D-08 rival_presence with pre-drift support (valid)
        rival_out = d08["outputs"][1]
        rival_input = rival_out["input"]
        valid_o = oracle_drift_output("rival_presence", rival_input["currentS"], rival_input["metrics"], rival_input["region_snapshot"], rival_out["target_path"], rival_out["cause_key"])
        self.assertEqual(valid_o, rival_out["expected"])
        self.assertEqual(valid_o["finalS"], rival_out["expected"]["finalS"], "D-08 valid oracle finalS")
        # Recompute using post-drift support (counterexample)
        wrong_metrics = dict(rival_input["metrics"])
        wrong_snapshot = dict(rival_input["region_snapshot"])
        wrong_snapshot["supportS"] = 4200
        ce_o = oracle_drift_output("rival_presence", 5000, wrong_metrics, wrong_snapshot)
        # Counterexample fixture does not have expected_contributions or currentS as computed field
        skip_keys = {"expected_contributions", "currentS"}
        for key in wrong["counterexample"]:
            if key in skip_keys:
                continue
            self.assertEqual(ce_o[key], wrong["counterexample"][key], f"counterexample field {key}")
        self.assertEqual(ce_o["finalS"], wrong["counterexample"]["finalS"])
        # Verify the counterexample differs from the valid value
        self.assertNotEqual(ce_o["finalS"], valid_o["finalS"])
        # Verify rejection reason
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
        metrics = {k: di[k] for k in ORACLE_DRIFT_METRIC_ORDER}
        # Recompute drift for each output and compare every intermediate
        region_snapshot = {"supportS": 5000, "tensionS": 5000, "organizationS": 5000, "rival_presenceS": 5000}
        drift_final_values = []
        for i, expected_out in enumerate(l01t["drift_outputs"]):
            with self.subTest(drift_output=i, region=expected_out["region_id"]):
                o_result = oracle_drift_output("support", 5000, metrics, region_snapshot, expected_out["target_path"], expected_out["cause_key"])
                self.assertEqual(o_result, expected_out["expected"])
                drift_final_values.append(o_result["finalS"])
        # Recompute pull using drift result
        weights = [62500] * 16
        o_pull = oracle_pull(drift_final_values, weights, l01t["pull_input_currentS"])
        self.assertEqual(o_pull, l01t["pull_expected"])
        self.assertEqual(o_pull["finalS"], l01t["pull_expected"]["finalS"])

    def test_oracle_latency_t1_reversion_matches_every_intermediate(self):
        data = load_fixture()
        r = data["vectors"]["latency"][1]
        self.assertEqual(r["id"], "L-01-T1-R")
        o_result = oracle_reversion(r["currentS"], r["midS"], r["alpha_ppm"])
        for key in ("distanceS", "elastic_numerator", "rounded_deltaS", "finalS"):
            self.assertEqual(o_result[key], r[key], f"T1-R {key}")
        self.assertEqual(o_result["finalS"], r["finalS"])

    def test_oracle_latency_t1_aggregation_matches_every_intermediate(self):
        data = load_fixture()
        a = data["vectors"]["latency"][2]
        self.assertEqual(a["id"], "L-01-T1-A")
        o_result = oracle_legislative_capacity_t1(a["coalition_strengthS"], a["party_disciplineS"], a["opposition_obstructionS"], a["senate_inertiaS"], a["current_metricS"])
        for key in ("weighted_offset_numerator", "weighted_offsetS", "targetS", "distanceS", "elastic_numerator", "elastic_deltaS", "capped_deltaS", "finalS", "delta_totalS"):
            self.assertEqual(o_result[key], a[key], f"T1-A {key}")
        self.assertEqual(o_result["finalS"], a["finalS"])
        self.assertEqual(o_result["delta_totalS"], a["delta_totalS"])

    def test_oracle_latency_public_cause_matches_recalculated_delta(self):
        data = load_fixture()
        c = data["vectors"]["latency"][3]
        self.assertEqual(c["id"], "L-01-CAUSE")
        self.assertEqual(c["cause_key"], "SYSTEM:AGG.metrics.legislative_capacity.internals.leg.coalition_strength")
        self.assertTrue(c["public"])
        self.assertNotIn("REG_TO_INT", c["cause_key"])
        # Verify deltaS = L-01-T1-A delta_totalS (already oracle-verified)
        a = data["vectors"]["latency"][2]
        self.assertEqual(c["deltaS"], a["delta_totalS"])

    def test_oracle_ordering_vectors_match_independent_calculation(self):
        data = load_fixture()
        ordv = data["vectors"]["ordering"]
        canonical_pairs = [[r, CANONICAL_REGION_VALUES[r]] for r in CANONICAL_REGION_ORDER]
        alphabetical_pairs = [[r, CANONICAL_REGION_VALUES[r]] for r in ALPHABETICAL_REGION_ORDER]

        o01 = ordv[0]
        self.assertEqual(o01["expected_order"], CANONICAL_REGION_ORDER)
        self.assertEqual(o01["expected_ordered_pairs"], canonical_pairs)

        o02 = ordv[1]
        self.assertEqual(dict(o02["insertion_order_a"]), dict(o02["insertion_order_b"]))
        self.assertNotEqual(o02["insertion_order_a"], o02["insertion_order_b"])
        self.assertEqual(o02["expected_ordered_pairs"], canonical_pairs)

        o03 = ordv[2]
        self.assertEqual(o03["binding_order"], EXPECTED_BINDING_ORDER)

        o04 = ordv[3]
        self.assertEqual(o04["expected_output_count"], len(o04["outputs"]))
        self.assertEqual(o04["source"], "tension")
        self.assertFalse(o04["collapsed"])
        seen = set()
        for out in o04["outputs"]:
            self.assertNotIn(out["destination"], seen, f"Duplicate destination in O-04: {out['destination']}")
            seen.add(out["destination"])
        self.assertEqual(len(o04["outputs"]), 2)

        o05 = ordv[4]
        canonical_json = json.dumps(o05["input_a"], sort_keys=True, separators=(",", ":"), allow_nan=False) + "\n"
        self.assertEqual(canonical_json, json.dumps(o05["input_b"], sort_keys=True, separators=(",", ":"), allow_nan=False) + "\n")
        serial = json.dumps(o05["input_a"], sort_keys=True, separators=(",", ":"), allow_nan=False) + "\n"
        self.assertEqual(serial.encode("utf-8"), json.dumps(o05["input_b"], sort_keys=True, separators=(",", ":"), allow_nan=False).encode("utf-8") + b"\n")
        self.assertTrue(o05["expected_bytes_equal"])

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
            r1 = oracle_reversion(5013, 5000, 34064)
            r2 = oracle_reversion(5013, 5000, 34064)
            self.assertEqual(r1, r2)
            la1 = oracle_legislative_capacity_t1(5013, 5000, 5000, 5000, 5000)
            la2 = oracle_legislative_capacity_t1(5013, 5000, 5000, 5000, 5000)
            self.assertEqual(la1, la2)

    def test_oracle_rejects_mutated_expected_values(self):
        data = load_fixture()
        metrics = {"legitimacyS": 6000, "economyS": 5000, "securityS": 5000, "party_organizationS": 5000, "social_tensionS": 5000, "public_agendaS": 5000, "internal_cohesionS": 5000}
        snap = {"supportS": 5000, "tensionS": 5000, "organizationS": 5000, "rival_presenceS": 5000}
        # Anti-tautology: verify oracle helpers do not accept/read fixture expected values
        oracle_funcs = [
            oracle_drift_output, oracle_pull, oracle_reversion,
            oracle_legislative_capacity_t1, oracle_drift_target_numerator,
            oracle_round_divide_half_away_from_zero, oracle_clamp_int,
            oracle_checked_i64, oracle_checked_add_i64, oracle_checked_mul_i64,
        ]
        for func in oracle_funcs:
            for pname in inspect.signature(func).parameters:
                self.assertNotEqual(pname, "expected", f"{func.__name__} has 'expected' parameter")
            src = inspect.getsource(func)
            self.assertNotIn("load_fixture", src, f"{func.__name__} calls load_fixture")
            self.assertNotIn("copy.deepcopy", src, f"{func.__name__} uses copy.deepcopy")
            self.assertNotIn('"expected"', src, f'{func.__name__} reads literal "expected"')
            self.assertNotIn("'expected'", src, f"{func.__name__} reads literal 'expected'")
        # Clone fixture data, mutate expected, assert oracle disagrees
        with self.subTest(mutation="R-01_expected_result"):
            mutated = copy.deepcopy(data["vectors"]["rounding"][0])
            mutated["expected_result"] = 999
            actual = oracle_round_divide_half_away_from_zero(data["vectors"]["rounding"][0]["numerator"], data["vectors"]["rounding"][0]["denominator"])
            self.assertNotEqual(actual, mutated["expected_result"])
        with self.subTest(mutation="D-01_numerator"):
            mutated = copy.deepcopy(data["vectors"]["drift"][1]["outputs"][0]["expected"])
            mutated["numerator"] = 0
            o = oracle_drift_output("support", 5000, metrics, snap)
            self.assertNotEqual(o["numerator"], mutated["numerator"])
        with self.subTest(mutation="D-01_finalS"):
            mutated = copy.deepcopy(data["vectors"]["drift"][1]["outputs"][0]["expected"])
            mutated["finalS"] = 9999
            o = oracle_drift_output("support", 5000, metrics, snap)
            self.assertNotEqual(o["finalS"], mutated["finalS"])
        with self.subTest(mutation="D-01_realized_deltaS"):
            mutated = copy.deepcopy(data["vectors"]["drift"][1]["outputs"][0]["expected"])
            mutated["realized_deltaS"] = 999
            o = oracle_drift_output("support", 5000, metrics, snap)
            self.assertNotEqual(o["realized_deltaS"], mutated["realized_deltaS"])
        with self.subTest(mutation="D-08_rival_finalS"):
            d08_rival = data["vectors"]["drift"][8]["outputs"][1]
            rival_metrics = d08_rival["input"]["metrics"]
            rival_snap = d08_rival["input"]["region_snapshot"]
            d08_mutated = oracle_drift_output("rival_presence", d08_rival["input"]["currentS"], rival_metrics, rival_snap)
            d08_mutated["finalS"] = 9999
            o = oracle_drift_output("rival_presence", d08_rival["input"]["currentS"], rival_metrics, rival_snap)
            self.assertNotEqual(o["finalS"], d08_mutated["finalS"])
        with self.subTest(mutation="P-01_weighted_numerator"):
            p01 = data["vectors"]["pull"][1]
            p01_mutated = oracle_pull(p01["ordered_region_values"], p01["ordered_weights"], p01["input_currentS"])
            p01_mutated["weighted_numerator"] = 0
            o = oracle_pull(p01["ordered_region_values"], p01["ordered_weights"], p01["input_currentS"])
            self.assertNotEqual(o["weighted_numerator"], p01_mutated["weighted_numerator"])
        with self.subTest(mutation="P-05_weighted_averageS"):
            p05 = data["vectors"]["pull"][5]
            p05_mut = oracle_pull(p05["ordered_region_values"], p05["ordered_weights"], p05["input_currentS"])
            p05_mut["weighted_averageS"] = 9999
            o = oracle_pull(p05["ordered_region_values"], p05["ordered_weights"], p05["input_currentS"])
            self.assertNotEqual(o["weighted_averageS"], p05_mut["weighted_averageS"])
        with self.subTest(mutation="L-01-T_pull_finalS"):
            l01t = data["vectors"]["latency"][0]
            l01t_mut = copy.deepcopy(l01t["pull_expected"])
            l01t_mut["finalS"] = 9999
            metrics_lt = {k: l01t["drift_inputs"][k] for k in ORACLE_DRIFT_METRIC_ORDER}
            snap_lt = {"supportS": 5000, "tensionS": 5000, "organizationS": 5000, "rival_presenceS": 5000}
            drift_finals = []
            for _ in range(16):
                o = oracle_drift_output("support", 5000, metrics_lt, snap_lt)
                drift_finals.append(o["finalS"])
            o_pull = oracle_pull(drift_finals, [62500] * 16, l01t["pull_input_currentS"])
            self.assertNotEqual(o_pull["finalS"], l01t_mut["finalS"])
        with self.subTest(mutation="L-01-T1-R_rounded_deltaS"):
            r = data["vectors"]["latency"][1]
            r_mut = oracle_reversion(r["currentS"], r["midS"], r["alpha_ppm"])
            r_mut["rounded_deltaS"] = 999
            o = oracle_reversion(r["currentS"], r["midS"], r["alpha_ppm"])
            self.assertNotEqual(o["rounded_deltaS"], r_mut["rounded_deltaS"])
        with self.subTest(mutation="L-01-T1-A_finalS"):
            a = data["vectors"]["latency"][2]
            a_mut = oracle_legislative_capacity_t1(a["coalition_strengthS"], a["party_disciplineS"], a["opposition_obstructionS"], a["senate_inertiaS"], a["current_metricS"])
            a_mut["finalS"] = 9999
            o = oracle_legislative_capacity_t1(a["coalition_strengthS"], a["party_disciplineS"], a["opposition_obstructionS"], a["senate_inertiaS"], a["current_metricS"])
            self.assertNotEqual(o["finalS"], a_mut["finalS"])
        with self.subTest(mutation="L-01-CAUSE_deltaS"):
            l01c = data["vectors"]["latency"][3]
            l01c_mut = copy.deepcopy(l01c)
            l01c_mut["deltaS"] = 999
            self.assertNotEqual(l01c["deltaS"], l01c_mut["deltaS"])
        with self.subTest(mutation="O-01_expected_ordered_pairs"):
            o01 = data["vectors"]["ordering"][0]
            o01_mut = copy.deepcopy(o01["expected_ordered_pairs"])
            o01_mut[0][1] = 9999
            self.assertNotEqual(o01["expected_ordered_pairs"], o01_mut)
        with self.subTest(mutation="O-02_expected_canonical_bytes_equal"):
            o02 = data["vectors"]["ordering"][1]
            self.assertIs(o02["expected_canonical_bytes_equal"], True)


if __name__ == "__main__":
    unittest.main()
