using System.Globalization;

namespace Toolbox.Ldap;

public static class HostNameRules
{
    public const int MaxLength = 253;

    public static bool IsValidComputerName(string? name)
    {
        if (string.IsNullOrEmpty(name) || name.Length > MaxLength)
        {
            return false;
        }

        foreach (var label in name.Split('.'))
        {
            if (label.Length is 0 or > 63 || label[0] == '-' || label[^1] == '-')
            {
                return false;
            }

            foreach (var c in label)
            {
                if (!(char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
                {
                    return false;
                }
            }
        }

        return true;
    }

    public static bool IsValidServer(string? server)
    {
        if (string.IsNullOrWhiteSpace(server))
        {
            return false;
        }

        var host = server.Trim();
        var colon = host.LastIndexOf(':');
        if (colon >= 0)
        {
            var portText = host[(colon + 1)..];
            if (!int.TryParse(portText, NumberStyles.None, CultureInfo.InvariantCulture, out var port) ||
                port is < 1 or > 65535)
            {
                return false;
            }

            host = host[..colon];
        }

        return IsValidComputerName(host);
    }
}
