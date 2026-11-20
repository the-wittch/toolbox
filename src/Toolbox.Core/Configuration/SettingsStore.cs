using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Toolbox.IO;
using Toolbox.Models;

namespace Toolbox.Configuration;

public sealed class SettingsLoadException : Exception
{
    public SettingsLoadException(string path, string message, Exception? inner = null)
        : base(message, inner)
    {
        FilePath = path;
    }

    public string FilePath { get; }
}

public sealed class SettingsLoadResult
{
    public required AppSettings Settings { get; init; }
    public required AppSettings InstallDefaults { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}

public sealed class SettingsStore
{
    public SettingsStore(string installSettingsPath, string userSettingsPath)
    {
        InstallSettingsPath = installSettingsPath;
        UserSettingsPath = userSettingsPath;
    }

    public string InstallSettingsPath { get; }

    public string UserSettingsPath { get; }

    public AppSettings InstallDefaults { get; private set; } = new();

    public SettingsLoadResult Load()
    {
        var warnings = new List<string>();
        var baseline = LoadInstallSettings(warnings);
        InstallDefaults = baseline;

        var effective = SettingsJson.Clone(baseline);
        var overlay = LoadUserOverlay(warnings);
        if (overlay is not null)
        {
            try
            {
                var merged = SettingsJson.Merge(SettingsJson.ToCanonicalNode(baseline), overlay);
                effective = SettingsJson.Deserialize(merged);
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
            {
                QuarantineUserOverlay(warnings, ex.Message);
            }
        }

        SettingsNormalizer.CopyAdminOnlySettings(baseline, effective);
        return new SettingsLoadResult
        {
            Settings = effective,
            InstallDefaults = SettingsJson.Clone(baseline),
            Warnings = warnings
        };
    }

    public void SaveUser(AppSettings effective)
    {
        var diff = SettingsJson.Diff(
            SettingsJson.ToCanonicalNode(InstallDefaults),
            SettingsJson.ToCanonicalNode(effective));
        RemoveAdminOnly(diff);
        AtomicFile.WriteAllText(UserSettingsPath, SettingsJson.Serialize(diff));
    }

    public void ResetUser()
    {
        if (!File.Exists(UserSettingsPath))
        {
            return;
        }

        var backup = UserSettingsPath + ".bak";
        File.Move(UserSettingsPath, backup, overwrite: true);
    }

    private AppSettings LoadInstallSettings(List<string> warnings)
    {
        if (!File.Exists(InstallSettingsPath))
        {
            warnings.Add($"No install settings found at {InstallSettingsPath}; using built-in defaults.");
            var defaults = new AppSettings();
            SettingsNormalizer.FillNulls(defaults);
            return defaults;
        }

        string text;
        try
        {
            text = File.ReadAllText(InstallSettingsPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new SettingsLoadException(
                InstallSettingsPath,
                $"The install settings file could not be read: {ex.Message}",
                ex);
        }

        try
        {
            return SettingsJson.Deserialize(SettingsJson.ParseObject(text));
        }
        catch (JsonException ex)
        {
            var where = ex.LineNumber is { } line
                ? string.Format(CultureInfo.InvariantCulture, " (line {0})", line + 1)
                : string.Empty;
            throw new SettingsLoadException(
                InstallSettingsPath,
                $"The install settings file is not valid JSON{where}: {ex.Message}",
                ex);
        }
    }

    private JsonObject? LoadUserOverlay(List<string> warnings)
    {
        if (!File.Exists(UserSettingsPath))
        {
            return null;
        }

        try
        {
            return SettingsJson.ParseObject(File.ReadAllText(UserSettingsPath));
        }
        catch (JsonException ex)
        {
            QuarantineUserOverlay(warnings, ex.Message);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            warnings.Add($"Your personal settings could not be read and were skipped: {ex.Message}");
            return null;
        }
    }

    private void QuarantineUserOverlay(List<string> warnings, string reason)
    {
        var stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
        var target = UserSettingsPath + ".corrupt-" + stamp;
        try
        {
            File.Move(UserSettingsPath, target, overwrite: true);
            warnings.Add($"Your personal settings were invalid and have been moved to {target}. Install defaults are in use. ({reason})");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            warnings.Add($"Your personal settings are invalid and were ignored ({reason}).");
        }
    }

    private static void RemoveAdminOnly(JsonObject diff)
    {
        foreach (var path in SettingsNormalizer.AdminOnlyPaths)
        {
            var parts = path.Split('.');
            JsonObject? parent = diff;
            for (var i = 0; i < parts.Length - 1 && parent is not null; i++)
            {
                parent = parent[parts[i]] as JsonObject;
            }

            parent?.Remove(parts[^1]);
        }

        if (diff["deployment"] is JsonObject { Count: 0 })
        {
            diff.Remove("deployment");
        }
    }
}
