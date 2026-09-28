using System.Diagnostics;
using Toolbox.Models;

namespace Toolbox.Services;

/// <summary>
/// Schedules a post-exit file sync from the configured install/share folder into
/// the running app directory. Toolbox.exe cannot overwrite itself while running,
/// so a short cmd script waits for this process to exit, then robocopies newer files.
/// </summary>
public sealed class UpdateService
{
    private readonly SettingsService _settings;

    public UpdateService(SettingsService settings)
    {
        _settings = settings;
    }

    /// <summary>
    /// Returns true if an updater process was started.
    /// Must stay fast and offline: never touch the UNC share on the UI/close thread
    /// (Directory.Exists / file scans can hang for tens of seconds and freeze close).
    /// </summary>
    public bool TryScheduleUpdateOnClose()
    {
        // Packaged (MSIX) installs are updated by SCCM — never robocopy over Program Files\WindowsApps.
        if (!AppDeploymentKind.SupportsFileSyncUpdates)
        {
            return false;
        }

        var deployment = _settings.Current.Deployment;
        if (!deployment.AutoUpdateOnClose)
        {
            return false;
        }

        var source = NormalizePath(deployment.UpdateSourcePath);
        if (string.IsNullOrWhiteSpace(source))
        {
            return false;
        }

        var destination = NormalizePath(AppContext.BaseDirectory);
        if (PathsEqual(source, destination))
        {
            // Already running from the install path — nothing to sync.
            return false;
        }

        // Do not probe the share here. The deferred script checks source + runs robocopy /XO.
        return LaunchDeferredUpdater(source, destination);
    }

    public static string SuggestUpdateSourcePath(DeploymentSettings deployment)
    {
        if (!string.IsNullOrWhiteSpace(deployment.UpdateSourcePath))
        {
            return deployment.UpdateSourcePath.Trim();
        }

        var baseDir = NormalizePath(AppContext.BaseDirectory);
        if (baseDir.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return baseDir;
        }

        return string.Empty;
    }

    private static bool LaunchDeferredUpdater(string source, string destination)
    {
        try
        {
            var pid = Environment.ProcessId;
            var scriptPath = Path.Combine(Path.GetTempPath(), $"ToolboxUpdate-{pid}.cmd");
            var logPath = Path.Combine(Path.GetTempPath(), $"ToolboxUpdate-{pid}.log");

            // Wait for this PID to exit, then robocopy newer files only (/XO).
            // User OU/theme live in LocalAppData and are not overwritten by this sync.
            // Keep retries low so a dead share cannot hang the updater for long.
            var script = $"""
                @echo off
                setlocal
                :wait
                tasklist /FI "PID eq {pid}" 2>nul | find "{pid}" >nul
                if not errorlevel 1 (
                  timeout /t 1 /nobreak >nul
                  goto wait
                )
                echo Updating Toolbox from "{source}" to "{destination}" > "{logPath}"
                if not exist "{source}\Toolbox.exe" (
                  echo Source Toolbox.exe not found. Skipping update. >> "{logPath}"
                  goto done
                )
                robocopy "{source}" "{destination}" /E /XO /R:1 /W:1 /NFL /NDL /NP /XD .git >> "{logPath}" 2>&1
                :done
                del "%~f0"
                endlocal
                """;

            File.WriteAllText(scriptPath, script);

            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c \"{scriptPath}\"",
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden
            });

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        try
        {
            return Path.GetFullPath(path.Trim().TrimEnd('\\', '/'));
        }
        catch
        {
            return path.Trim().TrimEnd('\\', '/');
        }
    }

    private static bool PathsEqual(string a, string b) =>
        string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
