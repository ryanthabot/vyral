using System.Diagnostics;

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
    /// <summary>Root used for app-portable storage — same walk-up as ProfileService.</summary>
    public static string GetAppDataRoot()
    {
        var baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
        // Identical to ProfileService: six hops from bin/Debug/net10.0-windows
        // lands on the VyralBot root; from a published single-file layout the
        // walk runs past the drive root, returns null, and the exe folder is
        // used instead (appdata next to Vyral.exe).
        var projectRoot = Directory.GetParent(baseDirectory)?.Parent?.Parent?.Parent?.Parent?.Parent?.FullName;
        return Path.Combine(projectRoot ?? baseDirectory, "appdata");
    }

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
    /// the singleton.
    /// </summary>
    public RestreamFfmpegInstall? Locate(string? overridePath = null)
    {
        if (!string.IsNullOrWhiteSpace(overridePath) && File.Exists(overridePath))
        {
            return new RestreamFfmpegInstall(overridePath, "custom path", TryGetVersion(overridePath));
        }

        foreach (var candidate in CandidateFfmpegPaths())
        {
            if (File.Exists(candidate))
            {
                return new RestreamFfmpegInstall(candidate, "bundled download", TryGetVersion(candidate));
            }
        }

        var onPath = FindOnPath();
        if (onPath is not null)
        {
            return new RestreamFfmpegInstall(onPath, "system PATH", TryGetVersion(onPath));
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

    /// <summary>Runs <c>ffmpeg -version</c> and returns the first line (e.g. "ffmpeg version 7.1…").</summary>
    public static string? TryGetVersion(string ffmpegPath, int timeoutMs = 5000)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = "-version",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
            });
            if (process is null)
            {
                return null;
            }

            string? firstLine = null;
            if (process.WaitForExit(timeoutMs))
            {
                firstLine = process.StandardOutput.ReadLine()?.Trim();
            }
            else
            {
                try { process.Kill(entireProcessTree: true); } catch { }
            }

            return firstLine;
        }
        catch
        {
            return null;
        }
    }
}
