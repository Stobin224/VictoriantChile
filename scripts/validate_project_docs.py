#!/usr/bin/env python3
"""Valida autoridad, enlaces y evidencia de la documentación canónica."""

from __future__ import annotations

import re
import sys
from pathlib import Path
from urllib.parse import unquote

ROOT = Path(__file__).resolve().parents[1]
DOCS = ROOT / "docs"
CONTRACTS = DOCS / "contracts"
DECISIONS = DOCS / "decisions"
INCREMENTS = DOCS / "implementation" / "increments"
STATUS = DOCS / "implementation" / "STATUS.md"

LINK_RE = re.compile(r"(?<!!)\[[^\]]+\]\(([^)]+)\)")
CONTRACT_ID_RE = re.compile(r"\bCON-[A-Z0-9-]+\b")
ADR_ID_RE = re.compile(r"\bADR-\d{4}\b")
FRONT_MATTER_RE = re.compile(r"\A---\s*\n(.*?)\n---\s*\n", re.DOTALL)


def markdown_files() -> list[Path]:
    files = sorted(ROOT.glob("*.md"))
    files.extend(sorted(DOCS.rglob("*.md")))
    return files


def parse_front_matter(path: Path) -> dict[str, str]:
    text = path.read_text(encoding="utf-8")
    match = FRONT_MATTER_RE.match(text)
    if not match:
        return {}

    values: dict[str, str] = {}
    for line in match.group(1).splitlines():
        if ":" not in line:
            continue
        key, value = line.split(":", 1)
        values[key.strip()] = value.strip()
    return values


def validate_links(files: list[Path]) -> list[str]:
    errors: list[str] = []
    for path in files:
        text = path.read_text(encoding="utf-8")
        for raw_target in LINK_RE.findall(text):
            target = raw_target.strip().strip("<>")
            if target.startswith(("http://", "https://", "mailto:", "#")):
                continue

            target = unquote(target.split("#", 1)[0])
            if not target:
                continue
            resolved = (ROOT / target.lstrip("/")) if target.startswith("/") else (path.parent / target)
            if not resolved.resolve().exists():
                errors.append(f"{path.relative_to(ROOT)}: enlace roto -> {raw_target}")
    return errors


def collect_canonical_contracts() -> tuple[dict[str, Path], list[str]]:
    contracts: dict[str, Path] = {}
    errors: list[str] = []
    for path in sorted(CONTRACTS.glob("*.md")):
        if path.name == "README.md":
            continue
        metadata = parse_front_matter(path)
        contract_id = metadata.get("id")
        authority = metadata.get("authority")
        if not contract_id or not CONTRACT_ID_RE.fullmatch(contract_id):
            errors.append(f"{path.relative_to(ROOT)}: falta id CON-* válido")
            continue
        if authority != "canonical":
            errors.append(f"{path.relative_to(ROOT)}: authority debe ser canonical")
        if contract_id in contracts:
            errors.append(
                f"Contrato canónico duplicado {contract_id}: "
                f"{contracts[contract_id].relative_to(ROOT)} y {path.relative_to(ROOT)}"
            )
        contracts[contract_id] = path
    return contracts, errors


def collect_adrs() -> tuple[dict[str, Path], list[str]]:
    adrs: dict[str, Path] = {}
    errors: list[str] = []
    for path in sorted(DECISIONS.glob("ADR-*.md")):
        metadata = parse_front_matter(path)
        adr_id = metadata.get("id")
        if not adr_id or not ADR_ID_RE.fullmatch(adr_id):
            errors.append(f"{path.relative_to(ROOT)}: falta id ADR-#### válido")
            continue
        if adr_id in adrs:
            errors.append(f"ADR duplicado {adr_id}")
        adrs[adr_id] = path
    return adrs, errors


def validate_references(
    files: list[Path], contracts: dict[str, Path], adrs: dict[str, Path]
) -> list[str]:
    errors: list[str] = []
    for path in files:
        if "archive" in path.relative_to(ROOT).parts:
            continue
        text = path.read_text(encoding="utf-8")
        for contract_id in sorted(set(CONTRACT_ID_RE.findall(text))):
            if contract_id not in contracts:
                errors.append(f"{path.relative_to(ROOT)}: referencia contrato inexistente {contract_id}")
        for adr_id in sorted(set(ADR_ID_RE.findall(text))):
            if adr_id not in adrs:
                errors.append(f"{path.relative_to(ROOT)}: referencia ADR inexistente {adr_id}")
    return errors


def validate_completed_increments() -> list[str]:
    errors: list[str] = []
    for path in sorted(INCREMENTS.glob("I-*.md")):
        text = path.read_text(encoding="utf-8")
        if re.search(r"^- Estado:\s*completado\s*$", text, re.MULTILINE | re.IGNORECASE):
            if "## Evidencia" not in text:
                errors.append(f"{path.relative_to(ROOT)}: incremento completado sin sección Evidencia")
            if not re.search(r"^- Comando:\s*`[^`]+`", text, re.MULTILINE):
                errors.append(f"{path.relative_to(ROOT)}: incremento completado sin comando reproducible")
            if not re.search(r"^- Resultado:\s*\S+", text, re.MULTILINE):
                errors.append(f"{path.relative_to(ROOT)}: incremento completado sin resultado")
    return errors


def validate_status_claims() -> list[str]:
    errors: list[str] = []
    if not STATUS.exists():
        return ["Falta docs/implementation/STATUS.md"]

    for line_number, line in enumerate(STATUS.read_text(encoding="utf-8").splitlines(), start=1):
        if not line.startswith("|") or "Implementado" not in line:
            continue
        link_count = len(LINK_RE.findall(line))
        if link_count < 2:
            errors.append(
                f"{STATUS.relative_to(ROOT)}:{line_number}: "
                "una fila Implementado debe enlazar código y prueba"
            )
    return errors


def validate_no_design_docs_in_assets() -> list[str]:
    errors: list[str] = []
    assets = ROOT / "Assets"
    for suffix in ("*.md", "*.txt", "*.rst"):
        for path in sorted(assets.rglob(suffix)):
            errors.append(f"Documento de diseño dentro de Assets: {path.relative_to(ROOT)}")
    return errors


def main() -> int:
    errors: list[str] = []
    files = markdown_files()

    contracts, contract_errors = collect_canonical_contracts()
    adrs, adr_errors = collect_adrs()
    errors.extend(contract_errors)
    errors.extend(adr_errors)
    errors.extend(validate_links(files))
    errors.extend(validate_references(files, contracts, adrs))
    errors.extend(validate_completed_increments())
    errors.extend(validate_status_claims())
    errors.extend(validate_no_design_docs_in_assets())

    if errors:
        print("ERROR: validación documental fallida:")
        for error in errors:
            print(f"- {error}")
        return 1

    print(
        "OK: validación documental aprobada "
        f"(contratos={len(contracts)}, adrs={len(adrs)}, incrementos={len(list(INCREMENTS.glob('I-*.md')))})"
    )
    return 0


if __name__ == "__main__":
    sys.exit(main())
