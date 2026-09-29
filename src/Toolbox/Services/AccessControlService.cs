using System.Security.Principal;

namespace Toolbox.Services;

public sealed class AccessControlService
{
    private readonly SettingsService _settings;

    public AccessControlService(SettingsService settings)
    {
        _settings = settings;
    }

    public bool HasAccess()
    {
        var config = _settings.Current.AccessControl;
        if (!config.Enabled || config.AllowedGroups.Count == 0)
        {
            return true;
        }

        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return config.AllowedGroups.Any(group =>
            !string.IsNullOrWhiteSpace(group) && principal.IsInRole(group));
    }

    public string DenyMessage => _settings.Current.AccessControl.DenyMessage;
}
