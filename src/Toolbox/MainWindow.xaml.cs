using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Toolbox.Configuration;
using Toolbox.Diagnostics;
using Toolbox.Laps;
using Toolbox.Models;
using Toolbox.Services;
using Toolbox.Views;
using Windows.Graphics;
using VirtualKey = Windows.System.VirtualKey;
using WinRT.Interop;

namespace Toolbox;

public sealed partial class MainWindow : Window
{
    private const string Mask = "••••••••••••";
    private const string NotRetrieved = "Not retrieved";

    private readonly ObservableCollection<ComputerItem> _results = new();
    private readonly ObservableCollection<string> _history = new();
    private readonly ObservableCollection<StatusLogEntry> _status = new();
    private readonly List<AvailableRemoteTool> _remoteTools = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SecureClipboard _clipboard;
    private readonly DispatcherQueueTimer _detailDebounce;
    private readonly DispatcherQueueTimer _lapsMaskTimer;

    private CancellationTokenSource? _searchCts;
    private CancellationTokenSource? _detailCts;
    private CancellationTokenSource? _lapsCts;
    private ComputerItem? _currentItem;
    private bool _currentOnline;
    private bool _lapsRevealed;
    private int _statusCounter;
    private bool _suppressThemeEvent;
    private bool _suppressPresetEvent;

    public MainWindow()
    {
        InitializeComponent();
        _clipboard = new SecureClipboard(DispatcherQueue);

        _detailDebounce = DispatcherQueue.CreateTimer();
        _detailDebounce.Interval = TimeSpan.FromMilliseconds(300);
        _detailDebounce.IsRepeating = false;
        _detailDebounce.Tick += async (_, _) =>
        {
            if (_currentItem is { } item)
            {
                await LoadDetailAsync(item);
            }
        };

        _lapsMaskTimer = DispatcherQueue.CreateTimer();
        _lapsMaskTimer.IsRepeating = false;
        _lapsMaskTimer.Tick += (_, _) => MaskLaps();

        ApplyWindowChrome();
        BindCollections();
        ApplyLabelsFromSettings();
        LoadRemoteTools();
        LoadThemeChoices();
        LoadPresets();
        LoadSearchHistory();
        LoadStreamingEndpoints();
        ClearDetail();
        Log(App.Settings.Current.Labels.StatusReady);
        RootGrid.Loaded += MainWindow_Loaded;
        Closed += MainWindow_Closed;
    }

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        _lifetime.Cancel();
        _clipboard.ClearOnExit();
        SaveHistory();
        try
        {
            App.Updater.ApplyPendingOnExit();
        }
        catch (Exception ex)
        {
            AppLog.Error("Applying the staged update failed.", ex);
        }
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        RootGrid.Loaded -= MainWindow_Loaded;
        ReportStartupState();

        if (App.LocalInstall.ShouldOfferLocalInstall)
        {
            var installDialog = new InstallLocallyDialog { XamlRoot = RootGrid.XamlRoot };
            var installResult = await installDialog.ShowAsync();
            if (installResult == ContentDialogResult.Primary && installDialog.InstallResult?.Succeeded == true)
            {
                Log($"Installed locally to {LocalInstallService.DefaultLocalInstallDirectory}.");
                if (LocalInstallService.TryLaunchLocalInstall())
                {
                    Close();
                    return;
                }

                Log("The local copy was installed but could not be started automatically. Use the desktop shortcut.");
            }
        }

        if (App.Settings.NeedsFirstRunSetup)
        {
            var dialog = new FirstRunDialog { XamlRoot = RootGrid.XamlRoot };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                ReloadFromSettings();
                Log($"Setup saved to {App.Settings.UserSettingsPath}.");
            }
        }

        await CheckForUpdatesAsync();
    }

    private void ReportStartupState()
    {
        foreach (var warning in App.Settings.LoadWarnings)
        {
            Log(warning);
        }

        var errors = App.Settings.Issues.Where(i => i.Severity == IssueSeverity.Error).ToList();
        foreach (var issue in App.Settings.Issues)
        {
            AppLog.Warn("Configuration: " + issue);
        }

        if (errors.Count > 0)
        {
            foreach (var issue in errors)
            {
                Log($"Configuration problem in {issue.Path}: {issue.Message}");
            }

            ShowNotice(InfoBarSeverity.Warning, "Configuration problems",
                $"{errors.Count} setting(s) are invalid and the related features are disabled. See the status log for details.");
        }
        else if (App.Settings.LoadWarnings.Count > 0)
        {
            ShowNotice(InfoBarSeverity.Warning, "Settings", App.Settings.LoadWarnings[0]);
        }

        if (UpdateService.ConsumeLastResult() is { } last)
        {
            Log(last.Message);
            ShowNotice(last.Succeeded ? InfoBarSeverity.Success : InfoBarSeverity.Error,
                last.Succeeded ? "Updated" : "Update failed", last.Message);
        }
    }

    private async Task CheckForUpdatesAsync()
    {
        try
        {
            var result = await App.Updater.CheckAndStageAsync(_lifetime.Token);
            switch (result.Status)
            {
                case UpdateCheckStatus.Ready:
                    Log(result.Message);
                    ShowNotice(InfoBarSeverity.Informational, "Update ready", result.Message);
                    break;
                case UpdateCheckStatus.Failed:
                    Log("Update check failed: " + result.Message);
                    ShowNotice(InfoBarSeverity.Warning, "Update not installed", result.Message);
                    break;
                case UpdateCheckStatus.UpToDate:
                    Log(result.Message);
                    break;
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void ShowNotice(InfoBarSeverity severity, string title, string message)
    {
        NoticeBar.Severity = severity;
        NoticeBar.Title = title;
        NoticeBar.Message = message;
        NoticeBar.IsOpen = true;
    }

    private void ApplyWindowChrome()
    {
        var ui = App.Settings.Current.Ui;
        Title = App.Settings.Current.ProgramTitle;
        TitleText.Text = App.Settings.Current.ProgramTitle;

        var hwnd = WindowNative.GetWindowHandle(this);
        var appWindow = AppWindow.GetFromWindowId(Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd));
        appWindow.Resize(new SizeInt32(Math.Max(ui.WindowWidth, 640), Math.Max(ui.WindowHeight, 480)));

        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
        if (File.Exists(iconPath))
        {
            appWindow.SetIcon(iconPath);
        }

        if (appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = Math.Max(ui.MinWidth, 640);
            presenter.PreferredMinimumHeight = Math.Max(ui.MinHeight, 480);
        }
    }

    private void BindCollections()
    {
        ResultsList.ItemsSource = _results;
        HistoryList.ItemsSource = _history;
        StatusList.ItemsSource = _status;

        var ui = App.Settings.Current.Ui;
        HistoryPanel.Visibility = ui.ShowSearchHistory ? Visibility.Visible : Visibility.Collapsed;
        HistoryColumn.Width = ui.ShowSearchHistory ? new GridLength(200) : new GridLength(0);
        StatusPanel.Visibility = ui.ShowStatusLog ? Visibility.Visible : Visibility.Collapsed;
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

    private void ReloadFromSettings()
    {
        Title = App.Settings.Current.ProgramTitle;
        TitleText.Text = App.Settings.Current.ProgramTitle;
        ApplyLabelsFromSettings();
        LoadRemoteTools();
        LoadPresets();
        LoadThemeChoices();
        LoadStreamingEndpoints();
        BindCollections();
        App.Theme.Apply(App.Settings.Current.Theme, this);
    }

    private void LoadRemoteTools()
    {
        _remoteTools.Clear();
        _remoteTools.AddRange(App.RemoteTools.GetAvailableTools());
        RemoteToolCombo.ItemsSource = null;
        RemoteToolCombo.ItemsSource = _remoteTools;
        RemoteToolCombo.SelectedItem = _remoteTools.FirstOrDefault(t => t.PreferAsDefault) ?? _remoteTools.FirstOrDefault();
        if (_remoteTools.Count == 0)
        {
            Log("No remote tools were found on this PC. Check remoteTools in appsettings.json.");
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
        ThemeCombo.SelectedItem = ThemeCombo.Items.OfType<ComboBoxItem>()
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
        PresetCombo.Visibility = PresetCombo.Items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        _suppressPresetEvent = false;
    }

    private async void SearchButton_Click(object sender, RoutedEventArgs e) => await RunSearchAsync();

    private void CancelSearchButton_Click(object sender, RoutedEventArgs e) => _searchCts?.Cancel();

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
        _searchCts?.Cancel();
        var cts = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _searchCts = cts;

        SetSearching(true);
        Log($"Searching for “{query}”…");
        try
        {
            var outcome = await App.AdSearcher.SearchAsync(query, cts.Token);
            PushHistory(query);
            _results.Clear();
            foreach (var item in outcome.Items)
            {
                _results.Add(item);
            }

            EmptyResultsText.Visibility = _results.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            ResultCountText.Text = outcome.Truncated
                ? $"First {_results.Count} results"
                : $"{_results.Count} result{(_results.Count == 1 ? string.Empty : "s")}";
            var seconds = outcome.Elapsed.TotalSeconds.ToString("0.0", CultureInfo.CurrentCulture);
            Log(outcome.Truncated
                ? $"Showing the first {_results.Count} computers ({seconds} s). Narrow the search to see the rest."
                : $"Found {_results.Count} computer(s) in {seconds} s.");
            ClearDetail();
        }
        catch (SearchInputException ex)
        {
            Log(ex.Message);
            SearchBox.Focus(FocusState.Programmatic);
        }
        catch (OperationCanceledException)
        {
            if (ReferenceEquals(_searchCts, cts))
            {
                Log("Search cancelled.");
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("Search failed.", ex);
            Log("Search failed: " + ErrorTranslator.Describe(ex));
        }
        finally
        {
            if (ReferenceEquals(_searchCts, cts))
            {
                SetSearching(false);
                _searchCts = null;
            }

            cts.Dispose();
        }
    }

    private void SetSearching(bool isSearching)
    {
        SearchProgress.IsActive = isSearching;
        SearchProgress.Visibility = isSearching ? Visibility.Visible : Visibility.Collapsed;
        SearchButton.Visibility = isSearching ? Visibility.Collapsed : Visibility.Visible;
        CancelSearchButton.Visibility = isSearching ? Visibility.Visible : Visibility.Collapsed;
    }

    private void LoadSearchHistory()
    {
        _history.Clear();
        foreach (var query in App.SearchHistory.Load(SettingsLimits.HistoryLimit(App.Settings.Current.Search)))
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
        var limit = SettingsLimits.HistoryLimit(App.Settings.Current.Search);
        while (_history.Count > limit)
        {
            _history.RemoveAt(_history.Count - 1);
        }

        SaveHistory();
    }

    private void SaveHistory()
    {
        try
        {
            App.SearchHistory.Save(_history, SettingsLimits.HistoryLimit(App.Settings.Current.Search));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn("Search history could not be saved.", ex);
        }
    }

    private void ClearHistoryButton_Click(object sender, RoutedEventArgs e)
    {
        _history.Clear();
        App.SearchHistory.Clear();
        Log("Search history cleared.");
    }

    private async void HistoryList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is string query)
        {
            SearchBox.Text = query;
            await RunSearchAsync();
        }
    }

    private void PresetCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressPresetEvent || PresetCombo.SelectedItem is not ComboBoxItem { Tag: SearchPreset preset })
        {
            return;
        }

        var text = PresetQuery.Resolve(preset.QueryText, DateTime.Now, CultureInfo.CurrentCulture);
        if (!string.IsNullOrEmpty(text))
        {
            SearchBox.Text = text;
            SearchBox.Focus(FocusState.Programmatic);
            SearchBox.SelectionStart = text.Length;
        }
    }

    private void ResultsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ResultsList.SelectedItem is not ComputerItem item || ReferenceEquals(item, _currentItem))
        {
            return;
        }

        ShowItemSummary(item);
        _detailDebounce.Stop();
        _detailDebounce.Start();
    }

    private async void ResultsList_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (ResultsList.SelectedItem is ComputerItem item)
        {
            _detailDebounce.Stop();
            await LoadDetailAsync(item);
        }
    }

    private async void ResultsList_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter && ResultsList.SelectedItem is ComputerItem item)
        {
            e.Handled = true;
            _detailDebounce.Stop();
            await LoadDetailAsync(item);
        }
    }

    private void ShowItemSummary(ComputerItem item)
    {
        _detailCts?.Cancel();
        _lapsCts?.Cancel();
        MaskLaps();
        _currentItem = item;
        _currentOnline = false;

        DetailName.Text = item.Name;
        DetailAdOs.Text = string.IsNullOrWhiteSpace(item.OperatingSystem)
            ? "Not recorded"
            : $"{item.OperatingSystem} {item.OperatingSystemVersion}".Trim();
        DetailLastLogon.Text = item.LastLogon?.ToLocalTime().ToString("g", CultureInfo.CurrentCulture) ?? "Never or unknown";
        DetailOs.Text = DetailOsVersion.Text = DetailLastUser.Text = "…";
        DetailLaps.Text = NotRetrieved;
        LapsInfoText.Text = App.Settings.Current.Laps.Enabled ? string.Empty : "LAPS retrieval is turned off.";
        SetPing(null, "Ping: …");

        var usable = item.HasValidHostName;
        RemoteButton.IsEnabled = usable && _remoteTools.Count > 0;
        CopyNameButton.IsEnabled = true;
        ShowLapsButton.IsEnabled = CopyLapsButton.IsEnabled = App.Settings.Current.Laps.Enabled;
        RebuildDetailActions(false);

        if (!usable)
        {
            Log($"“{item.Name}” is not a valid host name, so ping, remote registry and remote tools are disabled for it.");
        }
    }

    private async Task LoadDetailAsync(ComputerItem item)
    {
        if (!ReferenceEquals(item, _currentItem))
        {
            ShowItemSummary(item);
        }

        _detailCts?.Cancel();
        var cts = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _detailCts = cts;

        DetailProgress.IsActive = true;
        DetailProgress.Visibility = Visibility.Visible;
        DetailOs.Text = App.Settings.Current.Labels.LoadingDetail;

        try
        {
            var detail = await App.ComputerInfo.GetDetailAsync(item, cts.Token);
            if (cts.IsCancellationRequested || !ReferenceEquals(item, _currentItem))
            {
                return;
            }

            DetailOs.Text = detail.OperatingSystem;
            DetailOsVersion.Text = detail.OperatingSystemVersion;
            DetailLastUser.Text = detail.LastLoggedOnUser;
            _currentOnline = detail.Ping.IsReachable;
            SetPing(detail.Ping.IsReachable,
                detail.Ping.IsReachable ? $"Online ({detail.Ping.RoundtripTimeMs} ms)" : $"Offline ({detail.Ping.Status})");
            RebuildDetailActions(detail.Ping.IsReachable);
            Log($"Details loaded for {item.Name} ({(detail.Ping.IsReachable ? "online" : detail.Ping.Status)}).");
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            AppLog.Error($"Loading details for {item.Name} failed.", ex);
            DetailOs.Text = "Error";
            DetailOsVersion.Text = ErrorTranslator.Describe(ex);
            Log($"Could not load details for {item.Name}: {ErrorTranslator.Describe(ex)}");
        }
        finally
        {
            if (ReferenceEquals(_detailCts, cts))
            {
                DetailProgress.IsActive = false;
                DetailProgress.Visibility = Visibility.Collapsed;
                _detailCts = null;
            }

            cts.Dispose();
        }
    }

    private void SetPing(bool? online, string text)
    {
        PingDotUnknown.Visibility = online is null ? Visibility.Visible : Visibility.Collapsed;
        PingDotOnline.Visibility = online == true ? Visibility.Visible : Visibility.Collapsed;
        PingDotOffline.Visibility = online == false ? Visibility.Visible : Visibility.Collapsed;
        PingSummary.Text = text;
    }

    private void RebuildDetailActions(bool isOnline)
    {
        DetailActionsHost.Items.Clear();
        var usable = _currentItem?.HasValidHostName == true;
        foreach (var action in App.Settings.Current.DetailActions.Where(a => a.Enabled))
        {
            var button = new Button
            {
                Content = action.Label,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Tag = action,
                IsEnabled = usable && (!action.RequiresOnline || isOnline)
            };
            if (action.RequiresOnline && !isOnline)
            {
                ToolTipService.SetToolTip(button, "Available when the computer answers ping.");
            }

            button.Click += DetailAction_Click;
            DetailActionsHost.Items.Add(button);
        }
    }

    private void ClearDetail()
    {
        _detailCts?.Cancel();
        _lapsCts?.Cancel();
        MaskLaps();
        _currentItem = null;
        _currentOnline = false;
        DetailName.Text = DetailAdOs.Text = DetailLastLogon.Text = "—";
        DetailOs.Text = DetailOsVersion.Text = DetailLastUser.Text = "—";
        DetailLaps.Text = NotRetrieved;
        LapsInfoText.Text = string.Empty;
        SetPing(null, "Ping: —");
        RemoteButton.IsEnabled = CopyNameButton.IsEnabled = ShowLapsButton.IsEnabled = CopyLapsButton.IsEnabled = false;
        DetailActionsHost.Items.Clear();
    }

    private void DetailAction_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: DetailActionDefinition action } && _currentItem is { } item)
        {
            Log(App.RemoteTools.LaunchDetailAction(action, item.Name).Message);
        }
    }

    private void RemoteButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentItem is not { } item)
        {
            Log("Select a computer before remoting in.");
            return;
        }

        if (RemoteToolCombo.SelectedItem is not AvailableRemoteTool tool)
        {
            Log("No remote tool is available on this PC. Check remoteTools in appsettings.json.");
            return;
        }

        if (!_currentOnline && PingSummary.Text.StartsWith("Offline", StringComparison.Ordinal))
        {
            Log($"{item.Name} did not answer ping; trying anyway.");
        }

        Log(App.RemoteTools.LaunchRemote(tool, item.Name).Message);
    }

    private void CopyComputerName_Click(object sender, RoutedEventArgs e)
    {
        if (_currentItem is { } item)
        {
            SecureClipboard.CopyText(item.Name);
            Log($"Copied {item.Name}.");
        }
    }

    private async void ShowLapsButton_Click(object sender, RoutedEventArgs e)
    {
        if (_lapsRevealed)
        {
            MaskLaps();
            return;
        }

        var result = await RetrieveLapsAsync();
        if (result?.Password is null)
        {
            return;
        }

        DetailLaps.Text = result.Password;
        _lapsRevealed = true;
        ShowLapsButton.Content = "Hide";
        _lapsMaskTimer.Interval = SettingsLimits.Reveal(App.Settings.Current.Laps);
        _lapsMaskTimer.Start();
    }

    private async void CopyLapsButton_Click(object sender, RoutedEventArgs e)
    {
        var item = _currentItem;
        var result = await RetrieveLapsAsync();
        if (result?.Password is null || item is null)
        {
            return;
        }

        var clearAfter = SettingsLimits.ClipboardClear(App.Settings.Current.Laps);
        if (!_clipboard.CopySecret(result.Password, clearAfter))
        {
            Log("The password could not be copied. Try again.");
            return;
        }

        Log(clearAfter > TimeSpan.Zero
            ? $"Copied the LAPS password for {item.Name}. It stays out of clipboard history and will be cleared in {clearAfter.TotalSeconds:0} s."
            : $"Copied the LAPS password for {item.Name}. It stays out of clipboard history.");
    }

    private async Task<LapsResult?> RetrieveLapsAsync()
    {
        if (_currentItem is not { } item)
        {
            Log("Select a computer first.");
            return null;
        }

        _lapsCts?.Cancel();
        var cts = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _lapsCts = cts;
        ShowLapsButton.IsEnabled = CopyLapsButton.IsEnabled = false;
        LapsInfoText.Text = "Retrieving…";

        try
        {
            var result = await App.Laps.RetrieveAsync(item, cts.Token);
            if (!ReferenceEquals(item, _currentItem))
            {
                return null;
            }

            if (result.Status != LapsStatus.Retrieved)
            {
                LapsInfoText.Text = result.Message;
                Log($"LAPS for {item.Name}: {result.Message}");
                return null;
            }

            if (!_lapsRevealed)
            {
                DetailLaps.Text = Mask;
            }

            LapsInfoText.Text = DescribeLaps(result);
            return result;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        finally
        {
            if (ReferenceEquals(_lapsCts, cts))
            {
                ShowLapsButton.IsEnabled = CopyLapsButton.IsEnabled = _currentItem is not null;
                _lapsCts = null;
            }

            cts.Dispose();
        }
    }

    private static string DescribeLaps(LapsResult result)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(result.AccountName))
        {
            parts.Add(result.AccountName!);
        }

        parts.Add(LapsResult.Describe(result.Source));
        if (result.Expiration is { } expires)
        {
            parts.Add((expires < DateTimeOffset.Now ? "expired " : "expires ") +
                      expires.ToLocalTime().ToString("g", CultureInfo.CurrentCulture));
        }

        return string.Join(" · ", parts);
    }

    private void MaskLaps()
    {
        _lapsMaskTimer.Stop();
        if (_lapsRevealed)
        {
            DetailLaps.Text = Mask;
        }

        _lapsRevealed = false;
        ShowLapsButton.Content = "Show";
    }

    private void ThemeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressThemeEvent || ThemeCombo.SelectedItem is not ComboBoxItem { Tag: string themeId })
        {
            return;
        }

        App.Theme.Apply(themeId, this);
        try
        {
            App.Settings.SaveUserSettings(App.Settings.Current, markSetupComplete: false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn("Theme could not be saved.", ex);
            Log("Theme applied, but it could not be saved: " + ErrorTranslator.Describe(ex));
        }
    }

    private async void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SettingsDialog { XamlRoot = RootGrid.XamlRoot };
        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary || dialog.DefaultsRestored)
        {
            ReloadFromSettings();
            Log(result == ContentDialogResult.Primary
                ? $"Settings saved to {App.Settings.UserSettingsPath}."
                : "Personal settings were reset to the install defaults.");
        }
    }

    private void FocusSearch_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        SearchBox.Focus(FocusState.Keyboard);
        SearchBox.SelectAll();
    }

    private void Cancel_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (_searchCts is not null || _detailCts is not null)
        {
            args.Handled = true;
            _searchCts?.Cancel();
            _detailCts?.Cancel();
        }
    }

    private async void RefreshDetails_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (_currentItem is { } item)
        {
            args.Handled = true;
            await LoadDetailAsync(item);
        }
    }

    private void OpenLogButton_Click(object sender, RoutedEventArgs e)
    {
        var folder = AppLog.Directory;
        if (folder is null || !Directory.Exists(folder))
        {
            Log("The log folder is not available.");
            return;
        }

        try
        {
            var explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
            using var process = Process.Start(new ProcessStartInfo(explorer) { ArgumentList = { folder }, UseShellExecute = false });
        }
        catch (Win32Exception ex)
        {
            Log("Could not open the log folder: " + ErrorTranslator.Describe(ex));
        }
    }

    private void LoadStreamingEndpoints()
    {
        StreamingTab.Visibility = App.Streaming.IsTabVisible ? Visibility.Visible : Visibility.Collapsed;

        var selectedId = (StreamingEndpointCombo.SelectedItem as StreamingEndpoint)?.Id;
        StreamingEndpointCombo.Items.Clear();
        foreach (var endpoint in App.Streaming.GetEndpoints())
        {
            StreamingEndpointCombo.Items.Add(endpoint);
        }

        StreamingEndpointCombo.SelectedItem = StreamingEndpointCombo.Items.Count == 0
            ? null
            : StreamingEndpointCombo.Items.OfType<StreamingEndpoint>()
                  .FirstOrDefault(e => string.Equals(e.Id, selectedId, StringComparison.OrdinalIgnoreCase))
              ?? StreamingEndpointCombo.Items[0];
        StreamingAccountButton.Visibility = string.IsNullOrWhiteSpace(App.Streaming.AccountPageUrl)
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private StreamingEndpoint? SelectedStreamingEndpoint => StreamingEndpointCombo.SelectedItem as StreamingEndpoint;

    private void StreamingConnectButton_Click(object sender, RoutedEventArgs e) =>
        Log(SelectedStreamingEndpoint is { } endpoint ? App.Streaming.Connect(endpoint) : "Select a streaming PC first.");

    private void StreamingViewButton_Click(object sender, RoutedEventArgs e) =>
        Log(SelectedStreamingEndpoint is { } endpoint ? App.Streaming.OpenViewStream(endpoint) : "Select a streaming PC first.");

    private void StreamingControlButton_Click(object sender, RoutedEventArgs e) =>
        Log(SelectedStreamingEndpoint is { } endpoint ? App.Streaming.OpenControlApp(endpoint) : "Select a streaming PC first.");

    private void StreamingAccountButton_Click(object sender, RoutedEventArgs e) =>
        Log(App.Streaming.OpenStreamAccountPage());

    private void Log(string message)
    {
        AppLog.Info(message);
        _statusCounter++;
        _status.Add(new StatusLogEntry { Index = _statusCounter, Message = message });
        LatestStatusText.Text = message;

        var limit = SettingsLimits.StatusLogLimit(App.Settings.Current.Ui);
        while (_status.Count > limit)
        {
            _status.RemoveAt(0);
        }

        StatusList.ScrollIntoView(_status[^1]);
    }
}
