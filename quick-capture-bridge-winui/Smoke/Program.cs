using System.Text.Json;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Moment;

var root = Path.Combine(Path.GetTempPath(), $"quick-capture-bridge-smoke-{Guid.NewGuid():N}");
Directory.CreateDirectory(root);
try
{
    var pluginRoot = Path.Combine(root, ".obsidian", "plugins");
    Directory.CreateDirectory(Path.Combine(pluginRoot, "quick-daily-capture"));
    Directory.CreateDirectory(Path.Combine(pluginRoot, "quick-voice-notes"));
    Directory.CreateDirectory(Path.Combine(root, ".quick-capture", "inbox"));
    await File.WriteAllTextAsync(Path.Combine(root, ".obsidian", "daily-notes.json"), "{\"folder\":\"Daily\",\"format\":\"YYYY-MM-DD\",\"template\":\"Templates/daily.md\"}");
    Directory.CreateDirectory(Path.Combine(root, "Templates"));
    await File.WriteAllTextAsync(Path.Combine(root, "Templates", "daily.md"), "# Daily\n\n{{date}}\n");
    await File.WriteAllTextAsync(Path.Combine(pluginRoot, "quick-daily-capture", "data.json"), "{\"insertionLocation\":\"under-heading\",\"targetHeading\":\"Inbox\",\"missingHeadingBehavior\":\"create\",\"timestampFormat\":\"HH:mm\",\"closeAfterSave\":true,\"enterToSave\":true}");
    await File.WriteAllTextAsync(Path.Combine(pluginRoot, "quick-voice-notes", "data.json"), "{\"audioFolder\":\"Voice Notes\",\"enableTranscription\":false,\"destination\":\"separate-note\",\"transcriptionFolder\":\"Voice Transcriptions\",\"includeAudioEmbed\":true}");

    var settings = new BridgeSettings { VaultPath = root };
    var migration = PluginSettingsMigration.Import(settings);
    Assert(migration.ImportedPluginFiles == 2, "plugin settings import");
    Assert(settings.DailyInsertionLocation == "under-heading", "daily insertion import");
    Assert(settings.AudioFolder == "Voice Notes", "voice settings import");

    var dailyPath = new NativeDailyNoteService(settings).WriteCapture("Smoke text", new DateTimeOffset(2026, 8, 13, 10, 30, 0, TimeSpan.Zero));
    var dailyText = await File.ReadAllTextAsync(Path.Combine(root, dailyPath.Replace('/', Path.DirectorySeparatorChar)));
    Assert(dailyText.Contains("# Inbox", StringComparison.Ordinal) && dailyText.Contains("Smoke text", StringComparison.Ordinal), "Daily Note insertion");

    settings.DailyFilenameFormat = "DD MMMM YYYY";
    settings.DailyFilenamePrefix = "Journal-";
    var customDailyTimestamp = new DateTimeOffset(2026, 8, 14, 10, 31, 0, TimeSpan.Zero);
    var customDailyPath = new NativeDailyNoteService(settings).WriteCapture("Custom filename", customDailyTimestamp);
    var expectedStem = VaultPath.SanitizeFilename($"Journal-{MomentFormat.Format(customDailyTimestamp.ToLocalTime(), "DD MMMM YYYY")}");
    Assert(customDailyPath.EndsWith($"{expectedStem}.md", StringComparison.Ordinal), "custom localized filename and prefix");
    var duplicatePath = new NativeDailyNoteService(settings).WriteCapture("Same filename appends", customDailyTimestamp.AddMinutes(1));
    Assert(string.Equals(customDailyPath, duplicatePath, StringComparison.Ordinal), "duplicate filename reuses existing note");

    var legacyText = new TextJob { Text = "Legacy text", Timestamp = "2026-08-13T10:31:00+00:00" };
    await File.WriteAllTextAsync(Path.Combine(root, ".quick-capture", "inbox", $"text-{legacyText.Id}.json"), JsonSerializer.Serialize(legacyText));

    var audioPath = new VaultInbox(settings).EnsureAudioPath(DateTimeOffset.UtcNow);
    await File.WriteAllBytesAsync(audioPath, new byte[128]);
    new VaultInbox(settings).WriteVoice(audioPath, DateTimeOffset.UtcNow);
    using (var processor = new NativeVoiceProcessor(settings))
    {
        processor.Schedule();
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline && !(Directory.Exists(Path.Combine(root, ".quick-capture", "bridge-processed")) && Directory.EnumerateFiles(Path.Combine(root, ".quick-capture", "bridge-processed"), "*.json").Any())) await Task.Delay(100);
    }
    Assert(Directory.Exists(Path.Combine(root, "Voice Transcriptions")), "native voice output folder");
    Assert(Directory.EnumerateFiles(Path.Combine(root, "Voice Transcriptions"), "*.md").Any(), "native voice Markdown output");
    Assert(Directory.EnumerateFiles(Path.Combine(root, ".quick-capture", "bridge-processed"), "text-*.json").Any(), "legacy text migration");
    await DownloadMoveSmokeAsync(root);
    Console.WriteLine("Native migration smoke test passed.");
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
