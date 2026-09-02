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

    /// <summary>Path of the live settings file: &lt;repoRoot&gt;/appdata/ui-settings.json.</summary>
    public static string GetSettingsPath()
    {
        var appdata = FindAppdataDirectory();
        return Path.Combine(appdata, "ui-settings.json");
    }

    public UiPreferences Load()
    {
        try
        {
            var path = _settingsPathFactory();
            if (!File.Exists(path)) return UiPreferences.Defaults;
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<UiPreferences>(json, JsonOptions) ?? UiPreferences.Defaults;
        }
        catch (Exception)
        {
            return UiPreferences.Defaults; // corrupt/unreadable → defaults, never crash startup
        }
    }

    public void Save(UiPreferences preferences)
    {
        var path = _settingsPathFactory();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(preferences, JsonOptions));
        File.Move(tmp, path, overwrite: true);
    }

    /// <summary>Walks up from the exe directory looking for the appdata folder (portable layout).</summary>
    private static string FindAppdataDirectory()
    {
        var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "appdata");
            if (Directory.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }

        var fallback = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appdata");
        Directory.CreateDirectory(fallback);
        return fallback;
    }
}
