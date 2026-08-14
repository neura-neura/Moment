using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Moment;

/// <summary>
/// Owns the message-only window used by RegisterHotKey and a low-level
/// keyboard hook for F13-F24. Stream Deck firmware and keyboard drivers can
/// emit those extended function keys in a way that does not reach
/// RegisterHotKey reliably, so the hook is the primary path for them.
/// </summary>
public sealed class NativeHotkeyWindow : IDisposable
{
    private const uint WmHotkey = 0x0312;
    private const uint WmNcDestroy = 0x0082;
    private const int HwndMessage = -3;
    private const int ModNoRepeat = 0x4000;
    private const int WhKeyboardLl = 13;
    private const uint WmKeyDown = 0x0100;
    private const uint WmSysKeyDown = 0x0104;
    private const uint WmKeyUp = 0x0101;
    private const uint WmSysKeyUp = 0x0105;
    private const uint LlkhfUp = 0x0080;
    private const long LowLevelDebounceMilliseconds = 250;

    private static readonly NativeWindowProc WindowProc = StaticWindowProc;
    private static readonly LowLevelKeyboardProc KeyboardProc = StaticKeyboardProc;
    private static readonly Dictionary<nint, NativeHotkeyWindow> Instances = new();
    private static NativeHotkeyWindow? activeInstance;

    private readonly string className = $"MomentHotkeys-{Guid.NewGuid():N}";
    private readonly nint moduleHandle;
    private readonly HashSet<uint> lowLevelKeysDown = new();
    private readonly Dictionary<uint, long> lowLevelKeyTimes = new();
    private nint keyboardHook;
    private nint hwnd;
    private HotkeyBinding? lowLevelVoice;
    private HotkeyBinding? lowLevelText;
    private bool disposed;

    public event Action<int>? HotkeyPressed;

    public NativeHotkeyWindow()
    {
        moduleHandle = GetModuleHandle(null);
        if (moduleHandle == nint.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not resolve the bridge module handle.");

        var windowClass = new WindowClass
        {
            Size = (uint)Marshal.SizeOf<WindowClass>(),
            WindowProc = Marshal.GetFunctionPointerForDelegate(WindowProc),
            Instance = moduleHandle,
            ClassName = className
        };
        if (RegisterClassEx(ref windowClass) == 0)
        {
            var registerError = Marshal.GetLastWin32Error();
            throw new Win32Exception(registerError, $"Could not create the native hotkey window class '{className}'.");
        }

        hwnd = CreateWindowEx(0, className, "Moment hotkeys", 0,
            0, 0, 0, 0, new nint(HwndMessage), nint.Zero, moduleHandle, nint.Zero);
        if (hwnd == nint.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not create the native hotkey window.");
        Instances[hwnd] = this;
        activeInstance = this;

        // This hook is deliberately best-effort. RegisterHotKey remains the
        // fallback for ordinary combinations if Windows denies the hook.
        keyboardHook = SetWindowsHookEx(WhKeyboardLl, KeyboardProc, moduleHandle, 0);
    }

    public nint Handle => hwnd;

    public bool Register(int id, HotkeyBinding binding, out string error)
    {
        ClearLowLevelBinding(id);
        lowLevelKeysDown.Remove(binding.VirtualKey);
        lowLevelKeyTimes.Remove(binding.VirtualKey);
        if (IsExtendedFunctionKey(binding.VirtualKey) && keyboardHook != nint.Zero)
        {
            if (id == 1) lowLevelVoice = binding.Clone();
            else if (id == 2) lowLevelText = binding.Clone();
            error = "";
            return true;
        }

        if (User32RegisterHotKey(hwnd, id, binding.Modifiers | ModNoRepeat, binding.VirtualKey))
        {
            error = "";
            return true;
        }

        var code = Marshal.GetLastWin32Error();
        error = code == 1409
            ? "This shortcut is already registered by another application."
            : new Win32Exception(code).Message;
        return false;
    }

    public void Unregister(int id)
    {
        User32UnregisterHotKey(hwnd, id);
        ClearLowLevelBinding(id);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (keyboardHook != nint.Zero)
        {
            UnhookWindowsHookEx(keyboardHook);
            keyboardHook = nint.Zero;
        }
        lowLevelVoice = null;
        lowLevelText = null;
        lowLevelKeysDown.Clear();
        lowLevelKeyTimes.Clear();
        if (hwnd != nint.Zero)
        {
            User32UnregisterHotKey(hwnd, 1);
            User32UnregisterHotKey(hwnd, 2);
            Instances.Remove(hwnd);
            if (ReferenceEquals(activeInstance, this)) activeInstance = null;
            DestroyWindow(hwnd);
            hwnd = nint.Zero;
        }
        GC.KeepAlive(WindowProc);
        GC.KeepAlive(KeyboardProc);
    }

    private void ClearLowLevelBinding(int id)
    {
        if (id == 1) lowLevelVoice = null;
        if (id == 2) lowLevelText = null;
    }

    private static bool IsExtendedFunctionKey(uint key) => key >= 0x7C && key <= 0x87;

    private static nint StaticWindowProc(nint window, uint message, nint wParam, nint lParam)
    {
        if (Instances.TryGetValue(window, out var instance) && message == WmHotkey)
            instance.HotkeyPressed?.Invoke(wParam.ToInt32());
        if (message == WmNcDestroy) Instances.Remove(window);
        return DefWindowProc(window, message, wParam, lParam);
    }

    private static nint StaticKeyboardProc(int code, nint wParam, nint lParam)
    {
        var instance = activeInstance;
        if (instance is not null && code >= 0)
        {
            var message = unchecked((uint)wParam.ToInt64());
            var data = Marshal.PtrToStructure<KeyboardInput>(lParam);
            var isKeyUp = message is WmKeyUp or WmSysKeyUp || (data.Flags & LlkhfUp) != 0;
            if (isKeyUp)
            {
                instance.lowLevelKeysDown.Remove(data.VirtualKey);
            }
            else if (message is WmKeyDown or WmSysKeyDown)
            {
                var now = Environment.TickCount64;
                var repeatedTooSoon = instance.lowLevelKeyTimes.TryGetValue(data.VirtualKey, out var last) &&
                                       now - last < LowLevelDebounceMilliseconds;
                if (repeatedTooSoon) return CallNextHookEx(nint.Zero, code, wParam, lParam);
                instance.lowLevelKeysDown.Add(data.VirtualKey);
                instance.lowLevelKeyTimes[data.VirtualKey] = now;
                var modifiers = CurrentModifiers();
                if (Matches(instance.lowLevelVoice, data.VirtualKey, modifiers))
                    instance.HotkeyPressed?.Invoke(1);
                else if (Matches(instance.lowLevelText, data.VirtualKey, modifiers))
                    instance.HotkeyPressed?.Invoke(2);
            }
        }

        return CallNextHookEx(nint.Zero, code, wParam, lParam);
    }

    private static bool Matches(HotkeyBinding? binding, uint key, uint modifiers) =>
        binding is not null && binding.VirtualKey == key && binding.Modifiers == modifiers;

    private static uint CurrentModifiers()
    {
        var modifiers = 0u;
        if (IsKeyDown(0x12)) modifiers |= (uint)NativeMessages.ModAlt;
        if (IsKeyDown(0x11)) modifiers |= (uint)NativeMessages.ModControl;
        if (IsKeyDown(0x10)) modifiers |= (uint)NativeMessages.ModShift;
        if (IsKeyDown(0x5B) || IsKeyDown(0x5C)) modifiers |= (uint)NativeMessages.ModWindows;
        return modifiers;
    }

    private static bool IsKeyDown(int virtualKey) => (GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate nint NativeWindowProc(nint window, uint message, nint wParam, nint lParam);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate nint LowLevelKeyboardProc(int code, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public uint VirtualKey;
        public uint ScanCode;
        public uint Flags;
        public uint Time;
        public nint ExtraInfo;
    }

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

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandle(string? moduleName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassEx(ref WindowClass windowClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateWindowEx(uint extendedStyle, string className, string windowName, uint style,
        int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);

    [DllImport("user32.dll", SetLastError = true)] private static extern bool DestroyWindow(nint window);
    [DllImport("user32.dll", SetLastError = true, EntryPoint = "RegisterHotKey")] private static extern bool User32RegisterHotKey(nint window, int id, uint modifiers, uint virtualKey);
    [DllImport("user32.dll", SetLastError = true, EntryPoint = "UnregisterHotKey")] private static extern bool User32UnregisterHotKey(nint window, int id);
    [DllImport("user32.dll")] private static extern nint DefWindowProc(nint window, uint message, nint wParam, nint lParam);
    [DllImport("user32.dll", SetLastError = true)] private static extern nint SetWindowsHookEx(int hookType, LowLevelKeyboardProc callback, nint module, uint threadId);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")] private static extern nint CallNextHookEx(nint hook, int code, nint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int virtualKey);
}

public static class NativeWindowUtilities
{
    private static readonly nint HwndTopmost = new(-1);
    private static readonly nint HwndNotTopmost = new(-2);
    private const int GwlStyle = -16;
    private const int GwlExStyle = -20;
    private const long WsCaption = 0x00C00000L;
    private const long WsThickFrame = 0x00040000L;
    private const long WsSysMenu = 0x00080000L;
    private const long WsMinimizeBox = 0x00020000L;
    private const long WsMaximizeBox = 0x00010000L;
    private const long WsExToolWindow = 0x00000080L;
    private const long WsExNoActivate = 0x08000000L;
    private const uint SwpFrameChanged = 0x0020;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowWindow = 0x0040;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoZOrder = 0x0004;
    private const int SwRestore = 9;
    private const int SwShow = 5;
    private const int SwHide = 0;
    private const int SwMinimize = 6;
    private const int SmCxScreen = 0;
    private const int SmCyScreen = 1;

    public static void MakeTopmost(nint hwnd, int width, int height)
    {
        ConfigureOverlay(hwnd, width, height, OverlayPlacement.CenterTop, false);
    }

    public static void ResizeAndCenter(nint hwnd, int width, int height)
    {
        if (hwnd == nint.Zero) return;
        var x = Math.Max(0, (GetSystemMetrics(SmCxScreen) - width) / 2);
        var y = Math.Max(0, (GetSystemMetrics(SmCyScreen) - height) / 2);
        SetWindowPos(hwnd, nint.Zero, x, y, width, height, SwpShowWindow);
    }

    public static void ConfigureOverlay(nint hwnd, int width, int height, OverlayPlacement placement, bool activate)
    {
        if (hwnd == nint.Zero) return;
        var style = GetWindowLongPtr(hwnd, GwlStyle).ToInt64();
        style &= ~(WsCaption | WsThickFrame | WsSysMenu | WsMinimizeBox | WsMaximizeBox);
        SetWindowLongPtr(hwnd, GwlStyle, new nint(style));
        var extended = GetWindowLongPtr(hwnd, GwlExStyle).ToInt64() | WsExToolWindow;
        if (!activate) extended |= WsExNoActivate;
        else extended &= ~WsExNoActivate;
        SetWindowLongPtr(hwnd, GwlExStyle, new nint(extended));
        SetDefaultWindowCorners(hwnd);

        var workArea = GetPrimaryWorkArea();
        var screenWidth = workArea.Right - workArea.Left;
        var screenHeight = workArea.Bottom - workArea.Top;
        var x = placement is OverlayPlacement.CenterTop or OverlayPlacement.Center
            ? workArea.Left + Math.Max(0, (screenWidth - width) / 2)
            : Math.Max(workArea.Left, workArea.Right - width - 28);
        var y = placement == OverlayPlacement.CenterTop
            ? workArea.Top + 28
            : placement == OverlayPlacement.Center
                ? workArea.Top + Math.Max(0, (screenHeight - height) / 2)
                : Math.Max(workArea.Top, workArea.Bottom - height - 20);
        SetWindowPos(hwnd, HwndTopmost, x, y, width, height,
            SwpFrameChanged | SwpShowWindow | (activate ? 0u : SwpNoActivate));
        // Keep the HWND rectangular and let DWM apply the normal Windows corner
        // treatment. No hand-built Win32 region or custom capsule geometry is
        // used, so both overlays follow the native window clipping behavior.
    }

    public static void Minimize(nint hwnd)
    {
        if (hwnd != nint.Zero) ShowWindow(hwnd, SwMinimize);
    }

    public static void RestoreAndActivate(nint hwnd)
    {
        if (hwnd == nint.Zero) return;
        ShowWindow(hwnd, SwRestore);
        SetWindowPos(hwnd, HwndNotTopmost, 0, 0, 0, 0, SwpNoZOrder | SwpNoActivate | SwpNoMove | SwpNoSize);
        SetForegroundWindow(hwnd);
    }

    public static void Activate(nint hwnd)
    {
        if (hwnd == nint.Zero) return;
        ShowWindow(hwnd, SwShow);
        var foreground = GetForegroundWindow();
        var currentThread = GetCurrentThreadId();
        var foregroundThread = foreground == nint.Zero ? 0u : GetWindowThreadProcessId(foreground, out _);
        var attached = foregroundThread != 0 && foregroundThread != currentThread && AttachThreadInput(currentThread, foregroundThread, true);
        try
        {
            BringWindowToTop(hwnd);
            SetForegroundWindow(hwnd);
            SetActiveWindow(hwnd);
            SetFocus(hwnd);
        }
        finally
        {
            if (attached) AttachThreadInput(currentThread, foregroundThread, false);
        }
    }

    public static void Hide(nint hwnd)
    {
        if (hwnd != nint.Zero) ShowWindow(hwnd, SwHide);
    }

    private static void SetDefaultWindowCorners(nint hwnd)
    {
        // DWMWCP_ROUND asks Windows 11 to apply the same native corner radius
        // used by ordinary app windows. Do not replace it with a Win32 region.
        var preference = 2;
        _ = DwmSetWindowAttribute(hwnd, DwmwaWindowCornerPreference, ref preference, sizeof(int));
    }

    private static WindowRect GetPrimaryWorkArea()
    {
        var area = new WindowRect();
        return SystemParametersInfo(SpiGetWorkArea, 0, ref area, 0) ? area : new WindowRect
        {
            Right = GetSystemMetrics(SmCxScreen),
            Bottom = GetSystemMetrics(SmCyScreen)
        };
    }

    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowPos(nint window, nint insertAfter, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)] private static extern nint GetWindowLongPtr(nint window, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)] private static extern nint SetWindowLongPtr(nint window, int index, nint value);
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint window, int command);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint window);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint processId);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint sourceThread, uint targetThread, bool attach);
    [DllImport("user32.dll")] private static extern bool BringWindowToTop(nint window);
    [DllImport("user32.dll")] private static extern nint SetActiveWindow(nint window);
    [DllImport("user32.dll")] private static extern nint SetFocus(nint window);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    private const int DwmwaWindowCornerPreference = 33;
    private const uint SpiGetWorkArea = 0x0030;
    [StructLayout(LayoutKind.Sequential)] private struct WindowRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SystemParametersInfo(uint action, uint parameter, ref WindowRect value, uint flags);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int valueSize);
}

public enum OverlayPlacement
{
    CenterTop,
    BottomRight,
    Center
}
