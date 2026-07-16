using System.Globalization;
using System.Net;
using System.Text.Json;
using Hatchly.Core;

namespace Hatchly.Tools;

public sealed record RateFeed(
    string Id,
    string DisplayName,
    string SourceUrl);

public sealed record RateSyncResult(
    bool Changed,
    OfficialRatesDocument Document);

public sealed class OfficialRateSynchronizer
{
    public static readonly IReadOnlyList<RateFeed> DefaultFeeds =
    [
        new(
            "standard",
            "Standard",
            "https://cdn2.arkdedicated.com/asa/dynamicconfig.ini"),
        new(
            "apocalypse",
            "Apocalypse",
            "https://cdn2.arkdedicated.com/asa/arkpocalypse_dynamicconfig.ini"),
        new(
            "small-tribes",
            "Small Tribes",
            "https://cdn2.arkdedicated.com/asa/smalltribes_dynamicconfig.ini"),
        new(
            "conquest",
            "Conquest",
            "https://cdn2.arkdedicated.com/asa/conquest_dynamicconfig.ini")
    ];

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly HttpClient http;
    private readonly IReadOnlyList<RateFeed> feeds;
    private readonly Func<DateTimeOffset> utcNow;
    private readonly TimeSpan requestTimeout;

    public OfficialRateSynchronizer(
        HttpClient http,
        IReadOnlyList<RateFeed>? feeds = null,
        Func<DateTimeOffset>? utcNow = null,
        TimeSpan? requestTimeout = null)
    {
        this.http = http;
        this.feeds = feeds ?? DefaultFeeds;
        this.utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
        this.requestTimeout = requestTimeout ?? TimeSpan.FromSeconds(20);
    }

    public async Task<RateSyncResult> SynchronizeAsync(
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        var profiles = new List<OfficialRateProfile>();
        foreach (var feed in feeds)
        {
            var text = await FetchWithRetriesAsync(feed.SourceUrl, cancellationToken);
            var (hatch, mature) = ParseRequiredRates(text);
            profiles.Add(new OfficialRateProfile
            {
                Id = feed.Id,
                DisplayName = feed.DisplayName,
                SourceUrl = feed.SourceUrl,
                EggHatchSpeedMultiplier = hatch,
                BabyMatureSpeedMultiplier = mature
            });
        }

        var previous = await TryReadExistingAsync(outputPath, cancellationToken);
        var changed = previous is null || !SemanticallyEqual(previous.Profiles, profiles);
        var document = new OfficialRatesDocument
        {
            SchemaVersion = 1,
            LastRelevantRateChangeUtc = changed
                ? utcNow()
                : previous!.LastRelevantRateChangeUtc,
            Profiles = profiles
        };

        if (changed)
        {
            await AtomicJson.WriteAsync(
                outputPath,
                document,
                JsonOptions,
                cancellationToken);
        }

        return new RateSyncResult(changed, document);
    }

    public static (double Hatch, double Mature) ParseRequiredRates(string content)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using var reader = new StringReader(content.Replace("\0", string.Empty));
        while (reader.ReadLine() is { } rawLine)
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#'))
            {
                continue;
            }

            var separator = line.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();
            values[key] = value;
        }

        return (
            ParsePositive(values, "EggHatchSpeedMultiplier"),
            ParsePositive(values, "BabyMatureSpeedMultiplier"));
    }

    public static bool SemanticallyEqual(
        IReadOnlyList<OfficialRateProfile> left,
        IReadOnlyList<OfficialRateProfile> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        var rightById = right.ToDictionary(
            item => item.Id,
            StringComparer.OrdinalIgnoreCase);
        foreach (var profile in left)
        {
            if (!rightById.TryGetValue(profile.Id, out var candidate)
                || profile.EggHatchSpeedMultiplier != candidate.EggHatchSpeedMultiplier
                || profile.BabyMatureSpeedMultiplier != candidate.BabyMatureSpeedMultiplier)
            {
                return false;
            }
        }

        return true;
    }

    private async Task<string> FetchWithRetriesAsync(
        string url,
        CancellationToken cancellationToken)
    {
        Exception? lastError = null;
        for (var attempt = 1; attempt <= 4; attempt++)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(requestTimeout);
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.UserAgent.ParseAdd("HatchlyApp-RateSync/1.0");
                using var response = await http.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    timeout.Token);
                if (!response.IsSuccessStatusCode)
                {
                    throw new HttpRequestException(
                        $"Rate feed '{url}' returned {(int)response.StatusCode} {response.StatusCode}.",
                        null,
                        response.StatusCode);
                }

                return await response.Content.ReadAsStringAsync(timeout.Token);
            }
            catch (Exception exception) when (
                exception is HttpRequestException or TaskCanceledException
                && attempt < 4
                && !cancellationToken.IsCancellationRequested)
            {
                lastError = exception;
                await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt), cancellationToken);
            }
            catch (Exception exception)
            {
                lastError = exception;
                break;
            }
        }

        throw new HttpRequestException(
            $"Rate feed '{url}' failed after four attempts.",
            lastError,
            lastError is HttpRequestException requestError
                ? requestError.StatusCode
                : HttpStatusCode.RequestTimeout);
    }

    private static double ParsePositive(
        IReadOnlyDictionary<string, string> values,
        string key)
    {
        if (!values.TryGetValue(key, out var text)
            || !double.TryParse(
                text,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var value)
            || !double.IsFinite(value)
            || value <= 0)
        {
            throw new InvalidDataException(
                $"Required breeding value '{key}' is missing or invalid.");
        }

        return value;
    }

    private static async Task<OfficialRatesDocument?> TryReadExistingAsync(
        string outputPath,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(outputPath))
        {
            return null;
        }

        await using var stream = File.OpenRead(outputPath);
        return await JsonSerializer.DeserializeAsync<OfficialRatesDocument>(
            stream,
            JsonOptions,
            cancellationToken);
    }
}
