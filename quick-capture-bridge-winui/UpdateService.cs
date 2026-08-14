using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Moment;

public sealed record UpdateCheckResult(
    bool IsUpdateAvailable,
    Version CurrentVersion,
    Version LatestVersion,
    string LatestTag,
    string ReleaseUrl,
    string? InstallerUrl,
    string? ChecksumUrl,
    string Message);

/// <summary>
/// Checks the public GitHub Releases feed and hands the signed-by-release
/// installer back to the native app. The updater never replaces application
/// files in-process; it launches the normal NSIS installer and exits first.
/// </summary>
public sealed class UpdateService
{
    public const string Repository = "neura-neura/Moment";
    public const string RepositoryUrl = "https://github.com/neura-neura/Moment";
    public const string InstallerAssetName = "MomentSetup-x64.exe";
    public static Version CurrentVersion { get; } = new(1, 2, 4);

    private static readonly HttpClient Http = CreateHttpClient();

    public async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        var endpoint = $"https://api.github.com/repos/{Repository}/releases/latest";
        using var response = await Http.GetAsync(endpoint, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;
        var tag = root.TryGetProperty("tag_name", out var tagElement) ? tagElement.GetString() ?? "" : "";
        var latest = ParseVersion(tag);
        var releaseUrl = root.TryGetProperty("html_url", out var releaseElement) ? releaseElement.GetString() ?? RepositoryUrl : RepositoryUrl;
        string? installerUrl = null;
        string? checksumUrl = null;
        if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
        {
            foreach (var asset in assets.EnumerateArray())
            {
                if (!asset.TryGetProperty("name", out var name) || !string.Equals(name.GetString(), InstallerAssetName, StringComparison.OrdinalIgnoreCase)) continue;
                if (asset.TryGetProperty("browser_download_url", out var url)) installerUrl = url.GetString();
                continue;
            }
            foreach (var asset in assets.EnumerateArray())
            {
                if (!asset.TryGetProperty("name", out var name) || !string.Equals(name.GetString(), $"{InstallerAssetName}.sha256", StringComparison.OrdinalIgnoreCase)) continue;
                if (asset.TryGetProperty("browser_download_url", out var url)) checksumUrl = url.GetString();
                break;
            }
        }

        var available = latest > CurrentVersion && !string.IsNullOrWhiteSpace(installerUrl);
        var message = available
            ? $"Version {latest} is available."
            : latest <= CurrentVersion
                ? $"Moment {CurrentVersion} is up to date."
                : $"Release {latest} was found, but its Windows installer asset is missing.";
        return new UpdateCheckResult(available, CurrentVersion, latest, tag, releaseUrl, installerUrl, checksumUrl, message);
    }

    public async Task<string> DownloadInstallerAsync(UpdateCheckResult update, IProgress<long>? progress = null, CancellationToken cancellationToken = default)
    {
        if (!update.IsUpdateAvailable || string.IsNullOrWhiteSpace(update.InstallerUrl))
            throw new InvalidOperationException("There is no newer Moment installer to download.");
        var destination = Path.Combine(Path.GetTempPath(), $"MomentSetup-{update.LatestVersion}-{Guid.NewGuid():N}.exe");
        try
        {
            using var response = await Http.GetAsync(update.InstallerUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
            // Keep the output stream in a nested scope. The checksum pass below
            // must open the completed installer for reading after this handle is
            // released; leaving FileShare.None open made every update fail with
            // a misleading "file is being used" error.
            await using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024, true))
            {
                var buffer = new byte[128 * 1024];
                int read;
                long received = 0;
                while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    received += read;
                    progress?.Report(received);
                }
                await output.FlushAsync(cancellationToken);
            }

            if (!string.IsNullOrWhiteSpace(update.ChecksumUrl))
            {
                using var checksumResponse = await Http.GetAsync(update.ChecksumUrl, cancellationToken);
                checksumResponse.EnsureSuccessStatusCode();
                var expected = (await checksumResponse.Content.ReadAsStringAsync(cancellationToken)).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                await using var installerStream = File.OpenRead(destination);
                var actual = Convert.ToHexString(await SHA256.HashDataAsync(installerStream, cancellationToken));
                if (string.IsNullOrWhiteSpace(expected) || !actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("The downloaded installer checksum does not match the GitHub release.");
            }
            return destination;
        }
        catch
        {
            TryDelete(destination);
            throw;
        }
    }

    public static void LaunchInstaller(string installerPath)
    {
        if (!File.Exists(installerPath)) throw new FileNotFoundException("The downloaded installer could not be found.", installerPath);
        Process.Start(new ProcessStartInfo
        {
            FileName = installerPath,
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(installerPath)!
        });
    }

    /// <summary>
    /// Starts a detached PowerShell helper which waits for this Moment process
    /// to exit before launching NSIS. This prevents the installer from racing
    /// the app while it is still holding WinUI/WebView/native DLL handles.
    /// </summary>
    public static void LaunchInstallerAfterExit(string installerPath)
    {
        if (!File.Exists(installerPath)) throw new FileNotFoundException("The downloaded installer could not be found.", installerPath);
        var escapedInstaller = installerPath.Replace("'", "''", StringComparison.Ordinal);
        var escapedWorkingDirectory = (Path.GetDirectoryName(installerPath) ?? Path.GetTempPath()).Replace("'", "''", StringComparison.Ordinal);
        var script = $"$p=Get-Process -Id {Environment.ProcessId} -ErrorAction SilentlyContinue; if ($p) {{ $p.WaitForExit() }}; Start-Process -FilePath '{escapedInstaller}' -WorkingDirectory '{escapedWorkingDirectory}'";
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        var powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = powershell,
                Arguments = $"-NoLogo -NoProfile -NonInteractive -WindowStyle Hidden -EncodedCommand {encoded}",
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            });
        }
        catch
        {
            // Keep the direct path as a last-resort fallback on hardened
            // systems where PowerShell has been disabled by policy.
            LaunchInstaller(installerPath);
        }
    }

    private static Version ParseVersion(string tag)
    {
        var clean = tag.Trim();
        if (clean.StartsWith('v') || clean.StartsWith('V')) clean = clean[1..];
        var separator = clean.IndexOfAny(new[] { '-', '+' });
        if (separator >= 0) clean = clean[..separator];
        return Version.TryParse(clean, out var version) ? version : new Version(0, 0, 0);
    }

    private static HttpClient CreateHttpClient() => new(new HttpClientHandler { AllowAutoRedirect = true })
    {
        Timeout = TimeSpan.FromSeconds(45),
        DefaultRequestHeaders = { { "User-Agent", "Moment/1.2.4" }, { "Accept", "application/vnd.github+json" } }
    };

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
