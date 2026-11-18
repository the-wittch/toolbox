namespace Toolbox.Updates;

public static class VersionParsing
{
    private static readonly System.Buffers.SearchValues<char> SuffixStart = System.Buffers.SearchValues.Create("+- ");

    public static bool TryParse(string? text, out Version version)
    {
        version = new Version(0, 0, 0, 0);
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var value = text.Trim();
        if (value.StartsWith('v') || value.StartsWith('V'))
        {
            value = value[1..];
        }

        var cut = value.AsSpan().IndexOfAny(SuffixStart);
        if (cut >= 0)
        {
            value = value[..cut];
        }

        if (!value.Contains('.', StringComparison.Ordinal))
        {
            value += ".0";
        }

        if (!Version.TryParse(value, out var parsed))
        {
            return false;
        }

        version = Normalize(parsed);
        return true;
    }

    public static Version Normalize(Version v) =>
        new(v.Major, v.Minor, Math.Max(0, v.Build), Math.Max(0, v.Revision));

    public static string ToDisplay(Version v) =>
        v.Revision > 0 ? v.ToString(4) : v.ToString(3);
}
