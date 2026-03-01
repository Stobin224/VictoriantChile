#!/usr/bin/env python3
"""Prueba de humo de simulación mínima del content pack.

Objetivo:
- Cargar manifest + rules + templates.
- Aplicar 2 ticks de una simulación simplificada sobre métricas.
- Verificar que no haya errores de estructura básica y que los valores se mantengan finitos.
"""

from __future__ import annotations

import json
import math
import sys
from pathlib import Path
from typing import Any

ROOT = Path(__file__).resolve().parents[1]
CONTENT_DIR = ROOT / "Assets" / "StreamingAssets" / "content"


def load_json(path: Path) -> Any:
    with path.open("r", encoding="utf-8") as f:
        return json.load(f)


def clamp(v: int, lo: int, hi: int) -> int:
    return max(lo, min(hi, v))


def main() -> int:
    manifest = load_json(CONTENT_DIR / "manifest.json")
    aggregation = load_json(CONTENT_DIR / "rules" / "aggregation_config.json")
    target_config = load_json(CONTENT_DIR / "rules" / "target_config.json")
    effects = load_json(CONTENT_DIR / "templates" / "effects.json")

    required_manifest_files = set(manifest.get("files", {}).keys())
    if not required_manifest_files:
        print("ERROR: manifest sin archivos declarados")
        return 1

    for rel in required_manifest_files:
        if not (CONTENT_DIR / rel).exists():
            print(f"ERROR: archivo de manifest no existe: {rel}")
            return 1

    metric_bounds = {"default": (0, 10000)}
    for row in target_config:
        pattern = row.get("pattern")
        min_s = row.get("minS")
        max_s = row.get("maxS")
        if isinstance(pattern, str) and isinstance(min_s, int) and isinstance(max_s, int):
            if pattern == "metrics.*":
                metric_bounds["default"] = (min_s, max_s)
            elif pattern.startswith("metrics.") and "*" not in pattern:
                metric_bounds[pattern] = (min_s, max_s)

    state: dict[str, int] = {
        "metrics.legitimacy": 5000,
        "metrics.public_agenda": 5000,
        "metrics.information_quality": 5000,
        "metrics.social_tension": 5000,
    }

    # Toma algunos efectos con target de métricas para aplicar en ciclo smoke.
    metric_mods: list[tuple[str, str, int]] = []
    for effect in effects.get("effects", []):
        for mod in effect.get("mods", []):
            target = mod.get("target")
            op = mod.get("op")
            value = mod.get("valueS")
            if isinstance(target, str) and target.startswith("metrics.") and op in {"ADD", "SET"} and isinstance(value, int):
                metric_mods.append((target, op, value))

    if not metric_mods:
        print("ERROR: no se encontraron mods de métricas para smoke test")
        return 1

    agg_metrics = []
    for rule in aggregation:
        if isinstance(rule, dict) and rule.get("type") == "METRIC_AGGREGATION":
            agg_metrics.extend(rule.get("metrics", []))

    for tick in range(1, 3):
        # aplicar algunos efectos en orden determinista
        for target, op, value in metric_mods[:4]:
            current = state.get(target, 5000)
            if op == "ADD":
                state[target] = current + value
            elif op == "SET":
                state[target] = value

        # pseudo-aggregation: cap_per_weekS sobre métricas presentes
        for m in agg_metrics:
            metric = m.get("metric")
            cap = m.get("cap_per_weekS")
            if isinstance(metric, str) and metric in state and isinstance(cap, int):
                delta = 25  # constante chica para smoke
                delta = clamp(delta, -cap, cap)
                state[metric] += delta

        # clamp por target_config
        for target, value in list(state.items()):
            lo, hi = metric_bounds.get(target, metric_bounds["default"])
            state[target] = clamp(value, lo, hi)

        # sanidad numérica
        for target, value in state.items():
            if not isinstance(value, int) or not math.isfinite(float(value)):
                print(f"ERROR: valor inválido en tick {tick}: {target}={value}")
                return 1

    print("OK: smoke simulation completada (2 ticks)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
