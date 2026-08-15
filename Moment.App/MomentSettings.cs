using System.Text.Json;

namespace Moment;

public sealed class MomentSettings
{
    public string WorkspacePath { get; set; } = "";
    public HotkeyBinding VoiceHotkey { get; set; } = HotkeyBinding.F13;
    public HotkeyBinding TextHotkey { get; set; } = HotkeyBinding.F14;
    public bool StartWithWindows { get; set; }
    public bool StartMinimized { get; set; } = true;
    public bool CloseToTray { get; set; } = true;
    public string RecurringNoteInsertionLocation { get; set; } = "end";
    public string RecurringNoteTargetHeading { get; set; } = "Notes";
    public string RecurringNoteMissingHeadingBehavior { get; set; } = "create";
    public string RecurringNoteTimestampFormat { get; set; } = "HH:mm";
    public string RecurringNoteFilenameFormat { get; set; } = "YYYY-MM-DD";
    public string RecurringNoteFilenamePrefix { get; set; } = "";
    public string RecurringNoteFolder { get; set; } = "";
    public bool IncludeTimestamp { get; set; } = true;
    public bool RecurringNoteCloseAfterSave { get; set; } = true;
    public bool RecurringNoteEnterToSave { get; set; } = true;

    public string AudioInputDevice { get; set; } = "default";
    public string AudioFolder { get; set; } = "Voice Notes";
    public string VoiceFilenameFormat { get; set; } = "YYYY-MM-DD HH-mm-ss-SSS";
    public string VoiceFilenamePrefix { get; set; } = "";
    public int AudioBitsPerSecond { get; set; } = 64_000;
    public bool EnableTranscription { get; set; }
    public string WhisperLanguage { get; set; } = "auto";
    public string WhisperModel { get; set; } = "base";
    public string TranscriptionFolder { get; set; } = "Voice Transcriptions";
    public string TranscriptionFilenameFormat { get; set; } = "YYYY-MM-DD HH-mm-ss-SSS";
    public string TranscriptionFilenamePrefix { get; set; } = "";
    public bool IncludeAudioEmbed { get; set; } = true;
    public string TranscriptionDestination { get; set; } = "separate-note";
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
        "Moment",
        "settings.json");
    public MomentSettings Load()
    {
        try
        {
            if (!File.Exists(path)) return new MomentSettings();
            var json = File.ReadAllText(path);
            var settings = JsonSerializer.Deserialize<MomentSettings>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? new MomentSettings();

            // Preserve the current user's settings while the persisted names
            // move from the old bridge vocabulary to Moment's workspace and
            // recurring-note vocabulary. This is intentionally limited to
            // the existing Moment settings file; plugin settings are not read.
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            settings.WorkspacePath = ReadString(root, settings.WorkspacePath, "workspacePath", "vaultPath");
            settings.RecurringNoteInsertionLocation = ReadString(root, settings.RecurringNoteInsertionLocation, "recurringNoteInsertionLocation", "dailyInsertionLocation");
            settings.RecurringNoteTargetHeading = ReadString(root, settings.RecurringNoteTargetHeading, "recurringNoteTargetHeading", "dailyTargetHeading");
            settings.RecurringNoteMissingHeadingBehavior = ReadString(root, settings.RecurringNoteMissingHeadingBehavior, "recurringNoteMissingHeadingBehavior", "dailyMissingHeadingBehavior");
            settings.RecurringNoteTimestampFormat = ReadString(root, settings.RecurringNoteTimestampFormat, "recurringNoteTimestampFormat", "dailyTimestampFormat");
            settings.RecurringNoteFilenameFormat = ReadString(root, settings.RecurringNoteFilenameFormat, "recurringNoteFilenameFormat", "dailyFilenameFormat");
            settings.RecurringNoteFilenamePrefix = ReadString(root, settings.RecurringNoteFilenamePrefix, "recurringNoteFilenamePrefix", "dailyFilenamePrefix");
            settings.RecurringNoteCloseAfterSave = ReadBool(root, settings.RecurringNoteCloseAfterSave, "recurringNoteCloseAfterSave", "dailyCloseAfterSave");
            settings.RecurringNoteEnterToSave = ReadBool(root, settings.RecurringNoteEnterToSave, "recurringNoteEnterToSave", "dailyEnterToSave");
            if (string.Equals(settings.TranscriptionDestination, "daily-note", StringComparison.OrdinalIgnoreCase))
                settings.TranscriptionDestination = "recurring-note";
            return settings;
        }
        catch
        {
            return new MomentSettings();
        }
    }

    public void Save(MomentSettings settings)
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

    private static string ReadString(JsonElement root, string fallback, params string[] names)
    {
        foreach (var name in names)
            if (root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
                return value.GetString()?.Trim() ?? fallback;
        return fallback;
    }

    private static bool ReadBool(JsonElement root, bool fallback, params string[] names)
    {
        foreach (var name in names)
            if (root.TryGetProperty(name, out var value) && (value.ValueKind is JsonValueKind.True or JsonValueKind.False))
                return value.GetBoolean();
        return fallback;
    }
}
