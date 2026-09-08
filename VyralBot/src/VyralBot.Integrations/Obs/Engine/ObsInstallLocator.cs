namespace VyralBot.Integrations.Obs.Engine;

/// <summary>Resolved paths of a locally installed OBS Studio.</summary>
public sealed record ObsInstall(
    string Root,
    string BinDir,
    string ObsDllPath,
    string PluginsBinDir,
    string PluginsDataDir,
    string? UserPluginsBinDir,
    string? UserPluginsDataDir,
    string ConfigDir,
    string SceneCollectionsDir,
    string ProfilesDir);

/// <summary>
/// Finds the installed OBS Studio. An explicit override root is authoritative:
/// when one is set, only that directory is validated — there is no silent
/// fallback to the default install locations.
/// </summary>
public sealed class ObsInstallLocator
{
    private readonly string? overrideRoot;

    public ObsInstallLocator(string? overrideRoot = null)
    {
        this.overrideRoot = overrideRoot;
    }

    public ObsInstall? Locate()
    {
        if (!string.IsNullOrWhiteSpace(overrideRoot))
        {
            return LocateIn(overrideRoot);
        }

        foreach (var candidate in DefaultRoots())
        {
            if (LocateIn(candidate) is { } install)
            {
                return install;
            }
        }

        return null;
    }

    private static IEnumerable<string> DefaultRoots()
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (programFiles.Length > 0)
        {
            yield return Path.Combine(programFiles, "obs-studio");
        }

        // An x86 install cannot be loaded by this 64-bit process; the obs.dll
        // validation below rules it out on its own.
        var programFilesX86 = Environment.GetEnvironmentVariable("ProgramFiles(x86)");
        if (!string.IsNullOrWhiteSpace(programFilesX86))
        {
            yield return Path.Combine(programFilesX86, "obs-studio");
        }
    }

    private static ObsInstall? LocateIn(string root)
    {
        string binDir;
        var bin64 = Path.Combine(root, "bin", "64bit");
        if (File.Exists(Path.Combine(bin64, "obs.dll")))
        {
            binDir = bin64;
        }
        else if (File.Exists(Path.Combine(root, "obs.dll")))
        {
            binDir = root; // layout where the root itself is the bin directory
        }
        else
        {
            return null;
        }

        var configDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "obs-studio");
        var userPluginsBin = Path.Combine(configDir, "obs-plugins", "64bit");
        var userPluginsData = Path.Combine(configDir, "data", "obs-plugins");

        return new ObsInstall(
            Root: root,
            BinDir: binDir,
            ObsDllPath: Path.Combine(binDir, "obs.dll"),
            PluginsBinDir: Path.Combine(root, "obs-plugins", "64bit"),
            PluginsDataDir: Path.Combine(root, "data", "obs-plugins"),
            UserPluginsBinDir: Directory.Exists(userPluginsBin) ? userPluginsBin : null,
            UserPluginsDataDir: Directory.Exists(userPluginsData) ? userPluginsData : null,
            ConfigDir: configDir,
            SceneCollectionsDir: Path.Combine(configDir, "basic", "scenes"),
            ProfilesDir: Path.Combine(configDir, "basic", "profiles"));
    }
}
