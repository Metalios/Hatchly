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
            Rates = new ServerRates(1, 1, 1),
            ContainerType = TroughType.Normal,
            MaximumSimulation = TimeSpan.FromSeconds(500)
        });

        Assert.Equal(2, result.TotalItems);
        Assert.Equal(2, result.UsedSlots);
        Assert.Equal(58, result.AvailableSlots);
        Assert.Equal(2, result.EatenItems);
        Assert.Equal(0, result.SpoiledItems);
        Assert.InRange(result.Coverage.TotalSeconds, 100, 500);
        Assert.True(result.CoverageByDiet["diet"] > TimeSpan.Zero);
    }

    [Fact]
    public void Tek_multiplier_delays_spoilage()
    {
        var creature = RaiseCalculatorTests.Creature(baseFoodRate: .2);
        var food = RaiseCalculatorTests.Food(
            foodValue: 10,
            stackSize: 1,
            spoilSeconds: 2);

        TroughResult Run(TroughType type) =>
            new TroughCalculator().Calculate(new TroughRequest
            {
                Creatures = [new TroughCreatureRequest(creature, 10, 1)],
                Foods = [new TroughFoodRequest(food, 1)],
                Rates = new ServerRates(1, 1, 1),
                ContainerType = type,
                MaximumSimulation = TimeSpan.FromSeconds(30_000)
            });

        var handFeed = Run(TroughType.HandFeed);
        var tek = Run(TroughType.Tek);

        Assert.Equal(1, handFeed.SpoiledItems);
        Assert.True(tek.Coverage > handFeed.Coverage);
    }

    [Fact]
    public void Multiple_containers_combine_stack_capacity()
    {
        var food = RaiseCalculatorTests.Food(foodValue: 10, stackSize: 40);
        var result = new TroughCalculator().Calculate(new TroughRequest
        {
            Creatures = [],
            Foods = [new TroughFoodRequest(food, 90)],
            Rates = new ServerRates(1, 1, 1),
            ContainerType = TroughType.Normal,
            ContainerCount = 2
        });

        Assert.Equal(2, result.ContainerCount);
        Assert.Equal(60, result.SlotsPerContainer);
        Assert.Equal(120, result.SlotCapacity);
        Assert.Equal(90, result.UsedSlots);
        Assert.Equal(30, result.AvailableSlots);
    }

    [Fact]
    public void Food_over_container_capacity_is_rejected()
    {
        var food = RaiseCalculatorTests.Food(foodValue: 10, stackSize: 40);
        var request = new TroughRequest
        {
            Creatures = [],
            Foods = [new TroughFoodRequest(food, 61)],
            Rates = new ServerRates(1, 1, 1),
            ContainerType = TroughType.Normal
        };

        var error = Assert.Throws<ArgumentException>(
            () => new TroughCalculator().Calculate(request));

        Assert.Contains("uses 61 slots", error.Message);
        Assert.Contains("hold 60", error.Message);
    }

    [Fact]
    public void Shared_coverage_uses_the_first_diet_to_run_out()
    {
        var carnivore = RaiseCalculatorTests.Creature(baseFoodRate: .2) with
        {
            DietId = "carnivore",
            RaisingFoodIds = ["meat"]
        };
        var herbivore = RaiseCalculatorTests.Creature(baseFoodRate: .2) with
        {
            Id = "herbivore",
            DietId = "herbivore",
            RaisingFoodIds = ["berry"]
        };
        var meat = RaiseCalculatorTests.Food(
            foodValue: 10,
            stackSize: 1,
            spoilSeconds: 90_000_000) with
        {
            Id = "meat",
            Name = "Meat"
        };
        var berry = RaiseCalculatorTests.Food(
            foodValue: 10,
            stackSize: 1,
            spoilSeconds: 90_000_000) with
        {
            Id = "berry",
            Name = "Berry"
        };

        var result = new TroughCalculator().Calculate(new TroughRequest
        {
            Creatures =
            [
                new TroughCreatureRequest(carnivore, 10, 1),
                new TroughCreatureRequest(herbivore, 10, 1)
            ],
            Foods =
            [
                new TroughFoodRequest(meat, 1),
                new TroughFoodRequest(berry, 2)
            ],
            Rates = new ServerRates(1, 1, 1),
            ContainerType = TroughType.Normal,
            MaximumSimulation = TimeSpan.FromSeconds(500)
        });

        Assert.Equal(result.CoverageByDiet.Values.Min(), result.Coverage);
        Assert.True(
            result.CoverageByDiet["carnivore"]
            < result.CoverageByDiet["herbivore"]);
    }
}
