using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Toolbox.Models;

namespace Toolbox.Services;

public sealed class LocalInstallService
{
    private readonly SettingsService _settings;

    public LocalInstallService(SettingsService settings)
    {
        _settings = settings;
    }

    public static string DefaultLocalInstallDirectory { get; } =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs",
            "Toolbox");

    public static string LocalExecutablePath =>
        Path.Combine(DefaultLocalInstallDirectory, "Toolbox.exe");

    public bool IsRunningFromLocalInstall =>
        PathsEqual(NormalizePath(AppContext.BaseDirectory), NormalizePath(DefaultLocalInstallDirectory));

    public bool ShouldOfferLocalInstall
    {
        get
        {

            if (!AppDeploymentKind.SupportsLocalInstallOffer)
            {
                return false;
            }

            if (IsRunningFromLocalInstall)
            {
                return false;
            }

            var running = NormalizePath(AppContext.BaseDirectory);
            if (running.StartsWith(@"\\", StringComparison.Ordinal))
            {
                return true;
            }

            var updateSource = NormalizePath(_settings.Current.Deployment.UpdateSourcePath);
            return !string.IsNullOrWhiteSpace(updateSource) && PathsEqual(running, updateSource);
        }
    }

    public LocalInstallResult Install(
        string? sourceDirectory = null,
        bool createDesktopShortcut = true)
    {
        var source = NormalizePath(string.IsNullOrWhiteSpace(sourceDirectory)
            ? AppContext.BaseDirectory
            : sourceDirectory);

        var destination = NormalizePath(DefaultLocalInstallDirectory);

        if (string.IsNullOrWhiteSpace(source) || !Directory.Exists(source))
        {
            return LocalInstallResult.Fail($"Source folder not found: {source}");
        }

        if (PathsEqual(source, destination))
        {
            return LocalInstallResult.Fail("Toolbox is already running from the local install folder.");
        }

        try
        {
            Directory.CreateDirectory(destination);
            var exitCode = RunRobocopy(source, destination);

            if (exitCode >= 8)
            {
                return LocalInstallResult.Fail($"Copy failed (robocopy exit {exitCode}).");
            }

            if (!File.Exists(LocalExecutablePath))
            {
                return LocalInstallResult.Fail($"Toolbox.exe missing after copy to {destination}.");
            }

            EnsureUpdateSourcePath(destination, source);
            string? shortcutPath = null;
            if (createDesktopShortcut)
            {
                shortcutPath = CreateDesktopShortcut(LocalExecutablePath, destination);
            }

            return LocalInstallResult.Ok(destination, LocalExecutablePath, shortcutPath);
        }
        catch (Exception ex)
        {
            return LocalInstallResult.Fail(ex.Message);
        }
    }

    public bool TryLaunchLocalInstallAndExit(WindowCloser closeWindow)
    {
        if (!File.Exists(LocalExecutablePath))
        {
            return false;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = LocalExecutablePath,
                WorkingDirectory = DefaultLocalInstallDirectory,
                UseShellExecute = true
            });
            closeWindow();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public delegate void WindowCloser();

    private void EnsureUpdateSourcePath(string destination, string source)
    {
        var updateSource = source.StartsWith(@"\\", StringComparison.Ordinal)
            ? source
            : NormalizePath(_settings.Current.Deployment.UpdateSourcePath);

        if (string.IsNullOrWhiteSpace(updateSource))
        {
            updateSource = source;
        }

        _settings.Current.Deployment.UpdateSourcePath = updateSource;
        _settings.Current.Deployment.AutoUpdateOnClose = true;

        var appsettingsPath = Path.Combine(destination, "appsettings.json");
        TryPatchDeploymentSettings(appsettingsPath, updateSource, autoUpdateOnClose: true);
    }

    private static void TryPatchDeploymentSettings(string appsettingsPath, string updateSource, bool autoUpdateOnClose)
    {
        try
        {
            JsonNode root;
            if (File.Exists(appsettingsPath))
            {
                root = JsonNode.Parse(File.ReadAllText(appsettingsPath)) ?? new JsonObject();
            }
            else
            {
                root = new JsonObject();
            }

            var deployment = root["deployment"] as JsonObject ?? new JsonObject();
            deployment["updateSourcePath"] = updateSource;
            deployment["autoUpdateOnClose"] = autoUpdateOnClose;
            root["deployment"] = deployment;

            var json = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(appsettingsPath, json);
        }
        catch
        {

        }
    }

    private static string? CreateDesktopShortcut(string exePath, string workingDirectory)
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var shortcutPath = Path.Combine(desktop, "Toolbox.lnk");

        var iconPath = Path.Combine(workingDirectory, "Assets", "AppIcon.ico");
        var iconLocation = File.Exists(iconPath) ? $"{iconPath},0" : $"{exePath},0";

        var ps = $@"
$s = New-Object -ComObject WScript.Shell
$l = $s.CreateShortcut('{shortcutPath.Replace("'", "''")}')
$l.TargetPath = '{exePath.Replace("'", "''")}'
$l.WorkingDirectory = '{workingDirectory.Replace("'", "''")}'
$l.Description = 'Toolbox'
$l.IconLocation = '{iconLocation.Replace("'", "''")}'
$l.Save()
";
        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = "-NoProfile -ExecutionPolicy Bypass -Command " + PsQuote(ps),
            CreateNoWindow = true,
            UseShellExecute = false
        };
        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Could not start PowerShell to create the shortcut.");
        process.WaitForExit();
        if (process.ExitCode != 0 || !File.Exists(shortcutPath))
        {
            throw new InvalidOperationException("Desktop shortcut creation failed.");
        }

        return shortcutPath;
    }

    private static string PsQuote(string script) =>
        '"' + script.Replace("\"", "`\"") + '"';

    private static int RunRobocopy(string source, string destination)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "robocopy.exe",
            Arguments = $"\"{source}\" \"{destination}\" /E /XO /R:2 /W:1 /NFL /NDL /NJH /NJS /XD .git",
            CreateNoWindow = true,
            UseShellExecute = false
        };

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Could not start robocopy.");
        process.WaitForExit();
        return process.ExitCode;
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

public sealed class LocalInstallResult
{
    public bool Succeeded { get; init; }
    public string Message { get; init; } = string.Empty;
    public string? DestinationDirectory { get; init; }
    public string? ExecutablePath { get; init; }
    public string? ShortcutPath { get; init; }

    public static LocalInstallResult Ok(string destination, string exe, string? shortcut) => new()
    {
        Succeeded = true,
        Message = "Installed locally.",
        DestinationDirectory = destination,
        ExecutablePath = exe,
        ShortcutPath = shortcut
    };

    public static LocalInstallResult Fail(string message) => new()
    {
        Succeeded = false,
        Message = message
    };
}
