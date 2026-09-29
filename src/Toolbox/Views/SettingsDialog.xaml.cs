using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Toolbox.Models;
using Toolbox.Services;

namespace Toolbox.Views;

public sealed partial class SettingsDialog : ContentDialog
{

    public bool DefaultsRestored { get; private set; }

    private readonly List<StreamingEndpoint> _streamingEndpoints = new();
    private bool _suppressEndpointSelection;
    private bool _suppressEndpointEdit;

    public SettingsDialog()
    {
        InitializeComponent();
        LoadFieldsFromSettings();
    }

    private void LoadFieldsFromSettings()
    {
        var settings = App.Settings.Current;
        settings.Streaming ??= new StreamingSettings();
        settings.Streaming.Endpoints ??= new List<StreamingEndpoint>();

        ProgramTitleBox.Text = settings.ProgramTitle;
        RootOuBox.Text = settings.Ldap.RootOu;
        PrefixesBox.Text = string.Join(", ", settings.Search.ComputerNamePrefixes);
        PingTimeoutBox.Value = settings.Ping.TimeoutMs;
        AccessControlToggle.IsOn = settings.AccessControl.Enabled;
        AllowedGroupsBox.Text = string.Join(", ", settings.AccessControl.AllowedGroups);
        UpdateSourceBox.Text = UpdateService.SuggestUpdateSourcePath(settings.Deployment);
        AutoUpdateToggle.IsOn = settings.Deployment.AutoUpdateOnClose;

        StreamingEnabledToggle.IsOn = settings.Streaming.Enabled;
        StreamingConnectToolBox.Text = string.IsNullOrWhiteSpace(settings.Streaming.ConnectRemoteToolId)
            ? "sccm"
            : settings.Streaming.ConnectRemoteToolId;
        StreamingPasswordUrlBox.Text = settings.Streaming.ChangePasswordUrl;

        _streamingEndpoints.Clear();
        foreach (var endpoint in settings.Streaming.Endpoints)
        {
            _streamingEndpoints.Add(CloneEndpoint(endpoint));
        }

        RefreshStreamingEndpointsList(selectIndex: _streamingEndpoints.Count > 0 ? 0 : -1);

        PathHint.Text =
            $"Install defaults:\n{App.Settings.AdjacentSettingsPath}\n\n" +
            $"Per-user overlay:\n{App.Settings.UserSettingsPath}\n\n" +
            $"Running from:\n{AppContext.BaseDirectory}";

        ThemeBox.Items.Clear();
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

        ApplyEndpointFieldsToSelected();
        LoadSelectedEndpointFields();
    }

    private void StreamingAddEndpoint_Click(object sender, RoutedEventArgs e)
    {
        ApplyEndpointFieldsToSelected();
        var endpoint = new StreamingEndpoint
        {
            Id = $"stream-{_streamingEndpoints.Count + 1}",
            Label = $"Stream {_streamingEndpoints.Count + 1}"
        };
        _streamingEndpoints.Add(endpoint);
        RefreshStreamingEndpointsList(selectIndex: _streamingEndpoints.Count - 1);
    }

    private void StreamingRemoveEndpoint_Click(object sender, RoutedEventArgs e)
    {
        var index = StreamingEndpointsList.SelectedIndex;
        if (index < 0 || index >= _streamingEndpoints.Count)
        {
            return;
        }

        _streamingEndpoints.RemoveAt(index);
        var next = Math.Min(index, _streamingEndpoints.Count - 1);
        RefreshStreamingEndpointsList(selectIndex: next);
    }

    private void ApplyEndpointFieldsToSelected()
    {
        if (_suppressEndpointEdit || StreamingEndpointsList.SelectedItem is not StreamingEndpoint endpoint)
        {
            return;
        }

        endpoint.Id = StreamingEndpointIdBox.Text?.Trim() ?? string.Empty;
        endpoint.Label = StreamingEndpointLabelBox.Text?.Trim() ?? string.Empty;
        endpoint.ComputerName = StreamingEndpointComputerBox.Text?.Trim() ?? string.Empty;
        endpoint.ViewStreamUrl = StreamingEndpointViewUrlBox.Text?.Trim() ?? string.Empty;
        endpoint.ControlAppUrl = StreamingEndpointControlUrlBox.Text?.Trim() ?? string.Empty;

        var index = StreamingEndpointsList.SelectedIndex;
        _suppressEndpointSelection = true;
        StreamingEndpointsList.Items[index] = endpoint;
        StreamingEndpointsList.SelectedIndex = index;
        _suppressEndpointSelection = false;
    }

    private void RestoreDefaultsButton_Click(object sender, RoutedEventArgs e)
    {
        if (!App.Settings.RestoreFromInstallDefaults())
        {
            PathHint.Text =
                $"Could not read install defaults from:\n{App.Settings.AdjacentSettingsPath}\n\n" +
                "Republish/reinstall so that file includes the latest sections (streaming, etc.).";
            return;
        }

        DefaultsRestored = true;
        LoadFieldsFromSettings();
        PathHint.Text =
            $"Restored from install defaults and saved to:\n{App.Settings.ActiveSettingsPath}\n\n" +
            "Click Save if you still want to edit fields, or Cancel to close. Streaming and other install sections are now in your user settings.";
    }

    private void ContentDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        ApplyEndpointFieldsToSelected();

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

        settings.Streaming ??= new StreamingSettings();
        settings.Streaming.Enabled = StreamingEnabledToggle.IsOn;
        settings.Streaming.ConnectRemoteToolId = string.IsNullOrWhiteSpace(StreamingConnectToolBox.Text)
            ? "sccm"
            : StreamingConnectToolBox.Text.Trim();
        settings.Streaming.ChangePasswordUrl = StreamingPasswordUrlBox.Text?.Trim() ?? string.Empty;
        settings.Streaming.Endpoints = _streamingEndpoints
            .Select(CloneEndpoint)
            .Where(e => !string.IsNullOrWhiteSpace(e.Id) || !string.IsNullOrWhiteSpace(e.Label))
            .ToList();

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
            PathHint.Text = $"Save failed: {ex.Message}";
        }
    }

    private static StreamingEndpoint CloneEndpoint(StreamingEndpoint source) => new()
    {
        Id = source.Id,
        Label = source.Label,
        ComputerName = source.ComputerName,
        ViewStreamUrl = source.ViewStreamUrl,
        ControlAppUrl = source.ControlAppUrl
    };

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
