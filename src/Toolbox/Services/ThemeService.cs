using Microsoft.UI.Xaml;

namespace Toolbox.Services;

public sealed class ThemeService
{
    private readonly SettingsService _settings;

    public ThemeService(SettingsService settings)
    {
        _settings = settings;
    }

    public string CurrentThemeSetting => _settings.Current.Theme;

    public void Apply(string themeName, Window? window = null)
    {
        var normalized = Normalize(themeName);
        _settings.Current.Theme = normalized;

        ElementTheme elementTheme = normalized switch
        {
            "Light" => ElementTheme.Light,
            "Dark" => ElementTheme.Dark,
            _ => ElementTheme.Default
        };

        if (window?.Content is FrameworkElement frameworkElement)
        {
            frameworkElement.RequestedTheme = elementTheme;
        }
    }

    /// <summary>
    /// Application.RequestedTheme can only be set before any Window is shown.
    /// Call once at launch so app chrome matches a forced Light/Dark preference.
    /// </summary>
    public void ApplyApplicationThemeAtStartup()
    {
        if (Application.Current is null)
        {
            return;
        }

        var normalized = Normalize(_settings.Current.Theme);
        Application.Current.RequestedTheme = normalized switch
        {
            "Light" => ApplicationTheme.Light,
            "Dark" => ApplicationTheme.Dark,
            _ => Application.Current.RequestedTheme
        };
    }

    public IReadOnlyList<(string Id, string Label)> GetThemeChoices()
    {
        var labels = _settings.Current.Labels;
        return new List<(string, string)>
        {
            ("System", labels.ThemeSystem),
            ("Light", labels.ThemeLight),
            ("Dark", labels.ThemeDark)
        };
    }

    public static string Normalize(string? themeName)
    {
        return themeName?.Trim().ToLowerInvariant() switch
        {
            "light" => "Light",
            "dark" => "Dark",
            _ => "System"
        };
    }
}
