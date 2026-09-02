using System.IO;
using VyralBot.UI.Models;
using VyralBot.UI.Services;
using Xunit;

namespace VyralBot.UI.Tests;

public class UiPreferencesServiceTests : IDisposable
{
    private readonly string _dir;

    public UiPreferencesServiceTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "vyral-theme-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private static UiPreferencesService MakeService(Func<string> pathFactory) => new(pathFactory);

    [Fact]
    public void Load_returns_defaults_when_file_missing()
    {
        var prefs = MakeService(() => Path.Combine(_dir, "missing.json")).Load();
        Assert.Equal(ThemeCatalog.NativeId, prefs.ThemeId);
    }

    [Fact]
    public void Save_then_Load_round_trips()
    {
        var path = Path.Combine(_dir, "ui-settings.json");
        var svc = MakeService(() => path);
        var prefs = new UiPreferences
        {
            ThemeId = "crimson",
            Custom = new CustomThemeSettings { BaseTheme = "Light", PrimaryHex = "#FF0000", LogoVariant = "red" },
        };
        svc.Save(prefs);

        var loaded = MakeService(() => path).Load();
        Assert.Equal("crimson", loaded.ThemeId);
        Assert.Equal("Light", loaded.Custom.BaseTheme);
        Assert.Equal("#FF0000", loaded.Custom.PrimaryHex);
        Assert.Equal("red", loaded.Custom.LogoVariant);
    }

    [Fact]
    public void Load_returns_defaults_for_corrupt_file()
    {
        var path = Path.Combine(_dir, "ui-settings.json");
        File.WriteAllText(path, "{ not valid json !!");
        var prefs = MakeService(() => path).Load();
        Assert.Equal(ThemeCatalog.NativeId, prefs.ThemeId);
    }

    [Fact]
    public void Load_reads_camel_case_json()
    {
        var path = Path.Combine(_dir, "ui-settings.json");
        File.WriteAllText(path,
            """{ "themeId": "crimson", "custom": { "baseTheme": "Light", "primaryHex": "#FF0000", "logoVariant": "red" } }""");
        var prefs = MakeService(() => path).Load();
        Assert.Equal("crimson", prefs.ThemeId);
        Assert.Equal("Light", prefs.Custom.BaseTheme);
        Assert.Equal("red", prefs.Custom.LogoVariant);
    }

    [Fact]
    public void Save_creates_missing_directory()
    {
        var path = Path.Combine(_dir, "nested", "deeper", "ui-settings.json");
        MakeService(() => path).Save(new UiPreferences { ThemeId = "dark" });
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void Save_writes_no_tmp_leftover()
    {
        var path = Path.Combine(_dir, "ui-settings.json");
        MakeService(() => path).Save(new UiPreferences { ThemeId = "dark" });
        Assert.False(File.Exists(path + ".tmp"));
    }
}
