using System.Diagnostics;
using Toolbox.Models;

namespace Toolbox.Services;

public sealed class RemoteToolService
{
    private readonly SettingsService _settings;

    public RemoteToolService(SettingsService settings)
    {
        _settings = settings;
    }

    public IReadOnlyList<AvailableRemoteTool> GetAvailableTools()
    {
        var userName = Environment.UserName;
        var available = new List<AvailableRemoteTool>();

        foreach (var tool in _settings.Current.RemoteTools.Where(t => t.Enabled))
        {
            var path = ResolveFirstExistingPath(tool.CandidatePaths, userName);
            if (path is null)
            {
                continue;
            }

            available.Add(new AvailableRemoteTool
            {
                Id = tool.Id,
                Label = tool.Label,
                ExecutablePath = path,
                ArgumentsTemplate = tool.Arguments,
                PreferAsDefault = tool.PreferAsDefault
            });
        }

        return available;
    }

    public void LaunchRemote(AvailableRemoteTool tool, string computerName)
    {
        var args = ExpandTokens(tool.ArgumentsTemplate, computerName);
        Process.Start(new ProcessStartInfo
        {
            FileName = tool.ExecutablePath,
            Arguments = args,
            UseShellExecute = true
        });
    }

    public void LaunchDetailAction(DetailActionDefinition action, string computerName)
    {
        var executable = ExpandTokens(action.Executable, computerName);
        var args = ExpandTokens(action.Arguments, computerName);
        Process.Start(new ProcessStartInfo
        {
            FileName = executable,
            Arguments = args,
            UseShellExecute = true
        });
    }

    private static string? ResolveFirstExistingPath(IEnumerable<string> candidates, string userName)
    {
        foreach (var candidate in candidates)
        {
            var expanded = candidate
                .Replace("{userName}", userName, StringComparison.OrdinalIgnoreCase)
                .Replace("%USERNAME%", userName, StringComparison.OrdinalIgnoreCase);

            expanded = Environment.ExpandEnvironmentVariables(expanded);
            if (File.Exists(expanded))
            {
                return expanded;
            }
        }

        return null;
    }

    private static string ExpandTokens(string template, string computerName)
    {
        return template
            .Replace("{computerName}", computerName, StringComparison.OrdinalIgnoreCase)
            .Replace("{userName}", Environment.UserName, StringComparison.OrdinalIgnoreCase);
    }
}
