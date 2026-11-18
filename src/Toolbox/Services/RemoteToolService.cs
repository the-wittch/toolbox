using System.ComponentModel;
using System.Diagnostics;
using Toolbox.Diagnostics;
using Toolbox.Launching;
using Toolbox.Ldap;
using Toolbox.Models;

namespace Toolbox.Services;

public sealed record LaunchResult(bool Succeeded, string Message);

public sealed class RemoteToolService
{
    private const int ErrorElevationRequired = 740;

    private readonly SettingsService _settings;
    private readonly ExecutableResolver _resolver = new(
        File.Exists,
        new[] { Environment.SystemDirectory, Environment.GetFolderPath(Environment.SpecialFolder.Windows) },
        Environment.ExpandEnvironmentVariables);

    public RemoteToolService(SettingsService settings)
    {
        _settings = settings;
    }

    public IReadOnlyList<AvailableRemoteTool> GetAvailableTools()
    {
        var available = new List<AvailableRemoteTool>();
        foreach (var tool in _settings.Current.RemoteTools.Where(t => t.Enabled))
        {
            var resolved = tool.CandidatePaths
                .Select(path => _resolver.Resolve(path, Environment.UserName))
                .FirstOrDefault(r => r.Succeeded);
            if (resolved is null)
            {
                continue;
            }

            available.Add(new AvailableRemoteTool
            {
                Id = tool.Id,
                Label = string.IsNullOrWhiteSpace(tool.Label) ? tool.Id : tool.Label,
                ExecutablePath = resolved.Path!,
                ArgumentsTemplate = tool.Arguments,
                PreferAsDefault = tool.PreferAsDefault
            });
        }

        return available;
    }

    public LaunchResult LaunchRemote(AvailableRemoteTool tool, string computerName) =>
        Launch(tool.Label, tool.ExecutablePath, tool.ArgumentsTemplate, computerName);

    public LaunchResult LaunchDetailAction(DetailActionDefinition action, string computerName)
    {
        var resolved = _resolver.Resolve(action.Executable, Environment.UserName);
        return resolved.Succeeded
            ? Launch(action.Label, resolved.Path!, action.Arguments, computerName)
            : new LaunchResult(false, $"{action.Label}: {resolved.Error}");
    }

    private static LaunchResult Launch(string label, string executable, string argumentTemplate, string computerName)
    {
        if (!HostNameRules.IsValidComputerName(computerName))
        {
            return new LaunchResult(false, $"'{computerName}' is not a valid host name, so {label} was not started.");
        }

        IReadOnlyList<string> arguments;
        try
        {
            arguments = CommandTemplate.Expand(argumentTemplate, computerName, Environment.UserName);
        }
        catch (FormatException ex)
        {
            return new LaunchResult(false, $"{label}: {ex.Message}");
        }

        var commandLine = WindowsCommandLine.Join(arguments);
        var workingDirectory = WindowsPath.IsUnc(executable)
            ? Environment.SystemDirectory
            : Path.GetDirectoryName(executable) ?? Environment.SystemDirectory;

        try
        {
            Start(executable, commandLine, workingDirectory, elevate: false);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorElevationRequired)
        {
            try
            {
                Start(executable, commandLine, workingDirectory, elevate: true);
            }
            catch (Win32Exception inner)
            {
                AppLog.Warn($"Elevated launch of {label} failed.", inner);
                return new LaunchResult(false, $"{label} needs administrator rights and was not started.");
            }
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            AppLog.Warn($"Launching {label} ({executable}) failed.", ex);
            return new LaunchResult(false, $"Could not start {label}: {ErrorTranslator.Describe(ex)}");
        }

        AppLog.Info($"Launched {label} for {computerName}.");
        return new LaunchResult(true, $"Started {label} for {computerName}.");
    }

    private static void Start(string executable, string arguments, string workingDirectory, bool elevate)
    {
        var info = new ProcessStartInfo(executable, arguments)
        {
            UseShellExecute = elevate,
            WorkingDirectory = workingDirectory
        };
        if (elevate)
        {
            info.Verb = "runas";
        }

        using var process = Process.Start(info);
    }
}
