---
id: ADR-0001
status: accepted
date: 2026-07-06
---

# Documentación canónica dentro de Git

## Contexto

Los contratos estaban fuera del repositorio y duplicados dentro de `Assets/`. No existía historia atómica entre contrato, código y prueba, y Unity generaba metadatos para documentación.

## Decisión

La documentación canónica vive en `docs/` dentro del mismo repositorio. Los originales se conservan en `docs/archive/` como antecedentes no normativos. La carpeta externa conserva solo un puntero.

## Consecuencias

- Contratos y código pueden revisarse y versionarse juntos.
- Los documentos dejan de importarse como assets Unity.
- Todo cambio funcional debe actualizar trazabilidad en el mismo PR.

