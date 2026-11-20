using Toolbox.Configuration;
using Toolbox.Models;

namespace Toolbox.Views;

internal static class DialogText
{
    public static List<string> SplitList(string? text) =>
        string.IsNullOrWhiteSpace(text)
            ? new List<string>()
            : text.Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

    public static IReadOnlyList<ValidationIssue> ErrorsFor(AppSettings settings, params string[] pathPrefixes) =>
        SettingsValidator.Validate(settings)
            .Where(i => i.Severity == IssueSeverity.Error &&
                        pathPrefixes.Any(p => i.Path.StartsWith(p, StringComparison.Ordinal)))
            .ToList();

    public static string Describe(IEnumerable<ValidationIssue> issues) =>
        string.Join(Environment.NewLine, issues.Select(i => $"{i.Path}: {i.Message}"));
}
