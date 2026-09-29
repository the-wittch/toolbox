using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Toolbox.Services;

namespace Toolbox.Views;

public sealed partial class FirstRunDialog : ContentDialog
{
    public FirstRunDialog()
    {
        InitializeComponent();
        var settings = App.Settings.Current;

        ProgramTitleBox.Text = settings.ProgramTitle;
        ProgramTitleBox.PlaceholderText = string.IsNullOrWhiteSpace(settings.ProgramTitle)
            ? "Toolbox"
            : settings.ProgramTitle;

        var rootOu = settings.Ldap.RootOu?.Trim() ?? string.Empty;
        RootOuBox.PlaceholderText = string.IsNullOrWhiteSpace(rootOu)
            ? "OU=Computers,DC=contoso,DC=com"
            : rootOu;

        RootOuBox.Text = SettingsService.LooksLikePlaceholderOu(rootOu) ? string.Empty : rootOu;

        var prefixes = string.Join(", ", settings.Search.ComputerNamePrefixes);
        PrefixesBox.Text = prefixes;
        PrefixesBox.PlaceholderText = string.IsNullOrWhiteSpace(prefixes) ? "PC-, WS-" : prefixes;

        var updateSource = UpdateService.SuggestUpdateSourcePath(settings.Deployment);
        UpdateSourceBox.Text = updateSource;
        UpdateSourceBox.PlaceholderText = string.IsNullOrWhiteSpace(updateSource)
            ? @"\\fileserver\apps\Toolbox"
            : updateSource;
        AutoUpdateToggle.IsOn = settings.Deployment.AutoUpdateOnClose;

        PathHint.Text =
            $"Install defaults:\n{App.Settings.AdjacentSettingsPath}\n\n" +
            $"Will save your copy to:\n{App.Settings.UserSettingsPath}";

        foreach (var (id, label) in App.Theme.GetThemeChoices())
        {
            ThemeBox.Items.Add(new ComboBoxItem { Content = label, Tag = id });
        }

        var current = ThemeService.Normalize(settings.Theme);
        ThemeBox.SelectedItem = ThemeBox.Items
            .OfType<ComboBoxItem>()
            .FirstOrDefault(i => string.Equals(i.Tag?.ToString(), current, StringComparison.OrdinalIgnoreCase))
            ?? ThemeBox.Items.OfType<ComboBoxItem>().FirstOrDefault();
    }

    private void ContentDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        ErrorText.Visibility = Visibility.Collapsed;

        var rootOu = RootOuBox.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(rootOu) || SettingsService.LooksLikePlaceholderOu(rootOu))
        {
            args.Cancel = true;
            ErrorText.Text = "Enter your real LDAP root OU (not the example.com placeholder).";
            ErrorText.Visibility = Visibility.Visible;
            return;
        }

        var settings = App.Settings.Current;
        settings.ProgramTitle = string.IsNullOrWhiteSpace(ProgramTitleBox.Text)
            ? "Toolbox"
            : ProgramTitleBox.Text.Trim();
        settings.Ldap.RootOu = rootOu;
        settings.Search.ComputerNamePrefixes = SplitCsv(PrefixesBox.Text);
        settings.Deployment.UpdateSourcePath = UpdateSourceBox.Text?.Trim() ?? string.Empty;
        settings.Deployment.AutoUpdateOnClose = AutoUpdateToggle.IsOn;

        if (ThemeBox.SelectedItem is ComboBoxItem { Tag: string themeId })
        {
            settings.Theme = ThemeService.Normalize(themeId);
        }

        try
        {
            App.Settings.SaveUserSettings(settings);
            PathHint.Text = $"Saved to:\n{App.Settings.ActiveSettingsPath}";
        }
        catch (Exception ex)
        {
            args.Cancel = true;
            ErrorText.Text = $"Could not save settings: {ex.Message}";
            ErrorText.Visibility = Visibility.Visible;
        }
    }

    private static List<string> SplitCsv(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new List<string>();
        }

        return text
            .Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
