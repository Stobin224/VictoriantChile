---
id: CON-TRACE-001
authority: canonical
status: active
---

# Causalidad y reportes

## Requisito

- `REQ-TRACE-001`: todo cambio visible debe ser atribuible y la suma de causas debe coincidir con el delta real.

## Contrato

- Toda mutación recibe un `CauseRef`.
- Categorías mínimas: `DECISION`, `EVENT`, `REFORM`, `MODIFIER` y `SYSTEM`.
- Clamps, redondeos y normalizaciones registran causas `SYSTEM:*`.
- Por target y tick debe cumplirse:

  `valor_final - valor_inicial = suma(contribuciones causales)`

- Cada avance de 1, 4 o 12 ticks produce un `TurnReport`.
- El reporte contiene valor inicial/final, delta total, promedio semanal y principales causas.
- El log completo por tick es transitorio; el último resumen y un historial acotado pueden persistirse.

## Fallo obligatorio

Una mutación visible sin causa es un error de programación, no un caso recuperable ni un warning.

