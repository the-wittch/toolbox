using System.Text.Json;
using Toolbox.IO;

namespace Toolbox.Updates;

public sealed record ApplyResult(bool Succeeded, Version? Version, int FilesReplaced, string Message);

public static class UpdateApplier
{
    public static bool IsInterrupted(string installDirectory) =>
        File.Exists(UpdateLayout.JournalPath(installDirectory));

    public static StagedMarker? ReadMarker(string installDirectory)
    {
        var path = UpdateLayout.MarkerPath(installDirectory);
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<StagedMarker>(File.ReadAllText(path)) : null;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static bool TryGetStagedVersion(string installDirectory, out Version version)
    {
        version = new Version(0, 0, 0, 0);
        var marker = ReadMarker(installDirectory);
        return marker is not null && VersionParsing.TryParse(marker.Version, out version);
    }

    public static ApplyResult Apply(string installDirectory)
    {
        var marker = ReadMarker(installDirectory);
        if (marker is null || !VersionParsing.TryParse(marker.Version, out var version))
        {
            return new ApplyResult(false, null, 0, "No staged update was found.");
        }

        var resuming = IsInterrupted(installDirectory);
        var staging = UpdateLayout.StagingDirectory(installDirectory);
        var swapped = new List<(string Target, string? Old, string Staged)>();

        try
        {
            AtomicFile.WriteAllText(UpdateLayout.JournalPath(installDirectory), marker.Version);

            foreach (var relative in marker.Files)
            {
                if (ManifestPaths.Validate(relative) is { } pathError)
                {
                    throw new UpdateIntegrityException($"Staged entry '{relative}' is not allowed: {pathError}");
                }

                var target = ManifestPaths.Resolve(installDirectory, relative);
                var staged = ManifestPaths.Resolve(staging, relative);
                if (!File.Exists(staged))
                {
                    if (resuming)
                    {
                        continue;
                    }

                    throw new UpdateIntegrityException($"Staged file '{relative}' is missing.");
                }

                string? old = null;
                // files in use by a running process can be renamed, just not overwritten
                if (File.Exists(target))
                {
                    old = ReserveOldName(target);
                    File.Move(target, old);
                }

                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.Move(staged, target);
                }
                catch
                {
                    if (old is not null)
                    {
                        File.Move(old, target);
                    }

                    throw;
                }

                swapped.Add((target, old, staged));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or UpdateIntegrityException or ManifestException)
        {
            var rollbackErrors = Rollback(swapped);
            if (rollbackErrors == 0)
            {
                AtomicFile.TryDelete(UpdateLayout.JournalPath(installDirectory));
                return new ApplyResult(false, version, 0, $"The update could not be installed and was rolled back: {ex.Message}");
            }

            return new ApplyResult(false, version, 0,
                $"The update failed and {rollbackErrors} file(s) could not be restored; it will be retried when Toolbox next closes. ({ex.Message})");
        }

        var info = InstallInfo.TryLoad(installDirectory);
        if (info is not null)
        {
            info.InstalledVersion = marker.Version;
            info.InstalledUtc = DateTimeOffset.UtcNow;
            try
            {
                info.Save(installDirectory);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }

        UpdateStager.DeleteDirectory(UpdateLayout.WorkDirectory(installDirectory));
        CleanupOldFiles(installDirectory);
        return new ApplyResult(true, version, swapped.Count, $"Updated to {marker.Version}.");
    }

    public static void Discard(string installDirectory)
    {
        if (!IsInterrupted(installDirectory))
        {
            UpdateStager.DeleteDirectory(UpdateLayout.WorkDirectory(installDirectory));
        }
    }

    public static int CleanupOldFiles(string installDirectory)
    {
        if (!Directory.Exists(installDirectory))
        {
            return 0;
        }

        var work = Path.GetFullPath(UpdateLayout.WorkDirectory(installDirectory));
        var deleted = 0;
        IEnumerable<string> candidates;
        try
        {
            candidates = Directory.EnumerateFiles(installDirectory, "*" + UpdateLayout.OldFileMarker + "*", SearchOption.AllDirectories).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }

        foreach (var file in candidates)
        {
            if (Path.GetFullPath(file).StartsWith(work, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (AtomicFile.TryDelete(file))
            {
                deleted++;
            }
        }

        return deleted;
    }

    private static string ReserveOldName(string target)
    {
        var candidate = target + UpdateLayout.OldFileMarker;
        if (!File.Exists(candidate) || AtomicFile.TryDelete(candidate))
        {
            return candidate;
        }

        return target + UpdateLayout.OldFileMarker + "-" +
               Guid.NewGuid().ToString("N")[..8];
    }

    private static int Rollback(List<(string Target, string? Old, string Staged)> swapped)
    {
        var errors = 0;
        for (var i = swapped.Count - 1; i >= 0; i--)
        {
            var (target, old, staged) = swapped[i];
            try
            {
                File.Move(target, staged);
                if (old is not null)
                {
                    File.Move(old, target);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                errors++;
            }
        }

        return errors;
    }
}
