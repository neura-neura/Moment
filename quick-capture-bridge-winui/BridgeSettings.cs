using System.Text.Json;

namespace QuickCaptureBridgeWinUI;

public sealed class BridgeSettings
{
    public string VaultPath { get; set; } = "";
    public HotkeyBinding VoiceHotkey { get; set; } = HotkeyBinding.F13;
    public HotkeyBinding TextHotkey { get; set; } = HotkeyBinding.F14;
    public bool StartWithWindows { get; set; }
    public bool StartMinimized { get; set; } = true;
    public bool CloseToTray { get; set; } = true;
    // Migrated Quick Daily Capture settings.
    public string DailyInsertionLocation { get; set; } = "end";
    public string DailyTargetHeading { get; set; } = "Notes";
    public string DailyMissingHeadingBehavior { get; set; } = "create";
    public string DailyTimestampFormat { get; set; } = "HH:mm";
    public bool IncludeTimestamp { get; set; } = true;
    public bool DailyCloseAfterSave { get; set; } = true;
    public bool DailyEnterToSave { get; set; } = true;

    // Migrated Quick Voice Notes settings.
    public string AudioFolder { get; set; } = "Voice Notes";
    public int AudioBitsPerSecond { get; set; } = 64_000;
    public bool EnableTranscription { get; set; }
    public string WhisperLanguage { get; set; } = "auto";
    public string WhisperModel { get; set; } = "base";
    public string TranscriptionFolder { get; set; } = "Voice Transcriptions";
    public bool IncludeAudioEmbed { get; set; } = true;
    public string TranscriptionDestination { get; set; } = "separate-note";
    public string VoicePrefix { get; set; } = "";
    public bool NativeProcessingEnabled { get; set; } = true;
    public bool PluginSettingsImported { get; set; }
}

public sealed class HotkeyBinding
{
    public uint Modifiers { get; set; }
    public uint VirtualKey { get; set; }

    public HotkeyBinding Clone() => new() { Modifiers = Modifiers, VirtualKey = VirtualKey };

    public static HotkeyBinding F13 => new() { VirtualKey = 0x7C };
    public static HotkeyBinding F14 => new() { VirtualKey = 0x7D };
}

public static class HotkeyFormatter
{
    public static string Format(HotkeyBinding binding)
    {
        var parts = new List<string>();
        if ((binding.Modifiers & NativeMessages.ModAlt) != 0) parts.Add("Alt");
        if ((binding.Modifiers & NativeMessages.ModControl) != 0) parts.Add("Ctrl");
        if ((binding.Modifiers & NativeMessages.ModShift) != 0) parts.Add("Shift");
        if ((binding.Modifiers & NativeMessages.ModWindows) != 0) parts.Add("Win");
        parts.Add(KeyName(binding.VirtualKey));
        return string.Join(" + ", parts);
    }

    public static string KeyName(uint key)
    {
        // Win32 VK_F1 starts at 0x70 and VK_F24 ends at 0x87.
        if (key >= 0x70 && key <= 0x87) return $"F{key - 0x6F}";
        if (key >= 0x30 && key <= 0x39) return ((char)key).ToString();
        if (key >= 0x41 && key <= 0x5A) return ((char)key).ToString();
        return key switch
        {
            0x20 => "Space",
            0x09 => "Tab",
            0x0D => "Enter",
            0x1B => "Escape",
            0x08 => "Backspace",
            0x2D => "Insert",
            0x2E => "Delete",
            0x21 => "Page Up",
            0x22 => "Page Down",
            0x24 => "Home",
            0x23 => "End",
            0x25 => "Left",
            0x26 => "Up",
            0x27 => "Right",
            0x28 => "Down",
            0xBA => ";",
            0xBB => "=",
            0xBC => ",",
            0xBD => "-",
            0xBE => ".",
            0xBF => "/",
            0xC0 => "`",
            0xDB => "[",
            0xDC => "\\",
            0xDD => "]",
            0xDE => "'",
            _ => $"VK 0x{key:X2}"
        };
    }
}

public sealed class SettingsStore
{
    private readonly string path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "QuickCaptureBridge",
        "settings.json");

    public BridgeSettings Load()
    {
        try
        {
            if (!File.Exists(path)) return new BridgeSettings();
            return JsonSerializer.Deserialize<BridgeSettings>(File.ReadAllText(path), new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? new BridgeSettings();
        }
        catch
        {
            return new BridgeSettings();
        }
    }

    public void Save(BridgeSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(settings, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        }));
        File.Move(temporary, path, true);
    }
}
