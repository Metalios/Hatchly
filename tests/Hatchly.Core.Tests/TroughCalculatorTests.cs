using Hatchly.Core;

namespace Hatchly.Core.Tests;

public sealed class TroughCalculatorTests
{
    [Fact]
    public void Multiple_creatures_share_whole_stack_items()
    {
        var creature = RaiseCalculatorTests.Creature(baseFoodRate: .2);
        var food = RaiseCalculatorTests.Food(
            foodValue: 10,
            stackSize: 1,
            spoilSeconds: 90_000_000);
        var result = new TroughCalculator().Calculate(new TroughRequest
        {
            Creatures =
            [
                new TroughCreatureRequest(creature, 10, 2)
            ],
            Foods =
            [
                new TroughFoodRequest(food, 2)
            ],
            Diets = new Dictionary<string, DietDefinition>
            {
                ["diet"] = new()
                {
                    Id = "diet",
                    Name = "Test Diet",
                    FoodIds = ["food"]
                }
            },
            Rates = new ServerRates(1, 1, 1),
            SpoilMultiplier = 4,
            MaximumSimulation = TimeSpan.FromSeconds(500)
        });

        Assert.Equal(2, result.TotalItems);
        Assert.Equal(2, result.EatenItems);
        Assert.Equal(0, result.SpoiledItems);
        Assert.InRange(result.Coverage.TotalSeconds, 100, 500);
        Assert.True(result.CoverageByDiet["diet"] > TimeSpan.Zero);
    }

    [Fact]
    public void Tek_multiplier_delays_spoilage()
    {
        var creature = RaiseCalculatorTests.Creature(baseFoodRate: .001);
        var food = RaiseCalculatorTests.Food(
            foodValue: 10,
            stackSize: 1,
            spoilSeconds: 2);

        TroughResult Run(double multiplier) =>
            new TroughCalculator().Calculate(new TroughRequest
            {
                Creatures = [new TroughCreatureRequest(creature, 10, 1)],
                Foods = [new TroughFoodRequest(food, 1)],
                Diets = new Dictionary<string, DietDefinition>
                {
                    ["diet"] = new()
                    {
                        Id = "diet",
                        Name = "Test Diet",
                        FoodIds = ["food"]
                    }
                },
                Rates = new ServerRates(1, 1, 1),
                SpoilMultiplier = multiplier,
                MaximumSimulation = TimeSpan.FromSeconds(300)
            });

        var handFeed = Run(1);
        var tek = Run(100);

        Assert.Equal(1, handFeed.SpoiledItems);
        Assert.True(tek.Coverage > handFeed.Coverage);
    }
}
