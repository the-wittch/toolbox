using System.DirectoryServices;
using Toolbox.Models;

namespace Toolbox.Services;

public sealed class AdComputerSearcher
{
    private readonly SettingsService _settings;

    public AdComputerSearcher(SettingsService settings)
    {
        _settings = settings;
    }

    public Task<IReadOnlyList<ComputerItem>> SearchAsync(string query, CancellationToken cancellationToken = default)
    {
        return Task.Run(() => Search(query, cancellationToken), cancellationToken);
    }

    private IReadOnlyList<ComputerItem> Search(string query, CancellationToken cancellationToken)
    {
        var ldap = _settings.Current.Ldap;
        var search = _settings.Current.Search;
        var trimmed = (query ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return Array.Empty<ComputerItem>();
        }

        var isComputerNameSearch = search.ComputerNamePrefixes.Any(prefix =>
            !string.IsNullOrWhiteSpace(prefix) &&
            trimmed.Contains(prefix, StringComparison.OrdinalIgnoreCase));

        var path = ldap.RootOu.StartsWith("LDAP://", StringComparison.OrdinalIgnoreCase)
            ? ldap.RootOu
            : "LDAP://" + ldap.RootOu;

        using var entry = new DirectoryEntry(path);
        using var directorySearcher = new DirectorySearcher(entry)
        {
            PageSize = Math.Max(1, ldap.PageSize),
            Filter = $"(&{ldap.ComputerObjectFilter})"
        };

        foreach (var property in ldap.PropertiesToLoad.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            directorySearcher.PropertiesToLoad.Add(property);
        }

        var results = new List<ComputerItem>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        using var searchResults = directorySearcher.FindAll();
        foreach (SearchResult result in searchResults)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var name = ReadProperty(result, ldap.NameAttribute);
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var description = ReadProperty(result, ldap.DescriptionAttribute);
            if (string.IsNullOrWhiteSpace(description))
            {
                description = ldap.MissingDescriptionLabel;
            }

            bool matches;
            if (isComputerNameSearch)
            {
                matches = name.Contains(trimmed, StringComparison.OrdinalIgnoreCase);
            }
            else
            {
                matches = description.Contains(trimmed, StringComparison.OrdinalIgnoreCase);
            }

            if (!matches || !seen.Add(name))
            {
                continue;
            }

            results.Add(new ComputerItem
            {
                Name = name,
                Description = description,
                OperatingSystem = ReadProperty(result, "operatingSystem"),
                OperatingSystemVersion = ReadProperty(result, "operatingSystemVersion")
            });
        }

        return results
            .OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string? ReadProperty(SearchResult result, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(propertyName))
        {
            return null;
        }

        if (!result.Properties.Contains(propertyName) || result.Properties[propertyName].Count == 0)
        {
            return null;
        }

        return result.Properties[propertyName][0]?.ToString();
    }
}
