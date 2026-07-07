---
id: ADR-0002
status: accepted
date: 2026-07-06
---

# Núcleo headless y determinista

## Contexto

UI, eventos y legislación dependen del mismo estado, tiempo y reglas de cambio. Implementarlos directamente en escenas dificultaría pruebas, persistencia y explicación causal.

## Decisión

Construir primero un núcleo C# puro, sin `MonoBehaviour`, con tick semanal, fixed-point `S = 100`, target paths, mutación causal y orden determinista.

## Consecuencias

- Los tests EditMode verifican el dominio sin cargar escenas.
- Unity queda como adaptador y presentación.
- La UI final se difiere hasta demostrar el vertical slice headless.

