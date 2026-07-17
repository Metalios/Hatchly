using Hatchly.Core;

namespace Hatchly.Core.Tests;

public sealed class DataCatalogMergerTests
{
    [Fact]
    public void Creature_overrides_merge_exact_food_compatibility_and_multipliers()
    {
        var creature = RaiseCalculatorTests.Creature() with
        {
            RaisingFoodIds = ["food", "remove-me"]
        };

        var result = DataCatalogMerger.ApplyOverrides(
            [creature],
            [
                new CreatureOverride
                {
                    CreatureId = creature.Id,
                    IncludeFoodIds = ["special"],
                    ExcludeFoodIds = ["remove-me"],
                    FoodMultipliers = new() { ["special"] = 2 },
                    WasteMultipliers = new() { ["special"] = .5 }
                }
            ]).Single();

        Assert.Equal(["food", "special"], result.RaisingFoodIds);
        Assert.Equal(2, result.FoodMultiplier("special"));
        Assert.Equal(.5, result.WasteMultiplier("special"));
    }

    [Fact]
    public void Food_overrides_are_partial_and_can_disable_generated_foods()
    {
        var food = RaiseCalculatorTests.Food(10);
        var disabled = food with { Id = "disabled" };

        var result = DataCatalogMerger.ApplyFoodOverrides(
            [food, disabled],
            [
                new FoodOverride { FoodId = food.Id, Name = "Corrected food", StackSize = 30 },
                new FoodOverride { FoodId = disabled.Id, Disabled = true }
            ]);

        var corrected = Assert.Single(result);
        Assert.Equal("Corrected food", corrected.Name);
        Assert.Equal(30, corrected.StackSize);
        Assert.Equal(food.FoodValue, corrected.FoodValue);
    }

    [Fact]
    public void Validation_rejects_unknown_or_empty_per_creature_food_lists()
    {
        var food = RaiseCalculatorTests.Food(10);
        var empty = Catalog(RaiseCalculatorTests.Creature() with { RaisingFoodIds = [] }, food);
        var unknown = Catalog(
            RaiseCalculatorTests.Creature() with { RaisingFoodIds = ["missing"] },
            food);

        Assert.Contains(
            "does not contain any raising foods",
            Assert.Throws<InvalidDataException>(() => DataCatalogMerger.Validate(empty)).Message);
        Assert.Contains(
            "missing raising food 'missing'",
            Assert.Throws<InvalidDataException>(() => DataCatalogMerger.Validate(unknown)).Message);
    }

    private static DataCatalog Catalog(CreatureDefinition creature, FoodDefinition food) =>
        new()
        {
            Creatures = [creature],
            Foods = [food],
            Diets = [new DietDefinition { Id = "diet", Name = "Diet" }]
        };
}
