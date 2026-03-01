# Ready-to-merge checklist para PRs de contenido

Usa este checklist cuando el PR toque `Assets/StreamingAssets/content/**`.

## 1) Versionado y manifest
- [ ] Si cambió cualquier archivo de contenido (excepto `manifest.json`), `manifest.json` también fue actualizado.
- [ ] `content_pack_version` se incrementó en `manifest.json` para cambios de contenido retrocompatibles.
- [ ] Si hubo cambio incompatible de schema, también se incrementó `content_schema_version` y se documentó migración.
- [ ] Hashes de `manifest.files` recalculados con:

```bash
python3 scripts/recompute_manifest_hashes.py
```

## 2) Validaciones locales mínimas
- [ ] Sintaxis JSON:

```bash
for f in $(rg --files Assets/StreamingAssets/content -g '*.json'); do jq empty "$f" || exit 1; done
```

- [ ] Validación semántica:

```bash
python3 scripts/validate_content.py
```

- [ ] Enforcement base/head (opcional local, obligatorio en CI):

```bash
python3 scripts/check_manifest_bump.py --base <sha_base> --head <sha_head>
```

- [ ] Smoke simulation:

```bash
python3 scripts/smoke_simulation.py
```

## 3) Revisión de PR
- [ ] El PR explica claramente si es cambio de contenido o de schema.
- [ ] Se incluyeron riesgos/impacto de gameplay (si aplica).
- [ ] El workflow `Content Validation` está en verde.
