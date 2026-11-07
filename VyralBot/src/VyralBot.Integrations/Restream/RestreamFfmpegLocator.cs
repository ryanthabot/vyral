using System.Diagnostics;
using VyralBot.Core.Services.ProcessManagement;

namespace VyralBot.Integrations.Restream;

/// <summary>A resolvable FFmpeg executable: where it was found.</summary>
public sealed record RestreamFfmpegInstall(string FfmpegPath, string Source, string? Version);

/// <summary>
/// Resolves the FFmpeg binary used by the restream engine. Resolution order:
/// an explicit user override path, the copy Vyral downloaded into the portable
/// appdata tree, then the system PATH. Mirrors ObsInstallLocator: an explicit
/// override is authoritative and there is no silent fallback below it.
/// </summary>
public sealed class RestreamFfmpegLocator
{
    private readonly IProcessManager _processManager;

    /// <param name="processManager">
    /// Spawns the tracked <c>ffmpeg -version</c> probe (zero-orphan policy —
    /// no direct <c>Process.Start</c> anywhere in the restream stack). Same
    /// wiring as <see cref="RestreamEngine"/>.
    /// </param>
    public RestreamFfmpegLocator(IProcessManager processManager)
    {
        _processManager = processManager ?? throw new ArgumentNullException(nameof(processManager));
    }

    /// <summary>
    /// Root used for app-portable storage — the same deterministic resolution
    /// as ProfileService (appdata next to the exe when writable, else
    /// %LOCALAPPDATA%\VyralBot), so a downloaded FFmpeg is found no matter
    /// where the exe sits.
    /// </summary>
    public static string GetAppDataRoot() => VyralBot.Core.Services.AppDataRoot.GetRoot();

    public static string BundledFfmpegPath => Path.Combine(GetAppDataRoot(), "bin", "ffmpeg", "ffmpeg.exe");

    /// <summary>
    /// Every bundled location a Vyral install may have extracted FFmpeg into,
    /// most specific first. The appdata root moves with the launch layout
    /// (exe-adjacent for the published build, solution-root for dev runs, and
    /// the drive root resolves identically for the published layout) — probing
    /// all of them means one download stays found whichever shortcut starts
    /// the app.
    /// </summary>
    private static IEnumerable<string> CandidateFfmpegPaths()
    {
        yield return BundledFfmpegPath;

        // appdata next to the executable (published single-file layout).
        yield return Path.Combine(AppContext.BaseDirectory, "appdata", "bin", "ffmpeg", "ffmpeg.exe");

        // The 6-level walk from a shallow published path (Desktop\Vyral) lands
        // on the drive root — accept that location for any launch layout.
        var driveRoot = Path.GetPathRoot(AppContext.BaseDirectory);
        if (!string.IsNullOrEmpty(driveRoot))
        {
            yield return Path.Combine(driveRoot, "appdata", "bin", "ffmpeg", "ffmpeg.exe");
        }
    }

    /// <summary>
    /// Locates FFmpeg. The override comes from the user's saved settings (passed
    /// by the caller), not the constructor, so changes apply without re-creating
    /// the singleton. The only blocking step (the version probe's exit wait)
    /// stays on the thread pool, so this is safe to await from the UI thread.
    /// </summary>
    public async Task<RestreamFfmpegInstall?> LocateAsync(string? overridePath = null, CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(overridePath) && File.Exists(overridePath))
        {
            return new RestreamFfmpegInstall(overridePath, "custom path", await TryGetVersionAsync(overridePath, ct: ct).ConfigureAwait(false));
        }

        foreach (var candidate in CandidateFfmpegPaths())
        {
            if (File.Exists(candidate))
            {
                return new RestreamFfmpegInstall(candidate, "bundled download", await TryGetVersionAsync(candidate, ct: ct).ConfigureAwait(false));
            }
        }

        var onPath = FindOnPath();
        if (onPath is not null)
        {
            return new RestreamFfmpegInstall(onPath, "system PATH", await TryGetVersionAsync(onPath, ct: ct).ConfigureAwait(false));
        }

        return null;
    }

    /// <summary>First directory on PATH that contains ffmpeg.exe (no process spawn needed).</summary>
    public static string? FindOnPath()
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        foreach (var directory in path.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                var candidate = Path.Combine(directory, "ffmpeg.exe");
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch (ArgumentException)
            {
                // Malformed PATH entry — skip it.
            }
        }

        return null;
    }

    /// <summary>
    /// Runs a tracked <c>ffmpeg -version</c> probe and returns the first line
    /// (e.g. "ffmpeg version 7.1…"). Best-effort: any failure (missing exe,
    /// disposal race with the manager's exit handler, hung process) yields
    /// null — the version string is cosmetic, never load-bearing.
    /// </summary>
    public async Task<string?> TryGetVersionAsync(string ffmpegPath, int timeoutMs = 5000, CancellationToken ct = default)
    {
        try
        {
            var startInfo = new ProcessStartInfo(ffmpegPath)
            {
                Arguments = "-version",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
            };

            // Tracked spawn: the manager owns the process lifetime and disposes
            // the Process object the moment it exits, so nothing here may touch
            // the object after the exit wait — only the pid and the stream
            // reader captured up front.
            var process = await _processManager.StartProcessAsync(startInfo, ct).ConfigureAwait(false);
            var pid = process.Id;

            // Start the read FIRST, then wait for exit: ffmpeg writes its
            // version line before exiting, so an in-flight read drains the
            // buffered pipe data even though the exit handler disposes the
            // Process right after.
            var firstLineTask = process.StandardOutput.ReadLineAsync(ct);
            var exited = await Task.Run(() => process.WaitForExit(timeoutMs), CancellationToken.None).ConfigureAwait(false);

            if (!exited)
            {
                // Broken exe that hangs instead of printing — reap it so the
                // probe never leaves a tracked child behind.
                await _processManager.KillProcessAsync(pid, ct).ConfigureAwait(false);
                return null;
            }

            return (await firstLineTask.ConfigureAwait(false))?.Trim();
        }
        catch
        {
            return null;
        }
    }
}
