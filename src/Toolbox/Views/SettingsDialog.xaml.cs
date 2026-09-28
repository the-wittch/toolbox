using Microsoft.UI.Xaml.Controls;
using Toolbox.Services;

namespace Toolbox.Views;

public sealed partial class SettingsDialog : ContentDialog
{
    public SettingsDialog()
    {
        InitializeComponent();
        var settings = App.Settings.Current;

        ProgramTitleBox.Text = settings.ProgramTitle;
        RootOuBox.Text = settings.Ldap.RootOu;
        PrefixesBox.Text = string.Join(", ", settings.Search.ComputerNamePrefixes);
        PingTimeoutBox.Value = settings.Ping.TimeoutMs;
        AccessControlToggle.IsOn = settings.AccessControl.Enabled;
        AllowedGroupsBox.Text = string.Join(", ", settings.AccessControl.AllowedGroups);
        UpdateSourceBox.Text = UpdateService.SuggestUpdateSourcePath(settings.Deployment);
        AutoUpdateToggle.IsOn = settings.Deployment.AutoUpdateOnClose;
        PathHint.Text =
            $"Install defaults:\n{App.Settings.AdjacentSettingsPath}\n\n" +
            $"Per-user overlay:\n{App.Settings.UserSettingsPath}\n\n" +
            $"Running from:\n{AppContext.BaseDirectory}";

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
        var settings = App.Settings.Current;
        settings.ProgramTitle = string.IsNullOrWhiteSpace(ProgramTitleBox.Text)
            ? "Toolbox"
            : ProgramTitleBox.Text.Trim();
        settings.Ldap.RootOu = RootOuBox.Text?.Trim() ?? settings.Ldap.RootOu;
        settings.Search.ComputerNamePrefixes = SplitCsv(PrefixesBox.Text);
        settings.Ping.TimeoutMs = (int)Math.Clamp(PingTimeoutBox.Value, 500, 30000);
        settings.AccessControl.Enabled = AccessControlToggle.IsOn;
        settings.AccessControl.AllowedGroups = SplitCsv(AllowedGroupsBox.Text);
        settings.Deployment.UpdateSourcePath = UpdateSourceBox.Text?.Trim() ?? string.Empty;
        settings.Deployment.AutoUpdateOnClose = AutoUpdateToggle.IsOn;

        if (ThemeBox.SelectedItem is ComboBoxItem { Tag: string themeId })
        {
            settings.Theme = ThemeService.Normalize(themeId);
        }

        try
        {
            App.Settings.Save(settings);
            PathHint.Text = $"Saved to:\n{App.Settings.ActiveSettingsPath}";
        }
        catch (Exception ex)
        {
            args.Cancel = true;
            PathHint.Text = $"Save failed: {ex.Message}";
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
