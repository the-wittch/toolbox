using Toolbox.Models;

namespace Toolbox.Ldap;

public sealed record ComputerSearchQuery(string Filter, string Term, bool IsNameSearch);

public static class ComputerSearchQueryBuilder
{
    public const int MaxTermLength = 256;
    public const string DefaultBaseFilter = "(objectCategory=computer)";

    public static bool TryBuild(
        string? rawQuery,
        LdapSettings ldap,
        IEnumerable<string> computerNamePrefixes,
        out ComputerSearchQuery? query,
        out string? error)
    {
        query = null;
        error = null;

        var term = (rawQuery ?? string.Empty).Trim();
        if (term.Length == 0)
        {
            error = "Enter a computer name or description fragment to search.";
            return false;
        }

        if (term.Length > MaxTermLength)
        {
            error = $"Search text is limited to {MaxTermLength} characters.";
            return false;
        }

        if (term.Any(char.IsControl))
        {
            error = "Search text contains control characters.";
            return false;
        }

        var baseFilter = string.IsNullOrWhiteSpace(ldap.ComputerObjectFilter)
            ? DefaultBaseFilter
            : ldap.ComputerObjectFilter.Trim();
        if (!LdapFilter.IsWellFormed(baseFilter))
        {
            error = "The configured ldap.computerObjectFilter is not a valid LDAP filter.";
            return false;
        }

        if (!LdapFilter.IsValidAttributeName(ldap.NameAttribute) ||
            !LdapFilter.IsValidAttributeName(ldap.DescriptionAttribute))
        {
            error = "The configured ldap.nameAttribute or ldap.descriptionAttribute is not a valid attribute name.";
            return false;
        }

        var isNameSearch = computerNamePrefixes.Any(prefix =>
            !string.IsNullOrWhiteSpace(prefix) &&
            term.Contains(prefix.Trim(), StringComparison.OrdinalIgnoreCase));

        var escaped = LdapFilter.EscapeValue(term);
        var nameClause = $"({ldap.NameAttribute}=*{escaped}*)";
        var match = isNameSearch
            ? nameClause
            : $"(|{nameClause}({ldap.DescriptionAttribute}=*{escaped}*))";

        query = new ComputerSearchQuery($"(&{baseFilter}{match})", term, isNameSearch);
        return true;
    }
}
