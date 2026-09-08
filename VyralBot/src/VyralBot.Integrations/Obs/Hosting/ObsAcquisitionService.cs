using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using VyralBot.Integrations.Obs.Engine;

namespace VyralBot.Integrations.Obs.Hosting;

/// <summary>Progress of downloading and silently installing OBS Studio.</summary>
public enum ObsAcquirePhase
{
    /// <summary>The installer is being streamed to the temp directory.</summary>
    Downloading,

    /// <summary>The official NSIS installer is running silently (self-elevates; can take minutes).</summary>
    Installing,

    /// <summary>The installer finished and the locator now detects an OBS Studio install.</summary>
    Done,

    /// <summary>A step failed; <see cref="ObsAcquireProgress.Detail"/> holds the reason.</summary>
    Failed,
}

/// <summary>Immutable progress snapshot for acquisition UI binding.</summary>
public sealed record ObsAcquireProgress(ObsAcquirePhase Phase, int Percent, string Detail);

/// <summary>
/// Resolves the download URL of the latest official Windows installer.
/// Extracted so the asset lookup is unit-testable without touching the network.
/// </summary>
public interface IReleaseFeed
{
    /// <summary>
    /// The <c>(browser_download_url, size)</c> of the newest
    /// <c>OBS-Studio-*-Windows-Installer-x64.exe</c> asset, or null when the
    /// latest release carries no such asset.
    /// </summary>
    Task<(string Url, long Size)?> GetLatestWindowsInstallerAsync(CancellationToken ct = default);
}

/// <summary>
/// GitHub releases feed: GET releases/latest (a User-Agent header is REQUIRED
/// by the GitHub API) and pick the Windows x64 installer asset.
/// </summary>
public sealed class GitHubReleaseFeed(HttpClient? http = null) : IReleaseFeed
{
    public const string LatestReleaseUrl = "https://api.github.com/repos/obsproject/obs-studio/releases/latest";

    private const string InstallerPrefix = "OBS-Studio-";
    private const string InstallerSuffix = "-Windows-Installer-x64.exe";

    private static readonly HttpClient SharedClient = CreateClient();
    private readonly HttpClient _http = http ?? SharedClient;

    public async Task<(string Url, long Size)?> GetLatestWindowsInstallerAsync(CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseUrl);
        // Per request, not on the client: an injected HttpClient (tests) would
        // otherwise go out headerless, and the GitHub API rejects that.
        request.Headers.UserAgent.ParseAdd("VyralBot");

        using HttpResponseMessage response = await _http.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        string json = await response.Content.ReadAsStringAsync(ct);
        return PickWindowsInstaller(json);
    }

    /// <summary>First <c>OBS-Studio-*-Windows-Installer-x64.exe</c> asset in a releases/latest payload.</summary>
    public static (string Url, long Size)? PickWindowsInstaller(string json)
    {
        using JsonDocument doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Object
            || !doc.RootElement.TryGetProperty("assets", out JsonElement assets)
            || assets.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (JsonElement asset in assets.EnumerateArray())
        {
            if (asset.ValueKind != JsonValueKind.Object
                || !asset.TryGetProperty("name", out JsonElement name)
                || name.ValueKind != JsonValueKind.String
                || !asset.TryGetProperty("browser_download_url", out JsonElement url)
                || url.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            string assetName = name.GetString()!;
            if (assetName.Length > InstallerPrefix.Length + InstallerSuffix.Length
                && assetName.StartsWith(InstallerPrefix, StringComparison.OrdinalIgnoreCase)
                && assetName.EndsWith(InstallerSuffix, StringComparison.OrdinalIgnoreCase))
            {
                long size = asset.TryGetProperty("size", out JsonElement sizeElement)
                    && sizeElement.ValueKind == JsonValueKind.Number
                        ? sizeElement.GetInt64()
                        : 0;
                return (url.GetString()!, size);
            }
        }

        return null;
    }

    private static HttpClient CreateClient() => new();
}

/// <summary>
/// Downloads the official OBS Studio installer and runs it silently. Every
/// step is wrapped — failures are surfaced as a terminal
/// <see cref="ObsAcquirePhase.Failed"/> progress record, never as an exception
/// to the consumer (only consumer-requested cancellation propagates). The
/// installer NSIS binary self-elevates, so no elevation logic lives here.
/// </summary>
public sealed class ObsAcquisitionService
{
    private const int DownloadChunkBytes = 10 * 1024 * 1024;

    private static readonly HttpClient DownloadHttp = CreateDownloadClient();

    private readonly ObsInstallLocator locator;

    public ObsAcquisitionService(ObsInstallLocator locator)
    {
        this.locator = locator ?? throw new ArgumentNullException(nameof(locator));
    }

    /// <summary>GPL attribution shown by the not-installed UI panel.</summary>
    public const string ObsAttributionNotice =
        "OBS Studio © OBS Project — GPL-2.0. Vyral downloads and runs the official installer from " +
        "https://github.com/obsproject/obs-studio (source: https://github.com/obsproject/obs-studio).";

    public bool IsObsInstalled => locator.Locate() is not null;

    /// <summary>
    /// The feed and process runner are injectable for tests; production callers
    /// just pass a cancellation token and get the GitHub feed plus a real
    /// <c>installer.exe /S</c> launch. All yields sit at the iterator's top
    /// level (C# forbids them in try-with-catch) — each phase captures its
    /// failure into a local instead.
    /// </summary>
    public async IAsyncEnumerable<ObsAcquireProgress> DownloadAndInstallAsync(
        [EnumeratorCancellation] CancellationToken ct = default,
        IReleaseFeed? feed = null,
        Func<string, Task<int>>? runInstallerAsync = null)
    {
        IReleaseFeed effectiveFeed = feed ?? new GitHubReleaseFeed();
        Func<string, Task<int>> runner = runInstallerAsync ?? DefaultInstallerRunner;

        // 1. Resolve the latest official Windows installer.
        (string Url, long Size)? installer = null;
        string? resolveFailure = null;
        try
        {
            installer = await effectiveFeed.GetLatestWindowsInstallerAsync(ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            resolveFailure = ex.Message;
        }

        if (resolveFailure is not null)
        {
            yield return Failed($"could not read the latest OBS Studio release: {resolveFailure}");
            yield break;
        }

        if (installer is null)
        {
            yield return Failed("the latest OBS Studio release has no Windows installer asset");
            yield break;
        }

        // 2. Stream it to %TEMP%. Content-Length drives the percent; without
        //    one the progress is indeterminate (-1). The GUID suffix keeps a
        //    crashed earlier run's installer from colliding with this one.
        string installerPath = Path.Combine(
            Path.GetTempPath(), $"obs-studio-installer-{Guid.NewGuid():N}.exe");
        string? downloadFailure = null;

        yield return new(ObsAcquirePhase.Downloading, 0, "downloading the OBS Studio installer…");

        HttpResponseMessage? response = null;
        try
        {
            response = await DownloadHttp.GetAsync(
                installer.Value.Url, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            downloadFailure = ex.Message;
        }

        Stream? source = null;
        FileStream? target = null;
        long? total = null;
        if (downloadFailure is null && response is not null)
        {
            try
            {
                total = response.Content.Headers.ContentLength
                    ?? (installer.Value.Size > 0 ? installer.Value.Size : null);
                source = await response.Content.ReadAsStreamAsync(ct);
                target = new FileStream(
                    installerPath, FileMode.Create, FileAccess.Write, FileShare.None,
                    DownloadChunkBytes, useAsync: true);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                downloadFailure = ex.Message;
            }
        }

        if (downloadFailure is null && source is not null && target is not null)
        {
            // Large chunk on purpose: throttles progress records (a ~150 MB
            // installer reports ~15 times, not per tiny socket read).
            var buffer = new byte[DownloadChunkBytes];
            long written = 0;
            while (true)
            {
                int read;
                try
                {
                    read = await source.ReadAsync(buffer, ct);
                    if (read > 0)
                    {
                        await target.WriteAsync(buffer.AsMemory(0, read), ct);
                        written += read;
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    downloadFailure = ex.Message;
                    break;
                }

                if (read == 0)
                {
                    break;
                }

                yield return new(
                    ObsAcquirePhase.Downloading,
                    total is > 0 ? (int)(written * 100 / total.Value) : -1,
                    "downloading the OBS Studio installer…");
            }
        }

        source?.Dispose();
        target?.Dispose();
        response?.Dispose();

        if (downloadFailure is not null)
        {
            yield return Failed($"downloading the OBS Studio installer failed: {downloadFailure}");
            yield break;
        }

        // 3. Silent install. No timeout on purpose — NSIS self-elevates and can
        //    legitimately run for minutes on slow disks.
        yield return new(ObsAcquirePhase.Installing, 0, "running the OBS Studio installer (this can take a few minutes)…");

        int exitCode = -1;
        string? installFailure = null;
        try
        {
            exitCode = await runner(installerPath);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            installFailure = ex.Message;
        }

        if (installFailure is not null)
        {
            yield return Failed($"could not run the OBS Studio installer: {installFailure}");
            yield break;
        }

        // 4. The verdict is the locator, not the exit code — a self-elevating
        //    installer can hand back a nonzero code to the launching user.
        yield return locator.Locate() is not null
            ? new(ObsAcquirePhase.Done, 100, "OBS Studio installed")
            : Failed(exitCode == 0
                ? "Installer finished but OBS was not detected."
                : $"Installer finished but OBS was not detected (exit code {exitCode}).");
    }

    private static async Task<int> DefaultInstallerRunner(string installerPath)
    {
        using Process? process = Process.Start(
            new ProcessStartInfo(installerPath, "/S") { UseShellExecute = true });
        if (process is null)
        {
            return -1;
        }

        await process.WaitForExitAsync();
        return process.ExitCode;
    }

    private static ObsAcquireProgress Failed(string detail) => new(ObsAcquirePhase.Failed, 0, detail);

    private static HttpClient CreateDownloadClient()
    {
        var client = new HttpClient();
        // GitHub download URLs redirect to bouncer/CDN hosts that also want a UA.
        client.DefaultRequestHeaders.UserAgent.ParseAdd("VyralBot");
        return client;
    }
}
