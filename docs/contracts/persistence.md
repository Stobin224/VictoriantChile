---
id: CON-PERSIST-001
authority: canonical
status: active
---

# Persistencia

## Requisito

- `REQ-SAVE-001`: guardar y cargar el estado sin perder determinismo y con compatibilidad explícita.

## Contrato

- La fuente de verdad del save es `GameState`.
- Versiones separadas: formato de contenedor, schema de estado y Content Pack.
- El loader acepta schemas anteriores soportados mediante migraciones secuenciales puras.
- Un schema futuro se rechaza con diagnóstico claro.
- Migraciones cambian estructura y defaults; no rebalancean campañas.
- Renombres de IDs usan alias explícitos.
- La primera implementación valida JSON y migración base antes de agregar compresión, backups o cloud save.

