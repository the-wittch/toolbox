using System.Diagnostics;
using System.DirectoryServices;
using System.Runtime.InteropServices;
using Toolbox.Configuration;
using Toolbox.Ldap;
using Toolbox.Models;

namespace Toolbox.Services;

public sealed class SearchInputException : Exception
{
    public SearchInputException(string message)
        : base(message)
    {
    }
}

public sealed record SearchOutcome(IReadOnlyList<ComputerItem> Items, bool Truncated, TimeSpan Elapsed);

public sealed class AdComputerSearcher
{
    private const int SizeLimitExceeded = unchecked((int)0x80072023);

    private readonly SettingsService _settings;

    public AdComputerSearcher(SettingsService settings)
    {
        _settings = settings;
    }

    public Task<SearchOutcome> SearchAsync(string query, CancellationToken cancellationToken)
    {
        var settings = _settings.Current;
        if (!ComputerSearchQueryBuilder.TryBuild(query, settings.Ldap, settings.Search.ComputerNamePrefixes, out var built, out var error))
        {
            throw new SearchInputException(error!);
        }

        var work = Task.Run(() => Search(built!, settings.Ldap, cancellationToken), CancellationToken.None);
        return work.WaitAsync(cancellationToken);
    }

    private static SearchOutcome Search(ComputerSearchQuery query, LdapSettings ldap, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var maxResults = SettingsLimits.MaxResults(ldap);
        var timeout = SettingsLimits.SearchTimeout(ldap);
        var path = LdapPath.ForSearchRoot(ldap);

        using var root = DirectoryConnection.Open(path, ldap);
        using var searcher = new DirectorySearcher(root, query.Filter, Attributes(ldap), SearchScope.Subtree)
        {
            PageSize = SettingsLimits.PageSize(ldap),
            SizeLimit = maxResults + 1,
            ServerTimeLimit = timeout,
            ClientTimeout = timeout + TimeSpan.FromSeconds(5),
            CacheResults = false
        };

        var items = new List<ComputerItem>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var truncated = false;

        using var results = searcher.FindAll();
        try
        {
            foreach (SearchResult result in results)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                if (items.Count >= maxResults)
                {
                    truncated = true;
                    break;
                }

                var item = ToItem(result, ldap);
                if (item is not null && seen.Add(item.Name))
                {
                    items.Add(item);
                }
            }
        }
        catch (COMException ex) when (ex.HResult == SizeLimitExceeded)
        {
            truncated = true;
        }

        cancellationToken.ThrowIfCancellationRequested();
        items.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        return new SearchOutcome(items, truncated, stopwatch.Elapsed);
    }

    private static string[] Attributes(LdapSettings ldap) => new[]
    {
        ldap.NameAttribute, ldap.DescriptionAttribute, "operatingSystem", "operatingSystemVersion",
        "distinguishedName", "dNSHostName", "lastLogonTimestamp"
    }.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    private static ComputerItem? ToItem(SearchResult result, LdapSettings ldap)
    {
        var name = ReadString(result, ldap.NameAttribute)?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var description = ReadString(result, ldap.DescriptionAttribute);
        return new ComputerItem
        {
            Name = name,
            Description = string.IsNullOrWhiteSpace(description) ? ldap.MissingDescriptionLabel : description,
            OperatingSystem = ReadString(result, "operatingSystem"),
            OperatingSystemVersion = ReadString(result, "operatingSystemVersion"),
            DistinguishedName = ReadString(result, "distinguishedName") ?? string.Empty,
            DnsHostName = ReadString(result, "dNSHostName"),
            LastLogon = Read(result, "lastLogonTimestamp") is long fileTime ? WindowsVersionFormatter.FromFileTime(fileTime) : null,
            HasValidHostName = HostNameRules.IsValidComputerName(name)
        };
    }

    private static object? Read(SearchResult result, string property) =>
        result.Properties.Contains(property) && result.Properties[property].Count > 0
            ? result.Properties[property][0]
            : null;

    private static string? ReadString(SearchResult result, string property) => Read(result, property)?.ToString();
}
