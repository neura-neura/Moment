using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace Moment;

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
        using var instanceMutex = new Mutex(true, @"Local\Moment.SingleInstance", out var createdNew);
        if (!createdNew)
        {
            ActivateExistingInstance();
            return;
        }

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

    private static void ActivateExistingInstance()
    {
        // The first instance may still be creating its WinUI window. Give it
        // a short window to finish, then bring the hidden/tray window forward.
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var hwnd = FindWindow(null, "Moment");
            if (hwnd != nint.Zero)
            {
                NativeWindowUtilities.Activate(hwnd);
                return;
            }

            Thread.Sleep(50);
        }
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

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint FindWindow(string? className, string windowName);
}
