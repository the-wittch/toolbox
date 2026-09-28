using System.Text.Json;
using System.Text.Json.Serialization;
using Toolbox.Models;

namespace Toolbox.Services;

/// <summary>
/// Loads and saves <c>appsettings.json</c> beside the executable so help desk can
/// edit configuration on a file share without rebuilding. If the install folder
/// is read-only, saves fall back to a LocalApplicationData copy that overlays
/// the share defaults.
/// </summary>
public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly string _adjacentSettingsPath;
    private readonly string _fallbackSettingsPath;
    private string _writableSettingsPath;

    public AppSettings Current { get; private set; } = new();

    /// <summary>Path next to Toolbox.exe (preferred, share-editable).</summary>
    public string AdjacentSettingsPath => _adjacentSettingsPath;

    /// <summary>Per-user overlay under LocalApplicationData\Toolbox.</summary>
    public string UserSettingsPath => _fallbackSettingsPath;

    /// <summary>Path actually used for the last successful Save.</summary>
    public string ActiveSettingsPath => _writableSettingsPath;

    /// <summary>True until the user (or org template) marks setup complete.</summary>
    public bool NeedsFirstRunSetup => !Current.SetupCompleted;

    public SettingsService()
    {
        _adjacentSettingsPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var folder = Path.Combine(appData, "Toolbox");
        Directory.CreateDirectory(folder);
        _fallbackSettingsPath = Path.Combine(folder, "appsettings.json");
        _writableSettingsPath = _adjacentSettingsPath;
    }

    public AppSettings Load()
    {
        AppSettings settings;

        if (File.Exists(_adjacentSettingsPath))
        {
            settings = LoadFromFile(_adjacentSettingsPath) ?? new AppSettings();
        }
        else
        {
            settings = new AppSettings();
            TryWrite(_adjacentSettingsPath, settings);
        }

        // Optional per-user overlay when the share copy is locked read-only.
        if (File.Exists(_fallbackSettingsPath) &&
            !PathsEqual(_fallbackSettingsPath, _adjacentSettingsPath))
        {
            var overlay = LoadFromFile(_fallbackSettingsPath);
            if (overlay is not null)
            {
                settings = Merge(settings, overlay);
            }
        }

        Current = settings;
        return Current;
    }

    public void Save(AppSettings settings)
    {
        Current = settings;
        if (TryWrite(_adjacentSettingsPath, settings))
        {
            _writableSettingsPath = _adjacentSettingsPath;
            // If a per-user overlay exists, keep it in sync. Load() merges the overlay
            // on top of the install file — a stale overlay would otherwise undo UI saves
            // (e.g. AutoUpdateOnClose toggled off next to the exe, then flipped back on).
            SyncUserOverlayIfPresent(settings);
            return;
        }

        if (TryWrite(_fallbackSettingsPath, settings))
        {
            _writableSettingsPath = _fallbackSettingsPath;
            return;
        }

        throw new IOException(
            $"Could not write settings to '{_adjacentSettingsPath}' or '{_fallbackSettingsPath}'.");
    }

    public void SaveCurrent() => Save(Current);

    /// <summary>
    /// Saves user-specific settings to LocalApplicationData so a shared install folder
    /// is not overwritten when help desk runs from a file share. Overlay always wins
    /// on the next <see cref="Load"/>.
    /// </summary>
    public void SaveUserSettings(AppSettings settings)
    {
        settings.SetupCompleted = true;
        Current = settings;
        if (TryWrite(_fallbackSettingsPath, settings))
        {
            _writableSettingsPath = _fallbackSettingsPath;
            return;
        }

        // Last resort: install folder (may be shared / read-only).
        Save(settings);
    }

    private void SyncUserOverlayIfPresent(AppSettings settings)
    {
        if (!File.Exists(_fallbackSettingsPath) ||
            PathsEqual(_fallbackSettingsPath, _adjacentSettingsPath))
        {
            return;
        }

        TryWrite(_fallbackSettingsPath, settings);
    }

    public void ResetToDefaults()
    {
        Current = new AppSettings();
        Save(Current);
    }

    public static bool LooksLikePlaceholderOu(string? rootOu)
    {
        if (string.IsNullOrWhiteSpace(rootOu))
        {
            return true;
        }

        return rootOu.Contains("DC=example", StringComparison.OrdinalIgnoreCase)
               || rootOu.Contains("example.com", StringComparison.OrdinalIgnoreCase);
    }

    public static string ResolvePresetQuery(string queryText)
    {
        if (string.IsNullOrEmpty(queryText))
        {
            return queryText;
        }

        const string todayPrefix = "{today:";
        if (queryText.StartsWith(todayPrefix, StringComparison.OrdinalIgnoreCase) &&
            queryText.EndsWith('}'))
        {
            var format = queryText[todayPrefix.Length..^1];
            if (string.IsNullOrWhiteSpace(format))
            {
                format = "M/d/yyyy";
            }

            return DateTime.Now.ToString(format);
        }

        return queryText;
    }

    private static bool PathsEqual(string a, string b) =>
        string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

    private static bool TryWrite(string path, AppSettings settings)
    {
        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(settings, JsonOptions);
            var temp = path + ".tmp";
            File.WriteAllText(temp, json);
            File.Copy(temp, path, overwrite: true);
            File.Delete(temp);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static AppSettings? LoadFromFile(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
    }

    private static AppSettings Merge(AppSettings baseline, AppSettings overlay)
    {
        var baselineJson = JsonSerializer.SerializeToElement(baseline, JsonOptions);
        var overlayJson = JsonSerializer.SerializeToElement(overlay, JsonOptions);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            MergeElements(writer, baselineJson, overlayJson);
        }

        stream.Position = 0;
        return JsonSerializer.Deserialize<AppSettings>(stream, JsonOptions) ?? baseline;
    }

    private static void MergeElements(Utf8JsonWriter writer, JsonElement baseline, JsonElement overlay)
    {
        if (overlay.ValueKind == JsonValueKind.Object && baseline.ValueKind == JsonValueKind.Object)
        {
            writer.WriteStartObject();
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var prop in baseline.EnumerateObject())
            {
                names.Add(prop.Name);
            }

            foreach (var prop in overlay.EnumerateObject())
            {
                names.Add(prop.Name);
            }

            foreach (var name in names)
            {
                writer.WritePropertyName(name);
                var hasBase = baseline.TryGetProperty(name, out var baseProp);
                var hasOver = overlay.TryGetProperty(name, out var overProp);

                if (hasBase && hasOver)
                {
                    if (overProp.ValueKind is JsonValueKind.Object)
                    {
                        MergeElements(writer, baseProp, overProp);
                    }
                    else
                    {
                        overProp.WriteTo(writer);
                    }
                }
                else if (hasOver)
                {
                    overProp.WriteTo(writer);
                }
                else
                {
                    baseProp.WriteTo(writer);
                }
            }

            writer.WriteEndObject();
            return;
        }

        if (overlay.ValueKind != JsonValueKind.Undefined && overlay.ValueKind != JsonValueKind.Null)
        {
            overlay.WriteTo(writer);
        }
        else
        {
            baseline.WriteTo(writer);
        }
    }
}
