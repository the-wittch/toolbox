using System.Text.Json;
using Toolbox.IO;

namespace Toolbox.Configuration;

public sealed class SearchHistoryStore
{
    public const int MaxEntryLength = 256;

    public SearchHistoryStore(string path)
    {
        HistoryPath = path;
    }

    public string HistoryPath { get; }

    public IReadOnlyList<string> Load(int limit)
    {
        try
        {
            if (!File.Exists(HistoryPath))
            {
                return Array.Empty<string>();
            }

            var items = JsonSerializer.Deserialize<List<string?>>(File.ReadAllText(HistoryPath));
            return Clean(items ?? new List<string?>(), limit);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }
    }

    public void Save(IEnumerable<string> queries, int limit) =>
        AtomicFile.WriteAllText(HistoryPath, JsonSerializer.Serialize(Clean(queries, limit)));

    public void Clear() => AtomicFile.TryDelete(HistoryPath);

    private static List<string> Clean(IEnumerable<string?> items, int limit) =>
        items
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s!.Trim())
            .Where(s => s.Length <= MaxEntryLength)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(Math.Clamp(limit, 1, 500))
            .ToList();
}
