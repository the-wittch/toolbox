using System.Globalization;

namespace Toolbox.Ldap;

public static class WindowsVersionFormatter
{
    private const int FirstWindows11Build = 22000;

    public static (string OperatingSystem, string Version) Format(
        string? productName,
        string? displayVersion,
        string? currentBuild,
        int? updateBuildRevision,
        string? legacyCurrentVersion)
    {
        var name = string.IsNullOrWhiteSpace(productName) ? "Windows" : productName.Trim();
        var hasBuild = int.TryParse(currentBuild, NumberStyles.None, CultureInfo.InvariantCulture, out var build);

        // Windows 11 still says "Windows 10" in ProductName

        if (hasBuild && build >= FirstWindows11Build &&
            name.StartsWith("Windows 10", StringComparison.OrdinalIgnoreCase))
        {
            name = "Windows 11" + name["Windows 10".Length..];
        }

        string version;
        if (hasBuild)
        {
            var buildText = updateBuildRevision is { } ubr
                ? string.Create(CultureInfo.InvariantCulture, $"{build}.{ubr}")
                : build.ToString(CultureInfo.InvariantCulture);
            version = string.IsNullOrWhiteSpace(displayVersion)
                ? $"Build {buildText}"
                : $"{displayVersion.Trim()} (build {buildText})";
        }
        else
        {
            version = string.IsNullOrWhiteSpace(legacyCurrentVersion) ? "Not available" : legacyCurrentVersion.Trim();
        }

        return (name, version);
    }

    public static DateTimeOffset? FromFileTime(long value)
    {
        if (value <= 0 || value == long.MaxValue)
        {
            return null;
        }

        try
        {
            return DateTimeOffset.FromFileTime(value);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }
}
