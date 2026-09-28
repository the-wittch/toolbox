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

        if (!Directory.Exists(source))
        {
            return false;
        }

        if (!HasNewerOrMissingFiles(source, destination))
        {
            return false;
        }

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

            // /XO = skip files that are older/same on the destination (copy newer only).
            // User OU/theme live in LocalAppData and are not overwritten by this sync.
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
                robocopy "{source}" "{destination}" /E /XO /R:2 /W:1 /NFL /NDL /NP /XD .git >> "{logPath}" 2>&1
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

    private static bool HasNewerOrMissingFiles(string source, string destination)
    {
        foreach (var sourceFile in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, sourceFile);
            if (relative.StartsWith(".git", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var destFile = Path.Combine(destination, relative);
            if (!File.Exists(destFile))
            {
                return true;
            }

            var sourceTime = File.GetLastWriteTimeUtc(sourceFile);
            var destTime = File.GetLastWriteTimeUtc(destFile);
            // 2s skew tolerance for copy/FAT/UNC timestamps
            if (sourceTime > destTime.AddSeconds(2))
            {
                return true;
            }
        }

        return false;
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
