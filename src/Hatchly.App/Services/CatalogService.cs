using System.Net.Http.Json;
using System.Text.Json;
using Hatchly.Core;

namespace Hatchly.App.Services;

public sealed class CatalogService(HttpClient http)
{
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
        try
        {
            Catalog = await http.GetFromJsonAsync<DataCatalog>(
                $"data/catalog.json?v={DateTimeOffset.UtcNow.ToUnixTimeSeconds()}",
                JsonOptions);
            if (Catalog is null)
            {
                throw new InvalidDataException("The merged Hatchly data file was empty.");
            }

            DataCatalogMerger.Validate(Catalog);
        }
        catch (Exception exception)
        {
            Error = exception.Message;
        }
    }
}
