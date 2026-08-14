using Microsoft.Win32;

namespace QuickCaptureBridge;

public static class StartupManager
{
    private const string ValueName = "QuickCaptureBridge";

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(
            "Software\\Microsoft\\Windows\\CurrentVersion\\Run", writable: true)
            ?? Registry.CurrentUser.CreateSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run");
        if (enabled)
        {
            var executable = Environment.ProcessPath ?? throw new InvalidOperationException("The bridge executable path is unavailable.");
            key.SetValue(ValueName, $"\"{executable}\" --background");
        }
        else
        {
            key.DeleteValue(ValueName, false);
        }
    }
}
