using Toolbox.Models;

namespace Toolbox.Ldap;

public static class LdapPath
{
    private const string Scheme = "LDAP://";
    public const int LdapsPort = 636;

    public static bool IsAdsPath(string? value) =>
        value is not null && value.TrimStart().StartsWith(Scheme, StringComparison.OrdinalIgnoreCase);

    public static string ForSearchRoot(LdapSettings ldap)
    {
        var rootOu = (ldap.RootOu ?? string.Empty).Trim();
        if (IsAdsPath(rootOu))
        {
            return rootOu;
        }

        return Build(ResolveServer(ldap, rootOu), rootOu);
    }

    public static string ForObject(string distinguishedName, LdapSettings ldap)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(distinguishedName);
        return Build(ResolveServer(ldap, distinguishedName), distinguishedName);
    }

    public static string? ServerFromAdsPath(string adsPath)
    {
        if (!IsAdsPath(adsPath))
        {
            return null;
        }

        var rest = adsPath.Trim()[Scheme.Length..];
        var slash = rest.IndexOf('/', StringComparison.Ordinal);
        if (slash <= 0)
        {
            return null;
        }

        var candidate = rest[..slash];
        return candidate.Contains('=', StringComparison.Ordinal) ? null : candidate;
    }

    public static string? DomainFromDn(string? distinguishedName)
    {
        if (string.IsNullOrWhiteSpace(distinguishedName))
        {
            return null;
        }

        var labels = distinguishedName
            .Split(',')
            .Select(part => part.Trim())
            .Where(part => part.StartsWith("DC=", StringComparison.OrdinalIgnoreCase))
            .Select(part => part[3..].Trim())
            .Where(label => label.Length > 0)
            .ToList();
        return labels.Count == 0 ? null : string.Join('.', labels);
    }

    public static string EscapeDnForAdsPath(string distinguishedName) =>
        distinguishedName.Replace("/", @"\/", StringComparison.Ordinal);

    private static string? ResolveServer(LdapSettings ldap, string distinguishedName)
    {
        var server = (ldap.Server ?? string.Empty).Trim();
        if (server.Length == 0 && IsAdsPath(ldap.RootOu))
        {
            server = ServerFromAdsPath(ldap.RootOu!) ?? string.Empty;
        }

        if (!ldap.UseLdaps)
        {
            return server.Length == 0 ? null : server;
        }

        if (server.Length == 0)
        {
            server = DomainFromDn(distinguishedName) ?? DomainFromDn(ldap.RootOu) ?? string.Empty;
        }

        if (server.Length == 0)
        {
            return null;
        }

        return server.Contains(':', StringComparison.Ordinal) ? server : $"{server}:{LdapsPort}";
    }

    private static string Build(string? server, string distinguishedName)
    {
        var dn = EscapeDnForAdsPath(distinguishedName.Trim());
        return string.IsNullOrEmpty(server) ? Scheme + dn : $"{Scheme}{server}/{dn}";
    }
}
