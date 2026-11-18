using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Toolbox.Configuration;
using Toolbox.Diagnostics;
using Toolbox.Services;

namespace Toolbox.Views;

public sealed partial class FirstRunDialog : ContentDialog
{
    public FirstRunDialog()
    {
        InitializeComponent();
        var settings = App.Settings.Current;

        ProgramTitleBox.Text = settings.ProgramTitle;
        var rootOu = settings.Ldap.RootOu?.Trim() ?? string.Empty;
        RootOuBox.PlaceholderText = "OU=Computers,DC=contoso,DC=com";
        RootOuBox.Text = PresetQuery.LooksLikePlaceholderOu(rootOu) ? string.Empty : rootOu;
        PrefixesBox.Text = string.Join(", ", settings.Search.ComputerNamePrefixes);

        UpdateSourceBox.Text = settings.Deployment.UpdateSourcePath;
        AutoUpdateToggle.IsOn = settings.Deployment.AutoUpdateOnClose;
        var policyManaged = !string.IsNullOrWhiteSpace(App.Settings.Policy.UpdateSourcePath);
        UpdateSourceBox.IsEnabled = !policyManaged;
        AutoUpdateToggle.IsEnabled = App.Settings.Policy.AutoUpdate is null;

        PathHint.Text =
            $"Install defaults:\n{App.Settings.InstallSettingsPath}\n\nYour settings:\n{App.Settings.UserSettingsPath}";

        foreach (var (id, label) in App.Theme.GetThemeChoices())
        {
            ThemeBox.Items.Add(new ComboBoxItem { Content = label, Tag = id });
        }

        var current = ThemeService.Normalize(settings.Theme);
        ThemeBox.SelectedItem = ThemeBox.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(i => string.Equals(i.Tag?.ToString(), current, StringComparison.OrdinalIgnoreCase))
            ?? ThemeBox.Items.OfType<ComboBoxItem>().FirstOrDefault();
    }

    private void ContentDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        ErrorText.Visibility = Visibility.Collapsed;

        var rootOu = RootOuBox.Text?.Trim() ?? string.Empty;
        if (rootOu.Length == 0 || PresetQuery.LooksLikePlaceholderOu(rootOu))
        {
            ShowError(args, "Enter your LDAP root OU (not the example.com placeholder).");
            return;
        }

        var settings = SettingsJson.Clone(App.Settings.Current);
        settings.ProgramTitle = string.IsNullOrWhiteSpace(ProgramTitleBox.Text) ? "Toolbox" : ProgramTitleBox.Text.Trim();
        settings.Ldap.RootOu = rootOu;
        settings.Search.ComputerNamePrefixes = DialogText.SplitList(PrefixesBox.Text);
        settings.Deployment.UpdateSourcePath = UpdateSourceBox.Text?.Trim() ?? string.Empty;
        settings.Deployment.AutoUpdateOnClose = AutoUpdateToggle.IsOn;
        if (ThemeBox.SelectedItem is ComboBoxItem { Tag: string themeId })
        {
            settings.Theme = ThemeService.Normalize(themeId);
        }

        var errors = DialogText.ErrorsFor(settings, "ldap.rootOu", "deployment.updateSourcePath");
        if (errors.Count > 0)
        {
            ShowError(args, DialogText.Describe(errors));
            return;
        }

        try
        {
            App.Settings.SaveUserSettings(settings, markSetupComplete: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowError(args, "Could not save settings: " + ErrorTranslator.Describe(ex));
        }
    }

    private void ShowError(ContentDialogButtonClickEventArgs args, string message)
    {
        args.Cancel = true;
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }
}
