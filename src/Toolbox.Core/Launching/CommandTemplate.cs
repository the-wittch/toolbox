using System.Text;
using System.Text.RegularExpressions;

namespace Toolbox.Launching;

public static partial class CommandTemplate
{
    public const string ComputerNameToken = "{computerName}";
    public const string UserNameToken = "{userName}";

    private static readonly string[] KnownTokens = { ComputerNameToken, UserNameToken };

    public static IReadOnlyList<string> Tokenize(string? template)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(template))
        {
            return result;
        }

        var current = new StringBuilder();
        var inQuotes = false;
        var hasToken = false;
        foreach (var c in template)
        {
            if (c == '"')
            {
                inQuotes = !inQuotes;
                hasToken = true;
                continue;
            }

            if (!inQuotes && char.IsWhiteSpace(c))
            {
                if (hasToken)
                {
                    result.Add(current.ToString());
                    current.Clear();
                    hasToken = false;
                }

                continue;
            }

            current.Append(c);
            hasToken = true;
        }

        if (inQuotes)
        {
            throw new FormatException("The argument template has an unmatched double quote.");
        }

        if (hasToken)
        {
            result.Add(current.ToString());
        }

        return result;
    }

    public static IReadOnlyList<string> Expand(string? template, string computerName, string userName)
    {
        return Tokenize(template)
            .Select(argument => argument
                .Replace(ComputerNameToken, computerName, StringComparison.OrdinalIgnoreCase)
                .Replace(UserNameToken, userName, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    public static IReadOnlyList<string> FindUnknownTokens(string? template)
    {
        if (string.IsNullOrEmpty(template))
        {
            return Array.Empty<string>();
        }

        return TokenPattern().Matches(template)
            .Select(m => m.Value)
            .Where(token => !KnownTokens.Contains(token, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static string? Validate(string? template)
    {
        try
        {
            Tokenize(template);
        }
        catch (FormatException ex)
        {
            return ex.Message;
        }

        return null;
    }

    [GeneratedRegex(@"\{[A-Za-z]+\}", RegexOptions.CultureInvariant)]
    private static partial Regex TokenPattern();
}
