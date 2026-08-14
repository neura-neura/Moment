using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace QuickCaptureBridgeWinUI;

/// <summary>
/// Owns the WinUI entry point so the unpackaged runtime is prepared before
/// WinUI XAML is activated. The generated entry point is disabled in the
/// project file for the same reason.
/// </summary>
public static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        EnsureInsightsResourceLoaded();
        TouchPackageIdentity();
        WinRT.ComWrappersSupport.InitializeComWrappers();

        Application.Start(_ =>
        {
            var context = new DispatcherQueueSynchronizationContext(
                DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            new App();
        });
    }

    private static void EnsureInsightsResourceLoaded()
    {
        var resourcePath = Path.Combine(
            AppContext.BaseDirectory,
            "Microsoft.WindowsAppRuntime.Insights.Resource.dll");

        if (File.Exists(resourcePath))
        {
            _ = LoadLibrary(resourcePath);
        }
    }

    private static void TouchPackageIdentity()
    {
        // This is intentionally best-effort. Querying Package.Current gives
        // WinRT the same activation path used by the known-good unpackaged
        // WinUI host, while remaining valid when no package identity exists.
        try
        {
            _ = Windows.ApplicationModel.Package.Current;
        }
        catch
        {
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint LoadLibrary(string fileName);
}
