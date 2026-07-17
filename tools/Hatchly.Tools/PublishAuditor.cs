namespace Hatchly.Tools;

public static class PublishAuditor
{
    public const long MaximumFrameworkBrotliBytes = 2_359_296; // 2.25 MiB
    public const long MaximumHeaderBrandingBytes = 61_440; // 60 KiB
    public const long MaximumRuntimeImageBytes = 307_200; // 300 KiB
    public const long MaximumColdTransferBytes = 3_145_728; // 3 MiB

    private static readonly HashSet<string> ImageExtensions = new(
        [".avif", ".gif", ".jpeg", ".jpg", ".png", ".svg", ".webp"],
        StringComparer.OrdinalIgnoreCase);

    public static PublishAuditResult Audit(string publishPath)
    {
        var root = ResolveWebRoot(publishPath);
        var framework = Path.Combine(root, "_framework");
        if (!Directory.Exists(framework))
        {
            throw new InvalidDataException($"Published framework directory was not found under '{root}'.");
        }

        var frameworkBrotliBytes = Directory
            .EnumerateFiles(framework, "*.br", SearchOption.AllDirectories)
            .Sum(path => new FileInfo(path).Length);
        if (frameworkBrotliBytes == 0)
        {
            throw new InvalidDataException("The published application does not contain Brotli framework assets.");
        }

        var brandingPaths = new[]
        {
            Path.Combine(root, "images", "hatchly-emblem.webp"),
            Path.Combine(root, "images", "hatchly-wordmark.webp")
        };
        var missingBranding = brandingPaths.FirstOrDefault(path => !File.Exists(path));
        if (missingBranding is not null)
        {
            throw new InvalidDataException($"Required header branding asset is missing: '{missingBranding}'.");
        }

        var headerBrandingBytes = brandingPaths.Sum(path => new FileInfo(path).Length);
        var oversizedImage = Directory
            .EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(path => ImageExtensions.Contains(Path.GetExtension(path)))
            .Where(path => !Path.GetFileName(path).Equals(
                "hatchly-social.png",
                StringComparison.OrdinalIgnoreCase))
            .Select(path => new FileInfo(path))
            .FirstOrDefault(file => file.Length > MaximumRuntimeImageBytes);
        if (oversizedImage is not null)
        {
            throw new InvalidDataException(
                $"Runtime image '{Path.GetRelativePath(root, oversizedImage.FullName)}' is "
                + $"{FormatBytes(oversizedImage.Length)}; the limit is {FormatBytes(MaximumRuntimeImageBytes)}.");
        }

        var coldTransferBytes = Directory
            .EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(path => !path.EndsWith(".br", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
            .Where(path => !Path.GetFileName(path).Equals(
                "hatchly-social.png",
                StringComparison.OrdinalIgnoreCase))
            .Sum(TransferSize);

        var result = new PublishAuditResult(
            frameworkBrotliBytes,
            headerBrandingBytes,
            coldTransferBytes);
        var failures = new List<string>();
        AddFailure(
            failures,
            "Brotli framework payload",
            result.FrameworkBrotliBytes,
            MaximumFrameworkBrotliBytes);
        AddFailure(
            failures,
            "header branding",
            result.HeaderBrandingBytes,
            MaximumHeaderBrandingBytes);
        AddFailure(
            failures,
            "cold transfer",
            result.ColdTransferBytes,
            MaximumColdTransferBytes);
        if (failures.Count > 0)
        {
            throw new InvalidDataException(string.Join(Environment.NewLine, failures));
        }

        return result;
    }

    public static string FormatBytes(long bytes) => $"{bytes / 1024d:0.0} KiB";

    private static string ResolveWebRoot(string publishPath)
    {
        var path = Path.GetFullPath(publishPath);
        var nested = Path.Combine(path, "wwwroot");
        return Directory.Exists(nested) ? nested : path;
    }

    private static long TransferSize(string path)
    {
        var brotli = $"{path}.br";
        if (File.Exists(brotli))
        {
            return new FileInfo(brotli).Length;
        }

        var gzip = $"{path}.gz";
        return File.Exists(gzip)
            ? new FileInfo(gzip).Length
            : new FileInfo(path).Length;
    }

    private static void AddFailure(
        ICollection<string> failures,
        string label,
        long actual,
        long maximum)
    {
        if (actual > maximum)
        {
            failures.Add(
                $"Published {label} is {FormatBytes(actual)}; the limit is {FormatBytes(maximum)}.");
        }
    }
}

public sealed record PublishAuditResult(
    long FrameworkBrotliBytes,
    long HeaderBrandingBytes,
    long ColdTransferBytes);
