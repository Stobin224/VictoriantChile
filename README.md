# VictoriantChile

Simulador político en Unity ambientado en un Chile contemporáneo ficticio, orientado a decisiones estratégicas y simulación sistémica.

## Versión de Unity esperada
- **Editor**: `6000.3.10f1`. (fuente: `ProjectSettings/ProjectVersion.txt`)

## Estructura del repositorio
- `Assets/Juego pancho/`: documentación de diseño de juego (visión, sistemas y reglas).
- `Assets/StreamingAssets/content/`: contenido data-driven consumido por el juego.
  - `core/`: catálogos base (IGs, movimientos, regiones).
  - `rules/`: parámetros y reglas cuantitativas del motor.
  - `templates/`: plantillas reutilizables de efectos/eventos/reformas.
  - `strings/`: localización.
- `ProjectSettings/`: configuración del proyecto Unity.
- `scripts/`: utilidades de validación y soporte de contenido.

## Flujo de validación de contenido
Desde la raíz del repositorio:

Comando canónico para agentes y CI general:

```bash
python scripts/run_checks.py
```

Wrappers delgados equivalentes:

```bash
tools/run_checks.sh
```

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/run_checks.ps1
```

Checks individuales disponibles:

1. Validación sintáctica JSON: incluida en `python scripts/run_checks.py`.

2. Verificación no destructiva de hashes de `manifest.json`:

```bash
python scripts/verify_manifest_hashes.py
```

3. Recalcular hashes de `manifest.json` cuando se modificó contenido:

```bash
python scripts/recompute_manifest_hashes.py
python scripts/recompute_manifest_hashes.py --bump-pack
```

`verify_manifest_hashes.py` solo comprueba hashes y falla si no coinciden. `recompute_manifest_hashes.py` modifica `manifest.json`; no debe usarse como validación no destructiva.
Los hashes del Content Pack se calculan sobre bytes JSON con finales de línea normalizados a LF, para que el resultado sea reproducible en Windows y Linux.

4. Validación semántica:

```bash
python scripts/validate_content.py
```

5. Smoke de contrato content/runtime:

```bash
python scripts/smoke_simulation.py
```

6. Enforcement de versionado de manifest comparando base/head:

```bash
python scripts/check_manifest_bump.py --base <sha_base> --head <sha_head>
python scripts/run_checks.py --base-ref <sha_base> --head-ref <sha_head>
python scripts/run_checks.py --base-ref origin/main --working-tree
```

## CI y gate de contenido
- Existe workflow obligatorio de validación: `.github/workflows/content-validation.yml` (incluye enforcement de bump de manifest).
- Existe workflow general: `.github/workflows/repository-quality.yml`, que ejecuta `python scripts/run_checks.py` en pull requests y push a `main`.
- Para cambios en `Assets/StreamingAssets/content/**`, el PR debe pasar este workflow antes de merge.
- `AGENTS.md` define el contrato operativo para agentes.
- `docs/agent_tasks/TEMPLATE.md` contiene la plantilla de tareas acotadas y falsables.
- `docs/headless_testing.md` documenta el arnés headless para EditMode y la decisión sobre fast path .NET.

Acción manual pendiente para branch protection:
- Marcar `Repository Quality / repository-quality` como required global.
- No marcar `Content Validation` como required global si sigue usando filtros `paths`; puede mantenerse como check especializado adicional para cambios de contenido.

## Política de versionado y migraciones
La política de versionado de contenido está documentada en:
- `docs/content_versioning.md`
- `docs/content_pr_ready_checklist.md`

Resumen corto:
- Cambios retrocompatibles en datos: incrementar `content_pack_version`.
- Cambios incompatibles de estructura/schema: incrementar `content_schema_version` y definir migración.
- El runtime debe respetar `min_game_schema_version` para aceptar/rechazar packs.

## Arnés headless

El runner general no requiere Unity ni .NET por defecto:

```bash
python scripts/run_checks.py
```

Descubrir Unity exacto:

```bash
python scripts/find_unity.py --json
```

Ejecutar EditMode headless:

```powershell
python scripts/run_unity_editmode.py --unity-editor "C:\Program Files\Unity\Hub\Editor\6000.3.10f1\Editor\Unity.exe"
```

Integrarlo al runner:

```powershell
python scripts/run_checks.py --include-unity-editmode --unity-editor "C:\Program Files\Unity\Hub\Editor\6000.3.10f1\Editor\Unity.exe"
```

`UNITY_EDITOR_PATH` puede reemplazar `--unity-editor`. Los resultados XML/log/JSON se escriben fuera del proyecto, bajo `%TEMP%\VictoriantChile\HeadlessTests\<run-id>\`, salvo que se indique otra ruta.

El fast path .NET no está implementado en esta base porque la máquina validada no tenía .NET SDK instalado; `dotnet --info` solo reportó runtimes. El arnés actual no ejecuta PlayMode, ticks reales, scheduler, efectos ni persistencia.
