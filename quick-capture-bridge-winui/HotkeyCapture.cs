using Microsoft.UI.Xaml.Input;
using Windows.System;
using Windows.UI.Core;

namespace QuickCaptureBridgeWinUI;

public static class HotkeyCapture
{
    public static bool IsModifierDown(VirtualKey key)
    {
        var state = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(key);
        return (state & CoreVirtualKeyStates.Down) != 0;
    }

    public static HotkeyBinding FromKeyEvent(KeyRoutedEventArgs args)
    {
        var key = (uint)args.Key;
        if (key is (uint)VirtualKey.Menu or (uint)VirtualKey.Control or (uint)VirtualKey.Shift or (uint)VirtualKey.LeftWindows or (uint)VirtualKey.RightWindows)
            throw new InvalidOperationException("Choose a non-modifier key after pressing any modifiers.");

        var modifiers = 0u;
        if (IsModifierDown(VirtualKey.Menu)) modifiers |= (uint)NativeMessages.ModAlt;
        if (IsModifierDown(VirtualKey.Control)) modifiers |= (uint)NativeMessages.ModControl;
        if (IsModifierDown(VirtualKey.Shift)) modifiers |= (uint)NativeMessages.ModShift;
        if (IsModifierDown(VirtualKey.LeftWindows) || IsModifierDown(VirtualKey.RightWindows)) modifiers |= (uint)NativeMessages.ModWindows;
        return new HotkeyBinding { Modifiers = modifiers, VirtualKey = key };
    }
}
