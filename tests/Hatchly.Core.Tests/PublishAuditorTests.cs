using Hatchly.Tools;

namespace Hatchly.Core.Tests;

public sealed class PublishAuditorTests
{
    [Fact]
    public void Audit_uses_precompressed_transfer_sizes_and_excludes_social_image()
    {
        using var fixture = new PublishedFixture();
        fixture.Write("_framework/dotnet.wasm", 900_000);
        fixture.Write("_framework/dotnet.wasm.br", 400_000);
        fixture.Write("images/hatchly-emblem.webp", 30_000);
        fixture.Write("images/hatchly-wordmark.webp", 20_000);
        fixture.Write("images/hatchly-social.png", 275_000);
        fixture.Write("data/catalog.json", 100_000);
        fixture.Write("data/catalog.json.br", 10_000);

        var result = PublishAuditor.Audit(fixture.Root);

        Assert.Equal(400_000, result.FrameworkBrotliBytes);
        Assert.Equal(50_000, result.HeaderBrandingBytes);
        Assert.Equal(460_000, result.ColdTransferBytes);
    }

    [Fact]
    public void Audit_rejects_oversized_runtime_images()
    {
        using var fixture = new PublishedFixture();
        fixture.Write("_framework/dotnet.wasm.br", 1);
        fixture.Write("images/hatchly-emblem.webp", 1);
        fixture.Write("images/hatchly-wordmark.webp", 1);
        fixture.Write("images/creature.png", PublishAuditor.MaximumRuntimeImageBytes + 1);

        var error = Assert.Throws<InvalidDataException>(
            () => PublishAuditor.Audit(fixture.Root));

        Assert.Contains("creature.png", error.Message);
    }

    private sealed class PublishedFixture : IDisposable
    {
        public PublishedFixture()
        {
            Root = Path.Combine(Path.GetTempPath(), $"hatchly-publish-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public void Write(string relativePath, long size)
        {
            var path = Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var stream = File.Create(path);
            stream.SetLength(size);
        }

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
