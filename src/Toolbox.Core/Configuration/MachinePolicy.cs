using Toolbox.Models;

namespace Toolbox.Configuration;

public sealed class MachinePolicy
{
    public const string RegistryKeyPath = @"SOFTWARE\Policies\Toolbox";

    public IReadOnlyList<string>? AllowedGroups { get; init; }
    public string? DenyMessage { get; init; }
    public string? UpdateSourcePath { get; init; }
    public bool? AutoUpdate { get; init; }
    public IReadOnlyList<string>? TrustedUpdateSigners { get; init; }

    public string? ReadError { get; init; }

    public bool IsEmpty =>
        AllowedGroups is null && DenyMessage is null && UpdateSourcePath is null &&
        AutoUpdate is null && TrustedUpdateSigners is null;

    public void ApplyTo(AppSettings settings)
    {
        var groups = Clean(AllowedGroups);
        if (groups.Count > 0)
        {
            settings.AccessControl.Enabled = true;
            settings.AccessControl.AllowedGroups = groups;
        }

        if (!string.IsNullOrWhiteSpace(DenyMessage))
        {
            settings.AccessControl.DenyMessage = DenyMessage.Trim();
        }

        if (!string.IsNullOrWhiteSpace(UpdateSourcePath))
        {
            settings.Deployment.UpdateSourcePath = UpdateSourcePath.Trim();
        }

        if (AutoUpdate is { } autoUpdate)
        {
            settings.Deployment.AutoUpdateOnClose = autoUpdate;
        }

        var signers = Clean(TrustedUpdateSigners);
        if (signers.Count > 0)
        {
            settings.Deployment.TrustedSignerThumbprints = signers;
            settings.Deployment.RequireSignedUpdates = true;
        }
    }

    private static List<string> Clean(IReadOnlyList<string>? values) =>
        values is null
            ? new List<string>()
            : values.Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(v => v.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
}
