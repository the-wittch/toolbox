using System.DirectoryServices;
using System.DirectoryServices.AccountManagement;
using System.Net.NetworkInformation;
using Microsoft.Win32;
using Toolbox.Models;

namespace Toolbox.Services;

public sealed class ComputerInfoService
{
    private readonly SettingsService _settings;

    public ComputerInfoService(SettingsService settings)
    {
        _settings = settings;
    }

    public Task<ComputerDetail> GetDetailAsync(string computerName, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            var detail = new ComputerDetail { ComputerName = computerName };

            try
            {
                var os = GetRemoteWindowsOs(computerName);
                var comma = os.IndexOf(',');
                if (comma >= 0)
                {
                    detail.OperatingSystem = os[..comma].Trim();
                    detail.OperatingSystemVersion = os[(comma + 1)..].Trim();
                }
                else
                {
                    detail.OperatingSystem = os;
                }
            }
            catch
            {
                detail.OperatingSystem = "Not found";
                detail.OperatingSystemVersion = "Not found";
            }

            try
            {
                detail.LastLoggedOnUser = GetLastLoggedOnUser(computerName);
            }
            catch
            {
                detail.LastLoggedOnUser = "Not found";
            }

            try
            {
                detail.LapsPassword = GetLapsPassword(computerName);
            }
            catch
            {
                detail.LapsPassword = "N/A";
            }

            cancellationToken.ThrowIfCancellationRequested();
            detail.Ping = PingHost(computerName);
            return detail;
        }, cancellationToken);
    }

    public PingResult PingHost(string host)
    {
        var pingSettings = _settings.Current.Ping;
        try
        {
            using var pinger = new Ping();
            var buffer = new byte[Math.Clamp(pingSettings.BufferSize, 1, 65500)];
            var reply = pinger.Send(host, Math.Max(1, pingSettings.TimeoutMs), buffer);
            if (reply is null)
            {
                return new PingResult { IsReachable = false, Status = "Error" };
            }

            return new PingResult
            {
                IsReachable = reply.Status == IPStatus.Success,
                Status = reply.Status.ToString(),
                RoundtripTimeMs = reply.RoundtripTime
            };
        }
        catch (PingException)
        {
            return new PingResult { IsReachable = false, Status = "Error" };
        }
    }

    private static string GetRemoteWindowsOs(string pcName)
    {
        using var reg = RegistryKey.OpenRemoteBaseKey(RegistryHive.LocalMachine, pcName);
        using var key = reg.OpenSubKey(@"Software\Microsoft\Windows NT\CurrentVersion\");
        if (key is null)
        {
            return "Not found, Not found";
        }

        return $"{key.GetValue("ProductName")}, {key.GetValue("CurrentVersion")}";
    }

    private static string GetLastLoggedOnUser(string pcName)
    {
        const string location = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Authentication\LogonUI";
        var registryView = Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Registry32;
        using var hive = RegistryKey.OpenRemoteBaseKey(RegistryHive.LocalMachine, pcName, registryView);
        using var key = hive.OpenSubKey(location);
        var item = key?.GetValue("LastLoggedOnUser");
        return item?.ToString() ?? "No Logon Found";
    }

    private static string GetLapsPassword(string pcName)
    {
        using var ctx = new PrincipalContext(ContextType.Domain);
        var comp = ComputerPrincipal.FindByIdentity(ctx, pcName);
        if (comp is null)
        {
            return "N/A";
        }

        using var de = (DirectoryEntry)comp.GetUnderlyingObject();
        if (de.Properties.Contains("ms-Mcs-AdmPwd"))
        {
            return de.Properties["ms-Mcs-AdmPwd"][0]?.ToString() ?? "N/A";
        }

        return "N/A";
    }
}
