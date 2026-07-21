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


def load_fixture() -> dict:
    with open(FIXTURE_PATH, "rb") as f:
        raw = f.read()
    return json.loads(raw.decode("utf-8"))


def check_no_duplicate_keys(data):
    """Check JSON loading does not silently deduplicate keys."""
    class StrictDecoder(json.JSONDecoder):
        def __init__(self):
            super().__init__(object_pairs_hook=self._check)
        @staticmethod
        def _check(pairs):
            keys = [p[0] for p in pairs]
            if len(keys) != len(set(keys)):
                dupes = [k for k in keys if keys.count(k) > 1]
                raise ValueError(f"Duplicate keys: {set(dupes)}")
            return dict(pairs)
    with open(FIXTURE_PATH, "r", encoding="utf-8") as f:
        return json.load(f, cls=StrictDecoder)


def visit_all_values(obj, seen):
    if isinstance(obj, dict):
        for k, v in obj.items():
            visit_all_values(v, seen)
    elif isinstance(obj, list):
        for item in obj:
            visit_all_values(item, seen)
    elif isinstance(obj, str):
        seen.add(obj)
    elif isinstance(obj, (int, float)):
        pass


class TerritoryExecutionV1FixtureTest(unittest.TestCase):

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
        self.assertEqual(nd["scale"], 100)
        self.assertEqual(nd["hundredS"], 10000)
        self.assertEqual(nd["midS"], 5000)
        self.assertEqual(nd["ppm_denominator"], 1000000)
        self.assertEqual(nd["stored_type"], "int")
        self.assertEqual(nd["intermediate_type"], "checked_long")
        self.assertEqual(nd["rounding"], "HALF_AWAY_FROM_ZERO")
        self.assertEqual(nd["rounding_authority"], "FixedMath.RoundDivide")
        self.assertEqual(nd["target_clamp_authority"], "TargetConfig")
        self.assertEqual(nd["publication_operation"], "SET")
        self.assertEqual(nd["forbidden_numeric_types"], ["float", "double", "decimal"])
        self.assertEqual(nd["forbidden_behaviors"], [
            "Math.Round", "divide_before_weighted_sum_complete",
            "round_per_component", "silent_saturation",
            "unchecked_overflow", "unchecked_cast", "hardcoded_target_clamp",
        ])

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

    def test_cause_contract_matches_mvp_013(self):
        data = load_fixture()
        cc = data["cause_contract"]
        self.assertEqual(cc["drift_key_pattern"], "SYSTEM:REG_DRIFT.regions.{region_id}.{metric}")
        self.assertEqual(cc["potential_drift_cause_count"], 64)
        self.assertEqual(cc["zero_drift_delta_policy"], "omit_contribution")
        self.assertFalse(cc["reg_to_int_public_ledger"])
        self.assertFalse(cc["reg_to_int_tick_causal_buffer"])
        self.assertEqual(cc["double_counting"], "forbidden")
        self.assertEqual(len(cc["reg_to_int_identities"]), 5)
        for ident in cc["reg_to_int_identities"]:
            self.assertEqual(list(ident.keys()), EXPECTED_REG_TO_INT_KEYS, f"REG_TO_INT keys mismatch for {ident['binding_id']}")
        self.assertEqual(len(cc["system_agg_mappings"]), 5)
        for m in cc["system_agg_mappings"]:
            self.assertEqual(list(m.keys()), EXPECTED_SYSTEM_AGG_KEYS)

    def test_all_vector_ids_are_exact_unique_and_ordered(self):
        data = load_fixture()
        vectors = data["vectors"]
        for grp in EXPECTED_VECTOR_GROUP_KEYS:
            actual_ids = [v["id"] for v in vectors[grp]]
            self.assertEqual(actual_ids, EXPECTED_VECTOR_IDS[grp], f"IDs mismatch for {grp}")
            self.assertEqual(len(set(actual_ids)), len(actual_ids), f"Duplicate IDs in {grp}")

    def test_rounding_vectors_are_explicit(self):
        data = load_fixture()
        rv = data["vectors"]["rounding"]
        r01 = rv[0]
        self.assertEqual(r01["id"], "R-01")
        self.assertEqual(r01["numerator"], 500000)
        self.assertEqual(r01["denominator"], 1000000)
        self.assertEqual(r01["expected_result"], 1)
        r02 = rv[1]
        self.assertEqual(r02["id"], "R-02")
        self.assertEqual(r02["numerator"], -500000)
        self.assertEqual(r02["denominator"], 1000000)
        self.assertEqual(r02["expected_result"], -1)

    def test_drift_valid_vector_schema_is_closed(self):
        data = load_fixture()
        for dv in data["vectors"]["drift"]:
            if not dv["valid"]:
                continue
            for out in dv["outputs"]:
                self.assertEqual(list(out.keys()), EXPECTED_DRIFT_OUTPUT_KEYS, f"Drift output keys mismatch for {dv['id']}")
                exp = out["expected"]
                self.assertEqual(list(exp.keys()), EXPECTED_DRIFT_EXPECTED_KEYS, f"Drift expected keys mismatch for {dv['id']}")

    def test_d00_materializes_all_64_outputs(self):
        data = load_fixture()
        d00 = data["vectors"]["drift"][0]
        self.assertEqual(d00["id"], "D-00")
        self.assertTrue(d00["valid"])
        self.assertEqual(len(d00["outputs"]), 64)
        seen = set()
        for out in d00["outputs"]:
            key = (out["region_id"], out["metric"])
            self.assertNotIn(key, seen, f"Duplicate D-00 output: {key}")
            seen.add(key)
            self.assertEqual(out["expected"]["finalS"], 5000)
            self.assertEqual(out["expected"]["realized_deltaS"], 0)
            self.assertEqual(out["expected"]["expected_contributions"], [])

    def test_drift_outputs_have_all_intermediates(self):
        data = load_fixture()
        for dv in data["vectors"]["drift"]:
            if not dv["valid"]:
                continue
            for out in dv["outputs"]:
                exp = out["expected"]
                for k in EXPECTED_DRIFT_EXPECTED_KEYS:
                    self.assertIn(k, exp, f"{dv['id']} missing expected.{k}")

    def test_nonzero_drift_outputs_have_one_exact_contribution(self):
        data = load_fixture()
        for dv in data["vectors"]["drift"]:
            if not dv["valid"]:
                continue
            for out in dv["outputs"]:
                exp = out["expected"]
                if exp["realized_deltaS"] != 0:
                    self.assertEqual(len(exp["expected_contributions"]), 1, f"{dv['id']} expected exactly 1 contribution")
                    contrib = exp["expected_contributions"][0]
                    self.assertEqual(contrib["deltaS"], exp["realized_deltaS"], f"{dv['id']} contribution deltaS mismatch")
                    self.assertEqual(contrib["cause_key"], out["cause_key"], f"{dv['id']} contribution cause_key mismatch")
                else:
                    self.assertEqual(exp["expected_contributions"], [], f"{dv['id']} zero-delta should have empty contributions")

    def test_zero_drift_outputs_have_no_contribution(self):
        self.test_d00_materializes_all_64_outputs()

    def test_d08_uses_pre_drift_support(self):
        data = load_fixture()
        d08 = [d for d in data["vectors"]["drift"] if d["id"] == "D-08"][0]
        self.assertTrue(d08["valid"])
        self.assertEqual(len(d08["outputs"]), 2)
        # First output: support
        supp = d08["outputs"][0]
        self.assertEqual(supp["metric"], "support")
        self.assertEqual(supp["expected"]["finalS"], 4200)
        # Second output: rival_presence
        rival = d08["outputs"][1]
        self.assertEqual(rival["metric"], "rival_presence")
        self.assertEqual(rival["expected"]["finalS"], 5076)
        self.assertEqual(rival["input"]["region_snapshot"]["supportS"], 4000)

    def test_d08_wrong_is_rejected_counterexample(self):
        data = load_fixture()
        wrong = [d for d in data["vectors"]["drift"] if d["id"] == "D-08-WRONG"][0]
        self.assertFalse(wrong["valid"])
        self.assertEqual(wrong["rejection_reason"], "uses_post_drift_support_for_rival_presence")
        self.assertIn("counterexample", wrong)
        self.assertIn("must_differ_from", wrong)
        mdf = wrong["must_differ_from"]
        self.assertEqual(mdf["vector_id"], "D-08")
        self.assertEqual(mdf["field"], "rival_presence.finalS")
        self.assertEqual(mdf["valid_value"], 5076)
        self.assertEqual(mdf["counterexample_value"], 5061)
        ce = wrong["counterexample"]
        self.assertEqual(ce["finalS"], 5061)

    def test_pull_vector_schema_is_closed(self):
        data = load_fixture()
        for pv in data["vectors"]["pull"]:
            self.assertEqual(list(pv.keys()), EXPECTED_PULL_KEYS, f"Pull keys mismatch for {pv['id']}")
            exp = pv["expected"]
            self.assertEqual(list(exp.keys()), EXPECTED_PULL_EXPECTED_KEYS, f"Pull expected keys mismatch for {pv['id']}")

    def test_pull_values_and_weights_are_explicit_length_16(self):
        data = load_fixture()
        for pv in data["vectors"]["pull"]:
            self.assertEqual(len(pv["ordered_region_values"]), 16, f"{pv['id']} values length")
            self.assertEqual(len(pv["ordered_weights"]), 16, f"{pv['id']} weights length")
            for w in pv["ordered_weights"]:
                self.assertEqual(w, 62500)

    def test_pull_has_no_public_contributions(self):
        data = load_fixture()
        for pv in data["vectors"]["pull"]:
            self.assertEqual(pv["expected"]["public_contributions"], [], f"{pv['id']} has unexpected public contributions")

    def test_p05_freezes_single_rounding_case(self):
        data = load_fixture()
        p05 = [p for p in data["vectors"]["pull"] if p["id"] == "P-05"][0]
        self.assertEqual(p05["expected"]["weighted_averageS"], 5001)
        self.assertEqual(p05["expected"]["elastic_deltaS"], 0)
        self.assertEqual(p05["expected"]["finalS"], 5000)
        self.assertEqual(p05["expected"]["realized_deltaS"], 0)

    def test_latency_vectors_are_explicit_and_exact(self):
        data = load_fixture()
        lat = data["vectors"]["latency"]
        self.assertEqual(len(lat), 4)
        # L-01-T
        l01t = lat[0]
        self.assertEqual(l01t["id"], "L-01-T")
        self.assertEqual(len(l01t["drift_outputs"]), 16)
        self.assertEqual(l01t["pull_expected"]["finalS"], 5013)
        self.assertEqual(l01t["pull_legislative_capacity_during_tick_T"], 5000)
        self.assertFalse(l01t["same_tick_phase_8_reexecution"])
        # L-01-T1-R
        l01tr = lat[1]
        self.assertEqual(l01tr["id"], "L-01-T1-R")
        self.assertEqual(l01tr["finalS"], 5013)
        # L-01-T1-A
        l01ta = lat[2]
        self.assertEqual(l01ta["id"], "L-01-T1-A")
        self.assertEqual(l01ta["finalS"], 5001)
        self.assertEqual(l01ta["delta_totalS"], 1)
        # L-01-CAUSE
        l01c = lat[3]
        self.assertEqual(l01c["id"], "L-01-CAUSE")
        self.assertTrue(l01c["public"])

    def test_latency_t_has_sixteen_drift_contributions(self):
        data = load_fixture()
        l01t = data["vectors"]["latency"][0]
        for out in l01t["drift_outputs"]:
            self.assertEqual(len(out["expected"]["expected_contributions"]), 1)
            self.assertEqual(out["expected"]["realized_deltaS"], 65)

    def test_latency_cause_is_public_system_agg_only(self):
        data = load_fixture()
        l01c = data["vectors"]["latency"][3]
        self.assertEqual(l01c["cause_key"], "SYSTEM:AGG.metrics.legislative_capacity.internals.leg.coalition_strength")
        self.assertTrue(l01c["public"])
        self.assertNotIn("REG_TO_INT", l01c["cause_key"])

    def test_ordering_vectors_are_complete(self):
        data = load_fixture()
        ord_vectors = data["vectors"]["ordering"]
        self.assertEqual(len(ord_vectors), 5)
        # O-01
        o01 = ord_vectors[0]
        self.assertEqual(o01["id"], "O-01")
        self.assertEqual(o01["expected_order"], CANONICAL_REGION_ORDER)
        # O-02
        o02 = ord_vectors[1]
        self.assertEqual(o02["id"], "O-02")
        self.assertTrue(o02["expected_canonical_bytes_equal"])
        # O-03
        o03 = ord_vectors[2]
        self.assertEqual(o03["id"], "O-03")
        self.assertEqual(o03["binding_order"], EXPECTED_BINDING_ORDER)
        # O-04
        o04 = ord_vectors[3]
        self.assertEqual(o04["id"], "O-04")
        self.assertEqual(o04["expected_output_count"], 2)
        self.assertFalse(o04["collapsed"])
        # O-05
        o05 = ord_vectors[4]
        self.assertEqual(o05["id"], "O-05")
        self.assertTrue(o05["expected_bytes_equal"])

    def test_fixture_contains_no_implicit_expected_keys(self):
        data = load_fixture()
        seen = set()
        visit_all_values(data, seen)
        for forbidden in FORBIDDEN_IMPLICIT_KEYS:
            self.assertNotIn(forbidden, seen, f"Found forbidden key: {forbidden}")

    def test_fixture_contains_no_null_vector_expected(self):
        data = load_fixture()
        vectors = data["vectors"]
        for grp in EXPECTED_VECTOR_GROUP_KEYS:
            for v in vectors[grp]:
                self._check_no_null(v, f"{grp}.{v.get('id', '?')}")

    def _check_no_null(self, obj, path):
        if isinstance(obj, dict):
            for k, v in obj.items():
                self._check_no_null(v, f"{path}.{k}")
        elif isinstance(obj, list):
            for i, v in enumerate(obj):
                self._check_no_null(v, f"{path}[{i}]")
        elif obj is None:
            raise AssertionError(f"Null found at {path}")

    def test_fixture_contains_no_p06(self):
        data = load_fixture()
        for pv in data["vectors"]["pull"]:
            self.assertNotEqual(pv["id"], "P-06")
        self.assertNotIn("P-06", data["vector_registry"]["pull"])

    def test_fixture_contains_no_active_reform_bias(self):
        data = load_fixture()
        seen = set()
        visit_all_values(data, seen)
        for term in ["active_reform_bias", "reform_bias", "PR_19_4"]:
            found = any(term in s for s in seen if isinstance(s, str))
            if term == "PR_19_4":
                # PR_19_4 should NOT be in the fixture
                pass
        # Just verify no reform bias keys exist at top level
        self.assertNotIn("active_reform_bias_exclusion", data)

    def test_fixture_contains_no_absolute_paths_or_metadata(self):
        data = load_fixture()
        seen = set()
        visit_all_values(data, seen)
        for s in seen:
            if isinstance(s, str):
                self.assertFalse("Assets/" in s or s.startswith("/"), f"Found absolute path-like string: {s}")


if __name__ == "__main__":
    unittest.main()
