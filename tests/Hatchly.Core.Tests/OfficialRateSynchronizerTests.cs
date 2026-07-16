using System.Net;
using System.Text;
using System.Text.Json;
using Hatchly.Core;
using Hatchly.Tools;

namespace Hatchly.Core.Tests;

public sealed class OfficialRateSynchronizerTests
{
    [Theory]
    [InlineData("EggHatchSpeedMultiplier=1\r\nBabyMatureSpeedMultiplier=2\r\n", 1, 2)]
    [InlineData("[ServerSettings]\nEggHatchSpeedMultiplier = 3.5\nBabyMatureSpeedMultiplier=4\n", 3.5, 4)]
    [InlineData("# comment\nBabyMatureSpeedMultiplier=5\nEggHatchSpeedMultiplier=6", 6, 5)]
    [InlineData("EggHatchSpeedMultiplier=7.25\rBabyMatureSpeedMultiplier=8.5\r", 7.25, 8.5)]
    public void Parses_all_feed_line_formats(
        string content,
        double expectedHatch,
        double expectedMature)
    {
        var result = OfficialRateSynchronizer.ParseRequiredRates(content);

        Assert.Equal(expectedHatch, result.Hatch);
        Assert.Equal(expectedMature, result.Mature);
    }

    [Theory]
    [InlineData("EggHatchSpeedMultiplier=1")]
    [InlineData("EggHatchSpeedMultiplier=nope\nBabyMatureSpeedMultiplier=1")]
    [InlineData("EggHatchSpeedMultiplier=0\nBabyMatureSpeedMultiplier=1")]
    [InlineData("EggHatchSpeedMultiplier=1\nBabyMatureSpeedMultiplier=-2")]
    public void Rejects_missing_malformed_or_non_positive_values(string content)
    {
        Assert.Throws<InvalidDataException>(
            () => OfficialRateSynchronizer.ParseRequiredRates(content));
    }

    [Fact]
    public async Task Octet_stream_response_is_supported()
    {
        using var directory = new TemporaryDirectory();
        var output = Path.Combine(directory.Path, "rates.json");
        var handler = new SequenceHandler(_ =>
        {
            var content = new ByteArrayContent(
                Encoding.UTF8.GetBytes(
                    "EggHatchSpeedMultiplier=2\nBabyMatureSpeedMultiplier=3\n"));
            content.Headers.ContentType = new("application/octet-stream");
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        });
        using var http = new HttpClient(handler);
        var feeds = Enumerable.Range(1, 4)
            .Select(index => new RateFeed($"p{index}", $"P{index}", $"https://rates/{index}"))
            .ToArray();
        var synchronizer = new OfficialRateSynchronizer(http, feeds);

        var result = await synchronizer.SynchronizeAsync(output);

        Assert.True(result.Changed);
        Assert.All(result.Document.Profiles, profile =>
        {
            Assert.Equal(2, profile.EggHatchSpeedMultiplier);
            Assert.Equal(3, profile.BabyMatureSpeedMultiplier);
        });
    }

    [Fact]
    public async Task Failed_endpoint_preserves_previous_complete_file()
    {
        using var directory = new TemporaryDirectory();
        var output = Path.Combine(directory.Path, "rates.json");
        var original = """{"schemaVersion":1,"lastRelevantRateChangeUtc":"2026-01-01T00:00:00Z","profiles":[]}""";
        await File.WriteAllTextAsync(output, original);
        var handler = new SequenceHandler(request =>
            request.RequestUri!.AbsolutePath.EndsWith("/2", StringComparison.Ordinal)
                ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                : TextResponse(1, 1));
        using var http = new HttpClient(handler);
        var feeds = new[]
        {
            new RateFeed("p1", "P1", "https://rates/1"),
            new RateFeed("p2", "P2", "https://rates/2"),
            new RateFeed("p3", "P3", "https://rates/3"),
            new RateFeed("p4", "P4", "https://rates/4")
        };
        var synchronizer = new OfficialRateSynchronizer(
            http,
            feeds,
            requestTimeout: TimeSpan.FromSeconds(1));

        await Assert.ThrowsAsync<HttpRequestException>(
            () => synchronizer.SynchronizeAsync(output));

        Assert.Equal(original, await File.ReadAllTextAsync(output));
    }

    [Fact]
    public async Task Semantic_no_change_does_not_rewrite_or_change_timestamp()
    {
        using var directory = new TemporaryDirectory();
        var output = Path.Combine(directory.Path, "rates.json");
        var timestamp = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var feeds = Enumerable.Range(1, 4)
            .Select(index => new RateFeed($"p{index}", $"P{index}", $"https://rates/{index}"))
            .ToArray();
        var existing = new OfficialRatesDocument
        {
            SchemaVersion = 1,
            LastRelevantRateChangeUtc = timestamp,
            Profiles = feeds.Select(feed => new OfficialRateProfile
            {
                Id = feed.Id,
                DisplayName = "Old formatting is ignored",
                SourceUrl = "old-url",
                EggHatchSpeedMultiplier = 2,
                BabyMatureSpeedMultiplier = 3
            }).ToArray()
        };
        await File.WriteAllTextAsync(
            output,
            JsonSerializer.Serialize(existing, new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                WriteIndented = false
            }));
        var before = await File.ReadAllTextAsync(output);
        using var http = new HttpClient(new SequenceHandler(_ => TextResponse(2, 3)));
        var synchronizer = new OfficialRateSynchronizer(
            http,
            feeds,
            () => timestamp.AddDays(10));

        var result = await synchronizer.SynchronizeAsync(output);

        Assert.False(result.Changed);
        Assert.Equal(timestamp, result.Document.LastRelevantRateChangeUtc);
        Assert.Equal(before, await File.ReadAllTextAsync(output));
    }

    private static HttpResponseMessage TextResponse(double hatch, double mature) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(
                $"EggHatchSpeedMultiplier={hatch}\nBabyMatureSpeedMultiplier={mature}\n",
                Encoding.UTF8)
        };

    private sealed class SequenceHandler(
        Func<HttpRequestMessage, HttpResponseMessage> response)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(response(request));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"hatchly-tests-{Guid.NewGuid():N}");

        public TemporaryDirectory() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, true);
            }
        }
    }
}
