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

MID_S = 5000
PPM_DEN = 1000000
ALPHA_PPM = 109101
CAP_WEEK = 200


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

    def test_valid_contract_passes(self):
        self.assert_valid(self.valid, "valid contract should pass")

    def test_valid_contract_structure_passes_full_validation(self):
        errors = validate_contract(self.valid)
        self.assertEqual([], errors)


if __name__ == "__main__":
    unittest.main()
