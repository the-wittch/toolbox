using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Toolbox.Configuration;
using Toolbox.Diagnostics;
using Toolbox.Models;
using Toolbox.Services;

namespace Toolbox.Views;

public sealed partial class SettingsDialog : ContentDialog
{
    private readonly List<StreamingEndpoint> _streamingEndpoints = new();
    private bool _suppressEndpointSelection;
    private bool _suppressEndpointEdit;

    public SettingsDialog()
    {
        InitializeComponent();
        LoadFieldsFromSettings();
    }

    public bool DefaultsRestored { get; private set; }

    private void LoadFieldsFromSettings()
    {
        var settings = App.Settings.Current;

        ProgramTitleBox.Text = settings.ProgramTitle;
        RootOuBox.Text = settings.Ldap.RootOu;
        PrefixesBox.Text = string.Join(", ", settings.Search.ComputerNamePrefixes);
        PingTimeoutBox.Value = settings.Ping.TimeoutMs;

        var access = settings.AccessControl;
        AccessControlText.Text = access.Enabled
            ? $"Only members of {string.Join(", ", access.AllowedGroups)} can open Toolbox. Managed by your administrator."
            : "Anyone can open Toolbox. Your administrator can restrict it in the install settings or by group policy.";

        UpdateSourceBox.Text = settings.Deployment.UpdateSourcePath;
        AutoUpdateToggle.IsOn = settings.Deployment.AutoUpdateOnClose;
        UpdateSourceBox.IsEnabled = string.IsNullOrWhiteSpace(App.Settings.Policy.UpdateSourcePath);
        AutoUpdateToggle.IsEnabled = App.Settings.Policy.AutoUpdate is null;

        StreamingEnabledToggle.IsOn = settings.Streaming.Enabled;
        StreamingConnectToolBox.Text = string.IsNullOrWhiteSpace(settings.Streaming.ConnectRemoteToolId)
            ? "sccm"
            : settings.Streaming.ConnectRemoteToolId;
        StreamingAccountUrlBox.Text = settings.Streaming.AccountPageUrl;

        _streamingEndpoints.Clear();
        _streamingEndpoints.AddRange(settings.Streaming.Endpoints.Select(e => e.Clone()));
        RefreshStreamingEndpointsList(_streamingEndpoints.Count > 0 ? 0 : -1);

        PathHint.Text =
            $"Install defaults:\n{App.Settings.InstallSettingsPath}\n\n" +
            $"Your settings:\n{App.Settings.UserSettingsPath}\n\n" +
            $"Running from:\n{AppContext.BaseDirectory}\n\nVersion {UpdateService.CurrentVersion}";

        ThemeBox.Items.Clear();
        foreach (var (id, label) in App.Theme.GetThemeChoices())
        {
            ThemeBox.Items.Add(new ComboBoxItem { Content = label, Tag = id });
        }

        var current = ThemeService.Normalize(settings.Theme);
        ThemeBox.SelectedItem = ThemeBox.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(i => string.Equals(i.Tag?.ToString(), current, StringComparison.OrdinalIgnoreCase))
            ?? ThemeBox.Items.OfType<ComboBoxItem>().FirstOrDefault();
    }

    private void RefreshStreamingEndpointsList(int selectIndex)
    {
        _suppressEndpointSelection = true;
        StreamingEndpointsList.Items.Clear();
        foreach (var endpoint in _streamingEndpoints)
        {
            StreamingEndpointsList.Items.Add(endpoint);
        }

        if (selectIndex >= 0 && selectIndex < StreamingEndpointsList.Items.Count)
        {
            StreamingEndpointsList.SelectedIndex = selectIndex;
            LoadSelectedEndpointFields();
        }
        else
        {
            StreamingEndpointsList.SelectedItem = null;
            ClearEndpointFields();
        }

        _suppressEndpointSelection = false;
    }

    private void LoadSelectedEndpointFields()
    {
        _suppressEndpointEdit = true;
        if (StreamingEndpointsList.SelectedItem is StreamingEndpoint endpoint)
        {
            StreamingEndpointIdBox.Text = endpoint.Id;
            StreamingEndpointLabelBox.Text = endpoint.Label;
            StreamingEndpointComputerBox.Text = endpoint.ComputerName;
            StreamingEndpointViewUrlBox.Text = endpoint.ViewStreamUrl;
            StreamingEndpointControlUrlBox.Text = endpoint.ControlAppUrl;
        }
        else
        {
            ClearEndpointFields();
        }

        _suppressEndpointEdit = false;
    }

    private void ClearEndpointFields()
    {
        StreamingEndpointIdBox.Text = string.Empty;
        StreamingEndpointLabelBox.Text = string.Empty;
        StreamingEndpointComputerBox.Text = string.Empty;
        StreamingEndpointViewUrlBox.Text = string.Empty;
        StreamingEndpointControlUrlBox.Text = string.Empty;
    }

    private void StreamingEndpointsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEndpointSelection)
        {
            return;
        }

        if (e.RemovedItems.FirstOrDefault() is StreamingEndpoint previous)
        {
            ApplyEndpointFields(previous);
        }

        LoadSelectedEndpointFields();
    }

    private void StreamingAddEndpoint_Click(object sender, RoutedEventArgs e)
    {
        ApplyEndpointFieldsToSelected();
        var number = _streamingEndpoints.Count + 1;
        _streamingEndpoints.Add(new StreamingEndpoint { Id = $"stream-{number}", Label = $"Stream {number}" });
        RefreshStreamingEndpointsList(_streamingEndpoints.Count - 1);
    }

    private void StreamingRemoveEndpoint_Click(object sender, RoutedEventArgs e)
    {
        var index = StreamingEndpointsList.SelectedIndex;
        if (index < 0 || index >= _streamingEndpoints.Count)
        {
            return;
        }

        _streamingEndpoints.RemoveAt(index);
        RefreshStreamingEndpointsList(Math.Min(index, _streamingEndpoints.Count - 1));
    }

    private void ApplyEndpointFieldsToSelected()
    {
        if (StreamingEndpointsList.SelectedItem is StreamingEndpoint endpoint)
        {
            ApplyEndpointFields(endpoint);
        }
    }

    private void ApplyEndpointFields(StreamingEndpoint endpoint)
    {
        if (_suppressEndpointEdit)
        {
            return;
        }

        endpoint.Id = StreamingEndpointIdBox.Text?.Trim() ?? string.Empty;
        endpoint.Label = StreamingEndpointLabelBox.Text?.Trim() ?? string.Empty;
        endpoint.ComputerName = StreamingEndpointComputerBox.Text?.Trim() ?? string.Empty;
        endpoint.ViewStreamUrl = StreamingEndpointViewUrlBox.Text?.Trim() ?? string.Empty;
        endpoint.ControlAppUrl = StreamingEndpointControlUrlBox.Text?.Trim() ?? string.Empty;
    }

    private void RestoreDefaultsButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            App.Settings.ResetUserSettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowError("Could not reset your settings: " + ErrorTranslator.Describe(ex));
            return;
        }

        DefaultsRestored = true;
        ErrorText.Visibility = Visibility.Collapsed;
        LoadFieldsFromSettings();
    }

    private void ContentDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        ErrorText.Visibility = Visibility.Collapsed;
        ApplyEndpointFieldsToSelected();

        var settings = SettingsJson.Clone(App.Settings.Current);
        settings.ProgramTitle = string.IsNullOrWhiteSpace(ProgramTitleBox.Text) ? "Toolbox" : ProgramTitleBox.Text.Trim();
        settings.Ldap.RootOu = RootOuBox.Text?.Trim() ?? settings.Ldap.RootOu;
        settings.Search.ComputerNamePrefixes = DialogText.SplitList(PrefixesBox.Text);
        if (!double.IsNaN(PingTimeoutBox.Value))
        {
            settings.Ping.TimeoutMs = (int)Math.Clamp(PingTimeoutBox.Value, 500, 30000);
        }

        settings.Deployment.UpdateSourcePath = UpdateSourceBox.Text?.Trim() ?? string.Empty;
        settings.Deployment.AutoUpdateOnClose = AutoUpdateToggle.IsOn;
        settings.Streaming.Enabled = StreamingEnabledToggle.IsOn;
        settings.Streaming.ConnectRemoteToolId = string.IsNullOrWhiteSpace(StreamingConnectToolBox.Text)
            ? "sccm"
            : StreamingConnectToolBox.Text.Trim();
        settings.Streaming.AccountPageUrl = StreamingAccountUrlBox.Text?.Trim() ?? string.Empty;
        settings.Streaming.Endpoints = _streamingEndpoints
            .Select(e => e.Clone())
            .Where(e => !string.IsNullOrWhiteSpace(e.Id) || !string.IsNullOrWhiteSpace(e.Label))
            .ToList();
        if (ThemeBox.SelectedItem is ComboBoxItem { Tag: string themeId })
        {
            settings.Theme = ThemeService.Normalize(themeId);
        }

        var errors = DialogText.ErrorsFor(settings, "ldap.rootOu", "deployment.updateSourcePath", "streaming.");
        if (errors.Count > 0)
        {
            args.Cancel = true;
            ShowError(DialogText.Describe(errors));
            return;
        }

        try
        {
            App.Settings.SaveUserSettings(settings, markSetupComplete: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            args.Cancel = true;
            ShowError("Save failed: " + ErrorTranslator.Describe(ex));
        }
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }
}
