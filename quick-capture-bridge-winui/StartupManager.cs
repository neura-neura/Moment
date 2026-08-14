using Microsoft.Win32;

namespace QuickCaptureBridgeWinUI;

public static class StartupManager
{
    private const string ValueName = "QuickCaptureBridgeWinUI";

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run", true)
            ?? Registry.CurrentUser.CreateSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run");
        if (enabled)
        {
            var executable = Environment.ProcessPath ?? throw new InvalidOperationException("The bridge executable path is unavailable.");
            key.SetValue(ValueName, $"\"{executable}\" --background");
        }
        else key.DeleteValue(ValueName, false);
    }
}
