#!/usr/bin/env python3
"""Content/runtime contract smoke.

This is not the Unity runtime and does not claim to run real ticks. It exercises
the data contract that the future runtime is expected to consume.
"""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path
from typing import Any

ROOT = Path(__file__).resolve().parents[1]
DEFAULT_CONTENT_DIR = ROOT / "Assets" / "StreamingAssets" / "content"

if str(ROOT) not in sys.path:
    sys.path.insert(0, str(ROOT))

from scripts.validate_content import TargetCatalog, validate_content, validate_target_reference  # noqa: E402


def load_json(path: Path) -> Any:
    with path.open("r", encoding="utf-8") as f:
        return json.load(f)


def clamp(value: int, lo: int, hi: int) -> int:
    return max(lo, min(hi, value))


def mul_scaled(value: int, factor_s: int, scale: int) -> int:
    denominator = 100 * scale
    numerator = value * factor_s
    if numerator >= 0:
        return (numerator + denominator // 2) // denominator
    return -((-numerator + denominator // 2) // denominator)


def build_catalog(content_dir: Path) -> TargetCatalog:
    from scripts.validate_content import load_pack, validate_core, validate_target_config

    errors: list[str] = []
    pack = load_pack(content_dir)
    ids = validate_core(pack, errors)
    strings = pack["strings"] if isinstance(pack["strings"], dict) else {}
    catalog = validate_target_config(pack["target_config"], ids, set(strings.keys()), errors)
    if errors:
        raise ValueError("\n".join(errors))
    return catalog


def concrete_targets_for_catalog(catalog: TargetCatalog) -> set[str]:
    targets: set[str] = set()
    for rule in catalog.rules:
        parts = rule.pattern.split(".")
        if "*" not in parts:
            targets.add(rule.pattern)
        elif parts[0] == "regions":
            for region_id in catalog.region_ids:
                targets.add(f"regions.{region_id}.{parts[2]}")
        elif parts[0] == "igs":
            for ig_id in catalog.ig_ids:
                targets.add(f"igs.{ig_id}.{parts[2]}")
        elif parts[0] == "movements":
            for movement_id in catalog.movement_ids:
                targets.add(f"movements.{movement_id}.{parts[2]}")
    return targets


def apply_mod(initial: int, op: str, value_s: int, lo: int, hi: int, scale: int) -> int:
    if op == "ADD":
        result = initial + value_s
    elif op == "SET":
        result = value_s
    elif op == "MUL":
        result = mul_scaled(initial, value_s, scale)
    else:
        raise ValueError(f"unsupported op {op}")
    return clamp(result, lo, hi)


def smoke(content_dir: Path = DEFAULT_CONTENT_DIR) -> tuple[int, str]:
    content_dir = content_dir.resolve()
    validation_errors = validate_content(content_dir)
    if validation_errors:
        return 1, "semantic validation failed before smoke:\n" + "\n".join(f"- {e}" for e in validation_errors)

    catalog = build_catalog(content_dir)
    manifest = load_json(content_dir / "manifest.json")
    effects = load_json(content_dir / "templates" / "effects.json")
    aggregation = load_json(content_dir / "rules" / "aggregation_config.json")

    if not isinstance(manifest.get("files"), dict) or not manifest["files"]:
        return 1, "manifest has no declared files"
    for rel_path in manifest["files"]:
        if not (content_dir / rel_path).exists():
            return 1, f"manifest file does not exist: {rel_path}"

    if not isinstance(aggregation, dict) or not isinstance(aggregation.get("passes"), list) or not aggregation["passes"]:
        return 1, "aggregation_config must be an object with a non-empty passes list"
    passes = aggregation["passes"]

    state: dict[str, int] = {}
    for target in concrete_targets_for_catalog(catalog):
        rule = catalog.resolve(target)
        if rule:
            state[target] = rule.default_s

    target_count = len(state)
    modifier_count = 0
    for effect in effects.get("effects", []):
        if not isinstance(effect, dict):
            return 1, "effect item must be an object"
        for mod in effect.get("mods", []):
            modifier_count += 1
            target = mod.get("target")
            op = mod.get("op")
            value_s = mod.get("valueS")
            errors: list[str] = []
            rule = validate_target_reference(
                target,
                catalog,
                f"effect {effect.get('id')} mod",
                errors,
                allow_wildcard=False,
                context_kind="mutation",
                op=op if isinstance(op, str) else None,
                value_s=value_s if isinstance(value_s, int) else None,
            )
            if errors or rule is None:
                return 1, "\n".join(errors)
            if not isinstance(value_s, int) or not isinstance(op, str):
                return 1, f"effect {effect.get('id')} has invalid op/valueS"
            initial = state.get(target, rule.default_s)
            result = apply_mod(initial, op, value_s, rule.min_s, rule.max_s, rule.scale)
            if not isinstance(result, int) or not (rule.min_s <= result <= rule.max_s):
                return 1, f"effect {effect.get('id')} produced invalid result for {target}: {result}"

    exercised_passes = 0
    for pass_index, agg_pass in enumerate(passes, start=1):
        if not isinstance(agg_pass, dict):
            return 1, f"aggregation pass #{pass_index} must be an object"
        pass_type = agg_pass.get("type")
        exercised_passes += 1
        if pass_type == "INTERNAL_REVERSION":
            for group in agg_pass.get("groups", []):
                pattern = group.get("pattern")
                errors: list[str] = []
                validate_target_reference(pattern, catalog, f"aggregation pass #{pass_index} group", errors, allow_wildcard=True, context_kind="selector")
                if errors:
                    return 1, "\n".join(errors)
        elif pass_type == "METRIC_AGGREGATION":
            for metric in agg_pass.get("metrics", []):
                errors: list[str] = []
                validate_target_reference(metric.get("metric"), catalog, f"aggregation pass #{pass_index} metric", errors, allow_wildcard=False, context_kind="mutation")
                if errors:
                    return 1, "\n".join(errors)
                components = metric.get("components")
                if not isinstance(components, list) or not components:
                    return 1, f"aggregation pass #{pass_index} metric has no components"
                for component in components:
                    errors = []
                    validate_target_reference(component.get("target"), catalog, f"aggregation pass #{pass_index} component", errors, allow_wildcard=False, context_kind="aggregation_component")
                    if errors:
                        return 1, "\n".join(errors)
        elif pass_type == "DERIVED_INTERNALS":
            for rule in agg_pass.get("rules", []):
                errors = []
                validate_target_reference(rule.get("target"), catalog, f"aggregation pass #{pass_index} derived target", errors, allow_wildcard=False, context_kind="mutation", op=rule.get("op"))
                expr = rule.get("expr", {})
                if isinstance(expr, dict):
                    if "target" in expr:
                        validate_target_reference(expr.get("target"), catalog, f"aggregation pass #{pass_index} expr target", errors, allow_wildcard=False, context_kind="selector")
                    for target in expr.get("targets", []):
                        validate_target_reference(target, catalog, f"aggregation pass #{pass_index} expr targets", errors, allow_wildcard=False, context_kind="selector")
                if errors:
                    return 1, "\n".join(errors)
        else:
            return 1, f"unknown aggregation pass type: {pass_type}"

    effect_count = len(effects.get("effects", []))
    return (
        0,
        "OK: content/runtime contract smoke passed "
        f"(targets={target_count}, effects={effect_count}, modifiers={modifier_count}, aggregation_passes={exercised_passes}).",
    )


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--content-dir", type=Path, default=DEFAULT_CONTENT_DIR)
    args = parser.parse_args(argv)
    code, message = smoke(args.content_dir)
    print(message)
    return code


if __name__ == "__main__":
    sys.exit(main())
