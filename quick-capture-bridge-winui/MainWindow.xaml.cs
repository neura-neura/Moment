using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinRT.Interop;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace QuickCaptureBridgeWinUI;

/// <summary>
/// The application window. This hosts a Frame that displays pages. Add your
/// UI and logic to MainPage.xaml / MainPage.xaml.cs instead of here so you
/// can use Page features such as navigation events and the Loaded lifecycle.
/// </summary>
public sealed partial class MainWindow : Window
{
    private bool compactConfigured;

    public MainWindow()
    {
        InitializeComponent();
        // MainPage is created after the window is activated so the dispatcher
        // and native handle are ready for pickers and global hotkeys.
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.SetIcon("Assets/AppIcon.ico");
        Activated += (_, _) => ConfigureCompactWindow();
    }

    public Frame ContentFrame => RootFrame;

    public nint NativeHandle => WindowNative.GetWindowHandle(this);

    private void ConfigureCompactWindow()
    {
        if (compactConfigured) return;
        compactConfigured = true;
        // The settings window uses a native NavigationView with a left pane;
        // give the content area enough room for the pane and readable cards.
        NativeWindowUtilities.ResizeAndCenter(NativeHandle, 980, 720);
    }
}
