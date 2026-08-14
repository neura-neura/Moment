using System.Runtime.InteropServices;

namespace QuickCaptureBridgeWinUI;

/// <summary>
/// Native notification-area icon. It keeps the WinUI app independent from
/// WPF/WinForms and therefore avoids pulling an extra desktop UI framework into
/// the unpackaged installer.
/// </summary>
public sealed class TrayManager : IDisposable
{
    private const uint WmApp = 0x8000;
    private const uint WmCommand = 0x0111;
    private const uint WmDestroy = 0x0002;
    private const uint WmLButtonDblClk = 0x0203;
    private const uint WmRButtonUp = 0x0205;
    private const uint WmNull = 0x0000;
    private const uint NifMessage = 0x00000001;
    private const uint NifIcon = 0x00000002;
    private const uint NifTip = 0x00000004;
    private const uint NimAdd = 0x00000000;
    private const uint NimModify = 0x00000001;
    private const uint NimDelete = 0x00000002;
    private const uint ImageIcon = 1;
    private const uint LrLoadFromFile = 0x00000010;
    private const uint MfString = 0x00000000;
    private const uint TpmRightButton = 0x0002;
    private const uint TpmBottomAlign = 0x0020;
    private const uint TpmLeftAlign = 0x0000;
    private const uint NifInfo = 0x00000010;
    private const uint NiifInfo = 0x00000001;
    private const uint NiifWarning = 0x00000002;
    private const uint NiifError = 0x00000003;

    private static readonly WindowProc Proc = StaticWindowProc;
    private readonly string className = $"QuickCaptureBridgeTray-{Guid.NewGuid():N}";
    private readonly nint moduleHandle;
    private readonly nint hwnd;
    private readonly nint menu;
    private readonly nint icon;
    private bool disposed;

    public event Action? OpenRequested;
    public event Action? ExitRequested;

    public TrayManager()
    {
        moduleHandle = GetModuleHandle(null);
        var windowClass = new WindowClass
        {
            Size = (uint)Marshal.SizeOf<WindowClass>(),
            WindowProc = Marshal.GetFunctionPointerForDelegate(Proc),
            Instance = moduleHandle,
            ClassName = className
        };
        if (RegisterClassEx(ref windowClass) == 0)
            throw new InvalidOperationException("Could not create the notification tray window.");
        hwnd = CreateWindowEx(0, className, "Quick Capture Bridge tray", 0,
            0, 0, 0, 0, new nint(-3), nint.Zero, moduleHandle, nint.Zero);
        if (hwnd == nint.Zero) throw new InvalidOperationException("Could not create the notification tray host.");
        Instances[hwnd] = this;

        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
        icon = File.Exists(iconPath)
            ? LoadImage(nint.Zero, iconPath, ImageIcon, 32, 32, LrLoadFromFile)
            : LoadIcon(nint.Zero, new nint(32512));
        menu = CreatePopupMenu();
        AppendMenu(menu, MfString, 1, "Open Quick Capture Bridge");
        AppendMenu(menu, MfString, 2, "Exit");

        var data = new NotifyIconData
        {
            Size = (uint)Marshal.SizeOf<NotifyIconData>(),
            Window = hwnd,
            Id = 1,
            Flags = NifMessage | NifIcon | NifTip,
            CallbackMessage = WmApp + 1,
            Icon = icon,
            Tip = "Quick Capture Bridge"
        };
        ShellNotifyIcon(NimAdd, ref data);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        var data = new NotifyIconData { Size = (uint)Marshal.SizeOf<NotifyIconData>(), Window = hwnd, Id = 1, Tip = "", Info = "", InfoTitle = "" };
        ShellNotifyIcon(NimDelete, ref data);
        if (menu != nint.Zero) DestroyMenu(menu);
        if (icon != nint.Zero) DestroyIcon(icon);
        Instances.Remove(hwnd);
        if (hwnd != nint.Zero) DestroyWindow(hwnd);
        GC.KeepAlive(Proc);
    }

    public void ShowNotification(string title, string message, bool error = false)
    {
        if (disposed) return;
        var data = new NotifyIconData
        {
            Size = (uint)Marshal.SizeOf<NotifyIconData>(),
            Window = hwnd,
            Id = 1,
            Flags = NifInfo,
            Info = message.Length > 255 ? message[..255] : message,
            InfoTitle = title.Length > 63 ? title[..63] : title,
            InfoFlags = error ? NiifError : NiifInfo
        };
        ShellNotifyIcon(NimModify, ref data);
    }

    private void ShowMenu()
    {
        GetCursorPos(out var point);
        SetForegroundWindow(hwnd);
        TrackPopupMenu(menu, TpmLeftAlign | TpmBottomAlign | TpmRightButton,
            point.X, point.Y, 0, hwnd, nint.Zero);
        PostMessage(hwnd, WmNull, nint.Zero, nint.Zero);
    }

    private static nint StaticWindowProc(nint window, uint message, nint wParam, nint lParam)
    {
        var instance = Instances.FirstOrDefault(pair => pair.Value.hwnd == window).Value;
        if (instance is not null)
        {
            if (message == instance.WmTrayCallback)
            {
                var notification = unchecked((uint)lParam.ToInt64());
                if (notification == WmLButtonDblClk) instance.OpenRequested?.Invoke();
                else if (notification == WmRButtonUp) instance.ShowMenu();
            }
            else if (message == WmCommand)
            {
                switch (wParam.ToInt32() & 0xFFFF)
                {
                    case 1: instance.OpenRequested?.Invoke(); break;
                    case 2: instance.ExitRequested?.Invoke(); break;
                }
            }
        }
        return DefWindowProc(window, message, wParam, lParam);
    }

    private uint WmTrayCallback => WmApp + 1;
    private static readonly Dictionary<nint, TrayManager> Instances = new();

    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate nint WindowProc(nint window, uint message, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WindowClass
    {
        public uint Size;
        public uint Style;
        public nint WindowProc;
        public int ClassExtra;
        public int WindowExtra;
        public nint Instance;
        public nint Icon;
        public nint Cursor;
        public nint Background;
        [MarshalAs(UnmanagedType.LPWStr)] public string? MenuName;
        [MarshalAs(UnmanagedType.LPWStr)] public string? ClassName;
        public nint SmallIcon;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public uint Size;
        public nint Window;
        public uint Id;
        public uint Flags;
        public uint CallbackMessage;
        public nint Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
        public uint State;
        public uint StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
        public uint TimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string InfoTitle;
        public uint InfoFlags;
        public Guid Guid;
        public nint BalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X; public int Y; }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandle(string? name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern ushort RegisterClassEx(ref WindowClass windowClass);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern nint CreateWindowEx(uint exStyle, string className, string name, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool DestroyWindow(nint window);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool DestroyIcon(nint icon);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "Shell_NotifyIconW")] private static extern bool ShellNotifyIcon(uint message, ref NotifyIconData data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint LoadImage(nint instance, string name, uint type, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern nint LoadIcon(nint instance, nint name);
    [DllImport("user32.dll")] private static extern nint CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool AppendMenu(nint menu, uint flags, uint id, string text);
    [DllImport("user32.dll")] private static extern bool DestroyMenu(nint menu);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint window);
    [DllImport("user32.dll")] private static extern bool TrackPopupMenu(nint menu, uint flags, int x, int y, int reserved, nint owner, nint rect);
    [DllImport("user32.dll")] private static extern bool PostMessage(nint window, uint message, nint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern nint DefWindowProc(nint window, uint message, nint wParam, nint lParam);
}
