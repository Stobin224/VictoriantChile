# Contrato operativo para agentes

Este archivo dirige hacia el contexto correcto. No contiene el diseño completo.

## Autoridad

- Git conserva la historia oficial del proyecto.
- Los contratos activos en `docs/contracts/` describen el comportamiento requerido.
- Los ADRs aceptados en `docs/decisions/` explican decisiones costosas de revertir.
- El código y las pruebas describen la realidad implementada.
- `docs/implementation/STATUS.md` resume únicamente evidencia verificada.
- `docs/implementation/ROADMAP.md` expresa intención futura, no funcionalidad existente.
- `docs/archive/` conserva antecedentes y nunca es normativo.

Si contrato, código y estado discrepan, detener la decisión, registrar la desviación y presentar evidencia. No escoger silenciosamente una versión.

## Orden obligatorio de contexto

Antes de opinar o modificar:

1. comprobar rama, commit y cambios locales;
2. leer `docs/INDEX.md`;
3. leer `docs/implementation/STATUS.md`;
4. leer la ficha `I-###` activa;
5. leer solo los contratos y ADRs del sistema afectado;
6. inspeccionar código y pruebas reales;
7. comparar comportamiento requerido, implementado y verificado.

## Ciclo de cambio

1. Crear o activar una ficha `I-###`.
2. Enlazar requisitos `REQ-*` y contratos `CON-*`.
3. Crear un `ADR-####` si la decisión es costosa de revertir.
4. Implementar código y pruebas.
5. Ejecutar validaciones reproducibles.
6. Completar la evidencia de la ficha.
7. Actualizar `STATUS.md`, `ROADMAP.md` e `INDEX.md` cuando corresponda.
8. Entregar contrato, código, pruebas y estado en el mismo commit o PR.

## Contrato de minuciosidad

- Antes de escribir código, explicar el objetivo del micro paso actual.
- Implementar como máximo una línea o un bloque mínimo por confirmación del usuario.
- Introducir solo un concepto nuevo por micro paso.
- Después de cada edición, explicar exactamente lo agregado y detenerse.
- No continuar con el siguiente micro paso sin confirmación explícita del usuario.
- Si ya existe código adelantado, tratarlo como material pendiente de lectura y validación, no como avance cerrado.

## Invariantes técnicas

- Unity `6000.3.10f1`.
- Runtime C# headless, determinista y separado de UI y `MonoBehaviour`.
- Un tick equivale a una semana.
- Estado persistido en enteros fixed-point con `S = 100`.
- Toda mutación de un target requiere operación y causa.
- Contenido, implementación y verificación son estados distintos.
- Ningún incremento se completa sin prueba o procedimiento reproducible.

## Checklist final

Antes de emitir una conclusión:

- ¿Leí la ficha activa y los contratos relevantes?
- ¿Inspeccioné el código y las pruebas, en vez de confiar solo en `STATUS.md`?
- ¿Diferencié lo documentado, implementado y verificado?
- ¿Toda afirmación de funcionamiento tiene evidencia?
- ¿Actualicé la trazabilidad si cambió el comportamiento?
