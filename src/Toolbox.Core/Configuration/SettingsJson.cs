using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Toolbox.Models;

namespace Toolbox.Configuration;

public static class SettingsJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly JsonNodeOptions NodeOptions = new() { PropertyNameCaseInsensitive = true };

    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static JsonObject ParseObject(string json)
    {
        var node = JsonNode.Parse(json, NodeOptions, DocumentOptions);
        return node as JsonObject
               ?? throw new JsonException("The settings file must contain a JSON object.");
    }

    public static AppSettings Deserialize(JsonObject node)
    {
        var settings = node.Deserialize<AppSettings>(Options) ?? new AppSettings();
        SettingsNormalizer.FillNulls(settings);
        return settings;
    }

    public static JsonObject ToCanonicalNode(AppSettings settings)
    {
        var node = JsonSerializer.SerializeToNode(settings, Options) as JsonObject;
        return node ?? new JsonObject();
    }

    public static AppSettings Clone(AppSettings settings) => Deserialize(ToCanonicalNode(settings));

    public static string Serialize(JsonNode node) => node.ToJsonString(Options);

    public static JsonObject Merge(JsonObject baseline, JsonObject overlay)
    {
        var result = new JsonObject();
        foreach (var (name, baseValue) in baseline)
        {
            if (!TryGetCaseInsensitive(overlay, name, out var overValue))
            {
                result[name] = baseValue?.DeepClone();
                continue;
            }

            if (baseValue is JsonObject baseObject && overValue is JsonObject overObject)
            {
                result[name] = Merge(baseObject, overObject);
            }
            else if (IsEmpty(overValue))
            {
                result[name] = baseValue?.DeepClone();
            }
            else
            {
                result[name] = overValue!.DeepClone();
            }
        }

        return result;
    }

    public static JsonObject Diff(JsonObject baseline, JsonObject current)
    {
        var result = new JsonObject();
        foreach (var (name, currentValue) in current)
        {
            baseline.TryGetPropertyValue(name, out var baseValue);

            if (currentValue is JsonObject currentObject && baseValue is JsonObject baseObject)
            {
                var nested = Diff(baseObject, currentObject);
                if (nested.Count > 0)
                {
                    result[name] = nested;
                }

                continue;
            }

            if (!JsonNode.DeepEquals(baseValue, currentValue))
            {
                result[name] = currentValue?.DeepClone();
            }
        }

        return result;
    }

    private static bool TryGetCaseInsensitive(JsonObject obj, string name, out JsonNode? value)
    {
        if (obj.TryGetPropertyValue(name, out value))
        {
            return true;
        }

        foreach (var (key, candidate) in obj)
        {
            if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase))
            {
                value = candidate;
                return true;
            }
        }

        value = null;
        return false;
    }

    private static bool IsEmpty(JsonNode? value) => value switch
    {
        null => true,
        JsonArray array => array.Count == 0,
        JsonValue scalar when scalar.GetValueKind() == JsonValueKind.String =>
            string.IsNullOrWhiteSpace(scalar.GetValue<string>()),
        _ => false
    };
}
