# README — Diseño base (Victoria 3 simplificado a Chile, solo decisiones)

## 0) Resumen ejecutivo

Sim político serio singleplayer ambientado en un Chile contemporáneo ficticio (sin partidos/personas reales), inspirado en la lógica de política interna de Victoria 3: **Interest Groups (IGs) → coalición/gobierno → legitimidad → reformas por etapas → movimientos/crisis**.
Sin minijuegos ni tablero/cartas: el jugador toma decisiones; el sistema simula internamente y entrega consecuencias con **explicación causal** posterior.

Elecciones **cada 4 años fijas**. Mapa de Chile por regiones como vista principal. UI por paneles: Sede del Partido, Cámara, Senado, Gobierno, Justicia, Prensa/Opinión, Movimientos, Objetivos (Journal).

---

## 1) Principios de diseño

### 1.1 “Solo decisiones” (con indicadores numéricos visibles)

* Se muestran números “duros” al jugador para los indicadores principales (idealmente acotados, p.ej. 0–100).
* Los diagnósticos cualitativos (alto/medio/bajo, estable/en deterioro, tendencia, riesgo) se derivan de esos números y se muestran como apoyo, no como sustituto.
* La simulación es el “juego”; la UI es briefing + decisión + reporte con deltas/causas.


### 1.2 Victoria 3 simplificado (lo que se copia)

* IGs como motor estructural.
* Gobierno/coalición define viabilidad política.
* Legitimidad como “compuerta”: con legitimidad baja se bloquea avance de reformas normales.
* Reformas como proceso por etapas (no toggle).
* Movimientos como presión temática que escala hacia crisis o habilita vías excepcionales.

### 1.3 Chile contemporáneo (marco institucional, mundo ficticio)

* Presidencialismo fuerte + Congreso bicameral (Cámara/Senado).
* Elecciones cada 4 años fijas como ritmo de campaña.
* “Eliminar oposición” = neutralización política-institucional (fragmentación/pérdida de representación/capacidad).

---

## 2) Fantasía y experiencia de usuario

Eres el estratega/secretario general. Operas en:

* **Sede del partido** (cuadros, facciones, disciplina).
* **Congreso** (Cámara y Senado, comisiones, negociación).
* **Gobierno** (coalición, gabinete, agenda).
* **Prensa/Opinión** (ciclos, escándalos, marcos).
* **Movimientos** (presión social por temas).
* **Justicia/Constitucional** (fricción, judicialización).

Objetivo: ganar el ciclo político (4 años) y completar un “Journal” de victoria.

---

## 3) Core loop y tiempo

### 3.1 Loop por checkpoints

1. Briefing (titulares + informes internos + situación legislativa + alertas).
2. 1–3 decisiones (según urgencias y carga del periodo).
3. Simulación del periodo.
4. Reporte de consecuencias:

   * narrativa (qué pasó)
   * causalidad (por qué): IGs → legitimidad → reforma/movimiento → estabilidad

### 3.2 Control del tiempo por el jugador

Al presionar **Avanzar**, el jugador elige paso:

* 1 semana / 1 mes / 1 trimestre (MVP).
  El sistema ejecuta efectos internos durante ese período.

### 3.3 Decisiones bloqueantes

Crisis o plazos institucionales generan “lock”: no puedes avanzar sin resolver.

---

## 4) Estados internos (motor del juego)

Se guardan como variables internas; se exponen como diagnósticos.

### 4.1 Variables principales (propuesta)

* Legitimidad del gobierno
* Organización del partido
* Cohesión interna (facciones)
* Capacidad de gobernabilidad/implementación
* Información (calidad de briefings)
* Agenda/saliencia temática
* Tensión social (nacional + por región)
* Capacidad legislativa (coalición + disciplina + negociación)
* Reputación multieje (ver 6.2)

### 4.2 Triángulo de estabilidad (anti-estrategia dominante)

* Legitimidad ↔ Organización ↔ Cohesión
  Evita “ataque permanente” como estrategia óptima.

---

## 5) Interest Groups (IGs) — MVP de 9

Cada IG:

* peso nacional + sesgo regional
* aprobación (contento/neutral/enojado)
* tags ideológicos (2–4) para soporte a reformas/eventos

**Lista IGs:**

1. Empresariado y Finanzas
2. Sindicatos y Trabajo Organizado
3. Sector Público y Burocracia
4. Orden y Seguridad
5. Conservadores Cívicos/Religiosos
6. Progresistas Urbanos (ONG/estudiantes)
7. Profesionales y Clase Media
8. Territorio Productivo (rural/minería/puertos)
9. Ambiental-Regionalista / Pueblos originarios (abstracto)

---

## 6) Metaprogresión: cuadros + reputación (balanceada)

### 6.1 Cuadros (roles persistentes)

Roles: vocería, operador territorial, negociador, whip, policy lead, contralor.
Atributos internos: competencia, lealtad, ambición, redes, riesgo (escándalo).
Progresión contextual por “historial” (crisis resueltas, acuerdos logrados).

### 6.2 Reputación multieje (no una sola barra)

* Integridad
* Competencia
* Firmeza
* Empatía
* Pragmatismo
* Coherencia

### 6.3 Anti-snowball

* decaimiento
* trade-offs reales (pragmatismo vs coherencia, firmeza vs polarización)
* costos de mantenimiento (exposición/rivalidades)
* rendimientos decrecientes
* shocks que obligan a gastar capital

---

## 7) Congreso bicameral y reformas por etapas

### 7.1 Modelo legislativo (sin micro)

* Cámara y Senado como cámaras con ritmos distintos.
* Comisiones/agenda simplificadas.
* Disciplina y negociación como decisiones (no minijuego).

### 7.2 Reformas como “expediente” por hitos

Cada reforma tiene 3–5 hitos (ejemplo):

* ingreso → comisión → negociación → votación cámara A → cámara B → cierre/implementación
  (la cadena exacta se define en contenido, no en el motor)

### 7.3 Legitimidad como compuerta

* Reformas normales requieren legitimidad suficiente.
* Con legitimidad baja, solo avanzan reformas con “movimiento” fuerte que las empuje (vía excepcional).

---

## 8) Movimientos y crisis

Movimientos = presiones temáticas (pro/anti reforma).

* escalan si se ignoran
* generan crisis: paros, estallidos regionales, fracturas, caída de coalición, acusaciones
* pueden habilitar reformas incluso con gobierno débil (mecánica tipo V3 simplificada)

---

## 9) Constitución: dos rutas jugables (“de las formas que se pueda”)

### Ruta A — Reforma constitucional vía Congreso

* “reforma especial” en el mismo motor de leyes
* exige legitimidad alta + soportes clave + alto riesgo de backlash/judicialización

### Ruta B — Proceso constituyente (Journal multi-fase)

Cadena acotada (4–6 fases):

* habilitación → consulta/plebiscito → órgano redactor → borrador → salida → implementación
  Activa por crisis/movimiento fuerte y/o por habilitante previa (parámetro de diseño).

---

## 10) Objetivos (victorias / Journal)

Un objetivo principal activo (y subobjetivos):

* Presidente del partido llega a la Presidencia.
* Reforma constitucional profunda o proceso constituyente exitoso.
* Neutralizar oposición (institucionalmente): fragmentación/pérdida de representación/capacidad de coalición.
  Siempre con costos sistémicos (legitimidad, crisis, judicialización, movilización).

---

## 11) UI/UX (pantallas MVP)

### Pantalla principal

* Mapa por regiones con “lentes” (apoyo, tensión, organización, control, presencia rival).
  Nota: cada región además tiene “recursos/capacidades” estáticos (admin_capS/industry_capS/extractive_capS/social_capS/populationS) usados para ponderar selección de eventos; en MVP pueden quedar neutros (50) sin afectar balance.

* Outliner: reforma activa, movimientos escalando, crisis, votaciones.

### Ventanas/paneles

* Sede del Partido
* Cámara
* Senado
* Gobierno
* Justicia/Constitucional
* Prensa/Opinión
* Movimientos
* Journal (Objetivos)

### Reporte post-turno (obligatorio)

* “Qué ocurrió” + “por qué ocurrió” (cadena causal explicada).

---

## 12) Alcance MVP vs No-MVP

### MVP (primer vertical slice)

**Meta:** 30–60 min jugables, 1 ciclo parcial, con el loop completo.

* Mapa Chile por regiones (solo selección + “lentes” básicos).
* 9 IGs con peso/aprobación/tags.
* Motor de legitimidad + coalición simple.
* Motor de reformas por etapas:

  * 8–12 reformas (no 25–30 aún).
  * 1 reforma activa a la vez (para simplificar).
* Movimientos:

  * 3–5 temas base, escalamiento simple.
* Eventos plantilla:

  * 15–25 plantillas parametrizadas.
* Cuadros:

  * 6–10 cuadros, 3 roles principales.
* Reporte causal post-turno.
* Persistencia (save/load) mínima.

### No-MVP (post vertical slice)

* Catálogo completo de reformas (25–30) en 6 paquetes.
* Eventos plantilla 50–80 + cadenas.
* Proceso constituyente completo (Ruta B) con fases.
* Modelado más fino de Cámara/Senado (comisiones y “tiempos” diferenciados).
* IA rival con estilos avanzados.
* Telemetría y balance automático.
* Más capas del mapa (municipalidad, redes, judicialización, etc.).
* Herramientas internas (editor de eventos/reformas).

---

## 13) Backlog técnico (por subsistemas)

### 13.1 Motor de estado

* Definir `GameState` (serializable).
* Definir `RegionState` (por región).
* Definir `IGState` (peso/aprobación/tags).
* Definir `PartyState` (organización, cohesión, cuadros).
* Definir `GovernmentState` (coalición, legitimidad, agenda).
* Definir `LegislatureState` (Cámara/Senado, composición, agenda).
* Definir `ReputationState` (multieje).
* Definir `MovementState` (tema, intensidad, objetivo, timers).

### 13.2 Motor de simulación (time advance)

* Tick por día/semana interno (no visible).
* Resolución por “paso” (semana/mes/trimestre).
* Scheduler de consecuencias diferidas (timers).
* Manejo de “decisiones bloqueantes”.

### 13.3 Motor legislativo (reformas por etapas)

* Modelo de `Reform`:

  * área, tags, IGs pro/anti, prerequisitos, etapas, efectos.
* Modelo de `ReformProgress`:

  * etapa actual, soporte, riesgo, bloqueo.
* Regla de legitimidad (compuerta).
* Interacción con movimientos.

### 13.4 Motor de eventos plantilla

* Definir formato de plantilla:

  * condiciones (state predicates)
  * variables (región, IG, tema, severidad)
  * opciones (2–4)
  * efectos inmediatos y diferidos
  * memoria/flags
* Selector de eventos (prioridades, evitar repetición).
* Integración con reportes causales.

### 13.5 IA (singleplayer oponentes)

MVP:

* agentes rivales simples con “estrategia” por heurísticas:

  * atacar, coalicionar, bloquear, capturar agenda
* info imperfecta (errores).
  No-MVP:
* estilos (pragmático, populista, tecnócrata).
* planificación por horizonte.

### 13.6 UI paneles (PC)

* Mapa (selección de región + lentes).
* Outliner.
* Panel Sede/Partido (cuadros y decisiones internas).
* Panel Cámara y Senado (agenda + votaciones).
* Panel Gobierno (coalición/legitimidad).
* Panel Movimientos (alertas/gestión).
* Panel Prensa (ciclo).
* Journal (objetivos).
* Pantalla de “Consecuencias y causalidad” por turno.

### 13.7 Persistencia

* Save/Load del `GameState`.
* Versionado de saves (migraciones simples).

### 13.8 Telemetría / balance

* Logging de:

  * decisiones tomadas
  * cambios cualitativos traducidos a variables internas
  * eventos disparados
  * duración y tasas de victoria
* Herramientas de “replay” para depurar balance.

---

## 14) Riesgos y mitigaciones

* Arbitrariedad percibida → reporte causal consistente.
* Contenido infinito → plantillas parametrizadas + cadenas cortas.
* Estrategia dominante → triángulo de estabilidad + costos sistémicos.
* Metaprogresión rompe dificultad → decaimiento + trade-offs + mantenimiento.

---

## 15) Próximo paso recomendado (para programar sin decidir contenido final)

**Vertical slice**:

1. Definir estructuras de estado (GameState).
2. Implementar avance de tiempo con scheduler.
3. Implementar 9 IGs y legitimidad.
4. Implementar 8–12 reformas con 3–5 etapas.
5. Implementar 15–25 eventos plantilla.
6. Implementar reporte causal post-turno.
7. UI mínima: mapa + outliner + 3 paneles (Sede, Congreso, Gobierno).
8. Save/Load.

Las decisiones de contenido “grande” (año exacto, catálogo 25–30 reformas y 50–80 eventos) quedan para después del slice.
