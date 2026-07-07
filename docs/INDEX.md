# Índice canónico de contexto

Este es el punto de entrada documental. Permite localizar el contexto mínimo sin leer el archivo histórico completo.

## Clases de verdad

| Clase | Ubicación | Pregunta que responde |
|---|---|---|
| Normativa | [`contracts/`](contracts/README.md) | ¿Qué debe hacer el sistema? |
| Decisiones | [`decisions/`](decisions/README.md) | ¿Por qué se eligió este diseño? |
| Realidad | [`implementation/STATUS.md`](implementation/STATUS.md), código y pruebas | ¿Qué existe y qué está verificado? |
| Plan | [`implementation/ROADMAP.md`](implementation/ROADMAP.md) | ¿Qué se construirá después? |
| Antecedentes | [`archive/README.md`](archive/README.md) | ¿De dónde surgieron los contratos? |

## Mapa de trazabilidad

| Requisito | Sistema | Contrato | Decisiones | Incremento actual | Código / pruebas | Estado |
|---|---|---|---|---|---|---|
| `REQ-PRODUCT-001` | Loop de simulación política | [`CON-PRODUCT-001`](contracts/product.md) | [`ADR-0002`](decisions/ADR-0002-headless-deterministic-core.md) | [`I-001`](implementation/increments/I-001.md) | Aún no existe | Documentado |
| `REQ-CORE-001` | Estado canónico y tiempo | [`CON-SIM-001`](contracts/simulation-core.md) | [`ADR-0002`](decisions/ADR-0002-headless-deterministic-core.md) | [`I-001`](implementation/increments/I-001.md) | [`GameState`](../Assets/VictoriantChile/Simulation/GameState.cs), [`tests`](../Assets/VictoriantChile/Tests/EditMode/GameStateTests.cs) | Verificado parcial |
| `REQ-CORE-002` | Targets y mutación | [`CON-SIM-001`](contracts/simulation-core.md) | [`ADR-0002`](decisions/ADR-0002-headless-deterministic-core.md) | [`I-001`](implementation/increments/I-001.md) | Aún no existe | Documentado |
| `REQ-TRACE-001` | Causalidad verificable | [`CON-TRACE-001`](contracts/causality.md) | [`ADR-0002`](decisions/ADR-0002-headless-deterministic-core.md) | [`I-001`](implementation/increments/I-001.md) | Aún no existe | Documentado |
| `REQ-CONTENT-001` | Contenido data-driven | [`CON-CONTENT-001`](contracts/content.md) | — | [`I-000`](implementation/increments/I-000.md) | `Assets/StreamingAssets/content/`, `scripts/validate_content.py` | Verificado |
| `REQ-SAVE-001` | Persistencia versionada | [`CON-PERSIST-001`](contracts/persistence.md) | — | Futuro | Aún no existe | Documentado |
| `REQ-SYSTEMS-001` | Eventos, legislación y territorio | [`CON-SYSTEMS-001`](contracts/game-systems.md) | — | Futuro | Contenido sin runtime C# | Solo contenido |
| `REQ-DOCS-001` | Trazabilidad del proyecto | [`CON-DOCS-001`](contracts/documentation.md) | [`ADR-0001`](decisions/ADR-0001-canonical-documentation-in-git.md) | [`I-000`](implementation/increments/I-000.md) | `scripts/validate_project_docs.py` | Verificado |

## Contexto recomendado para `TargetResolver`

1. [`CON-SIM-001`](contracts/simulation-core.md).
2. [`ADR-0002`](decisions/ADR-0002-headless-deterministic-core.md).
3. [`I-001`](implementation/increments/I-001.md).
4. `Assets/StreamingAssets/content/rules/target_config.json`.
5. Código y pruebas, cuando existan.

## Regla de actualización

Una fila cambia a **Implementado** solo cuando enlaza código y pruebas. Cambia a **Verificado** únicamente cuando la ficha del incremento contiene comando y resultado reproducibles.
