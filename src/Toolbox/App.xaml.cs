using System.Globalization;
using System.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Toolbox.Configuration;
using Toolbox.Diagnostics;
using Toolbox.Services;

namespace Toolbox;

public partial class App : Application
{
    private const uint MessageBoxIconError = 0x10;
    private const int CrashLogsToKeep = 10;

    private Window? _window;
    private SettingsLoadException? _settingsError;

    public static SettingsService Settings { get; } = new();
    public static AdComputerSearcher AdSearcher { get; private set; } = null!;
    public static ComputerInfoService ComputerInfo { get; private set; } = null!;
    public static LapsService Laps { get; private set; } = null!;
    public static RemoteToolService RemoteTools { get; private set; } = null!;
    public static AccessControlService AccessControl { get; private set; } = null!;
    public static ThemeService Theme { get; private set; } = null!;
    public static UpdateService Updater { get; private set; } = null!;
    public static LocalInstallService LocalInstall { get; private set; } = null!;
    public static SearchHistoryStore SearchHistory { get; private set; } = null!;
    public static StreamingService Streaming { get; private set; } = null!;

    public static Window? MainAppWindow => ((App)Current)._window;

    public App()
    {
        AppLog.Initialize(Path.Combine(SettingsService.AppDataFolder, "logs"));
        AppDomain.CurrentDomain.UnhandledException += (_, e) => AppLog.Error("Unhandled exception.", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            AppLog.Warn("Unobserved background task exception.", e.Exception);
            e.SetObserved();
        };

        try
        {
            Settings.Load();
            var theme = ThemeService.Normalize(Settings.Current.Theme);
            if (theme == "Light")
            {
                RequestedTheme = ApplicationTheme.Light;
            }
            else if (theme == "Dark")
            {
                RequestedTheme = ApplicationTheme.Dark;
            }
        }
        catch (SettingsLoadException ex)
        {
            _settingsError = ex;
        }

        InitializeComponent();
        UnhandledException += OnUnhandledException;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        AppLog.Info($"Toolbox {UpdateService.CurrentVersion} starting from {AppContext.BaseDirectory} (packaged={AppDeploymentKind.IsPackaged}).");

        if (_settingsError is not null)
        {
            AppLog.Error("Install settings could not be loaded.", _settingsError);
            ShowFatal("Toolbox cannot start because its configuration is invalid.\n\n" +
                      _settingsError.Message + "\n\nFile: " + _settingsError.FilePath +
                      "\n\nAsk your administrator to fix this file.");
            Exit();
            return;
        }

        try
        {
            AdSearcher = new AdComputerSearcher(Settings);
            ComputerInfo = new ComputerInfoService(Settings);
            Laps = new LapsService(Settings);
            RemoteTools = new RemoteToolService(Settings);
            AccessControl = new AccessControlService(Settings);
            Theme = new ThemeService(Settings);
            Updater = new UpdateService(Settings);
            LocalInstall = new LocalInstallService(Settings);
            SearchHistory = new SearchHistoryStore(Path.Combine(SettingsService.AppDataFolder, "search-history.json"));
            Streaming = new StreamingService(Settings, RemoteTools);

            var decision = AccessControl.Evaluate();
            if (!decision.Allowed)
            {
                AppLog.Warn($"Access denied for {Environment.UserDomainName}\\{Environment.UserName}: {decision.Reason}");
                ShowAccessDeniedAndExit();
                return;
            }

            _window = new MainWindow();
            Theme.Apply(Settings.Current.Theme, _window);
            _window.Activate();
        }
        catch (Exception ex)
        {
            ReportFatalError("Toolbox failed to start", ex);
            Exit();
        }
    }

    private void ShowAccessDeniedAndExit()
    {
        _window = new Window { Title = Settings.Current.ProgramTitle };
        var host = new Grid();
        _window.Content = host;
        _window.Activate();

        host.Loaded += async (_, _) =>
        {
            var dialog = new ContentDialog
            {
                Title = "Access denied",
                Content = AccessControl.DenyMessage,
                CloseButtonText = "Exit",
                XamlRoot = host.XamlRoot
            };
            await dialog.ShowAsync();
            _window?.Close();
            Exit();
        };
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        ReportFatalError("Toolbox hit an unexpected error and has to close", e.Exception);
        e.Handled = true;
        Exit();
    }

    private static void ReportFatalError(string title, Exception ex)
    {
        AppLog.Error(title, ex);
        var logPath = WriteCrashLog(ex);
        var message = new StringBuilder()
            .AppendLine(title + ".")
            .AppendLine()
            .AppendLine(ErrorTranslator.Describe(ex))
            .AppendLine()
            .AppendLine("Details for your administrator were written to:")
            .AppendLine(logPath ?? AppLog.CurrentFilePath ?? "(no log available)");
        ShowFatal(message.ToString());
    }

    private static void ShowFatal(string text)
    {
        try
        {
            _ = NativeMethods.MessageBoxW(IntPtr.Zero, text, "Toolbox", MessageBoxIconError);
        }
        catch (DllNotFoundException)
        {
        }
    }

    private static string? WriteCrashLog(Exception ex)
    {
        try
        {
            var folder = Path.Combine(SettingsService.AppDataFolder, "logs");
            Directory.CreateDirectory(folder);
            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            var logPath = Path.Combine(folder, $"crash-{stamp}.log");

            var body = new StringBuilder()
                .AppendLine(DateTimeOffset.Now.ToString("u", CultureInfo.InvariantCulture))
                .AppendLine("Version: " + UpdateService.CurrentVersion)
                .AppendLine("BaseDirectory: " + AppContext.BaseDirectory)
                .AppendLine("Executable: " + Environment.ProcessPath)
                .AppendLine("Packaged: " + AppDeploymentKind.IsPackaged)
                .AppendLine("HResult: 0x" + ex.HResult.ToString("X8", CultureInfo.InvariantCulture));
            AppendPayloadDiagnostics(body, AppContext.BaseDirectory);
            body.AppendLine().AppendLine(ex.ToString());
            File.WriteAllText(logPath, body.ToString());

            foreach (var old in Directory.GetFiles(folder, "crash-*.log").OrderByDescending(f => f, StringComparer.Ordinal).Skip(CrashLogsToKeep))
            {
                File.Delete(old);
            }

            return logPath;
        }
        catch (Exception logError) when (logError is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    // unpackaged WinUI crashes in InitializeComponent when these are missing
    private static void AppendPayloadDiagnostics(StringBuilder body, string baseDir)
    {
        body.AppendLine().AppendLine("--- Payload check ---");
        string[] required =
        [
            "Toolbox.exe", "Toolbox.pri", "resources.pri", "MainWindow.xbf", "App.xbf",
            "Microsoft.UI.Xaml.dll", "Microsoft.WindowsAppRuntime.Bootstrap.dll"
        ];

        foreach (var name in required)
        {
            var path = Path.Combine(baseDir, name);
            body.AppendLine(File.Exists(path) ? $"OK   {name} ({new FileInfo(path).Length} bytes)" : $"MISS {name}");
        }
    }
}
