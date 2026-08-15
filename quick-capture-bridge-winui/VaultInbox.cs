using System.Text.Json;

namespace Moment;

public sealed class VaultInbox
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly BridgeSettings settings;

    public VaultInbox(BridgeSettings settings) => this.settings = settings;

    public string VaultPath => settings.VaultPath;
    public bool IsConfigured => Directory.Exists(settings.VaultPath);

    public string EnsureAudioPath(DateTimeOffset startedAt)
    {
        EnsureConfigured();
        var folder = SafeRelativeFolder(settings.AudioFolder, "Voice Notes");
        var directory = Path.Combine(settings.VaultPath, folder);
        Directory.CreateDirectory(directory);
        var stem = MomentFilename.Format(startedAt, settings.VoiceFilenameFormat, settings.VoiceFilenamePrefix);
        var path = Path.Combine(directory, stem + ".webm");
        for (var suffix = 1; File.Exists(path); suffix++) path = Path.Combine(directory, $"{stem}-{suffix}.webm");
        return path;
    }

    public string WriteText(string text, DateTimeOffset timestamp)
    {
        EnsureConfigured();
        if (settings.NativeProcessingEnabled)
        {
            return new NativeDailyNoteService(settings).WriteCapture(text, timestamp);
        }

        var job = new TextJob
        {
            Text = text.Trim(),
            Timestamp = timestamp.ToString("O"),
            CreatedAt = DateTimeOffset.Now.ToString("O")
        };
        WriteAtomic(Path.Combine(EnsureInbox(false), $"text-{job.Id}.json"), job);
        return Path.Combine(".quick-capture", "inbox", $"text-{job.Id}.json").Replace(Path.DirectorySeparatorChar, '/');
    }

    public string WriteVoice(string audioPath, DateTimeOffset startedAt)
    {
        EnsureConfigured();
        var relative = Path.GetRelativePath(settings.VaultPath, audioPath).Replace(Path.DirectorySeparatorChar, '/');
        var job = new VoiceJob
        {
            AudioPath = relative,
            StartedAt = startedAt.ToString("O"),
            CreatedAt = DateTimeOffset.Now.ToString("O")
        };
        var directory = EnsureInbox(settings.NativeProcessingEnabled);
        var path = Path.Combine(directory, $"voice-{job.Id}.json");
        WriteAtomic(path, job);
        return Path.GetRelativePath(settings.VaultPath, path).Replace(Path.DirectorySeparatorChar, '/');
    }

    private string EnsureInbox(bool native)
    {
        var inbox = Path.Combine(settings.VaultPath, ".quick-capture", native ? "bridge-inbox" : "inbox");
        Directory.CreateDirectory(inbox);
        return inbox;
    }

    private void EnsureConfigured()
    {
        if (!IsConfigured) throw new InvalidOperationException("Choose an existing Obsidian vault before capturing.");
    }

    private static string SafeRelativeFolder(string value, string fallback)
    {
        var candidate = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim().Trim('\\', '/');
        if (Path.IsPathRooted(candidate) || candidate.Split(new[] { '/', '\\' }).Any(part => part == ".."))
            throw new InvalidOperationException("Audio folder must be relative to the selected vault.");
        return candidate.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
    }

    private static void WriteAtomic<T>(string path, T value)
    {
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(value, JsonOptions));
        File.Move(temporary, path, true);
    }
}

public sealed class TextJob
{
    public int Version { get; set; } = 1;
    public string Type { get; set; } = "text";
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string CreatedAt { get; set; } = DateTimeOffset.Now.ToString("O");
    public string Timestamp { get; set; } = DateTimeOffset.Now.ToString("O");
    public string Text { get; set; } = "";
    public string Source { get; set; } = "moment";
}

public sealed class VoiceJob
{
    public int Version { get; set; } = 1;
    public string Type { get; set; } = "voice";
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string CreatedAt { get; set; } = DateTimeOffset.Now.ToString("O");
    public string StartedAt { get; set; } = DateTimeOffset.Now.ToString("O");
    public string AudioPath { get; set; } = "";
    public string MimeType { get; set; } = "audio/webm;codecs=opus";
}
