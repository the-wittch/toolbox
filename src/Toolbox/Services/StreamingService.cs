using System.Diagnostics;
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

    public bool IsTabVisible
    {
        get
        {
            var streaming = _settings.Current.Streaming;
            return streaming.Enabled && GetEndpoints().Count > 0;
        }
    }

    public IReadOnlyList<StreamingEndpoint> GetEndpoints()
    {
        return _settings.Current.Streaming.Endpoints
            .Where(e => !string.IsNullOrWhiteSpace(e.Id) || !string.IsNullOrWhiteSpace(e.Label))
            .ToList();
    }

    public string ChangePasswordUrl =>
        _settings.Current.Streaming.ChangePasswordUrl?.Trim() ?? string.Empty;

    public string Connect(StreamingEndpoint endpoint)
    {
        var computerName = string.IsNullOrWhiteSpace(endpoint.ComputerName)
            ? endpoint.Label
            : endpoint.ComputerName.Trim();

        if (string.IsNullOrWhiteSpace(computerName))
        {
            return "No computer name configured for this streaming endpoint.";
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

        try
        {
            _remoteTools.LaunchRemote(tool, computerName);
            return $"Connecting to streaming PC {computerName} with {tool.Label}.";
        }
        catch (Exception ex)
        {
            return $"Failed to connect to {computerName}: {ex.Message}";
        }
    }

    public string OpenViewStream(StreamingEndpoint endpoint) =>
        OpenUrl(endpoint.ViewStreamUrl, "view stream");

    public string OpenControlApp(StreamingEndpoint endpoint) =>
        OpenUrl(endpoint.ControlAppUrl, "control app");

    public string OpenChangePassword() =>
        OpenUrl(ChangePasswordUrl, "change stream password");

    private static string OpenUrl(string? url, string actionLabel)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return $"No URL configured for {actionLabel}.";
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url.Trim(),
                UseShellExecute = true
            });
            return $"Opened {actionLabel}.";
        }
        catch (Exception ex)
        {
            return $"Failed to open {actionLabel}: {ex.Message}";
        }
    }
}
