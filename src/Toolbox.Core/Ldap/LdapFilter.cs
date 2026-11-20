using System.Text;
using System.Text.RegularExpressions;

namespace Toolbox.Ldap;

public static partial class LdapFilter
{
    public static string EscapeValue(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var builder = new StringBuilder(value.Length + 8);
        foreach (var c in value)
        {
            switch (c)
            {
                case '\\': builder.Append(@"\5c"); break;
                case '*': builder.Append(@"\2a"); break;
                case '(': builder.Append(@"\28"); break;
                case ')': builder.Append(@"\29"); break;
                case '\0': builder.Append(@"\00"); break;
                default: builder.Append(c); break;
            }
        }

        return builder.ToString();
    }

    public static bool IsWellFormed(string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
        {
            return false;
        }

        var text = filter.Trim();
        if (text[0] != '(' || text[^1] != ')')
        {
            return false;
        }

        var depth = 0;
        var sawEquals = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            switch (c)
            {
                case '\\':
                    if (i + 2 >= text.Length || !Uri.IsHexDigit(text[i + 1]) || !Uri.IsHexDigit(text[i + 2]))
                    {
                        return false;
                    }

                    i += 2;
                    break;
                case '(':
                    depth++;
                    break;
                case ')':
                    depth--;
                    if (depth < 0)
                    {
                        return false;
                    }

                    if (depth == 0 && i != text.Length - 1)
                    {
                        return false;
                    }

                    break;
                case '=':
                    sawEquals = true;
                    break;
                case '\0':
                    return false;
            }
        }

        return depth == 0 && sawEquals;
    }

    public static bool IsValidAttributeName(string? name) =>
        !string.IsNullOrWhiteSpace(name) && AttributeNamePattern().IsMatch(name);

    [GeneratedRegex(@"^(?:[A-Za-z][A-Za-z0-9-]{0,63}|[0-9]+(?:\.[0-9]+)+)$", RegexOptions.CultureInvariant)]
    private static partial Regex AttributeNamePattern();
}
