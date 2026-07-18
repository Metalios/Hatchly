using Hatchly.Core;
using Hatchly.Tools;

namespace Hatchly.Core.Tests;

public sealed class CatalogRegressionTests
{
    [Fact]
    public async Task Acrocanthosaurus_matches_known_one_x_maturation_reference()
    {
        var catalog = await ToolProgram.LoadCatalogAsync(FindDataDirectory());
        var acro = catalog.Creatures.Single(
            creature => creature.Id == "acrocanthosaurus");
        var rawMeat = catalog.Foods.Single(food => food.Id == "raw-meat");

        var plan = new RaiseCalculator().Calculate(new RaiseRequest
        {
            Creature = acro,
            Food = rawMeat,
            ServerSelection = ServerSelection.Unofficial,
            Rates = new ServerRates(1, 1, 1),
            MaturityPercent = 0,
            AdultWeight = acro.AdultWeight,
            DesiredBuffer = TimeSpan.FromHours(1),
            AsOf = new DateTimeOffset(2026, 7, 17, 12, 0, 0, TimeSpan.Zero)
        });

        AssertWithinOneSecond(
            TimeSpan.FromDays(3)
            + TimeSpan.FromHours(20)
            + TimeSpan.FromMinutes(35)
            + TimeSpan.FromSeconds(33),
            plan.Lifecycle.BirthToAdultDuration);
        AssertWithinOneSecond(
            TimeSpan.FromHours(9)
            + TimeSpan.FromMinutes(15)
            + TimeSpan.FromSeconds(33),
            plan.Lifecycle.BabyPhaseDuration);
        AssertWithinOneSecond(
            TimeSpan.FromHours(4)
            + TimeSpan.FromMinutes(59)
            + TimeSpan.FromSeconds(59),
            plan.Lifecycle.BirthDuration);
    }

    private static void AssertWithinOneSecond(TimeSpan expected, TimeSpan actual) =>
        Assert.InRange(Math.Abs((actual - expected).TotalSeconds), 0, 1);

    private static string FindDataDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(
                directory.FullName,
                "src",
                "Hatchly.App",
                "wwwroot",
                "data");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate src/Hatchly.App/wwwroot/data from the test output directory.");
    }
}
