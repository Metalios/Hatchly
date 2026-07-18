using System.Text.Json.Serialization;

namespace Hatchly.Core;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ServerSelection
{
    Standard,
    Apocalypse,
    SmallTribes,
    Conquest,
    Unofficial
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum BirthMethod
{
    Incubation,
    Gestation,
    CropPlotIncubation
}

public static class MaturationThresholds
{
    public const double Adolescent = 0.50;
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum FeedingBufferStatus
{
    TargetMet,
    TargetAvailableLater,
    CarriesToJuvenile,
    Juvenile
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TroughType
{
    Normal,
    Maeguana,
    Tek,
    HandFeed
}

public sealed record TroughContainerProfile(
    TroughType Type,
    string DisplayName,
    int SlotCapacity,
    double SpoilMultiplier,
    bool SupportsBabyPhase);

public static class TroughProfiles
{
    public static TroughContainerProfile Get(TroughType type) => type switch
    {
        TroughType.Normal => new(type, "Feeding Trough", 60, 4, false),
        TroughType.Maeguana => new(type, "Maeguana", 300, 4, true),
        TroughType.Tek => new(type, "Powered Tek Trough", 100, 100, false),
        TroughType.HandFeed => new(type, "Hand-feed inventory", 300, 1, true),
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
    };
}

public sealed record ServerRates(
    double HatchSpeed,
    double MaturationSpeed,
    double ConsumptionSpeed = 1);

public sealed record OfficialRateProfile
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public required string SourceUrl { get; init; }
    public required double EggHatchSpeedMultiplier { get; init; }
    public required double BabyMatureSpeedMultiplier { get; init; }

    public ServerRates ToServerRates() =>
        new(EggHatchSpeedMultiplier, BabyMatureSpeedMultiplier, 1);
}

public sealed record OfficialRatesDocument
{
    public int SchemaVersion { get; init; } = 1;
    public DateTimeOffset LastRelevantRateChangeUtc { get; init; }
    public required IReadOnlyList<OfficialRateProfile> Profiles { get; init; }
}

public sealed record CreatureDefinition
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required BirthMethod BirthMethod { get; init; }
    public required string DietId { get; init; }
    public required IReadOnlyList<string> RaisingFoodIds { get; init; }
    public required double BaseFoodRate { get; init; }
    public required double BabyFoodRateMultiplier { get; init; }
    public required double ExtraBabyFoodRateMultiplier { get; init; }
    public required double AgeSpeed { get; init; }
    public required double AgeSpeedMultiplier { get; init; }
    public double? EggSpeed { get; init; }
    public double? EggSpeedMultiplier { get; init; }
    public double? GestationSpeed { get; init; }
    public double? GestationSpeedMultiplier { get; init; }
    public required double AdultWeight { get; init; }
    public double JuvenileThreshold { get; init; } = 0.10;
    public Dictionary<string, double> FoodMultipliers { get; init; } = [];
    public Dictionary<string, double> WasteMultipliers { get; init; } = [];
    public string? SpecialBehavior { get; init; }

    public double FoodMultiplier(string foodId) =>
        FoodMultipliers.TryGetValue(foodId, out var value) ? value : 1;

    public double WasteMultiplier(string foodId) =>
        WasteMultipliers.TryGetValue(foodId, out var value) ? value : 1;
}

public sealed record FoodDefinition
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required double FoodValue { get; init; }
    public required int StackSize { get; init; }
    public required double SpoilSeconds { get; init; }
    public required double ItemWeight { get; init; }
    public double Waste { get; init; }
}

public sealed record DietDefinition
{
    public required string Id { get; init; }
    public required string Name { get; init; }
}

public sealed record FoodOverride
{
    public required string FoodId { get; init; }
    public bool Disabled { get; init; }
    public string? Name { get; init; }
    public double? FoodValue { get; init; }
    public int? StackSize { get; init; }
    public double? SpoilSeconds { get; init; }
    public double? ItemWeight { get; init; }
    public double? Waste { get; init; }
}

public sealed record CreatureOverride
{
    public required string CreatureId { get; init; }
    public BirthMethod? BirthMethod { get; init; }
    public string? SpecialBehavior { get; init; }
    public double? JuvenileThreshold { get; init; }
    public IReadOnlyList<string> IncludeFoodIds { get; init; } = [];
    public IReadOnlyList<string> ExcludeFoodIds { get; init; } = [];
    public Dictionary<string, double> FoodMultipliers { get; init; } = [];
    public Dictionary<string, double> WasteMultipliers { get; init; } = [];
}

public sealed record DataCatalog
{
    public int SchemaVersion { get; init; } = 1;
    public required IReadOnlyList<CreatureDefinition> Creatures { get; init; }
    public required IReadOnlyList<FoodDefinition> Foods { get; init; }
    public required IReadOnlyList<DietDefinition> Diets { get; init; }
}

public sealed record ElderclawSettings(
    double GreenhousePercent = 300,
    bool ShovelTilled = false);

public sealed record RaiseRequest
{
    public required CreatureDefinition Creature { get; init; }
    public required FoodDefinition Food { get; init; }
    public required ServerSelection ServerSelection { get; init; }
    public required ServerRates Rates { get; init; }
    public required double MaturityPercent { get; init; }
    public required double AdultWeight { get; init; }
    public required TimeSpan DesiredBuffer { get; init; }
    public double ProvisioningLossPercent { get; init; }
    public ElderclawSettings Elderclaw { get; init; } = new();
    public DateTimeOffset AsOf { get; init; } = DateTimeOffset.Now;
}

public sealed record DailyProvisioning(
    int Day,
    double FoodPoints,
    int ItemQuantity);

public sealed record LifecycleResult
{
    public required TimeSpan BirthDuration { get; init; }
    public required string BirthLabel { get; init; }
    public required TimeSpan BabyPhaseDuration { get; init; }
    public required TimeSpan JuvenilePhaseDuration { get; init; }
    public required TimeSpan AdolescentPhaseDuration { get; init; }
    public required TimeSpan JuvenileToAdultDuration { get; init; }
    public required TimeSpan BirthToAdultDuration { get; init; }
    public required TimeSpan EggOrConceptionToAdultDuration { get; init; }
    public required TimeSpan ElapsedMaturation { get; init; }
    public required TimeSpan RemainingMaturation { get; init; }
    public required TimeSpan TimeToJuvenile { get; init; }
    public required TimeSpan TimeToAdolescent { get; init; }
    public required TimeSpan TimeToAdult { get; init; }
}

public sealed record FeedingCapacityResult
{
    public required FeedingBufferStatus Status { get; init; }
    public required double CapacityWeight { get; init; }
    public required int FullItemQuantity { get; init; }
    public required TimeSpan FullInventoryDuration { get; init; }
    public required DateTimeOffset IfFilledUntil { get; init; }
    public required int SpoiledItemQuantity { get; init; }
    public required TimeSpan EffectiveTarget { get; init; }
    public required TimeSpan TimeUntilTargetAvailable { get; init; }
    public required double TargetAvailableMaturityPercent { get; init; }
    public required int TargetAvailableItemQuantity { get; init; }
    public required TimeSpan TimeUntilLastFullInventory { get; init; }
    public required double LastFullInventoryMaturityPercent { get; init; }
    public required int LastFullInventoryItemQuantity { get; init; }
    public required int FoodConsumedBeforeJuvenile { get; init; }
    public required int FoodRequiredToFillCurrentCapacity { get; init; }
}

public sealed record RaisePlan
{
    public required LifecycleResult Lifecycle { get; init; }
    public required FeedingCapacityResult Feeding { get; init; }
    public required double CurrentFoodPointsPerMinute { get; init; }
    public required double CurrentItemsPerMinute { get; init; }
    public required int FoodToJuvenile { get; init; }
    public required int FoodToAdult { get; init; }
    public required int TotalFoodFromBirth { get; init; }
    public required IReadOnlyList<DailyProvisioning> DailyProvisioning { get; init; }
}

public sealed record TroughCreatureRequest(
    CreatureDefinition Creature,
    double MaturityPercent,
    int Quantity);

public sealed record TroughFoodRequest(
    FoodDefinition Food,
    double Stacks);

public sealed record TroughRequest
{
    public required IReadOnlyList<TroughCreatureRequest> Creatures { get; init; }
    public required IReadOnlyList<TroughFoodRequest> Foods { get; init; }
    public required ServerRates Rates { get; init; }
    public required TroughType ContainerType { get; init; }
    public int ContainerCount { get; init; } = 1;
    public TimeSpan MaximumSimulation { get; init; } = TimeSpan.FromDays(3);
}

public sealed record TroughResult
{
    public required TimeSpan Coverage { get; init; }
    public required IReadOnlyDictionary<string, TimeSpan> CoverageByDiet { get; init; }
    public required int ContainerCount { get; init; }
    public required int SlotsPerContainer { get; init; }
    public required int SlotCapacity { get; init; }
    public required int UsedSlots { get; init; }
    public required int AvailableSlots { get; init; }
    public required int TotalItems { get; init; }
    public required int EatenItems { get; init; }
    public required int SpoiledItems { get; init; }
    public required double TotalFoodPoints { get; init; }
    public required double EatenFoodPoints { get; init; }
    public required double SpoiledFoodPoints { get; init; }
    public required double WastedFoodPoints { get; init; }
    public required bool SimulationCapped { get; init; }
}
