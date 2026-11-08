using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;

namespace VyralBot.Integrations.Restream;

/// <summary>Progress of downloading and extracting the bundled FFmpeg.</summary>
public enum RestreamAcquirePhase
{
    /// <summary>The zip is streaming to the temp directory (includes checksum verification).</summary>
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
/// Downloads the official gyan.dev FFmpeg "essentials" build (a static GPL
/// build shipping x264) and extracts only ffmpeg.exe into Vyral's portable
/// appdata tree. Modeled on ObsAcquisitionService: every step is wrapped and
/// failures surface as a terminal <see cref="RestreamAcquirePhase.Failed"/>
/// progress record, never as an exception (only cancellation propagates). All
/// yields sit in try-free scopes (C# forbids them in try-with-catch) — each
/// phase captures its failure into a local instead.
///
/// Integrity: the artifact is pinned — one immutable versioned URL plus its
/// SHA-256 — so a truncated or tampered zip can never be extracted. The
/// rolling "release-essentials" URL changes bytes with every FFmpeg release
/// and cannot be hash-pinned; the versioned package under /packages/ never
/// changes once published. To ship a newer FFmpeg, update the three pinned
/// constants below (URL + SHA-256 + byte sizes) together.
/// </summary>
public sealed class RestreamFfmpegAcquisitionService
{
    private const int DownloadChunkBytes = 4 * 1024 * 1024;

    /// <summary>
    /// Full-restart retries for the zip download (a dropped connection starts
    /// the ~110 MB transfer over; HTTP range resume would need server
    /// cooperation and is not worth the complexity here).
    /// </summary>
    private const int MaxDownloadAttempts = 3;

    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Pinned to gyan.dev's immutable versioned package for FFmpeg 9.0.2
    /// essentials (the official Windows distribution referenced from
    /// ffmpeg.org; GPL build that includes x264).
    /// </summary>
    public const string DownloadUrl = "https://www.gyan.dev/ffmpeg/builds/packages/ffmpeg-9.0.2-essentials_build.zip";

    /// <summary>
    /// SHA-256 of the pinned zip, computed from the downloaded artifact and
    /// cross-checked against the publisher's sidecar
    /// (<c>ffmpeg-9.0.2-essentials_build.zip.sha256</c>) on 2026-11-07.
    /// The install aborts unless the download hashes to exactly this value.
    /// </summary>
    public const string ExpectedSha256 = "60f467265b1e312373dbcd92200c2618a74850f98d3d078e94296bb3fa2047ba";

    /// <summary>Exact size of the pinned zip (disk-space floor for the temp drive).</summary>
    public const long ExpectedZipBytes = 114_768_076;

    /// <summary>Exact size of ffmpeg.exe inside the zip (disk-space floor for the install drive).</summary>
    public const long ExpectedFfmpegBytes = 105_423_872;

    /// <summary>Headroom on top of the pinned sizes so the check never rejects a nearly-full-but-sufficient disk.</summary>
    private const long FreeSpaceMarginBytes = 64 * 1024 * 1024;

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
            "FFmpeg download starting (source: {Source}, target: {Target})",
            DownloadUrl,
            RestreamFfmpegLocator.BundledFfmpegPath);
        try
        {
            // 0. Disk-space preflight on both drives the install touches —
            //    fail fast with a clear message instead of dying mid-download.
            yield return new(RestreamAcquirePhase.Downloading, 0, "checking disk space…");

            var spaceProblem =
                DiskSpaceProblem(Path.GetTempPath(), ExpectedZipBytes + FreeSpaceMarginBytes)
                ?? DiskSpaceProblem(RestreamFfmpegLocator.BundledFfmpegPath, ExpectedFfmpegBytes + FreeSpaceMarginBytes);
            if (spaceProblem is not null)
            {
                _logger?.LogError("FFmpeg install aborted — not enough disk space: {Problem}", spaceProblem);
                yield return Failed($"not enough disk space: {spaceProblem}");
                yield break;
            }

            // 1. Stream the zip to temp with byte-progress; a dropped or
            //    truncated transfer is retried up to MaxDownloadAttempts times.
            long written = 0;
            string? failure = null;

            for (var attempt = 1; attempt <= MaxDownloadAttempts; attempt++)
            {
                if (attempt > 1)
                {
                    yield return new(
                        RestreamAcquirePhase.Downloading,
                        0,
                        $"download failed — retrying (attempt {attempt} of {MaxDownloadAttempts})…");
                    await Task.Delay(RetryDelay, ct);
                }

                yield return new(RestreamAcquirePhase.Downloading, 0, "downloading FFmpeg (~110 MB)…");

                HttpResponseMessage? response = null;
                Stream? source = null;
                FileStream? target = null;
                long? total = null;
                string? attemptFailure = null;

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
                    attemptFailure = ex.Message;
                }

                long attemptWritten = 0;
                if (attemptFailure is null && source is not null && target is not null)
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
                                attemptWritten += read;
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            attemptFailure = ex.Message;
                            break;
                        }

                        if (read == 0)
                        {
                            break;
                        }

                        // Percent from Content-Length when present, else from the
                        // pinned size (capped at 99 — 100 is only for a verified zip).
                        var reference = total is > 0 ? total.Value : ExpectedZipBytes;
                        yield return new(
                            RestreamAcquirePhase.Downloading,
                            (int)Math.Min(99, attemptWritten * 100 / reference),
                            "downloading FFmpeg (~110 MB)…");
                    }
                }

                source?.Dispose();
                target?.Dispose();
                response?.Dispose();

                if (attemptFailure is null)
                {
                    var expected = total is > 0 ? total.Value : ExpectedZipBytes;
                    if (attemptWritten != expected)
                    {
                        // Server closed the connection early — retryable.
                        attemptFailure = $"connection closed early ({attemptWritten:N0} of {expected:N0} bytes)";
                    }
                    else
                    {
                        written = attemptWritten;
                        break;
                    }
                }

                _logger?.LogWarning(
                    "FFmpeg download attempt {Attempt}/{Max} failed: {Failure}",
                    attempt,
                    MaxDownloadAttempts,
                    attemptFailure);
                failure = attemptFailure;
            }

            if (failure is not null)
            {
                _logger?.LogError("FFmpeg download failed after {Max} attempts: {Failure}", MaxDownloadAttempts, failure);
                yield return Failed($"downloading FFmpeg failed after {MaxDownloadAttempts} attempts: {failure}");
                yield break;
            }

            // 2. Integrity gate: exact SHA-256 against the pinned constant. A
            //    corrupted or altered zip is a terminal failure (retrying
            //    would just re-download the same bad bytes if the source went
            //    bad; the user is told to try again later or browse manually).
            yield return new(RestreamAcquirePhase.Downloading, 100, "verifying download (SHA-256)…");

            string? hashFailure = null;
            try
            {
                await using var verifyStream = new FileStream(zipPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                var actual = Convert.ToHexString(await SHA256.HashDataAsync(verifyStream, ct));
                if (!string.Equals(actual, ExpectedSha256, StringComparison.OrdinalIgnoreCase))
                {
                    hashFailure =
                        $"checksum mismatch — the downloaded file does not match the pinned FFmpeg 9.0.2 build "
                        + $"(expected {ExpectedSha256[..12]}…, got {actual[..12]}…). Try again later or browse to a local ffmpeg.exe.";
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                hashFailure = ex.Message;
            }

            if (hashFailure is not null)
            {
                _logger?.LogError("FFmpeg checksum verification failed: {Failure}", hashFailure);
                yield return Failed($"verifying the FFmpeg download failed: {hashFailure}");
                yield break;
            }

            _logger?.LogInformation(
                "FFmpeg zip downloaded and verified ({Bytes} bytes, SHA-256 OK) — extracting", written);

            // 3. Extract only bin/ffmpeg.exe (the build is static — one file is
            //    the whole runtime) into appdata/bin/ffmpeg/. Extract to a
            //    ".tmp" stage and move over the final name only once complete,
            //    so no half-extracted ffmpeg.exe is ever visible to the locator.
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

            // 4. The verdict is the locator.
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

    /// <summary>
    /// Returns a user-facing description when the drive holding
    /// <paramref name="path"/> has less than <paramref name="requiredBytes"/>
    /// free, else <c>null</c>. Best-effort — an unprobeable path passes.
    /// </summary>
    private static string? DiskSpaceProblem(string path, long requiredBytes)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(path));
            if (string.IsNullOrEmpty(root))
            {
                return null;
            }

            var drive = new DriveInfo(root);
            if (!drive.IsReady)
            {
                return $"drive {root} is not ready";
            }

            if (drive.AvailableFreeSpace < requiredBytes)
            {
                return $"{root} has {drive.AvailableFreeSpace / 1024 / 1024:N0} MB free, but the FFmpeg install needs {requiredBytes / 1024 / 1024:N0} MB";
            }
        }
        catch (Exception)
        {
            // Best-effort check — never block the install on a probe failure.
        }

        return null;
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
