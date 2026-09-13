using System.IO.Compression;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;

namespace VyralBot.Integrations.Restream;

/// <summary>Progress of downloading and extracting the bundled FFmpeg.</summary>
public enum RestreamAcquirePhase
{
    /// <summary>The zip is streaming to the temp directory.</summary>
    Downloading,

    /// <summary>ffmpeg.exe is being pulled out of the zip into the appdata tree.</summary>
    Extracting,

    /// <summary>The locator now detects the bundled executable.</summary>
    Done,

    /// <summary>A step failed; <see cref="RestreamAcquireProgress.Detail"/> holds the reason.</summary>
    Failed,
}

/// <summary>Immutable progress snapshot for acquisition UI binding.</summary>
public sealed record RestreamAcquireProgress(RestreamAcquirePhase Phase, int Percent, string Detail);

/// <summary>
/// Downloads the official gyan.dev FFmpeg "release-essentials" build (a static
/// GPL build shipping x264) and extracts only ffmpeg.exe into Vyral's portable
/// appdata tree. Modeled on ObsAcquisitionService: every step is wrapped and
/// failures surface as a terminal <see cref="RestreamAcquirePhase.Failed"/>
/// progress record, never as an exception (only cancellation propagates). All
/// yields sit in try-free scopes (C# forbids them in try-with-catch) — each
/// phase captures its failure into a local instead.
/// </summary>
public sealed class RestreamFfmpegAcquisitionService
{
    private const int DownloadChunkBytes = 4 * 1024 * 1024;

    /// <summary>Pinned to gyan.dev's stable "latest release" essentials zip (~90 MB).</summary>
    public const string DownloadUrl = "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip";

    /// <summary>GPL attribution shown by the download UI.</summary>
    public const string AttributionNotice =
        "FFmpeg by the FFmpeg developers — GPL build (includes x264). Downloaded from https://www.gyan.dev/ffmpeg/builds/ .";

    private static readonly HttpClient DownloadHttp = CreateDownloadClient();

    private readonly ILogger<RestreamFfmpegAcquisitionService>? _logger;

    public RestreamFfmpegAcquisitionService(ILogger<RestreamFfmpegAcquisitionService>? logger = null)
    {
        _logger = logger;
    }

    public bool IsBundledInstalled => File.Exists(RestreamFfmpegLocator.BundledFfmpegPath);

    public async IAsyncEnumerable<RestreamAcquireProgress> DownloadAndInstallAsync(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        string zipPath = Path.Combine(Path.GetTempPath(), $"ffmpeg-essentials-{Guid.NewGuid():N}.zip");
        string stagingPath = RestreamFfmpegLocator.BundledFfmpegPath + ".tmp";
        _logger?.LogInformation(
            "FFmpeg download starting (target: {Target})", RestreamFfmpegLocator.BundledFfmpegPath);
        try
        {
            // 1. Stream the zip to temp with byte-progress.
            yield return new(RestreamAcquirePhase.Downloading, 0, "downloading FFmpeg (~90 MB)…");

            HttpResponseMessage? response = null;
            Stream? source = null;
            FileStream? target = null;
            long? total = null;
            string? failure = null;

            try
            {
                response = await DownloadHttp.GetAsync(DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
                response.EnsureSuccessStatusCode();
                total = response.Content.Headers.ContentLength;
                source = await response.Content.ReadAsStreamAsync(ct);
                target = new FileStream(
                    zipPath, FileMode.Create, FileAccess.Write, FileShare.None,
                    DownloadChunkBytes, useAsync: true);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                failure = ex.Message;
            }

            long written = 0;
            if (failure is null && source is not null && target is not null)
            {
                var buffer = new byte[DownloadChunkBytes];
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
                        failure = ex.Message;
                        break;
                    }

                    if (read == 0)
                    {
                        break;
                    }

                    yield return new(
                        RestreamAcquirePhase.Downloading,
                        total is > 0 ? (int)(written * 100 / total.Value) : -1,
                        "downloading FFmpeg (~90 MB)…");
                }
            }

            source?.Dispose();
            target?.Dispose();
            response?.Dispose();

            if (failure is not null)
            {
                _logger?.LogError("FFmpeg download failed: {Failure}", failure);
                yield return Failed($"downloading FFmpeg failed: {failure}");
                yield break;
            }

            _logger?.LogInformation("FFmpeg zip downloaded ({Bytes} bytes) — extracting", written);

            // 2. Extract only bin/ffmpeg.exe (the build is static — one file is
            //    the whole runtime) into appdata/bin/ffmpeg/.
            yield return new(RestreamAcquirePhase.Extracting, 0, "extracting ffmpeg.exe…");

            string? extractFailure = null;
            bool sawEntry = false;
            try
            {
                await using var zipStream = new FileStream(zipPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);
                // The zip wraps everything in "ffmpeg-<version>-essentials_build/".
                var entry = archive.Entries.FirstOrDefault(e =>
                    e.FullName.Replace('\\', '/').EndsWith("/bin/ffmpeg.exe", StringComparison.OrdinalIgnoreCase));
                if (entry is null)
                {
                    extractFailure = "the downloaded FFmpeg archive has no bin/ffmpeg.exe";
                }
                else
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(RestreamFfmpegLocator.BundledFfmpegPath)!);
                    entry.ExtractToFile(stagingPath, overwrite: true);
                    File.Move(stagingPath, RestreamFfmpegLocator.BundledFfmpegPath, overwrite: true);
                    sawEntry = true;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                extractFailure = ex.Message;
            }

            if (extractFailure is not null)
            {
                _logger?.LogError("FFmpeg extraction failed: {Failure}", extractFailure);
                yield return Failed($"extracting FFmpeg failed: {extractFailure}");
                yield break;
            }

            // 3. The verdict is the locator.
            var installed = sawEntry && File.Exists(RestreamFfmpegLocator.BundledFfmpegPath);
            if (installed)
            {
                try
                {
                    _logger?.LogInformation(
                        "FFmpeg installed at {Path} ({Bytes} bytes)",
                        RestreamFfmpegLocator.BundledFfmpegPath,
                        new FileInfo(RestreamFfmpegLocator.BundledFfmpegPath).Length);
                }
                catch (Exception)
                {
                    _logger?.LogInformation("FFmpeg installed at {Path}", RestreamFfmpegLocator.BundledFfmpegPath);
                }
            }
            else
            {
                _logger?.LogError(
                    "FFmpeg extraction finished but {Path} does not exist afterwards (file may have been removed by antivirus)",
                    RestreamFfmpegLocator.BundledFfmpegPath);
            }

            yield return installed
                ? new(RestreamAcquirePhase.Done, 100, "FFmpeg installed")
                : Failed("extraction finished but ffmpeg.exe was not found afterwards.");
        }
        finally
        {
            try { if (File.Exists(zipPath)) File.Delete(zipPath); } catch { }
            try { if (File.Exists(stagingPath)) File.Delete(stagingPath); } catch { }
        }
    }

    private static RestreamAcquireProgress Failed(string detail) => new(RestreamAcquirePhase.Failed, 0, detail);

    private static HttpClient CreateDownloadClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("VyralBot");
        client.Timeout = TimeSpan.FromMinutes(15);
        return client;
    }
}
