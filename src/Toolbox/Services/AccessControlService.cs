using System.Security.Principal;
using Toolbox.Security;

namespace Toolbox.Services;

public sealed class AccessControlService
{
    private readonly SettingsService _settings;

    public AccessControlService(SettingsService settings)
    {
        _settings = settings;
    }

    public string DenyMessage => _settings.Current.AccessControl.DenyMessage;

    public AccessDecision Evaluate()
    {
        if (_settings.Policy.ReadError is { } error)
        {
            return new AccessDecision(false, "Machine policy could not be read: " + error);
        }

        var config = _settings.Current.AccessControl;
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return AccessPolicy.Evaluate(config.Enabled, config.AllowedGroups, group =>
            AccessPolicy.LooksLikeSid(group)
                ? principal.IsInRole(new SecurityIdentifier(group))
                : principal.IsInRole(group));
    }
}
