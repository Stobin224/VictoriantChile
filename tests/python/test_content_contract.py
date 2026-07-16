from __future__ import annotations

import json
import shutil
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
CONTENT_DIR = ROOT / "Assets" / "StreamingAssets" / "content"

sys.path.insert(0, str(ROOT))

from scripts.smoke_simulation import smoke
from scripts.content_hash import canonical_json_sha256_file, normalize_json_line_endings
from scripts.validate_content import TargetCatalog, TargetRule, validate_content
from scripts.verify_manifest_hashes import verify
import scripts.recompute_manifest_hashes as recompute_manifest_hashes
import scripts.verify_manifest_hashes as verify_manifest_hashes


def load_json(path: Path):
    return json.loads(path.read_text(encoding="utf-8"))


def write_json(path: Path, data) -> None:
    path.write_text(json.dumps(data, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")


def force_crlf(path: Path) -> None:
    data = path.read_bytes().replace(b"\r\n", b"\n").replace(b"\r", b"\n")
    path.write_bytes(data.replace(b"\n", b"\r\n"))


class ContentFixtureTest(unittest.TestCase):
    def setUp(self) -> None:
        self.tmp = tempfile.TemporaryDirectory()
        self.content_dir = Path(self.tmp.name) / "content"
        shutil.copytree(CONTENT_DIR, self.content_dir)

    def tearDown(self) -> None:
        self.tmp.cleanup()

    def assertValidationFailsWith(self, expected: str) -> None:
        errors = validate_content(self.content_dir)
        self.assertTrue(errors, "validation unexpectedly passed")
        self.assertIn(expected, "\n".join(errors))

    def test_current_baseline_passes(self) -> None:
        self.assertEqual([], verify(CONTENT_DIR))
        self.assertEqual([], validate_content(CONTENT_DIR))
        code, message = smoke(CONTENT_DIR)
        self.assertEqual(0, code, message)

    def test_modified_hash_fails(self) -> None:
        target = self.content_dir / "strings" / "es.json"
        data = load_json(target)
        data["test.hash_changed"] = "changed"
        write_json(target, data)
        errors = verify(self.content_dir)
        self.assertTrue(any("hash mismatch" in error for error in errors))

    def test_lf_and_crlf_have_same_canonical_hash(self) -> None:
        lf = Path(self.tmp.name) / "lf.json"
        crlf = Path(self.tmp.name) / "crlf.json"
        cr = Path(self.tmp.name) / "cr.json"
        lf.write_bytes(b'{\n  "a": 1\n}\n')
        crlf.write_bytes(b'{\r\n  "a": 1\r\n}\r\n')
        cr.write_bytes(b'{\r  "a": 1\r}\r')
        self.assertEqual(canonical_json_sha256_file(lf), canonical_json_sha256_file(crlf))
        self.assertEqual(canonical_json_sha256_file(lf), canonical_json_sha256_file(cr))
        self.assertEqual(normalize_json_line_endings(crlf.read_bytes()), lf.read_bytes())
        self.assertEqual(normalize_json_line_endings(cr.read_bytes()), lf.read_bytes())

    def test_recompute_and_verify_share_hash_function(self) -> None:
        self.assertIs(
            verify_manifest_hashes.canonical_json_sha256_file,
            recompute_manifest_hashes.canonical_json_sha256_file,
        )

    def test_original_manifest_passes_with_crlf_fixture(self) -> None:
        for path in self.content_dir.rglob("*.json"):
            force_crlf(path)
        self.assertEqual([], verify(self.content_dir))

    def test_semantic_change_still_changes_canonical_hash(self) -> None:
        target = self.content_dir / "core" / "movements.json"
        before = canonical_json_sha256_file(target)
        data = load_json(target)
        data["movements"][0]["name"] = "Changed semantic value"
        write_json(target, data)
        after = canonical_json_sha256_file(target)
        self.assertNotEqual(before, after)

    def test_verify_manifest_hashes_does_not_modify_files(self) -> None:
        target = self.content_dir / "core" / "movements.json"
        force_crlf(target)
        before = target.read_bytes()
        self.assertEqual([], verify(self.content_dir))
        self.assertEqual(before, target.read_bytes())

    def test_extra_json_not_declared_fails(self) -> None:
        write_json(self.content_dir / "rules" / "extra.json", {"unused": True})
        errors = verify(self.content_dir)
        self.assertTrue(any("not declared" in error for error in errors))

    def test_manifest_path_escape_fails(self) -> None:
        manifest_path = self.content_dir / "manifest.json"
        manifest = load_json(manifest_path)
        manifest["files"]["../evil.json"] = "sha256:" + ("0" * 64)
        write_json(manifest_path, manifest)
        errors = verify(self.content_dir)
        self.assertTrue(any("escapes content dir" in error for error in errors))

    def test_unknown_target_fails(self) -> None:
        path = self.content_dir / "templates" / "effects.json"
        data = load_json(path)
        data["effects"][0]["mods"][0]["target"] = "metrics.not_declared"
        write_json(path, data)
        self.assertValidationFailsWith("metrics target is not explicitly declared")

    def test_lowercase_target_segments_validate(self) -> None:
        path = self.content_dir / "templates" / "effects.json"
        data = load_json(path)
        data["effects"][0]["mods"][0]["target"] = "regions.metropolitana.support"
        write_json(path, data)
        errors = validate_content(self.content_dir)
        self.assertNotIn("ASCII lowercase snake_case", "\n".join(errors))

    def test_uppercase_target_config_pattern_fails(self) -> None:
        path = self.content_dir / "rules" / "target_config.json"
        data = load_json(path)
        data[0]["pattern"] = "metrics.Legitimacy"
        write_json(path, data)
        self.assertValidationFailsWith("ASCII lowercase snake_case")

    def test_uppercase_concrete_target_reference_fails(self) -> None:
        path = self.content_dir / "templates" / "effects.json"
        data = load_json(path)
        data["effects"][0]["mods"][0]["target"] = "metrics.SOCIAL_TENSION"
        write_json(path, data)
        self.assertValidationFailsWith("ASCII lowercase snake_case")

    def test_uppercase_internal_and_region_targets_fail(self) -> None:
        path = self.content_dir / "rules" / "aggregation_config.json"
        data = load_json(path)
        data["passes"][0]["groups"][0]["pattern"] = "internals.Economy.growth"
        data["passes"][1]["metrics"][0]["components"][0]["target"] = "regions.Metropolitana.support"
        write_json(path, data)
        self.assertValidationFailsWith("ASCII lowercase snake_case")

    def test_unicode_target_segment_fails(self) -> None:
        path = self.content_dir / "templates" / "effects.json"
        data = load_json(path)
        data["effects"][0]["mods"][0]["target"] = "metrics.legitimidad-á"
        write_json(path, data)
        self.assertValidationFailsWith("ASCII lowercase snake_case")

    def test_static_region_fields_are_valid_read_only_selectors(self) -> None:
        path = self.content_dir / "templates" / "events.json"
        data = load_json(path)
        data["events"][0]["vars"]["static_admin"] = {"target": "regions.*.admin_capS"}
        data["events"][0]["vars"]["static_industry"] = {"target": "regions.*.industry_capS"}
        data["events"][0]["vars"]["static_extractive"] = {"target": "regions.*.extractive_capS"}
        data["events"][0]["vars"]["static_social"] = {"target": "regions.*.social_capS"}
        data["events"][0]["vars"]["static_population"] = {"target": "regions.*.populationS"}
        write_json(path, data)
        self.assertEqual([], validate_content(self.content_dir))

    def test_static_region_concrete_selector_is_valid(self) -> None:
        path = self.content_dir / "rules" / "aggregation_config.json"
        data = load_json(path)
        data["passes"][1]["metrics"][0]["components"][0]["target"] = "regions.metropolitana.admin_capS"
        write_json(path, data)
        errors = validate_content(self.content_dir)
        self.assertNotIn("ASCII lowercase snake_case", "\n".join(errors))
        self.assertNotIn("read-only", "\n".join(errors))

    def test_static_region_field_mutation_is_rejected(self) -> None:
        path = self.content_dir / "templates" / "effects.json"
        data = load_json(path)
        data["effects"][0]["mods"][0]["target"] = "regions.metropolitana.admin_capS"
        write_json(path, data)
        self.assertValidationFailsWith("static regional resource is read-only")

    def test_static_region_unknown_camel_case_field_is_rejected(self) -> None:
        path = self.content_dir / "templates" / "events.json"
        data = load_json(path)
        data["events"][0]["vars"]["bad_static"] = {"target": "regions.*.otherFieldS"}
        write_json(path, data)
        self.assertValidationFailsWith("ASCII lowercase snake_case")

    def test_static_region_lowercase_lookalike_is_rejected(self) -> None:
        path = self.content_dir / "templates" / "events.json"
        data = load_json(path)
        data["events"][0]["vars"]["bad_static"] = {"target": "regions.*.admin_caps"}
        write_json(path, data)
        self.assertValidationFailsWith("static regional field must use exact canonical casing")

    def test_static_region_wildcard_still_requires_selector_context(self) -> None:
        path = self.content_dir / "templates" / "effects.json"
        data = load_json(path)
        data["effects"][0]["mods"][0]["target"] = "regions.*.admin_capS"
        write_json(path, data)
        self.assertValidationFailsWith("wildcard is only allowed")

    def test_manifest_bool_version_fails(self) -> None:
        path = self.content_dir / "manifest.json"
        data = load_json(path)
        data["content_pack_version"] = True
        write_json(path, data)
        self.assertValidationFailsWith("content_pack_version must be a positive integer")

    def test_target_config_bool_scale_fails(self) -> None:
        path = self.content_dir / "rules" / "target_config.json"
        data = load_json(path)
        data[0]["scale"] = True
        write_json(path, data)
        self.assertValidationFailsWith("target_config[0].scale: must be a positive integer")

    def test_effect_bool_value_fails(self) -> None:
        path = self.content_dir / "templates" / "effects.json"
        data = load_json(path)
        data["effects"][0]["mods"][0]["valueS"] = True
        write_json(path, data)
        self.assertValidationFailsWith("valueS: must be an integer")

    def test_event_bool_numeric_field_fails(self) -> None:
        path = self.content_dir / "templates" / "events.json"
        data = load_json(path)
        data["events"][0]["base_priority"] = True
        write_json(path, data)
        self.assertValidationFailsWith("base_priority: must be an integer")

    def test_zero_and_one_plain_ints_still_validate(self) -> None:
        path = self.content_dir / "templates" / "events.json"
        data = load_json(path)
        data["events"][0]["base_priority"] = 1
        data["events"][0]["weight"] = 0
        write_json(path, data)
        errors = validate_content(self.content_dir)
        self.assertNotIn("base_priority: must be an integer", "\n".join(errors))
        self.assertNotIn("weight: must be an integer", "\n".join(errors))

    def test_wildcard_mutation_target_fails(self) -> None:
        path = self.content_dir / "templates" / "effects.json"
        data = load_json(path)
        data["effects"][0]["mods"][0]["target"] = "metrics.*"
        write_json(path, data)
        self.assertValidationFailsWith("wildcard is only allowed")

    def test_disallowed_operation_fails(self) -> None:
        path = self.content_dir / "templates" / "effects.json"
        data = load_json(path)
        data["effects"][0]["mods"][0]["target"] = "movements.mov_trabajo_huelgas.direction"
        data["effects"][0]["mods"][0]["op"] = "ADD"
        data["effects"][0]["mods"][0]["valueS"] = 1
        write_json(path, data)
        self.assertValidationFailsWith("is not allowed")

    def test_default_out_of_range_fails(self) -> None:
        path = self.content_dir / "rules" / "target_config.json"
        data = load_json(path)
        for row in data:
            if row["pattern"] == "movements.*.direction":
                row["defaultS"] = 2
                break
        write_json(path, data)
        self.assertValidationFailsWith("defaultS must be within minS/maxS")

    def test_missing_auto_option_fails(self) -> None:
        path = self.content_dir / "templates" / "events.json"
        data = load_json(path)
        data["events"][0]["auto_option_id"] = "missing"
        write_json(path, data)
        self.assertValidationFailsWith("auto_option_id does not exist")

    def test_missing_followup_fails(self) -> None:
        path = self.content_dir / "templates" / "events.json"
        data = load_json(path)
        data["events"][1]["options"][0]["followups"][0]["event_id"] = "evt_missing"
        write_json(path, data)
        self.assertValidationFailsWith("event_id does not exist")

    def test_aggregation_without_passes_fails(self) -> None:
        path = self.content_dir / "rules" / "aggregation_config.json"
        data = load_json(path)
        data.pop("passes")
        write_json(path, data)
        self.assertValidationFailsWith("passes must be a non-empty list")

    def test_smoke_exercises_passes(self) -> None:
        code, message = smoke(self.content_dir)
        self.assertEqual(0, code, message)
        self.assertIn("aggregation_passes=4", message)

        path = self.content_dir / "rules" / "aggregation_config.json"
        data = load_json(path)
        data["passes"] = []
        write_json(path, data)
        code, message = smoke(self.content_dir)
        self.assertNotEqual(0, code)
        self.assertIn("passes", message)


class RunChecksTest(unittest.TestCase):
    def make_repo_fixture(self) -> tempfile.TemporaryDirectory:
        tmp = tempfile.TemporaryDirectory()
        repo = Path(tmp.name) / "repo"
        shutil.copytree(ROOT / "scripts", repo / "scripts")
        shutil.copytree(CONTENT_DIR, repo / "Assets" / "StreamingAssets" / "content")
        (repo / "tests" / "python").mkdir(parents=True)
        (repo / "tests" / "python" / "test_placeholder.py").write_text(
            "import unittest\n\nclass Placeholder(unittest.TestCase):\n    def test_ok(self):\n        self.assertTrue(True)\n",
            encoding="utf-8",
        )
        return tmp

    def test_run_checks_nonzero_when_subcheck_fails(self) -> None:
        tmp = self.make_repo_fixture()
        try:
            repo = Path(tmp.name) / "repo"
            write_json(repo / "Assets" / "StreamingAssets" / "content" / "rules" / "extra.json", {"bad": True})
            result = subprocess.run(
                [sys.executable, str(repo / "scripts" / "run_checks.py")],
                cwd=Path(tmp.name),
                capture_output=True,
                text=True,
                shell=False,
            )
            self.assertNotEqual(0, result.returncode)
            self.assertIn("verify_manifest_hashes: FAIL", result.stdout)
        finally:
            tmp.cleanup()

    def test_run_checks_json_output_contains_required_states(self) -> None:
        tmp = self.make_repo_fixture()
        try:
            repo = Path(tmp.name) / "repo"
            output = Path(tmp.name) / "nested" / "checks" / "checks.json"
            result = subprocess.run(
                [sys.executable, str(repo / "scripts" / "run_checks.py"), "--json-output", str(output)],
                cwd=Path(tmp.name) / "repo" / "Assets",
                capture_output=True,
                text=True,
                shell=False,
            )
            self.assertEqual(0, result.returncode, result.stdout + result.stderr)
            data = load_json(output)
            self.assertEqual("PASS", data["overall_status"])
            self.assertIsInstance(data["started_at"], str)
            self.assertIsInstance(data["duration_ms"], int)
            self.assertTrue(data["steps"])
            for step in data["steps"]:
                self.assertIn("name", step)
                self.assertIn("status", step)
                self.assertIn("exit_code", step)
                self.assertIn("duration_ms", step)
                self.assertIn("stdout", step)
                self.assertIn("stderr", step)
            skipped = {item["name"]: item["reason"] for item in data["skipped_checks"]}
            self.assertEqual("--base-ref was not provided", skipped["check_manifest_bump"])
            self.assertEqual("--include-dotnet was not provided", skipped["dotnet_test"])
            self.assertEqual("--include-unity-editmode was not provided", skipped["unity_editmode"])
            self.assertEqual([], data["errors"])
        finally:
            tmp.cleanup()

    def test_run_checks_json_failure_contains_diagnostics(self) -> None:
        tmp = self.make_repo_fixture()
        try:
            repo = Path(tmp.name) / "repo"
            write_json(repo / "Assets" / "StreamingAssets" / "content" / "rules" / "extra.json", {"bad": True})
            output = Path(tmp.name) / "checks.json"
            result = subprocess.run(
                [sys.executable, str(repo / "scripts" / "run_checks.py"), "--json-output", str(output)],
                cwd=repo,
                capture_output=True,
                text=True,
                shell=False,
            )
            self.assertNotEqual(0, result.returncode)
            data = load_json(output)
            self.assertEqual("FAIL", data["overall_status"])
            failed = {step["name"]: step for step in data["steps"] if step["status"] == "FAIL"}
            self.assertIn("verify_manifest_hashes", failed)
            self.assertIn("not declared", failed["verify_manifest_hashes"]["stdout"])
            self.assertTrue(data["errors"])
            self.assertIn("verify_manifest_hashes: exited with code", "\n".join(data["errors"]))
        finally:
            tmp.cleanup()


class TargetCatalogParityTest(unittest.TestCase):
    def make_rule(self, pattern: str, index: int) -> TargetRule:
        return TargetRule(pattern, 100, -10_000, 10_000, 0, frozenset({"ADD", "MUL", "SET"}), index)

    def make_catalog(self, patterns: list[str]) -> TargetCatalog:
        return TargetCatalog(
            rules=[self.make_rule(pattern, i) for i, pattern in enumerate(patterns)],
            metric_ids={"legitimacy", "economy", "security"},
            region_ids={"all", "alpha"},
            ig_ids={"ig_alpha"},
            movement_ids={"mov_alpha"},
        )

    def test_exact_pattern_beats_wildcard(self) -> None:
        catalog = self.make_catalog(["metrics.*", "metrics.legitimacy"])
        self.assertEqual("metrics.legitimacy", catalog.resolve("metrics.legitimacy").pattern)

    def test_more_literal_segments_beat_generic(self) -> None:
        catalog = self.make_catalog(["internals.*.*", "internals.economy.*"])
        self.assertEqual("internals.economy.*", catalog.resolve("internals.economy.inflation").pattern)

    def test_longer_pattern_text_breaks_literal_count_tie(self) -> None:
        catalog = self.make_catalog(["regions.all.*", "regions.*.support"])
        first = catalog.rules[0]
        second = catalog.rules[1]
        self.assertEqual(2, sum(1 for part in first.pattern.split(".") if part != "*"))
        self.assertEqual(2, sum(1 for part in second.pattern.split(".") if part != "*"))
        self.assertGreater(len(second.pattern), len(first.pattern))
        self.assertEqual("regions.*.support", catalog.resolve("regions.all.support").pattern)

    def test_load_order_wins_complete_tie(self) -> None:
        catalog = self.make_catalog(["regions.*.value", "regions.alpha.*"])
        first = catalog.rules[0]
        second = catalog.rules[1]
        self.assertEqual(len(first.pattern), len(second.pattern))
        self.assertEqual(
            sum(1 for part in first.pattern.split(".") if part != "*"),
            sum(1 for part in second.pattern.split(".") if part != "*"),
        )
        self.assertEqual("regions.*.value", catalog.resolve("regions.alpha.value").pattern)

    def test_repeated_resolution_is_stable(self) -> None:
        catalog = self.make_catalog(["metrics.*", "metrics.legitimacy"])
        first = catalog.resolve("metrics.legitimacy")
        second = catalog.resolve("metrics.legitimacy")
        self.assertIs(first, second)


class WorkingTreeManifestBumpTest(unittest.TestCase):
    def setUp(self) -> None:
        self.tmp = tempfile.TemporaryDirectory()
        self.repo = Path(self.tmp.name) / "repo"
        content = self.repo / "Assets" / "StreamingAssets" / "content"
        content.mkdir(parents=True)
        write_json(content / "data.json", {"value": 1})
        write_json(
            content / "manifest.json",
            {
                "content_pack_id": "test",
                "content_pack_version": 2,
                "content_schema_version": 2,
                "min_game_schema_version": 1,
                "files": {"data.json": "sha256:" + ("0" * 64)},
            },
        )
        subprocess.run(["git", "init"], cwd=self.repo, check=True, capture_output=True, text=True)
        subprocess.run(["git", "config", "user.email", "test@example.com"], cwd=self.repo, check=True)
        subprocess.run(["git", "config", "user.name", "Test"], cwd=self.repo, check=True)
        subprocess.run(["git", "add", "."], cwd=self.repo, check=True)
        subprocess.run(["git", "commit", "-m", "base"], cwd=self.repo, check=True, capture_output=True, text=True)

    def tearDown(self) -> None:
        self.tmp.cleanup()

    def run_check(self, *args: str) -> subprocess.CompletedProcess[str]:
        return subprocess.run(
            [sys.executable, str(ROOT / "scripts" / "check_manifest_bump.py"), *args],
            cwd=self.repo,
            capture_output=True,
            text=True,
            shell=False,
        )

    def test_head_vs_head_does_not_validate_local_changes(self) -> None:
        write_json(self.repo / "Assets" / "StreamingAssets" / "content" / "data.json", {"value": 2})
        result = self.run_check("--base", "HEAD", "--head", "HEAD")
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)
        self.assertIn("no hubo cambios", result.stdout)

    def test_missing_base_ref_has_clear_error(self) -> None:
        result = self.run_check("--base", "refs/does/not/exist", "--working-tree")
        self.assertNotEqual(0, result.returncode)
        self.assertIn("base ref not found", result.stdout + result.stderr)

    def test_working_tree_detects_local_content_change(self) -> None:
        write_json(self.repo / "Assets" / "StreamingAssets" / "content" / "data.json", {"value": 2})
        result = self.run_check("--base", "HEAD", "--working-tree")
        self.assertNotEqual(0, result.returncode)
        self.assertIn("sin actualizar manifest", result.stdout)

    def test_working_tree_detects_staged_content_change(self) -> None:
        write_json(self.repo / "Assets" / "StreamingAssets" / "content" / "data.json", {"value": 2})
        subprocess.run(["git", "add", "Assets/StreamingAssets/content/data.json"], cwd=self.repo, check=True)
        result = self.run_check("--base", "HEAD", "--working-tree")
        self.assertNotEqual(0, result.returncode)
        self.assertIn("sin actualizar manifest", result.stdout)

    def test_working_tree_detects_untracked_content_change(self) -> None:
        write_json(self.repo / "Assets" / "StreamingAssets" / "content" / "new.json", {"value": 2})
        result = self.run_check("--base", "HEAD", "--working-tree")
        self.assertNotEqual(0, result.returncode)
        self.assertIn("sin actualizar manifest", result.stdout)

    def test_working_tree_detects_deleted_content_file(self) -> None:
        (self.repo / "Assets" / "StreamingAssets" / "content" / "data.json").unlink()
        result = self.run_check("--base", "HEAD", "--working-tree")
        self.assertNotEqual(0, result.returncode)
        self.assertIn("sin actualizar manifest", result.stdout)

    def test_working_tree_requires_manifest_bump_for_content_change(self) -> None:
        write_json(self.repo / "Assets" / "StreamingAssets" / "content" / "data.json", {"value": 2})
        manifest_path = self.repo / "Assets" / "StreamingAssets" / "content" / "manifest.json"
        manifest = load_json(manifest_path)
        manifest["files"]["data.json"] = "sha256:" + ("1" * 64)
        write_json(manifest_path, manifest)
        result = self.run_check("--base", "HEAD", "--working-tree")
        self.assertNotEqual(0, result.returncode)
        self.assertIn("content_pack_version no increment", result.stdout)

        manifest["content_pack_version"] = 3
        write_json(manifest_path, manifest)
        result = self.run_check("--base", "HEAD", "--working-tree")
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)

    def test_pack_version_decrease_fails(self) -> None:
        manifest_path = self.repo / "Assets" / "StreamingAssets" / "content" / "manifest.json"
        manifest = load_json(manifest_path)
        manifest["content_pack_version"] = 1
        write_json(manifest_path, manifest)
        result = self.run_check("--base", "HEAD", "--working-tree")
        self.assertNotEqual(0, result.returncode)
        self.assertIn("content_pack_version no puede disminuir", result.stdout)

    def test_schema_version_decrease_fails(self) -> None:
        manifest_path = self.repo / "Assets" / "StreamingAssets" / "content" / "manifest.json"
        manifest = load_json(manifest_path)
        manifest["content_schema_version"] = 1
        write_json(manifest_path, manifest)
        result = self.run_check("--base", "HEAD", "--working-tree")
        self.assertNotEqual(0, result.returncode)
        self.assertIn("content_schema_version no puede disminuir", result.stdout)

    def test_content_change_pack_jump_fails(self) -> None:
        write_json(self.repo / "Assets" / "StreamingAssets" / "content" / "data.json", {"value": 2})
        manifest_path = self.repo / "Assets" / "StreamingAssets" / "content" / "manifest.json"
        manifest = load_json(manifest_path)
        manifest["files"]["data.json"] = "sha256:" + ("1" * 64)
        manifest["content_pack_version"] = 4
        write_json(manifest_path, manifest)
        result = self.run_check("--base", "HEAD", "--working-tree")
        self.assertNotEqual(0, result.returncode)
        self.assertIn("content_pack_version no increment", result.stdout)
        self.assertIn("old=2, new=4", result.stdout)

    def test_content_change_exact_pack_bump_passes(self) -> None:
        write_json(self.repo / "Assets" / "StreamingAssets" / "content" / "data.json", {"value": 2})
        manifest_path = self.repo / "Assets" / "StreamingAssets" / "content" / "manifest.json"
        manifest = load_json(manifest_path)
        manifest["files"]["data.json"] = "sha256:" + ("1" * 64)
        manifest["content_pack_version"] = 3
        write_json(manifest_path, manifest)
        result = self.run_check("--base", "HEAD", "--working-tree")
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)

    def test_schema_exact_bump_requires_pack_exact_bump(self) -> None:
        manifest_path = self.repo / "Assets" / "StreamingAssets" / "content" / "manifest.json"
        manifest = load_json(manifest_path)
        manifest["content_schema_version"] = 3
        manifest["content_pack_version"] = 3
        write_json(manifest_path, manifest)
        result = self.run_check("--base", "HEAD", "--working-tree")
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)

    def test_schema_jump_fails(self) -> None:
        manifest_path = self.repo / "Assets" / "StreamingAssets" / "content" / "manifest.json"
        manifest = load_json(manifest_path)
        manifest["content_schema_version"] = 4
        manifest["content_pack_version"] = 3
        write_json(manifest_path, manifest)
        result = self.run_check("--base", "HEAD", "--working-tree")
        self.assertNotEqual(0, result.returncode)
        self.assertIn("content_schema_version debe incrementar exactamente en +1", result.stdout)

    def test_boolean_version_fails(self) -> None:
        manifest_path = self.repo / "Assets" / "StreamingAssets" / "content" / "manifest.json"
        manifest = load_json(manifest_path)
        manifest["content_pack_version"] = True
        write_json(manifest_path, manifest)
        result = self.run_check("--base", "HEAD", "--working-tree")
        self.assertNotEqual(0, result.returncode)
        self.assertIn("content_pack_version debe ser un entero positivo", result.stdout)

    def test_manifest_only_without_version_regression_passes(self) -> None:
        manifest_path = self.repo / "Assets" / "StreamingAssets" / "content" / "manifest.json"
        manifest = load_json(manifest_path)
        manifest["metadata_note"] = "manifest-only correction"
        write_json(manifest_path, manifest)
        result = self.run_check("--base", "HEAD", "--working-tree")
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)

    def test_manifest_only_pack_jump_fails(self) -> None:
        manifest_path = self.repo / "Assets" / "StreamingAssets" / "content" / "manifest.json"
        manifest = load_json(manifest_path)
        manifest["metadata_note"] = "manifest-only correction"
        manifest["content_pack_version"] = 4
        write_json(manifest_path, manifest)
        result = self.run_check("--base", "HEAD", "--working-tree")
        self.assertNotEqual(0, result.returncode)
        self.assertIn("cambio solo de manifest no puede saltar content_pack_version", result.stdout)

    def test_working_tree_ignores_non_content_changes(self) -> None:
        (self.repo / "README.md").write_text("outside content\n", encoding="utf-8")
        result = self.run_check("--base", "HEAD", "--working-tree")
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)
        self.assertIn("no hubo cambios", result.stdout)


if __name__ == "__main__":
    unittest.main()
