using Toolbox.Models;

namespace Toolbox.Configuration;

public static class SettingsNormalizer
{
    public static void FillNulls(AppSettings s)
    {
        s.ProgramTitle ??= "Toolbox";
        s.Theme ??= "System";
        s.Ldap ??= new LdapSettings();
        s.Search ??= new SearchSettings();
        s.Search.ComputerNamePrefixes ??= new List<string>();
        s.Search.Presets ??= new List<SearchPreset>();
        s.AccessControl ??= new AccessControlSettings();
        s.AccessControl.AllowedGroups ??= new List<string>();
        s.Ping ??= new PingSettings();
        s.Details ??= new DetailSettings();
        s.Laps ??= new LapsSettings();
        s.Deployment ??= new DeploymentSettings();
        s.Deployment.TrustedSignerThumbprints ??= new List<string>();
        s.Streaming ??= new StreamingSettings();
        s.Streaming.Endpoints ??= new List<StreamingEndpoint>();
        s.RemoteTools ??= new List<RemoteToolDefinition>();
        s.DetailActions ??= new List<DetailActionDefinition>();
        s.Labels ??= new LabelSettings();
        s.Ui ??= new UiSettings();

        s.Search.Presets.RemoveAll(p => p is null);
        s.Streaming.Endpoints.RemoveAll(e => e is null);
        s.RemoteTools.RemoveAll(t => t is null);
        s.DetailActions.RemoveAll(a => a is null);
        foreach (var tool in s.RemoteTools)
        {
            tool.CandidatePaths ??= new List<string>();
        }
    }

    public static void CopyAdminOnlySettings(AppSettings from, AppSettings to)
    {
        to.AccessControl = from.AccessControl.Clone();
        to.Deployment.TrustedSignerThumbprints = new List<string>(from.Deployment.TrustedSignerThumbprints);
        to.Deployment.RequireSignedUpdates = from.Deployment.RequireSignedUpdates;
        to.Deployment.AllowDowngrade = from.Deployment.AllowDowngrade;
    }

    internal static readonly string[] AdminOnlyPaths =
    {
        "accessControl",
        "deployment.trustedSignerThumbprints",
        "deployment.requireSignedUpdates",
        "deployment.allowDowngrade"
    };
}
