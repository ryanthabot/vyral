using Microsoft.Extensions.Logging;

namespace VyralBot.Core.Services;

/// <summary>
/// Resolves Vyral's portable data root ("appdata") deterministically. The old
/// heuristic — walking up a fixed number of parent directories — produced a
/// different location for every install depth (on some machines literally
/// "C:\Users\appdata", which is uncreatable without admin and crashed
/// startup). Instead: use "appdata" next to the executable when that location
/// is writable (true portable behavior for the downloaded single-file exe),
/// and fall back to %LOCALAPPDATA%\VyralBot when it is not (e.g. Program
/// Files). Existing data from the old heuristic's locations is migrated once.
/// </summary>
public static class AppDataRoot
{
    /// <summary>Writable data root for profiles, credentials and bundled tools.</summary>
    public static string GetRoot() => Resolve();

    public static string Resolve()
    {
        // 1. Portable layout: appdata next to the executable. Probed for real
        //    write access — an exe launched from a read-only folder must not
        //    select a root it cannot create.
        var portable = Path.Combine(AppContext.BaseDirectory, "appdata");
        if (IsWritableDirectory(portable))
        {
            return portable;
        }

        // 2. Per-user fallback — always creatable without elevation.
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "VyralBot");
    }

    /// <summary>
    /// Copies profiles from the old heuristic's locations into the current
    /// root when the root has no profiles yet. One-time, best-effort: failures
    /// are logged, never thrown. Returns true when a migration ran.
    /// </summary>
    public static bool MigrateLegacyData(
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
            return false;
        }

        foreach (var candidate in legacyCandidates)
        {
            var candidateProfiles = Path.Combine(candidate, "profiles");
            if (!HasEntries(candidateProfiles))
            {
                continue;
            }

            try
            {
                Directory.CreateDirectory(targetProfiles);
                foreach (var profileDir in Directory.GetDirectories(candidateProfiles))
                {
                    var destination = Path.Combine(targetProfiles, Path.GetFileName(profileDir));
                    if (!Directory.Exists(destination))
                    {
                        Directory.Move(profileDir, destination);
                    }
                }

                foreach (var file in Directory.GetFiles(candidate))
                {
                    var fileName = Path.GetFileName(file);
                    // A stale current_profile.txt pointing at a profile that
                    // failed to move would be worse than none — files move
                    // only when the destination is free, so this is safe.
                    if (fileName.Equals("current_profile.txt", StringComparison.OrdinalIgnoreCase))
                    {
                        var hasMatchingProfile = false;
                        var profileName = File.ReadAllText(file).Trim();
                        if (profileName.Length > 0)
                        {
                            hasMatchingProfile = Directory.Exists(Path.Combine(targetProfiles, profileName));
                        }

                        if (!hasMatchingProfile)
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
                    "Migrated existing profiles from legacy location {Legacy} into {Root}",
                    candidate, root);
                return true;
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex,
                    "Failed to migrate profiles from legacy location {Legacy}", candidate);
            }
        }

        return false;
    }

    /// <summary>Locations the old fixed-walk heuristic could have produced.</summary>
    private static IReadOnlyList<string> DefaultLegacyCandidates()
    {
        var candidates = new List<string>();

        // The old heuristic's answer for THIS exe location (dev: solution root;
        // published: varies with install depth — C:\, C:\Users, ...).
        var walkUp = TryLegacyWalkUpRoot();
        if (walkUp is not null)
        {
            candidates.Add(walkUp);
        }

        // What a shallow published path (e.g. Desktop\Vyral) resolved to.
        var driveRoot = Path.GetPathRoot(AppContext.BaseDirectory);
        if (!string.IsNullOrEmpty(driveRoot))
        {
            candidates.Add(Path.Combine(driveRoot, "appdata"));
        }

        return candidates;
    }

    private static string? TryLegacyWalkUpRoot()
    {
        try
        {
            // Identical expression to the old ProfileService: GetParent plus
            // five .Parent steps (the first GetParent only strips the trailing
            // separator of BaseDirectory).
            var projectRoot = Directory
                .GetParent(AppContext.BaseDirectory)?
                .Parent?.Parent?.Parent?.Parent?.Parent?.FullName;
            return projectRoot is null ? null : Path.Combine(projectRoot, "appdata");
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static bool IsWritableDirectory(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            var probe = Path.Combine(directory, $".write-probe-{Guid.NewGuid():N}.tmp");
            File.WriteAllText(probe, string.Empty);
            File.Delete(probe);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool HasEntries(string directory) =>
        Directory.Exists(directory) && Directory.EnumerateFileSystemEntries(directory).Any();
}
