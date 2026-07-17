"""ASA DevKit adapter for the deterministic Hatchly export core.

Run from the Unreal Editor Python console:
    py "I:/Repos/metalios/Hatchly/devkit/ExportHatchlyData.py"

Set HATCHLY_REPO_ROOT to the Hatchly checkout. Set HATCHLY_EXPORT_MODE=probe
to verify reflected ASA property bindings without changing generated data.
"""

from __future__ import annotations

import datetime as dt
import json
import os
import sys
from pathlib import Path
from typing import Any, Iterable, Mapping, Sequence

import unreal


SCRIPT_DIR = Path(__file__).resolve().parent
if str(SCRIPT_DIR) not in sys.path:
    sys.path.insert(0, str(SCRIPT_DIR))

from hatchly_export_core import (  # noqa: E402
    atomic_write_json,
    build_export,
    load_policy,
    positive_number,
    read_records,
    slug,
)


MODE = os.environ.get("HATCHLY_EXPORT_MODE", "export").strip().casefold()
POLICY_PATH = Path(os.environ.get("HATCHLY_POLICY_PATH", SCRIPT_DIR / "export-policy.json"))
REPO_ROOT = os.environ.get("HATCHLY_REPO_ROOT")
if REPO_ROOT:
    DATA_DIR = Path(REPO_ROOT) / "src" / "Hatchly.App" / "wwwroot" / "data"
else:
    DATA_DIR = Path(unreal.Paths.project_saved_dir()) / "Hatchly"

CREATURE_PATH = DATA_DIR / "creatures.generated.json"
FOOD_PATH = DATA_DIR / "foods.generated.json"
REPORT_PATH = DATA_DIR / "devkit-export-report.json"
PROBE_PATH = DATA_DIR / "property-probe.json"


PROPERTY_CANDIDATES: dict[str, tuple[str, ...]] = {
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
    "statusValues": ("BaseCharacterStatusValues", "MaxStatusValues", "BaseStatusValues"),
    "statusComponent": (
        "MyCharacterStatusComponent",
        "CharacterStatusComponentClass",
        "DinoCharacterStatusComponentTemplate",
    ),
    "dinoSettings": ("DinoSettingsClass", "DinoSettings", "PrimalDinoSettings"),
    "dinoEntryClass": ("DinoClass", "DinoCharacterClass", "DinoClassToSpawn"),
    "dinoEntries": (
        "DinoEntries",
        "DinoEntriesList",
        "DinoEntriesToAdd",
        "AdditionalDinoEntries",
    ),
    "eggDinoClass": ("EggDinoClassToSpawn",),
    "usesGender": ("bUsesGender", "UsesGender"),
    "neutered": ("bNeutered", "Neutered"),
    "boss": ("bIsBossDino", "IsBossDino"),
    "canMate": ("bCanMate", "bAllowMating", "bCanBreed", "CanBreed"),
    "preventMating": ("bPreventBreeding", "bDisableMating", "PreventBreeding"),
    "matingInterval": (
        "MatingIntervalMinMultiplier",
        "MatingIntervalMaxMultiplier",
        "MatingIntervalMultiplier",
    ),
    "herbivore": ("bIsHerbivore", "IsHerbivore"),
    "dietName": ("HatchlyDietId", "BabyFoodType", "DinoFoodTypeName", "FoodTypeName"),
    "allowedFoodClasses": (
        "BabyFoodItemClasses",
        "FoodItemClasses",
        "AllowedFoodItemClasses",
        "RemoteAddItemOnlyAllowItemClasses",
    ),
    "foodType": ("DinoFoodTypeName", "FoodTypeName", "MyConsumableType", "MyItemType"),
    "foodValue": ("FoodValue", "DinoFoodValue", "AddFood", "BaseFoodValue"),
    "foodStatusValues": (
        "UseItemAddCharacterStatusValues",
        "AddCharacterStatusValues",
        "BaseItemStatusValues",
    ),
    "itemName": ("DescriptiveNameBase", "ItemName", "DescriptiveName"),
    "stackSize": ("MaxItemQuantity", "ItemStackSize", "MaxStackSize"),
    "itemWeight": ("BaseItemWeight", "ItemWeight", "Weight"),
    "spoilSeconds": ("SpoilingTime", "SpoilTime", "BaseSpoilingTime"),
}

REQUIRED_BINDINGS = {
    "baseFoodRate",
    "babyFoodRateMultiplier",
    "extraBabyFoodRateMultiplier",
    "ageSpeed",
    "ageSpeedMultiplier",
    "statusValues",
    "dinoEntryClass",
    "foodValue",
    "stackSize",
    "itemWeight",
    "spoilSeconds",
}

binding_matches: dict[str, set[str]] = {key: set() for key in PROPERTY_CANDIDATES}


def _asset_name(asset_data: unreal.AssetData) -> str:
    return str(asset_data.asset_name)


def _asset_path(asset_data: unreal.AssetData) -> str:
    return str(asset_data.package_name)


def _read_property(
    objects: Iterable[Any],
    binding: str,
    *,
    accept_false: bool = True,
) -> Any:
    for obj in objects:
        if obj is None:
            continue
        for candidate in PROPERTY_CANDIDATES[binding]:
            try:
                value = obj.get_editor_property(candidate)
            except Exception:
                continue
            if value is not None and (accept_false or value is not False):
                binding_matches[binding].add(candidate)
                return value
    return None


def _object_path(value: Any) -> str:
    if value is None:
        return ""
    for method in ("get_path_name", "get_name"):
        try:
            return str(getattr(value, method)())
        except Exception:
            continue
    return str(value)


def _default_object(value: Any) -> Any:
    if value is None:
        return None
    try:
        return unreal.get_default_object(value)
    except Exception:
        return value


def _load_default_object(asset_data: unreal.AssetData | None) -> Any:
    if asset_data is None:
        return None
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


def _native_class_name(asset_data: unreal.AssetData) -> str:
    try:
        return str(unreal.AssetRegistryHelpers.find_asset_native_class(asset_data))
    except Exception:
        return ""


def _discover_assets(roots: Sequence[str]) -> list[unreal.AssetData]:
    registry = unreal.AssetRegistryHelpers.get_asset_registry()
    assets: dict[str, unreal.AssetData] = {}
    for root in roots:
        for asset in registry.get_assets_by_path(root, recursive=True):
            assets[_asset_path(asset)] = asset
    return sorted(assets.values(), key=_asset_path)


def _is_character(asset_data: unreal.AssetData) -> bool:
    name = _asset_name(asset_data).casefold()
    native = _native_class_name(asset_data).casefold()
    return (
        "primaldinocharacter" in native
        or ("character" in name and name.endswith(("_bp", "_bp_c")))
    ) and "status" not in name


def _is_dino_entry(asset_data: unreal.AssetData) -> bool:
    return "dinoentry" in _asset_name(asset_data).casefold()


def _is_primal_game_data(asset_data: unreal.AssetData) -> bool:
    return "primalgamedata" in _asset_name(asset_data).casefold()


def _is_primal_item(asset_data: unreal.AssetData) -> bool:
    name = _asset_name(asset_data).casefold()
    native = _native_class_name(asset_data).casefold()
    return "primalitem" in native or name.startswith("primalitem")


def _related_asset(
    assets: Sequence[unreal.AssetData],
    creature_id: str,
    markers: Sequence[str],
) -> unreal.AssetData | None:
    matches = [
        item
        for item in assets
        if creature_id in slug(_asset_name(item))
        and any(marker in _asset_name(item).casefold() for marker in markers)
    ]
    return sorted(matches, key=_asset_path)[0] if matches else None


def _class_key(value: Any) -> str:
    path = _object_path(value).casefold()
    return path.replace("_c'", "'").replace("_c\"", "\"")


def _sequence(value: Any) -> list[Any]:
    if value is None:
        return []
    if isinstance(value, (str, bytes)):
        return [value]
    try:
        return list(value)
    except TypeError:
        return [value]


def _status_value(values: Any, index: int) -> float | None:
    try:
        return positive_number(_sequence(values)[index])
    except (IndexError, TypeError):
        return None


def _food_id(name: str) -> str:
    value = re_sub_prefix(name)
    return slug(value)


def re_sub_prefix(value: str) -> str:
    prefixes = (
        "PrimalItemConsumable_",
        "PrimalItemResource_",
        "PrimalItem_",
    )
    for prefix in prefixes:
        if value.casefold().startswith(prefix.casefold()):
            return value[len(prefix) :]
    return value


def _food_value(item_object: Any) -> float | None:
    direct = positive_number(_read_property([item_object], "foodValue"))
    if direct is not None:
        return direct
    return _status_value(_read_property([item_object], "foodStatusValues"), 4)


def _extract_food(asset_data: unreal.AssetData) -> dict[str, Any] | None:
    item = _load_default_object(asset_data)
    food_value = _food_value(item)
    if food_value is None:
        return None
    name = _asset_name(asset_data)
    return {
        "id": _food_id(name),
        "name": name,
        "displayName": str(_read_property([item], "itemName") or re_sub_prefix(name)),
        "assetPath": _asset_path(asset_data),
        "classPath": _class_key(item.get_class()) if item is not None else "",
        "foodType": slug(str(_read_property([item], "foodType") or "")),
        "foodValue": food_value,
        "stackSize": positive_number(_read_property([item], "stackSize")),
        "itemWeight": positive_number(_read_property([item], "itemWeight")),
        "spoilSeconds": positive_number(_read_property([item], "spoilSeconds")),
        "waste": 0,
    }


def _extract_live_classes(assets: Sequence[unreal.AssetData]) -> set[str]:
    result: set[str] = set()
    entries: list[Any] = [
        _load_default_object(item) for item in assets if _is_dino_entry(item)
    ]
    for game_data_asset in (item for item in assets if _is_primal_game_data(item)):
        game_data = _load_default_object(game_data_asset)
        entries.extend(
            _default_object(item)
            for item in _sequence(_read_property([game_data], "dinoEntries"))
        )
    for entry in entries:
        value = _read_property([entry], "dinoEntryClass")
        if value is not None:
            result.add(_class_key(value))
    return result


def _extract_eggs(assets: Sequence[unreal.AssetData]) -> dict[str, Any]:
    result: dict[str, Any] = {}
    for asset in (item for item in assets if _is_primal_item(item)):
        obj = _load_default_object(asset)
        spawn_class = _read_property([obj], "eggDinoClass")
        if spawn_class is not None:
            result[_class_key(spawn_class)] = obj
    return result


def _accepted_food_ids(
    character: Any,
    settings: Any,
    foods: Sequence[Mapping[str, Any]],
) -> list[str]:
    allowed_classes = {
        _class_key(item)
        for item in _sequence(_read_property([character, settings], "allowedFoodClasses"))
        if item is not None
    }
    food_type = slug(str(_read_property([character, settings], "dietName") or ""))
    accepted: set[str] = set()
    for food in foods:
        if allowed_classes and str(food.get("classPath", "")) in allowed_classes:
            accepted.add(str(food["id"]))
        elif food_type and food_type == str(food.get("foodType", "")):
            accepted.add(str(food["id"]))
    return sorted(accepted)


def _extract_creature(
    asset_data: unreal.AssetData,
    assets: Sequence[unreal.AssetData],
    foods: Sequence[Mapping[str, Any]],
    live_classes: set[str],
    eggs: Mapping[str, Any],
) -> dict[str, Any]:
    name = _asset_name(asset_data)
    creature_id = slug(name)
    character = _load_default_object(asset_data)
    character_class = _class_key(character.get_class()) if character is not None else ""
    status_value = _read_property([character], "statusComponent")
    status = _default_object(status_value)
    if status is None:
        status = _load_default_object(
            _related_asset(assets, creature_id, ("status", "characterstatus"))
        )
    settings = _default_object(_read_property([character], "dinoSettings"))
    egg = eggs.get(character_class)
    sources = [character, status, egg, settings]
    status_values = _read_property([status], "statusValues")

    values = {
        key: positive_number(_read_property(sources, key))
        for key in (
            "baseFoodRate",
            "babyFoodRateMultiplier",
            "extraBabyFoodRateMultiplier",
            "ageSpeed",
            "ageSpeedMultiplier",
            "eggSpeed",
            "eggSpeedMultiplier",
            "gestationSpeed",
            "gestationSpeedMultiplier",
        )
    }
    uses_gender = bool(_read_property([character], "usesGender"))
    can_mate = _read_property([character], "canMate")
    prevented = bool(_read_property([character], "preventMating"))
    mating_interval = positive_number(_read_property([character], "matingInterval"))
    mating_allowed = bool(can_mate) if can_mate is not None else bool(
        uses_gender and not prevented and mating_interval is not None
    )
    diet_name = _read_property([character, settings], "dietName")
    if diet_name:
        diet_id = slug(str(diet_name))
    else:
        diet_id = "herbivore" if bool(_read_property([character], "herbivore")) else "carnivore"

    return {
        "id": creature_id,
        "name": name,
        "displayName": str(_read_property([character], "itemName") or name),
        "assetPath": _asset_path(asset_data),
        "classPath": character_class,
        "liveReference": character_class in live_classes,
        "fertilizedEgg": character_class in eggs,
        "usesGender": uses_gender,
        "matingAllowed": mating_allowed,
        "neutered": bool(_read_property([character], "neutered")),
        "boss": bool(_read_property([character], "boss")),
        "birthMethod": "Incubation" if character_class in eggs else "Gestation",
        "dietId": diet_id,
        "raisingFoodIds": _accepted_food_ids(character, settings, foods),
        "adultWeight": _status_value(status_values, 7),
        **values,
    }


def _probe_report() -> dict[str, Any]:
    missing = sorted(key for key in REQUIRED_BINDINGS if not binding_matches[key])
    return {
        "schemaVersion": 1,
        "mode": MODE,
        "devKitVersion": _devkit_version(),
        "propertyMatches": {
            key: sorted(values) for key, values in sorted(binding_matches.items())
        },
        "missingRequiredBindings": missing,
        "ready": not missing,
    }


def _devkit_version() -> str:
    try:
        return str(unreal.SystemLibrary.get_engine_version())
    except Exception:
        return "unknown"


def main() -> None:
    if MODE not in {"probe", "export"}:
        raise RuntimeError("HATCHLY_EXPORT_MODE must be 'probe' or 'export'.")

    policy = load_policy(POLICY_PATH)
    assets = _discover_assets(policy.official_roots)
    raw_foods = [
        food
        for food in (_extract_food(item) for item in assets if _is_primal_item(item))
        if food is not None
    ]
    live_classes = _extract_live_classes(assets)
    eggs = _extract_eggs(assets)
    raw_creatures = [
        _extract_creature(item, assets, raw_foods, live_classes, eggs)
        for item in assets
        if _is_character(item)
    ]

    probe = _probe_report()
    atomic_write_json(PROBE_PATH, probe)
    if not probe["ready"]:
        missing = ", ".join(probe["missingRequiredBindings"])
        raise RuntimeError(
            f"Hatchly export stopped because required DevKit properties were not resolved: {missing}. "
            f"Review {PROBE_PATH}."
        )
    if MODE == "probe":
        unreal.log(f"Hatchly property probe passed. See {PROBE_PATH}")
        return

    creature_document, food_document, report = build_export(
        raw_creatures,
        raw_foods,
        policy,
        previous_creatures=read_records(CREATURE_PATH, "creatures"),
        previous_foods=read_records(FOOD_PATH, "foods"),
    )
    report.update(
        {
            "generatedAtUtc": dt.datetime.now(dt.timezone.utc)
            .replace(microsecond=0)
            .isoformat()
            .replace("+00:00", "Z"),
            "devKitVersion": _devkit_version(),
            "propertyMatches": probe["propertyMatches"],
            "outputDirectory": str(DATA_DIR),
        }
    )
    atomic_write_json(REPORT_PATH, report)
    if report["blockingErrors"]:
        raise RuntimeError(
            f"Hatchly export produced blocking review errors. See {REPORT_PATH}; generated files were preserved."
        )

    atomic_write_json(CREATURE_PATH, creature_document)
    atomic_write_json(FOOD_PATH, food_document)
    unreal.log(
        "Hatchly export complete: "
        f"{report['exportedCreatures']} creatures and {report['exportedFoods']} foods. "
        f"Review {REPORT_PATH}."
    )


if __name__ == "__main__":
    main()
