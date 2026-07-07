---
id: CON-SIM-001
authority: canonical
status: active
---

# Núcleo de simulación

## Requisitos

- `REQ-CORE-001`: estado canónico serializable y determinista.
- `REQ-CORE-002`: resolución y mutación uniforme de targets.

## Invariantes

- Un tick equivale a una semana.
- Todo valor persistido de simulación usa enteros; escala general `S = 100`.
- `GameState` contiene metadatos, métricas, regiones, IGs, movimientos, reformas, efectos, scheduler y memoria necesaria.
- Los datos estáticos pertenecen al Content Pack, no se duplican en el save.
- Los targets usan paths ASCII estables:
  - `metrics.*`
  - `internals.*.*`
  - `regions.{id}.*`
  - `igs.{id}.clout|approval`
  - `movements.{id}.intensity|direction`
- `TargetConfig` define existencia, default, escala, clamp y operaciones permitidas.
- Ningún sistema modifica diccionarios visibles directamente.

## Cambios y efectos

- Operaciones: `ADD`, `MUL` y `SET`.
- `SET` ganador reemplaza el cálculo de `ADD` y `MUL` para ese target y tick.
- Prioridad y desempates son deterministas.
- Los efectos soportan inicio, fin exclusivo, prioridad y stacking.
- El scheduler ejecuta por `run_tick`, mayor prioridad y luego ID estable.
- `AdvanceTicks(n)` produce el mismo resultado con igual estado, seed y entradas.

## Dependencias

La causalidad obligatoria está definida en [`CON-TRACE-001`](causality.md). Los valores concretos viven en `Assets/StreamingAssets/content/rules/`.

