using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Toolbox.Diagnostics;
using Toolbox.IO;
using Toolbox.Launching;
using Toolbox.Models;
using Toolbox.Updates;

namespace Toolbox.Services;

public enum UpdateCheckStatus
{
    NotEligible,
    UpToDate,
    Ready,
    Failed
}

public sealed record UpdateCheckResult(UpdateCheckStatus Status, string Message, Version? Version = null);

public sealed record VerifiedRelease(ReleaseManifest Manifest, SignatureCheck Signature);

public sealed class UpdateService
{
    private static readonly TimeSpan ShareTimeout = TimeSpan.FromSeconds(30);

    private readonly SettingsService _settings;

    public UpdateService(SettingsService settings)
    {
        _settings = settings;
    }

    public static Version CurrentVersion { get; } = ReadCurrentVersion();

    public static string InstallDirectory { get; } =
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(AppContext.BaseDirectory));

    private static string LastResultPath => Path.Combine(SettingsService.AppDataFolder, "last-update.json");

    public async Task<UpdateCheckResult> CheckAndStageAsync(CancellationToken cancellationToken)
    {
        var (eligible, reason, source) = GetEligibility();
        if (!eligible)
        {
            AppLog.Info("Update check skipped: " + reason);
            return new UpdateCheckResult(UpdateCheckStatus.NotEligible, reason);
        }

        // no ConfigureAwait(false) below: the mutex has to be released on this (UI) thread
        using var mutex = AcquireUpdateLock();
        if (mutex is null)
        {
            return new UpdateCheckResult(UpdateCheckStatus.NotEligible, "Another Toolbox window is already checking for updates.");
        }

        try
        {
            UpdateApplier.CleanupOldFiles(InstallDirectory);
            if (UpdateApplier.IsInterrupted(InstallDirectory))
            {
                return new UpdateCheckResult(UpdateCheckStatus.Ready, "A previous update did not finish. It will be completed when Toolbox closes.");
            }

            if (UpdateApplier.TryGetStagedVersion(InstallDirectory, out var staged) && staged > CurrentVersion)
            {
                return new UpdateCheckResult(UpdateCheckStatus.Ready, $"Version {VersionParsing.ToDisplay(staged)} will be installed when Toolbox closes.", staged);
            }

            var release = await ReadVerifiedReleaseAsync(source!, _settings.Current.Deployment, cancellationToken);
            var version = release.Manifest.ParsedVersion;
            if (version == CurrentVersion)
            {
                UpdateApplier.Discard(InstallDirectory);
                return new UpdateCheckResult(UpdateCheckStatus.UpToDate, "Toolbox is up to date.", version);
            }

            if (version < CurrentVersion && !_settings.Current.Deployment.AllowDowngrade)
            {
                AppLog.Warn($"Update source has older version {version}; downgrade protection kept {CurrentVersion}.");
                return new UpdateCheckResult(UpdateCheckStatus.UpToDate, $"The update source has an older version ({VersionParsing.ToDisplay(version)}); it was not installed.", version);
            }

            var outcome = await UpdateStager.StageAsync(source!, InstallDirectory, release.Manifest, null, cancellationToken);
            AppLog.Info($"Staged {version} from {source}: {outcome.ChangedFiles} changed, {outcome.UnchangedFiles} unchanged, signed={release.Signature.IsSigned}.");
            return new UpdateCheckResult(UpdateCheckStatus.Ready, $"Version {VersionParsing.ToDisplay(version)} will be installed when Toolbox closes.", version);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is ManifestException or UpdateIntegrityException or InvalidOperationException)
        {
            AppLog.Error("Update check failed.", ex);
            return new UpdateCheckResult(UpdateCheckStatus.Failed, ex.Message);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or TimeoutException)
        {
            AppLog.Warn("Update source could not be read.", ex);
            return new UpdateCheckResult(UpdateCheckStatus.Failed, "The update source could not be reached: " + ErrorTranslator.Describe(ex));
        }
    }

    public ApplyResult? ApplyPendingOnExit()
    {
        if (!GetEligibility().Eligible)
        {
            return null;
        }

        var hasStaged = UpdateApplier.TryGetStagedVersion(InstallDirectory, out var staged);
        if (!hasStaged || (staged <= CurrentVersion && !UpdateApplier.IsInterrupted(InstallDirectory) && !_settings.Current.Deployment.AllowDowngrade))
        {
            return null;
        }

        if (OtherInstanceRunning())
        {
            AppLog.Info("Staged update kept: another Toolbox window is still open.");
            return null;
        }

        using var mutex = AcquireUpdateLock();
        if (mutex is null)
        {
            return null;
        }

        var result = UpdateApplier.Apply(InstallDirectory);
        if (result.Succeeded)
        {
            AppLog.Info(result.Message);
        }
        else
        {
            AppLog.Error(result.Message);
        }

        try
        {
            AtomicFile.WriteAllText(LastResultPath, JsonSerializer.Serialize(new LastUpdate(result.Succeeded, result.Message)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn("Could not record the update result.", ex);
        }

        return result;
    }

    public static (bool Succeeded, string Message)? ConsumeLastResult()
    {
        try
        {
            if (!File.Exists(LastResultPath))
            {
                return null;
            }

            var last = JsonSerializer.Deserialize<LastUpdate>(File.ReadAllText(LastResultPath));
            File.Delete(LastResultPath);
            return last is null ? null : (last.Succeeded, last.Message);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static async Task<VerifiedRelease> ReadVerifiedReleaseAsync(string sourceDirectory, DeploymentSettings deployment, CancellationToken cancellationToken)
    {
        var manifestPath = Path.Combine(sourceDirectory, ReleaseManifest.FileName);
        var signaturePath = Path.Combine(sourceDirectory, ReleaseManifest.SignatureFileName);

        var read = Task.Run(() =>
        {
            if (!File.Exists(manifestPath))
            {
                throw new ManifestException($"No {ReleaseManifest.FileName} was found in {sourceDirectory}. Republish the release with scripts\\publish.ps1.");
            }

            if (new FileInfo(manifestPath).Length > ReleaseManifest.MaxManifestBytes)
            {
                throw new ManifestException("The release manifest is too large.");
            }

            var manifestBytes = File.ReadAllBytes(manifestPath);
            var signatureBytes = File.Exists(signaturePath) ? File.ReadAllBytes(signaturePath) : null;
            return (manifestBytes, signatureBytes);
        }, CancellationToken.None);

        var (manifest, signature) = await read.WaitAsync(ShareTimeout, cancellationToken);

        var check = ManifestVerifier.Verify(manifest, signature, deployment.TrustedSignerThumbprints, deployment.RequireSignedUpdates);
        if (!check.IsTrusted)
        {
            throw new ManifestException(check.Error ?? "The release could not be verified.");
        }

        return new VerifiedRelease(ReleaseManifest.Parse(manifest), check);
    }

    private (bool Eligible, string Reason, string? Source) GetEligibility()
    {
        if (!AppDeploymentKind.SupportsFileSyncUpdates)
        {
            return (false, "packaged (MSIX) installs are updated by your deployment tool.", null);
        }

        var deployment = _settings.Current.Deployment;
        if (!deployment.AutoUpdateOnClose)
        {
            return (false, "automatic updates are turned off.", null);
        }

        if (WindowsPath.IsUnc(InstallDirectory))
        {
            return (false, "Toolbox is running from a network folder.", null);
        }

        var info = InstallInfo.TryLoad(InstallDirectory) ?? MigrateLegacyInstall(deployment);
        if (info is null)
        {
            return (false, "this folder was not installed by Install-Toolbox.ps1 or the in-app installer.", null);
        }

        var source = !string.IsNullOrWhiteSpace(deployment.UpdateSourcePath)
            ? deployment.UpdateSourcePath.Trim()
            : info.UpdateSourcePath?.Trim() ?? string.Empty;
        if (source.Length == 0 || !WindowsPath.IsFullyQualified(source))
        {
            return (false, "no valid update source is configured.", null);
        }

        if (string.Equals(Path.TrimEndingDirectorySeparator(source), InstallDirectory, StringComparison.OrdinalIgnoreCase))
        {
            return (false, "Toolbox is running from the update source.", null);
        }

        return (true, string.Empty, source);
    }

    // installs made before 3.0 kept the update path in appsettings.json and had no install marker
    private static InstallInfo? MigrateLegacyInstall(DeploymentSettings deployment)
    {
        if (string.IsNullOrWhiteSpace(deployment.UpdateSourcePath) ||
            !string.Equals(InstallDirectory, Path.TrimEndingDirectorySeparator(LocalInstallService.DefaultLocalInstallDirectory), StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var info = new InstallInfo { UpdateSourcePath = deployment.UpdateSourcePath.Trim(), InstalledVersion = VersionParsing.ToDisplay(CurrentVersion) };
        try
        {
            info.Save(InstallDirectory);
            AppLog.Info("Created install marker for an existing local install.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn("Could not write the install marker.", ex);
            return null;
        }

        return info;
    }

    public static bool OtherInstanceRunning(string? directory = null)
    {
        var target = directory ?? InstallDirectory;
        var self = Environment.ProcessId;
        foreach (var process in Process.GetProcessesByName("Toolbox"))
        {
            using (process)
            {
                if (process.Id == self)
                {
                    continue;
                }

                try
                {
                    var path = process.MainModule?.FileName;
                    if (path is not null && string.Equals(Path.GetDirectoryName(path), target, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static UpdateLock? AcquireUpdateLock()
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(InstallDirectory.ToUpperInvariant())))[..16];
        var mutex = new Mutex(false, @"Local\Toolbox.Update." + hash);
        try
        {
            if (mutex.WaitOne(0))
            {
                return new UpdateLock(mutex);
            }
        }
        catch (AbandonedMutexException)
        {
            return new UpdateLock(mutex);
        }

        mutex.Dispose();
        return null;
    }

    private static Version ReadCurrentVersion()
    {
        var informational = typeof(UpdateService).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        return VersionParsing.TryParse(informational, out var version)
            ? version
            : VersionParsing.Normalize(typeof(UpdateService).Assembly.GetName().Version ?? new Version(0, 0));
    }

    private sealed record LastUpdate(bool Succeeded, string Message);

    private sealed class UpdateLock : IDisposable
    {
        private readonly Mutex _mutex;

        public UpdateLock(Mutex mutex)
        {
            _mutex = mutex;
        }

        public void Dispose()
        {
            _mutex.ReleaseMutex();
            _mutex.Dispose();
        }
    }
}
