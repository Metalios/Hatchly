using System.Text;
using System.Text.Json;
using Hatchly.Core;
using Microsoft.JSInterop;

namespace Hatchly.App.Services;

public sealed class CatalogService(HttpClient http, IJSRuntime js)
{
    private const string CatalogPointerKey = "hatchly.catalog.current";
    private const string CatalogCachePrefix = "hatchly.catalog.schema-1.";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private Task? loadingTask;

    public DataCatalog? Catalog { get; private set; }
    public string? Error { get; private set; }

    public Task EnsureLoadedAsync()
    {
        loadingTask ??= LoadAsync();
        return loadingTask;
    }

    private async Task LoadAsync()
    {
        var loadedFromCache = await TryLoadCachedAsync();
        if (loadedFromCache)
        {
            _ = RefreshAsync();
            return;
        }

        await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        try
        {
            var json = await http.GetStringAsync("data/catalog.json");
            var catalog = ParseAndValidate(json);
            Catalog = catalog;
            Error = null;
            await StoreCachedAsync(json, catalog.SchemaVersion);
        }
        catch (Exception exception)
        {
            if (Catalog is null)
            {
                Error = exception.Message;
            }
        }
    }

    private async Task<bool> TryLoadCachedAsync()
    {
        try
        {
            var cacheKey = await js.InvokeAsync<string?>(
                "hatchlyStorage.get",
                CatalogPointerKey);
            if (string.IsNullOrWhiteSpace(cacheKey)
                || !cacheKey.StartsWith(CatalogCachePrefix, StringComparison.Ordinal))
            {
                return false;
            }

            var json = await js.InvokeAsync<string?>("hatchlyStorage.get", cacheKey);
            if (string.IsNullOrWhiteSpace(json))
            {
                return false;
            }

            Catalog = ParseAndValidate(json);
            Error = null;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task StoreCachedAsync(string json, int schemaVersion)
    {
        try
        {
            var previousKey = await js.InvokeAsync<string?>(
                "hatchlyStorage.get",
                CatalogPointerKey);
            var cacheKey = $"hatchly.catalog.schema-{schemaVersion}.{ContentVersion(json)}";
            await js.InvokeVoidAsync("hatchlyStorage.set", cacheKey, json);
            await js.InvokeVoidAsync("hatchlyStorage.set", CatalogPointerKey, cacheKey);
            if (!string.IsNullOrWhiteSpace(previousKey)
                && previousKey.StartsWith("hatchly.catalog.schema-", StringComparison.Ordinal)
                && !previousKey.Equals(cacheKey, StringComparison.Ordinal))
            {
                await js.InvokeVoidAsync("hatchlyStorage.remove", previousKey);
            }
        }
        catch
        {
            // Browser-local caching is an optimization; the validated network data remains usable.
        }
    }

    private static DataCatalog ParseAndValidate(string json)
    {
        var catalog = JsonSerializer.Deserialize<DataCatalog>(json, JsonOptions)
            ?? throw new InvalidDataException("The merged Hatchly data file was empty.");
        if (catalog.SchemaVersion != 1)
        {
            throw new InvalidDataException("The Hatchly creature catalog uses an unsupported schema.");
        }

        DataCatalogMerger.Validate(catalog);
        return catalog;
    }

    private static string ContentVersion(string value)
    {
        const uint offset = 2166136261;
        const uint prime = 16777619;
        var hash = offset;
        foreach (var item in Encoding.UTF8.GetBytes(value))
        {
            hash ^= item;
            hash *= prime;
        }

        return hash.ToString("x8", System.Globalization.CultureInfo.InvariantCulture);
    }
}
