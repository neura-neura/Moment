using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Moment;

var root = Path.Combine(Path.GetTempPath(), $"moment-smoke-{Guid.NewGuid():N}");
Directory.CreateDirectory(root);
try
{
    Directory.CreateDirectory(Path.Combine(root, ".obsidian"));
    await File.WriteAllTextAsync(Path.Combine(root, ".obsidian", "daily-notes.json"), "{\"folder\":\"Recurring\",\"format\":\"YYYY-MM-DD\",\"template\":\"Templates/recurring.md\"}");
    Directory.CreateDirectory(Path.Combine(root, "Templates"));
    await File.WriteAllTextAsync(Path.Combine(root, "Templates", "recurring.md"), "# Recurring note\n\n{{date}}\n");

    var settings = new MomentSettings
    {
        WorkspacePath = root,
        RecurringNoteInsertionLocation = "under-heading",
        RecurringNoteTargetHeading = "Inbox",
        RecurringNoteMissingHeadingBehavior = "create"
    };

    var recurringNotePath = new RecurringNoteService(settings).WriteCapture("Smoke text", new DateTimeOffset(2026, 8, 13, 10, 30, 0, TimeSpan.Zero));
    var recurringText = await File.ReadAllTextAsync(Path.Combine(root, recurringNotePath.Replace('/', Path.DirectorySeparatorChar)));
    Assert(recurringText.Contains("# Inbox", StringComparison.Ordinal) && recurringText.Contains("Smoke text", StringComparison.Ordinal), "recurring-note insertion");

    settings.RecurringNoteFilenameFormat = "DD MMMM YYYY";
    settings.RecurringNoteFilenamePrefix = "Journal-";
    var customTimestamp = new DateTimeOffset(2026, 8, 14, 10, 31, 0, TimeSpan.Zero);
    var customPath = new RecurringNoteService(settings).WriteCapture("Custom filename", customTimestamp);
    var expectedStem = WorkspacePath.SanitizeFilename($"Journal-{MomentFormat.Format(customTimestamp.ToLocalTime(), "DD MMMM YYYY")}");
    Assert(customPath.EndsWith($"{expectedStem}.md", StringComparison.Ordinal), "custom localized filename and prefix");
    var duplicatePath = new RecurringNoteService(settings).WriteCapture("Same filename appends", customTimestamp.AddMinutes(1));
    Assert(string.Equals(customPath, duplicatePath, StringComparison.Ordinal), "duplicate filename reuses existing note");

    settings.VoiceFilenameFormat = "DD MMMM YYYY HH-mm";
    settings.VoiceFilenamePrefix = "Voice-";
    settings.TranscriptionFilenameFormat = "DD MMMM YYYY";
    settings.TranscriptionFilenamePrefix = "Transcript-";
    var voiceTimestamp = new DateTimeOffset(2026, 8, 15, 11, 32, 0, TimeSpan.Zero);
    var audioPath = new CaptureStore(settings).EnsureAudioPath(voiceTimestamp);
    var expectedAudioStem = WorkspacePath.SanitizeFilename($"Voice-{MomentFormat.Format(voiceTimestamp.ToLocalTime(), "DD MMMM YYYY HH-mm")}");
    Assert(Path.GetFileNameWithoutExtension(audioPath) == expectedAudioStem, "custom voice filename and prefix");
    await File.WriteAllBytesAsync(audioPath, new byte[128]);
    new CaptureStore(settings).WriteVoice(audioPath, voiceTimestamp);
    using (var processor = new NativeVoiceProcessor(settings))
    {
        processor.Schedule();
        var completed = CaptureQueuePaths.Completed(root);
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline && !(Directory.Exists(completed) && Directory.EnumerateFiles(completed, "*.json").Any())) await Task.Delay(100);
    }
    Assert(Directory.Exists(Path.Combine(root, "Voice Transcriptions")), "native voice output folder");
    Assert(Directory.EnumerateFiles(Path.Combine(root, "Voice Transcriptions"), "*.md").Any(), "native voice Markdown output");
    var expectedTranscriptStem = WorkspacePath.SanitizeFilename($"Transcript-{MomentFormat.Format(voiceTimestamp.ToLocalTime(), "DD MMMM YYYY")}");
    Assert(File.Exists(Path.Combine(root, "Voice Transcriptions", $"{expectedTranscriptStem}.md")), "custom transcription filename and prefix");
    Assert(Directory.EnumerateFiles(CaptureQueuePaths.Completed(root), "voice-*.json").Any(), "completed voice queue item");
    Assert(!Directory.Exists(Path.Combine(root, ".quick-capture")), "legacy queue is not created");
    await DownloadMoveSmokeAsync(root);
    Console.WriteLine("Native Moment smoke test passed.");
}
finally
{
    try { Directory.Delete(root, true); } catch { }
}

static void Assert(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException($"Smoke assertion failed: {name}");
}

static async Task DownloadMoveSmokeAsync(string root)
{
    var payload = Encoding.UTF8.GetBytes("whisper-download-test");
    var digest = Convert.ToHexString(SHA256.HashData(payload));
    var destination = Path.Combine(root, "download-test.bin");
    var temporary = $"{destination}.download";
    var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    var port = ((IPEndPoint)listener.LocalEndpoint).Port;
    var server = Task.Run(async () =>
    {
        using var client = await listener.AcceptTcpClientAsync();
        await using var stream = client.GetStream();
        var request = new byte[2048];
        _ = await stream.ReadAsync(request);
        var header = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Length: {payload.Length}\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(header);
        await stream.WriteAsync(payload);
    });
    try
    {
        var method = typeof(NativeWhisperEngine).GetMethod("DownloadVerifiedAsync", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Download method was not found.");
        var task = (Task?)method.Invoke(null, new object?[] { $"http://127.0.0.1:{port}/whisper.zip", destination, digest, null, CancellationToken.None })
            ?? throw new InvalidOperationException("Download method did not return a task.");
        await task;
        Assert(File.Exists(destination) && !File.Exists(temporary), "verified download moves after closing stream");
    }
    finally
    {
        listener.Stop();
        try { await server; } catch { }
    }
}
