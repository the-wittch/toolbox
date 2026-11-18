using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Toolbox.Diagnostics;
using Toolbox.Launching;
using Toolbox.Updates;

namespace Toolbox.Services;

public sealed class LocalInstallService
{
    private readonly SettingsService _settings;

    public LocalInstallService(SettingsService settings)
    {
        _settings = settings;
    }

    public static string DefaultLocalInstallDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Toolbox");

    public static string LocalExecutablePath => Path.Combine(DefaultLocalInstallDirectory, ReleaseManifest.EntryExecutable);

    public bool ShouldOfferLocalInstall
    {
        get
        {
            if (!AppDeploymentKind.SupportsLocalInstallOffer)
            {
                return false;
            }

            var running = UpdateService.InstallDirectory;
            if (SamePath(running, DefaultLocalInstallDirectory))
            {
                return false;
            }

            if (WindowsPath.IsUnc(running))
            {
                return true;
            }

            var source = _settings.Current.Deployment.UpdateSourcePath;
            return !string.IsNullOrWhiteSpace(source) && SamePath(running, source);
        }
    }

    public async Task<LocalInstallResult> InstallAsync(IProgress<double>? progress, CancellationToken cancellationToken)
    {
        var source = UpdateService.InstallDirectory;
        var destination = DefaultLocalInstallDirectory;

        if (UpdateService.OtherInstanceRunning(Path.TrimEndingDirectorySeparator(destination)))
        {
            return LocalInstallResult.Fail("Toolbox is already running from the local install folder. Close it and try again.");
        }

        try
        {
            var release = await UpdateService.ReadVerifiedReleaseAsync(source, _settings.Current.Deployment, cancellationToken).ConfigureAwait(false);
            if (UpdateApplier.IsInterrupted(destination))
            {
                UpdateApplier.Apply(destination);
            }

            await UpdateStager.StageAsync(source, destination, release.Manifest, progress, cancellationToken).ConfigureAwait(false);
            var applied = UpdateApplier.Apply(destination);
            if (!applied.Succeeded)
            {
                return LocalInstallResult.Fail(applied.Message);
            }

            var updateSource = WindowsPath.IsUnc(source)
                ? source
                : string.IsNullOrWhiteSpace(_settings.Current.Deployment.UpdateSourcePath) ? source : _settings.Current.Deployment.UpdateSourcePath.Trim();
            new InstallInfo
            {
                UpdateSourcePath = updateSource,
                InstalledVersion = VersionParsing.ToDisplay(release.Manifest.ParsedVersion),
                InstalledUtc = DateTimeOffset.UtcNow
            }.Save(destination);

            string? shortcut = null;
            try
            {
                shortcut = CreateDesktopShortcut(LocalExecutablePath, destination);
            }
            catch (Exception ex) when (ex is COMException or InvalidOperationException or UnauthorizedAccessException)
            {
                AppLog.Warn("Desktop shortcut could not be created.", ex);
            }

            AppLog.Info($"Installed {release.Manifest.Version} to {destination} from {source} (signed={release.Signature.IsSigned}).");
            return LocalInstallResult.Ok(destination, LocalExecutablePath, shortcut);
        }
        catch (OperationCanceledException)
        {
            return LocalInstallResult.Fail("Installation was cancelled.");
        }
        catch (Exception ex) when (ex is ManifestException or UpdateIntegrityException or InvalidOperationException)
        {
            AppLog.Error("Local install failed.", ex);
            return LocalInstallResult.Fail(ex.Message);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or TimeoutException)
        {
            AppLog.Error("Local install failed.", ex);
            return LocalInstallResult.Fail(ErrorTranslator.Describe(ex));
        }
    }

    public static bool TryLaunchLocalInstall()
    {
        if (!File.Exists(LocalExecutablePath))
        {
            return false;
        }

        try
        {
            using var process = Process.Start(new ProcessStartInfo(LocalExecutablePath)
            {
                WorkingDirectory = DefaultLocalInstallDirectory,
                UseShellExecute = false
            });
            return true;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            AppLog.Warn("Could not start the local install.", ex);
            return false;
        }
    }

    private static string CreateDesktopShortcut(string exePath, string workingDirectory)
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var shortcutPath = Path.Combine(desktop, "Toolbox.lnk");
        var iconPath = Path.Combine(workingDirectory, "Assets", "AppIcon.ico");

        var shellType = Type.GetTypeFromProgID("WScript.Shell")
                        ?? throw new InvalidOperationException("Windows Script Host is not available.");
        dynamic shell = Activator.CreateInstance(shellType)!;
        try
        {
            dynamic link = shell.CreateShortcut(shortcutPath);
            try
            {
                link.TargetPath = exePath;
                link.WorkingDirectory = workingDirectory;
                link.Description = "Toolbox";
                link.IconLocation = (File.Exists(iconPath) ? iconPath : exePath) + ",0";
                link.Save();
            }
            finally
            {
                Marshal.FinalReleaseComObject(link);
            }
        }
        finally
        {
            Marshal.FinalReleaseComObject(shell);
        }

        return shortcutPath;
    }

    private static bool SamePath(string a, string b)
    {
        try
        {
            return string.Equals(
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(a.Trim())),
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(b.Trim())),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }
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

    public static LocalInstallResult Fail(string message) => new() { Succeeded = false, Message = message };
}
