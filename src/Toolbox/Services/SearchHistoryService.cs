using System.Text.Json;

namespace Toolbox.Services;

public sealed class SearchHistoryService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _historyPath;

    public SearchHistoryService()
    {
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Toolbox");
        Directory.CreateDirectory(folder);
        _historyPath = Path.Combine(folder, "search-history.json");
    }

    public string HistoryPath => _historyPath;

    public IReadOnlyList<string> Load(int limit)
    {
        limit = Math.Max(1, limit);
        try
        {
            if (!File.Exists(_historyPath))
            {
                return Array.Empty<string>();
            }

            var json = File.ReadAllText(_historyPath);
            var items = JsonSerializer.Deserialize<List<string>>(json, JsonOptions);
            if (items is null || items.Count == 0)
            {
                return Array.Empty<string>();
            }

            return items
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(limit)
                .ToList();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    public void Save(IEnumerable<string> queries, int limit)
    {
        limit = Math.Max(1, limit);
        try
        {
            var items = queries
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(limit)
                .ToList();

            var directory = Path.GetDirectoryName(_historyPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(items, JsonOptions);
            var temp = _historyPath + ".tmp";
            File.WriteAllText(temp, json);
            File.Copy(temp, _historyPath, overwrite: true);
            try { File.Delete(temp); } catch {  }
        }
        catch
        {

        }
    }
}
