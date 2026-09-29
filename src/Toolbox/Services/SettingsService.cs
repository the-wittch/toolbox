using System.Text.Json;
using System.Text.Json.Serialization;
using Toolbox.Models;

namespace Toolbox.Services;

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

    public string AdjacentSettingsPath => _adjacentSettingsPath;

    public string UserSettingsPath => _fallbackSettingsPath;

    public string ActiveSettingsPath => _writableSettingsPath;

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
        AppSettings baseline;

        if (File.Exists(_adjacentSettingsPath))
        {
            baseline = LoadFromFile(_adjacentSettingsPath) ?? new AppSettings();
        }
        else
        {
            baseline = new AppSettings();
            TryWrite(_adjacentSettingsPath, baseline);
        }

        AppSettings settings = baseline;

        if (File.Exists(_fallbackSettingsPath) &&
            !PathsEqual(_fallbackSettingsPath, _adjacentSettingsPath))
        {
            var overlay = LoadFromFile(_fallbackSettingsPath);
            if (overlay is not null)
            {
                settings = Merge(baseline, overlay);
            }
        }

        if (FillMissingFromBaseline(settings, baseline) &&
            File.Exists(_fallbackSettingsPath) &&
            !PathsEqual(_fallbackSettingsPath, _adjacentSettingsPath))
        {
            TryWrite(_fallbackSettingsPath, settings);
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

    public void SaveUserSettings(AppSettings settings)
    {
        settings.SetupCompleted = true;
        Current = settings;
        if (TryWrite(_fallbackSettingsPath, settings))
        {
            _writableSettingsPath = _fallbackSettingsPath;
            return;
        }

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

    public bool RestoreFromInstallDefaults()
    {
        var install = LoadFromFile(_adjacentSettingsPath);
        if (install is null)
        {
            return false;
        }

        install.SetupCompleted = true;
        install.Streaming ??= new StreamingSettings();
        install.Streaming.Endpoints ??= new List<StreamingEndpoint>();

        Current = install;
        if (TryWrite(_fallbackSettingsPath, Current))
        {
            _writableSettingsPath = _fallbackSettingsPath;
            return true;
        }

        if (TryWrite(_adjacentSettingsPath, Current))
        {
            _writableSettingsPath = _adjacentSettingsPath;
            return true;
        }

        return false;
    }

    public void ResetToDefaults()
    {
        _ = RestoreFromInstallDefaults();
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
                    if (overProp.ValueKind is JsonValueKind.Object && baseProp.ValueKind is JsonValueKind.Object)
                    {
                        MergeElements(writer, baseProp, overProp);
                    }
                    else if (IsEmptyOverlayValue(overProp) && !IsEmptyOverlayValue(baseProp))
                    {

                        baseProp.WriteTo(writer);
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
            if (IsEmptyOverlayValue(overlay) && !IsEmptyOverlayValue(baseline))
            {
                baseline.WriteTo(writer);
            }
            else
            {
                overlay.WriteTo(writer);
            }
        }
        else
        {
            baseline.WriteTo(writer);
        }
    }

    private static bool IsEmptyOverlayValue(JsonElement value) =>
        value.ValueKind switch
        {
            JsonValueKind.Null => true,
            JsonValueKind.String => string.IsNullOrWhiteSpace(value.GetString()),
            JsonValueKind.Array => value.GetArrayLength() == 0,
            _ => false
        };

    private static bool FillMissingFromBaseline(AppSettings settings, AppSettings baseline)
    {
        var changed = false;

        var streaming = settings.Streaming ??= new StreamingSettings();
        var baselineStreaming = baseline.Streaming ??= new StreamingSettings();
        streaming.Endpoints ??= new List<StreamingEndpoint>();
        baselineStreaming.Endpoints ??= new List<StreamingEndpoint>();

        if (streaming.Endpoints.Count == 0 && baselineStreaming.Endpoints.Count > 0)
        {
            streaming.Endpoints = baselineStreaming.Endpoints
                .Select(CloneEndpoint)
                .ToList();
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(streaming.ChangePasswordUrl) &&
            !string.IsNullOrWhiteSpace(baselineStreaming.ChangePasswordUrl))
        {
            streaming.ChangePasswordUrl = baselineStreaming.ChangePasswordUrl;
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(streaming.ConnectRemoteToolId) &&
            !string.IsNullOrWhiteSpace(baselineStreaming.ConnectRemoteToolId))
        {
            streaming.ConnectRemoteToolId = baselineStreaming.ConnectRemoteToolId;
            changed = true;
        }

        if (changed && streaming.Endpoints.Count > 0 && !streaming.Enabled)
        {
            streaming.Enabled = baselineStreaming.Enabled;
        }

        return changed;
    }

    private static StreamingEndpoint CloneEndpoint(StreamingEndpoint source) => new()
    {
        Id = source.Id,
        Label = source.Label,
        ComputerName = source.ComputerName,
        ViewStreamUrl = source.ViewStreamUrl,
        ControlAppUrl = source.ControlAppUrl
    };
}
