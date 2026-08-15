using System.Text.Json;

namespace Moment;

public sealed class CaptureStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly MomentSettings settings;

    public CaptureStore(MomentSettings settings) => this.settings = settings;

    public string WorkspacePath => settings.WorkspacePath;
    public bool IsConfigured => Directory.Exists(settings.WorkspacePath);

    public string EnsureAudioPath(DateTimeOffset startedAt)
    {
        EnsureConfigured();
        var directory = global::Moment.WorkspacePath.ResolveConfiguredFolder(settings.WorkspacePath, settings.AudioFolder, "Voice Notes");
        Directory.CreateDirectory(directory);
        var stem = MomentFilename.Format(startedAt, settings.VoiceFilenameFormat, settings.VoiceFilenamePrefix);
        var path = Path.Combine(directory, stem + ".webm");
        for (var suffix = 1; File.Exists(path); suffix++) path = Path.Combine(directory, $"{stem}-{suffix}.webm");
        return path;
    }

    public string WriteText(string text, DateTimeOffset timestamp)
    {
        EnsureConfigured();
        return new RecurringNoteService(settings).WriteCapture(text, timestamp);
    }

    public string WriteVoice(string audioPath, DateTimeOffset startedAt)
    {
        EnsureConfigured();
        var job = new VoiceJob
        {
            AudioPath = Path.GetFullPath(audioPath),
            StartedAt = startedAt.ToString("O"),
            CreatedAt = DateTimeOffset.Now.ToString("O")
        };
        var directory = EnsurePending();
        var path = Path.Combine(directory, $"voice-{job.Id}.json");
        WriteAtomic(path, job);
        return Path.GetFullPath(path);
    }

    private string EnsurePending()
    {
        var pending = CaptureQueuePaths.Pending(settings.WorkspacePath);
        Directory.CreateDirectory(pending);
        return pending;
    }

    private void EnsureConfigured()
    {
        if (!IsConfigured) throw new InvalidOperationException("Choose an existing workspace folder before capturing.");
    }

    private static void WriteAtomic<T>(string path, T value)
    {
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(value, JsonOptions));
        File.Move(temporary, path, true);
    }
}

internal static class CaptureQueuePaths
{
    private static string Root(string workspace) => Path.Combine(workspace, ".moment", "capture");

    public static string Pending(string workspace) => Path.Combine(Root(workspace), "pending");
    public static string Completed(string workspace) => Path.Combine(Root(workspace), "completed");
    public static string Failed(string workspace) => Path.Combine(Root(workspace), "failed");

    public static void MigratePreviousQueue(string workspace)
    {
        if (string.IsNullOrWhiteSpace(workspace) || !Directory.Exists(workspace)) return;
        try
        {
            var previousRoot = Path.Combine(workspace, ".quick-capture");
            MoveDirectory(Path.Combine(previousRoot, "bridge-inbox"), Pending(workspace));
            MoveDirectory(Path.Combine(previousRoot, "bridge-processed"), Completed(workspace));
            MoveDirectory(Path.Combine(previousRoot, "bridge-failed"), Failed(workspace));
            if (Directory.Exists(previousRoot) && !Directory.EnumerateFileSystemEntries(previousRoot).Any()) Directory.Delete(previousRoot);
        }
        catch
        {
            // A locked previous queue must never prevent Moment from starting.
        }
    }

    private static void MoveDirectory(string source, string destination)
    {
        if (!Directory.Exists(source)) return;
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.TopDirectoryOnly))
        {
            var target = Path.Combine(destination, Path.GetFileName(file));
            if (File.Exists(target))
            {
                var stem = Path.GetFileNameWithoutExtension(target);
                var extension = Path.GetExtension(target);
                for (var suffix = 1; ; suffix++)
                {
                    var candidate = Path.Combine(destination, $"{stem}-{suffix}{extension}");
                    if (!File.Exists(candidate)) { target = candidate; break; }
                }
            }
            File.Move(file, target);
        }
        if (!Directory.EnumerateFileSystemEntries(source).Any()) Directory.Delete(source);
    }
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
