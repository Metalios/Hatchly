using Hatchly.Tools;

namespace Hatchly.Core.Tests;

public sealed class PagesPreparerTests
{
    [Fact]
    public void Prepare_installs_transformed_index_and_repository_base_path()
    {
        using var fixture = new PagesFixture();
        fixture.WritePublished("_framework/blazor.webassembly.abc123.js", "boot");
        fixture.WritePublished("CNAME", "hatchlyapp.com");
        fixture.WritePublished("index.html.br", "stale");
        fixture.WritePublished("index.html.gz", "stale");
        var transformed = fixture.WriteIntermediate(
            "<html><head><base href=\"/\" /></head><body>"
            + "<script src=\"_framework/blazor.webassembly.abc123.js\"></script>"
            + "</body></html>\r\n");

        var result = PagesPreparer.Prepare(fixture.PublishRoot, transformed, "/HatchlyApp");

        var index = File.ReadAllText(result.IndexPath);
        Assert.Equal("/HatchlyApp/", result.BaseHref);
        Assert.Contains("<base href=\"/HatchlyApp/\" />", index);
        Assert.DoesNotContain('\r', index);
        Assert.Equal(index, File.ReadAllText(result.NotFoundPath));
        Assert.True(File.Exists(Path.Combine(result.WebRoot, ".nojekyll")));
        Assert.False(File.Exists(Path.Combine(result.WebRoot, "CNAME")));
        Assert.False(File.Exists($"{result.IndexPath}.br"));
        Assert.False(File.Exists($"{result.IndexPath}.gz"));
    }

    [Theory]
    [InlineData(null, "/")]
    [InlineData("", "/")]
    [InlineData("/", "/")]
    [InlineData("HatchlyApp", "/HatchlyApp/")]
    [InlineData("/HatchlyApp/", "/HatchlyApp/")]
    public void Base_paths_are_normalized(string? value, string expected)
    {
        Assert.Equal(expected, PagesPreparer.NormalizeBasePath(value));
    }

    [Fact]
    public void Prepare_rejects_unresolved_framework_placeholders()
    {
        using var fixture = new PagesFixture();
        var transformed = fixture.WriteIntermediate(
            "<base href=\"/\" /><script src=\"_framework/blazor.webassembly#[.{fingerprint}].js\"></script>");

        var error = Assert.Throws<InvalidDataException>(
            () => PagesPreparer.Prepare(fixture.PublishRoot, transformed, "/"));

        Assert.Contains("unresolved", error.Message);
    }

    private sealed class PagesFixture : IDisposable
    {
        private readonly string root = Path.Combine(
            Path.GetTempPath(),
            $"hatchly-pages-{Guid.NewGuid():N}");

        public PagesFixture()
        {
            PublishRoot = Path.Combine(root, "publish");
            Directory.CreateDirectory(Path.Combine(PublishRoot, "wwwroot"));
        }

        public string PublishRoot { get; }

        public void WritePublished(string relativePath, string content)
        {
            var path = Path.Combine(PublishRoot, "wwwroot", relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }

        public string WriteIntermediate(string content)
        {
            var path = Path.Combine(root, "intermediate", "index.html");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
            return path;
        }

        public void Dispose() => Directory.Delete(root, recursive: true);
    }
}
