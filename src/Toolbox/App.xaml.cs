using System.Runtime.InteropServices;
using System.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Toolbox.Services;

namespace Toolbox;

public partial class App : Application
{
    private Window? _window;

    public static SettingsService Settings { get; } = new();
    public static AdComputerSearcher AdSearcher { get; private set; } = null!;
    public static ComputerInfoService ComputerInfo { get; private set; } = null!;
    public static RemoteToolService RemoteTools { get; private set; } = null!;
    public static AccessControlService AccessControl { get; private set; } = null!;
    public static ThemeService Theme { get; private set; } = null!;
    public static UpdateService Updater { get; private set; } = null!;
    public static LocalInstallService LocalInstall { get; private set; } = null!;
    public static SearchHistoryService SearchHistory { get; private set; } = null!;
    public static StreamingService Streaming { get; private set; } = null!;

    public static Window? MainAppWindow => ((App)Current)._window;

    public App()
    {

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
        catch
        {

        }

        InitializeComponent();
        UnhandledException += OnUnhandledException;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            Settings.Load();
            AdSearcher = new AdComputerSearcher(Settings);
            ComputerInfo = new ComputerInfoService(Settings);
            RemoteTools = new RemoteToolService(Settings);
            AccessControl = new AccessControlService(Settings);
            Theme = new ThemeService(Settings);
            Updater = new UpdateService(Settings);
            LocalInstall = new LocalInstallService(Settings);
            SearchHistory = new SearchHistoryService();
            Streaming = new StreamingService(Settings, RemoteTools);

            if (!AccessControl.HasAccess())
            {
                _ = ShowAccessDeniedAndExitAsync();
                return;
            }

            try
            {
                _window = new MainWindow();
            }
            catch (Exception ex)
            {

                ReportFatalError("Toolbox failed while creating the main window", ex);
                Exit();
                return;
            }

            Theme.Apply(Settings.Current.Theme, _window);
            _window.Activate();
        }
        catch (Exception ex)
        {
            ReportFatalError("Toolbox failed to start", ex);
            Exit();
        }
    }

    private Task ShowAccessDeniedAndExitAsync()
    {
        _window = new Window { Title = Settings.Current.ProgramTitle };
        var dialogHost = new Grid();
        _window.Content = dialogHost;
        _window.Activate();

        dialogHost.Loaded += async (_, _) =>
        {
            var dialog = new ContentDialog
            {
                Title = "Access denied",
                Content = AccessControl.DenyMessage,
                CloseButtonText = "Exit",
                XamlRoot = dialogHost.XamlRoot
            };
            await dialog.ShowAsync();
            _window?.Close();
            Exit();
        };

        return Task.CompletedTask;
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        ReportFatalError("Toolbox hit an unexpected error", e.Exception);
        e.Handled = true;
        Exit();
    }

    private static void ReportFatalError(string title, Exception ex)
    {
        var logPath = WriteCrashLog(ex);
        var message = new StringBuilder()
            .AppendLine(title + ".")
            .AppendLine()
            .AppendLine(ex.GetType().Name + ": " + ex.Message);

        for (var inner = ex.InnerException; inner is not null; inner = inner.InnerException)
        {
            message.AppendLine("→ " + inner.GetType().Name + ": " + inner.Message);
        }

        message
            .AppendLine()
            .AppendLine("Details were written to:")
            .AppendLine(logPath);

        try
        {
            NativeMessageBox(message.ToString(), "Toolbox");
        }
        catch
        {

        }
    }

    private static string WriteCrashLog(Exception ex)
    {
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Toolbox");
        Directory.CreateDirectory(folder);
        var logPath = Path.Combine(folder, "crash.log");

        var body = new StringBuilder()
            .AppendLine(DateTimeOffset.Now.ToString("u"))
            .AppendLine("BaseDirectory: " + AppContext.BaseDirectory)
            .AppendLine("CurrentDirectory: " + Environment.CurrentDirectory)
            .AppendLine("Executable: " + Environment.ProcessPath)
            .AppendLine("HResult: 0x" + ex.HResult.ToString("X8"))
            .AppendLine("Packaged: " + AppDeploymentKind.IsPackaged);

        AppendPayloadDiagnostics(body, AppContext.BaseDirectory);

        body.AppendLine().AppendLine(ex.ToString());
        for (var inner = ex.InnerException; inner is not null; inner = inner.InnerException)
        {
            body.AppendLine()
                .AppendLine("--- Inner ---")
                .AppendLine(inner.GetType().FullName + ": " + inner.Message)
                .AppendLine("HResult: 0x" + inner.HResult.ToString("X8"));
        }

        File.WriteAllText(logPath, body.ToString());
        return logPath;
    }

    private static void AppendPayloadDiagnostics(StringBuilder body, string baseDir)
    {
        body.AppendLine().AppendLine("--- Payload check (unpackaged WinUI needs these) ---");
        if (string.IsNullOrWhiteSpace(baseDir) || !Directory.Exists(baseDir))
        {
            body.AppendLine("BaseDirectory missing.");
            return;
        }

        string[] required =
        [
            "Toolbox.exe",
            "Toolbox.pri",
            "resources.pri",
            "MainWindow.xbf",
            "App.xbf",
            "Microsoft.UI.Xaml.dll",
            "Microsoft.WindowsAppRuntime.Bootstrap.dll"
        ];

        foreach (var name in required)
        {
            var path = Path.Combine(baseDir, name);
            body.AppendLine(File.Exists(path)
                ? $"OK   {name} ({new FileInfo(path).Length} bytes)"
                : $"MISS {name}");
        }

        try
        {
            var xbfCount = Directory.GetFiles(baseDir, "*.xbf", SearchOption.AllDirectories).Length;
            var priCount = Directory.GetFiles(baseDir, "*.pri", SearchOption.TopDirectoryOnly).Length;
            body.AppendLine($"xbf files: {xbfCount}");
            body.AppendLine($"pri files (root): {priCount}");
        }
        catch (Exception scanEx)
        {
            body.AppendLine("Payload scan failed: " + scanEx.Message);
        }
    }

    private static void NativeMessageBox(string text, string caption)
    {
        MessageBoxW(IntPtr.Zero, text, caption, 0x00000010);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);
}
