from __future__ import annotations

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
            errors.append(
                f"Missing top-level key(s): {sorted(missing)}"
            )
        if extra:
            errors.append(
                f"Unexpected top-level key(s): {sorted(extra)}"
            )

    actual_order_keys = set(order.keys())
    if actual_order_keys != EXPECTED_CANONICAL_ORDER_KEYS:
        missing = EXPECTED_CANONICAL_ORDER_KEYS - actual_order_keys
        extra = actual_order_keys - EXPECTED_CANONICAL_ORDER_KEYS
        if missing:
            errors.append(
                f"Missing canonical_region_order key(s): {sorted(missing)}"
            )
        if extra:
            errors.append(
                f"Unexpected canonical_region_order key(s): {sorted(extra)}"
            )

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
                errors.append(
                    f"region {rid} missing static resource {field}"
                )
            elif r[field] != 5000:
                errors.append(
                    f"region {rid} static resource {field} = {r[field]}, "
                    f"expected 5000"
                )

    return errors


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

    def test_every_region_has_all_five_static_resources_at_5000(self):
        cp_regions = self.regions_data["regions"]
        for r in cp_regions:
            for field in EXPECTED_STATIC_RESOURCE_NAMES:
                self.assertIn(field, r, f"{r['id']} missing {field}")
                self.assertEqual(
                    5000, r[field], f"{r['id']}.{field} != 5000"
                )

    def test_content_pack_declaration_matches_contract_order(self):
        cp_ids = [r["id"] for r in self.regions_data["regions"]]
        contract_ids = self.contract["canonical_region_order"][
            "ordered_region_ids"
        ]
        self.assertEqual(contract_ids, cp_ids)

    def test_alphabetical_order_does_not_match_canonical(self):
        contract_ids = self.contract["canonical_region_order"][
            "ordered_region_ids"
        ]
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
        cls.contract = extract_canonical_block(
            cls.root / "docs" / "territory_contract.md"
        )
        cls.regions_data = load_json(
            cls.root
            / "Assets"
            / "StreamingAssets"
            / "content"
            / "core"
            / "regions.json"
        )

    def test_content_pack_region_order_matches_contract(self):
        errors = validate_content_pack_against_contract(
            self.contract, self.regions_data
        )
        self.assertEqual([], errors, "\n".join(errors))


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
        self.assertLess(
            begin_pos, end_pos,
            "BEGIN_MARKER must appear before END_MARKER",
        )

    def test_json_block_is_between_markers(self):
        text = self.contract_path.read_text(encoding="utf-8")
        begin_pos = text.index(BEGIN_MARKER)
        end_pos = text.index(END_MARKER)
        json_start = text.index("{", begin_pos)
        json_end = text.rindex("}", 0, end_pos) + 1
        self.assertGreater(json_start, begin_pos)
        self.assertLess(json_end, end_pos)
        parsed = json.loads(text[json_start:json_end])
        self.assertIn("canonical_region_order", parsed)
        self.assertIn("regional_dynamic_targets", parsed)
        self.assertIn("static_regional_resources", parsed)

    def test_contract_parses_as_valid_json(self):
        text = self.contract_path.read_text(encoding="utf-8")
        begin = text.find(BEGIN_MARKER)
        end = text.find(END_MARKER)
        json_start = text.index("{", begin)
        json_end = text.rindex("}", 0, end) + 1
        parsed = json.loads(text[json_start:json_end])
        self.assertIn("canonical_region_order", parsed)
        self.assertIn("regional_dynamic_targets", parsed)
        self.assertIn("static_regional_resources", parsed)

    def test_contract_has_no_trailing_whitespace_lines(self):
        for i, line in enumerate(
            self.contract_path.read_text(encoding="utf-8").split("\n"), 1
        ):
            self.assertEqual(
                line,
                line.rstrip(),
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
        }

    def assert_invalid(self, contract: dict, description: str):
        errors = validate_contract(contract)
        self.assertTrue(
            errors,
            f"Mutation '{description}' should have been rejected",
        )

    def assert_valid(self, contract: dict, description: str):
        errors = validate_contract(contract)
        self.assertEqual(
            [], errors, f"Expected valid contract for '{description}': "
            f"{' '.join(errors)}"
        )

    def test_swap_tarapaca_antofagasta(self):
        ids = self.valid["canonical_region_order"]["ordered_region_ids"]
        i1 = ids.index("tarapaca")
        i2 = ids.index("antofagasta")
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
        ids = self.valid["canonical_region_order"]["ordered_region_ids"]
        ids.pop()
        self.valid["canonical_region_order"]["region_count"] = 15
        self.assert_invalid(self.valid, "one region omitted")

    def test_unknown_id(self):
        ids = self.valid["canonical_region_order"]["ordered_region_ids"]
        ids[0] = "unknown_region"
        self.assert_invalid(self.valid, "unknown region ID")

    def test_weight_ppm_each_62499(self):
        self.valid["canonical_region_order"]["weight_ppm_each"] = 62499
        self.assert_invalid(self.valid, "weight_ppm_each = 62499")

    def test_one_region_with_different_weight(self):
        self.valid["canonical_region_order"]["weight_ppm_each"] = 1
        self.assert_invalid(self.valid, "one region has weight_ppm != 62500")

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
        self.valid["canonical_region_order"][
            "authority"
        ] = "game_state_alphabetic"
        self.assert_invalid(self.valid, "authority is wrong")

    def test_source_path_wrong(self):
        self.valid["canonical_region_order"][
            "source_path"
        ] = "Assets/StreamingAssets/content/rules/target_config.json"
        self.assert_invalid(self.valid, "source_path is wrong")

    def test_forbidden_source_removed(self):
        self.valid["canonical_region_order"][
            "forbidden_order_sources"
        ] = EXPECTED_FORBIDDEN_SOURCES[:-1]
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

    def test_valid_contract_passes(self):
        self.assert_valid(self.valid, "valid contract should pass")

    def test_valid_contract_structure_passes_full_validation(self):
        errors = validate_contract(self.valid)
        self.assertEqual([], errors)


if __name__ == "__main__":
    unittest.main()
