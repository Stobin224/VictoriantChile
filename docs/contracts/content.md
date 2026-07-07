---
id: CON-CONTENT-001
authority: canonical
status: active
---

# Content Pack

## Requisito

- `REQ-CONTENT-001`: reglas y catálogos deben ser data-driven, versionados y validables sin ejecutar UI.

## Contrato

- El contenido canónico reside en `Assets/StreamingAssets/content/`.
- JSON es el único formato de contenido runtime.
- `manifest.json` declara ID, versión de pack, schema, compatibilidad, idiomas y hashes.
- IDs son ASCII `snake_case`, estables y únicos.
- Todo texto visible usa claves de localización.
- El contenido numérico de simulación usa enteros fixed-point, no floats.
- Referencias, enums, rangos, localización y hashes deben validarse antes de integrar cambios.
- Cambios retrocompatibles incrementan `content_pack_version`.
- Cambios incompatibles incrementan además `content_schema_version` y documentan migración.

## Evidencia actual

Las herramientas existentes validan 9 IGs, 9 movimientos, 7 reformas y 8 eventos. Esto demuestra contenido consistente, no un runtime C# que lo consuma.

Procedimientos relacionados:

- [`../content_versioning.md`](../content_versioning.md)
- [`../content_pr_ready_checklist.md`](../content_pr_ready_checklist.md)
