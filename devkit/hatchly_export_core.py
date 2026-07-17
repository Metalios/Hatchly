"""Deterministic, Unreal-independent Hatchly DevKit export rules."""

from __future__ import annotations

import json
import os
import re
import tempfile
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any, Iterable, Mapping, Sequence


SCHEMA_VERSION = 1


def slug(value: str) -> str:
    value = re.sub(r"(_Character)?_BP(_C)?$", "", value, flags=re.IGNORECASE)
    return re.sub(r"[^a-zA-Z0-9]+", "-", value).strip("-").lower()


def display_name(value: str) -> str:
    value = re.sub(r"(_Character)?_BP(_C)?$", "", value, flags=re.IGNORECASE)
    value = re.sub(r"(?<=[a-z0-9])(?=[A-Z])", " ", value).replace("_", " ")
    return " ".join(value.split())


def positive_number(value: Any) -> float | None:
    try:
        number = float(value)
    except (TypeError, ValueError):
        return None
    return number if number > 0 else None


@dataclass(frozen=True)
class ExportPolicy:
    official_roots: tuple[str, ...]
    excluded_path_tokens: tuple[str, ...]
    stable_id_aliases: Mapping[str, str] = field(default_factory=dict)
    force_include_ids: frozenset[str] = field(default_factory=frozenset)
    force_exclude_ids: frozenset[str] = field(default_factory=frozenset)
    special_reproduction_ids: frozenset[str] = field(default_factory=frozenset)
    food_includes: Mapping[str, tuple[str, ...]] = field(default_factory=dict)
    food_excludes: Mapping[str, tuple[str, ...]] = field(default_factory=dict)

    @classmethod
    def from_mapping(cls, value: Mapping[str, Any]) -> "ExportPolicy":
        def ids(name: str) -> frozenset[str]:
            return frozenset(slug(str(item)) for item in value.get(name, []))

        def food_map(name: str) -> dict[str, tuple[str, ...]]:
            return {
                slug(str(key)): tuple(sorted({slug(str(item)) for item in items}))
                for key, items in value.get(name, {}).items()
            }

        return cls(
            official_roots=tuple(str(item) for item in value["officialRoots"]),
            excluded_path_tokens=tuple(
                str(item).casefold() for item in value.get("excludedPathTokens", [])
            ),
            stable_id_aliases={
                slug(str(key)): slug(str(item))
                for key, item in value.get("stableIdAliases", {}).items()
            },
            force_include_ids=ids("forceIncludeIds"),
            force_exclude_ids=ids("forceExcludeIds"),
            special_reproduction_ids=ids("specialReproductionIds"),
            food_includes=food_map("foodIncludes"),
            food_excludes=food_map("foodExcludes"),
        )


def load_policy(path: Path) -> ExportPolicy:
    document = json.loads(path.read_text(encoding="utf-8"))
    if document.get("schemaVersion") != SCHEMA_VERSION:
        raise ValueError(f"Unsupported export policy schema in {path}")
    return ExportPolicy.from_mapping(document)


def _stable_id(raw: Mapping[str, Any], policy: ExportPolicy) -> str:
    candidate = slug(str(raw.get("id") or raw.get("name") or ""))
    return policy.stable_id_aliases.get(candidate, candidate)


def _official_path(path: str, policy: ExportPolicy) -> bool:
    folded = path.casefold()
    return any(folded.startswith(root.casefold()) for root in policy.official_roots) and not any(
        token in folded for token in policy.excluded_path_tokens
    )


def _valid_food(raw: Mapping[str, Any]) -> tuple[dict[str, Any] | None, str | None]:
    food_id = slug(str(raw.get("id") or raw.get("name") or ""))
    required = {
        "foodValue": positive_number(raw.get("foodValue")),
        "itemWeight": positive_number(raw.get("itemWeight")),
        "spoilSeconds": positive_number(raw.get("spoilSeconds")),
        "stackSize": positive_number(raw.get("stackSize")),
    }
    missing = sorted(key for key, value in required.items() if value is None)
    waste = raw.get("waste", 0)
    try:
        waste_value = float(waste)
    except (TypeError, ValueError):
        waste_value = -1
    if not food_id or missing or waste_value < 0:
        detail = ", ".join(missing) if missing else "id or waste"
        return None, f"invalid food values: {detail}"

    return {
        "id": food_id,
        "name": str(raw.get("displayName") or display_name(str(raw.get("name", food_id)))),
        "foodValue": required["foodValue"],
        "stackSize": int(required["stackSize"]),
        "spoilSeconds": required["spoilSeconds"],
        "itemWeight": required["itemWeight"],
        "waste": waste_value,
    }, None


def _valid_reproduction(raw: Mapping[str, Any], creature_id: str, policy: ExportPolicy) -> bool:
    if creature_id in policy.special_reproduction_ids:
        return True
    if bool(raw.get("fertilizedEgg")):
        return True
    return (
        bool(raw.get("usesGender"))
        and bool(raw.get("matingAllowed"))
        and not bool(raw.get("neutered"))
        and positive_number(raw.get("gestationSpeed")) is not None
        and positive_number(raw.get("gestationSpeedMultiplier")) is not None
    )


def _creature_record(
    raw: Mapping[str, Any],
    food_ids: frozenset[str],
    policy: ExportPolicy,
) -> tuple[dict[str, Any] | None, str | None]:
    creature_id = _stable_id(raw, policy)
    path = str(raw.get("assetPath", ""))
    if not creature_id:
        return None, "missing stable id"
    if creature_id in policy.force_exclude_ids:
        return None, "explicitly excluded"
    if not _official_path(path, policy) and creature_id not in policy.force_include_ids:
        return None, "outside official live content policy"
    if not bool(raw.get("liveReference")) and creature_id not in policy.force_include_ids:
        return None, "not referenced by official live creature data"
    if bool(raw.get("boss")):
        return None, "boss creature"
    if not _valid_reproduction(raw, creature_id, policy):
        return None, "no supported player-managed raising phase"

    required_names = (
        "baseFoodRate",
        "babyFoodRateMultiplier",
        "extraBabyFoodRateMultiplier",
        "ageSpeed",
        "ageSpeedMultiplier",
        "adultWeight",
    )
    values = {name: positive_number(raw.get(name)) for name in required_names}
    missing = sorted(name for name, value in values.items() if value is None)
    if missing:
        return None, f"missing required values: {', '.join(missing)}"

    birth_method = str(raw.get("birthMethod") or "")
    if birth_method not in {"Incubation", "Gestation", "CropPlotIncubation"}:
        birth_method = "Incubation" if bool(raw.get("fertilizedEgg")) else "Gestation"

    timing: dict[str, float | None]
    if birth_method == "Gestation":
        timing = {
            "gestationSpeed": positive_number(raw.get("gestationSpeed")),
            "gestationSpeedMultiplier": positive_number(
                raw.get("gestationSpeedMultiplier")
            ),
        }
    else:
        timing = {
            "eggSpeed": positive_number(raw.get("eggSpeed")),
            "eggSpeedMultiplier": positive_number(raw.get("eggSpeedMultiplier")),
        }
    if any(value is None for value in timing.values()):
        return None, f"missing {birth_method.lower()} timing values"

    accepted = {slug(str(item)) for item in raw.get("raisingFoodIds", [])}
    accepted.update(policy.food_includes.get(creature_id, ()))
    accepted.difference_update(policy.food_excludes.get(creature_id, ()))
    unknown = sorted(accepted - food_ids)
    accepted.intersection_update(food_ids)
    if unknown:
        return None, f"references unresolved foods: {', '.join(unknown)}"
    if not accepted:
        return None, "no mechanically accepted positive-food items"

    record: dict[str, Any] = {
        "id": creature_id,
        "name": str(raw.get("displayName") or display_name(str(raw.get("name", creature_id)))),
        "birthMethod": birth_method,
        "dietId": slug(str(raw.get("dietId") or "unknown")),
        "raisingFoodIds": sorted(accepted, key=str.casefold),
        "baseFoodRate": values["baseFoodRate"],
        "babyFoodRateMultiplier": values["babyFoodRateMultiplier"],
        "extraBabyFoodRateMultiplier": values["extraBabyFoodRateMultiplier"],
        "ageSpeed": values["ageSpeed"],
        "ageSpeedMultiplier": values["ageSpeedMultiplier"],
        "adultWeight": values["adultWeight"],
        "juvenileThreshold": positive_number(raw.get("juvenileThreshold")) or 0.1,
        "foodMultipliers": {
            slug(str(key)): float(value)
            for key, value in sorted(raw.get("foodMultipliers", {}).items())
            if slug(str(key)) in accepted and positive_number(value) is not None
        },
        "wasteMultipliers": _valid_waste_multipliers(
            raw.get("wasteMultipliers", {}), accepted
        ),
        **timing,
    }
    return record, None


def _valid_waste_multipliers(
    values: Mapping[str, Any], accepted: set[str]
) -> dict[str, float]:
    result: dict[str, float] = {}
    for key, value in sorted(values.items()):
        food_id = slug(str(key))
        try:
            multiplier = float(value)
        except (TypeError, ValueError):
            continue
        if food_id in accepted and multiplier >= 0:
            result[food_id] = multiplier
    return result


def _by_id(records: Iterable[Mapping[str, Any]]) -> dict[str, Mapping[str, Any]]:
    return {str(item["id"]): item for item in records}


def canonical_json(value: Any) -> str:
    return json.dumps(value, sort_keys=True, separators=(",", ":"), ensure_ascii=False)


def build_export(
    raw_creatures: Sequence[Mapping[str, Any]],
    raw_foods: Sequence[Mapping[str, Any]],
    policy: ExportPolicy,
    previous_creatures: Sequence[Mapping[str, Any]] = (),
    previous_foods: Sequence[Mapping[str, Any]] = (),
) -> tuple[dict[str, Any], dict[str, Any], dict[str, Any]]:
    foods: list[dict[str, Any]] = []
    ambiguous_foods: dict[str, str] = {}
    for raw in raw_foods:
        record, reason = _valid_food(raw)
        key = slug(str(raw.get("id") or raw.get("name") or raw.get("assetPath", "unknown")))
        if record is None:
            ambiguous_foods[key] = reason or "unknown food error"
        else:
            foods.append(record)
    foods.sort(key=lambda item: item["id"].casefold())
    if len({item["id"].casefold() for item in foods}) != len(foods):
        raise ValueError("Duplicate generated food ids")
    food_ids = frozenset(item["id"] for item in foods)

    creatures: list[dict[str, Any]] = []
    excluded: dict[str, str] = {}
    ambiguous: dict[str, str] = {}
    sources: dict[str, str] = {}
    for raw in raw_creatures:
        key = _stable_id(raw, policy) or slug(str(raw.get("assetPath", "unknown")))
        record, reason = _creature_record(raw, food_ids, policy)
        if record is None:
            if reason in {
                "explicitly excluded",
                "outside official live content policy",
                "not referenced by official live creature data",
                "boss creature",
                "no supported player-managed raising phase",
            }:
                excluded[key] = reason
            else:
                ambiguous[key] = reason or "unknown creature error"
        else:
            creatures.append(record)
            sources[record["id"]] = str(raw.get("assetPath", ""))
    creatures.sort(key=lambda item: item["id"].casefold())
    if len({item["id"].casefold() for item in creatures}) != len(creatures):
        raise ValueError("Duplicate generated creature ids")

    current_creatures = _by_id(creatures)
    previous_creature_map = _by_id(previous_creatures)
    current_foods = _by_id(foods)
    previous_food_map = _by_id(previous_foods)
    missing = sorted(set(previous_creature_map) - set(current_creatures))
    blocking_missing = sorted(
        item for item in missing if item not in policy.force_exclude_ids
    )
    new_ids = sorted(set(current_creatures) - set(previous_creature_map))
    changed_ids = sorted(
        item
        for item in set(current_creatures) & set(previous_creature_map)
        if canonical_json(current_creatures[item])
        != canonical_json(previous_creature_map[item])
    )
    new_food_ids = sorted(set(current_foods) - set(previous_food_map))
    changed_food_ids = sorted(
        item
        for item in set(current_foods) & set(previous_food_map)
        if canonical_json(current_foods[item]) != canonical_json(previous_food_map[item])
    )

    creature_document = {"schemaVersion": SCHEMA_VERSION, "creatures": creatures}
    food_document = {"schemaVersion": SCHEMA_VERSION, "foods": foods}
    report = {
        "schemaVersion": SCHEMA_VERSION,
        "exportedCreatures": len(creatures),
        "exportedFoods": len(foods),
        "newCreatures": new_ids,
        "changedCreatures": changed_ids,
        "missingCreatures": missing,
        "newFoods": new_food_ids,
        "changedFoods": changed_food_ids,
        "ambiguousCreatures": dict(sorted(ambiguous.items())),
        "ambiguousFoods": dict(sorted(ambiguous_foods.items())),
        "excludedCreatures": dict(sorted(excluded.items())),
        "sourceAssets": dict(sorted(sources.items())),
        "blockingErrors": [
            f"Previously exported creature '{item}' disappeared without an explicit exclusion."
            for item in blocking_missing
        ],
        "manualOverridesTouched": False,
    }
    return creature_document, food_document, report


def atomic_write_json(path: Path, value: Any) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    payload = json.dumps(value, indent=2, ensure_ascii=False, sort_keys=True) + "\n"
    descriptor, temporary = tempfile.mkstemp(
        prefix=f".{path.name}.", suffix=".tmp", dir=path.parent
    )
    try:
        with os.fdopen(descriptor, "w", encoding="utf-8", newline="\n") as stream:
            stream.write(payload)
        os.replace(temporary, path)
    finally:
        if os.path.exists(temporary):
            os.unlink(temporary)


def read_records(path: Path, key: str) -> list[Mapping[str, Any]]:
    if not path.exists():
        return []
    document = json.loads(path.read_text(encoding="utf-8"))
    return list(document.get(key, []))
