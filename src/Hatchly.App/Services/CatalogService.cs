using System.Net.Http.Json;
using System.Text.Json;
using Hatchly.Core;

namespace Hatchly.App.Services;

public sealed class CatalogService(HttpClient http)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public DataCatalog? Catalog { get; private set; }
    public string? Error { get; private set; }

    public async Task EnsureLoadedAsync()
    {
        if (Catalog is not null || Error is not null)
        {
            return;
        }

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
