using System.Text;
using System.Text.Json;

namespace Moment;

/// <summary>
/// Owns voice-note processing. Jobs are kept on disk so closing Moment or
/// restarting Windows cannot lose a recording while Whisper is running.
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
    private MomentSettings settings;
    private Task worker;
    private int disposed;

    public event Action<string, bool>? StatusChanged;

    public NativeVoiceProcessor(MomentSettings initialSettings)
    {
        settings = initialSettings;
        CaptureQueuePaths.MigratePreviousQueue(settings.WorkspacePath);
        worker = Task.Run(ProcessLoopAsync);
        Schedule();
    }

    public void UpdateSettings(MomentSettings next)
    {
        lock (settingsLock) settings = next;
        CaptureQueuePaths.MigratePreviousQueue(next.WorkspacePath);
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
        if (string.IsNullOrWhiteSpace(current.WorkspacePath)) return 0;
        var failed = CaptureQueuePaths.Failed(current.WorkspacePath);
        if (!Directory.Exists(failed)) return 0;
        var pending = CaptureQueuePaths.Pending(current.WorkspacePath);
        Directory.CreateDirectory(pending);
        var count = 0;
        foreach (var job in Directory.EnumerateFiles(failed, "*.json", SearchOption.TopDirectoryOnly))
        {
            var errorSidecar = $"{job}.error.txt";
            if (File.Exists(errorSidecar))
            {
                try
                {
                    if (IsNonRetryableFailure(File.ReadAllText(errorSidecar))) continue;
                }
                catch { /* a locked sidecar should not prevent other jobs from retrying */ }
            }
            var destination = UniqueMovePath(pending, Path.GetFileName(job));
            try
            {
                File.Move(job, destination);
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
        catch { /* shutdown must never prevent Moment from closing */ }
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
        if (string.IsNullOrWhiteSpace(current.WorkspacePath) || !Directory.Exists(current.WorkspacePath)) return false;
        var pending = CaptureQueuePaths.Pending(current.WorkspacePath);
        var voiceJobs = Directory.Exists(pending)
            ? Directory.EnumerateFiles(pending, "voice-*.json", SearchOption.TopDirectoryOnly).ToList()
            : new List<string>();
        var jobs = voiceJobs.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
        if (jobs.Length == 0) return false;
        foreach (var path in jobs)
        {
            token.ThrowIfCancellationRequested();
            await ProcessJobAsync(path, current, token);
        }
        return true;
    }

    private async Task ProcessJobAsync(string jobPath, MomentSettings current, CancellationToken token)
    {
        VoiceJob? job;
        try
        {
            job = JsonSerializer.Deserialize<VoiceJob>(await File.ReadAllTextAsync(jobPath, token), JsonOptions);
            if (job is null || string.IsNullOrWhiteSpace(job.AudioPath)) throw new InvalidOperationException("The voice job is missing its audio path.");
        }
        catch (Exception error)
        {
            await MoveToFailureAsync(jobPath, current.WorkspacePath, $"Invalid voice job: {error.Message}", token);
            return;
        }

        try
        {
            var startedAt = DateTimeOffset.TryParse(job.StartedAt, out var parsed) ? parsed : DateTimeOffset.Now;
            var audioPath = WorkspacePath.Resolve(current.WorkspacePath, job.AudioPath);
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
                "recurring-note" => VoiceDestination.RecurringNote,
                "both" => VoiceDestination.Both,
                _ => VoiceDestination.Separate
            };

            string? separatePath = null;
            string? recurringNotePath = null;
            if (destination is VoiceDestination.RecurringNote or VoiceDestination.Both)
            {
                try
                {
                    recurringNotePath = new RecurringNoteService(current).WriteCapture(body, startedAt);
                }
                catch (Exception error) when (destination == VoiceDestination.RecurringNote || destination == VoiceDestination.Both)
                {
                    // Preserve the recording by falling back to a separate
                    // Markdown note when recurring-note insertion fails.
                    Publish($"Recurring note insertion failed; saved the voice note separately ({error.Message}).", false);
                    separatePath = WriteSeparateNote(body, startedAt, current);
                }
            }
            if (destination is VoiceDestination.Separate or VoiceDestination.Both && separatePath is null)
                separatePath = WriteSeparateNote(body, startedAt, current);

            await MoveToCompletedAsync(jobPath, current.WorkspacePath, token);
            var output = recurringNotePath ?? separatePath ?? job.AudioPath;
            Publish($"Voice note processed: {output}", true);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception error)
        {
            var message = DescribeProcessingFailure(error.Message, job.AudioPath);
            await MoveToFailureAsync(jobPath, current.WorkspacePath, message, token);
            Publish(message, false);
        }
    }

    private static string BuildVoiceNoteBody(string transcript, string audioRelativePath, MomentSettings current)
    {
        var cleanTranscript = transcript.Trim();
        var parts = new List<string>();
        if (cleanTranscript.Length > 0) parts.Add(cleanTranscript);
        if (current.IncludeAudioEmbed) parts.Add($"![[{audioRelativePath.Replace('\\', '/') }]]");
        if (parts.Count == 0) parts.Add("Voice note");
        return string.Join("\n\n", parts);
    }

    private static string WriteSeparateNote(string body, DateTimeOffset startedAt, MomentSettings current)
    {
        var folder = WorkspacePath.Sanitize(string.IsNullOrWhiteSpace(current.TranscriptionFolder) ? "Voice Transcriptions" : current.TranscriptionFolder);
        var stem = MomentFilename.Format(startedAt, current.TranscriptionFilenameFormat, current.TranscriptionFilenamePrefix);
        var relative = WorkspacePath.Combine(folder, $"{stem}.md");
        var path = WorkspacePath.Resolve(current.WorkspacePath, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        for (var suffix = 1; File.Exists(path); suffix++)
        {
            relative = WorkspacePath.Combine(folder, $"{stem}-{suffix}.md");
            path = WorkspacePath.Resolve(current.WorkspacePath, relative);
        }
        WriteAtomic(path, body.EndsWith('\n') ? body : body + "\n");
        return relative;
    }

    private static string DescribeProcessingFailure(string error, string audioPath)
    {
        if (IsNonRetryableFailure(error))
            return $"Voice note saved, but Whisper detected no speech. Whisper is installed. Check Voice > Input device and record again. Audio remains at {audioPath}.";

        return error.Contains("Whisper", StringComparison.OrdinalIgnoreCase)
            ? $"Voice note saved, but Whisper transcription failed. Audio remains at {audioPath}. Install / repair Whisper, then use Retry failed jobs. Details: {error}"
            : error;
    }

    private static bool IsNonRetryableFailure(string message) =>
        message.Contains("did not detect any speech", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("detected no speech", StringComparison.OrdinalIgnoreCase);

    private static async Task MoveToCompletedAsync(string jobPath, string workspace, CancellationToken token)
    {
        var directory = CaptureQueuePaths.Completed(workspace);
        Directory.CreateDirectory(directory);
        var destination = UniqueMovePath(directory, Path.GetFileName(jobPath));
        await MoveAsync(jobPath, destination, token);
    }

    private static async Task MoveToFailureAsync(string jobPath, string workspace, string reason, CancellationToken token)
    {
        try
        {
            var directory = CaptureQueuePaths.Failed(workspace);
            Directory.CreateDirectory(directory);
            var destination = UniqueMovePath(directory, Path.GetFileName(jobPath));
            await MoveAsync(jobPath, destination, token);
            await File.WriteAllTextAsync($"{destination}.error.txt", reason, Encoding.UTF8, token);
        }
        catch { /* keep the original job if the workspace is temporarily unavailable */ }
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
        var temporary = $"{path}.moment-{Guid.NewGuid():N}.tmp";
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

    private MomentSettings SnapshotSettings()
    {
        lock (settingsLock) return settings;
    }

    private void Publish(string message, bool succeeded) => StatusChanged?.Invoke(message, succeeded);

    private enum VoiceDestination { Separate, RecurringNote, Both }
}
