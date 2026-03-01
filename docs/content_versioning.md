# Política de versionado de contenido y migraciones

Este documento define cómo evolucionar `Assets/StreamingAssets/content/manifest.json` sin romper partidas ni loaders.

## Campos clave
- `content_pack_version`: versión incremental del paquete de contenido.
- `content_schema_version`: versión del schema de datos esperado por el runtime.
- `min_game_schema_version`: versión mínima de schema que el juego soporta.

## Reglas de incremento

### 1) Cambios retrocompatibles (sin cambiar schema)
Ejemplos:
- agregar nuevas reformas/eventos/strings,
- ajustar pesos/rangos/tags,
- corregir textos o balance sin modificar estructura.

Acción requerida:
- incrementar **solo** `content_pack_version` en `+1`.

### 2) Cambios incompatibles de schema
Ejemplos:
- renombrar campos consumidos por el runtime,
- cambiar tipos de datos (`int` → `string`, objeto → array),
- mover información entre archivos obligando cambios de loader.

Acciones requeridas:
1. incrementar `content_schema_version` en `+1`.
2. incrementar `content_pack_version` en `+1`.
3. agregar notas de migración en este documento.
4. implementar migrador en runtime o declarar incompatibilidad explícita.

## Procedimiento de migración
1. Definir alcance del cambio y si es retrocompatible.
2. Actualizar números de versión en `manifest.json`.
3. Ejecutar validaciones:
   - sintaxis JSON (`jq`),
   - validación semántica (`python3 scripts/validate_content.py`),
   - enforcement de versionado (`python3 scripts/check_manifest_bump.py --base <sha_base> --head <sha_head>`).
4. Si hubo cambio de schema, documentar en la tabla de historial.

## Historial de migraciones
| Schema | Pack mínimo recomendado | Estado | Notas |
|---|---:|---|---|
| 1 | 1 | vigente | Schema base inicial. |


## Enforcements en flujo de PR
- El workflow `.github/workflows/content-validation.yml` corre en PRs y pushes con cambios de contenido.
- El template de PR (`.github/pull_request_template.md`) incluye checklist de versionado y validación.
- Recomendación de repositorio: marcar `Content Validation / validate-content` como *required status check* en la rama protegida principal.

## Checklist de PR de contenido
- [ ] `manifest.json` actualizado según reglas anteriores.
- [ ] Validación sintáctica y semántica en verde.
- [ ] Notas de migración actualizadas (si aplica).
