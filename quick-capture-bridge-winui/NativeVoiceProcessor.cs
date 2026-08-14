using System.Text;
using System.Text.Json;

namespace QuickCaptureBridgeWinUI;

/// <summary>
/// Owns the voice-note side of the bridge. Jobs are kept on disk so closing
/// the bridge or restarting Windows cannot lose a recording while Whisper is
/// running. The Obsidian plugins are not involved in this path.
/// </summary>
public sealed class NativeVoiceProcessor : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly NativeWhisperEngine whisper = new();
    private readonly SemaphoreSlim signal = new(0);
    private readonly CancellationTokenSource cancellation = new();
    private readonly object settingsLock = new();
    private BridgeSettings settings;
    private Task worker;
    private int disposed;

    public event Action<string, bool>? StatusChanged;

    public NativeVoiceProcessor(BridgeSettings initialSettings)
    {
        settings = initialSettings;
        worker = Task.Run(ProcessLoopAsync);
        Schedule();
    }

    public void UpdateSettings(BridgeSettings next)
    {
        lock (settingsLock) settings = next;
        Schedule();
    }

    public void Schedule()
    {
        if (Volatile.Read(ref disposed) != 0) return;
        try { signal.Release(); } catch (ObjectDisposedException) { }
    }

    public int RetryFailedJobs()
    {
        var current = SnapshotSettings();
        if (!current.NativeProcessingEnabled || string.IsNullOrWhiteSpace(current.VaultPath)) return 0;
        var failed = Path.Combine(current.VaultPath, ".quick-capture", "bridge-failed");
        if (!Directory.Exists(failed)) return 0;
        var inbox = Path.Combine(current.VaultPath, ".quick-capture", "bridge-inbox");
        Directory.CreateDirectory(inbox);
        var count = 0;
        foreach (var job in Directory.EnumerateFiles(failed, "*.json", SearchOption.TopDirectoryOnly))
        {
            var destination = UniqueMovePath(inbox, Path.GetFileName(job));
            try
            {
                File.Move(job, destination);
                var errorSidecar = $"{job}.error.txt";
                if (File.Exists(errorSidecar)) File.Delete(errorSidecar);
                count++;
            }
            catch { }
        }
        if (count > 0) Schedule();
        return count;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        cancellation.Cancel();
        try { signal.Release(); } catch (ObjectDisposedException) { }
        try { worker.GetAwaiter().GetResult(); } catch (OperationCanceledException) { }
        catch { /* shutdown must never prevent the bridge from closing */ }
        signal.Dispose();
        cancellation.Dispose();
    }

    private async Task ProcessLoopAsync()
    {
        try
        {
            while (!cancellation.IsCancellationRequested)
            {
                await signal.WaitAsync(cancellation.Token);
                while (!cancellation.IsCancellationRequested && await ProcessAvailableAsync(cancellation.Token)) { }
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
    }

    private async Task<bool> ProcessAvailableAsync(CancellationToken token)
    {
        var current = SnapshotSettings();
        if (!current.NativeProcessingEnabled || string.IsNullOrWhiteSpace(current.VaultPath) || !Directory.Exists(current.VaultPath)) return false;
        var inbox = Path.Combine(current.VaultPath, ".quick-capture", "bridge-inbox");
        var voiceJobs = Directory.Exists(inbox)
            ? Directory.EnumerateFiles(inbox, "voice-*.json", SearchOption.TopDirectoryOnly).ToList()
            : new List<string>();
        var textJobs = new List<string>();
        if (CanTakeLegacyVoiceJobs(current))
            voiceJobs.AddRange(LegacyJobFiles(current.VaultPath, "voice-*.json"));
        if (CanTakeLegacyTextJobs(current))
            textJobs.AddRange(LegacyJobFiles(current.VaultPath, "text-*.json"));
        var jobs = voiceJobs.Concat(textJobs).OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
        if (jobs.Length == 0) return false;
        foreach (var path in jobs)
        {
            token.ThrowIfCancellationRequested();
            if (Path.GetFileName(path).StartsWith("text-", StringComparison.OrdinalIgnoreCase))
                await ProcessTextJobAsync(path, current, token);
            else
                await ProcessJobAsync(path, current, token);
        }
        return true;
    }

    private async Task ProcessJobAsync(string jobPath, BridgeSettings current, CancellationToken token)
    {
        VoiceJob? job;
        try
        {
            job = JsonSerializer.Deserialize<VoiceJob>(await File.ReadAllTextAsync(jobPath, token), JsonOptions);
            if (job is null || string.IsNullOrWhiteSpace(job.AudioPath)) throw new InvalidOperationException("The voice job is missing its audio path.");
        }
        catch (Exception error)
        {
            await MoveToFailureAsync(jobPath, current.VaultPath, $"Invalid voice job: {error.Message}", token);
            return;
        }

        try
        {
            var startedAt = DateTimeOffset.TryParse(job.StartedAt, out var parsed) ? parsed : DateTimeOffset.Now;
            var audioPath = VaultPath.Resolve(current.VaultPath, job.AudioPath);
            if (!File.Exists(audioPath)) throw new FileNotFoundException("The recorded WebM file is missing.", audioPath);

            string transcript = "";
            if (current.EnableTranscription)
            {
                Publish($"Transcribing voice note with Whisper ({current.WhisperModel}).", true);
                transcript = await whisper.TranscribeAsync(audioPath, current, token);
            }

            var body = BuildVoiceNoteBody(transcript, job.AudioPath, current);
            var destination = current.TranscriptionDestination?.Trim().ToLowerInvariant() switch
            {
                "daily-note" => VoiceDestination.Daily,
                "both" => VoiceDestination.Both,
                _ => VoiceDestination.Separate
            };

            string? separatePath = null;
            string? dailyPath = null;
            if (destination is VoiceDestination.Daily or VoiceDestination.Both)
            {
                try
                {
                    dailyPath = new NativeDailyNoteService(current).WriteCapture(body, startedAt);
                }
                catch (Exception error) when (destination == VoiceDestination.Daily || destination == VoiceDestination.Both)
                {
                    // Match the plugin's lossless behavior: a Daily Note
                    // failure falls back to a separate Markdown note.
                    Publish($"Daily Note insertion failed; saved the voice note separately ({error.Message}).", false);
                    separatePath = WriteSeparateNote(body, startedAt, current);
                }
            }
            if (destination is VoiceDestination.Separate or VoiceDestination.Both && separatePath is null)
                separatePath = WriteSeparateNote(body, startedAt, current);

            await MoveToProcessedAsync(jobPath, current.VaultPath, token);
            var output = dailyPath ?? separatePath ?? job.AudioPath;
            Publish($"Voice note processed: {output}", true);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception error)
        {
            await MoveToFailureAsync(jobPath, current.VaultPath, error.Message, token);
            var message = error.Message;
            if (message.Contains("Whisper", StringComparison.OrdinalIgnoreCase))
                message = $"Voice note saved, but Whisper transcription failed. Audio remains at {job.AudioPath}. Install / repair Whisper, then use Retry failed jobs. Details: {message}";
            Publish(message, false);
        }
    }

    private async Task ProcessTextJobAsync(string jobPath, BridgeSettings current, CancellationToken token)
    {
        try
        {
            var job = JsonSerializer.Deserialize<TextJob>(await File.ReadAllTextAsync(jobPath, token), JsonOptions);
            if (job is null || string.IsNullOrWhiteSpace(job.Text)) throw new InvalidOperationException("The text job is empty.");
            var timestamp = DateTimeOffset.TryParse(job.Timestamp, out var parsed) ? parsed : DateTimeOffset.Now;
            var destination = new NativeDailyNoteService(current).WriteCapture(job.Text, timestamp);
            await MoveToProcessedAsync(jobPath, current.VaultPath, token);
            Publish($"Legacy text capture migrated to {destination}.", true);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception error)
        {
            await MoveToFailureAsync(jobPath, current.VaultPath, error.Message, token);
            Publish($"Legacy text capture migration failed: {error.Message}", false);
        }
    }

    private static string BuildVoiceNoteBody(string transcript, string audioRelativePath, BridgeSettings current)
    {
        var cleanTranscript = transcript.Trim();
        var prefix = current.VoicePrefix?.Trim() ?? "";
        if (prefix.Length > 0 && cleanTranscript.Length > 0) cleanTranscript = $"{prefix} {cleanTranscript}";
        var parts = new List<string>();
        if (cleanTranscript.Length > 0) parts.Add(cleanTranscript);
        if (current.IncludeAudioEmbed) parts.Add($"![[{audioRelativePath.Replace('\\', '/') }]]");
        if (parts.Count == 0) parts.Add("Voice note");
        return string.Join("\n\n", parts);
    }

    private static string WriteSeparateNote(string body, DateTimeOffset startedAt, BridgeSettings current)
    {
        var folder = VaultPath.Sanitize(string.IsNullOrWhiteSpace(current.TranscriptionFolder) ? "Voice Transcriptions" : current.TranscriptionFolder);
        var stem = MomentFormat.Format(startedAt.ToLocalTime(), "YYYY-MM-DD HH-mm-ss-SSS");
        var relative = VaultPath.Combine(folder, $"{stem}.md");
        var path = VaultPath.Resolve(current.VaultPath, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        for (var suffix = 1; File.Exists(path); suffix++)
        {
            relative = VaultPath.Combine(folder, $"{stem}-{suffix}.md");
            path = VaultPath.Resolve(current.VaultPath, relative);
        }
        WriteAtomic(path, body.EndsWith('\n') ? body : body + "\n");
        return relative;
    }

    private static async Task MoveToProcessedAsync(string jobPath, string vault, CancellationToken token)
    {
        var directory = Path.Combine(vault, ".quick-capture", "bridge-processed");
        Directory.CreateDirectory(directory);
        var destination = UniqueMovePath(directory, Path.GetFileName(jobPath));
        await MoveAsync(jobPath, destination, token);
    }

    private static async Task MoveToFailureAsync(string jobPath, string vault, string reason, CancellationToken token)
    {
        try
        {
            var directory = Path.Combine(vault, ".quick-capture", "bridge-failed");
            Directory.CreateDirectory(directory);
            var destination = UniqueMovePath(directory, Path.GetFileName(jobPath));
            await MoveAsync(jobPath, destination, token);
            await File.WriteAllTextAsync($"{destination}.error.txt", reason, Encoding.UTF8, token);
        }
        catch { /* keep the original job if the vault is temporarily unavailable */ }
    }

    private static async Task MoveAsync(string source, string destination, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        await Task.Run(() => File.Move(source, destination, false), token);
    }

    private static string UniqueMovePath(string directory, string filename)
    {
        var destination = Path.Combine(directory, filename);
        if (!File.Exists(destination)) return destination;
        var stem = Path.GetFileNameWithoutExtension(filename);
        var extension = Path.GetExtension(filename);
        for (var suffix = 1; ; suffix++)
        {
            destination = Path.Combine(directory, $"{stem}-{suffix}{extension}");
            if (!File.Exists(destination)) return destination;
        }
    }

    private static void WriteAtomic(string path, string content)
    {
        var temporary = $"{path}.bridge-{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporary, content, new UTF8Encoding(false));
            if (File.Exists(path))
            {
                try { File.Replace(temporary, path, null); }
                catch (IOException) { File.Move(temporary, path, true); }
            }
            else File.Move(temporary, path);
        }
        finally { try { if (File.Exists(temporary)) File.Delete(temporary); } catch { } }
    }

    private BridgeSettings SnapshotSettings()
    {
        lock (settingsLock) return settings;
    }

    private void Publish(string message, bool succeeded) => StatusChanged?.Invoke(message, succeeded);

    private static IEnumerable<string> LegacyJobFiles(string vault, string pattern)
    {
        var directory = Path.Combine(vault, ".quick-capture", "inbox");
        return Directory.Exists(directory) ? Directory.EnumerateFiles(directory, pattern, SearchOption.TopDirectoryOnly) : Enumerable.Empty<string>();
    }

    private static bool CanTakeLegacyVoiceJobs(BridgeSettings current) =>
        current.PluginSettingsImported && !IsPluginEnabled(current.VaultPath, "quick-voice-notes");

    private static bool CanTakeLegacyTextJobs(BridgeSettings current) =>
        current.PluginSettingsImported && !IsPluginEnabled(current.VaultPath, "quick-daily-capture");

    private static bool IsPluginEnabled(string vault, string id)
    {
        var pluginDirectory = Path.Combine(vault, ".obsidian", "plugins", id);
        if (!File.Exists(Path.Combine(pluginDirectory, "manifest.json"))) return false;
        var enabledPath = Path.Combine(vault, ".obsidian", "community-plugins.json");
        if (!File.Exists(enabledPath)) return true;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(enabledPath));
            return document.RootElement.ValueKind == JsonValueKind.Array && document.RootElement.EnumerateArray().Any(value => value.ValueKind == JsonValueKind.String && string.Equals(value.GetString(), id, StringComparison.OrdinalIgnoreCase));
        }
        catch { return true; }
    }

    private enum VoiceDestination { Separate, Daily, Both }
}
