namespace QuickCaptureBridge;

public sealed class BridgeSettings
{
    public string VaultPath { get; set; } = "";
    public int VoiceHotkey { get; set; } = 0x7C; // F13
    public int TextHotkey { get; set; } = 0x7D; // F14
    public bool StartWithWindows { get; set; }
    public string AudioFolder { get; set; } = "Voice Notes";
}

public sealed class TextJob
{
    public int Version { get; set; } = 1;
    public string Type { get; set; } = "text";
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string CreatedAt { get; set; } = DateTimeOffset.Now.ToString("O");
    public string Timestamp { get; set; } = DateTimeOffset.Now.ToString("O");
    public string Text { get; set; } = "";
    public string Source { get; set; } = "quick-capture-bridge";
}

public sealed class VoiceJob
{
    public int Version { get; set; } = 1;
    public string Type { get; set; } = "voice";
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string CreatedAt { get; set; } = DateTimeOffset.Now.ToString("O");
    public string StartedAt { get; set; } = DateTimeOffset.Now.ToString("O");
    public string AudioPath { get; set; } = "";
    public string MimeType { get; set; } = "audio/wav";
}

public sealed class HotkeyOption
{
    public HotkeyOption(string name, int code)
    {
        Name = name;
        Code = code;
    }

    public string Name { get; }
    public int Code { get; }
}

public static class HotkeyOptions
{
    public static readonly IReadOnlyList<HotkeyOption> All = Enumerable.Range(13, 12)
        .Select(index => new HotkeyOption($"F{index}", 0x6F + index))
        .ToArray();
}
