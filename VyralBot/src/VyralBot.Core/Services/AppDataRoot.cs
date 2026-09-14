using Microsoft.Extensions.Logging;

namespace VyralBot.Core.Services;

/// <summary>
/// Resolves Vyral's data root. The root is the standard per-user application
/// folder (%LOCALAPPDATA%\VyralBot) — it never moves with the executable, so
/// re-downloading or relocating Vyral.exe can never "lose" profiles or platform
/// connections. Data written by previous layouts (an "appdata" folder next to
/// the exe, or the old fixed-walk locations like C:\appdata) is adopted once,
/// automatically, on first launch.
/// </summary>
public static class AppDataRoot
{
    /// <summary>The per-user data root: stable regardless of where the exe sits.</summary>
    public static string GetRoot() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "VyralBot");

    /// <summary>
    /// Moves profiles from any legacy location into the current root when the
    /// root has no profiles yet. When several legacy locations have data, the
    /// most recently used one wins. One-time, best-effort: failures are logged,
    /// never thrown. Returns the legacy location that was adopted, or null.
    /// </summary>
    public static string? MigrateLegacyData(
        ILogger? logger,
        string? root = null,
        IReadOnlyList<string>? legacyCandidates = null)
    {
        root ??= GetRoot();
        legacyCandidates ??= DefaultLegacyCandidates();
        var targetProfiles = Path.Combine(root, "profiles");

        // Only migrate into an empty root — never touch existing data.
        if (Directory.Exists(targetProfiles)
            && Directory.EnumerateFileSystemEntries(targetProfiles).Any())
        {
            return null;
        }

        // The most recently used legacy location with profiles wins — an exe
        // that was moved between folders may have left several behind.
        var candidates = legacyCandidates
            .Where(c => HasEntries(Path.Combine(c, "profiles")))
            .Select(c => (Path: c, Activity: LastProfileActivity(Path.Combine(c, "profiles"))))
            .OrderByDescending(x => x.Activity)
            .ToList();

        foreach (var (candidate, _) in candidates)
        {
            try
            {
                var candidateProfiles = Path.Combine(candidate, "profiles");
                Directory.CreateDirectory(targetProfiles);

                foreach (var profileDir in Directory.GetDirectories(candidateProfiles))
                {
                    var destination = Path.Combine(targetProfiles, Path.GetFileName(profileDir));
                    if (!Directory.Exists(destination))
                    {
                        Directory.Move(profileDir, destination);
                    }
                }

                // Loose root-level files (current_profile.txt, ui settings…).
                foreach (var file in Directory.GetFiles(candidate))
                {
                    var fileName = Path.GetFileName(file);
                    if (fileName.Equals("current_profile.txt", StringComparison.OrdinalIgnoreCase))
                    {
                        // Only adopt the pointer when its profile actually
                        // migrated — a dangling one would break startup.
                        var profileName = File.ReadAllText(file).Trim();
                        if (profileName.Length == 0
                            || !Directory.Exists(Path.Combine(targetProfiles, profileName)))
                        {
                            logger?.LogInformation(
                                "Skipping legacy {File} — its profile did not migrate", fileName);
                            continue;
                        }
                    }

                    var destinationFile = Path.Combine(root, fileName);
                    if (!File.Exists(destinationFile))
                    {
                        File.Move(file, destinationFile);
                    }
                }

                logger?.LogInformation(
                    "Adopted existing profiles from previous data location {Legacy} into {Root}",
                    candidate, root);
                return candidate;
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex,
                    "Failed to migrate profiles from legacy location {Legacy}", candidate);
            }
        }

        return null;
    }

    /// <summary>Legacy "appdata" locations previous layouts could have used, in preference order.</summary>
    private static IReadOnlyList<string> DefaultLegacyCandidates()
    {
        var candidates = new List<string>
        {
            // appdata next to the executable (the portable layout this exe
            // layout used most recently).
            Path.Combine(AppContext.BaseDirectory, "appdata"),
        };

        // The old fixed-walk heuristic's answer for THIS exe location (dev:
        // solution root; published: varies with install depth — C:\appdata,
        // C:\Users\appdata, …).
        try
        {
            var walkUp = Directory
                .GetParent(AppContext.BaseDirectory)?
                .Parent?.Parent?.Parent?.Parent?.Parent?.FullName;
            if (walkUp is not null)
            {
                candidates.Add(Path.Combine(walkUp, "appdata"));
            }
        }
        catch (Exception)
        {
            // Shallow path — the walk ran past the root; nothing to adopt there.
        }

        // What a shallow published path resolved to.
        var driveRoot = Path.GetPathRoot(AppContext.BaseDirectory);
        if (!string.IsNullOrEmpty(driveRoot))
        {
            candidates.Add(Path.Combine(driveRoot, "appdata"));
        }

        return candidates
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Newest write time anywhere directly inside a profiles folder.</summary>
    private static DateTime LastProfileActivity(string profilesDir)
    {
        var latest = DateTime.MinValue;
        try
        {
            latest = Directory.GetLastWriteTimeUtc(profilesDir);
            foreach (var dir in Directory.GetDirectories(profilesDir))
            {
                var time = Directory.GetLastWriteTimeUtc(dir);
                if (time > latest)
                {
                    latest = time;
                }
            }
        }
        catch (Exception)
        {
            // Unreadable candidate — its timestamp just sorts lowest.
        }

        return latest;
    }

    private static bool HasEntries(string directory) =>
        Directory.Exists(directory) && Directory.EnumerateFileSystemEntries(directory).Any();
}
