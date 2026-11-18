using System.Text.Json;
using System.Text.Json.Serialization;

namespace Toolbox.Updates;

public sealed class ManifestException : Exception
{
    public ManifestException(string message)
        : base(message)
    {
    }

    public ManifestException(string message, Exception inner)
        : base(message, inner)
    {
    }
}

public sealed class ManifestFile
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    [JsonPropertyName("size")]
    public long Size { get; set; }

    [JsonPropertyName("sha256")]
    public string Sha256 { get; set; } = string.Empty;
}

public sealed class ReleaseManifest
{
    public const string FileName = "toolbox-manifest.json";
    public const string SignatureFileName = "toolbox-manifest.sig";
    public const string ProductName = "Toolbox";
    public const string EntryExecutable = "Toolbox.exe";
    public const int SupportedFormatVersion = 1;
    public const int MaxManifestBytes = 4 * 1024 * 1024;
    public const int MaxFiles = 20000;

    [JsonPropertyName("formatVersion")]
    public int FormatVersion { get; set; }

    [JsonPropertyName("product")]
    public string Product { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName("files")]
    public List<ManifestFile> Files { get; set; } = new();

    [JsonIgnore]
    public Version ParsedVersion { get; private set; } = new(0, 0, 0, 0);

    public static ReleaseManifest Parse(ReadOnlySpan<byte> json)
    {
        if (json.Length == 0 || json.Length > MaxManifestBytes)
        {
            throw new ManifestException("The release manifest is empty or too large.");
        }

        ReleaseManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<ReleaseManifest>(json);
        }
        catch (JsonException ex)
        {
            throw new ManifestException("The release manifest is not valid JSON.", ex);
        }

        if (manifest is null)
        {
            throw new ManifestException("The release manifest is empty.");
        }

        manifest.Validate();
        return manifest;
    }

    private void Validate()
    {
        if (FormatVersion != SupportedFormatVersion)
        {
            throw new ManifestException($"Unsupported manifest format version {FormatVersion}.");
        }

        if (!string.Equals(Product, ProductName, StringComparison.Ordinal))
        {
            throw new ManifestException($"The manifest is for '{Product}', not {ProductName}.");
        }

        if (!VersionParsing.TryParse(Version, out var parsed))
        {
            throw new ManifestException($"The manifest version '{Version}' is not a valid version.");
        }

        ParsedVersion = parsed;

        Files ??= new List<ManifestFile>();
        if (Files.Count == 0 || Files.Count > MaxFiles)
        {
            throw new ManifestException("The manifest file list is empty or too long.");
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Files)
        {
            if (file is null)
            {
                throw new ManifestException("The manifest contains an empty file entry.");
            }

            var error = ManifestPaths.Validate(file.Path);
            if (error is not null)
            {
                throw new ManifestException($"Manifest entry '{file.Path}' is not allowed: {error}");
            }

            file.Path = ManifestPaths.Normalize(file.Path);
            if (!seen.Add(file.Path))
            {
                throw new ManifestException($"Manifest entry '{file.Path}' appears more than once.");
            }

            if (file.Size < 0)
            {
                throw new ManifestException($"Manifest entry '{file.Path}' has a negative size.");
            }

            if (!IsSha256Hex(file.Sha256))
            {
                throw new ManifestException($"Manifest entry '{file.Path}' has an invalid SHA-256 value.");
            }

            file.Sha256 = file.Sha256.ToUpperInvariant();
        }

        if (!seen.Contains(EntryExecutable))
        {
            throw new ManifestException($"The manifest does not include {EntryExecutable}.");
        }
    }

    private static bool IsSha256Hex(string? value) =>
        value is { Length: 64 } && value.All(Uri.IsHexDigit);
}
