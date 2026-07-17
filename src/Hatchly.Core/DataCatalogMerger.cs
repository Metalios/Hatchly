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
                    JuvenileThreshold = value.JuvenileThreshold ?? creature.JuvenileThreshold,
                    RaisingFoodIds = MergeFoodIds(
                        creature.RaisingFoodIds,
                        value.IncludeFoodIds,
                        value.ExcludeFoodIds),
                    FoodMultipliers = MergeMultipliers(
                        creature.FoodMultipliers,
                        value.FoodMultipliers),
                    WasteMultipliers = MergeMultipliers(
                        creature.WasteMultipliers,
                        value.WasteMultipliers)
                };
            })
            .OrderBy(creature => creature.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static IReadOnlyList<FoodDefinition> ApplyFoodOverrides(
        IEnumerable<FoodDefinition> foods,
        IEnumerable<FoodOverride> overrides)
    {
        var byId = overrides.ToDictionary(
            item => item.FoodId,
            StringComparer.OrdinalIgnoreCase);

        return foods
            .Where(food => !byId.TryGetValue(food.Id, out var value) || !value.Disabled)
            .Select(food =>
            {
                if (!byId.TryGetValue(food.Id, out var value))
                {
                    return food;
                }

                return food with
                {
                    Name = value.Name ?? food.Name,
                    FoodValue = value.FoodValue ?? food.FoodValue,
                    StackSize = value.StackSize ?? food.StackSize,
                    SpoilSeconds = value.SpoilSeconds ?? food.SpoilSeconds,
                    ItemWeight = value.ItemWeight ?? food.ItemWeight,
                    Waste = value.Waste ?? food.Waste
                };
            })
            .OrderBy(food => food.Id, StringComparer.OrdinalIgnoreCase)
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

            if (creature.RaisingFoodIds.Count == 0)
            {
                throw new InvalidDataException(
                    $"Creature '{creature.Id}' does not contain any raising foods.");
            }

            EnsureUnique(creature.RaisingFoodIds, $"raising food on creature '{creature.Id}'");
            foreach (var foodId in creature.RaisingFoodIds)
            {
                if (!foodIds.Contains(foodId))
                {
                    throw new InvalidDataException(
                        $"Creature '{creature.Id}' references missing raising food '{foodId}'.");
                }
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
                if (!creature.RaisingFoodIds.Contains(
                        multiplier.Key,
                        StringComparer.OrdinalIgnoreCase)
                    || multiplier.Value <= 0)
                {
                    throw new InvalidDataException(
                        $"Creature '{creature.Id}' has invalid food multiplier '{multiplier.Key}'.");
                }
            }

            foreach (var multiplier in creature.WasteMultipliers)
            {
                if (!creature.RaisingFoodIds.Contains(
                        multiplier.Key,
                        StringComparer.OrdinalIgnoreCase)
                    || multiplier.Value < 0)
                {
                    throw new InvalidDataException(
                        $"Creature '{creature.Id}' has invalid waste multiplier '{multiplier.Key}'.");
                }
            }
        }
    }

    private static IReadOnlyList<string> MergeFoodIds(
        IEnumerable<string> source,
        IEnumerable<string> included,
        IEnumerable<string> excluded)
    {
        var excludedIds = excluded.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return source
            .Concat(included)
            .Where(id => !excludedIds.Contains(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static Dictionary<string, double> MergeMultipliers(
        IReadOnlyDictionary<string, double> source,
        IReadOnlyDictionary<string, double> overrides)
    {
        var result = new Dictionary<string, double>(source, StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in overrides)
        {
            result[key] = value;
        }

        return result;
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
