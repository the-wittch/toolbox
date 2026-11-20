using System.Text.Json.Serialization;

namespace Toolbox.Models;

public sealed class AppSettings
{
    public string ProgramTitle { get; set; } = "Toolbox";
    public string Theme { get; set; } = "System";

    public bool SetupCompleted { get; set; }

    public LdapSettings Ldap { get; set; } = new();
    public SearchSettings Search { get; set; } = new();
    public AccessControlSettings AccessControl { get; set; } = new();
    public PingSettings Ping { get; set; } = new();
    public DetailSettings Details { get; set; } = new();
    public LapsSettings Laps { get; set; } = new();
    public DeploymentSettings Deployment { get; set; } = new();
    public StreamingSettings Streaming { get; set; } = new();
    public List<RemoteToolDefinition> RemoteTools { get; set; } = new();
    public List<DetailActionDefinition> DetailActions { get; set; } = new();
    public LabelSettings Labels { get; set; } = new();
    public UiSettings Ui { get; set; } = new();
}

public sealed class DeploymentSettings
{
    public string UpdateSourcePath { get; set; } = string.Empty;

    public bool AutoUpdateOnClose { get; set; } = true;

    public List<string> TrustedSignerThumbprints { get; set; } = new();

    public bool RequireSignedUpdates { get; set; }

    public bool AllowDowngrade { get; set; }
}

public sealed class StreamingSettings
{
    public bool Enabled { get; set; } = true;

    public string ConnectRemoteToolId { get; set; } = "sccm";

    [JsonPropertyName("changePasswordUrl")]
    public string AccountPageUrl { get; set; } = string.Empty;

    public List<StreamingEndpoint> Endpoints { get; set; } = new();
}

public sealed class StreamingEndpoint
{
    public string Id { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string ComputerName { get; set; } = string.Empty;
    public string ViewStreamUrl { get; set; } = string.Empty;
    public string ControlAppUrl { get; set; } = string.Empty;

    public StreamingEndpoint Clone() => new()
    {
        Id = Id,
        Label = Label,
        ComputerName = ComputerName,
        ViewStreamUrl = ViewStreamUrl,
        ControlAppUrl = ControlAppUrl
    };

    public override string ToString() => string.IsNullOrWhiteSpace(Label) ? Id : Label;
}

public sealed class LdapSettings
{
    public string RootOu { get; set; } = "OU=Computers,DC=example,DC=com";

    public string Server { get; set; } = string.Empty;

    public bool UseLdaps { get; set; }

    public int PageSize { get; set; } = 1000;
    public int MaxResults { get; set; } = 500;
    public int SearchTimeoutSeconds { get; set; } = 30;
    public string ComputerObjectFilter { get; set; } = "(objectCategory=computer)";
    public string NameAttribute { get; set; } = "cn";
    public string DescriptionAttribute { get; set; } = "description";
    public string MissingDescriptionLabel { get; set; } = "No description available";
}

public sealed class SearchSettings
{
    public List<string> ComputerNamePrefixes { get; set; } = new() { "PC-", "WS-" };
    public List<SearchPreset> Presets { get; set; } = new();
    public int HistoryLimit { get; set; } = 50;
}

public sealed class SearchPreset
{
    public string Id { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string QueryText { get; set; } = string.Empty;
}

public sealed class AccessControlSettings
{
    public bool Enabled { get; set; }
    public List<string> AllowedGroups { get; set; } = new();
    public string DenyMessage { get; set; } = "Unable to open the program. You have insufficient privileges.";

    public AccessControlSettings Clone() => new()
    {
        Enabled = Enabled,
        AllowedGroups = new List<string>(AllowedGroups),
        DenyMessage = DenyMessage
    };
}

public sealed class PingSettings
{
    public int TimeoutMs { get; set; } = 3000;
    public int BufferSize { get; set; } = 32;
}

public sealed class DetailSettings
{
    public int RemoteRegistryTimeoutSeconds { get; set; } = 15;
}

public sealed class LapsSettings
{
    public bool Enabled { get; set; } = true;

    public int ClipboardClearSeconds { get; set; } = 30;

    public int RevealSeconds { get; set; } = 20;

    public int LookupTimeoutSeconds { get; set; } = 20;
}

public sealed class RemoteToolDefinition
{
    public string Id { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public string Arguments { get; set; } = "{computerName}";
    public List<string> CandidatePaths { get; set; } = new();
    public bool PreferAsDefault { get; set; }
}

public sealed class DetailActionDefinition
{
    public string Id { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public bool RequiresOnline { get; set; }
    public string Executable { get; set; } = string.Empty;
    public string Arguments { get; set; } = string.Empty;
}

public sealed class LabelSettings
{
    public string SearchPlaceholder { get; set; } = "Computer name, description, or username";
    public string SearchButton { get; set; } = "Search";
    public string RemoteButton { get; set; } = "Remote in";
    public string ResultsEmpty { get; set; } = "No computers found. Try a different search.";
    public string ResultsHint { get; set; } = "Select a computer to load OS info and ping results.";
    public string HistoryHeader { get; set; } = "Recent searches";
    public string StatusReady { get; set; } = "Ready";
    public string LoadingDetail { get; set; } = "Loading details…";
    public string ThemeLight { get; set; } = "Light";
    public string ThemeDark { get; set; } = "Dark";
    public string ThemeSystem { get; set; } = "System";
}

public sealed class UiSettings
{
    public int WindowWidth { get; set; } = 1180;
    public int WindowHeight { get; set; } = 720;
    public int MinWidth { get; set; } = 900;
    public int MinHeight { get; set; } = 560;
    public bool ShowSearchHistory { get; set; } = true;
    public bool ShowStatusLog { get; set; } = true;
    public int StatusLogLimit { get; set; } = 200;
}

public sealed class ComputerItem
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? OperatingSystem { get; set; }
    public string? OperatingSystemVersion { get; set; }
    public string DistinguishedName { get; set; } = string.Empty;
    public string? DnsHostName { get; set; }
    public DateTimeOffset? LastLogon { get; set; }

    public bool HasValidHostName { get; set; }

    public string OperatingSystemDisplay =>
        string.IsNullOrWhiteSpace(OperatingSystem) ? string.Empty : OperatingSystem!;
}

public sealed class ComputerDetail
{
    public string ComputerName { get; set; } = string.Empty;
    public string OperatingSystem { get; set; } = "Not available";
    public string OperatingSystemVersion { get; set; } = "Not available";
    public string LastLoggedOnUser { get; set; } = "Not available";
    public PingResult Ping { get; set; } = new();
}

public sealed class PingResult
{
    public bool IsReachable { get; set; }
    public string Status { get; set; } = "Unknown";
    public long RoundtripTimeMs { get; set; }
}

public sealed class AvailableRemoteTool
{
    public string Id { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string ExecutablePath { get; set; } = string.Empty;
    public string ArgumentsTemplate { get; set; } = string.Empty;
    public bool PreferAsDefault { get; set; }

    public override string ToString() => Label;
}

public sealed class StatusLogEntry
{
    public int Index { get; set; }
    public string Message { get; set; } = string.Empty;
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.Now;

    public string Display => $"{Timestamp:HH:mm:ss}  {Message}";
}
