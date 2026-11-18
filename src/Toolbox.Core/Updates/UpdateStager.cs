using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Toolbox.IO;

namespace Toolbox.Updates;

public sealed class UpdateIntegrityException : Exception
{
    public UpdateIntegrityException(string message)
        : base(message)
    {
    }
}

public sealed class StagedMarker
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName("files")]
    public List<string> Files { get; set; } = new();

    [JsonPropertyName("stagedUtc")]
    public DateTimeOffset StagedUtc { get; set; }
}

public sealed record StagedUpdate(Version Version, int ChangedFiles, int UnchangedFiles, long BytesCopied);

public static class UpdateStager
{
    private const int BufferSize = 128 * 1024;

    public static async Task<StagedUpdate> StageAsync(
        string sourceDirectory,
        string installDirectory,
        ReleaseManifest manifest,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (UpdateApplier.IsInterrupted(installDirectory))
        {
            throw new InvalidOperationException("A previous update did not finish; it will be completed when Toolbox closes.");
        }

        var workDirectory = UpdateLayout.WorkDirectory(installDirectory);
        DeleteDirectory(workDirectory);
        var staging = UpdateLayout.StagingDirectory(installDirectory);
        Directory.CreateDirectory(staging);

        var total = Math.Max(1, manifest.Files.Sum(f => f.Size));
        long processed = 0;
        long copied = 0;
        var changed = new List<string>();
        var unchanged = 0;

        try
        {
            foreach (var file in manifest.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var installed = ManifestPaths.Resolve(installDirectory, file.Path);
                if (await MatchesAsync(installed, file, cancellationToken).ConfigureAwait(false))
                {
                    unchanged++;
                }
                else
                {
                    var source = ManifestPaths.Resolve(sourceDirectory, file.Path);
                    var target = ManifestPaths.Resolve(staging, file.Path);
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    await CopyVerifiedAsync(source, target, file, cancellationToken).ConfigureAwait(false);
                    changed.Add(file.Path);
                    copied += file.Size;
                }

                processed += file.Size;
                progress?.Report((double)processed / total);
            }

            var marker = new StagedMarker
            {
                Version = VersionParsing.ToDisplay(manifest.ParsedVersion),
                Files = changed,
                StagedUtc = DateTimeOffset.UtcNow
            };
            AtomicFile.WriteAllText(UpdateLayout.MarkerPath(installDirectory), JsonSerializer.Serialize(marker));
        }
        catch
        {
            DeleteDirectory(workDirectory);
            throw;
        }

        return new StagedUpdate(manifest.ParsedVersion, changed.Count, unchanged, copied);
    }

    public static async Task<bool> MatchesAsync(string path, ManifestFile expected, CancellationToken cancellationToken)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length != expected.Size)
        {
            return false;
        }

        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, BufferSize, useAsync: true);
            var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
            return string.Equals(Convert.ToHexString(hash), expected.Sha256, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static async Task CopyVerifiedAsync(string source, string target, ManifestFile expected, CancellationToken cancellationToken)
    {
        if (!File.Exists(source))
        {
            throw new UpdateIntegrityException($"'{expected.Path}' is listed in the release manifest but missing from the release folder.");
        }

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[BufferSize];
        long written = 0;

        await using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan))
        await using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, BufferSize, useAsync: true))
        {
            int read;
            while ((read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                written += read;
                if (written > expected.Size)
                {
                    break;
                }

                hash.AppendData(buffer, 0, read);
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            }

            await output.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        var actual = Convert.ToHexString(hash.GetHashAndReset());
        if (written != expected.Size || !string.Equals(actual, expected.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            AtomicFile.TryDelete(target);
            throw new UpdateIntegrityException($"'{expected.Path}' failed the integrity check (size or SHA-256 mismatch). The release folder may be incomplete or modified.");
        }
    }

    internal static void DeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
