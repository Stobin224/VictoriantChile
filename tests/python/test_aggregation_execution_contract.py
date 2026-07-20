from __future__ import annotations

import json
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
VECTORS_PATH = ROOT / "tests" / "aggregation" / "aggregation_execution_v1_vectors.json"
AGG_CONFIG_PATH = (
    ROOT / "Assets" / "StreamingAssets" / "content" / "rules" / "aggregation_config.json"
)
PPM = 1_000_000
MID_S = 5000


def round_half_away_from_zero(numerator: int, denominator: int) -> int:
    sign = -1 if (numerator < 0) ^ (denominator < 0) else 1
    q, r = divmod(abs(numerator), abs(denominator))
    if r * 2 >= abs(denominator):
        q += 1
    return sign * q


def clamp(value: int, minimum: int = 0, maximum: int = 10000) -> int:
    return minimum if value < minimum else maximum if value > maximum else value


def compute_reversion(current_s: int, alpha_ppm: int) -> dict:
    distance = MID_S - current_s
    delta = round_half_away_from_zero(distance * alpha_ppm, PPM)
    return {
        "distanceS": distance,
        "rounded_deltaS": delta,
        "finalS": clamp(current_s + delta),
    }


def compute_metric(current_metric_s: int, components: list[dict], alpha_ppm: int, cap_per_week_s: int) -> dict:
    numerator = sum(c["weight_ppm"] * (c["componentS"] - MID_S) for c in components)
    weighted_offset = round_half_away_from_zero(numerator, PPM)
    target_s = clamp(MID_S + weighted_offset)
    elastic_delta = round_half_away_from_zero((target_s - current_metric_s) * alpha_ppm, PPM)
    capped_delta = clamp(elastic_delta, -cap_per_week_s, cap_per_week_s)
    pre_final = current_metric_s + capped_delta
    final_s = clamp(pre_final)
    return {
        "weighted_offsetS": weighted_offset,
        "targetS": target_s,
        "elastic_deltaS": elastic_delta,
        "capped_deltaS": capped_delta,
        "pre_finalS": pre_final,
        "finalS": final_s,
        "delta_totalS": final_s - current_metric_s,
    }


def prefix_values(case: dict) -> list[int]:
    values: list[int] = []
    components = case["components"]
    for count in range(len(components) + 1):
        vector = []
        for index, component in enumerate(components):
            value = component["componentS"] if index < count else MID_S
            vector.append(
                {
                    "target": component["target"],
                    "componentS": value,
                    "weight_ppm": component["weight_ppm"],
                }
            )
        values.append(
            compute_metric(
                case["current_metricS"],
                vector,
                case["alpha_ppm"],
                case["cap_per_weekS"],
            )["finalS"]
        )
    return values


def contributions(case: dict) -> list[dict]:
    values = prefix_values(case)
    metric = case["metric"]
    result: list[dict] = []
    base_delta = values[0] - case["current_metricS"]
    if base_delta:
        result.append({"cause": f"SYSTEM:AGG.{metric}", "deltaS": base_delta})
    for index, component in enumerate(case["components"]):
        delta = values[index + 1] - values[index]
        if delta:
            result.append({"cause": f"SYSTEM:AGG.{metric}.{component['target']}", "deltaS": delta})
    return result


def load_vectors() -> dict:
    return json.loads(VECTORS_PATH.read_text(encoding="utf-8"))


def load_config() -> dict:
    return json.loads(AGG_CONFIG_PATH.read_text(encoding="utf-8"))


def value_map(items: list[dict]) -> dict[str, int]:
    return {item["target"]: item["valueS"] for item in items}


def contribution_map(items: list[dict]) -> dict[str, list[dict]]:
    return {item["target"]: item["contributions"] for item in items}


class AggregationExecutionContractTest(unittest.TestCase):
    maxDiff = None

    def test_reversion_vectors_are_independent(self) -> None:
        vectors = load_vectors()
        self.assertEqual(vectors["numeric_domain"]["midS"], MID_S)
        for case in vectors["reversion_cases"]:
            self.assertEqual(compute_reversion(case["currentS"], case["alpha_ppm"]), case["expected"], case["id"])

    def test_derived_vectors_are_independent(self) -> None:
        for case in load_vectors()["derived_cases"]:
            if case["kind"] == "AVG":
                self.assertEqual(round_half_away_from_zero(sum(case["inputs"]), len(case["inputs"])), case["expected"])
            else:
                self.assertEqual(case["input"], case["expected"])

    def test_metric_vectors_are_independent(self) -> None:
        for case in load_vectors()["metric_cases"]:
            actual = compute_metric(case["current_metricS"], case["components"], case["alpha_ppm"], case["cap_per_weekS"])
            for key in ("weighted_offsetS", "targetS", "elastic_deltaS", "capped_deltaS", "pre_finalS", "finalS", "delta_totalS"):
                self.assertEqual(actual[key], case["expected"][key], case["id"])
            self.assertEqual(prefix_values(case), case["expected"]["F_values"], case["id"])
            self.assertEqual(contributions(case), case["expected"]["contributions"], case["id"])
            self.assertEqual(
                case["expected"]["delta_totalS"],
                sum(item["deltaS"] for item in case["expected"]["contributions"]),
                case["id"],
            )

    def test_end_to_end_fixture_matches_config_semantics(self) -> None:
        vectors = load_vectors()
        config = load_config()
        case = vectors["end_to_end_case"]
        internals = {target: 5000 for target in all_internal_targets()}
        internals.update(value_map(case["initial_internal_overrides"]))
        metrics = value_map(case["initial_metric_values"])

        for group in next(p for p in config["passes"] if p["type"] == "INTERNAL_REVERSION")["groups"]:
            prefix = group["pattern"][:-1]
            for target in all_internal_targets():
                if target.startswith(prefix) and target not in {"internals.legitimacy.performance", "internals.legitimacy.social_tension_load"}:
                    internals[target] = compute_reversion(internals[target], group["alpha_ppm"])["finalS"]

        internals["internals.legitimacy.performance"] = round_half_away_from_zero(
            metrics["metrics.economy"] + metrics["metrics.security"] + metrics["metrics.governability"],
            3,
        )
        internals["internals.legitimacy.social_tension_load"] = metrics["metrics.social_tension"]

        metric_passes = [p for p in config["passes"] if p["type"] == "METRIC_AGGREGATION"]
        for metric_config in metric_passes[0]["metrics"] + metric_passes[1]["metrics"]:
            metric = metric_config["metric"]
            metric_case = {
                "metric": metric,
                "current_metricS": metrics[metric],
                "alpha_ppm": metric_config["alpha_ppm"],
                "cap_per_weekS": metric_config["cap_per_weekS"],
                "components": [
                    {
                        "target": component["target"],
                        "componentS": internals[component["target"]],
                        "weight_ppm": component["weight_ppm"],
                    }
                    for component in metric_config["components"]
                ],
            }
            metrics[metric] = compute_metric(
                metric_case["current_metricS"],
                metric_case["components"],
                metric_case["alpha_ppm"],
                metric_case["cap_per_weekS"],
            )["finalS"]
            expected_contribs = contribution_map(case["expected_metric_contributions"]).get(metric, [])
            self.assertEqual(contributions(metric_case), expected_contribs, metric)

        self.assertEqual(metrics, value_map(case["expected_final_metric_values"]))
        for target, expected in value_map(case["expected_final_internal_overrides"]).items():
            self.assertEqual(internals[target], expected, target)


def all_internal_targets() -> list[str]:
    return [
        "internals.economy.growth",
        "internals.economy.unemployment",
        "internals.economy.inflation",
        "internals.economy.fiscal_stability",
        "internals.security.police_capacity",
        "internals.security.crime_rate",
        "internals.security.violent_crime",
        "internals.security.organized_crime",
        "internals.tension.cost_of_living",
        "internals.tension.polarization",
        "internals.tension.protest_activity",
        "internals.tension.institutional_trust",
        "internals.agenda.media_heat",
        "internals.agenda.policy_conflict",
        "internals.agenda.movement_salience",
        "internals.info.intel_capacity",
        "internals.info.media_noise",
        "internals.info.institutional_access",
        "internals.gov.bureaucracy_capacity",
        "internals.gov.budget_flexibility",
        "internals.gov.execution_focus",
        "internals.gov.legal_friction",
        "internals.leg.coalition_strength",
        "internals.leg.party_discipline",
        "internals.leg.opposition_obstruction",
        "internals.leg.senate_inertia",
        "internals.party.field_ops",
        "internals.party.funding",
        "internals.party.cadre_quality",
        "internals.party.internal_scandal",
        "internals.cohesion.factionalism",
        "internals.cohesion.leadership_unity",
        "internals.cohesion.discipline_culture",
        "internals.cohesion.ambition_rivalries",
        "internals.legitimacy.performance",
        "internals.legitimacy.integrity",
        "internals.legitimacy.scandal_pressure",
        "internals.legitimacy.social_tension_load",
    ]


if __name__ == "__main__":
    unittest.main()
