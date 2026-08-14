using System.Text.Json;

namespace QuickCaptureBridge;

public sealed class VaultInbox
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private readonly BridgeSettings _settings;

    public VaultInbox(BridgeSettings settings) => _settings = settings;

    public bool IsConfigured => Directory.Exists(_settings.VaultPath);

    public string EnsureAudioPath(DateTimeOffset startedAt)
    {
        EnsureConfigured();
        var folder = SafeRelativeFolder(_settings.AudioFolder, "Voice Notes");
        var directory = Path.Combine(_settings.VaultPath, folder);
        Directory.CreateDirectory(directory);
        var stem = startedAt.ToLocalTime().ToString("yyyy-MM-dd HH-mm-ss-fff");
        var path = Path.Combine(directory, stem + ".wav");
        for (var suffix = 1; File.Exists(path); suffix++)
            path = Path.Combine(directory, $"{stem}-{suffix}.wav");
        return path;
    }

    public void WriteText(string text, DateTimeOffset timestamp)
    {
        EnsureConfigured();
        var job = new TextJob { Text = text.Trim(), CreatedAt = DateTimeOffset.Now.ToString("O"), Timestamp = timestamp.ToString("O") };
        var path = Path.Combine(EnsureInbox(), $"text-{job.Id}.json");
        WriteAtomic(path, job);
    }

    public void WriteVoice(string audioPath, DateTimeOffset startedAt)
    {
        EnsureConfigured();
        var relative = Path.GetRelativePath(_settings.VaultPath, audioPath).Replace(Path.DirectorySeparatorChar, '/');
        var job = new VoiceJob { AudioPath = relative, StartedAt = startedAt.ToString("O"), CreatedAt = DateTimeOffset.Now.ToString("O") };
        var path = Path.Combine(EnsureInbox(), $"voice-{job.Id}.json");
        WriteAtomic(path, job);
    }

    private string EnsureInbox()
    {
        var path = Path.Combine(_settings.VaultPath, ".quick-capture", "inbox");
        Directory.CreateDirectory(path);
        return path;
    }

    private void EnsureConfigured()
    {
        if (!IsConfigured) throw new InvalidOperationException("Choose an Obsidian vault in Quick Capture Bridge settings first.");
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
