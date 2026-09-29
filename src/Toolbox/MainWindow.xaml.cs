using System.Collections.ObjectModel;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Toolbox.Models;
using Toolbox.Services;
using Toolbox.Views;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using Windows.System;
using WinRT.Interop;

namespace Toolbox;

public sealed partial class MainWindow : Window
{
    private readonly ObservableCollection<ComputerItem> _results = new();
    private readonly ObservableCollection<string> _history = new();
    private readonly ObservableCollection<StatusLogEntry> _status = new();
    private readonly List<AvailableRemoteTool> _remoteTools = new();
    private CancellationTokenSource? _searchCts;
    private CancellationTokenSource? _detailCts;
    private ComputerDetail? _currentDetail;
    private int _statusCounter;
    private bool _suppressThemeEvent;
    private bool _suppressPresetEvent;

    public MainWindow()
    {
        InitializeComponent();
        ApplyWindowChrome();
        BindCollections();
        ApplyLabelsFromSettings();
        LoadRemoteTools();
        LoadThemeChoices();
        LoadPresets();
        LoadSearchHistory();
        LoadStreamingEndpoints();
        Log(App.Settings.Current.Labels.StatusReady);
        RootGrid.Loaded += MainWindow_Loaded;
        Closed += MainWindow_Closed;
    }

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        App.SearchHistory.Save(_history, Math.Max(1, App.Settings.Current.Search.HistoryLimit));

        _ = App.Updater.TryScheduleUpdateOnClose();
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        RootGrid.Loaded -= MainWindow_Loaded;

        if (App.LocalInstall.ShouldOfferLocalInstall)
        {
            var installDialog = new InstallLocallyDialog
            {
                XamlRoot = RootGrid.XamlRoot
            };
            var installResult = await installDialog.ShowAsync();
            if (installResult == ContentDialogResult.Primary &&
                installDialog.InstallResult?.Succeeded == true)
            {
                Log($"Installed locally to {LocalInstallService.DefaultLocalInstallDirectory}");
                if (App.LocalInstall.TryLaunchLocalInstallAndExit(Close))
                {
                    return;
                }

                Log("Local install finished, but the new copy could not be started automatically.");
            }
        }

        if (!App.Settings.NeedsFirstRunSetup)
        {
            return;
        }

        var dialog = new FirstRunDialog
        {
            XamlRoot = RootGrid.XamlRoot
        };
        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            Title = App.Settings.Current.ProgramTitle;
            TitleText.Text = App.Settings.Current.ProgramTitle;
            ApplyLabelsFromSettings();
            LoadThemeChoices();
            LoadStreamingEndpoints();
            App.Theme.Apply(App.Settings.Current.Theme, this);
            Log($"First-run setup saved to {App.Settings.ActiveSettingsPath}");
        }
    }

    private void ApplyWindowChrome()
    {
        var ui = App.Settings.Current.Ui;
        Title = App.Settings.Current.ProgramTitle;
        TitleText.Text = App.Settings.Current.ProgramTitle;

        var hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);
        appWindow.Resize(new SizeInt32(ui.WindowWidth, ui.WindowHeight));

        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
        if (File.Exists(iconPath))
        {
            appWindow.SetIcon(iconPath);
        }

        if (appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = ui.MinWidth;
            presenter.PreferredMinimumHeight = ui.MinHeight;
        }
    }

    private void BindCollections()
    {
        ResultsList.ItemsSource = _results;
        HistoryList.ItemsSource = _history;
        StatusList.ItemsSource = _status;

        HistoryPanel.Visibility = App.Settings.Current.Ui.ShowSearchHistory
            ? Visibility.Visible
            : Visibility.Collapsed;
        HistoryColumn.Width = App.Settings.Current.Ui.ShowSearchHistory
            ? new GridLength(200)
            : new GridLength(0);
        StatusPanel.Visibility = App.Settings.Current.Ui.ShowStatusLog
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void ApplyLabelsFromSettings()
    {
        var labels = App.Settings.Current.Labels;
        SearchBox.PlaceholderText = labels.SearchPlaceholder;
        SearchButton.Content = labels.SearchButton;
        RemoteButton.Content = labels.RemoteButton;
        ResultsHint.Text = labels.ResultsHint;
        EmptyResultsText.Text = labels.ResultsEmpty;
        HistoryHeader.Text = labels.HistoryHeader;
    }

    private void LoadRemoteTools()
    {
        _remoteTools.Clear();
        _remoteTools.AddRange(App.RemoteTools.GetAvailableTools());
        RemoteToolCombo.ItemsSource = _remoteTools;
        var preferred = _remoteTools.FirstOrDefault(t => t.PreferAsDefault) ?? _remoteTools.FirstOrDefault();
        if (preferred is not null)
        {
            RemoteToolCombo.SelectedItem = preferred;
        }
    }

    private void LoadThemeChoices()
    {
        _suppressThemeEvent = true;
        ThemeCombo.Items.Clear();
        foreach (var (id, label) in App.Theme.GetThemeChoices())
        {
            ThemeCombo.Items.Add(new ComboBoxItem { Content = label, Tag = id });
        }

        var current = ThemeService.Normalize(App.Settings.Current.Theme);
        ThemeCombo.SelectedItem = ThemeCombo.Items
            .OfType<ComboBoxItem>()
            .FirstOrDefault(i => string.Equals(i.Tag?.ToString(), current, StringComparison.OrdinalIgnoreCase))
            ?? ThemeCombo.Items.OfType<ComboBoxItem>().FirstOrDefault();
        _suppressThemeEvent = false;
    }

    private void LoadPresets()
    {
        _suppressPresetEvent = true;
        PresetCombo.Items.Clear();
        foreach (var preset in App.Settings.Current.Search.Presets)
        {
            PresetCombo.Items.Add(new ComboBoxItem { Content = preset.Label, Tag = preset });
        }

        PresetCombo.SelectedIndex = PresetCombo.Items.Count > 0 ? 0 : -1;
        _suppressPresetEvent = false;
    }

    private async void SearchButton_Click(object sender, RoutedEventArgs e) => await RunSearchAsync();

    private async void SearchBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            e.Handled = true;
            await RunSearchAsync();
        }
    }

    private async Task RunSearchAsync()
    {
        var query = SearchBox.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(query))
        {
            Log("Enter a computer name or description fragment to search.");
            SearchBox.Focus(FocusState.Programmatic);
            return;
        }

        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        var token = _searchCts.Token;

        SetSearching(true);
        Log($"Searching for “{query}”…");
        PushHistory(query);

        try
        {
            var found = await App.AdSearcher.SearchAsync(query, token);
            _results.Clear();
            foreach (var item in found)
            {
                _results.Add(item);
            }

            EmptyResultsText.Visibility = _results.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            Log($"Search complete. Found {_results.Count} computer(s).");
            ClearDetail();
        }
        catch (OperationCanceledException)
        {
            Log("Search cancelled.");
        }
        catch (Exception ex)
        {
            Log($"Search failed: {ex.Message}");
            EmptyResultsText.Visibility = Visibility.Visible;
            _results.Clear();
        }
        finally
        {
            SetSearching(false);
        }
    }

    private void SetSearching(bool isSearching)
    {
        SearchProgress.IsActive = isSearching;
        SearchProgress.Visibility = isSearching ? Visibility.Visible : Visibility.Collapsed;
        SearchButton.IsEnabled = !isSearching;
        SearchBox.IsEnabled = !isSearching;
    }

    private void LoadSearchHistory()
    {
        _history.Clear();
        var limit = Math.Max(1, App.Settings.Current.Search.HistoryLimit);
        foreach (var query in App.SearchHistory.Load(limit))
        {
            _history.Add(query);
        }
    }

    private void PushHistory(string query)
    {
        var existing = _history.FirstOrDefault(h => string.Equals(h, query, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            _history.Remove(existing);
        }

        _history.Insert(0, query);
        var limit = Math.Max(1, App.Settings.Current.Search.HistoryLimit);
        while (_history.Count > limit)
        {
            _history.RemoveAt(_history.Count - 1);
        }

        App.SearchHistory.Save(_history, limit);
    }

    private void HistoryList_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (HistoryList.SelectedItem is string query)
        {
            SearchBox.Text = query;
            _ = RunSearchAsync();
        }
    }

    private void PresetCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressPresetEvent)
        {
            return;
        }

        if (PresetCombo.SelectedItem is ComboBoxItem { Tag: SearchPreset preset })
        {
            var text = SettingsService.ResolvePresetQuery(preset.QueryText);
            if (!string.IsNullOrEmpty(text))
            {
                SearchBox.Text = text;
            }
        }
    }

    private void ResultsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {

    }

    private async void ResultsList_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (ResultsList.SelectedItem is ComputerItem item)
        {
            await LoadDetailAsync(item);
        }
    }

    private async Task LoadDetailAsync(ComputerItem item)
    {
        _detailCts?.Cancel();
        _detailCts = new CancellationTokenSource();
        var token = _detailCts.Token;

        DetailProgress.IsActive = true;
        DetailProgress.Visibility = Visibility.Visible;
        DetailName.Text = item.Name;
        DetailOs.Text = App.Settings.Current.Labels.LoadingDetail;
        DetailOsVersion.Text = "…";
        DetailLastUser.Text = "…";
        DetailLaps.Text = "…";
        PingSummary.Text = "Ping: …";
        PingDetails.Text = string.Empty;
        PingDot.Fill = GetToolboxBrush("ToolboxSubtleFillBrush");
        Log($"Loading details for {item.Name}…");

        try
        {
            var detail = await App.ComputerInfo.GetDetailAsync(item.Name, token);
            if (token.IsCancellationRequested)
            {
                return;
            }

            _currentDetail = detail;
            DetailName.Text = detail.ComputerName;
            DetailOs.Text = detail.OperatingSystem;
            DetailOsVersion.Text = detail.OperatingSystemVersion;
            DetailLastUser.Text = detail.LastLoggedOnUser;
            DetailLaps.Text = detail.LapsPassword;
            ApplyPingUi(detail.Ping);
            RebuildDetailActions(detail.Ping.IsReachable);
            Log($"Details ready for {item.Name} ({detail.Ping.Status}).");
        }
        catch (OperationCanceledException)
        {
            Log("Detail load cancelled.");
        }
        catch (Exception ex)
        {
            Log($"Detail load failed: {ex.Message}");
            DetailOs.Text = "Error";
            DetailOsVersion.Text = ex.Message;
        }
        finally
        {
            DetailProgress.IsActive = false;
            DetailProgress.Visibility = Visibility.Collapsed;
        }
    }

    private void ApplyPingUi(PingResult ping)
    {
        PingSummary.Text = ping.IsReachable
            ? $"Ping: online ({ping.RoundtripTimeMs} ms)"
            : $"Ping: offline ({ping.Status})";
        PingDetails.Text = $"Reply: {ping.Status}";
        PingDot.Fill = new SolidColorBrush(ping.IsReachable
            ? ColorHelper.FromArgb(255, 46, 125, 80)
            : ColorHelper.FromArgb(255, 176, 60, 60));
    }

    private void RebuildDetailActions(bool isOnline)
    {
        DetailActionsHost.Items.Clear();
        foreach (var action in App.Settings.Current.DetailActions.Where(a => a.Enabled))
        {
            var button = new Button
            {
                Content = action.Label,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Tag = action,
                IsEnabled = !action.RequiresOnline || isOnline
            };
            button.Click += DetailAction_Click;
            DetailActionsHost.Items.Add(button);
        }
    }

    private void DetailAction_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: DetailActionDefinition action })
        {
            return;
        }

        var computer = _currentDetail?.ComputerName ?? (ResultsList.SelectedItem as ComputerItem)?.Name;
        if (string.IsNullOrWhiteSpace(computer))
        {
            Log("Select a computer first.");
            return;
        }

        try
        {
            App.RemoteTools.LaunchDetailAction(action, computer);
            Log($"Launched {action.Label} for {computer}.");
        }
        catch (Exception ex)
        {
            Log($"Could not launch {action.Label}: {ex.Message}");
        }
    }

    private void ClearDetail()
    {
        _currentDetail = null;
        DetailName.Text = "—";
        DetailOs.Text = "—";
        DetailOsVersion.Text = "—";
        DetailLastUser.Text = "—";
        DetailLaps.Text = "—";
        PingSummary.Text = "Ping: —";
        PingDetails.Text = string.Empty;
        PingDot.Fill = GetToolboxBrush("ToolboxSubtleFillBrush");
        DetailActionsHost.Items.Clear();
    }

    private static Brush GetToolboxBrush(string key)
    {
        if (Application.Current.Resources.TryGetValue(key, out var value) && value is Brush brush)
        {
            return brush;
        }

        return new SolidColorBrush(Colors.Gray);
    }

    private void RemoteButton_Click(object sender, RoutedEventArgs e)
    {
        var computer = _currentDetail?.ComputerName ?? (ResultsList.SelectedItem as ComputerItem)?.Name;
        if (string.IsNullOrWhiteSpace(computer))
        {
            Log("Select a computer before remoting in.");
            return;
        }

        if (RemoteToolCombo.SelectedItem is not AvailableRemoteTool tool)
        {
            Log("No remote tool is available on this PC. Check remoteTools paths in appsettings.json.");
            return;
        }

        try
        {
            App.RemoteTools.LaunchRemote(tool, computer);
            Log($"Connecting to {computer} with {tool.Label}…");
        }
        catch (Exception ex)
        {
            Log($"Remote launch failed: {ex.Message}");
        }
    }

    private void CopyComputerName_Click(object sender, RoutedEventArgs e)
    {
        var name = DetailName.Text;
        if (string.IsNullOrWhiteSpace(name) || name == "—")
        {
            return;
        }

        var package = new DataPackage();
        package.SetText(name);
        Clipboard.SetContent(package);
        Log($"Copied computer name {name}.");
    }

    private void CopyLaps_Click(object sender, RoutedEventArgs e)
    {
        var laps = DetailLaps.Text;
        if (string.IsNullOrWhiteSpace(laps) || laps is "—" or "N/A" or "…")
        {
            Log("No LAPS password to copy.");
            return;
        }

        var package = new DataPackage();
        package.SetText(laps);
        Clipboard.SetContent(package);
        Log("Copied LAPS password.");
    }

    private void ThemeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressThemeEvent)
        {
            return;
        }

        if (ThemeCombo.SelectedItem is ComboBoxItem { Tag: string themeId })
        {
            App.Theme.Apply(themeId, this);
            try
            {
                App.Settings.SaveCurrent();
            }
            catch (Exception ex)
            {
                Log($"Theme applied, but settings could not be saved: {ex.Message}");
            }
        }
    }

    private async void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SettingsDialog
        {
            XamlRoot = RootGrid.XamlRoot
        };
        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary || dialog.DefaultsRestored)
        {
            Title = App.Settings.Current.ProgramTitle;
            TitleText.Text = App.Settings.Current.ProgramTitle;
            ApplyLabelsFromSettings();
            LoadRemoteTools();
            LoadPresets();
            LoadThemeChoices();
            LoadStreamingEndpoints();
            App.Theme.Apply(App.Settings.Current.Theme, this);
            BindCollections();
            if (result == ContentDialogResult.Primary)
            {
                Log($"Settings saved to {App.Settings.ActiveSettingsPath}");
            }
            else
            {
                Log($"Restored install defaults to {App.Settings.ActiveSettingsPath}");
            }
        }
    }

    private void LoadStreamingEndpoints()
    {
        var visible = App.Streaming.IsTabVisible;
        StreamingTab.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

        var selectedId = (StreamingEndpointCombo.SelectedItem as StreamingEndpoint)?.Id;
        StreamingEndpointCombo.Items.Clear();
        foreach (var endpoint in App.Streaming.GetEndpoints())
        {
            StreamingEndpointCombo.Items.Add(endpoint);
        }

        if (StreamingEndpointCombo.Items.Count == 0)
        {
            StreamingEndpointCombo.SelectedItem = null;
            return;
        }

        StreamingEndpointCombo.SelectedItem = StreamingEndpointCombo.Items
            .OfType<StreamingEndpoint>()
            .FirstOrDefault(e => string.Equals(e.Id, selectedId, StringComparison.OrdinalIgnoreCase))
            ?? StreamingEndpointCombo.Items[0];
    }

    private StreamingEndpoint? SelectedStreamingEndpoint =>
        StreamingEndpointCombo.SelectedItem as StreamingEndpoint;

    private void StreamingConnectButton_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedStreamingEndpoint is null)
        {
            Log("Select a streaming PC first.");
            return;
        }

        Log(App.Streaming.Connect(SelectedStreamingEndpoint));
    }

    private void StreamingViewButton_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedStreamingEndpoint is null)
        {
            Log("Select a streaming PC first.");
            return;
        }

        Log(App.Streaming.OpenViewStream(SelectedStreamingEndpoint));
    }

    private void StreamingControlButton_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedStreamingEndpoint is null)
        {
            Log("Select a streaming PC first.");
            return;
        }

        Log(App.Streaming.OpenControlApp(SelectedStreamingEndpoint));
    }

    private void StreamingPasswordButton_Click(object sender, RoutedEventArgs e)
    {
        Log(App.Streaming.OpenChangePassword());
    }

    private void Log(string message)
    {
        _statusCounter++;
        _status.Add(new StatusLogEntry
        {
            Index = _statusCounter,
            Message = message
        });

        var limit = Math.Max(20, App.Settings.Current.Ui.StatusLogLimit);
        while (_status.Count > limit)
        {
            _status.RemoveAt(0);
        }

        if (_status.Count > 0)
        {
            StatusList.ScrollIntoView(_status[^1]);
        }
    }
}
