using System.ComponentModel;
using System.Diagnostics;
using Toolbox.Diagnostics;
using Toolbox.Launching;
using Toolbox.Models;

namespace Toolbox.Services;

public sealed class StreamingService
{
    private readonly SettingsService _settings;
    private readonly RemoteToolService _remoteTools;

    public StreamingService(SettingsService settings, RemoteToolService remoteTools)
    {
        _settings = settings;
        _remoteTools = remoteTools;
    }

    public bool IsTabVisible => _settings.Current.Streaming.Enabled && GetEndpoints().Count > 0;

    public IReadOnlyList<StreamingEndpoint> GetEndpoints() =>
        _settings.Current.Streaming.Endpoints
            .Where(e => !string.IsNullOrWhiteSpace(e.Id) || !string.IsNullOrWhiteSpace(e.Label))
            .ToList();

    public string ChangePasswordUrl => _settings.Current.Streaming.ChangePasswordUrl?.Trim() ?? string.Empty;

    public string Connect(StreamingEndpoint endpoint)
    {
        var computerName = string.IsNullOrWhiteSpace(endpoint.ComputerName)
            ? endpoint.Label?.Trim() ?? string.Empty
            : endpoint.ComputerName.Trim();
        if (computerName.Length == 0)
        {
            return "No computer name is configured for this streaming endpoint.";
        }

        var toolId = string.IsNullOrWhiteSpace(_settings.Current.Streaming.ConnectRemoteToolId)
            ? "sccm"
            : _settings.Current.Streaming.ConnectRemoteToolId.Trim();
        var tools = _remoteTools.GetAvailableTools();
        var tool = tools.FirstOrDefault(t => string.Equals(t.Id, toolId, StringComparison.OrdinalIgnoreCase))
                   ?? tools.FirstOrDefault();
        if (tool is null)
        {
            return $"Remote tool '{toolId}' is not installed on this PC.";
        }

        return _remoteTools.LaunchRemote(tool, computerName).Message;
    }

    public string OpenViewStream(StreamingEndpoint endpoint) => OpenUrl(endpoint.ViewStreamUrl, "the stream viewer");

    public string OpenControlApp(StreamingEndpoint endpoint) => OpenUrl(endpoint.ControlAppUrl, "the control app");

    public string OpenChangePassword() => OpenUrl(ChangePasswordUrl, "the password page");

    private static string OpenUrl(string? url, string what)
    {
        var check = UrlPolicy.Check(url);
        if (!check.IsValid)
        {
            return $"Cannot open {what}: {check.Error}";
        }

        try
        {
            using var process = Process.Start(new ProcessStartInfo(check.Uri!.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            AppLog.Warn($"Opening {check.Uri!.Host} failed.", ex);
            return $"Could not open {what}: {ErrorTranslator.Describe(ex)}";
        }

        return check.IsInsecure
            ? $"Opened {what} over plain http ({check.Uri.Host})."
            : $"Opened {what}.";
    }
}
