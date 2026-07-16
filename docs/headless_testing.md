# Headless Testing

This repository separates two headless test paths:

- Fast path .NET: pure C# tests outside Unity. This path is not implemented in this baseline because `dotnet --info` reported no installed .NET SDK on the validation machine.
- Unity EditMode integration: real Unity Test Framework execution in batch/headless mode.

The harness does not run PlayMode tests, real ticks, persistence, scheduler logic, effects, events, crises, or legislation. It only proves that the pure simulation assembly can compile without Unity engine references and can be tested by Unity EditMode.

## Unity Discovery

The project expects the exact editor version from `ProjectSettings/ProjectVersion.txt`.

Discovery precedence:

1. `--unity-editor <path>`
2. `UNITY_EDITOR_PATH`
3. Unity Hub standard path for the exact version

Windows standard path:

```powershell
C:\Program Files\Unity\Hub\Editor\6000.3.10f1\Editor\Unity.exe
```

Example with spaces:

```powershell
python scripts/find_unity.py --unity-editor "C:\Program Files\Unity\Hub\Editor\6000.3.10f1\Editor\Unity.exe"
```

JSON discovery output:

```bash
python scripts/find_unity.py --json
```

The script checks the editor version with `-version` before opening the project. It does not scan the disk and does not silently choose another Unity version.

## Unity EditMode

Run EditMode tests directly:

```bash
python scripts/run_unity_editmode.py
```

With an explicit editor:

```powershell
python scripts/run_unity_editmode.py --unity-editor "C:\Program Files\Unity\Hub\Editor\6000.3.10f1\Editor\Unity.exe"
```

The runner writes outputs outside the Unity project by default, under the system temp directory:

```text
%TEMP%/VictoriantChile/HeadlessTests/<run-id>/
```

- `editmode-results.xml`
- `editmode.log`
- `editmode-run.json`, unless `--json-output <path>` is provided

The runner fails if Unity is missing, the version mismatches, Unity cannot start, the command times out, XML is missing, stale or invalid, zero tests run, or any test fails. It parses the XML instead of trusting only Unity's process exit code.

## Repository Runner

The default repository checks do not require Unity or .NET:

```bash
python scripts/run_checks.py
```

Unity EditMode is opt-in:

```powershell
python scripts/run_checks.py --include-unity-editmode --unity-editor "C:\Program Files\Unity\Hub\Editor\6000.3.10f1\Editor\Unity.exe"
```

.NET is also opt-in:

```bash
python scripts/run_checks.py --include-dotnet
```

In this baseline, `--include-dotnet` fails intentionally because the fast path was not implemented without a working .NET SDK.

## .NET Compatibility Decision

`ProjectSettings/ProjectSettings.asset` currently has:

- `scriptingRuntimeVersion: 1`
- `apiCompatibilityLevelPerPlatform: {}`
- `apiCompatibilityLevel: 6`

For Unity 6000 this is equivalent to the modern .NET Standard profile used for pure assemblies, but the fast path gate also requires an installed .NET SDK and a passing `dotnet test`. The machine only had .NET runtime 8.0.20 and no SDK, so no `dotnet/` projects were added.
