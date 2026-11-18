using System.Net.NetworkInformation;
using System.Net.Sockets;
using Microsoft.Win32;
using Toolbox.Configuration;
using Toolbox.Diagnostics;
using Toolbox.Ldap;
using Toolbox.Models;

namespace Toolbox.Services;

public sealed class ComputerInfoService
{
    private readonly SettingsService _settings;

    public ComputerInfoService(SettingsService settings)
    {
        _settings = settings;
    }

    public async Task<ComputerDetail> GetDetailAsync(ComputerItem item, CancellationToken cancellationToken)
    {
        var detail = new ComputerDetail { ComputerName = item.Name };
        if (!item.HasValidHostName)
        {
            detail.OperatingSystem = detail.OperatingSystemVersion = detail.LastLoggedOnUser = "Not queried";
            detail.Ping = new PingResult { Status = "Not queried (invalid host name)" };
            return detail;
        }

        var ping = PingAsync(item.Name, cancellationToken);
        var registry = ReadRegistryAsync(item.Name, cancellationToken);
        detail.Ping = await ping.ConfigureAwait(false);
        var snapshot = await registry.ConfigureAwait(false);
        detail.OperatingSystem = snapshot.OperatingSystem;
        detail.OperatingSystemVersion = snapshot.Version;
        detail.LastLoggedOnUser = snapshot.LastUser;
        return detail;
    }

    public async Task<PingResult> PingAsync(string host, CancellationToken cancellationToken)
    {
        var ping = _settings.Current.Ping;
        try
        {
            using var pinger = new Ping();
            var reply = await pinger.SendPingAsync(
                host,
                SettingsLimits.PingTimeout(ping),
                new byte[SettingsLimits.PingBuffer(ping)],
                null,
                cancellationToken).ConfigureAwait(false);
            return new PingResult
            {
                IsReachable = reply.Status == IPStatus.Success,
                Status = reply.Status == IPStatus.TimedOut ? "No reply" : reply.Status.ToString(),
                RoundtripTimeMs = reply.RoundtripTime
            };
        }
        catch (PingException ex)
        {
            return new PingResult { Status = ex.InnerException is SocketException ? "Name not resolved" : "Error" };
        }
    }

    private sealed record RegistrySnapshot(string OperatingSystem, string Version, string LastUser);

    private async Task<RegistrySnapshot> ReadRegistryAsync(string host, CancellationToken cancellationToken)
    {
        // RegistryKey.OpenRemoteBaseKey cannot be cancelled, so stop waiting rather than block
        var work = Task.Run(() => ReadRegistry(host), CancellationToken.None);
        try
        {
            return await work.WaitAsync(SettingsLimits.RegistryTimeout(_settings.Current.Details), cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            return new RegistrySnapshot("Timed out", "Timed out", "Timed out");
        }
    }

    private static RegistrySnapshot ReadRegistry(string host)
    {
        try
        {
            using var hive = RegistryKey.OpenRemoteBaseKey(RegistryHive.LocalMachine, host, RegistryView.Default);
            string os = "Not available", version = "Not available", lastUser = "No logon recorded";

            using (var current = hive.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
            {
                if (current is not null)
                {
                    (os, version) = WindowsVersionFormatter.Format(
                        current.GetValue("ProductName") as string,
                        (current.GetValue("DisplayVersion") ?? current.GetValue("ReleaseId")) as string,
                        (current.GetValue("CurrentBuildNumber") ?? current.GetValue("CurrentBuild")) as string,
                        current.GetValue("UBR") as int?,
                        current.GetValue("CurrentVersion") as string);
                }
            }

            using (var logon = hive.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Authentication\LogonUI"))
            {
                if (logon?.GetValue("LastLoggedOnUser") is string user && user.Length > 0)
                {
                    lastUser = user;
                }
            }

            return new RegistrySnapshot(os, version, lastUser);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            AppLog.Warn($"Remote registry query for {host} failed.", ex);
            var message = ErrorTranslator.Describe(ex);
            return new RegistrySnapshot(message, "Not available", "Not available");
        }
    }
}
