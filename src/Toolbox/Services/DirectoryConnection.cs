using System.DirectoryServices;
using Toolbox.Models;

namespace Toolbox.Services;

internal static class DirectoryConnection
{
    public static DirectoryEntry Open(string adsPath, LdapSettings ldap) =>
        new(adsPath, null, null, AuthenticationFor(adsPath, ldap));

    // AD refuses SASL sign/seal on top of TLS, so LDAPS uses the TLS channel alone
    public static AuthenticationTypes AuthenticationFor(string adsPath, LdapSettings ldap)
    {
        var tls = ldap.UseLdaps || adsPath.Contains(":636/", StringComparison.Ordinal);
        return tls
            ? AuthenticationTypes.Secure | AuthenticationTypes.SecureSocketsLayer
            : AuthenticationTypes.Secure | AuthenticationTypes.Signing | AuthenticationTypes.Sealing;
    }
}
