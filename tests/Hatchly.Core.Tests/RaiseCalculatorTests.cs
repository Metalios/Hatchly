using Hatchly.Core;

namespace Hatchly.Core.Tests;

public sealed class RaiseCalculatorTests
{
    private readonly RaiseCalculator calculator = new();

    [Theory]
    [InlineData(ServerSelection.Standard, 1, 1000)]
    [InlineData(ServerSelection.Apocalypse, 3, 333.333333)]
    [InlineData(ServerSelection.SmallTribes, 2, 500)]
    [InlineData(ServerSelection.Conquest, 5, 200)]
    public void Official_profiles_apply_maturation_rate(
        ServerSelection selection,
        double matureRate,
        double expectedSeconds)
    {
        var plan = Calculate(
            Creature(),
            Food(foodValue: 100),
            maturityPercent: 0,
            rates: new ServerRates(matureRate, matureRate, 1),
            selection: selection);

        Assert.InRange(
            Math.Abs(plan.Lifecycle.BirthToAdultDuration.TotalSeconds - expectedSeconds),
            0,
            0.001);
        Assert.InRange(
            Math.Abs(plan.Lifecycle.BirthDuration.TotalSeconds - expectedSeconds),
            0,
            0.001);
    }

    [Fact]
    public void Gestation_and_incubation_are_distinguished()
    {
        var incubation = Calculate(Creature(), Food(foodValue: 100));
        var gestation = Calculate(
            Creature(
                birthMethod: BirthMethod.Gestation,
                gestationSpeed: .002,
                eggSpeed: null),
            Food(foodValue: 100));

        Assert.Equal("Incubation", incubation.Lifecycle.BirthLabel);
        Assert.Equal(1000, incubation.Lifecycle.BirthDuration.TotalSeconds, 3);
        Assert.Equal("Gestation", gestation.Lifecycle.BirthLabel);
        Assert.Equal(500, gestation.Lifecycle.BirthDuration.TotalSeconds, 3);
    }

    [Theory]
    [InlineData(0, FeedingBufferStatus.TargetAvailableLater)]
    [InlineData(5, FeedingBufferStatus.TargetMet)]
    [InlineData(10, FeedingBufferStatus.Juvenile)]
    [InlineData(100, FeedingBufferStatus.Juvenile)]
    public void Maturity_boundaries_are_handled(
        double maturity,
        FeedingBufferStatus expectedStatus)
    {
        var plan = Calculate(
            Creature(baseFoodRate: .2),
            Food(foodValue: 1, itemWeight: 1),
            maturity,
            adultWeight: 100,
            desiredBuffer: TimeSpan.FromSeconds(10));

        Assert.Equal(expectedStatus, plan.Feeding.Status);
    }

    [Fact]
    public void Capacity_uses_weight_and_floors_to_whole_items()
    {
        var plan = Calculate(
            Creature(),
            Food(foodValue: 100, itemWeight: .4),
            maturityPercent: 3.2,
            adultWeight: 100);

        Assert.Equal(3.2, plan.Feeding.CapacityWeight, 6);
        Assert.Equal(8, plan.Feeding.FullItemQuantity);
        Assert.Equal(8, plan.Feeding.FoodRequiredToFillCurrentCapacity);
    }

    [Fact]
    public void Food_too_heavy_for_current_capacity_returns_zero_items()
    {
        var plan = Calculate(
            Creature(baseFoodRate: .2),
            Food(foodValue: 10, itemWeight: 1000),
            maturityPercent: 5,
            adultWeight: 100,
            desiredBuffer: TimeSpan.FromSeconds(30));

        Assert.Equal(0, plan.Feeding.FullItemQuantity);
        Assert.Equal(TimeSpan.Zero, plan.Feeding.FullInventoryDuration);
        Assert.Equal(FeedingBufferStatus.TargetAvailableLater, plan.Feeding.Status);
    }

    [Fact]
    public void Desired_buffer_can_be_met_now()
    {
        var plan = Calculate(
            Creature(baseFoodRate: .2),
            Food(foodValue: 1, itemWeight: 1),
            maturityPercent: 5,
            adultWeight: 100,
            desiredBuffer: TimeSpan.FromSeconds(10));

        Assert.Equal(FeedingBufferStatus.TargetMet, plan.Feeding.Status);
        Assert.True(plan.Feeding.FullInventoryDuration >= TimeSpan.FromSeconds(10));
        Assert.Equal(TimeSpan.Zero, plan.Feeding.TimeUntilTargetAvailable);
    }

    [Fact]
    public void Desired_buffer_reports_when_capacity_supports_it_later()
    {
        var plan = Calculate(
            Creature(baseFoodRate: .2),
            Food(foodValue: 1, itemWeight: 1),
            maturityPercent: 1,
            adultWeight: 100,
            desiredBuffer: TimeSpan.FromSeconds(40));

        Assert.Equal(FeedingBufferStatus.TargetAvailableLater, plan.Feeding.Status);
        Assert.True(plan.Feeding.TimeUntilTargetAvailable > TimeSpan.Zero);
        Assert.True(plan.Feeding.TargetAvailableMaturityPercent > 1);
        Assert.True(plan.Feeding.TargetAvailableItemQuantity > 1);
    }

    [Fact]
    public void Requested_buffer_is_superseded_by_juvenile()
    {
        var plan = Calculate(
            Creature(),
            Food(foodValue: 100, itemWeight: .1),
            maturityPercent: 9,
            adultWeight: 100,
            desiredBuffer: TimeSpan.FromHours(2));

        Assert.Equal(FeedingBufferStatus.CarriesToJuvenile, plan.Feeding.Status);
        Assert.Equal(plan.Lifecycle.TimeToJuvenile, plan.Feeding.EffectiveTarget);
        Assert.Equal(
            plan.Lifecycle.TimeToJuvenile.TotalSeconds,
            plan.Feeding.FullInventoryDuration.TotalSeconds,
            0);
    }

    [Fact]
    public void Spoilage_removes_whole_items_before_consumption()
    {
        var plan = Calculate(
            Creature(baseFoodRate: .001),
            Food(foodValue: 100, itemWeight: .1, stackSize: 2, spoilSeconds: 2),
            maturityPercent: 5,
            adultWeight: 10,
            desiredBuffer: TimeSpan.FromSeconds(30));

        Assert.True(plan.Feeding.SpoiledItemQuantity > 0);
        Assert.Equal(5, plan.Feeding.FullItemQuantity);
    }

    [Fact]
    public void Unofficial_consumption_rate_changes_food_totals()
    {
        var normal = Calculate(
            Creature(baseFoodRate: .2),
            Food(foodValue: 10),
            rates: new ServerRates(1, 1, 1));
        var doubled = Calculate(
            Creature(baseFoodRate: .2),
            Food(foodValue: 10),
            rates: new ServerRates(1, 1, 2));

        Assert.True(doubled.FoodToAdult > normal.FoodToAdult);
        Assert.InRange(doubled.FoodToAdult, normal.FoodToAdult * 2 - 1, normal.FoodToAdult * 2 + 1);
    }

    [Fact]
    public void Creature_food_multiplier_reduces_item_count()
    {
        var normal = Calculate(Creature(baseFoodRate: .2), Food(foodValue: 10));
        var special = Calculate(
            Creature(
                baseFoodRate: .2,
                foodMultipliers: new Dictionary<string, double> { ["food"] = 2 }),
            Food(foodValue: 10));

        Assert.InRange(special.FoodToAdult, normal.FoodToAdult / 2 - 1, normal.FoodToAdult / 2 + 1);
    }

    [Fact]
    public void Elderclaw_growth_conditions_modify_crop_plot_incubation()
    {
        var creature = Creature(
            birthMethod: BirthMethod.CropPlotIncubation,
            specialBehavior: "elderclaw-crop-plot");
        var baseline = Calculate(
            creature,
            Food(foodValue: 100),
            elderclaw: new ElderclawSettings(100, false));
        var boosted = Calculate(
            creature,
            Food(foodValue: 100),
            elderclaw: new ElderclawSettings(300, true));

        Assert.Equal(1000, baseline.Lifecycle.BirthDuration.TotalSeconds, 3);
        Assert.InRange(
            Math.Abs(boosted.Lifecycle.BirthDuration.TotalSeconds - (1000 / 3.9)),
            0,
            0.001);

        var gestationBacked = Calculate(
            Creature(
                birthMethod: BirthMethod.CropPlotIncubation,
                eggSpeed: null,
                gestationSpeed: .001,
                specialBehavior: "elderclaw-crop-plot"),
            Food(foodValue: 100),
            elderclaw: new ElderclawSettings(300, true));
        Assert.InRange(
            Math.Abs(gestationBacked.Lifecycle.BirthDuration.TotalSeconds - (1000 / 3.9)),
            0,
            0.001);
    }

    [Fact]
    public void Golden_reference_has_exact_lifecycle_and_item_counts()
    {
        var plan = Calculate(
            Creature(baseFoodRate: .2),
            Food(foodValue: 10, itemWeight: .5),
            maturityPercent: 4,
            adultWeight: 200,
            desiredBuffer: TimeSpan.FromSeconds(20));

        Assert.Equal(1000, plan.Lifecycle.BirthToAdultDuration.TotalSeconds, 3);
        Assert.Equal(60, plan.Lifecycle.TimeToJuvenile.TotalSeconds, 3);
        Assert.Equal(16, plan.Feeding.FullItemQuantity);
        Assert.Equal(2, plan.FoodToJuvenile);
        Assert.Equal(10, plan.FoodToAdult);
        Assert.Equal(11, plan.TotalFoodFromBirth);
    }

    private RaisePlan Calculate(
        CreatureDefinition creature,
        FoodDefinition food,
        double maturityPercent = 0,
        double adultWeight = 100,
        TimeSpan? desiredBuffer = null,
        ServerRates? rates = null,
        ServerSelection selection = ServerSelection.Standard,
        ElderclawSettings? elderclaw = null) =>
        calculator.Calculate(new RaiseRequest
        {
            Creature = creature,
            Food = food,
            ServerSelection = selection,
            Rates = rates ?? new ServerRates(1, 1, 1),
            MaturityPercent = maturityPercent,
            AdultWeight = adultWeight,
            DesiredBuffer = desiredBuffer ?? TimeSpan.FromSeconds(30),
            Elderclaw = elderclaw ?? new ElderclawSettings(),
            AsOf = new DateTimeOffset(2026, 7, 16, 12, 0, 0, TimeSpan.Zero)
        });

    internal static CreatureDefinition Creature(
        BirthMethod birthMethod = BirthMethod.Incubation,
        double baseFoodRate = .1,
        double? eggSpeed = .1,
        double? gestationSpeed = null,
        string? specialBehavior = null,
        Dictionary<string, double>? foodMultipliers = null) => new()
        {
            Id = "creature",
            Name = "Test Creature",
            BirthMethod = birthMethod,
            DietId = "diet",
            BaseFoodRate = baseFoodRate,
            BabyFoodRateMultiplier = 1,
            ExtraBabyFoodRateMultiplier = 1,
            AgeSpeed = .001,
            AgeSpeedMultiplier = 1,
            EggSpeed = eggSpeed,
            EggSpeedMultiplier = eggSpeed is null ? null : 1,
            GestationSpeed = gestationSpeed,
            GestationSpeedMultiplier = gestationSpeed is null ? null : 1,
            AdultWeight = 100,
            FoodMultipliers = foodMultipliers ?? [],
            SpecialBehavior = specialBehavior
        };

    internal static FoodDefinition Food(
        double foodValue,
        double itemWeight = .1,
        int stackSize = 100,
        double spoilSeconds = 90_000_000) => new()
        {
            Id = "food",
            Name = "Test Food",
            FoodValue = foodValue,
            ItemWeight = itemWeight,
            StackSize = stackSize,
            SpoilSeconds = spoilSeconds
        };
}
