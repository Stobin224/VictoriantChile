from __future__ import annotations

import json
import re
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


# --- cause_prefix_materialization helper ------------------------------------

def materialize_cause(source: str, target_path: str) -> str:
    """Build a canonical CauseRef key from source and target.

    source format: CATEGORY:BASE_ID (exactly one ':')
    target_path: dot-separated path, no colon

    Returns CATEGORY:BASE_ID.target_path

    Raises ValueError for invalid formats as specified by cause_prefix_materialization.
    """
    parts = source.split(":")
    if len(parts) != 2:
        raise ValueError(f"source must have exactly one ':': {source}")
    if not parts[0] or not parts[1]:
        raise ValueError(f"empty category or base_id: {source}")
    category, base_id = parts
    if category != "SYSTEM":
        raise ValueError(f"category must be SYSTEM, got: {category}")
    allowed_bases = {"AGG", "REVERSION", "DERIVED"}
    if base_id not in allowed_bases:
        raise ValueError(f"base_id must be one of {allowed_bases}, got: {base_id}")
    if not target_path:
        raise ValueError("target_path must not be empty")
    if ":" in target_path:
        raise ValueError(f"target_path must not contain ':': {target_path}")
    return f"{source}.{target_path}"


# --- dispatch helper ---------------------------------------------------------

PRIMARY_METRICS = [
    "metrics.economy",
    "metrics.security",
    "metrics.social_tension",
    "metrics.public_agenda",
    "metrics.information_quality",
    "metrics.governability",
    "metrics.legislative_capacity",
    "metrics.party_organization",
    "metrics.internal_cohesion",
]
LEGITIMACY_METRIC = "metrics.legitimacy"


def classify_passes(passes: list[dict]) -> dict[str, dict]:
    classified: dict[str, dict] = {}
    seen_metrics: set[str] = set()
    for p in passes:
        pass_type = p["type"]
        if pass_type == "INTERNAL_REVERSION":
            key = "reversion"
        elif pass_type == "DERIVED_INTERNALS":
            key = "derived"
        elif pass_type == "METRIC_AGGREGATION":
            metric_names = [m["metric"] for m in p.get("metrics", [])]
            duplicate_metrics = [m for m in metric_names if m in seen_metrics]
            if duplicate_metrics:
                raise ValueError(f"duplicate aggregation metric: {duplicate_metrics[0]}")
            seen_metrics.update(metric_names)
            if metric_names == [LEGITIMACY_METRIC]:
                key = "legitimacy"
            elif metric_names == PRIMARY_METRICS:
                key = "primary"
            elif LEGITIMACY_METRIC in metric_names:
                raise ValueError("legitimacy cannot be mixed into primary metrics")
            else:
                raise ValueError("primary metric pass must contain the nine canonical primary metrics")
        else:
            raise ValueError(f"unknown pass type: {pass_type}")

        if key in classified:
            raise ValueError(f"duplicate pass role: {key}")
        classified[key] = p

    required = {"reversion", "derived", "primary", "legitimacy"}
    if set(classified) != required:
        raise ValueError(f"aggregation passes must be exactly {sorted(required)}")
    return classified


def dispatch_order(passes: list[dict]) -> list[dict]:
    """Return canonical semantic execution order: reversion, derived, primary, legitimacy."""
    classified = classify_passes(passes)
    return [
        classified["reversion"],
        classified["derived"],
        classified["primary"],
        classified["legitimacy"],
    ]


# --- visibility policy -------------------------------------------------------

VISIBLE_PATTERNS: list[tuple[str, str]] = [
    ("metrics", r"^metrics\.[a-z][a-z0-9_]*$"),
    ("igs_clout_approval", r"^igs\.[a-z][a-z0-9_]*\.(clout|approval)$"),
    ("movements_intensity_direction", r"^movements\.[a-z][a-z0-9_]*\.(intensity|direction)$"),
]
HIDDEN_PATTERNS: list[tuple[str, str]] = [
    ("internals", r"^internals\."),
    ("static_regional_resources", r"^regions\."),
]


def is_visible_target(path: str) -> bool:
    """Return True if target path is publicly visible per the MVP contract."""
    for _name, pat in VISIBLE_PATTERNS:
        if re.match(pat, path):
            return True
    return False


def is_hidden_target(path: str) -> bool:
    """Return True if target path is explicitly hidden per the MVP contract."""
    for _name, pat in HIDDEN_PATTERNS:
        if re.match(pat, path):
            return True
    return False


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
        self.assertEqual(
            materialize_cause("SYSTEM:AGG", "metrics.economy"),
            "SYSTEM:AGG.metrics.economy",
        )
        self.assertEqual(
            materialize_cause("SYSTEM:AGG", "metrics.economy.internals.economy.growth"),
            "SYSTEM:AGG.metrics.economy.internals.economy.growth",
        )
        self.assertEqual(
            materialize_cause("SYSTEM:REVERSION", "internals.economy.growth"),
            "SYSTEM:REVERSION.internals.economy.growth",
        )
        self.assertEqual(
            materialize_cause("SYSTEM:DERIVED", "internals.legitimacy.performance"),
            "SYSTEM:DERIVED.internals.legitimacy.performance",
        )

    def test_materialize_cause_negative_cases(self) -> None:
        # double colon in source
        with self.assertRaises(ValueError):
            materialize_cause("SYSTEM:AGG:EXTRA", "metrics.economy")
        # empty base_id
        with self.assertRaises(ValueError):
            materialize_cause("SYSTEM:", "metrics.economy")
        # category other than SYSTEM
        with self.assertRaises(ValueError):
            materialize_cause("EVENT:AGG", "metrics.economy")
        # unknown base_id
        with self.assertRaises(ValueError):
            materialize_cause("SYSTEM:UNKNOWN", "metrics.economy")
        # empty target
        with self.assertRaises(ValueError):
            materialize_cause("SYSTEM:AGG", "")
        # target with colon
        with self.assertRaises(ValueError):
            materialize_cause("SYSTEM:AGG", "metrics:economy")
        # source with no colon
        with self.assertRaises(ValueError):
            materialize_cause("SYSTEMAGG", "metrics.economy")
        # source with three parts
        with self.assertRaises(ValueError):
            materialize_cause("A:B:C", "metrics.economy")
        # lowercase system
        with self.assertRaises(ValueError):
            materialize_cause("system:AGG", "metrics.economy")

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
        # metrics.* is visible
        self.assertTrue(is_visible_target("metrics.economy"))
        self.assertTrue(is_visible_target("metrics.social_tension"))
        self.assertTrue(is_visible_target("metrics.legitimacy"))
        # IGS regional fields (clout|approval) are visible
        self.assertTrue(is_visible_target("igs.ig_empresarial.clout"))
        self.assertTrue(is_visible_target("igs.ig_sindical.approval"))
        # Movements intensity/direction are visible
        self.assertTrue(is_visible_target("movements.mov_trabajo_huelgas.intensity"))
        self.assertTrue(is_visible_target("movements.mov_trabajo_huelgas.direction"))
        # internals.* is never visible
        self.assertFalse(is_visible_target("internals.economy.growth"))
        self.assertFalse(is_visible_target("internals.tension.cost_of_living"))
        self.assertFalse(is_visible_target("internals.legitimacy.performance"))
        # internals.* is explicitly hidden
        self.assertTrue(is_hidden_target("internals.economy.growth"))
        self.assertTrue(is_hidden_target("internals.tension.cost_of_living"))
        self.assertTrue(is_hidden_target("internals.legitimacy.performance"))
        # Static regional resources are not visible
        self.assertFalse(is_visible_target("regions.metropolitana.population"))
        self.assertFalse(is_visible_target("regions.biobio.area_km2"))

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
        ordered = dispatch_order(passes)
        ordered_types = [p["type"] for p in ordered]
        self.assertEqual(ordered_types, [
            "INTERNAL_REVERSION",
            "DERIVED_INTERNALS",
            "METRIC_AGGREGATION",
            "METRIC_AGGREGATION",
        ])
        self.assertEqual([m["metric"] for m in ordered[2]["metrics"]], PRIMARY_METRICS)
        self.assertEqual([m["metric"] for m in ordered[3]["metrics"]], [LEGITIMACY_METRIC])

    def test_dispatch_order_adversarial_a_b_is_semantic(self) -> None:
        config = read_aggregation_config()
        passes = config["passes"]
        scenario_a = [passes[0], passes[1], passes[2], passes[3]]
        scenario_b = [passes[3], passes[2], passes[0], passes[1]]

        ordered_a = dispatch_order(scenario_a)
        ordered_b = dispatch_order(scenario_b)

        self.assertEqual(ordered_a, ordered_b)
        self.assertEqual([p["type"] for p in ordered_b], [
            "INTERNAL_REVERSION",
            "DERIVED_INTERNALS",
            "METRIC_AGGREGATION",
            "METRIC_AGGREGATION",
        ])
        self.assertEqual([g["pattern"] for g in ordered_b[0]["groups"]], [g["pattern"] for g in passes[0]["groups"]])
        self.assertEqual([r["target"] for r in ordered_b[1]["rules"]], [r["target"] for r in passes[2]["rules"]])
        self.assertEqual([m["metric"] for m in ordered_b[2]["metrics"]], PRIMARY_METRICS)
        self.assertEqual([m["metric"] for m in ordered_b[3]["metrics"]], [LEGITIMACY_METRIC])

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


# --- atomicity contract tests (D) -------------------------------------------

PASSES_MARKER = "## Pass Execution Atomicity"
INTERNAL_REVERSION_PAYLOAD = "fail_closed_without_partial_state_or_causal_publication"


class AtomicityContractTest(unittest.TestCase):
    """Verify pass_execution_semantics contract in JSON and documentation."""

    maxDiff = None

    def setUp(self) -> None:
        self.decisions = read_decisions()
        self.mvp12 = [
            d for d in self.decisions["decisions"]
            if d["id"] == "MVP-012-national-aggregation"
        ][0]
        self.pes = self.mvp12["resolution"]["pass_execution_semantics"]
        self.md_source = (
            ROOT / "docs" / "aggregation_contract.md"
        ).read_text(encoding="utf-8")

    def test_immutable_snapshot_at_pass_start(self) -> None:
        self.assertEqual(self.pes["input_snapshot"], "immutable_snapshot_at_pass_start")

    def test_planning_before_publication(self) -> None:
        self.assertEqual(self.pes["planning"], "all_outputs_and_causal_contributions_planned_before_publication")

    def test_single_atomic_batch(self) -> None:
        self.assertEqual(self.pes["publication"], "single_atomic_batch")

    def test_next_pass_complete_visibility(self) -> None:
        self.assertEqual(self.pes["next_pass_visibility"], "complete_output_of_previous_pass")

    def test_failure_behavior_fail_closed(self) -> None:
        self.assertEqual(self.pes["failure_behavior"], "fail_closed_without_partial_state_or_causal_publication")

    def test_rule_order_config_order(self) -> None:
        self.assertEqual(self.pes["rule_order"], "config_order")

    def test_dictionary_order_forbidden(self) -> None:
        self.assertTrue(self.pes["dictionary_order_forbidden"])

    def test_duplicate_target_fail_closed(self) -> None:
        self.assertEqual(self.pes["duplicate_target_policy"], "fail_closed_before_publication")

    def test_overlapping_reversion_groups_fail_closed(self) -> None:
        self.assertEqual(self.pes["overlapping_reversion_group_policy"], "fail_closed_before_publication")

    def test_fail_closed_triggers_listed(self) -> None:
        triggers = self.pes["fail_closed_triggers"]
        required = {
            "missing_target",
            "duplicate_target",
            "overlapping_reversion_group",
            "arithmetic_overflow",
            "out_of_range_conversion",
            "invalid_cause_prefix",
            "causal_accounting_mismatch",
            "ledger_rejection",
        }
        self.assertSetEqual(set(triggers), required)

    def test_fail_closed_guarantees_listed(self) -> None:
        guarantees = self.pes["fail_closed_guarantees"]
        required = {
            "zero_partial_state",
            "zero_partial_internals",
            "zero_partial_metrics",
            "zero_partial_causal_contributions",
        }
        self.assertSetEqual(set(guarantees), required)

    def test_internal_reversion_sub_block(self) -> None:
        ir = self.pes["internal_reversion"]
        self.assertTrue(ir["snapshot_rule"], "immutable_at_pass_start")
        self.assertTrue(ir["cross_observation_forbidden"])
        self.assertTrue(ir["plan_before_publication"])
        self.assertTrue(ir["atomic_batch"])
        self.assertTrue(ir["overlapping_group_fail_closed"])

    def test_derived_internals_sub_block(self) -> None:
        di = self.pes["derived_internals"]
        self.assertEqual(di["snapshot_rule"], "post_reversion_immutable")
        self.assertTrue(di["cross_observation_forbidden"])
        self.assertTrue(di["plan_before_publication"])
        self.assertTrue(di["atomic_batch"])
        self.assertTrue(di["duplicate_target_fail_closed"])

    def test_metric_aggregation_sub_block(self) -> None:
        ma = self.pes["metric_aggregation"]
        self.assertTrue(ma["pass_level_snapshot"])
        self.assertTrue(ma["cross_metric_observation_within_pass_forbidden"])
        self.assertTrue(ma["next_pass_sees_complete_state"])
        self.assertTrue(ma["plan_before_publication"])
        self.assertTrue(ma["atomic_batch"])

    def test_aggregation_doc_mentions_atomicity(self) -> None:
        self.assertIn(PASSES_MARKER, self.md_source)
        self.assertIn("Immutable snapshot", self.md_source)
        self.assertIn("Full planning", self.md_source)
        self.assertIn("Single atomic publication", self.md_source)
        self.assertIn("Fail-closed", self.md_source)
        self.assertIn("No cross-observation within pass", self.md_source)


# --- provenance contract tests (E) ------------------------------------------

PROVENANCE_SCOPE = "ephemeral_execution_plan_only"
HIDDEN_INTERNALS_MARKER = "## Hidden Internals and Causality"


class ProvenanceContractTest(unittest.TestCase):
    """Verify hidden_internal_policy provenance fields in JSON and documentation."""

    maxDiff = None

    def setUp(self) -> None:
        self.decisions = read_decisions()
        self.mvp12 = [
            d for d in self.decisions["decisions"]
            if d["id"] == "MVP-012-national-aggregation"
        ][0]
        self.hip = self.mvp12["resolution"]["hidden_internal_policy"]
        self.md_source = (
            ROOT / "docs" / "aggregation_contract.md"
        ).read_text(encoding="utf-8")

    def test_ephemeral_scope(self) -> None:
        self.assertEqual(self.hip["provenance_scope"], PROVENANCE_SCOPE)

    def test_not_serialized(self) -> None:
        self.assertFalse(self.hip["provenance_serialized"])

    def test_not_in_game_state(self) -> None:
        self.assertFalse(self.hip["provenance_stored_in_game_state"])

    def test_not_in_public_ledger(self) -> None:
        self.assertFalse(self.hip["provenance_stored_in_public_ledger"])

    def test_not_in_turn_report(self) -> None:
        self.assertFalse(self.hip["provenance_exposed_in_turn_report"])

    def test_lifetime_current_pass_only(self) -> None:
        self.assertEqual(self.hip["provenance_lifetime"], "current_pass_only")

    def test_public_influence_through_visible_metrics(self) -> None:
        self.assertEqual(
            self.hip["public_influence_through"],
            "SYSTEM:AGG.<metric>.<internal_target>",
        )

    def test_no_double_counting(self) -> None:
        self.assertTrue(self.hip["no_double_counting"])

    def test_internals_remain_hidden(self) -> None:
        self.assertTrue(self.hip["internals_remain_hidden"])
        self.assertTrue(self.hip["no_public_target_catalog_entry"])
        self.assertTrue(self.hip["no_TickCausalBuffer_own_row"])
        self.assertTrue(self.hip["no_top_n_slot_consumption"])
        self.assertTrue(self.hip["no_accidental_TurnReport_exposure"])

    def test_provenance_clarifications(self) -> None:
        pc = self.hip["provenance_clarifications"]
        self.assertEqual(pc["reversion_labels"], "ephemeral_plan_provenance_no_serialization")
        self.assertEqual(pc["derived_labels"], "ephemeral_plan_provenance_no_serialization")
        self.assertTrue(pc["no_game_state_schema_change"])
        self.assertTrue(pc["no_second_ledger"])
        self.assertTrue(pc["no_hidden_ledger_rows"])
        self.assertTrue(pc["no_top_n_appearance"])
        self.assertTrue(pc["no_turn_report_appearance"])
        self.assertEqual(pc["lifetime"], "current_pass_only")
        self.assertEqual(pc["purpose"], "structured_diagnostic_and_traceability_during_planning_validation_only")
        self.assertEqual(
            pc["public_influence_only_through"],
            "SYSTEM:AGG.<metric>.<internal_target>",
        )
        self.assertEqual(
            pc["single_registration_rule"],
            "do_not_register_same_influence_as_REVERSION_DERIVED_AND_AGG_simultaneously",
        )

    def test_aggregation_doc_mentions_provenance(self) -> None:
        self.assertIn(HIDDEN_INTERNALS_MARKER, self.md_source)
        self.assertIn(PROVENANCE_SCOPE, self.md_source)
        self.assertIn("not serialized", self.md_source.lower())
        self.assertIn("Not stored in GameState", self.md_source)
        self.assertIn("Not stored in the public ledger", self.md_source)
        self.assertIn("Not exposed in the turn report", self.md_source)
        self.assertIn("Lifetime is limited to the current pass only", self.md_source)
        self.assertIn("single registration rule", self.md_source.lower())


if __name__ == "__main__":
    unittest.main()
