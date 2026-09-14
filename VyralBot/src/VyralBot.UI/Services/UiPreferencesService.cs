using System.IO;
using System.Text.Json;
using VyralBot.UI.Models;

namespace VyralBot.UI.Services;

/// <summary>Persists app-level UI preferences (theme choice) to appdata/ui-settings.json.</summary>
public sealed class UiPreferencesService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    private readonly Func<string> _settingsPathFactory;

    public UiPreferencesService(Func<string>? settingsPathFactory = null)
        => _settingsPathFactory = settingsPathFactory ?? GetSettingsPath;

    /// <summary>Path of the live settings file in the per-user data root.</summary>
    public static string GetSettingsPath()
    {
        var appdata = VyralBot.Core.Services.AppDataRoot.GetRoot();
        return Path.Combine(appdata, "ui-settings.json");
    }

    public UiPreferences Load()
    {
        try
        {
            var path = _settingsPathFactory();
            if (!File.Exists(path))
            {
                AdoptLegacySettings(path);
                if (!File.Exists(path)) return CloneDefaults();
            }
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<UiPreferences>(json, JsonOptions) ?? CloneDefaults();
        }
        catch (Exception)
        {
            return CloneDefaults(); // corrupt/unreadable → defaults, never crash startup
        }
    }

    /// <summary>
    /// Carries ui-settings.json over from a previous-layout location (an
    /// appdata folder next to the exe, or any parent folder's) so the saved
    /// theme survives the data-root change. Best-effort, runs once per launch
    /// at most (only while the target file is missing).
    /// </summary>
    private static void AdoptLegacySettings(string targetPath)
    {
        try
        {
            var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (dir is not null)
            {
                var candidate = Path.Combine(dir.FullName, "appdata", "ui-settings.json");
                if (File.Exists(candidate))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
                    File.Copy(candidate, targetPath, overwrite: false);
                    return;
                }

                dir = dir.Parent;
            }
        }
        catch (Exception)
        {
            // Missing or unreadable legacy settings → defaults; never crash startup.
        }
    }

    /// <summary>
    /// Copy of <see cref="UiPreferences.Defaults"/>. The static Defaults instance is shared,
    /// and callers (ThemeManager) mutate the loaded preferences — returning Defaults itself
    /// would pollute the defaults for the whole session.
    /// </summary>
    private static UiPreferences CloneDefaults() => new()
    {
        ThemeId = UiPreferences.Defaults.ThemeId,
        Custom = new CustomThemeSettings
        {
            BaseTheme = UiPreferences.Defaults.Custom.BaseTheme,
            PrimaryHex = UiPreferences.Defaults.Custom.PrimaryHex,
            LogoVariant = UiPreferences.Defaults.Custom.LogoVariant,
        },
        DarkShadeLevel = UiPreferences.Defaults.DarkShadeLevel,
        LightShadeLevel = UiPreferences.Defaults.LightShadeLevel,
    };

    public void Save(UiPreferences preferences)
    {
        var path = _settingsPathFactory();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(preferences, JsonOptions));
        File.Move(tmp, path, overwrite: true);
    }
}
