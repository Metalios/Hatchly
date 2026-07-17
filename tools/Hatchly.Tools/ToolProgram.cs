using System.Text.Json;
using Hatchly.Core;

namespace Hatchly.Tools;

public static class ToolProgram
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0)
        {
            PrintUsage();
            return 2;
        }

        try
        {
            return args[0].ToLowerInvariant() switch
            {
                "sync-rates" => await SyncRatesAsync(args[1..]),
                "merge-data" => await MergeDataAsync(args[1..]),
                "validate-data" => await ValidateDataAsync(args[1..]),
                "audit-publish" => AuditPublish(args[1..]),
                "prepare-pages" => PreparePages(args[1..]),
                _ => UnknownCommand(args[0])
            };
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }

    private static async Task<int> SyncRatesAsync(string[] args)
    {
        var output = RequiredOption(args, "--output");
        using var http = new HttpClient();
        var synchronizer = new OfficialRateSynchronizer(http);
        var result = await synchronizer.SynchronizeAsync(output);
        Console.WriteLine(result.Changed
            ? $"Official rates changed; wrote {output}."
            : "Official rate values are unchanged.");
        return 0;
    }

    private static async Task<int> MergeDataAsync(string[] args)
    {
        var dataDirectory = RequiredOption(args, "--data-dir");
        var output = RequiredOption(args, "--output");
        var catalog = await LoadCatalogAsync(dataDirectory);
        await AtomicJson.WriteAsync(output, catalog, JsonOptions);
        Console.WriteLine(
            $"Merged {catalog.Creatures.Count} creatures, {catalog.Foods.Count} foods, and {catalog.Diets.Count} diets.");
        return 0;
    }

    private static async Task<int> ValidateDataAsync(string[] args)
    {
        var dataDirectory = RequiredOption(args, "--data-dir");
        var catalog = await LoadCatalogAsync(dataDirectory);
        Console.WriteLine(
            $"Validated {catalog.Creatures.Count} creatures, {catalog.Foods.Count} foods, and {catalog.Diets.Count} diets.");
        return 0;
    }

    private static int AuditPublish(string[] args)
    {
        var path = RequiredOption(args, "--path");
        var result = PublishAuditor.Audit(path);
        Console.WriteLine($"Framework Brotli payload: {PublishAuditor.FormatBytes(result.FrameworkBrotliBytes)}");
        Console.WriteLine($"Header branding: {PublishAuditor.FormatBytes(result.HeaderBrandingBytes)}");
        Console.WriteLine($"Cold transfer: {PublishAuditor.FormatBytes(result.ColdTransferBytes)}");
        return 0;
    }

    private static int PreparePages(string[] args)
    {
        var publishPath = RequiredOption(args, "--publish-path");
        var intermediateIndex = RequiredOption(args, "--intermediate-index");
        var basePath = OptionalOption(args, "--base-path") ?? "/";
        var result = PagesPreparer.Prepare(publishPath, intermediateIndex, basePath);
        Console.WriteLine(
            $"Prepared GitHub Pages at '{result.WebRoot}' with base href '{result.BaseHref}'.");
        return 0;
    }

    public static async Task<DataCatalog> LoadCatalogAsync(string dataDirectory)
    {
        var creatures = await ReadAsync<CreatureFile>(
            Path.Combine(dataDirectory, "creatures.generated.json"));
        var foods = await ReadAsync<FoodFile>(
            Path.Combine(dataDirectory, "foods.generated.json"));
        var foodOverrides = await ReadAsync<FoodOverrideFile>(
            Path.Combine(dataDirectory, "food-overrides.json"));
        var diets = await ReadAsync<DietFile>(Path.Combine(dataDirectory, "diets.json"));
        var overrides = await ReadAsync<OverrideFile>(
            Path.Combine(dataDirectory, "creature-overrides.json"));
        var creatureIds = creatures.Creatures
            .Select(item => item.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unknownOverride = overrides.Overrides.FirstOrDefault(
            item => !creatureIds.Contains(item.CreatureId));
        if (unknownOverride is not null)
        {
            throw new InvalidDataException(
                $"Override references missing creature '{unknownOverride.CreatureId}'.");
        }

        var generatedFoodIds = foods.Foods
            .Select(item => item.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unknownFoodOverride = foodOverrides.Overrides.FirstOrDefault(
            item => !generatedFoodIds.Contains(item.FoodId));
        if (unknownFoodOverride is not null)
        {
            throw new InvalidDataException(
                $"Food override references missing food '{unknownFoodOverride.FoodId}'.");
        }

        foreach (var creatureOverride in overrides.Overrides)
        {
            var referencedFoodIds = creatureOverride.IncludeFoodIds
                .Concat(creatureOverride.ExcludeFoodIds)
                .Concat(creatureOverride.FoodMultipliers.Keys)
                .Concat(creatureOverride.WasteMultipliers.Keys)
                .Distinct(StringComparer.OrdinalIgnoreCase);
            var unknownFoodId = referencedFoodIds.FirstOrDefault(
                item => !generatedFoodIds.Contains(item));
            if (unknownFoodId is not null)
            {
                throw new InvalidDataException(
                    $"Creature override '{creatureOverride.CreatureId}' references missing food '{unknownFoodId}'.");
            }

            var conflictingFoodId = creatureOverride.IncludeFoodIds.FirstOrDefault(
                item => creatureOverride.ExcludeFoodIds.Contains(
                    item,
                    StringComparer.OrdinalIgnoreCase));
            if (conflictingFoodId is not null)
            {
                throw new InvalidDataException(
                    $"Creature override '{creatureOverride.CreatureId}' both includes and excludes food '{conflictingFoodId}'.");
            }
        }

        var catalog = new DataCatalog
        {
            SchemaVersion = 1,
            Creatures = DataCatalogMerger.ApplyOverrides(
                    creatures.Creatures,
                    overrides.Overrides)
                .OrderBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            Foods = DataCatalogMerger.ApplyFoodOverrides(
                foods.Foods,
                foodOverrides.Overrides),
            Diets = diets.Diets
                .OrderBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
                .ToArray()
        };
        DataCatalogMerger.Validate(catalog);
        return catalog;
    }

    private static async Task<T> ReadAsync<T>(string path)
    {
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions)
            ?? throw new InvalidDataException($"'{path}' was empty.");
    }

    private static string RequiredOption(string[] args, string name)
    {
        var index = Array.FindIndex(
            args,
            item => item.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (index < 0 || index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
        {
            throw new ArgumentException($"Required option '{name}' was not supplied.");
        }

        return Path.GetFullPath(args[index + 1]);
    }

    private static string? OptionalOption(string[] args, string name)
    {
        var index = Array.FindIndex(
            args,
            item => item.Equals(name, StringComparison.OrdinalIgnoreCase));
        return index < 0 || index + 1 >= args.Length ? null : args[index + 1];
    }

    private static int UnknownCommand(string command)
    {
        Console.Error.WriteLine($"Unknown command '{command}'.");
        PrintUsage();
        return 2;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Hatchly.Tools commands:");
        Console.WriteLine("  sync-rates --output <official-rates.json>");
        Console.WriteLine("  merge-data --data-dir <directory> --output <catalog.json>");
        Console.WriteLine("  validate-data --data-dir <directory>");
        Console.WriteLine("  audit-publish --path <publish directory or wwwroot>");
        Console.WriteLine("  prepare-pages --publish-path <directory> --intermediate-index <index.html> [--base-path <path>]");
    }

    private sealed record CreatureFile
    {
        public required IReadOnlyList<CreatureDefinition> Creatures { get; init; }
    }

    private sealed record FoodFile
    {
        public required IReadOnlyList<FoodDefinition> Foods { get; init; }
    }

    private sealed record FoodOverrideFile
    {
        public required IReadOnlyList<FoodOverride> Overrides { get; init; }
    }

    private sealed record DietFile
    {
        public required IReadOnlyList<DietDefinition> Diets { get; init; }
    }

    private sealed record OverrideFile
    {
        public required IReadOnlyList<CreatureOverride> Overrides { get; init; }
    }
}
