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

1. Validación de sintaxis JSON:

```bash
for f in $(rg --files Assets/StreamingAssets/content -g '*.json'); do jq empty "$f" || exit 1; done
```

2. Validación semántica (IDs, referencias cruzadas, loc_*, enums y rangos `S`):

```bash
python3 scripts/validate_content.py
```

3. Prueba de humo de simulación mínima (2 ticks):

```bash
python3 scripts/smoke_simulation.py
```

## CI y gate de contenido
- Existe workflow obligatorio de validación: `.github/workflows/content-validation.yml`.
- Para cambios en `Assets/StreamingAssets/content/**`, el PR debe pasar este workflow antes de merge.

## Política de versionado y migraciones
La política de versionado de contenido está documentada en:
- `docs/content_versioning.md`

Resumen corto:
- Cambios retrocompatibles en datos: incrementar `content_pack_version`.
- Cambios incompatibles de estructura/schema: incrementar `content_schema_version` y definir migración.
- El runtime debe respetar `min_game_schema_version` para aceptar/rechazar packs.
