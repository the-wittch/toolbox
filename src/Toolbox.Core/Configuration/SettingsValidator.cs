using System.Globalization;
using Toolbox.Launching;
using Toolbox.Ldap;
using Toolbox.Models;
using Toolbox.Updates;

namespace Toolbox.Configuration;

public enum IssueSeverity
{
    Warning,
    Error
}

public sealed record ValidationIssue(IssueSeverity Severity, string Path, string Message)
{
    public override string ToString() => $"{Severity}: {Path}: {Message}";
}

public static class SettingsValidator
{
    public static IReadOnlyList<ValidationIssue> Validate(AppSettings s)
    {
        ArgumentNullException.ThrowIfNull(s);
        var issues = new List<ValidationIssue>();
        ValidateLdap(s.Ldap, issues);
        ValidateSearch(s.Search, issues);
        ValidateAccessControl(s.AccessControl, issues);
        ValidateRanges(s, issues);
        ValidateRemoteTools(s.RemoteTools, issues);
        ValidateDetailActions(s.DetailActions, issues);
        ValidateStreaming(s.Streaming, issues);
        ValidateDeployment(s.Deployment, issues);
        return issues;
    }

    private static void ValidateLdap(LdapSettings ldap, List<ValidationIssue> issues)
    {
        var root = (ldap.RootOu ?? string.Empty).Trim();
        if (root.Length == 0)
        {
            Error(issues, "ldap.rootOu", "A search root is required.");
        }
        else if (PresetQuery.LooksLikePlaceholderOu(root))
        {
            Warn(issues, "ldap.rootOu", "Still set to the example.com placeholder.");
        }
        else if (!LdapPath.IsAdsPath(root) && !root.Contains('=', StringComparison.Ordinal))
        {
            Error(issues, "ldap.rootOu", "Must be a distinguished name such as OU=Computers,DC=contoso,DC=com.");
        }

        if (!string.IsNullOrWhiteSpace(ldap.Server) && !HostNameRules.IsValidServer(ldap.Server))
        {
            Error(issues, "ldap.server", "Must be a host name, optionally with :port.");
        }

        if (ldap.UseLdaps && LdapPath.IsAdsPath(root) && !root.Contains(":636", StringComparison.Ordinal))
        {
            Warn(issues, "ldap.useLdaps", "rootOu is a full LDAP:// path without :636; it is used as written.");
        }

        if (ldap.UseLdaps && string.IsNullOrWhiteSpace(ldap.Server) && LdapPath.DomainFromDn(root) is null &&
            LdapPath.ServerFromAdsPath(root) is null)
        {
            Error(issues, "ldap.server", "LDAPS needs a server or a rootOu containing DC= components.");
        }

        if (!string.IsNullOrWhiteSpace(ldap.ComputerObjectFilter) && !LdapFilter.IsWellFormed(ldap.ComputerObjectFilter))
        {
            Error(issues, "ldap.computerObjectFilter", "Not a well-formed LDAP filter.");
        }

        if (!LdapFilter.IsValidAttributeName(ldap.NameAttribute))
        {
            Error(issues, "ldap.nameAttribute", "Not a valid attribute name.");
        }

        if (!LdapFilter.IsValidAttributeName(ldap.DescriptionAttribute))
        {
            Error(issues, "ldap.descriptionAttribute", "Not a valid attribute name.");
        }
    }

    private static void ValidateSearch(SearchSettings search, List<ValidationIssue> issues)
    {
        var presetIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < search.Presets.Count; i++)
        {
            var preset = search.Presets[i];
            if (!string.IsNullOrWhiteSpace(preset.Id) && !presetIds.Add(preset.Id))
            {
                Warn(issues, $"search.presets[{i}].id", $"Duplicate preset id '{preset.Id}'.");
            }
        }
    }

    private static void ValidateAccessControl(AccessControlSettings access, List<ValidationIssue> issues)
    {
        if (access.Enabled && !access.AllowedGroups.Any(g => !string.IsNullOrWhiteSpace(g)))
        {
            Error(issues, "accessControl.allowedGroups", "Access control is enabled with no groups; every user will be denied.");
        }
    }

    private static void ValidateRanges(AppSettings s, List<ValidationIssue> issues)
    {
        Range(issues, "ldap.pageSize", s.Ldap.PageSize, SettingsLimits.PageSizeRange);
        Range(issues, "ldap.maxResults", s.Ldap.MaxResults, SettingsLimits.MaxResultsRange);
        Range(issues, "ldap.searchTimeoutSeconds", s.Ldap.SearchTimeoutSeconds, SettingsLimits.SearchTimeoutRange);
        Range(issues, "ping.timeoutMs", s.Ping.TimeoutMs, SettingsLimits.PingTimeoutRange);
        Range(issues, "ping.bufferSize", s.Ping.BufferSize, SettingsLimits.PingBufferRange);
        Range(issues, "details.remoteRegistryTimeoutSeconds", s.Details.RemoteRegistryTimeoutSeconds, SettingsLimits.RegistryTimeoutRange);
        Range(issues, "laps.clipboardClearSeconds", s.Laps.ClipboardClearSeconds, SettingsLimits.ClipboardClearRange);
        Range(issues, "laps.revealSeconds", s.Laps.RevealSeconds, SettingsLimits.RevealRange);
        Range(issues, "laps.lookupTimeoutSeconds", s.Laps.LookupTimeoutSeconds, SettingsLimits.LapsTimeoutRange);
        Range(issues, "search.historyLimit", s.Search.HistoryLimit, SettingsLimits.HistoryLimitRange);
        Range(issues, "ui.statusLogLimit", s.Ui.StatusLogLimit, SettingsLimits.StatusLogRange);

        if (s.Laps.ClipboardClearSeconds == 0)
        {
            Warn(issues, "laps.clipboardClearSeconds", "0 keeps copied LAPS passwords on the clipboard indefinitely.");
        }
    }

    private static void ValidateRemoteTools(List<RemoteToolDefinition> tools, List<ValidationIssue> issues)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < tools.Count; i++)
        {
            var tool = tools[i];
            var path = $"remoteTools[{i}]";
            if (string.IsNullOrWhiteSpace(tool.Id))
            {
                Error(issues, path + ".id", "Remote tool id is required.");
            }
            else if (!ids.Add(tool.Id))
            {
                Error(issues, path + ".id", $"Duplicate remote tool id '{tool.Id}'.");
            }

            if (!tool.Enabled)
            {
                continue;
            }

            if (tool.CandidatePaths.Count == 0)
            {
                Warn(issues, path + ".candidatePaths", "No candidate paths; the tool will never be available.");
            }

            for (var p = 0; p < tool.CandidatePaths.Count; p++)
            {
                var error = ExecutableResolver.ValidateConfiguredPath(tool.CandidatePaths[p]);
                if (error is not null)
                {
                    Error(issues, $"{path}.candidatePaths[{p}]", error);
                }
                else if (WindowsPath.IsUnc(tool.CandidatePaths[p].Trim()))
                {
                    Warn(issues, $"{path}.candidatePaths[{p}]", "Runs a program from a network share; make sure only administrators can write there.");
                }
            }

            ValidateArguments(issues, path + ".arguments", tool.Arguments, requireComputer: true);
        }
    }

    private static void ValidateDetailActions(List<DetailActionDefinition> actions, List<ValidationIssue> issues)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < actions.Count; i++)
        {
            var action = actions[i];
            var path = $"detailActions[{i}]";
            if (!string.IsNullOrWhiteSpace(action.Id) && !ids.Add(action.Id))
            {
                Warn(issues, path + ".id", $"Duplicate detail action id '{action.Id}'.");
            }

            if (!action.Enabled)
            {
                continue;
            }

            var error = ExecutableResolver.ValidateConfiguredPath(action.Executable);
            if (error is not null)
            {
                Error(issues, path + ".executable", error);
            }

            ValidateArguments(issues, path + ".arguments", action.Arguments, requireComputer: false);
        }
    }

    private static void ValidateArguments(List<ValidationIssue> issues, string path, string? template, bool requireComputer)
    {
        var error = CommandTemplate.Validate(template);
        if (error is not null)
        {
            Error(issues, path, error);
            return;
        }

        foreach (var token in CommandTemplate.FindUnknownTokens(template))
        {
            Warn(issues, path, $"Unknown token {token} is passed through literally.");
        }

        if (requireComputer && !(template ?? string.Empty).Contains(CommandTemplate.ComputerNameToken, StringComparison.OrdinalIgnoreCase))
        {
            Warn(issues, path, "Does not contain {computerName}.");
        }
    }

    private static void ValidateStreaming(StreamingSettings streaming, List<ValidationIssue> issues)
    {
        CheckUrl(issues, "streaming.changePasswordUrl", streaming.AccountPageUrl, optional: true);
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < streaming.Endpoints.Count; i++)
        {
            var endpoint = streaming.Endpoints[i];
            var path = $"streaming.endpoints[{i}]";
            if (!string.IsNullOrWhiteSpace(endpoint.Id) && !ids.Add(endpoint.Id))
            {
                Warn(issues, path + ".id", $"Duplicate endpoint id '{endpoint.Id}'.");
            }

            if (!string.IsNullOrWhiteSpace(endpoint.ComputerName) && !HostNameRules.IsValidComputerName(endpoint.ComputerName.Trim()))
            {
                Error(issues, path + ".computerName", "Not a valid host name.");
            }

            CheckUrl(issues, path + ".viewStreamUrl", endpoint.ViewStreamUrl, optional: true);
            CheckUrl(issues, path + ".controlAppUrl", endpoint.ControlAppUrl, optional: true);
        }
    }

    private static void ValidateDeployment(DeploymentSettings deployment, List<ValidationIssue> issues)
    {
        var source = (deployment.UpdateSourcePath ?? string.Empty).Trim();
        if (source.Length > 0 && (!WindowsPath.IsFullyQualified(source) || WindowsPath.IsDevicePath(source)))
        {
            Error(issues, "deployment.updateSourcePath", "Must be a full path such as \\\\fileserver\\apps\\Toolbox.");
        }

        for (var i = 0; i < deployment.TrustedSignerThumbprints.Count; i++)
        {
            if (!Thumbprints.IsValid(deployment.TrustedSignerThumbprints[i]))
            {
                Error(issues, $"deployment.trustedSignerThumbprints[{i}]", "Must be a 40 (SHA-1) or 64 (SHA-256) character hex thumbprint.");
            }
        }

        if (deployment.RequireSignedUpdates && deployment.TrustedSignerThumbprints.Count == 0)
        {
            Error(issues, "deployment.requireSignedUpdates", "Signed updates are required but no trusted signer thumbprint is configured; updates are refused.");
        }

        if (source.Length > 0 && deployment.AutoUpdateOnClose && deployment.TrustedSignerThumbprints.Count == 0)
        {
            Warn(issues, "deployment.trustedSignerThumbprints",
                "Updates are checked for integrity but not authenticity. Pin a signing certificate so a modified share cannot push code.");
        }

        if (deployment.AllowDowngrade)
        {
            Warn(issues, "deployment.allowDowngrade", "Older releases on the share will replace newer installed versions.");
        }
    }

    private static void CheckUrl(List<ValidationIssue> issues, string path, string? url, bool optional)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            if (!optional)
            {
                Error(issues, path, "A URL is required.");
            }

            return;
        }

        var check = UrlPolicy.Check(url);
        if (!check.IsValid)
        {
            Error(issues, path, check.Error!);
        }
        else if (check.IsInsecure)
        {
            Warn(issues, path, "Uses http; prefer https.");
        }
    }

    private static void Range(List<ValidationIssue> issues, string path, int value, (int Min, int Max) range)
    {
        if (value < range.Min || value > range.Max)
        {
            Warn(issues, path, string.Format(CultureInfo.InvariantCulture,
                "{0} is outside {1}–{2}; the nearest allowed value is used.", value, range.Min, range.Max));
        }
    }

    private static void Error(List<ValidationIssue> issues, string path, string message) =>
        issues.Add(new ValidationIssue(IssueSeverity.Error, path, message));

    private static void Warn(List<ValidationIssue> issues, string path, string message) =>
        issues.Add(new ValidationIssue(IssueSeverity.Warning, path, message));
}
