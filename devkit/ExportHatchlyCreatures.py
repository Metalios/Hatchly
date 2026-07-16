"""
ASA DevKit editor script for exporting Hatchly creature data.

Run from the Unreal Editor Python console:
    py "C:/path/to/HatchlyApp/devkit/ExportHatchlyCreatures.py"

Set HATCHLY_EXPORT_PATH to write directly into a Hatchly checkout. Otherwise the
file is written to Saved/Hatchly/creatures.generated.json in the DevKit project.
"""

from __future__ import annotations

import datetime as dt
import json
import os
import re
from pathlib import Path
from typing import Any, Iterable

import unreal


SCHEMA_VERSION = 1
CREATURE_ROOTS = (
    "/Game/PrimalEarth/Dinos",
    "/Game/ASA",
)
OUTPUT_PATH = Path(
    os.environ.get(
        "HATCHLY_EXPORT_PATH",
        os.path.join(
            unreal.Paths.project_saved_dir(),
            "Hatchly",
            "creatures.generated.json",
        ),
    )
)
REPORT_PATH = OUTPUT_PATH.with_name("creature-export-report.json")

PROPERTY_NAMES = {
    "baseFoodRate": ("BaseFoodConsumptionRate", "BaseFoodRate"),
    "babyFoodRateMultiplier": (
        "BabyDinoConsumingFoodRateMultiplier",
        "BabyFoodConsumptionRateMultiplier",
    ),
    "extraBabyFoodRateMultiplier": (
        "ExtraBabyDinoConsumingFoodRateMultiplier",
        "ExtraBabyFoodRateMultiplier",
    ),
    "ageSpeed": ("BabyAgeSpeed", "AgeSpeed"),
    "ageSpeedMultiplier": ("ExtraBabyAgeSpeedMultiplier", "BabyAgeSpeedMultiplier"),
    "eggSpeed": ("EggIncubationSpeed", "EggSpeed"),
    "eggSpeedMultiplier": ("EggIncubationSpeedMultiplier", "EggSpeedMultiplier"),
    "gestationSpeed": ("BabyGestationSpeed", "GestationSpeed"),
    "gestationSpeedMultiplier": (
        "BabyGestationSpeedMultiplier",
        "GestationSpeedMultiplier",
    ),
}


def _asset_name(asset_data: unreal.AssetData) -> str:
    return str(asset_data.asset_name)


def _slug(value: str) -> str:
    value = re.sub(r"(_Character)?_BP(_C)?$", "", value, flags=re.IGNORECASE)
    value = re.sub(r"[^a-zA-Z0-9]+", "-", value).strip("-").lower()
    return value


def _display_name(value: str) -> str:
    value = re.sub(r"(_Character)?_BP(_C)?$", "", value, flags=re.IGNORECASE)
    value = re.sub(r"(?<=[a-z0-9])(?=[A-Z])", " ", value)
    value = value.replace("_", " ")
    return " ".join(part for part in value.split() if part)


def _load_default_object(asset_data: unreal.AssetData) -> Any:
    asset = asset_data.get_asset()
    if asset is None:
        return None

    try:
        generated_class = asset.get_editor_property("generated_class")
        if generated_class:
            return unreal.get_default_object(generated_class)
    except Exception:
        pass

    return asset


def _read_property(objects: Iterable[Any], candidates: Iterable[str]) -> Any:
    for obj in objects:
        if obj is None:
            continue
        for candidate in candidates:
            try:
                value = obj.get_editor_property(candidate)
                if value is not None:
                    return value
            except Exception:
                continue
    return None


def _as_positive_float(value: Any) -> float | None:
    try:
        number = float(value)
    except (TypeError, ValueError):
        return None
    return number if number > 0 else None


def _find_related_asset(
    all_assets: list[unreal.AssetData],
    creature_key: str,
    markers: tuple[str, ...],
) -> unreal.AssetData | None:
    candidates = []
    for asset in all_assets:
        name = _asset_name(asset).lower()
        if creature_key in _slug(name) and any(marker in name for marker in markers):
            candidates.append(asset)
    return sorted(candidates, key=_asset_name)[0] if candidates else None


def _adult_weight(status_object: Any) -> float | None:
    direct = _read_property(
        [status_object],
        ("BaseCharacterStatusValues", "MaxStatusValues", "BaseStatusValues"),
    )
    if direct is None:
        return None

    try:
        # Weight is status index 7 in ARK's EPrimalCharacterStatusValue ordering.
        return _as_positive_float(direct[7])
    except Exception:
        return None


def _diet_id(character_object: Any) -> str:
    explicit = _read_property(
        [character_object],
        ("HatchlyDietId", "BabyFoodType", "DinoFoodTypeName"),
    )
    if explicit:
        return _slug(str(explicit))

    herbivore = _read_property(
        [character_object],
        ("bIsHerbivore", "IsHerbivore"),
    )
    return "herbivore" if bool(herbivore) else "carnivore"


def _extract_record(
    character_asset: unreal.AssetData,
    all_assets: list[unreal.AssetData],
) -> tuple[dict[str, Any] | None, list[str]]:
    warnings: list[str] = []
    name = _asset_name(character_asset)
    stable_id = _slug(name)
    character_object = _load_default_object(character_asset)
    status_asset = _find_related_asset(
        all_assets,
        stable_id,
        ("status", "characterstatus"),
    )
    egg_asset = _find_related_asset(
        all_assets,
        stable_id,
        ("egg", "fertilized"),
    )
    status_object = _load_default_object(status_asset) if status_asset else None
    egg_object = _load_default_object(egg_asset) if egg_asset else None
    sources = [character_object, status_object, egg_object]

    values: dict[str, float | None] = {}
    for output_name, candidates in PROPERTY_NAMES.items():
        values[output_name] = _as_positive_float(_read_property(sources, candidates))

    adult_weight = _adult_weight(status_object)
    gestation = values["gestationSpeed"] is not None
    birth_method = "Gestation" if gestation else "Incubation"

    required = (
        "baseFoodRate",
        "babyFoodRateMultiplier",
        "extraBabyFoodRateMultiplier",
        "ageSpeed",
        "ageSpeedMultiplier",
    )
    missing = [key for key in required if values[key] is None]
    if adult_weight is None:
        missing.append("adultWeight")
    if gestation:
        if values["gestationSpeedMultiplier"] is None:
            missing.append("gestationSpeedMultiplier")
    elif values["eggSpeed"] is None or values["eggSpeedMultiplier"] is None:
        missing.append("eggSpeed/eggSpeedMultiplier")

    if missing:
        warnings.append(f"missing required values: {', '.join(missing)}")
        return None, warnings

    if adult_weight and (adult_weight < 1 or adult_weight > 100000):
        warnings.append(f"suspicious adult weight: {adult_weight}")
    if values["ageSpeed"] and values["ageSpeed"] > 1:
        warnings.append(f"suspicious age speed: {values['ageSpeed']}")

    record: dict[str, Any] = {
        "id": stable_id,
        "name": _display_name(name),
        "birthMethod": birth_method,
        "dietId": _diet_id(character_object),
        "baseFoodRate": values["baseFoodRate"],
        "babyFoodRateMultiplier": values["babyFoodRateMultiplier"],
        "extraBabyFoodRateMultiplier": values["extraBabyFoodRateMultiplier"],
        "ageSpeed": values["ageSpeed"],
        "ageSpeedMultiplier": values["ageSpeedMultiplier"],
        "adultWeight": adult_weight,
        "juvenileThreshold": 0.1,
        "foodMultipliers": {},
        "wasteMultipliers": {},
    }
    if gestation:
        record["gestationSpeed"] = values["gestationSpeed"]
        record["gestationSpeedMultiplier"] = values["gestationSpeedMultiplier"]
    else:
        record["eggSpeed"] = values["eggSpeed"]
        record["eggSpeedMultiplier"] = values["eggSpeedMultiplier"]

    return record, warnings


def _discover_assets() -> list[unreal.AssetData]:
    registry = unreal.AssetRegistryHelpers.get_asset_registry()
    assets: dict[str, unreal.AssetData] = {}
    for root in CREATURE_ROOTS:
        for asset in registry.get_assets_by_path(root, recursive=True):
            assets[str(asset.package_name)] = asset
    return sorted(assets.values(), key=lambda item: str(item.package_name))


def _is_character_asset(asset: unreal.AssetData) -> bool:
    name = _asset_name(asset).lower()
    return (
        "character" in name
        and name.endswith(("_bp", "_bp_c"))
        and "status" not in name
        and "test" not in name
    )


def _read_previous() -> dict[str, dict[str, Any]]:
    if not OUTPUT_PATH.exists():
        return {}
    try:
        document = json.loads(OUTPUT_PATH.read_text(encoding="utf-8"))
        return {item["id"]: item for item in document.get("creatures", [])}
    except Exception:
        unreal.log_warning(f"Could not parse previous Hatchly export at {OUTPUT_PATH}")
        return {}


def _canonical(record: dict[str, Any]) -> str:
    return json.dumps(record, sort_keys=True, separators=(",", ":"))


def main() -> None:
    all_assets = _discover_assets()
    previous = _read_previous()
    records: list[dict[str, Any]] = []
    suspicious: dict[str, list[str]] = {}

    for asset in (item for item in all_assets if _is_character_asset(item)):
        record, warnings = _extract_record(asset, all_assets)
        key = _slug(_asset_name(asset))
        if warnings:
            suspicious[key] = warnings
        if record is not None:
            records.append(record)

    records.sort(key=lambda item: item["id"])
    current = {item["id"]: item for item in records}
    new_ids = sorted(set(current) - set(previous))
    missing_ids = sorted(set(previous) - set(current))
    changed_ids = sorted(
        key
        for key in set(current) & set(previous)
        if _canonical(current[key]) != _canonical(previous[key])
    )

    document = {
        "schemaVersion": SCHEMA_VERSION,
        "generatedAtUtc": dt.datetime.now(dt.timezone.utc)
        .replace(microsecond=0)
        .isoformat()
        .replace("+00:00", "Z"),
        "creatures": records,
    }
    report = {
        "schemaVersion": SCHEMA_VERSION,
        "outputPath": str(OUTPUT_PATH),
        "discoveredAssets": len(all_assets),
        "exportedCreatures": len(records),
        "new": new_ids,
        "changed": changed_ids,
        "missing": missing_ids,
        "suspicious": suspicious,
        "manualOverridesTouched": False,
    }

    OUTPUT_PATH.parent.mkdir(parents=True, exist_ok=True)
    OUTPUT_PATH.write_text(
        json.dumps(document, indent=2, sort_keys=False) + "\n",
        encoding="utf-8",
    )
    REPORT_PATH.write_text(
        json.dumps(report, indent=2, sort_keys=True) + "\n",
        encoding="utf-8",
    )

    unreal.log(
        "Hatchly export complete: "
        f"{len(records)} records, {len(new_ids)} new, "
        f"{len(changed_ids)} changed, {len(missing_ids)} missing."
    )
    if suspicious:
        unreal.log_warning(
            f"{len(suspicious)} records need review. See {REPORT_PATH}"
        )


if __name__ == "__main__":
    main()
