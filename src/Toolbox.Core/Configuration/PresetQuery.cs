using System.Globalization;

namespace Toolbox.Configuration;

public static class PresetQuery
{
    private const string TodayPrefix = "{today:";

    public static string Resolve(string? queryText, DateTime now, CultureInfo culture)
    {
        if (string.IsNullOrEmpty(queryText))
        {
            return string.Empty;
        }

        if (!queryText.StartsWith(TodayPrefix, StringComparison.OrdinalIgnoreCase) || !queryText.EndsWith('}'))
        {
            return queryText;
        }

        var format = queryText[TodayPrefix.Length..^1];
        if (string.IsNullOrWhiteSpace(format))
        {
            format = "M/d/yyyy";
        }

        try
        {
            return now.ToString(format, culture);
        }
        catch (FormatException)
        {
            return queryText;
        }
    }

    public static bool LooksLikePlaceholderOu(string? rootOu) =>
        string.IsNullOrWhiteSpace(rootOu) ||
        rootOu.Contains("DC=example", StringComparison.OrdinalIgnoreCase) ||
        rootOu.Contains("example.com", StringComparison.OrdinalIgnoreCase);
}
