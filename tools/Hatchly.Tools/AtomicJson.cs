using System.Text;
using System.Text.Json;

namespace Hatchly.Tools;

public static class AtomicJson
{
    public static async Task WriteAsync<T>(
        string outputPath,
        T value,
        JsonSerializerOptions options,
        CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(outputPath))
            ?? throw new InvalidOperationException("The output path has no directory.");
        Directory.CreateDirectory(directory);

        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(outputPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            var json = JsonSerializer.Serialize(value, options) + Environment.NewLine;
            await File.WriteAllTextAsync(
                temporaryPath,
                json,
                new UTF8Encoding(false),
                cancellationToken);
            File.Move(temporaryPath, outputPath, true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}
