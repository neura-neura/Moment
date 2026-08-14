using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Moment;

public sealed record WhisperModelInfo(string Id, string Label, string Filename, long Bytes, string Sha256);
public sealed record WhisperEngineStatus(bool EngineInstalled, bool ModelInstalled, string EngineVersion, string ModelLabel);
public sealed record WhisperInstallProgress(string Phase, long ReceivedBytes, long? TotalBytes);

public sealed class NativeWhisperEngine
{
    private const string EngineVersion = "v1.9.2";
    private const string EngineUrl = "https://github.com/ggml-org/whisper.cpp/releases/download/v1.9.2/whisper-bin-x64.zip";
    private const string EngineSha256 = "49dcc16de826f20bd53d44f947a1ae49dfa81f86cad67a64d80820cb192d674a";
    private static readonly string[] RequiredEngineFiles = { "whisper-cli.exe", "whisper.dll", "ggml.dll", "ggml-base.dll", "ggml-cpu-x64.dll" };
    private static readonly HttpClient Http = CreateHttpClient();
    private readonly string dataDirectory;

    public NativeWhisperEngine()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var momentDirectory = Path.Combine(localAppData, "Moment", "whisper");
        var legacyDirectory = Path.Combine(localAppData, "QuickCaptureBridge", "whisper");
        dataDirectory = !Directory.Exists(momentDirectory) && Directory.Exists(legacyDirectory)
            ? legacyDirectory
            : momentDirectory;
    }

    public async Task<WhisperEngineStatus> GetStatusAsync(BridgeSettings settings, CancellationToken cancellationToken = default)
    {
        var model = GetModel(settings.WhisperModel);
        return new WhisperEngineStatus(await EngineHealthy(cancellationToken), await VerifyFile(ModelPath(model), model.Bytes, model.Sha256, cancellationToken), EngineVersion, model.Label);
    }

    public async Task InstallAsync(BridgeSettings settings, Action<WhisperInstallProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(dataDirectory);
        if (!await EngineHealthy(cancellationToken))
        {
            var archivePath = Path.Combine(dataDirectory, $"whisper-{EngineVersion}.zip");
            await DownloadVerifiedAsync(EngineUrl, archivePath, EngineSha256, (received, total) => progress?.Invoke(new WhisperInstallProgress("engine", received, total)), cancellationToken);
            try
            {
                var target = EngineDirectory;
                Directory.CreateDirectory(target);
                using var archive = ZipFile.OpenRead(archivePath);
                foreach (var entry in archive.Entries)
                {
                    if (!entry.FullName.StartsWith("Release/", StringComparison.OrdinalIgnoreCase) || entry.FullName.EndsWith("/", StringComparison.Ordinal)) continue;
                    var name = Path.GetFileName(entry.FullName);
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    entry.ExtractToFile(Path.Combine(target, name), true);
                }
            }
            finally { TryDelete(archivePath); }
            if (!await EngineHealthy(cancellationToken)) throw new InvalidOperationException("The installed whisper.cpp engine failed validation.");
        }

        var model = GetModel(settings.WhisperModel);
        var modelPath = ModelPath(model);
        if (!await VerifyFile(modelPath, model.Bytes, model.Sha256, cancellationToken))
            await DownloadVerifiedAsync(ModelUrl(model), modelPath, model.Sha256, (received, total) => progress?.Invoke(new WhisperInstallProgress("model", received, total)), cancellationToken);
        await File.WriteAllTextAsync($"{modelPath}.sha256", $"{model.Sha256}\n", Encoding.UTF8, cancellationToken);
    }

    public async Task<string> TranscribeAsync(string audioPath, BridgeSettings settings, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(audioPath)) throw new FileNotFoundException("The saved voice note could not be found.", audioPath);
        var status = await GetStatusAsync(settings, cancellationToken);
        if (!status.EngineInstalled || !status.ModelInstalled) throw new InvalidOperationException("Whisper is not ready. Install the selected engine and model in Moment settings.");

        var work = Path.Combine(Path.GetTempPath(), $"quick-capture-whisper-{Guid.NewGuid():N}");
        Directory.CreateDirectory(work);
        var wav = Path.Combine(work, "recording.wav");
        var outputBase = Path.Combine(work, "transcript");
        try
        {
            await RunProcessAsync(EncoderPath, new[] { "-hide_banner", "-loglevel", "error", "-i", audioPath, "-ac", "1", "-ar", "16000", "-c:a", "pcm_s16le", "-f", "wav", wav }, TimeSpan.FromMinutes(5), cancellationToken);
            var model = GetModel(settings.WhisperModel);
            var language = string.IsNullOrWhiteSpace(settings.WhisperLanguage) ? "auto" : settings.WhisperLanguage;
            await RunProcessAsync(Path.Combine(EngineDirectory, "whisper-cli.exe"), new[] { "-m", ModelPath(model), "-f", wav, "-l", language, "-otxt", "-of", outputBase, "-nt", "-np" }, TimeSpan.FromMinutes(30), cancellationToken);
            var transcriptPath = $"{outputBase}.txt";
            var transcript = (await File.ReadAllTextAsync(transcriptPath, Encoding.UTF8, cancellationToken)).Trim();
            if (transcript.Length == 0) throw new InvalidOperationException("Whisper completed but did not detect any speech.");
            return transcript;
        }
        finally { try { Directory.Delete(work, true); } catch { } }
    }

    public static IReadOnlyList<WhisperModelInfo> Models { get; } = new[]
    {
        new WhisperModelInfo("tiny", "Tiny multilingual (74 MB, fastest)", "ggml-tiny.bin", 77_691_713, "be07e048e1e599ad46341c8d2a135645097a538221678b7acdd1b1919c6e1b21"),
        new WhisperModelInfo("base", "Base multilingual (141 MB, recommended)", "ggml-base.bin", 147_951_465, "60ed5bc3dd14eea856493d334349b405782ddcaf0028d4b5df4088345fba2efe"),
        new WhisperModelInfo("small", "Small multilingual (465 MB, more accurate)", "ggml-small.bin", 487_601_967, "1be3a9b2063867b937e64e2ec7483364a79917e157fa98c5d94b5c1fffea987b"),
        new WhisperModelInfo("medium", "Medium multilingual (1.43 GB, demanding)", "ggml-medium.bin", 1_533_763_059, "6c14d5adee5f86394037b4e4e8b59f1673b6cee10e3cf0b11bbdbee79c156208"),
        new WhisperModelInfo("large-v3-turbo-q5_0", "Large v3 Turbo Q5 (547 MB, high quality)", "ggml-large-v3-turbo-q5_0.bin", 574_041_195, "394221709cd5ad1f40c46e6031ca61bce88931e6e088c188294c6d5a55ffa7e2")
    };

    private string EngineDirectory => Path.Combine(dataDirectory, "engine", EngineVersion);
    private string EncoderPath => Path.Combine(AppContext.BaseDirectory, "Tools", "ffmpeg.exe");
    private string ModelPath(WhisperModelInfo model) => Path.Combine(dataDirectory, "models", model.Filename);
    private static WhisperModelInfo GetModel(string id) => Models.FirstOrDefault(model => string.Equals(model.Id, id, StringComparison.OrdinalIgnoreCase)) ?? Models[1];
    private static string ModelUrl(WhisperModelInfo model) => $"https://huggingface.co/ggerganov/whisper.cpp/resolve/main/{model.Filename}";

    private async Task<bool> EngineHealthy(CancellationToken cancellationToken)
    {
        if (!RequiredEngineFiles.All(file => File.Exists(Path.Combine(EngineDirectory, file)))) return false;
        try
        {
            var output = await RunProcessAsync(Path.Combine(EngineDirectory, "whisper-cli.exe"), new[] { "--version" }, TimeSpan.FromSeconds(30), cancellationToken);
            return output.Contains("1.9.2", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private static async Task<bool> VerifyFile(string path, long expectedBytes, string expectedSha256, CancellationToken cancellationToken)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length != expectedBytes) return false;
            await using var stream = File.OpenRead(path);
            var digest = await SHA256.HashDataAsync(stream, cancellationToken);
            return Convert.ToHexString(digest).Equals(expectedSha256, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private static async Task<byte[]> HashFileAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        return await SHA256.HashDataAsync(stream, cancellationToken);
    }

    private static async Task DownloadVerifiedAsync(string url, string destination, string expectedSha256, Action<long, long?>? progress, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temporary = $"{destination}.download";
        if (File.Exists(temporary))
        {
            try
            {
                var temporaryDigest = Convert.ToHexString(await HashFileAsync(temporary, cancellationToken));
                if (temporaryDigest.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
                {
                    File.Move(temporary, destination, true);
                    return;
                }
            }
            catch { /* an interrupted or locked partial download will be replaced */ }
            TryDelete(temporary);
        }
        using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength;
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[128 * 1024];
        long received = 0;
        int read;
        // Close the destination stream before replacing the final file. On
        // Windows an open FileStream with FileShare.None prevents Move from
        // completing, which previously left a valid archive stranded as
        // whisper-v1.9.2.zip.download and made the UI report "not ready".
        await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024, true))
        {
            while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                hash.AppendData(buffer, 0, read);
                received += read;
                progress?.Invoke(received, total);
            }
            await output.FlushAsync(cancellationToken);
        }
        var actual = Convert.ToHexString(hash.GetHashAndReset());
        if (!actual.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            TryDelete(temporary);
            throw new InvalidOperationException($"Checksum mismatch. Expected {expectedSha256}, received {actual}.");
        }
        File.Move(temporary, destination, true);
    }

    private static async Task<string> RunProcessAsync(string executable, IEnumerable<string> arguments, TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (!File.Exists(executable)) throw new FileNotFoundException("Required native executable is missing.", executable);
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = executable,
                WorkingDirectory = Path.GetDirectoryName(executable)!,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            }
        };
        foreach (var argument in arguments) process.StartInfo.ArgumentList.Add(argument);
        if (!process.Start()) throw new InvalidOperationException($"Could not start {Path.GetFileName(executable)}.");
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        try
        {
            var stderr = process.StandardError.ReadToEndAsync(timeoutSource.Token);
            var stdout = process.StandardOutput.ReadToEndAsync(timeoutSource.Token);
            await process.WaitForExitAsync(timeoutSource.Token);
            var error = await stderr;
            var output = await stdout;
            if (process.ExitCode != 0) throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? output.Trim() : error.Trim());
            return $"{output}\n{error}";
        }
        catch
        {
            try { if (!process.HasExited) process.Kill(true); } catch { }
            throw;
        }
    }

    private static HttpClient CreateHttpClient() => new(new HttpClientHandler { AllowAutoRedirect = true })
    {
        Timeout = Timeout.InfiniteTimeSpan,
        DefaultRequestHeaders = { { "User-Agent", "Moment/1.2.5" } }
    };

    private static void TryDelete(string path) { try { if (File.Exists(path)) File.Delete(path); } catch { } }
}
