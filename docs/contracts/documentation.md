---
id: CON-DOCS-001
authority: canonical
status: active
---

# Contexto y trazabilidad

## Requisito

- `REQ-DOCS-001`: cualquier agente debe reconstruir intención, decisión, implementación y evidencia sin depender de conversaciones previas.

## Contrato

- `AGENTS.md` funciona como router operativo.
- `docs/INDEX.md` relaciona requisitos, contratos, ADRs, incrementos, código, pruebas y estado.
- Cada decisión costosa de revertir tiene un ADR.
- Cada incremento tiene una ficha `I-###`.
- Una afirmación de **Implementado** enlaza código y prueba.
- Una afirmación de **Verificado** incluye comando y resultado reproducibles.
- Los documentos archivados nunca son autoridad.
- Dos contratos canónicos no pueden compartir el mismo `CON-*`.
- Contradicciones entre contrato y código se registran; no se resuelven por suposición.

