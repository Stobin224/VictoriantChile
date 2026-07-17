from __future__ import annotations

import json
import re
import subprocess
import unittest
import xml.etree.ElementTree as ET
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
CONTENT_ASSEMBLY = ROOT / "Assets" / "VictoriantChile" / "Content"
CONTENT_ASMDEF = CONTENT_ASSEMBLY / "VictoriantChile.Content.asmdef"
CONTENT_ASMDEF_META = CONTENT_ASSEMBLY / "VictoriantChile.Content.asmdef.meta"
CORE_ASMDEF_META = ROOT / "Assets" / "VictoriantChile" / "Simulation" / "Core" / "VictoriantChile.Simulation.Core.asmdef.meta"
EDITMODE_ASMDEF = ROOT / "Assets" / "VictoriantChile" / "Tests" / "EditMode" / "VictoriantChile.Simulation.Tests.EditMode.asmdef"


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


def git_json(ref_path: str):
    return json.loads(git_text("show", ref_path))


def meta_guid(path: Path) -> str:
    match = re.search(r"^guid:\s*([0-9a-f]{32})$", path.read_text(encoding="utf-8"), re.MULTILINE)
    if not match:
        raise AssertionError(f"Missing GUID in {path}")
    return match.group(1)


class RuntimeContentLoaderContractTest(unittest.TestCase):
    def test_newtonsoft_package_is_pinned_and_only_package_change(self) -> None:
        manifest = load_json(ROOT / "Packages" / "manifest.json")
        lock = load_json(ROOT / "Packages" / "packages-lock.json")
        base_manifest = git_json("origin/main:Packages/manifest.json")
        base_lock = git_json("origin/main:Packages/packages-lock.json")

        self.assertEqual("3.2.2", manifest["dependencies"].get("com.unity.nuget.newtonsoft-json"))
        self.assertEqual("3.2.2", lock["dependencies"]["com.unity.nuget.newtonsoft-json"]["version"])
        self.assertEqual(0, lock["dependencies"]["com.unity.nuget.newtonsoft-json"]["depth"])

        self.assertEqual(base_manifest["dependencies"], manifest["dependencies"])

        for package, base_entry in base_lock["dependencies"].items():
            self.assertIn(package, lock["dependencies"])
            self.assertEqual(base_entry.get("version"), lock["dependencies"][package].get("version"), package)
        self.assertEqual(set(base_lock["dependencies"]), set(lock["dependencies"]))

    def test_content_asmdef_contract(self) -> None:
        asmdef = load_json(CONTENT_ASMDEF)
        core_guid = meta_guid(CORE_ASMDEF_META)

        self.assertEqual("VictoriantChile.Content", asmdef["name"])
        self.assertEqual("VictoriantChile.Content", asmdef["rootNamespace"])
        self.assertIs(True, asmdef["noEngineReferences"])
        self.assertIs(False, asmdef["autoReferenced"])
        self.assertIs(False, asmdef["allowUnsafeCode"])
        self.assertIn(f"GUID:{core_guid}", asmdef["references"])
        self.assertIn("Newtonsoft.Json", asmdef["references"])

    def test_editmode_asmdef_references_content_by_real_guid(self) -> None:
        editmode = load_json(EDITMODE_ASMDEF)
        content_guid = meta_guid(CONTENT_ASMDEF_META)

        self.assertIn(f"GUID:{content_guid}", editmode["references"])

    def test_content_assets_have_meta_files_and_unique_guids(self) -> None:
        for path in [CONTENT_ASSEMBLY, *CONTENT_ASSEMBLY.rglob("*")]:
            if path.name.endswith(".meta"):
                continue
            if path.is_dir() or path.suffix in {".cs", ".asmdef"}:
                self.assertTrue(path.with_name(path.name + ".meta").exists(), f"missing meta for {path}")

        seen: dict[str, Path] = {}
        for meta in (ROOT / "Assets").rglob("*.meta"):
            guid = meta_guid(meta)
            self.assertNotIn(guid, seen, f"duplicate GUID {guid}: {seen.get(guid)} and {meta}")
            seen[guid] = meta

    def test_content_productive_code_has_no_unity_api_dependencies(self) -> None:
        forbidden = re.compile(
            r"UnityEngine|UnityEditor|MonoBehaviour|ScriptableObject|JsonUtility|UnityWebRequest|Application\.streamingAssetsPath"
        )
        for path in CONTENT_ASSEMBLY.rglob("*.cs"):
            self.assertIsNone(forbidden.search(path.read_text(encoding="utf-8")), path)

    def test_directory_source_does_not_forward_raw_io_exception_messages(self) -> None:
        loading_source = (CONTENT_ASSEMBLY / "Loading" / "ContentLoading.cs").read_text(encoding="utf-8")

        self.assertNotIn("ContentFileReadResult.Failed(ex.Message)", loading_source)
        self.assertIn("I/O read failed", loading_source)
        self.assertIn("Read access denied", loading_source)

    def test_core_does_not_reference_loader_concerns(self) -> None:
        forbidden = re.compile(r"Newtonsoft|System\.IO|System\.Text\.Json|Security\.Cryptography|SHA256|Json")
        core = ROOT / "Assets" / "VictoriantChile" / "Simulation" / "Core"
        for path in core.rglob("*.cs"):
            self.assertIsNone(forbidden.search(path.read_text(encoding="utf-8")), path)

    def test_public_content_api_does_not_expose_newtonsoft_types(self) -> None:
        forbidden = re.compile(r"\b(JObject|JArray|JToken|JsonReader|Newtonsoft\.)\b")
        public_declaration = re.compile(r"^\s*public\s", re.MULTILINE)
        for path in CONTENT_ASSEMBLY.rglob("*.cs"):
            text = path.read_text(encoding="utf-8")
            for match in public_declaration.finditer(text):
                line_end = text.find("\n", match.start())
                line = text[match.start() : line_end if line_end >= 0 else len(text)]
                self.assertIsNone(forbidden.search(line), f"{path}: {line}")

    def test_solution_contains_exactly_expected_projects(self) -> None:
        tree = ET.parse(ROOT / "Victoriant Chile.slnx")
        projects = [element.attrib["Path"] for element in tree.getroot().findall("Project")]
        expected = {
            "VictoriantChile.Content.csproj",
            "VictoriantChile.Simulation.Core.csproj",
            "VictoriantChile.Simulation.Tests.EditMode.csproj",
        }

        self.assertEqual(3, len(projects))
        self.assertEqual(expected, set(projects))
        self.assertEqual(len(projects), len(set(projects)))
        for project in projects:
            self.assertNotRegex(project, r"^[A-Za-z]:\\|^/|\\")
            self.assertNotIn("Library", project)
            self.assertNotIn("Temp", project)
            self.assertNotIn("Packages", project)

    def test_no_csproj_files_are_tracked_or_untracked(self) -> None:
        tracked = git_text("ls-files", "*.csproj").splitlines()
        untracked = git_text("ls-files", "--others", "--exclude-standard", "*.csproj").splitlines()

        self.assertEqual([], tracked)
        self.assertEqual([], untracked)


if __name__ == "__main__":
    unittest.main()
