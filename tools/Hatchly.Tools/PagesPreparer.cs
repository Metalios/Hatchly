using System.Text;
using System.Text.RegularExpressions;

namespace Hatchly.Tools;

public static partial class PagesPreparer
{
    public static PagesPreparationResult Prepare(
        string publishPath,
        string intermediateIndexPath,
        string? basePath)
    {
        var root = ResolveWebRoot(publishPath);
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException($"Published web root '{root}' does not exist.");
        }

        var intermediateIndex = Path.GetFullPath(intermediateIndexPath);
        if (!File.Exists(intermediateIndex))
        {
            throw new FileNotFoundException(
                "The transformed Blazor index was not generated.",
                intermediateIndex);
        }

        var content = File.ReadAllText(intermediateIndex, Encoding.UTF8);
        if (content.Contains("#[.{fingerprint}]", StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "The supplied Blazor index still contains unresolved static-asset placeholders.");
        }

        var baseHref = NormalizeBasePath(basePath);
        var match = BaseElement().Match(content);
        if (!match.Success)
        {
            throw new InvalidDataException("The transformed Blazor index does not contain a base element.");
        }

        content = BaseElement().Replace(
            content,
            $"<base href=\"{baseHref}\" />",
            count: 1);
        content = content.ReplaceLineEndings("\n");
        EnsureBootScriptExists(content, root);

        var indexPath = Path.Combine(root, "index.html");
        var notFoundPath = Path.Combine(root, "404.html");
        WriteUtf8(indexPath, content);
        WriteUtf8(notFoundPath, content);
        WriteUtf8(Path.Combine(root, ".nojekyll"), string.Empty);

        DeleteIfPresent(Path.Combine(root, "CNAME"));
        DeleteIfPresent($"{indexPath}.br");
        DeleteIfPresent($"{indexPath}.gz");
        DeleteIfPresent($"{notFoundPath}.br");
        DeleteIfPresent($"{notFoundPath}.gz");

        return new PagesPreparationResult(root, baseHref, indexPath, notFoundPath);
    }

    public static string NormalizeBasePath(string? value)
    {
        var path = string.IsNullOrWhiteSpace(value) ? "/" : value.Trim();
        if (path == "/")
        {
            return path;
        }

        return $"/{path.Trim('/')}/";
    }

    private static string ResolveWebRoot(string publishPath)
    {
        var path = Path.GetFullPath(publishPath);
        var nested = Path.Combine(path, "wwwroot");
        return Directory.Exists(nested) ? nested : path;
    }

    private static void EnsureBootScriptExists(string content, string root)
    {
        var match = BootScript().Match(content);
        if (!match.Success)
        {
            throw new InvalidDataException(
                "The transformed Blazor index does not reference its fingerprinted boot script.");
        }

        var relativePath = match.Groups["path"].Value.Replace('/', Path.DirectorySeparatorChar);
        var scriptPath = Path.GetFullPath(Path.Combine(root, relativePath));
        if (!scriptPath.StartsWith(root, StringComparison.OrdinalIgnoreCase)
            || !File.Exists(scriptPath))
        {
            throw new InvalidDataException(
                $"The transformed Blazor boot script '{relativePath}' is missing from the publish tree.");
        }
    }

    private static void WriteUtf8(string path, string content) =>
        File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

    private static void DeleteIfPresent(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    [GeneratedRegex("<base\\s+href=\"[^\"]*\"\\s*/?>", RegexOptions.IgnoreCase)]
    private static partial Regex BaseElement();

    [GeneratedRegex("<script\\s+src=\"(?<path>_framework/blazor\\.webassembly\\.[^\"]+\\.js)\"")]
    private static partial Regex BootScript();
}

public sealed record PagesPreparationResult(
    string WebRoot,
    string BaseHref,
    string IndexPath,
    string NotFoundPath);
