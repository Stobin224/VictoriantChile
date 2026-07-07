# Roadmap construible

El roadmap expresa intención futura. Solo [`STATUS.md`](STATUS.md) puede declarar realidad verificada.

## Fase 0 — Línea base y trazabilidad

Estado: completada mediante [`I-000`](increments/I-000.md).

## Fase 1 — Estado y targets

Estado: activa mediante [`I-001`](increments/I-001.md).

- ensamblados headless y tests EditMode;
- fixed-point y `GameState`;
- `TargetPath`, `TargetConfig` y matching;
- `TargetResolver` con errores y clamps.

## Fase 2 — Mutación, efectos, tiempo y causalidad

- `CauseRef` y API única de mutación;
- `ADD`, `MUL`, `SET`, efectos y stacking;
- scheduler y `AdvanceTicks`;
- causal buffer y `TurnReport`.

## Fase 3 — Contenido runtime y persistencia

- loader del manifest y catálogos;
- validación runtime;
- save/load JSON v1 y migración base.

## Fase 4 — Vertical slice headless

`LoadContent → NewGame → ApplyFakeDecision → AdvanceTicks(4) → Aggregate → Report → Save → Load`

## Fases posteriores

1. Eventos mínimos.
2. Legislación mínima.
3. UI de depuración.
4. Territorio, movimientos y legislación completa.
5. Mapa, paneles y primera campaña.
6. Ruta constitucional, metaprogresión, IA y contenido extenso.

