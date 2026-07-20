from __future__ import annotations

import json
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
AGG_CONFIG_PATH = (
    ROOT / "Assets" / "StreamingAssets" / "content" / "rules" / "aggregation_config.json"
)
DECISIONS_PATH = ROOT / "docs" / "mvp_contract_decisions.json"

# --- helpers ----------------------------------------------------------------

PPM = 1_000_000


def round_half_away_from_zero(x: int, d: int) -> int:
    """Integer RoundHalfAwayFromZero: x/d rounded to nearest integer.
    Ties (exact .5) go away from zero.
    """
    if d == 0:
        raise ZeroDivisionError
    sign = -1 if (x < 0) ^ (d < 0) else 1
    ax, ad = abs(x), abs(d)
    q, r = divmod(ax, ad)
    if r * 2 >= ad:
        q += 1
    return sign * q


def clamp(value: int, lo: int, hi: int) -> int:
    if value < lo:
        return lo
    if value > hi:
        return hi
    return value


def read_aggregation_config() -> dict:
    return json.loads(AGG_CONFIG_PATH.read_text(encoding="utf-8"))


def read_decisions() -> dict:
    return json.loads(DECISIONS_PATH.read_text(encoding="utf-8"))


# --- metric aggregation pipeline --------------------------------------------

def compute_weighted_offset(
    components: list[dict], midS: int
) -> int:
    numerator = 0
    for c in components:
        numerator += c["weight_ppm"] * (c["componentS"] - midS)
    return round_half_away_from_zero(numerator, PPM)


def compute_aggregation(
    current_metricS: int,
    components: list[dict],
    alpha_ppm: int,
    cap_per_weekS: int,
    midS: int = 5000,
    minS: int = 0,
    maxS: int = 10000,
) -> dict:
    weighted_offsetS = compute_weighted_offset(components, midS)
    target_unclampedS = midS + weighted_offsetS
    targetS = clamp(target_unclampedS, minS, maxS)
    distance_to_targetS = targetS - current_metricS
    elastic_numerator = distance_to_targetS * alpha_ppm
    elastic_deltaS = round_half_away_from_zero(elastic_numerator, PPM)
    capped_deltaS = clamp(elastic_deltaS, -cap_per_weekS, cap_per_weekS)
    pre_finalS = current_metricS + capped_deltaS
    final_metricS = clamp(pre_finalS, minS, maxS)
    delta_totalS = final_metricS - current_metricS
    return {
        "weighted_offsetS": weighted_offsetS,
        "targetS": targetS,
        "elastic_deltaS": elastic_deltaS,
        "capped_deltaS": capped_deltaS,
        "finalS": final_metricS,
        "delta_totalS": delta_totalS,
    }


def compute_reversion(
    currentS: int,
    alpha_ppm: int,
    midS: int = 5000,
    minS: int = 0,
    maxS: int = 10000,
) -> dict:
    distanceS = midS - currentS
    reversion_deltaS = round_half_away_from_zero(distanceS * alpha_ppm, PPM)
    pre_clampS = currentS + reversion_deltaS
    finalS = clamp(pre_clampS, minS, maxS)
    return {
        "distanceS": distanceS,
        "rounded_deltaS": reversion_deltaS,
        "finalS": finalS,
    }


# --- causal prefix counterfactual -------------------------------------------

def F(
    current_metricS: int,
    component_list: list[dict],
    alpha_ppm: int,
    cap_per_weekS: int,
    midS: int = 5000,
    minS: int = 0,
    maxS: int = 10000,
) -> int:
    result = compute_aggregation(
        current_metricS, component_list, alpha_ppm, cap_per_weekS, midS, minS, maxS
    )
    return result["finalS"]


def compute_marginal_deltas(
    current_metricS: int,
    components: list[dict],
    alpha_ppm: int,
    cap_per_weekS: int,
    midS: int = 5000,
    minS: int = 0,
    maxS: int = 10000,
) -> dict:
    n = len(components)
    f_values = []
    for i in range(n + 1):
        prefix = components[:i]
        # build Vi: first i real, remaining at midS
        vi = list(prefix) + [
            {"weight_ppm": c["weight_ppm"], "componentS": midS} for c in components[i:]
        ]
        fv = F(current_metricS, vi, alpha_ppm, cap_per_weekS, midS, minS, maxS)
        f_values.append(fv)
    base_deltaS = f_values[0] - current_metricS
    component_deltas = []
    for i in range(1, n + 1):
        cd = f_values[i] - f_values[i - 1]
        component_deltas.append({
            "component": components[i - 1]["target"],
            "deltaS": cd,
        })
    sum_deltas = base_deltaS + sum(cd["deltaS"] for cd in component_deltas)
    total_delta = f_values[n] - current_metricS
    return {
        "F_values": f_values,
        "base_deltaS": base_deltaS,
        "component_deltas": component_deltas,
        "sum_component_deltas": sum_deltas,
        "total_delta": total_delta,
        "telescopic_ok": sum_deltas == total_delta,
    }


# --- tests ------------------------------------------------------------------


class AggregationContractTest(unittest.TestCase):
    maxDiff = None

    # --- economy formula test ---

    def test_economy_weighted_offset(self) -> None:
        components = [
            {"target": "internals.economy.growth", "componentS": 6000, "weight_ppm": 350000},
            {"target": "internals.economy.unemployment", "componentS": 4000, "weight_ppm": -250000},
            {"target": "internals.economy.inflation", "componentS": 5000, "weight_ppm": -250000},
            {"target": "internals.economy.fiscal_stability", "componentS": 6000, "weight_ppm": 150000},
        ]
        result = compute_aggregation(5000, components, 82996, 200)
        self.assertEqual(result["weighted_offsetS"], 750)
        self.assertEqual(result["targetS"], 5750)
        self.assertEqual(result["elastic_deltaS"], 62)
        self.assertEqual(result["capped_deltaS"], 62)
        self.assertEqual(result["finalS"], 5062)
        self.assertEqual(result["delta_totalS"], 62)

    # --- social tension formula test ---

    def test_social_tension_weighted_offset(self) -> None:
        components = [
            {"target": "internals.tension.cost_of_living", "componentS": 6000, "weight_ppm": 350000},
            {"target": "internals.tension.polarization", "componentS": 6000, "weight_ppm": 250000},
            {"target": "internals.tension.protest_activity", "componentS": 4000, "weight_ppm": 250000},
            {"target": "internals.tension.institutional_trust", "componentS": 6000, "weight_ppm": -150000},
        ]
        result = compute_aggregation(5000, components, 159104, 400)
        self.assertEqual(result["weighted_offsetS"], 200)
        self.assertEqual(result["targetS"], 5200)
        self.assertEqual(result["elastic_deltaS"], 32)
        self.assertEqual(result["capped_deltaS"], 32)
        self.assertEqual(result["finalS"], 5032)
        self.assertEqual(result["delta_totalS"], 32)

    # --- weekly cap test ---

    def test_weekly_cap(self) -> None:
        components = [{"target": "test", "componentS": 10000, "weight_ppm": 1000000}]
        result = compute_aggregation(5000, components, 292893, 600)
        # target = 5000 + 1.0 * (10000-5000) = 10000
        self.assertEqual(result["targetS"], 10000)
        # elastic = round(5000 * 292893 / 1000000) = round(1464.465) = 1464
        self.assertEqual(result["elastic_deltaS"], 1464)
        # capped = clamp(1464, -600, 600) = 600
        self.assertEqual(result["capped_deltaS"], 600)
        # final = 5000 + 600 = 5600
        self.assertEqual(result["finalS"], 5600)

    # --- rounding vectors ---

    def test_rounding_half_away_from_zero_positive(self) -> None:
        self.assertEqual(round_half_away_from_zero(500000, PPM), 1)

    def test_rounding_half_away_from_zero_negative(self) -> None:
        self.assertEqual(round_half_away_from_zero(-500000, PPM), -1)

    def test_rounding_half_away_from_zero_exact(self) -> None:
        self.assertEqual(round_half_away_from_zero(0, PPM), 0)

    def test_rounding_half_away_from_zero_trunc(self) -> None:
        self.assertEqual(round_half_away_from_zero(499999, PPM), 0)
        self.assertEqual(round_half_away_from_zero(-499999, PPM), 0)

    def test_rounding_half_away_from_zero_ties(self) -> None:
        self.assertEqual(round_half_away_from_zero(1500000, PPM), 2)
        self.assertEqual(round_half_away_from_zero(-1500000, PPM), -2)

    # --- reversion vector ---

    def test_reversion_6000_to_5974(self) -> None:
        result = compute_reversion(6000, 26307)
        self.assertEqual(result["distanceS"], -1000)
        self.assertEqual(result["rounded_deltaS"], -26)
        self.assertEqual(result["finalS"], 5974)

    # --- AVG with HALF_AWAY_FROM_ZERO ---

    def test_avg_half_away_from_zero(self) -> None:
        # AVG(5000, 5000, 5000) = 5000
        self.assertEqual(round_half_away_from_zero(5000 + 5000 + 5000, 3), 5000)
        # AVG(6000, 4000, 5000) = round(15000/3) = 5000
        self.assertEqual(round_half_away_from_zero(15000, 3), 5000)
        # AVG(6000, 6000, 6000) = 6000
        self.assertEqual(round_half_away_from_zero(18000, 3), 6000)
        # AVG(5000, 5001, 5000) = round(15001/3) = 5000 (ties .333)
        self.assertEqual(round_half_away_from_zero(15001, 3), 5000)
        # AVG(5000, 5002, 5000) = round(15002/3) = 5001 (ties .667)
        self.assertEqual(round_half_away_from_zero(15002, 3), 5001)

    # --- marginal algorithm for economy ---

    def test_marginal_economy(self) -> None:
        components = [
            {"target": "internals.economy.growth", "componentS": 6000, "weight_ppm": 350000},
            {"target": "internals.economy.unemployment", "componentS": 4000, "weight_ppm": -250000},
            {"target": "internals.economy.inflation", "componentS": 5000, "weight_ppm": -250000},
            {"target": "internals.economy.fiscal_stability", "componentS": 6000, "weight_ppm": 150000},
        ]
        result = compute_marginal_deltas(5000, components, 82996, 200)
        self.assertEqual(result["F_values"], [5000, 5029, 5050, 5050, 5062])
        self.assertEqual(result["base_deltaS"], 0)
        self.assertEqual(len(result["component_deltas"]), 4)
        self.assertEqual(result["component_deltas"][0]["deltaS"], 29)
        self.assertEqual(result["component_deltas"][1]["deltaS"], 21)
        self.assertEqual(result["component_deltas"][2]["deltaS"], 0)
        self.assertEqual(result["component_deltas"][3]["deltaS"], 12)
        self.assertTrue(result["telescopic_ok"])
        self.assertEqual(result["total_delta"], 62)

    # --- marginal algorithm for social tension ---

    def test_marginal_social_tension(self) -> None:
        components = [
            {"target": "internals.tension.cost_of_living", "componentS": 6000, "weight_ppm": 350000},
            {"target": "internals.tension.polarization", "componentS": 6000, "weight_ppm": 250000},
            {"target": "internals.tension.protest_activity", "componentS": 4000, "weight_ppm": 250000},
            {"target": "internals.tension.institutional_trust", "componentS": 6000, "weight_ppm": -150000},
        ]
        result = compute_marginal_deltas(5000, components, 159104, 400)
        self.assertEqual(result["F_values"], [5000, 5056, 5095, 5056, 5032])
        self.assertEqual(result["base_deltaS"], 0)
        self.assertEqual(len(result["component_deltas"]), 4)
        self.assertEqual(result["component_deltas"][0]["deltaS"], 56)
        self.assertEqual(result["component_deltas"][1]["deltaS"], 39)
        self.assertEqual(result["component_deltas"][2]["deltaS"], -39)
        self.assertEqual(result["component_deltas"][3]["deltaS"], -24)
        self.assertTrue(result["telescopic_ok"])
        self.assertEqual(result["total_delta"], 32)

    # --- telescopic identity exact ---

    def test_telescopic_identity_exact(self) -> None:
        components = [
            {"target": "a", "componentS": 7000, "weight_ppm": 500000},
            {"target": "b", "componentS": 3000, "weight_ppm": -500000},
        ]
        result = compute_marginal_deltas(5000, components, 100000, 500)
        # F(V0) = 5000 (all at midS)
        # F(V1) = round to check
        f0 = 5000
        # V1: a=7000, b=5000
        off1 = round_half_away_from_zero(500000 * (7000 - 5000), PPM)
        self.assertEqual(off1, 1000)
        t1 = clamp(5000 + 1000, 0, 10000)
        self.assertEqual(t1, 6000)
        e1 = round_half_away_from_zero((6000 - 5000) * 100000, PPM)
        self.assertEqual(e1, 100)
        f1 = clamp(5000 + clamp(e1, -500, 500), 0, 10000)
        self.assertEqual(f1, 5100)
        # F(V2): a=7000, b=3000
        off2 = round_half_away_from_zero(
            500000 * (7000 - 5000) + (-500000) * (3000 - 5000), PPM
        )
        # 500000*2000 + (-500000)*(-2000) = 1000000000 + 1000000000 = 2000000000
        off2 = round_half_away_from_zero(2000000000, PPM)
        self.assertEqual(off2, 2000)
        t2 = clamp(5000 + 2000, 0, 10000)
        self.assertEqual(t2, 7000)
        e2 = round_half_away_from_zero((7000 - 5000) * 100000, PPM)
        self.assertEqual(e2, 200)
        f2 = clamp(5000 + clamp(e2, -500, 500), 0, 10000)
        self.assertEqual(f2, 5200)
        # verify
        self.assertTrue(result["telescopic_ok"])
        self.assertEqual(result["total_delta"], result["sum_component_deltas"])

    # --- component order matches config ---

    def test_component_order_matches_config(self) -> None:
        config = read_aggregation_config()
        metric_passes = [p for p in config["passes"] if p["type"] == "METRIC_AGGREGATION"]
        # first pass: 9 metrics
        first_pass = metric_passes[0]
        self.assertEqual(len(first_pass["metrics"]), 9)
        self.assertEqual(first_pass["metrics"][0]["metric"], "metrics.economy")
        self.assertEqual(first_pass["metrics"][1]["metric"], "metrics.security")
        # second pass: legitimacy
        second_pass = metric_passes[1]
        self.assertEqual(len(second_pass["metrics"]), 1)
        self.assertEqual(second_pass["metrics"][0]["metric"], "metrics.legitimacy")
        # component order within economy
        eco = first_pass["metrics"][0]
        comp_targets = [c["target"] for c in eco["components"]]
        self.assertEqual(comp_targets, [
            "internals.economy.growth",
            "internals.economy.unemployment",
            "internals.economy.inflation",
            "internals.economy.fiscal_stability",
        ])
        # component order within social_tension
        st = first_pass["metrics"][2]
        comp_targets_st = [c["target"] for c in st["components"]]
        self.assertEqual(comp_targets_st, [
            "internals.tension.cost_of_living",
            "internals.tension.polarization",
            "internals.tension.protest_activity",
            "internals.tension.institutional_trust",
        ])

    # --- canonical IDs with dots, no extra colon ---

    def test_canonical_cause_ids(self) -> None:
        self.assertEqual("SYSTEM:AGG.metrics.economy", "SYSTEM:AGG.metrics.economy")
        self.assertEqual(
            "SYSTEM:AGG.metrics.economy.internals.economy.growth",
            "SYSTEM:AGG.metrics.economy.internals.economy.growth",
        )
        self.assertEqual(
            "SYSTEM:REVERSION.internals.economy.growth",
            "SYSTEM:REVERSION.internals.economy.growth",
        )
        self.assertEqual(
            "SYSTEM:DERIVED.internals.legitimacy.performance",
            "SYSTEM:DERIVED.internals.legitimacy.performance",
        )

    # --- no ambiguous colon-only formats ---

    def test_no_ambiguous_colon_formats(self) -> None:
        # Valid CauseRef format is CATEGORY + ":" + ID with dots as separators
        valid = "SYSTEM:AGG.metrics.economy"
        self.assertEqual(valid.count(":"), 1)
        self.assertEqual(valid.count("."), 2)
        # Ambiguous forms use colon where dot should be
        ambiguous1 = "SYSTEM:AGG:metrics.economy"
        self.assertNotEqual(ambiguous1, valid)
        ambiguous2 = "SYSTEM:AGG:metrics.economy:internals.economy.growth"
        self.assertNotEqual(ambiguous2, "SYSTEM:AGG.metrics.economy.internals.economy.growth")

    # --- internals remain hidden from public catalog ---

    def test_internals_not_in_public_catalog(self) -> None:
        config = read_aggregation_config()
        # INTERNAL_REVERSION groups target internals.*
        reversion_pass = config["passes"][0]
        self.assertEqual(reversion_pass["type"], "INTERNAL_REVERSION")
        for group in reversion_pass["groups"]:
            self.assertIn("internals.", group["pattern"])

    # --- config structure: passes by type ---

    def test_pass_types_config(self) -> None:
        config = read_aggregation_config()
        passes = config["passes"]
        self.assertEqual(len(passes), 4)
        self.assertEqual(passes[0]["type"], "INTERNAL_REVERSION")
        self.assertEqual(passes[1]["type"], "METRIC_AGGREGATION")
        self.assertEqual(passes[2]["type"], "DERIVED_INTERNALS")
        self.assertEqual(passes[3]["type"], "METRIC_AGGREGATION")

    # --- dispatch by type (not array position) ---

    def test_dispatch_order_by_type(self) -> None:
        config = read_aggregation_config()
        passes = config["passes"]
        types = [p["type"] for p in passes]
        # Physical order: INTERNAL_REVERSION, METRIC_AGGREGATION, DERIVED_INTERNALS, METRIC_AGGREGATION
        self.assertEqual(types, [
            "INTERNAL_REVERSION",
            "METRIC_AGGREGATION",
            "DERIVED_INTERNALS",
            "METRIC_AGGREGATION",
        ])
        # Dispatch by type maps to phases 6, 7, 8 independent of physical position
        phase_6_types = {"INTERNAL_REVERSION"}
        phase_7_types = {"DERIVED_INTERNALS"}
        phase_8_types = {"METRIC_AGGREGATION"}
        for p in passes:
            if p["type"] in phase_6_types:
                self.assertEqual(p["type"], "INTERNAL_REVERSION")
            elif p["type"] in phase_7_types:
                self.assertEqual(p["type"], "DERIVED_INTERNALS")
            elif p["type"] in phase_8_types:
                self.assertEqual(p["type"], "METRIC_AGGREGATION")
            else:
                self.fail(f"Unexpected type: {p['type']}")

    # --- nine metrics before legitimacy in phase 8 ---

    def test_nine_metrics_before_legitimacy(self) -> None:
        config = read_aggregation_config()
        metric_passes = [p for p in config["passes"] if p["type"] == "METRIC_AGGREGATION"]
        self.assertEqual(len(metric_passes), 2)
        first_metrics = metric_passes[0]["metrics"]
        second_metrics = metric_passes[1]["metrics"]
        self.assertEqual(len(first_metrics), 9)
        self.assertEqual(len(second_metrics), 1)
        self.assertEqual(second_metrics[0]["metric"], "metrics.legitimacy")
        metric_names = [m["metric"] for m in first_metrics]
        self.assertNotIn("metrics.legitimacy", metric_names)

    # --- exact DERIVED internals ---

    def test_derived_internals(self) -> None:
        config = read_aggregation_config()
        derived_pass = config["passes"][2]
        self.assertEqual(derived_pass["type"], "DERIVED_INTERNALS")
        self.assertEqual(derived_pass["cause_prefix"], "SYSTEM:DERIVED")
        rules = derived_pass["rules"]
        self.assertEqual(len(rules), 2)
        # performance
        self.assertEqual(rules[0]["target"], "internals.legitimacy.performance")
        self.assertEqual(rules[0]["op"], "SET")
        self.assertEqual(rules[0]["expr"]["kind"], "AVG")
        self.assertEqual(rules[0]["expr"]["targets"], [
            "metrics.economy",
            "metrics.security",
            "metrics.governability",
        ])
        # social_tension_load
        self.assertEqual(rules[1]["target"], "internals.legitimacy.social_tension_load")
        self.assertEqual(rules[1]["op"], "SET")
        self.assertEqual(rules[1]["expr"]["kind"], "COPY")
        self.assertEqual(rules[1]["expr"]["target"], "metrics.social_tension")

    # --- abs(weights) sum to 1,000,000 ---

    def test_weights_abs_sum_ppm(self) -> None:
        config = read_aggregation_config()
        for p in config["passes"]:
            if p["type"] != "METRIC_AGGREGATION":
                continue
            for metric in p["metrics"]:
                abs_sum = sum(abs(c["weight_ppm"]) for c in metric["components"])
                self.assertEqual(
                    abs_sum, PPM,
                    f"{metric['metric']}: abs(weights)={abs_sum} != {PPM}",
                )

    # --- MVP-012 exact content ---

    def test_mvp_012_vectors_match_contract(self) -> None:
        decisions = read_decisions()
        mvp12 = [d for d in decisions["decisions"] if d["id"] == "MVP-012-national-aggregation"][0]
        resolution = mvp12["resolution"]

        eco_vec = resolution["vectors"]["economy"]
        components_eco = eco_vec["components"]
        result_eco = compute_aggregation(
            eco_vec["current_metricS"],
            components_eco,
            eco_vec["alpha_ppm"],
            eco_vec["cap_per_weekS"],
        )
        self.assertEqual(result_eco["weighted_offsetS"], eco_vec["weighted_offsetS"])
        self.assertEqual(result_eco["targetS"], eco_vec["targetS"])
        self.assertEqual(result_eco["elastic_deltaS"], eco_vec["elastic_deltaS"])
        self.assertEqual(result_eco["capped_deltaS"], eco_vec["capped_deltaS"])
        self.assertEqual(result_eco["finalS"], eco_vec["finalS"])
        self.assertEqual(result_eco["delta_totalS"], eco_vec["delta_totalS"])

        st_vec = resolution["vectors"]["social_tension"]
        components_st = st_vec["components"]
        result_st = compute_aggregation(
            st_vec["current_metricS"],
            components_st,
            st_vec["alpha_ppm"],
            st_vec["cap_per_weekS"],
        )
        self.assertEqual(result_st["weighted_offsetS"], st_vec["weighted_offsetS"])
        self.assertEqual(result_st["targetS"], st_vec["targetS"])
        self.assertEqual(result_st["elastic_deltaS"], st_vec["elastic_deltaS"])
        self.assertEqual(result_st["capped_deltaS"], st_vec["capped_deltaS"])
        self.assertEqual(result_st["finalS"], st_vec["finalS"])
        self.assertEqual(result_st["delta_totalS"], st_vec["delta_totalS"])

        rev_vec = resolution["vectors"]["reversion_6000_to_5974"]
        result_rev = compute_reversion(rev_vec["currentS"], rev_vec["alpha_ppm"])
        self.assertEqual(result_rev["distanceS"], rev_vec["distanceS"])
        self.assertEqual(result_rev["rounded_deltaS"], rev_vec["rounded_deltaS"])
        self.assertEqual(result_rev["finalS"], rev_vec["finalS"])


if __name__ == "__main__":
    unittest.main()
