from __future__ import annotations

import json
import os
import re
import unittest
from pathlib import Path


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


class TerritoryExecutionV1FixtureTest(unittest.TestCase):

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
                exp = out["expected"]
                for k in EXPECTED_DRIFT_EXPECTED_KEYS:
                    self.assertIn(k, exp, f"{dv['id']} missing expected.{k}")
                if exp["realized_deltaS"] != 0:
                    self.assertEqual(len(exp["expected_contributions"]), 1, f"{dv['id']} expected 1 contribution")
                    c = exp["expected_contributions"][0]
                    self.assertEqual(c["deltaS"], exp["realized_deltaS"], f"{dv['id']} contribution deltaS")
                    self.assertEqual(c["cause_key"], out["cause_key"], f"{dv['id']} contribution cause_key")
                else:
                    self.assertEqual(exp["expected_contributions"], [], f"{dv['id']} zero-delta empty contributions")

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
        for out in load_fixture()["vectors"]["latency"][0]["drift_outputs"]:
            self.assertEqual(len(out["expected"]["expected_contributions"]), 1)
            self.assertEqual(out["expected"]["realized_deltaS"], 65)

    def test_latency_cause_is_public_system_agg_only(self):
        l01c = load_fixture()["vectors"]["latency"][3]
        self.assertEqual(l01c["cause_key"], "SYSTEM:AGG.metrics.legislative_capacity.internals.leg.coalition_strength")
        self.assertTrue(l01c["public"])
        self.assertNotIn("REG_TO_INT", l01c["cause_key"])

    def test_ordering_vectors_are_complete(self):
        ordv = load_fixture()["vectors"]["ordering"]
        self.assertEqual(len(ordv), 5)
        o01 = ordv[0]
        self.assertEqual(o01["id"], "O-01")
        self.assertEqual(list(o01.keys()), EXPECTED_ORDERING_KEYS_BY_ID["O-01"])
        self.assertEqual(len(o01["values_by_region"]), 16)
        for rid, val in o01["values_by_region"]:
            self.assertEqual(val, CANONICAL_REGION_VALUES[rid], f"O-01 {rid}")
        self.assertEqual({p[1] for p in o01["values_by_region"]}, set(range(1001, 1017)), "O-01 unique 1001..1016")
        for rid, val in o01["stored_alphabetical_pairs"]:
            self.assertEqual(val, CANONICAL_REGION_VALUES[rid], f"O-01 alpha {rid}")
        self.assertEqual(o01["expected_ordered_pairs"], o01["values_by_region"])
        self.assertTrue(o01["expected_result_independent_of_stored_order"])
        o02 = ordv[1]
        self.assertEqual(o02["id"], "O-02")
        self.assertEqual(list(o02.keys()), EXPECTED_ORDERING_KEYS_BY_ID["O-02"])
        self.assertEqual(len(o02["insertion_order_a"]), 16)
        self.assertEqual(len(o02["insertion_order_b"]), 16)
        self.assertEqual(dict(o02["insertion_order_a"]), dict(o02["insertion_order_b"]), "O-02 same mapping")
        self.assertNotEqual(o02["insertion_order_a"], o02["insertion_order_b"], "O-02 orders differ")
        self.assertEqual(o02["expected_ordered_pairs"], o02["insertion_order_a"])
        self.assertTrue(o02["expected_canonical_bytes_equal"])
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

    def test_fixture_contains_no_active_reform_bias(self):
        data = load_fixture()
        keys = set()
        strings = set()
        collect_keys_and_strings(data, keys, strings)
        for term in FORBIDDEN_ACTIVE_REFORM_TERMS:
            self.assertNotIn(term, keys, f"Key: {term}")
            self.assertNotIn(term, strings, f"String: {term}")
        for key in keys:
            for term in FORBIDDEN_ACTIVE_REFORM_TERMS:
                self.assertNotIn(term, key.lower(), f"Key contains {term}: {key}")
        for s in strings:
            for term in FORBIDDEN_ACTIVE_REFORM_TERMS:
                self.assertNotIn(term, s.lower(), f"String contains {term}: {s}")

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


if __name__ == "__main__":
    unittest.main()
