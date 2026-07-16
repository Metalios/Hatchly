namespace Hatchly.Core;

public sealed class RaiseCalculator
{
    private const double BaseMinimumFoodRate = 0.000155;

    public RaisePlan Calculate(RaiseRequest request)
    {
        Validate(request);

        var creature = request.Creature;
        var maturity = Math.Clamp(request.MaturityPercent / 100d, 0, 1);
        var maturationSeconds = 1d
            / creature.AgeSpeed
            / creature.AgeSpeedMultiplier
            / request.Rates.MaturationSpeed;
        var juvenileSeconds = maturationSeconds * creature.JuvenileThreshold;
        var elapsedSeconds = maturationSeconds * maturity;
        var remainingSeconds = Math.Max(0, maturationSeconds - elapsedSeconds);
        var toJuvenileSeconds = Math.Max(0, juvenileSeconds - elapsedSeconds);
        var birthSeconds = CalculateBirthSeconds(request);

        var maxFoodRate = creature.BaseFoodRate
            * creature.BabyFoodRateMultiplier
            * creature.ExtraBabyFoodRateMultiplier
            * request.Rates.ConsumptionSpeed;
        var minFoodRate = BaseMinimumFoodRate
            * creature.BabyFoodRateMultiplier
            * creature.ExtraBabyFoodRateMultiplier
            * request.Rates.ConsumptionSpeed;
        var decayPerSecond = (maxFoodRate - minFoodRate) / maturationSeconds;
        var foodMultiplier = creature.FoodMultiplier(request.Food.Id);

        double FoodPointsForPeriod(double start, double end)
        {
            end = Math.Min(maturationSeconds, Math.Max(start, end));
            var startRate = maxFoodRate - decayPerSecond * start;
            var endRate = maxFoodRate - decayPerSecond * end;
            var duration = end - start;
            return 0.5 * duration * (startRate - endRate) + endRate * duration;
        }

        int ItemsForPeriod(double start, double end, double lossPercent = 0)
        {
            var points = FoodPointsForPeriod(start, end);
            points *= 1 + Math.Max(0, lossPercent) / 100d;
            var pointsPerItem = request.Food.FoodValue * foodMultiplier;
            return pointsPerItem <= 0 ? 0 : (int)Math.Ceiling(points / pointsPerItem);
        }

        var capacity = CalculateCapacity(
            request,
            maturity,
            maturationSeconds,
            juvenileSeconds,
            maxFoodRate,
            minFoodRate,
            decayPerSecond);

        var daily = new List<DailyProvisioning>();
        for (var day = 1; day <= 60; day++)
        {
            var start = elapsedSeconds + (day - 1) * TimeSpan.FromDays(1).TotalSeconds;
            var end = elapsedSeconds + day * TimeSpan.FromDays(1).TotalSeconds;
            if (start >= maturationSeconds)
            {
                break;
            }

            var points = FoodPointsForPeriod(start, end);
            daily.Add(new DailyProvisioning(
                day,
                points,
                ItemsForPeriod(start, end, request.ProvisioningLossPercent)));
        }

        var currentRate = Math.Max(minFoodRate, maxFoodRate - decayPerSecond * elapsedSeconds);
        var pointsPerMinute = currentRate * 60;

        return new RaisePlan
        {
            Lifecycle = new LifecycleResult
            {
                BirthDuration = TimeSpan.FromSeconds(birthSeconds),
                BirthLabel = creature.BirthMethod switch
                {
                    BirthMethod.Gestation => "Gestation",
                    BirthMethod.CropPlotIncubation => "Crop plot incubation",
                    _ => "Incubation"
                },
                BabyPhaseDuration = TimeSpan.FromSeconds(juvenileSeconds),
                JuvenileToAdultDuration = TimeSpan.FromSeconds(maturationSeconds - juvenileSeconds),
                BirthToAdultDuration = TimeSpan.FromSeconds(maturationSeconds),
                EggOrConceptionToAdultDuration = TimeSpan.FromSeconds(birthSeconds + maturationSeconds),
                ElapsedMaturation = TimeSpan.FromSeconds(elapsedSeconds),
                RemainingMaturation = TimeSpan.FromSeconds(remainingSeconds),
                TimeToJuvenile = TimeSpan.FromSeconds(toJuvenileSeconds),
                TimeToAdult = TimeSpan.FromSeconds(remainingSeconds)
            },
            Feeding = capacity,
            CurrentFoodPointsPerMinute = pointsPerMinute,
            CurrentItemsPerMinute = pointsPerMinute / (request.Food.FoodValue * foodMultiplier),
            FoodToJuvenile = ItemsForPeriod(elapsedSeconds, juvenileSeconds),
            FoodToAdult = ItemsForPeriod(elapsedSeconds, maturationSeconds),
            TotalFoodFromBirth = ItemsForPeriod(0, maturationSeconds),
            DailyProvisioning = daily
        };
    }

    private static FeedingCapacityResult CalculateCapacity(
        RaiseRequest request,
        double maturity,
        double maturationSeconds,
        double juvenileSeconds,
        double maxFoodRate,
        double minFoodRate,
        double decayPerSecond)
    {
        var creature = request.Creature;
        var food = request.Food;
        var threshold = creature.JuvenileThreshold;
        var elapsed = maturity * maturationSeconds;
        var remainingToJuvenile = Math.Max(0, juvenileSeconds - elapsed);

        if (maturity >= threshold)
        {
            return new FeedingCapacityResult
            {
                Status = FeedingBufferStatus.Juvenile,
                CapacityWeight = request.AdultWeight * maturity,
                FullItemQuantity = 0,
                FullInventoryDuration = TimeSpan.Zero,
                IfFilledUntil = request.AsOf,
                SpoiledItemQuantity = 0,
                EffectiveTarget = TimeSpan.Zero,
                TimeUntilTargetAvailable = TimeSpan.Zero,
                TargetAvailableMaturityPercent = maturity * 100,
                TargetAvailableItemQuantity = 0,
                TimeUntilLastFullInventory = TimeSpan.Zero,
                LastFullInventoryMaturityPercent = maturity * 100,
                FoodConsumedBeforeJuvenile = 0,
                FoodRequiredToFillCurrentCapacity = 0
            };
        }

        CoverageSimulation CoverageAt(double atMaturity)
        {
            var capacityWeight = request.AdultWeight * atMaturity;
            var items = food.ItemWeight <= 0
                ? 0
                : Math.Max(0, (int)Math.Floor(capacityWeight / food.ItemWeight));
            var secondsToJuvenile = Math.Max(
                0,
                juvenileSeconds - atMaturity * maturationSeconds);

            return InventoryCoverageSimulator.Simulate(
                creature,
                food,
                items,
                atMaturity,
                maturationSeconds,
                maxFoodRate,
                minFoodRate,
                decayPerSecond,
                TimeSpan.FromSeconds(secondsToJuvenile));
        }

        var currentCapacityWeight = request.AdultWeight * maturity;
        var currentItems = food.ItemWeight <= 0
            ? 0
            : Math.Max(0, (int)Math.Floor(currentCapacityWeight / food.ItemWeight));
        var currentCoverage = CoverageAt(maturity);
        var desiredSeconds = Math.Max(0, request.DesiredBuffer.TotalSeconds);
        var effectiveTarget = Math.Min(desiredSeconds, remainingToJuvenile);
        var carries = remainingToJuvenile > 0
            && currentCoverage.Duration.TotalSeconds >= remainingToJuvenile - 0.5;

        var status = carries
            ? FeedingBufferStatus.CarriesToJuvenile
            : currentCoverage.Duration.TotalSeconds >= effectiveTarget - 0.5
                ? FeedingBufferStatus.TargetMet
                : FeedingBufferStatus.TargetAvailableLater;

        var targetMaturity = maturity;
        var targetCoverage = currentCoverage;
        if (status == FeedingBufferStatus.TargetAvailableLater)
        {
            (targetMaturity, targetCoverage) = FindEarliestMaturity(
                maturity,
                threshold,
                candidate =>
                {
                    var coverage = CoverageAt(candidate);
                    var remaining = Math.Max(
                        0,
                        juvenileSeconds - candidate * maturationSeconds);
                    var target = Math.Min(desiredSeconds, remaining);
                    return coverage.Duration.TotalSeconds >= target - 0.5;
                },
                CoverageAt);
        }

        var (lastFullMaturity, _) = FindEarliestMaturity(
            maturity,
            threshold,
            candidate =>
            {
                var coverage = CoverageAt(candidate);
                var remaining = Math.Max(
                    0,
                    juvenileSeconds - candidate * maturationSeconds);
                return coverage.Duration.TotalSeconds >= remaining - 0.5;
            },
            CoverageAt);

        return new FeedingCapacityResult
        {
            Status = status,
            CapacityWeight = currentCapacityWeight,
            FullItemQuantity = currentItems,
            FullInventoryDuration = currentCoverage.Duration,
            IfFilledUntil = request.AsOf.Add(currentCoverage.Duration),
            SpoiledItemQuantity = currentCoverage.SpoiledItems,
            EffectiveTarget = TimeSpan.FromSeconds(effectiveTarget),
            TimeUntilTargetAvailable = TimeSpan.FromSeconds(
                Math.Max(0, (targetMaturity - maturity) * maturationSeconds)),
            TargetAvailableMaturityPercent = targetMaturity * 100,
            TargetAvailableItemQuantity = targetCoverage.InitialItems,
            TimeUntilLastFullInventory = TimeSpan.FromSeconds(
                Math.Max(0, (lastFullMaturity - maturity) * maturationSeconds)),
            LastFullInventoryMaturityPercent = lastFullMaturity * 100,
            FoodConsumedBeforeJuvenile = ItemsForRemainingBaby(
                creature,
                food,
                elapsed,
                juvenileSeconds,
                maxFoodRate,
                decayPerSecond),
            FoodRequiredToFillCurrentCapacity = currentItems
        };
    }

    private static int ItemsForRemainingBaby(
        CreatureDefinition creature,
        FoodDefinition food,
        double start,
        double end,
        double maxFoodRate,
        double decayPerSecond)
    {
        if (end <= start)
        {
            return 0;
        }

        var startRate = maxFoodRate - decayPerSecond * start;
        var endRate = maxFoodRate - decayPerSecond * end;
        var points = 0.5 * (end - start) * (startRate + endRate);
        var pointsPerItem = food.FoodValue * creature.FoodMultiplier(food.Id);
        return pointsPerItem <= 0 ? 0 : (int)Math.Ceiling(points / pointsPerItem);
    }

    private static (double Maturity, CoverageSimulation Coverage) FindEarliestMaturity(
        double start,
        double end,
        Func<double, bool> predicate,
        Func<double, CoverageSimulation> coverage)
    {
        if (predicate(start))
        {
            return (start, coverage(start));
        }

        var low = start;
        var high = end;
        for (var i = 0; i < 24; i++)
        {
            var middle = (low + high) / 2;
            if (predicate(middle))
            {
                high = middle;
            }
            else
            {
                low = middle;
            }
        }

        return (high, coverage(high));
    }

    private static double CalculateBirthSeconds(RaiseRequest request)
    {
        var creature = request.Creature;
        var seconds = creature.SpecialBehavior == "elderclaw-crop-plot"
            ? creature.GestationSpeed is > 0
                && creature.GestationSpeedMultiplier is > 0
                ? 1d
                    / creature.GestationSpeed.Value
                    / creature.GestationSpeedMultiplier.Value
                    / request.Rates.HatchSpeed
                : creature.EggSpeed is > 0 && creature.EggSpeedMultiplier is > 0
                    ? 100d
                        / creature.EggSpeed.Value
                        / creature.EggSpeedMultiplier.Value
                        / request.Rates.HatchSpeed
                    : 0
            : creature.BirthMethod switch
        {
            BirthMethod.Gestation when creature.GestationSpeed is > 0
                && creature.GestationSpeedMultiplier is > 0 =>
                1d
                / creature.GestationSpeed.Value
                / creature.GestationSpeedMultiplier.Value
                / request.Rates.HatchSpeed,
            _ when creature.EggSpeed is > 0 && creature.EggSpeedMultiplier is > 0 =>
                100d
                / creature.EggSpeed.Value
                / creature.EggSpeedMultiplier.Value
                / request.Rates.HatchSpeed,
            _ => 0
        };

        if (creature.SpecialBehavior == "elderclaw-crop-plot")
        {
            var greenhouse = Math.Clamp(request.Elderclaw.GreenhousePercent, 0, 300) / 100d;
            var shovel = request.Elderclaw.ShovelTilled ? 1.3 : 1;
            seconds /= Math.Max(1, greenhouse * shovel);
        }

        return seconds;
    }

    private static void Validate(RaiseRequest request)
    {
        if (request.Rates.HatchSpeed <= 0
            || request.Rates.MaturationSpeed <= 0
            || request.Rates.ConsumptionSpeed <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                "All server rates must be greater than zero.");
        }

        if (request.AdultWeight <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request.AdultWeight),
                "Adult weight must be greater than zero.");
        }

        if (request.Food.FoodValue <= 0
            || request.Food.ItemWeight <= 0
            || request.Food.StackSize <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request.Food),
                "Food values, item weight and stack size must be greater than zero.");
        }
    }
}

internal sealed record CoverageSimulation(
    TimeSpan Duration,
    int InitialItems,
    int EatenItems,
    int SpoiledItems);

internal static class InventoryCoverageSimulator
{
    public static CoverageSimulation Simulate(
        CreatureDefinition creature,
        FoodDefinition food,
        int itemQuantity,
        double maturity,
        double maturationSeconds,
        double maxFoodRate,
        double minFoodRate,
        double decayPerSecond,
        TimeSpan maximumDuration)
    {
        if (itemQuantity <= 0 || maximumDuration <= TimeSpan.Zero)
        {
            return new CoverageSimulation(TimeSpan.Zero, itemQuantity, 0, 0);
        }

        var stacks = CreateStacks(itemQuantity, food.StackSize, food.SpoilSeconds);
        var remainingItems = itemQuantity;
        var eaten = 0;
        var spoiled = 0;
        var hunger = 0d;
        var foodRate = maxFoodRate - decayPerSecond * maturity * maturationSeconds;
        var foodPoints = food.FoodValue * creature.FoodMultiplier(food.Id);
        var limit = Math.Max(0, (int)Math.Ceiling(maximumDuration.TotalSeconds));
        var time = 0;

        while (remainingItems > 0 && time < limit)
        {
            time++;
            foodRate = Math.Max(minFoodRate, foodRate - decayPerSecond);
            hunger += foodRate;

            if (hunger > foodPoints)
            {
                var stack = stacks.FirstOrDefault(candidate => candidate.Count > 0);
                if (stack is not null)
                {
                    stack.Count--;
                    remainingItems--;
                    eaten++;
                    hunger -= foodPoints;
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
                    spoiled++;
                    stack.NextSpoil = food.SpoilSeconds;
                }
            }
        }

        return new CoverageSimulation(
            TimeSpan.FromSeconds(time),
            itemQuantity,
            eaten,
            spoiled);
    }

    private static List<FoodStack> CreateStacks(
        int itemQuantity,
        int stackSize,
        double spoilSeconds)
    {
        var stacks = new List<FoodStack>();
        var remaining = itemQuantity;
        while (remaining > 0)
        {
            var count = Math.Min(stackSize, remaining);
            stacks.Add(new FoodStack
            {
                Count = count,
                NextSpoil = spoilSeconds >= 86400 * 365
                    ? double.PositiveInfinity
                    : spoilSeconds
            });
            remaining -= count;
        }

        return stacks;
    }

    private sealed class FoodStack
    {
        public int Count { get; set; }
        public double NextSpoil { get; set; }
    }
}
