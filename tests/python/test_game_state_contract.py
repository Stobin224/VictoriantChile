from __future__ import annotations

import json
import re
import subprocess
import unittest
import xml.etree.ElementTree as ET
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
ASSETS = ROOT / "Assets"
CORE = ASSETS / "VictoriantChile" / "Simulation" / "Core"
CONTENT = ASSETS / "VictoriantChile" / "Content"
EDITMODE = ASSETS / "VictoriantChile" / "Tests" / "EditMode"


def load_json(path: Path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def git_text(*args: str) -> str:
    result = subprocess.run(
        ["git", *args],
        cwd=ROOT,
        capture_output=True,
        text=True,
        shell=False,
        check=True,
    )
    return result.stdout


def meta_guid(path: Path) -> str:
    match = re.search(r"^guid:\s*([0-9a-f]{32})$", path.read_text(encoding="utf-8"), re.MULTILINE)
    if not match:
        raise AssertionError(f"Missing GUID in {path}")
    return match.group(1)


class GameStateContractTest(unittest.TestCase):
    def test_new_game_state_assets_have_meta_files(self) -> None:
        expected = [
            CORE / "State",
            CORE / "State" / "CloutNormalizer.cs",
            CORE / "State" / "ContentFileIdentity.cs",
            CORE / "State" / "GameState.cs",
            CORE / "State" / "GameStateContentMetadata.cs",
            CORE / "State" / "InitialTargetRegistry.cs",
            CORE / "State" / "InterestGroupState.cs",
            CORE / "State" / "InternalDomainState.cs",
            CORE / "State" / "MetricState.cs",
            CORE / "State" / "MovementState.cs",
            CORE / "State" / "RegionState.cs",
            CORE / "State" / "StateCollection.cs",
            CONTENT / "State",
            CONTENT / "State" / "GameStateFactory.cs",
            CONTENT / "State" / "StateInitializationDiagnostics.cs",
            EDITMODE / "CloutNormalizerTests.cs",
            EDITMODE / "GameStateFactoryTests.cs",
        ]

        for path in expected:
            self.assertTrue(path.exists(), path)
            self.assertTrue(path.with_name(path.name + ".meta").exists(), f"missing meta for {path}")

    def test_no_duplicate_asset_guids(self) -> None:
        seen: dict[str, Path] = {}
        for meta in ASSETS.rglob("*.meta"):
            guid = meta_guid(meta)
            self.assertNotIn(guid, seen, f"duplicate GUID {guid}: {seen.get(guid)} and {meta}")
            seen[guid] = meta

    def test_asmdef_dependency_direction_is_preserved(self) -> None:
        core_asmdef = load_json(CORE / "VictoriantChile.Simulation.Core.asmdef")
        content_asmdef = load_json(CONTENT / "VictoriantChile.Content.asmdef")
        editmode_asmdef = load_json(EDITMODE / "VictoriantChile.Simulation.Tests.EditMode.asmdef")
        core_guid = meta_guid(CORE / "VictoriantChile.Simulation.Core.asmdef.meta")
        content_guid = meta_guid(CONTENT / "VictoriantChile.Content.asmdef.meta")

        self.assertNotIn("VictoriantChile.Content", "\n".join(core_asmdef.get("references", [])))
        self.assertIn(f"GUID:{core_guid}", content_asmdef["references"])
        self.assertIn(f"GUID:{content_guid}", editmode_asmdef["references"])

    def test_no_new_asmdef_was_added(self) -> None:
        asmdefs = {path.relative_to(ASSETS).as_posix() for path in ASSETS.rglob("*.asmdef")}
        self.assertEqual(
            {
                "VictoriantChile/Content/VictoriantChile.Content.asmdef",
                "VictoriantChile/Simulation/Core/VictoriantChile.Simulation.Core.asmdef",
                "VictoriantChile/Tests/EditMode/VictoriantChile.Simulation.Tests.EditMode.asmdef",
            },
            asmdefs,
        )

    def test_no_package_change_after_content_loader_baseline(self) -> None:
        current_manifest = load_json(ROOT / "Packages" / "manifest.json")
        current_lock = load_json(ROOT / "Packages" / "packages-lock.json")
        base_manifest = json.loads(git_text("show", "origin/main:Packages/manifest.json"))
        base_lock = json.loads(git_text("show", "origin/main:Packages/packages-lock.json"))

        self.assertEqual(base_manifest["dependencies"], current_manifest["dependencies"])
        self.assertEqual(base_lock["dependencies"].keys(), current_lock["dependencies"].keys())
        self.assertEqual("3.2.2", current_manifest["dependencies"]["com.unity.nuget.newtonsoft-json"])
        self.assertEqual("3.2.2", current_lock["dependencies"]["com.unity.nuget.newtonsoft-json"]["version"])

    def test_no_csproj_files_are_tracked_or_untracked(self) -> None:
        self.assertEqual([], git_text("ls-files", "*.csproj").splitlines())
        self.assertEqual([], git_text("ls-files", "--others", "--exclude-standard", "*.csproj").splitlines())

    def test_solution_contains_canonical_project_set(self) -> None:
        tree = ET.parse(ROOT / "Victoriant Chile.slnx")
        projects = [element.attrib["Path"] for element in tree.getroot().findall("Project")]
        self.assertEqual(
            {
                "VictoriantChile.Content.csproj",
                "VictoriantChile.Simulation.Core.csproj",
                "VictoriantChile.Simulation.Tests.EditMode.csproj",
            },
            set(projects),
        )
        self.assertEqual(len(projects), len(set(projects)))

    def test_core_has_no_forbidden_runtime_dependencies_or_nondeterminism(self) -> None:
        forbidden = re.compile(
            r"UnityEngine|UnityEditor|Newtonsoft|System\.IO|System\.Text\.Json|System\.Security\.Cryptography|"
            r"\bfloat\b|\bdouble\b|\bdecimal\b|\bRandom\b|DateTime\.Now|DateTime\.UtcNow|Guid\.NewGuid|\bunsafe\b"
        )
        for path in CORE.rglob("*.cs"):
            self.assertIsNone(forbidden.search(path.read_text(encoding="utf-8")), path)

    def test_public_state_api_has_no_mutable_setters_or_arrays(self) -> None:
        public_setter = re.compile(r"\bpublic\s+[^;\n{]+{\s*get;\s*set;\s*}", re.MULTILINE)
        public_array = re.compile(r"\bpublic\s+[A-Za-z0-9_<>,\s]+\[\]\s+[A-Za-z0-9_]+")
        for path in [*(CORE / "State").rglob("*.cs"), *(CONTENT / "State").rglob("*.cs")]:
            text = path.read_text(encoding="utf-8")
            self.assertIsNone(public_setter.search(text), path)
            self.assertIsNone(public_array.search(text), path)

    def test_game_state_factory_requires_explicit_seed(self) -> None:
        factory = (CONTENT / "State" / "GameStateFactory.cs").read_text(encoding="utf-8")

        self.assertIn("CreateInitialState(ContentPack pack, int rngSeed)", factory)
        self.assertEqual(1, factory.count("CreateInitialState("))
        self.assertNotRegex(factory, r"CreateInitialState\s*\(\s*ContentPack\s+pack\s*\)")

    def test_content_pack_and_protected_areas_are_unchanged(self) -> None:
        self.assertEqual("", git_text("diff", "--", "Assets/StreamingAssets/content"))
        self.assertEqual("", git_text("diff", "--", "Packages", "ProjectSettings", "Assets/Scenes"))
        self.assertEqual("", git_text("diff", "--", "Assets/Juego pancho/*.txt"))
        self.assertEqual("", git_text("diff", "--", ".vscode/settings.json", "Victoriant Chile.slnx"))


if __name__ == "__main__":
    unittest.main()
