using System.Text.Json;
using System.Text.Json.Serialization;
using Toolbox.IO;

namespace Toolbox.Updates;

public sealed class InstallInfo
{
    public const string FileName = "toolbox-install.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public string UpdateSourcePath { get; set; } = string.Empty;

    public string? InstalledVersion { get; set; }

    public DateTimeOffset? InstalledUtc { get; set; }

    public static string PathFor(string installDirectory) => Path.Combine(installDirectory, FileName);

    public static InstallInfo? TryLoad(string installDirectory)
    {
        var path = PathFor(installDirectory);
        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize<InstallInfo>(File.ReadAllText(path), JsonOptions)
                : null;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void Save(string installDirectory) =>
        AtomicFile.WriteAllText(PathFor(installDirectory), JsonSerializer.Serialize(this, JsonOptions));
}
