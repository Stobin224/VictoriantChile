#!/usr/bin/env python3
"""Validación semántica del content pack.

Checks implementados:
- IDs únicos en catálogos principales.
- Referencias cruzadas válidas (IGs, movements, effects, strings).
- Rangos S para targets de métricas según target_config.
"""

from __future__ import annotations

import json
import sys
from pathlib import Path
from typing import Any

ROOT = Path(__file__).resolve().parents[1]
CONTENT_DIR = ROOT / "Assets" / "StreamingAssets" / "content"


class ValidationError(Exception):
    pass


def load_json(path: Path) -> Any:
    with path.open("r", encoding="utf-8") as f:
        return json.load(f)


def ensure_unique_ids(items: list[dict[str, Any]], label: str) -> list[str]:
    ids = [item.get("id") for item in items]
    missing = [i for i in ids if not i or not isinstance(i, str)]
    if missing:
        raise ValidationError(f"{label}: hay elementos sin id string válido")

    duplicated = sorted({x for x in ids if ids.count(x) > 1})
    if duplicated:
        raise ValidationError(f"{label}: IDs duplicados detectados: {duplicated}")
    return ids


def collect_metric_ranges(target_config: list[dict[str, Any]]) -> tuple[dict[str, tuple[int, int]], tuple[int, int]]:
    default_min = None
    default_max = None
    exact_ranges: dict[str, tuple[int, int]] = {}

    for row in target_config:
        pattern = row.get("pattern")
        min_s = row.get("minS")
        max_s = row.get("maxS")
        if not isinstance(pattern, str) or not isinstance(min_s, int) or not isinstance(max_s, int):
            continue
        if pattern == "metrics.*":
            default_min, default_max = min_s, max_s
        elif pattern.startswith("metrics.") and "*" not in pattern:
            exact_ranges[pattern] = (min_s, max_s)

    if default_min is None or default_max is None:
        raise ValidationError("target_config: falta patrón metrics.* con minS/maxS")
    return exact_ranges, (default_min, default_max)


def check_target_value(target: str, value_s: int, exact_ranges: dict[str, tuple[int, int]], default_range: tuple[int, int], context: str) -> list[str]:
    if not target.startswith("metrics."):
        return []
    lo, hi = exact_ranges.get(target, default_range)
    if value_s < lo or value_s > hi:
        return [f"{context}: valueS={value_s} fuera de rango [{lo}, {hi}] para {target}"]
    return []


def validate() -> None:
    manifest = load_json(CONTENT_DIR / "manifest.json")
    igs_data = load_json(CONTENT_DIR / "core" / "igs.json")
    movements_data = load_json(CONTENT_DIR / "core" / "movements.json")
    regions_data = load_json(CONTENT_DIR / "core" / "regions.json")
    effects_data = load_json(CONTENT_DIR / "templates" / "effects.json")
    reforms_data = load_json(CONTENT_DIR / "templates" / "reforms.json")
    events_data = load_json(CONTENT_DIR / "templates" / "events.json")
    strings_data = load_json(CONTENT_DIR / "strings" / "es.json")
    target_config = load_json(CONTENT_DIR / "rules" / "target_config.json")

    errors: list[str] = []

    # 1) IDs únicos
    ig_ids = set(ensure_unique_ids(igs_data.get("igs", []), "core/igs.json:igs"))
    movement_ids = set(ensure_unique_ids(movements_data.get("movements", []), "core/movements.json:movements"))
    ensure_unique_ids(regions_data.get("regions", []), "core/regions.json:regions")
    effect_ids = set(ensure_unique_ids(effects_data.get("effects", []), "templates/effects.json:effects"))
    reform_ids = set(ensure_unique_ids(reforms_data.get("reforms", []), "templates/reforms.json:reforms"))
    event_ids = set(ensure_unique_ids(events_data.get("events", []), "templates/events.json:events"))

    # 2) referencias cruzadas
    movement_tags = {
        tag
        for m in movements_data.get("movements", [])
        for tag in m.get("tags", [])
        if isinstance(tag, str)
    }
    string_keys = set(strings_data.keys())

    for reform in reforms_data.get("reforms", []):
        rid = reform["id"]
        for ig_id in reform.get("igs_stance", {}).keys():
            if ig_id not in ig_ids:
                errors.append(f"reform {rid}: ig_stance referencia IG inexistente: {ig_id}")
        for tag in reform.get("movement_tags", []):
            if tag not in movement_tags:
                errors.append(f"reform {rid}: movement_tag no encontrado en core/movements: {tag}")
        for eff in reform.get("on_pass_effects", []):
            tid = eff.get("template_id")
            if tid and tid not in effect_ids:
                errors.append(f"reform {rid}: on_pass_effects referencia effect inexistente: {tid}")
        for key in ("loc_title", "loc_desc"):
            loc = reform.get(key)
            if isinstance(loc, str) and loc not in string_keys:
                errors.append(f"reform {rid}: string faltante en es.json para {key}: {loc}")

    for event in events_data.get("events", []):
        eid = event["id"]
        movement_id = event.get("movement_id")
        if movement_id and movement_id not in movement_ids:
            errors.append(f"event {eid}: movement_id inexistente: {movement_id}")

        if isinstance(event.get("loc_title"), str) and event["loc_title"] not in string_keys:
            errors.append(f"event {eid}: string faltante en es.json para loc_title: {event['loc_title']}")
        if isinstance(event.get("loc_desc"), str) and event["loc_desc"] not in string_keys:
            errors.append(f"event {eid}: string faltante en es.json para loc_desc: {event['loc_desc']}")

        option_ids: list[str] = []
        for option in event.get("options", []):
            oid = option.get("id")
            if isinstance(oid, str):
                option_ids.append(oid)
            for eff in option.get("effects", []):
                tid = eff.get("template_id")
                if tid and tid not in effect_ids:
                    errors.append(f"event {eid}/{oid}: effect inexistente: {tid}")

        dups = sorted({x for x in option_ids if option_ids.count(x) > 1})
        if dups:
            errors.append(f"event {eid}: option IDs duplicados: {dups}")

    # Comprobar manifest references
    manifest_files: dict[str, str] = manifest.get("files", {})
    for rel_path in manifest_files.keys():
        if not (CONTENT_DIR / rel_path).exists():
            errors.append(f"manifest.json referencia archivo inexistente: {rel_path}")

    # 3) rangos S (métricas)
    exact_ranges, default_range = collect_metric_ranges(target_config)

    for effect in effects_data.get("effects", []):
        eid = effect["id"]
        for i, mod in enumerate(effect.get("mods", []), start=1):
            target = mod.get("target")
            value_s = mod.get("valueS")
            op = mod.get("op")
            if isinstance(target, str) and isinstance(value_s, int) and op == "SET":
                errors.extend(
                    check_target_value(target, value_s, exact_ranges, default_range, f"effect {eid} mod#{i}")
                )

    # valueS explícitos en prereqs de reformas
    for reform in reforms_data.get("reforms", []):
        rid = reform["id"]
        for i, prereq in enumerate(reform.get("prereqs", []), start=1):
            target = prereq.get("target")
            value_s = prereq.get("valueS")
            if isinstance(target, str) and isinstance(value_s, int):
                errors.extend(
                    check_target_value(target, value_s, exact_ranges, default_range, f"reform {rid} prereq#{i}")
                )

    if errors:
        raise ValidationError("\n".join(f"- {e}" for e in errors))

    print(
        "OK: validación semántica aprobada "
        f"(igs={len(ig_ids)}, movements={len(movement_ids)}, reforms={len(reform_ids)}, events={len(event_ids)})."
    )


if __name__ == "__main__":
    try:
        validate()
    except ValidationError as exc:
        print("ERROR: validación semántica falló")
        print(exc)
        sys.exit(1)
