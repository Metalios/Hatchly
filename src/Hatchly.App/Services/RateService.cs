using System.Text.Json;
using Hatchly.Core;
using Microsoft.JSInterop;

namespace Hatchly.App.Services;

public sealed class RateService(HttpClient http, IJSRuntime js)
{
    private const string OfficialCacheKey = "hatchly.official-rates";
    private const string UnofficialKey = "hatchly.unofficial-rates";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public event Action? Changed;

    public OfficialRatesDocument? OfficialRates { get; private set; }
    public ServerSelection SelectedServer { get; private set; } = ServerSelection.Standard;
    public ServerRates UnofficialRates { get; private set; } = new(1, 1, 1);
    public bool UsingCachedOfficialRates { get; private set; }
    public string? Error { get; private set; }
    public bool IsInitialized { get; private set; }

    public ServerRates? CurrentRates =>
        SelectedServer == ServerSelection.Unofficial
            ? UnofficialRates
            : FindProfile(SelectedServer)?.ToServerRates();

    public OfficialRateProfile? CurrentOfficialProfile =>
        SelectedServer == ServerSelection.Unofficial
            ? null
            : FindProfile(SelectedServer);

    public async Task EnsureLoadedAsync()
    {
        if (IsInitialized)
        {
            return;
        }

        IsInitialized = true;
        await LoadPreferencesAsync();

        try
        {
            var json = await http.GetStringAsync(
                $"data/official-rates.json?v={DateTimeOffset.UtcNow.ToUnixTimeSeconds()}");
            var document = ParseAndValidate(json);
            OfficialRates = document;
            UsingCachedOfficialRates = false;
            Error = null;
            await js.InvokeVoidAsync("hatchlyStorage.set", OfficialCacheKey, json);
        }
        catch (Exception networkException)
        {
            try
            {
                var cached = await js.InvokeAsync<string?>(
                    "hatchlyStorage.get",
                    OfficialCacheKey);
                if (string.IsNullOrWhiteSpace(cached))
                {
                    throw new InvalidDataException("No cached official rates are available.");
                }

                OfficialRates = ParseAndValidate(cached);
                UsingCachedOfficialRates = true;
                Error = "Using cached official rates.";
            }
            catch
            {
                OfficialRates = null;
                Error = $"Official rates could not be loaded: {networkException.Message}";
            }
        }

        Changed?.Invoke();
    }

    public async Task SelectAsync(ServerSelection selection)
    {
        SelectedServer = selection;
        Changed?.Invoke();
        await Task.CompletedTask;
    }

    public async Task SaveUnofficialAsync(ServerRates rates)
    {
        if (!IsValid(rates))
        {
            throw new ArgumentOutOfRangeException(
                nameof(rates),
                "All unofficial rates must be greater than zero.");
        }

        UnofficialRates = rates;
        await js.InvokeVoidAsync(
            "hatchlyStorage.set",
            UnofficialKey,
            JsonSerializer.Serialize(rates, JsonOptions));
        Changed?.Invoke();
    }

    private async Task LoadPreferencesAsync()
    {
        var unofficial = await js.InvokeAsync<string?>("hatchlyStorage.get", UnofficialKey);
        if (!string.IsNullOrWhiteSpace(unofficial))
        {
            try
            {
                var rates = JsonSerializer.Deserialize<ServerRates>(unofficial, JsonOptions);
                if (rates is not null && IsValid(rates))
                {
                    UnofficialRates = rates;
                }
            }
            catch (JsonException)
            {
                // Invalid browser-local preferences are ignored.
            }
        }
    }

    private OfficialRateProfile? FindProfile(ServerSelection selection)
    {
        var id = selection switch
        {
            ServerSelection.Apocalypse => "apocalypse",
            ServerSelection.SmallTribes => "small-tribes",
            ServerSelection.Conquest => "conquest",
            _ => "standard"
        };

        return OfficialRates?.Profiles.FirstOrDefault(
            profile => profile.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
    }

    private static OfficialRatesDocument ParseAndValidate(string json)
    {
        var document = JsonSerializer.Deserialize<OfficialRatesDocument>(json, JsonOptions)
            ?? throw new InvalidDataException("The official-rate document was empty.");

        var expected = new[] { "standard", "apocalypse", "small-tribes", "conquest" };
        if (document.SchemaVersion != 1
            || document.Profiles.Count != expected.Length
            || document.Profiles.Select(item => item.Id)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count() != expected.Length)
        {
            throw new InvalidDataException(
                "The official-rate document has an unsupported or incomplete schema.");
        }

        foreach (var id in expected)
        {
            var profile = document.Profiles.FirstOrDefault(
                candidate => candidate.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidDataException($"Official profile '{id}' is missing.");

            if (!double.IsFinite(profile.EggHatchSpeedMultiplier)
                || !double.IsFinite(profile.BabyMatureSpeedMultiplier)
                || profile.EggHatchSpeedMultiplier <= 0
                || profile.BabyMatureSpeedMultiplier <= 0)
            {
                throw new InvalidDataException(
                    $"Official profile '{id}' contains an invalid breeding rate.");
            }
        }

        return document;
    }

    private static bool IsValid(ServerRates rates) =>
        double.IsFinite(rates.HatchSpeed)
        && double.IsFinite(rates.MaturationSpeed)
        && double.IsFinite(rates.ConsumptionSpeed)
        && rates.HatchSpeed > 0
        && rates.MaturationSpeed > 0
        && rates.ConsumptionSpeed > 0;
}
