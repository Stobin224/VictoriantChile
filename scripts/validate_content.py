#!/usr/bin/env python3
"""Semantic validation for the content pack.

The validator intentionally stays dependency-free. It validates the current
content contract without implementing the future Unity runtime.
"""

from __future__ import annotations

import argparse
import json
import re
import sys
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Iterable

ROOT = Path(__file__).resolve().parents[1]
DEFAULT_CONTENT_DIR = ROOT / "Assets" / "StreamingAssets" / "content"

ID_RE = re.compile(r"^[a-z][a-z0-9_]*$")
TARGET_SEGMENT_RE = re.compile(r"^[A-Za-z][A-Za-z0-9_]*$")
SHA_RE = re.compile(r"^sha256:[0-9a-f]{64}$")
ALLOWED_REFORM_KIND = {"NORMAL", "CONSTITUTIONAL", "EXCEPTIONAL", "SPECIAL_CONSTITUTIONAL"}
ALLOWED_STAGE_KIND = {"WORK", "VOTE"}
ALLOWED_STAGE_CHAMBER = {"NONE", "LOWER", "UPPER", "BOTH"}
ALLOWED_PREREQ_TYPE = {"METRIC", "FLAG", "REFORM_STATUS", "MOVEMENT"}
ALLOWED_PREREQ_OP = {">=", ">", "<=", "<", "==", "!="}
ALLOWED_EFFECT_MOD_OP = {"ADD", "MUL", "SET"}
ALLOWED_ON_PASS_EFFECT_TYPE = {"MODIFIER"}
ALLOWED_EVENT_KIND = {"AUTO", "CHOICE", "CRISIS"}
ALLOWED_EVENT_SCOPE = {"NATIONAL", "REGION", "MULTI_REGION"}
ALLOWED_MACROZONE = {"NORTH", "CENTER", "SOUTH", "AUSTRAL"}
ALLOWED_AGG_PASS = {"INTERNAL_REVERSION", "METRIC_AGGREGATION", "DERIVED_INTERNALS"}
ALLOWED_EXPR_KIND = {"AVG", "COPY"}
ALLOWED_NORMALIZE_GROUPS = {"igs.clout_sum_100"}
STATIC_REGION_FIELDS = {"admin_capS", "industry_capS", "extractive_capS", "social_capS", "populationS"}
READ_ONLY_TARGET_CONTEXTS = {"selector", "condition", "aggregation_component", "legislative_ref"}


class ValidationError(Exception):
    pass


@dataclass(frozen=True)
class TargetRule:
    pattern: str
    scale: int
    min_s: int
    max_s: int
    default_s: int
    allow_ops: frozenset[str]
    index: int


@dataclass
class TargetCatalog:
    rules: list[TargetRule]
    metric_ids: set[str]
    region_ids: set[str]
    ig_ids: set[str]
    movement_ids: set[str]

    def resolve(self, target: str, *, allow_wildcard: bool = False) -> TargetRule | None:
        if "*" in target and not allow_wildcard:
            return None
        matches = [rule for rule in self.rules if pattern_matches(rule.pattern, target)]
        if not matches:
            return None
        return sorted(matches, key=lambda r: pattern_specificity(r.pattern), reverse=True)[0]

    def is_static_region_target(self, target: str) -> bool:
        parts = target.split(".")
        return len(parts) == 3 and parts[0] == "regions" and parts[2] in STATIC_REGION_FIELDS


def load_json(path: Path) -> Any:
    with path.open("r", encoding="utf-8") as f:
        return json.load(f)


def load_pack(content_dir: Path) -> dict[str, Any]:
    return {
        "manifest": load_json(content_dir / "manifest.json"),
        "igs": load_json(content_dir / "core" / "igs.json"),
        "movements": load_json(content_dir / "core" / "movements.json"),
        "regions": load_json(content_dir / "core" / "regions.json"),
        "effects": load_json(content_dir / "templates" / "effects.json"),
        "reforms": load_json(content_dir / "templates" / "reforms.json"),
        "events": load_json(content_dir / "templates" / "events.json"),
        "strings": load_json(content_dir / "strings" / "es.json"),
        "target_config": load_json(content_dir / "rules" / "target_config.json"),
        "aggregation_config": load_json(content_dir / "rules" / "aggregation_config.json"),
        "legislative_config": load_json(content_dir / "rules" / "legislative_config.json"),
    }


def as_errors(fn: Any, errors: list[str]) -> None:
    try:
        fn()
    except ValidationError as exc:
        errors.append(str(exc))


def validate_id(value: Any, context: str, errors: list[str]) -> str | None:
    if not isinstance(value, str) or not value:
        errors.append(f"{context}: id must be a non-empty string")
        return None
    if not ID_RE.fullmatch(value):
        errors.append(f"{context}: id must be ASCII snake_case: {value!r}")
        return None
    return value


def ensure_unique_ids(items: list[Any], label: str, errors: list[str], prefix: str | None = None) -> list[str]:
    ids: list[str] = []
    for i, item in enumerate(items):
        if not isinstance(item, dict):
            errors.append(f"{label}[{i}]: item must be an object")
            continue
        value = validate_id(item.get("id"), f"{label}[{i}]", errors)
        if value:
            if prefix and not value.startswith(prefix):
                errors.append(f"{label}[{i}]: id {value!r} must start with {prefix!r}")
            ids.append(value)
    duplicated = sorted({item_id for item_id in ids if ids.count(item_id) > 1})
    if duplicated:
        errors.append(f"{label}: duplicate ids: {duplicated}")
    return ids


def require_object(value: Any, context: str, errors: list[str]) -> dict[str, Any]:
    if not isinstance(value, dict):
        errors.append(f"{context}: must be an object")
        return {}
    return value


def require_list(value: Any, context: str, errors: list[str]) -> list[Any]:
    if not isinstance(value, list):
        errors.append(f"{context}: must be a list")
        return []
    return value


def add_enum_error(errors: list[str], value: Any, allowed: set[str], context: str) -> None:
    if value is None:
        errors.append(f"{context}: required value is missing")
    elif not isinstance(value, str) or value not in allowed:
        errors.append(f"{context}: invalid value {value!r}; allowed={sorted(allowed)}")


def is_plain_int(value: Any) -> bool:
    return type(value) is int


def validate_loc_keys(node: Any, string_keys: set[str], context: str, errors: list[str]) -> None:
    if isinstance(node, dict):
        for key, value in node.items():
            child_context = f"{context}.{key}"
            if key.startswith("loc_"):
                if not isinstance(value, str):
                    errors.append(f"{child_context}: loc_* value must be a string")
                elif value not in string_keys:
                    errors.append(f"{child_context}: missing localization key {value!r}")
            validate_loc_keys(value, string_keys, child_context, errors)
    elif isinstance(node, list):
        for i, item in enumerate(node):
            validate_loc_keys(item, string_keys, f"{context}[{i}]", errors)


def pattern_specificity(pattern: str) -> tuple[int, int, int]:
    parts = pattern.split(".")
    literal_count = sum(1 for part in parts if part != "*")
    return (literal_count, len(parts), -pattern.count("*"))


def pattern_matches(pattern: str, target: str) -> bool:
    pattern_parts = pattern.split(".")
    target_parts = target.split(".")
    if len(pattern_parts) != len(target_parts):
        return False
    return all(pp == "*" or pp == tp for pp, tp in zip(pattern_parts, target_parts))


def validate_target_shape(target: Any, context: str, errors: list[str], *, allow_wildcard: bool) -> bool:
    if not isinstance(target, str) or not target:
        errors.append(f"{context}: target must be a non-empty string")
        return False
    if "*" in target and not allow_wildcard:
        errors.append(f"{context}: wildcard is only allowed in selector/config contexts: {target}")
        return False
    parts = target.split(".")
    if any(not part for part in parts):
        errors.append(f"{context}: target has an empty segment: {target}")
        return False
    for part in parts:
        if part == "*":
            continue
        if not TARGET_SEGMENT_RE.fullmatch(part):
            errors.append(f"{context}: target segment must be ASCII identifier-like text: {target}")
            return False
    ns = parts[0]
    expected = {"metrics": 2, "regions": 3, "igs": 3, "movements": 3, "internals": 3}
    if ns not in expected:
        errors.append(f"{context}: unknown target namespace {ns!r}: {target}")
        return False
    if len(parts) != expected[ns]:
        errors.append(f"{context}: target {target!r} must have {expected[ns]} segments")
        return False
    return True


def validate_target_reference(
    target: Any,
    catalog: TargetCatalog,
    context: str,
    errors: list[str],
    *,
    allow_wildcard: bool,
    context_kind: str,
    op: str | None = None,
    value_s: int | None = None,
) -> TargetRule | None:
    if not validate_target_shape(target, context, errors, allow_wildcard=allow_wildcard):
        return None
    assert isinstance(target, str)
    parts = target.split(".")
    if "*" not in parts:
        if parts[0] == "metrics" and parts[1] not in catalog.metric_ids:
            errors.append(f"{context}: metrics target is not explicitly declared: {target}")
            return None
        if parts[0] == "regions" and parts[1] not in catalog.region_ids:
            errors.append(f"{context}: region id does not exist: {parts[1]}")
            return None
        if parts[0] == "igs" and parts[1] not in catalog.ig_ids:
            errors.append(f"{context}: IG id does not exist: {parts[1]}")
            return None
        if parts[0] == "movements" and parts[1] not in catalog.movement_ids:
            errors.append(f"{context}: movement id does not exist: {parts[1]}")
            return None
    if catalog.is_static_region_target(target):
        if context_kind not in READ_ONLY_TARGET_CONTEXTS:
            errors.append(f"{context}: static regional resource is read-only and cannot be mutated: {target}")
        return None
    rule = catalog.resolve(target, allow_wildcard=allow_wildcard)
    if rule is None:
        errors.append(f"{context}: target does not resolve against TargetConfig: {target}")
        return None
    if op is not None and op not in rule.allow_ops:
        errors.append(f"{context}: op {op!r} is not allowed for {target}; allowed={sorted(rule.allow_ops)}")
    if op == "SET" and value_s is not None and not (rule.min_s <= value_s <= rule.max_s):
        errors.append(f"{context}: SET valueS={value_s} outside range [{rule.min_s}, {rule.max_s}] for {target}")
    return rule


def validate_manifest(manifest: Any, content_dir: Path, errors: list[str]) -> None:
    manifest_obj = require_object(manifest, "manifest.json", errors)
    for field in ("content_pack_id", "default_language"):
        if not isinstance(manifest_obj.get(field), str) or not manifest_obj.get(field):
            errors.append(f"manifest.json: {field} must be a non-empty string")
    for field in ("content_pack_version", "content_schema_version", "min_game_schema_version"):
        if not is_plain_int(manifest_obj.get(field)) or manifest_obj.get(field) < 1:
            errors.append(f"manifest.json: {field} must be a positive integer")
    languages = manifest_obj.get("languages")
    if not isinstance(languages, list) or not languages or not all(isinstance(x, str) and x for x in languages):
        errors.append("manifest.json: languages must be a non-empty list of strings")
    files = manifest_obj.get("files")
    if not isinstance(files, dict) or not files:
        errors.append("manifest.json: files must be a non-empty object")
        return
    for rel_path, digest in files.items():
        if not isinstance(rel_path, str) or not rel_path or Path(rel_path).is_absolute():
            errors.append(f"manifest.json: invalid relative file path {rel_path!r}")
            continue
        resolved = (content_dir / rel_path).resolve()
        try:
            resolved.relative_to(content_dir.resolve())
        except ValueError:
            errors.append(f"manifest.json: file path escapes content dir: {rel_path}")
            continue
        if not resolved.exists():
            errors.append(f"manifest.json: referenced file does not exist: {rel_path}")
        if not isinstance(digest, str) or not SHA_RE.fullmatch(digest):
            errors.append(f"manifest.json: invalid sha256 digest for {rel_path}: {digest!r}")


def validate_target_config(target_config: Any, ids: dict[str, set[str]], string_keys: set[str], errors: list[str]) -> TargetCatalog:
    rows = require_list(target_config, "rules/target_config.json", errors)
    rules: list[TargetRule] = []
    patterns: list[str] = []
    for i, row_any in enumerate(rows):
        row = require_object(row_any, f"target_config[{i}]", errors)
        pattern = row.get("pattern")
        if not isinstance(pattern, str):
            errors.append(f"target_config[{i}].pattern: must be a string")
            continue
        validate_target_shape(pattern, f"target_config[{i}].pattern", errors, allow_wildcard=True)
        patterns.append(pattern)
        scale = row.get("scale")
        min_s = row.get("minS")
        max_s = row.get("maxS")
        default_s = row.get("defaultS")
        if not is_plain_int(scale) or scale <= 0:
            errors.append(f"target_config[{i}].scale: must be a positive integer")
            scale = 1
        for field_name, value in (("minS", min_s), ("maxS", max_s), ("defaultS", default_s)):
            if not is_plain_int(value):
                errors.append(f"target_config[{i}].{field_name}: must be an integer")
        if is_plain_int(min_s) and is_plain_int(max_s) and is_plain_int(default_s):
            if min_s > max_s:
                errors.append(f"target_config[{i}]: minS must be <= maxS")
            elif not (min_s <= default_s <= max_s):
                errors.append(f"target_config[{i}]: defaultS must be within minS/maxS")
        allow_ops = row.get("allow_ops")
        if not isinstance(allow_ops, list) or not allow_ops:
            errors.append(f"target_config[{i}].allow_ops: must be a non-empty list")
            allow_set: set[str] = set()
        else:
            allow_set = {op for op in allow_ops if isinstance(op, str)}
            invalid = sorted(allow_set - ALLOWED_EFFECT_MOD_OP)
            if invalid or len(allow_set) != len(allow_ops):
                errors.append(f"target_config[{i}].allow_ops: invalid ops {invalid or allow_ops}")
        normalize_group = row.get("normalize_group")
        if normalize_group is not None and normalize_group not in ALLOWED_NORMALIZE_GROUPS:
            errors.append(f"target_config[{i}].normalize_group: invalid value {normalize_group!r}")
        ui = row.get("ui")
        if isinstance(ui, dict):
            label = ui.get("label")
            if isinstance(label, str) and label not in string_keys:
                errors.append(f"target_config[{i}].ui.label: missing localization key {label!r}")
            decimals = ui.get("decimals")
            if decimals is not None and (not is_plain_int(decimals) or decimals < 0):
                errors.append(f"target_config[{i}].ui.decimals: must be a non-negative integer")
        if isinstance(pattern, str):
            rules.append(
                TargetRule(
                    pattern=pattern,
                    scale=scale if is_plain_int(scale) else 1,
                    min_s=min_s if is_plain_int(min_s) else 0,
                    max_s=max_s if is_plain_int(max_s) else 0,
                    default_s=default_s if is_plain_int(default_s) else 0,
                    allow_ops=frozenset(allow_set),
                    index=i,
                )
            )
    duplicated = sorted({pattern for pattern in patterns if patterns.count(pattern) > 1})
    if duplicated:
        errors.append(f"target_config: duplicate patterns: {duplicated}")
    return TargetCatalog(
        rules=rules,
        metric_ids={r.pattern.split(".")[1] for r in rules if r.pattern.startswith("metrics.") and "*" not in r.pattern},
        region_ids=ids["regions"],
        ig_ids=ids["igs"],
        movement_ids=ids["movements"],
    )


def validate_core(pack: dict[str, Any], errors: list[str]) -> dict[str, set[str]]:
    igs = require_list(require_object(pack["igs"], "core/igs.json", errors).get("igs"), "core/igs.json.igs", errors)
    movements = require_list(
        require_object(pack["movements"], "core/movements.json", errors).get("movements"),
        "core/movements.json.movements",
        errors,
    )
    regions = require_list(
        require_object(pack["regions"], "core/regions.json", errors).get("regions"),
        "core/regions.json.regions",
        errors,
    )
    ig_ids = set(ensure_unique_ids(igs, "core/igs.json.igs", errors, "ig_"))
    movement_ids = set(ensure_unique_ids(movements, "core/movements.json.movements", errors, "mov_"))
    region_ids = set(ensure_unique_ids(regions, "core/regions.json.regions", errors))
    for ig in igs:
        if not isinstance(ig, dict):
            continue
        tags = ig.get("tags")
        if not isinstance(tags, list) or not (2 <= len(tags) <= 4) or not all(isinstance(t, str) for t in tags):
            errors.append(f"IG {ig.get('id')}: tags must contain 2-4 strings")
    total_weight = 0
    for region in regions:
        if not isinstance(region, dict):
            continue
        rid = region.get("id")
        if not isinstance(region.get("name"), str) or not region.get("name"):
            errors.append(f"region {rid}: name must be a non-empty string")
        weight = region.get("weight_ppm")
        if not is_plain_int(weight) or weight < 0:
            errors.append(f"region {rid}: weight_ppm must be a non-negative integer")
        else:
            total_weight += weight
        add_enum_error(errors, region.get("macrozone"), ALLOWED_MACROZONE, f"region {rid}.macrozone")
        for field in STATIC_REGION_FIELDS:
            value = region.get(field)
            if value is not None and (not is_plain_int(value) or not (0 <= value <= 10000)):
                errors.append(f"region {rid}.{field}: must be an integer in 0..10000")
    if regions and total_weight != 1_000_000:
        errors.append(f"core/regions.json: weight_ppm sum must be 1000000, got {total_weight}")
    return {"igs": ig_ids, "movements": movement_ids, "regions": region_ids}


def collect_movement_tags(movements_data: dict[str, Any]) -> set[str]:
    return {
        tag
        for movement in movements_data.get("movements", [])
        if isinstance(movement, dict)
        for tag in movement.get("tags", [])
        if isinstance(tag, str)
    }


def validate_effect_mods(
    mods: Any,
    catalog: TargetCatalog,
    context: str,
    errors: list[str],
    *,
    mutation: bool = True,
) -> None:
    for i, mod_any in enumerate(require_list(mods, f"{context}.mods", errors), start=1):
        mod = require_object(mod_any, f"{context}.mods[{i}]", errors)
        target = mod.get("target")
        op = mod.get("op")
        add_enum_error(errors, op, ALLOWED_EFFECT_MOD_OP, f"{context}.mods[{i}].op")
        value_s = mod.get("valueS")
        if not is_plain_int(value_s):
            errors.append(f"{context}.mods[{i}].valueS: must be an integer")
            value_s = None
        if not isinstance(mod.get("is_per_tick"), bool):
            errors.append(f"{context}.mods[{i}].is_per_tick: must be a boolean")
        validate_target_reference(
            target,
            catalog,
            f"{context}.mods[{i}].target",
            errors,
            allow_wildcard=not mutation,
            context_kind="mutation" if mutation else "selector",
            op=op if isinstance(op, str) else None,
            value_s=value_s,
        )


def validate_condition_node(node: Any, catalog: TargetCatalog, movement_ids: set[str], context: str, errors: list[str]) -> None:
    if isinstance(node, dict):
        if "cmp" in node:
            cmp_obj = require_object(node["cmp"], f"{context}.cmp", errors)
            op = cmp_obj.get("op")
            add_enum_error(errors, op, ALLOWED_PREREQ_OP, f"{context}.cmp.op")
            value = cmp_obj.get("valueS", cmp_obj.get("value"))
            if not is_plain_int(value):
                errors.append(f"{context}.cmp.value: must be an integer")
            validate_target_reference(
                cmp_obj.get("target"),
                catalog,
                f"{context}.cmp.target",
                errors,
                allow_wildcard=False,
                context_kind="condition",
            )
        if "movement_cmp" in node:
            cmp_obj = require_object(node["movement_cmp"], f"{context}.movement_cmp", errors)
            if cmp_obj.get("movement_id") not in movement_ids:
                errors.append(f"{context}.movement_cmp: movement_id does not exist: {cmp_obj.get('movement_id')}")
            add_enum_error(errors, cmp_obj.get("op"), ALLOWED_PREREQ_OP, f"{context}.movement_cmp.op")
            value = cmp_obj.get("valueS", cmp_obj.get("value"))
            if not is_plain_int(value):
                errors.append(f"{context}.movement_cmp.value: must be an integer")
        for key, value in node.items():
            if key not in {"cmp", "movement_cmp"}:
                validate_condition_node(value, catalog, movement_ids, f"{context}.{key}", errors)
    elif isinstance(node, list):
        for i, item in enumerate(node):
            validate_condition_node(item, catalog, movement_ids, f"{context}[{i}]", errors)


def validate_events(
    events_data: dict[str, Any],
    effect_ids: set[str],
    movement_ids: set[str],
    catalog: TargetCatalog,
    string_keys: set[str],
    errors: list[str],
) -> set[str]:
    events = require_list(events_data.get("events"), "templates/events.json.events", errors)
    event_ids = set(ensure_unique_ids(events, "templates/events.json.events", errors, "evt_"))
    for event in events:
        if not isinstance(event, dict):
            continue
        eid = event.get("id")
        validate_loc_keys(event, string_keys, f"event {eid}", errors)
        add_enum_error(errors, event.get("kind"), ALLOWED_EVENT_KIND, f"event {eid}.kind")
        add_enum_error(errors, event.get("scope"), ALLOWED_EVENT_SCOPE, f"event {eid}.scope")
        for field in ("blocking",):
            if not isinstance(event.get(field), bool):
                errors.append(f"event {eid}.{field}: must be a boolean")
        for field in ("base_priority", "weight", "cooldown_weeks", "max_per_campaign"):
            if not is_plain_int(event.get(field)):
                errors.append(f"event {eid}.{field}: must be an integer")
        if event.get("movement_id") and event.get("movement_id") not in movement_ids:
            errors.append(f"event {eid}: movement_id does not exist: {event.get('movement_id')}")
        for var_name, var in require_object(event.get("vars", {}), f"event {eid}.vars", errors).items():
            if isinstance(var, dict) and "target" in var:
                validate_target_reference(
                    var.get("target"),
                    catalog,
                    f"event {eid}.vars.{var_name}.target",
                    errors,
                    allow_wildcard=True,
                    context_kind="selector",
                )
        validate_condition_node(event.get("conditions", {}), catalog, movement_ids, f"event {eid}.conditions", errors)
        option_ids: list[str] = []
        for opt_i, option_any in enumerate(require_list(event.get("options"), f"event {eid}.options", errors), start=1):
            option = require_object(option_any, f"event {eid}.options[{opt_i}]", errors)
            oid = validate_id(option.get("id"), f"event {eid}.options[{opt_i}]", errors)
            if oid:
                option_ids.append(oid)
            validate_loc_keys(option, string_keys, f"event {eid}.option {oid}", errors)
            for eff_i, eff_any in enumerate(require_list(option.get("effects"), f"event {eid}/{oid}.effects", errors), start=1):
                eff = require_object(eff_any, f"event {eid}/{oid}.effects[{eff_i}]", errors)
                add_enum_error(errors, eff.get("type"), ALLOWED_ON_PASS_EFFECT_TYPE, f"event {eid}/{oid}.effects[{eff_i}].type")
                if eff.get("template_id") not in effect_ids:
                    errors.append(f"event {eid}/{oid}: effect does not exist: {eff.get('template_id')}")
                duration = eff.get("duration_weeks")
                if not is_plain_int(duration) or duration <= 0:
                    errors.append(f"event {eid}/{oid}.effects[{eff_i}].duration_weeks: must be a positive integer")
            for followup_i, followup_any in enumerate(option.get("followups", []), start=1):
                followup = require_object(followup_any, f"event {eid}/{oid}.followups[{followup_i}]", errors)
                if followup.get("event_id") not in event_ids:
                    errors.append(f"event {eid}/{oid}.followups[{followup_i}]: event_id does not exist: {followup.get('event_id')}")
                if not is_plain_int(followup.get("after_weeks")) or followup.get("after_weeks") <= 0:
                    errors.append(f"event {eid}/{oid}.followups[{followup_i}].after_weeks: must be a positive integer")
        duplicated = sorted({oid for oid in option_ids if option_ids.count(oid) > 1})
        if duplicated:
            errors.append(f"event {eid}: duplicate option ids: {duplicated}")
        auto_option_id = event.get("auto_option_id")
        if auto_option_id is not None and auto_option_id not in option_ids:
            errors.append(f"event {eid}: auto_option_id does not exist in options: {auto_option_id}")
    return event_ids


def validate_reforms(
    reforms_data: dict[str, Any],
    effect_ids: set[str],
    ig_ids: set[str],
    movement_tags: set[str],
    catalog: TargetCatalog,
    string_keys: set[str],
    errors: list[str],
) -> set[str]:
    reforms = require_list(reforms_data.get("reforms"), "templates/reforms.json.reforms", errors)
    reform_ids = set(ensure_unique_ids(reforms, "templates/reforms.json.reforms", errors, "ref_"))
    for reform in reforms:
        if not isinstance(reform, dict):
            continue
        rid = reform.get("id")
        validate_loc_keys(reform, string_keys, f"reform {rid}", errors)
        add_enum_error(errors, reform.get("kind"), ALLOWED_REFORM_KIND, f"reform {rid}.kind")
        for ig_id, stance in require_object(reform.get("igs_stance"), f"reform {rid}.igs_stance", errors).items():
            if ig_id not in ig_ids:
                errors.append(f"reform {rid}: igs_stance references missing IG: {ig_id}")
            if not is_plain_int(stance) or not (-100 <= stance <= 100):
                errors.append(f"reform {rid}: igs_stance[{ig_id}] must be integer in -100..100")
        for tag in reform.get("movement_tags", []):
            if tag not in movement_tags:
                errors.append(f"reform {rid}: movement_tag not found in core/movements: {tag}")
        for field in ("cooldown_weeks", "max_per_campaign", "base_difficultyS"):
            if not is_plain_int(reform.get(field)):
                errors.append(f"reform {rid}.{field}: must be an integer")
        for eff_i, eff_any in enumerate(reform.get("on_pass_effects", []), start=1):
            eff = require_object(eff_any, f"reform {rid}.on_pass_effects[{eff_i}]", errors)
            add_enum_error(errors, eff.get("type"), ALLOWED_ON_PASS_EFFECT_TYPE, f"reform {rid}.on_pass_effects[{eff_i}].type")
            if eff.get("template_id") not in effect_ids:
                errors.append(f"reform {rid}: on_pass_effect references missing effect: {eff.get('template_id')}")
            if not is_plain_int(eff.get("duration_weeks")) or eff.get("duration_weeks") <= 0:
                errors.append(f"reform {rid}.on_pass_effects[{eff_i}].duration_weeks: must be a positive integer")
        for prereq_i, prereq_any in enumerate(reform.get("prereqs", []), start=1):
            prereq = require_object(prereq_any, f"reform {rid}.prereqs[{prereq_i}]", errors)
            prereq_type = prereq.get("type")
            add_enum_error(errors, prereq_type, ALLOWED_PREREQ_TYPE, f"reform {rid}.prereqs[{prereq_i}].type")
            add_enum_error(errors, prereq.get("op"), ALLOWED_PREREQ_OP, f"reform {rid}.prereqs[{prereq_i}].op")
            value_s = prereq.get("valueS")
            if prereq_type == "METRIC":
                if not is_plain_int(value_s):
                    errors.append(f"reform {rid}.prereqs[{prereq_i}].valueS: must be an integer")
                    value_s = None
                validate_target_reference(
                    prereq.get("target"),
                    catalog,
                    f"reform {rid}.prereqs[{prereq_i}].target",
                    errors,
                    allow_wildcard=False,
                    context_kind="condition",
                    value_s=value_s,
                )
        stage_ids: list[str] = []
        for stage_i, stage_any in enumerate(require_list(reform.get("stages"), f"reform {rid}.stages", errors), start=1):
            stage = require_object(stage_any, f"reform {rid}.stages[{stage_i}]", errors)
            sid = validate_id(stage.get("id"), f"reform {rid}.stages[{stage_i}]", errors)
            if sid:
                stage_ids.append(sid)
            add_enum_error(errors, stage.get("kind"), ALLOWED_STAGE_KIND, f"reform {rid}.stages[{stage_i}].kind")
            add_enum_error(errors, stage.get("chamber"), ALLOWED_STAGE_CHAMBER, f"reform {rid}.stages[{stage_i}].chamber")
            if not is_plain_int(stage.get("weightS")):
                errors.append(f"reform {rid}.stages[{stage_i}].weightS: must be an integer")
        duplicated = sorted({sid for sid in stage_ids if stage_ids.count(sid) > 1})
        if duplicated:
            errors.append(f"reform {rid}: duplicate stage ids: {duplicated}")
    return reform_ids


def validate_aggregation_config(config: Any, catalog: TargetCatalog, errors: list[str]) -> None:
    root = require_object(config, "rules/aggregation_config.json", errors)
    passes = root.get("passes")
    if not isinstance(passes, list) or not passes:
        errors.append("aggregation_config: passes must be a non-empty list")
        return
    for pass_i, pass_any in enumerate(passes, start=1):
        agg_pass = require_object(pass_any, f"aggregation_config.passes[{pass_i}]", errors)
        pass_type = agg_pass.get("type")
        add_enum_error(errors, pass_type, ALLOWED_AGG_PASS, f"aggregation_config.passes[{pass_i}].type")
        if pass_type == "INTERNAL_REVERSION":
            for group_i, group_any in enumerate(agg_pass.get("groups", []), start=1):
                group = require_object(group_any, f"aggregation_config.passes[{pass_i}].groups[{group_i}]", errors)
                validate_target_reference(
                    group.get("pattern"),
                    catalog,
                    f"aggregation_config.passes[{pass_i}].groups[{group_i}].pattern",
                    errors,
                    allow_wildcard=True,
                    context_kind="selector",
                )
                for field in ("half_life_weeks", "alpha_ppm"):
                    if not is_plain_int(group.get(field)) or group.get(field) <= 0:
                        errors.append(f"aggregation_config.passes[{pass_i}].groups[{group_i}].{field}: must be a positive integer")
            for target in agg_pass.get("skip_targets", []):
                validate_target_reference(
                    target,
                    catalog,
                    f"aggregation_config.passes[{pass_i}].skip_targets",
                    errors,
                    allow_wildcard=False,
                    context_kind="selector",
                )
        elif pass_type == "METRIC_AGGREGATION":
            for metric_i, metric_any in enumerate(agg_pass.get("metrics", []), start=1):
                metric = require_object(metric_any, f"aggregation_config.passes[{pass_i}].metrics[{metric_i}]", errors)
                validate_target_reference(
                    metric.get("metric"),
                    catalog,
                    f"aggregation_config.passes[{pass_i}].metrics[{metric_i}].metric",
                    errors,
                    allow_wildcard=False,
                    context_kind="mutation",
                )
                for field in ("half_life_weeks", "alpha_ppm", "cap_per_weekS"):
                    if not is_plain_int(metric.get(field)) or metric.get(field) <= 0:
                        errors.append(f"aggregation_config.passes[{pass_i}].metrics[{metric_i}].{field}: must be a positive integer")
                for comp_i, comp_any in enumerate(metric.get("components", []), start=1):
                    comp = require_object(comp_any, f"aggregation_config.passes[{pass_i}].metrics[{metric_i}].components[{comp_i}]", errors)
                    validate_target_reference(
                        comp.get("target"),
                        catalog,
                        f"aggregation_config.passes[{pass_i}].metrics[{metric_i}].components[{comp_i}].target",
                        errors,
                        allow_wildcard=False,
                        context_kind="aggregation_component",
                    )
                    if not is_plain_int(comp.get("weight_ppm")):
                        errors.append(f"aggregation_config.passes[{pass_i}].metrics[{metric_i}].components[{comp_i}].weight_ppm: must be an integer")
        elif pass_type == "DERIVED_INTERNALS":
            for rule_i, rule_any in enumerate(agg_pass.get("rules", []), start=1):
                rule = require_object(rule_any, f"aggregation_config.passes[{pass_i}].rules[{rule_i}]", errors)
                op = rule.get("op")
                add_enum_error(errors, op, ALLOWED_EFFECT_MOD_OP, f"aggregation_config.passes[{pass_i}].rules[{rule_i}].op")
                validate_target_reference(
                    rule.get("target"),
                    catalog,
                    f"aggregation_config.passes[{pass_i}].rules[{rule_i}].target",
                    errors,
                    allow_wildcard=False,
                    context_kind="mutation",
                    op=op if isinstance(op, str) else None,
                )
                expr = require_object(rule.get("expr"), f"aggregation_config.passes[{pass_i}].rules[{rule_i}].expr", errors)
                add_enum_error(errors, expr.get("kind"), ALLOWED_EXPR_KIND, f"aggregation_config.passes[{pass_i}].rules[{rule_i}].expr.kind")
                if "target" in expr:
                    validate_target_reference(expr.get("target"), catalog, f"aggregation_config.passes[{pass_i}].rules[{rule_i}].expr.target", errors, allow_wildcard=False, context_kind="selector")
                for target in expr.get("targets", []):
                    validate_target_reference(target, catalog, f"aggregation_config.passes[{pass_i}].rules[{rule_i}].expr.targets", errors, allow_wildcard=False, context_kind="selector")


def iter_target_like_entries(node: Any, context: str) -> Iterable[tuple[str, Any, str]]:
    if isinstance(node, dict):
        for key, value in node.items():
            child = f"{context}.{key}"
            if key in {"target", "metric"}:
                yield child, value, "target"
            elif key.endswith("_target_pattern") or key == "pattern":
                yield child, value, "pattern"
            for item in iter_target_like_entries(value, child):
                yield item
    elif isinstance(node, list):
        for i, value in enumerate(node):
            yield from iter_target_like_entries(value, f"{context}[{i}]")


def validate_legislative_config(config: Any, catalog: TargetCatalog, errors: list[str]) -> None:
    root = require_object(config, "rules/legislative_config.json", errors)
    for field in ("schema_version", "scale", "midS"):
        if not is_plain_int(root.get(field)):
            errors.append(f"legislative_config.{field}: must be an integer")
    for context, value, kind in iter_target_like_entries(root, "legislative_config"):
        if isinstance(value, str) and value.startswith(("metrics.", "regions.", "igs.", "movements.", "internals.")):
            validate_target_reference(
                value,
                catalog,
                context,
                errors,
                allow_wildcard=kind == "pattern",
                context_kind="legislative_ref",
            )


def validate_content(content_dir: Path = DEFAULT_CONTENT_DIR) -> list[str]:
    content_dir = content_dir.resolve()
    errors: list[str] = []
    try:
        pack = load_pack(content_dir)
    except (OSError, json.JSONDecodeError) as exc:
        return [f"failed to load content pack: {exc}"]

    string_keys = set(pack["strings"].keys()) if isinstance(pack["strings"], dict) else set()
    validate_manifest(pack["manifest"], content_dir, errors)
    ids = validate_core(pack, errors)
    catalog = validate_target_config(pack["target_config"], ids, string_keys, errors)
    movement_tags = collect_movement_tags(pack["movements"])

    effects = require_list(require_object(pack["effects"], "templates/effects.json", errors).get("effects"), "templates/effects.json.effects", errors)
    effect_ids = set(ensure_unique_ids(effects, "templates/effects.json.effects", errors, "eff_"))
    for effect in effects:
        if not isinstance(effect, dict):
            continue
        eid = effect.get("id")
        validate_loc_keys(effect, string_keys, f"effect {eid}", errors)
        validate_effect_mods(effect.get("mods"), catalog, f"effect {eid}", errors)

    validate_reforms(pack["reforms"], effect_ids, ids["igs"], movement_tags, catalog, string_keys, errors)
    validate_events(pack["events"], effect_ids, ids["movements"], catalog, string_keys, errors)
    validate_aggregation_config(pack["aggregation_config"], catalog, errors)
    validate_legislative_config(pack["legislative_config"], catalog, errors)
    return errors


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--content-dir", type=Path, default=DEFAULT_CONTENT_DIR)
    args = parser.parse_args(argv)

    errors = validate_content(args.content_dir)
    if errors:
        print("ERROR: semantic content validation failed")
        for error in errors:
            print(f"- {error}")
        return 1
    print("OK: semantic content validation passed.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
