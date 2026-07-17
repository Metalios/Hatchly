from __future__ import annotations

import json
import tempfile
import unittest
from pathlib import Path

from devkit.hatchly_export_core import (
    ExportPolicy,
    atomic_write_json,
    build_export,
    canonical_json,
)


def policy(**changes):
    values = {
        "official_roots": ("/Game/PrimalEarth",),
        "excluded_path_tokens": ("/test/", "/mods/", "/deprecated/", "_boss_"),
        "force_include_ids": frozenset({"reaper"}),
        "force_exclude_ids": frozenset(),
        "special_reproduction_ids": frozenset({"reaper"}),
        "food_includes": {},
        "food_excludes": {},
    }
    values.update(changes)
    return ExportPolicy(**values)


def food(food_id="raw-meat", **changes):
    values = {
        "id": food_id,
        "name": food_id,
        "displayName": food_id.replace("-", " ").title(),
        "foodValue": 50,
        "stackSize": 40,
        "spoilSeconds": 600,
        "itemWeight": 0.1,
        "waste": 0,
    }
    values.update(changes)
    return values


def creature(creature_id="argentavis", **changes):
    values = {
        "id": creature_id,
        "name": creature_id,
        "displayName": creature_id.title(),
        "assetPath": f"/Game/PrimalEarth/Dinos/{creature_id}",
        "liveReference": True,
        "fertilizedEgg": True,
        "usesGender": True,
        "matingAllowed": True,
        "neutered": False,
        "boss": False,
        "birthMethod": "Incubation",
        "dietId": "carnivore",
        "raisingFoodIds": ["raw-meat"],
        "baseFoodRate": 0.001,
        "babyFoodRateMultiplier": 25.5,
        "extraBabyFoodRateMultiplier": 20,
        "ageSpeed": 0.000003,
        "ageSpeedMultiplier": 1,
        "eggSpeed": 0.005,
        "eggSpeedMultiplier": 1,
        "adultWeight": 400,
    }
    values.update(changes)
    return values


class HatchlyExportCoreTests(unittest.TestCase):
    def test_exports_egg_gestation_and_special_raiseable_creatures(self):
        equus = creature(
            "equus",
            fertilizedEgg=False,
            birthMethod="Gestation",
            eggSpeed=None,
            eggSpeedMultiplier=None,
            gestationSpeed=0.005,
            gestationSpeedMultiplier=1,
        )
        reaper = creature(
            "reaper",
            liveReference=False,
            fertilizedEgg=False,
            usesGender=False,
            matingAllowed=False,
            birthMethod="Gestation",
            eggSpeed=None,
            eggSpeedMultiplier=None,
            gestationSpeed=0.004,
            gestationSpeedMultiplier=1,
        )
        creatures, _, report = build_export(
            [creature(), equus, reaper], [food()], policy()
        )

        self.assertEqual(
            ["argentavis", "equus", "reaper"],
            [item["id"] for item in creatures["creatures"]],
        )
        self.assertEqual([], report["blockingErrors"])

    def test_excludes_boss_test_and_mod_assets(self):
        boss = creature("dragon", boss=True)
        test = creature("test-dodo", assetPath="/Game/PrimalEarth/Test/TestDodo")
        mod = creature("mod-dino", assetPath="/Game/Mods/Example/ModDino")
        deprecated = creature(
            "old-dodo",
            assetPath="/Game/PrimalEarth/Deprecated/OldDodo",
        )
        creatures, _, report = build_export(
            [boss, test, mod, deprecated], [food()], policy()
        )

        self.assertEqual([], creatures["creatures"])
        self.assertEqual(4, len(report["excludedCreatures"]))

    def test_duplicate_variants_require_an_explicit_stable_id_policy(self):
        duplicate = creature("duplicate")

        with self.assertRaisesRegex(ValueError, "Duplicate generated creature ids"):
            build_export([duplicate, dict(duplicate)], [food()], policy())

    def test_exports_every_positive_food_and_applies_policy_food_rules(self):
        blood_pack = food("blood-pack", foodValue=200, itemWeight=0.05)
        invalid = food("bad-food", foodValue=0)
        bloodstalker = creature("bloodstalker", raisingFoodIds=["raw-meat"])
        export_policy = policy(
            food_includes={"bloodstalker": ("blood-pack",)},
            food_excludes={"bloodstalker": ("raw-meat",)},
        )

        creatures, foods, report = build_export(
            [bloodstalker], [food(), blood_pack, invalid], export_policy
        )

        self.assertEqual(
            ["blood-pack"], creatures["creatures"][0]["raisingFoodIds"]
        )
        self.assertEqual(
            ["blood-pack", "raw-meat"], [item["id"] for item in foods["foods"]]
        )
        self.assertIn("bad-food", report["ambiguousFoods"])

    def test_missing_previous_creature_requires_explicit_exclusion(self):
        previous = [
            {
                "id": "missing-creature",
                "name": "Missing Creature",
            }
        ]
        _, _, report = build_export([], [food()], policy(), previous_creatures=previous)
        self.assertEqual(1, len(report["blockingErrors"]))

        approved = policy(force_exclude_ids=frozenset({"missing-creature"}))
        _, _, approved_report = build_export(
            [], [food()], approved, previous_creatures=previous
        )
        self.assertEqual([], approved_report["blockingErrors"])

    def test_generated_files_are_byte_identical_and_overrides_are_untouched(self):
        creature_document, food_document, _ = build_export(
            [creature()], [food()], policy()
        )
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            override_path = root / "creature-overrides.json"
            override_payload = '{"schemaVersion":1,"overrides":[]}\n'
            override_path.write_text(override_payload, encoding="utf-8")
            creature_path = root / "creatures.generated.json"
            food_path = root / "foods.generated.json"

            atomic_write_json(creature_path, creature_document)
            atomic_write_json(food_path, food_document)
            first_creatures = creature_path.read_bytes()
            first_foods = food_path.read_bytes()
            atomic_write_json(creature_path, json.loads(canonical_json(creature_document)))
            atomic_write_json(food_path, json.loads(canonical_json(food_document)))

            self.assertEqual(first_creatures, creature_path.read_bytes())
            self.assertEqual(first_foods, food_path.read_bytes())
            self.assertEqual(override_payload, override_path.read_text(encoding="utf-8"))


if __name__ == "__main__":
    unittest.main()
