using Microsoft.UI.Xaml;

namespace Moment;

public partial class App : Application
{
    private Window? _window;
    private MainPage? _mainPage;
    private TrayManager? _tray;
    private bool _exiting;
    public static MainWindow CurrentWindow { get; private set; } = null!;

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, exception) =>
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Moment", "startup.log");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.AppendAllText(path, $"{DateTimeOffset.Now:O} {exception.Exception}\n");
        };
    }

    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        var mainWindow = new MainWindow();
        CurrentWindow = mainWindow;
        _window = mainWindow;
        _mainPage = new MainPage();
        mainWindow.ContentFrame.Content = _mainPage;
        _tray = new TrayManager();
        _mainPage.NotificationRequested += (title, message, error) => _tray?.ShowNotification(title, message, error);
        _tray.OpenRequested += OpenMainWindow;
        _tray.ExitRequested += ExitApplication;
        mainWindow.AppWindow.Closing += (_, closing) =>
        {
            if (!_exiting && new SettingsStore().Load().CloseToTray)
            {
                closing.Cancel = true;
                NativeWindowUtilities.Hide(mainWindow.NativeHandle);
            }
        };
        mainWindow.Closed += MainWindowClosed;
        mainWindow.Activate();

        var startedInBackground = args.Arguments.Contains("--background", StringComparison.OrdinalIgnoreCase);
        var startedInForeground = args.Arguments.Contains("--foreground", StringComparison.OrdinalIgnoreCase);
        var launchSettings = new SettingsStore().Load();
        // The installer and an explicit user launch must win over the
        // background startup preference. StartMinimized is only for the
        // Windows-login/tray path, never for the Finish-page Launch Moment
        // action.
        if (startedInForeground)
            NativeWindowUtilities.RestoreAndActivate(mainWindow.NativeHandle);
        else if (startedInBackground || (launchSettings.StartWithWindows && launchSettings.StartMinimized))
            NativeWindowUtilities.Hide(mainWindow.NativeHandle);
    }

    private void MainWindowClosed(object sender, WindowEventArgs args)
    {
        if (!_exiting && new SettingsStore().Load().CloseToTray)
        {
            args.Handled = true;
            NativeWindowUtilities.Hide(CurrentWindow.NativeHandle);
            return;
        }

        _mainPage?.Dispose();
        _mainPage = null;
        _tray?.Dispose();
        _tray = null;
    }

    private void OpenMainWindow() => CurrentWindow.DispatcherQueue.TryEnqueue(() =>
        NativeWindowUtilities.RestoreAndActivate(CurrentWindow.NativeHandle));

    public void ExitForUpdate() => CurrentWindow.DispatcherQueue.TryEnqueue(ExitApplication);

    private void ExitApplication() => CurrentWindow.DispatcherQueue.TryEnqueue(() =>
    {
        _exiting = true;
        _mainPage?.Dispose();
        _mainPage = null;
        _tray?.Dispose();
        _tray = null;
        CurrentWindow.Close();
    });
}
