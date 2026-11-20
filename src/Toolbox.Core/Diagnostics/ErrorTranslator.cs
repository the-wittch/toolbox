using System.Globalization;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security;

namespace Toolbox.Diagnostics;

public static class ErrorTranslator
{
    private static readonly Dictionary<int, string> KnownHResults = new()
    {
        [unchecked((int)0x8007203A)] = "No domain controller could be reached. Check the network or VPN connection.",
        [unchecked((int)0x80072020)] = "The directory refused the request. Check the search root and your connection.",
        [unchecked((int)0x80072030)] = "The configured search root (ldap.rootOu) does not exist in the directory.",
        [unchecked((int)0x80072032)] = "The configured search root (ldap.rootOu) is not a valid distinguished name.",
        [unchecked((int)0x80005000)] = "The configured LDAP path is invalid. Check ldap.rootOu and ldap.server.",
        [unchecked((int)0x8007052E)] = "The directory rejected your credentials. Sign out and back in, then try again.",
        [unchecked((int)0x80070005)] = "Access denied. Your account does not have permission for this operation.",
        [unchecked((int)0x80072024)] = "The directory limited the number of results. Narrow the search.",
        [unchecked((int)0x80072023)] = "The directory limited the number of results. Narrow the search.",
        [unchecked((int)0x80072022)] = "The directory search timed out. Narrow the search or try again.",
        [unchecked((int)0x8007202F)] = "The directory requires a stronger connection (signing or LDAPS). Check ldap.useLdaps.",
        [unchecked((int)0x80072027)] = "The directory rejected the secure connection settings. Check ldap.useLdaps and ldap.server.",
        [unchecked((int)0x8007200A)] = "A requested directory attribute does not exist in this domain's schema.",
        [unchecked((int)0x80070035)] = "The computer or network path was not found.",
        [unchecked((int)0x80070043)] = "The network name was not found.",
        [unchecked((int)0x800706BA)] = "The remote computer's RPC service is unavailable (offline, firewalled, or Remote Registry stopped).",
        [unchecked((int)0x80070079)] = "The remote operation timed out.",
        [unchecked((int)0x80070070)] = "There is not enough free disk space.",
        [unchecked((int)0x80070027)] = "There is not enough free disk space.",
        [unchecked((int)0x80070020)] = "A file is in use by another program.",
    };

    public static string Describe(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var root = Unwrap(exception);

        if (KnownHResults.TryGetValue(root.HResult, out var known))
        {
            return known;
        }

        return root switch
        {
            OperationCanceledException => "The operation was cancelled.",
            TimeoutException => "The operation timed out.",
            UnauthorizedAccessException or SecurityException => "Access denied. Your account does not have permission for this operation.",
            PingException { InnerException: SocketException } => "The computer name could not be resolved (DNS).",
            SocketException => "A network error occurred. Check the connection.",
            PathTooLongException => "A file path is too long.",
            DirectoryNotFoundException or FileNotFoundException => "A required file or folder was not found.",
            ExternalException ext => "Unexpected error (code 0x" + ext.ErrorCode.ToString("X8", CultureInfo.InvariantCulture) + "). See the log for details.",
            _ => "Unexpected error (" + root.GetType().Name + "). See the log for details."
        };
    }

    private static Exception Unwrap(Exception exception)
    {
        var current = exception;
        while (current is AggregateException { InnerExceptions.Count: 1 } aggregate)
        {
            current = aggregate.InnerExceptions[0];
        }

        while (current is System.Reflection.TargetInvocationException { InnerException: { } inner })
        {
            current = inner;
        }

        return current;
    }
}
