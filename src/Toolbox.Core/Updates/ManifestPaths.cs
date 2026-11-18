namespace Toolbox.Updates;

public static class ManifestPaths
{
    private static readonly char[] InvalidChars = { '<', '>', ':', '"', '|', '?', '*' };

    private static readonly HashSet<string> ReservedDeviceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    private static readonly HashSet<string> ProtectedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ReleaseManifest.FileName,
        ReleaseManifest.SignatureFileName,
        InstallInfo.FileName
    };

    public static string? Validate(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "empty path";
        }

        if (path.Length > 400)
        {
            return "path too long";
        }

        if (path[0] is '/' or '\\')
        {
            return "absolute path";
        }

        var segments = path.Split('/', '\\');
        foreach (var segment in segments)
        {
            if (segment.Length == 0)
            {
                return "empty path segment";
            }

            if (segment is "." or "..")
            {
                return "relative segment";
            }

            if (segment.IndexOfAny(InvalidChars) >= 0 || segment.Any(char.IsControl))
            {
                return "invalid character";
            }

            if (segment.EndsWith('.') || segment.EndsWith(' '))
            {
                return "segment ends with a dot or space";
            }

            // 8.3 short names could alias a protected file such as toolbox-install.json
            if (segment.Contains('~', StringComparison.Ordinal))
            {
                return "tilde in name";
            }

            var stem = segment.Split('.')[0];
            if (ReservedDeviceNames.Contains(stem))
            {
                return "reserved device name";
            }
        }

        if (segments[0].Equals(UpdateLayout.WorkDirectoryName, StringComparison.OrdinalIgnoreCase))
        {
            return "reserved folder";
        }

        if (segments.Length == 1 && ProtectedNames.Contains(segments[0]))
        {
            return "reserved file";
        }

        if (segments[^1].Contains(UpdateLayout.OldFileMarker, StringComparison.OrdinalIgnoreCase))
        {
            return "reserved file name";
        }

        return null;
    }

    public static string Normalize(string path) => path.Replace('\\', '/');

    public static string Resolve(string root, string relativePath)
    {
        var fullRoot = Path.GetFullPath(root);
        var combined = Path.GetFullPath(Path.Combine(
            new[] { fullRoot }.Concat(relativePath.Split('/', '\\')).ToArray()));
        var rootWithSeparator = fullRoot.EndsWith(Path.DirectorySeparatorChar)
            ? fullRoot
            : fullRoot + Path.DirectorySeparatorChar;
        if (!combined.StartsWith(rootWithSeparator, OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal))
        {
            throw new ManifestException($"'{relativePath}' resolves outside the target folder.");
        }

        return combined;
    }
}
