namespace Toolbox.Launching;

public static class WindowsPath
{
    private static readonly char[] Separators = { '\\', '/' };

    public static bool IsDevicePath(string path) =>
        path.StartsWith(@"\\?\", StringComparison.Ordinal) ||
        path.StartsWith(@"\\.\", StringComparison.Ordinal) ||
        path.StartsWith("//?/", StringComparison.Ordinal) ||
        path.StartsWith("//./", StringComparison.Ordinal);

    public static bool IsUnc(string path)
    {
        if (path.Length < 5 || IsDevicePath(path))
        {
            return false;
        }

        if (!(path.StartsWith(@"\\", StringComparison.Ordinal) || path.StartsWith("//", StringComparison.Ordinal)))
        {
            return false;
        }

        var parts = path[2..].Split(Separators);
        return parts.Length >= 2 && parts[0].Length > 0 && parts[1].Length > 0;
    }

    public static bool IsDriveRooted(string path) =>
        path.Length >= 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' && (path[2] == '\\' || path[2] == '/');

    public static bool IsFullyQualified(string path) => IsDriveRooted(path) || IsUnc(path);

    public static bool IsBareFileName(string path) =>
        path.Length > 0 && path.IndexOfAny(Separators) < 0 && !path.Contains(':', StringComparison.Ordinal);

    public static string GetExtension(string path)
    {
        var lastSeparator = path.LastIndexOfAny(Separators);
        var lastDot = path.LastIndexOf('.');
        return lastDot > lastSeparator ? path[lastDot..] : string.Empty;
    }

    public static string GetDirectoryName(string path)
    {
        var lastSeparator = path.LastIndexOfAny(Separators);
        return lastSeparator > 0 ? path[..lastSeparator] : string.Empty;
    }

    public static string Combine(string directory, string fileName) =>
        directory.EndsWith('\\') || directory.EndsWith('/') ? directory + fileName : directory + "\\" + fileName;

    public static bool HasParentSegment(string path) =>
        path.Split(Separators).Any(segment => segment == "..");
}
