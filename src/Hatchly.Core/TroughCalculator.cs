namespace Hatchly.Core;

public sealed class TroughCalculator
{
    private const double BaseMinimumFoodRate = 0.000155;

    public TroughResult Calculate(TroughRequest request)
    {
        var profile = TroughProfiles.Get(request.ContainerType);
        var containerCount = Math.Clamp(request.ContainerCount, 1, 100);
        var slotCapacity = checked(profile.SlotCapacity * containerCount);
        var usedSlots = request.Foods.Sum(RequiredSlots);

        if (usedSlots > slotCapacity)
        {
            throw new ArgumentException(
                $"The selected food uses {usedSlots} slots, but "
                + $"{containerCount} {profile.DisplayName} container"
                + $"{(containerCount == 1 ? "" : "s")} hold {slotCapacity}.",
                nameof(request));
        }

        if (request.Creatures.Count == 0)
        {
            return Empty(containerCount, profile.SlotCapacity, slotCapacity, usedSlots);
        }

        var stacks = CreateStacks(request.Foods, profile.SpoilMultiplier);
        var totalItems = stacks.Sum(stack => stack.Count);
        var creatures = ExpandCreatures(request);
        var remainingItems = totalItems;
        var time = 0;
        var maxSeconds = Math.Max(0, (int)request.MaximumSimulation.TotalSeconds);
        var eatenItems = 0;
        var spoiledItems = 0;
        var eatenPoints = 0d;
        var spoiledPoints = 0d;
        var wastedPoints = 0d;
        var coverageByDiet = creatures
            .Select(creature => creature.DietId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToDictionary(id => id, _ => 0, StringComparer.OrdinalIgnoreCase);

        while (remainingItems > 0 && time < maxSeconds)
        {
            time++;

            foreach (var creature in creatures)
            {
                if (creature.FoodRate < creature.MinimumFoodRate)
                {
                    continue;
                }

                creature.FoodRate -= creature.DecayPerSecond;
                creature.Hunger += creature.FoodRate;

                if (creature.Hunger < 20)
                {
                    continue;
                }

                foreach (var stack in stacks)
                {
                    if (stack.Count <= 0
                        || !creature.AllowedFoodIds.Contains(stack.Food.Id))
                    {
                        continue;
                    }

                    var multiplier = creature.Definition.FoodMultiplier(stack.Food.Id);
                    var foodPoints = stack.Food.FoodValue * multiplier;
                    if (creature.Hunger > foodPoints)
                    {
                        stack.Count--;
                        remainingItems--;
                        eatenItems++;
                        eatenPoints += foodPoints;
                        wastedPoints += stack.Food.Waste
                            * creature.Definition.WasteMultiplier(stack.Food.Id);
                        creature.Hunger -= foodPoints;
                        coverageByDiet[creature.DietId] = time;
                    }

                    break;
                }
            }

            foreach (var stack in stacks)
            {
                if (stack.Count <= 0 || double.IsPositiveInfinity(stack.NextSpoil))
                {
                    continue;
                }

                stack.NextSpoil--;
                if (stack.NextSpoil <= 0)
                {
                    stack.Count--;
                    remainingItems--;
                    spoiledItems++;
                    spoiledPoints += stack.Food.FoodValue;
                    wastedPoints += stack.Food.Waste;
                    stack.NextSpoil = stack.SpoilSeconds;
                }
            }
        }

        var totalPoints = eatenPoints + spoiledPoints + wastedPoints;
        var coverageSeconds = coverageByDiet.Count == 0
            ? 0
            : coverageByDiet.Values.Min();
        return new TroughResult
        {
            Coverage = TimeSpan.FromSeconds(coverageSeconds),
            CoverageByDiet = coverageByDiet.ToDictionary(
                entry => entry.Key,
                entry => TimeSpan.FromSeconds(entry.Value),
                StringComparer.OrdinalIgnoreCase),
            ContainerCount = containerCount,
            SlotsPerContainer = profile.SlotCapacity,
            SlotCapacity = slotCapacity,
            UsedSlots = usedSlots,
            AvailableSlots = slotCapacity - usedSlots,
            TotalItems = totalItems,
            EatenItems = eatenItems,
            SpoiledItems = spoiledItems,
            TotalFoodPoints = totalPoints,
            EatenFoodPoints = eatenPoints,
            SpoiledFoodPoints = spoiledPoints,
            WastedFoodPoints = wastedPoints,
            SimulationCapped = remainingItems > 0 && time >= maxSeconds
        };
    }

    private static List<SimulatedCreature> ExpandCreatures(TroughRequest request)
    {
        var result = new List<SimulatedCreature>();

        foreach (var row in request.Creatures)
        {
            var quantity = Math.Clamp(row.Quantity, 0, 500);
            for (var i = 0; i < quantity; i++)
            {
                var maturationSeconds = 1d
                    / row.Creature.AgeSpeed
                    / row.Creature.AgeSpeedMultiplier
                    / request.Rates.MaturationSpeed;
                var maxFoodRate = row.Creature.BaseFoodRate
                    * row.Creature.BabyFoodRateMultiplier
                    * row.Creature.ExtraBabyFoodRateMultiplier
                    * request.Rates.ConsumptionSpeed;
                var minimumFoodRate = BaseMinimumFoodRate
                    * row.Creature.BabyFoodRateMultiplier
                    * row.Creature.ExtraBabyFoodRateMultiplier
                    * request.Rates.ConsumptionSpeed;
                var decay = (maxFoodRate - minimumFoodRate) / maturationSeconds;
                var maturity = Math.Clamp(row.MaturityPercent / 100d, 0, 1);

                result.Add(new SimulatedCreature
                {
                    Definition = row.Creature,
                    DietId = row.Creature.DietId,
                    AllowedFoodIds = row.Creature.RaisingFoodIds
                        .ToHashSet(StringComparer.OrdinalIgnoreCase),
                    FoodRate = maxFoodRate - decay * maturity * maturationSeconds,
                    MinimumFoodRate = minimumFoodRate,
                    DecayPerSecond = decay
                });
            }
        }

        return result;
    }

    private static List<TroughStack> CreateStacks(
        IReadOnlyList<TroughFoodRequest> foods,
        double spoilMultiplier)
    {
        var result = new List<TroughStack>();
        foreach (var row in foods)
        {
            var itemQuantity = Math.Max(
                0,
                (int)Math.Floor(row.Stacks * row.Food.StackSize));
            while (itemQuantity > 0)
            {
                var count = Math.Min(row.Food.StackSize, itemQuantity);
                var spoilSeconds = row.Food.SpoilSeconds * spoilMultiplier;
                result.Add(new TroughStack
                {
                    Food = row.Food,
                    Count = count,
                    SpoilSeconds = spoilSeconds,
                    NextSpoil = spoilSeconds >= 86400 * 365
                        ? double.PositiveInfinity
                        : spoilSeconds
                });
                itemQuantity -= count;
            }
        }

        return result;
    }

    private static int RequiredSlots(TroughFoodRequest request)
    {
        var itemQuantity = Math.Max(
            0,
            (int)Math.Floor(request.Stacks * request.Food.StackSize));
        return itemQuantity == 0
            ? 0
            : (int)Math.Ceiling(itemQuantity / (double)request.Food.StackSize);
    }

    private static TroughResult Empty(
        int containerCount,
        int slotsPerContainer,
        int slotCapacity,
        int usedSlots) => new()
    {
        Coverage = TimeSpan.Zero,
        CoverageByDiet = new Dictionary<string, TimeSpan>(),
        ContainerCount = containerCount,
        SlotsPerContainer = slotsPerContainer,
        SlotCapacity = slotCapacity,
        UsedSlots = usedSlots,
        AvailableSlots = slotCapacity - usedSlots,
        TotalItems = 0,
        EatenItems = 0,
        SpoiledItems = 0,
        TotalFoodPoints = 0,
        EatenFoodPoints = 0,
        SpoiledFoodPoints = 0,
        WastedFoodPoints = 0,
        SimulationCapped = false
    };

    private sealed class SimulatedCreature
    {
        public required CreatureDefinition Definition { get; init; }
        public required string DietId { get; init; }
        public required HashSet<string> AllowedFoodIds { get; init; }
        public double FoodRate { get; set; }
        public double MinimumFoodRate { get; init; }
        public double DecayPerSecond { get; init; }
        public double Hunger { get; set; }
    }

    private sealed class TroughStack
    {
        public required FoodDefinition Food { get; init; }
        public int Count { get; set; }
        public double SpoilSeconds { get; init; }
        public double NextSpoil { get; set; }
    }
}
