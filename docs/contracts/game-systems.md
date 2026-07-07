---
id: CON-SYSTEMS-001
authority: canonical
status: active
---

# Sistemas de juego

## Requisito

- `REQ-SYSTEMS-001`: eventos, movimientos, legislación y territorio reutilizan el núcleo común sin sistemas paralelos.

## Eventos y movimientos

- Templates resuelven condiciones, variables, opciones, efectos, cooldowns y máximos por campaña.
- Eventos `AUTO`, `CHOICE` y `CRISIS` usan scheduler, efectos y causalidad comunes.
- Una decisión bloqueante detiene el avance hasta resolverse.
- Los movimientos escalan por tick y pueden habilitar o bloquear rutas.

## Legislación

- MVP: una reforma activa.
- Etapas `WORK` y `VOTE`; cámaras `NONE`, `LOWER`, `UPPER` o `BOTH`.
- Legitimidad actúa como compuerta; Senado añade fricción.
- Soporte, progreso, pass/fail y costes son deterministas y configurables.

## Territorio

- 16 regiones con IDs estables.
- Estado regional dinámico separado de definiciones estáticas.
- Acoplamiento nacional-regional usa inercia y un tick de latencia para el feedback.
- Eventos regionales pueden generar spillover nacional.

## Límite actual

Existen templates y reglas JSON, pero estos sistemas aún no tienen runtime C#.

