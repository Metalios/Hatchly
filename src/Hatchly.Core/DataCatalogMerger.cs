namespace Hatchly.Core;

public static class DataCatalogMerger
{
    public static IReadOnlyList<CreatureDefinition> ApplyOverrides(
        IEnumerable<CreatureDefinition> creatures,
        IEnumerable<CreatureOverride> overrides)
    {
        var byId = overrides.ToDictionary(
            item => item.CreatureId,
            StringComparer.OrdinalIgnoreCase);

        return creatures
            .Select(creature =>
            {
                if (!byId.TryGetValue(creature.Id, out var value))
                {
                    return creature;
                }

                return creature with
                {
                    BirthMethod = value.BirthMethod ?? creature.BirthMethod,
                    SpecialBehavior = value.SpecialBehavior ?? creature.SpecialBehavior,
                    JuvenileThreshold = value.JuvenileThreshold ?? creature.JuvenileThreshold
                };
            })
            .OrderBy(creature => creature.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static void Validate(DataCatalog catalog)
    {
        EnsureUnique(catalog.Creatures.Select(item => item.Id), "creature");
        EnsureUnique(catalog.Foods.Select(item => item.Id), "food");
        EnsureUnique(catalog.Diets.Select(item => item.Id), "diet");

        var foodIds = catalog.Foods
            .Select(item => item.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var dietIds = catalog.Diets
            .Select(item => item.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var diet in catalog.Diets)
        {
            if (diet.FoodIds.Count == 0)
            {
                throw new InvalidDataException(
                    $"Diet '{diet.Id}' does not contain any foods.");
            }

            foreach (var foodId in diet.FoodIds)
            {
                if (!foodIds.Contains(foodId))
                {
                    throw new InvalidDataException(
                        $"Diet '{diet.Id}' references missing food '{foodId}'.");
                }
            }
        }

        foreach (var food in catalog.Foods)
        {
            if (food.FoodValue <= 0
                || food.ItemWeight <= 0
                || food.StackSize <= 0
                || food.SpoilSeconds <= 0
                || food.Waste < 0)
            {
                throw new InvalidDataException(
                    $"Food '{food.Id}' has invalid food, weight, stack, spoil, or waste values.");
            }
        }

        foreach (var creature in catalog.Creatures)
        {
            if (!dietIds.Contains(creature.DietId))
            {
                throw new InvalidDataException(
                    $"Creature '{creature.Id}' references missing diet '{creature.DietId}'.");
            }

            if (creature.BaseFoodRate <= 0
                || creature.BabyFoodRateMultiplier <= 0
                || creature.ExtraBabyFoodRateMultiplier <= 0
                || creature.AgeSpeed <= 0
                || creature.AgeSpeedMultiplier <= 0
                || creature.AdultWeight <= 0
                || creature.JuvenileThreshold <= 0
                || creature.JuvenileThreshold > 1)
            {
                throw new InvalidDataException(
                    $"Creature '{creature.Id}' has invalid food, age, weight, or juvenile values.");
            }

            var hasValidBirthValues = creature.BirthMethod switch
            {
                BirthMethod.CropPlotIncubation
                    when creature.SpecialBehavior == "elderclaw-crop-plot" =>
                    (creature.GestationSpeed is > 0
                        && creature.GestationSpeedMultiplier is > 0)
                    || (creature.EggSpeed is > 0
                        && creature.EggSpeedMultiplier is > 0),
                BirthMethod.Gestation =>
                    creature.GestationSpeed is > 0
                    && creature.GestationSpeedMultiplier is > 0,
                _ =>
                    creature.EggSpeed is > 0
                    && creature.EggSpeedMultiplier is > 0
            };
            if (!hasValidBirthValues)
            {
                throw new InvalidDataException(
                    $"Creature '{creature.Id}' has invalid birth timing values.");
            }

            foreach (var multiplier in creature.FoodMultipliers)
            {
                if (!foodIds.Contains(multiplier.Key) || multiplier.Value <= 0)
                {
                    throw new InvalidDataException(
                        $"Creature '{creature.Id}' has invalid food multiplier '{multiplier.Key}'.");
                }
            }

            foreach (var multiplier in creature.WasteMultipliers)
            {
                if (!foodIds.Contains(multiplier.Key) || multiplier.Value < 0)
                {
                    throw new InvalidDataException(
                        $"Creature '{creature.Id}' has invalid waste multiplier '{multiplier.Key}'.");
                }
            }
        }
    }

    private static void EnsureUnique(IEnumerable<string> values, string label)
    {
        var duplicate = values
            .GroupBy(value => value, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidDataException(
                $"Duplicate {label} id '{duplicate.Key}'.");
        }
    }
}
