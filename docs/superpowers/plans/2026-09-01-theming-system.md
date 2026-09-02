# VyralBot Theming System Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give VyralBot a complete theming system: Light mode, Dark mode, 6 unique built-in themes, a Custom color theme builder, and per-theme logo color variants (file-name driven), all switchable live from Settings → Theme and persisted across restarts.

**Architecture:** WPF DynamicResource color-swapping. Each theme is a ResourceDictionary of `Color` entries only (one canonical key set). A single shared `ThemeBrushes.xaml` defines every `SolidColorBrush`/style once, wrapping colors via `{DynamicResource ...}`. Switching a theme = ThemeManager swaps one merged dictionary in `Application.Resources`; every brush in the app re-resolves automatically. MaterialDesign light/dark base + primary color is switched in parallel via `PaletteHelper`. Theme selection persists to `appdata/ui-settings.json` (app-level, outside any profile). Logo variants resolve by file-name convention (`vyral-logo-{variant}.png`, fallback `vyral-logo.png`) and are published as an `AppLogoSource` resource that views bind with `DynamicResource`.

**Tech Stack:** WPF (.NET 10, `net10.0-windows`), MaterialDesignThemes 5.1.0 (`PaletteHelper`/`ITheme`), CommunityToolkit.Mvvm 8.4, xUnit for pure-logic tests, System.Text.Json for persistence.

## Global Constraints

- Target framework: `net10.0-windows` (do not change).
- The logo **design** never changes — only which color variant PNG is displayed. Variant files the user will add later: `vyral-logo-red.png`, `vyral-logo-blue.png`, `vyral-logo-green.png`, `vyral-logo-white.png`, `vyral-logo-black.png`. `vyral-logo.png` IS the native/teal variant.
- Platform brand colors (Twitch purple `#9146FF`, Kick green `#53FC18`, YouTube red `#FF0000`, TikTok `#00F2EA`/`#FF0050`, Discord `#5865F2`) must remain identical in every theme — they are brands, not theme colors.
- Theme accent/colors may change, but the **color key set** must be identical across all theme dictionaries (any view using `{DynamicResource XBrush}` must never crash after a theme switch).
- Chat badge colors (moderator/sub/VIP/orange) stay constant across themes.
- Keep every legacy brush alias (`VyralRedBrush`, `PrimaryRedBrush`, `CardBackgroundBrush`, `BackgroundDarkBrush`, etc.) — dozens of views already reference them.
- `appdata/ui-settings.json` is app-level (shared across profiles) — theme is a device/user preference, not per-streamer-profile.
- Build must stay at 0 errors after every task. Run `dotnet build VyralBot.sln` from `VyralBot/src` as the gate.
- Git: commit after every task with conventional-commit messages.

## Current State (verified 2026-09-01)

- Solution: `VyralBot/src/VyralBot.sln` — builds clean, 0 errors (baseline commit exists).
- `src/VyralBot.UI/App.xaml` merges: `materialDesign:BundledTheme BaseTheme="Dark" PrimaryColor="Teal"`, MaterialDesign3.Defaults, and `/Resources/Themes/RedSlate.xaml` (which, despite the name, IS the current teal/slate dark theme — 60 Color keys + ~70 brushes + scrollbar styles).
- No theme-switching code exists anywhere (`grep ThemeManager|ApplyTheme|PaletteHelper` → only App.xaml hits).
- `Views/SettingsView.xaml` line ~611: a "Theme" settings category exists with a `"Coming Soon..."` placeholder — this is where the picker UI goes.
- Logo shown in: `MainWindow.xaml` (title bar + window Icon), `Views/ChatView.xaml` (×3, one inside a Style Setter at line 40), `Views/ChatHubView.xaml`, `Views/IntegrationsView.xaml`, `Views/PlatformsView.xaml`, `Views/SettingsView.xaml`.
- Hardcoded-color offenders (hex count): `CurrencyView.xaml` 65 (VS-Code gray palette, zero theme brushes), `FlowEditorView.xaml` 53, `EventHistoryView.xaml` 33, `GiveawayView.xaml` 12, plus ≤6 in several others. `Controls/FlowCanvas/FlowCanvasPalette.cs` centralizes canvas colors as `const string` fields.
- Settings persistence patterns in use: `IProfileService` profile dictionaries, `IDataService.Get/SetConfigValueAsync` (DB, profile-scoped), and a plain `appdata/` folder (`current_profile.txt`).
- No test projects exist. `Directory.Build.props` sets `LangVersion=latest`, `Nullable=enable`, `ImplicitUsings=enable`.

## File Structure (final state)

```
src/VyralBot.UI/
  Models/
    UiPreferences.cs               (create) — persisted prefs model + defaults
    ThemeOption.cs                 (create) — picker metadata record
    ThemeCatalog.cs                (create) — the 9 built-ins + custom; palette specs live here
    ColorShadeGenerator.cs         (create) — HSL shade math for custom accents
  Services/
    UiPreferencesService.cs        (create) — load/save appdata/ui-settings.json
    ThemeManager.cs                (create) — apply/switch themes, MD palette, logo resolution
  Themes/
    ThemeBrushes.xaml              (create) — all brushes/styles (moved from RedSlate.xaml)
    Colors/
      NativeColors.xaml            (create) — extracted from RedSlate.xaml colors, verbatim values
      LightColors.xaml             (create)
      DarkColors.xaml              (create)
      CrimsonColors.xaml           (create)
      AzureColors.xaml             (create)
      EmeraldColors.xaml           (create)
      FrostColors.xaml             (create)
      CarbonColors.xaml            (create)
      EmberColors.xaml             (create)
      CustomColors.xaml            (create) — same keys, values overwritten at runtime
  Resources/Themes/RedSlate.xaml   (DELETE after move)
  Views/SettingsView.xaml          (modify) — Theme panel UI
  ViewModels/SettingsViewModel.cs  (modify) — theme state + commands
  MainWindow.xaml(.cs)             (modify) — logo DynamicResource + icon wiring
  Controls/FlowCanvas/FlowCanvasPalette.cs (modify) — theme-aware lookup
tests/VyralBot.UI.Tests/           (create) — xUnit, pure logic only
```

---

### Task 1: Theme foundation — preferences, catalog, shade generator (+ tests)

**Files:**
- Create: `src/VyralBot.UI/Models/UiPreferences.cs`
- Create: `src/VyralBot.UI/Models/ThemeOption.cs`
- Create: `src/VyralBot.UI/Models/ThemeCatalog.cs`
- Create: `src/VyralBot.UI/Models/ColorShadeGenerator.cs`
- Create: `src/VyralBot.UI/Services/UiPreferencesService.cs`
- Create: `tests/VyralBot.UI.Tests/VyralBot.UI.Tests.csproj`
- Test: `tests/VyralBot.UI.Tests/UiPreferencesServiceTests.cs`, `ColorShadeGeneratorTests.cs`, `ThemeCatalogTests.cs`
- Modify: `src/VyralBot.sln` (add test project)

**Interfaces (produced, consumed by later tasks):**
```csharp
namespace VyralBot.UI.Models;
public sealed class UiPreferences {
    public string ThemeId { get; set; } = ThemeCatalog.NativeId;         // "native"|"light"|"dark"|"crimson"|"azure"|"emerald"|"frost"|"carbon"|"ember"|"custom"
    public CustomThemeSettings Custom { get; set; } = new();
    public static UiPreferences Defaults { get; }
}
public sealed class CustomThemeSettings {
    public string BaseTheme { get; set; } = "Dark";    // "Light" | "Dark"
    public string PrimaryHex { get; set; } = "#14B8A6";// #RRGGBB
    public string LogoVariant { get; set; } = "native";
}
public sealed record ThemeOption(string Id, string DisplayName, bool IsLight, string AccentHex, string LogoVariant, string PreviewBackgroundHex, bool IsBuiltIn);
public static class ThemeCatalog {
    public const string NativeId = "native"; public const string CustomId = "custom";
    public static IReadOnlyList<ThemeOption> BuiltInThemes { get; }   // native, light, dark, crimson, azure, emerald, frost, carbon, ember
    public static ThemeOption? Find(string id);
    public static IReadOnlyList<string> ValidLogoVariants { get; }    // native, red, blue, green, white, black
}
public static class ColorShadeGenerator {
    public static System.Windows.Media.Color Hover(System.Windows.Media.Color baseColor);   // +10% lightness
    public static System.Windows.Media.Color Pressed(System.Windows.Media.Color baseColor); // -10% lightness
    public static System.Windows.Media.Color Light(System.Windows.Media.Color baseColor);   // +30% lightness
    public static System.Windows.Media.Color Dark(System.Windows.Media.Color baseColor);    // -25% lightness
    public static bool TryParseHex(string? hex, out System.Windows.Media.Color color);       // "#RRGGBB" or "#AARRGGBB"
}
namespace VyralBot.UI.Services;
public sealed class UiPreferencesService {
    public UiPreferences Load();                       // missing/corrupt file → Defaults
    public void Save(UiPreferences preferences);       // atomic write (tmp + move)
    public static string GetSettingsPath();            // <repoRoot>/appdata/ui-settings.json  (root = walk up from BaseDirectory until "appdata" dir found, else BaseDirectory/appdata)
}
```

- [ ] **Step 1: Create the test project**

`tests/VyralBot.UI.Tests/VyralBot.UI.Tests.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.12.0" />
    <PackageReference Include="xunit" Version="2.9.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\VyralBot.UI\VyralBot.UI.csproj" />
  </ItemGroup>
</Project>
```
Add to `VyralBot.sln` via `dotnet sln VyralBot.sln add ..\..\tests\VyralBot.UI.Tests\VyralBot.UI.Tests.csproj` (run from `src/`; adjust relative path: solution is at `src/`, tests at repo-root/tests → `dotnet sln add ../../tests/...`).

- [ ] **Step 2: Write failing tests**

`tests/VyralBot.UI.Tests/ColorShadeGeneratorTests.cs`:
```csharp
using System.Windows.Media;
using VyralBot.UI.Models;
using Xunit;

namespace VyralBot.UI.Tests;

public class ColorShadeGeneratorTests
{
    [Fact]
    public void Hover_is_lighter_than_base()
        => Assert.True(CompareLightness(ColorShadeGenerator.Hover(Colors.Teal), Colors.Teal) > 0);

    [Fact]
    public void Pressed_is_darker_than_base()
        => Assert.True(CompareLightness(ColorShadeGenerator.Pressed(Colors.Teal), Colors.Teal) < 0);

    [Fact]
    public void Light_is_lighter_and_Dark_is_darker()
    {
        var teal = Color.FromRgb(0x14, 0xB8, 0xA6);
        Assert.True(CompareLightness(ColorShadeGenerator.Light(teal), teal) > 0);
        Assert.True(CompareLightness(ColorShadeGenerator.Dark(teal), teal) < 0);
    }

    [Fact]
    public void TryParseHex_accepts_six_digit_hex()
    {
        Assert.True(ColorShadeGenerator.TryParseHex("#14B8A6", out var c));
        Assert.Equal(0x14, c.R); Assert.Equal(0xB8, c.G); Assert.Equal(0xA6, c.B);
    }

    [Fact]
    public void TryParseHex_accepts_eight_digit_hex_and_rejects_garbage()
    {
        Assert.True(ColorShadeGenerator.TryParseHex("#FF14B8A6", out _));
        Assert.False(ColorShadeGenerator.TryParseHex("hello", out _));
        Assert.False(ColorShadeGenerator.TryParseHex(null, out _));
    }

    private static double CompareLightness(Color a, Color b) => Lightness(a) - Lightness(b);
    private static double Lightness(Color c) => (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;
}
```

`tests/VyralBot.UI.Tests/ThemeCatalogTests.cs`:
```csharp
using VyralBot.UI.Models;
using Xunit;

namespace VyralBot.UI.Tests;

public class ThemeCatalogTests
{
    [Fact]
    public void Has_nine_builtin_themes() => Assert.Equal(9, ThemeCatalog.BuiltInThemes.Count);

    [Fact]
    public void Theme_ids_are_unique()
        => Assert.Equal(ThemeCatalog.BuiltInThemes.Count, ThemeCatalog.BuiltInThemes.Select(t => t.Id).Distinct().Count());

    [Fact]
    public void Contains_required_themes()
    {
        var ids = ThemeCatalog.BuiltInThemes.Select(t => t.Id).ToHashSet();
        Assert.Superset(new[] { "native", "light", "dark", "crimson", "azure", "emerald", "frost", "carbon", "ember" }, ids);
    }

    [Fact]
    public void Find_returns_null_for_unknown() => Assert.Null(ThemeCatalog.Find("does-not-exist"));

    [Fact]
    public void Logo_variants_are_the_promised_set()
        => Assert.Superset(new[] { "native", "red", "blue", "green", "white", "black" }, ThemeCatalog.ValidLogoVariants);
}
```

`tests/VyralBot.UI.Tests/UiPreferencesServiceTests.cs`:
```csharp
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
    public void Dispose() { try { Directory.Delete(_dir, recursive: true); } catch { } }

    private UiPreferencesService MakeService(Func<string> pathFactory) => new(pathFactory);

    [Fact]
    public void Load_returns_defaults_when_file_missing()
    {
        var svc = MakeService(() => Path.Combine(_dir, "missing.json"));
        var prefs = svc.Load();
        Assert.Equal(ThemeCatalog.NativeId, prefs.ThemeId);
    }

    [Fact]
    public void Save_then_Load_round_trips()
    {
        var path = Path.Combine(_dir, "ui-settings.json");
        var svc = MakeService(() => path);
        var prefs = new UiPreferences { ThemeId = "crimson", Custom = new CustomThemeSettings { BaseTheme = "Light", PrimaryHex = "#FF0000", LogoVariant = "red" } };
        svc.Save(prefs);
        var loaded = svc.Load();
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
}
```
Note: `UiPreferencesService` needs a constructor taking an optional path factory: `public UiPreferencesService(Func<string>? settingsPathFactory = null)` — tests inject temp paths; production uses `GetSettingsPath()`.

- [ ] **Step 3: Run tests, verify they fail to compile (types missing)**

Run: `dotnet test tests/VyralBot.UI.Tests/VyralBot.UI.Tests.csproj`
Expected: build error CS0246 (`UiPreferences`/`ThemeCatalog`/`ColorShadeGenerator` not found).

- [ ] **Step 4: Implement the models and service**

`src/VyralBot.UI/Models/UiPreferences.cs`:
```csharp
namespace VyralBot.UI.Models;

/// <summary>App-level UI preferences persisted to appdata/ui-settings.json.</summary>
public sealed class UiPreferences
{
    public string ThemeId { get; set; } = ThemeCatalog.NativeId;
    public CustomThemeSettings Custom { get; set; } = new();

    public static UiPreferences Defaults { get; } = new();
}

public sealed class CustomThemeSettings
{
    /// <summary>"Light" or "Dark" — the base palette custom accents are applied over.</summary>
    public string BaseTheme { get; set; } = "Dark";
    public string PrimaryHex { get; set; } = "#14B8A6";
    public string LogoVariant { get; set; } = "native";
}
```

`src/VyralBot.UI/Models/ThemeOption.cs`:
```csharp
namespace VyralBot.UI.Models;

/// <summary>Metadata for one theme as shown in the Settings → Theme picker.</summary>
public sealed record ThemeOption(
    string Id,
    string DisplayName,
    bool IsLight,
    string AccentHex,
    string LogoVariant,
    string PreviewBackgroundHex,
    bool IsBuiltIn);
```

`src/VyralBot.UI/Models/ThemeCatalog.cs` (palette values match the dictionaries built in Task 2 — single source of truth for metadata):
```csharp
namespace VyralBot.UI.Models;

public static class ThemeCatalog
{
    public const string NativeId = "native";
    public const string CustomId = "custom";

    public static IReadOnlyList<string> ValidLogoVariants { get; } =
        new[] { "native", "red", "blue", "green", "white", "black" };

    // AccentHex/PreviewBackgroundHex mirror the dictionaries in Themes/Colors/.
    // LogoVariant names the vyral-logo-{variant}.png this theme prefers (falls back to vyral-logo.png).
    public static IReadOnlyList<ThemeOption> BuiltInThemes { get; } = new[]
    {
        new ThemeOption("native",  "Native Teal",  IsLight: false, "#14B8A6", "native", "#0F172A", IsBuiltIn: true),
        new ThemeOption("light",   "Light",        IsLight: true,  "#0D9488", "native", "#F1F5F9", IsBuiltIn: true),
        new ThemeOption("dark",    "Dark",         IsLight: false, "#14B8A6", "white",  "#0B0F14", IsBuiltIn: true),
        new ThemeOption("crimson", "Crimson Forge",IsLight: false, "#EF4444", "red",    "#1A0E10", IsBuiltIn: true),
        new ThemeOption("azure",   "Azure Depths", IsLight: false, "#3B82F6", "blue",   "#0B1220", IsBuiltIn: true),
        new ThemeOption("emerald", "Emerald Grove",IsLight: false, "#10B981", "green",  "#0A1410", IsBuiltIn: true),
        new ThemeOption("frost",   "Arctic Frost", IsLight: true,  "#0284C7", "black",  "#F8FAFC", IsBuiltIn: true),
        new ThemeOption("carbon",  "Carbon Graphite", IsLight: false, "#94A3B8", "white", "#09090B", IsBuiltIn: true),
        new ThemeOption("ember",   "Sunset Ember", IsLight: false, "#F97316", "native", "#170E08", IsBuiltIn: true),
    };

    public static ThemeOption? Find(string id) =>
        BuiltInThemes.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase))
        ?? (string.Equals(id, CustomId, StringComparison.OrdinalIgnoreCase) ? CustomOption : null);

    public static ThemeOption CustomOption { get; } =
        new(CustomId, "Custom", IsLight: false, "#14B8A6", "native", "#0F172A", IsBuiltIn: false);
}
```

`src/VyralBot.UI/Models/ColorShadeGenerator.cs`:
```csharp
using System.Windows.Media;

namespace VyralBot.UI.Models;

/// <summary>Derives hover/pressed/light/dark shades from a base accent color (HSL math).</summary>
public static class ColorShadeGenerator
{
    public static Color Hover(Color baseColor) => ShiftLightness(baseColor, +0.10);
    public static Color Pressed(Color baseColor) => ShiftLightness(baseColor, -0.10);
    public static Color Light(Color baseColor) => ShiftLightness(baseColor, +0.30);
    public static Color Dark(Color baseColor) => ShiftLightness(baseColor, -0.25);

    public static bool TryParseHex(string? hex, out Color color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(hex)) return false;
        var s = hex.Trim().TrimStart('#');
        try
        {
            if (s.Length == 6)
            {
                color = Color.FromRgb(Convert.ToByte(s[..2], 16), Convert.ToByte(s[2..4], 16), Convert.ToByte(s[4..6], 16));
                return true;
            }
            if (s.Length == 8)
            {
                color = Color.FromArgb(Convert.ToByte(s[..2], 16), Convert.ToByte(s[2..4], 16), Convert.ToByte(s[4..6], 16), Convert.ToByte(s[6..8], 16));
                return true;
            }
        }
        catch (FormatException) { }
        return false;
    }

    public static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    private static Color ShiftLightness(Color color, double delta)
    {
        RgbToHsl(color, out var h, out var s, out var l);
        l = Math.Clamp(l + delta, 0.0, 1.0);
        var result = HslToRgb(h, s, l);
        return Color.FromArgb(color.A, result[0], result[1], result[2]);
    }

    private static void RgbToHsl(Color c, out double h, out double s, out double l)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        l = (max + min) / 2.0;
        if (max == min) { h = s = 0; return; }
        double d = max - min;
        s = l > 0.5 ? d / (2.0 - max - min) : d / (max + min);
        h = max == r ? (g - b) / d + (g < b ? 6 : 0)
          : max == g ? (b - r) / d + 2
          : (r - g) / d + 4;
        h /= 6.0;
    }

    private static byte[] HslToRgb(double h, double s, double l)
    {
        if (s == 0) { var gray = (byte)Math.Round(l * 255); return new[] { gray, gray, gray }; }
        double q = l < 0.5 ? l * (1 + s) : l + s - l * s;
        double p = 2 * l - q;
        return new[]
        {
            (byte)Math.Round(HueToRgb(p, q, h + 1.0 / 3.0) * 255),
            (byte)Math.Round(HueToRgb(p, q, h) * 255),
            (byte)Math.Round(HueToRgb(p, q, h - 1.0 / 3.0) * 255),
        };
    }

    private static double HueToRgb(double p, double q, double t)
    {
        if (t < 0) t += 1;
        if (t > 1) t -= 1;
        return t switch
        {
            < 1.0 / 6.0 => p + (q - p) * 6 * t,
            < 1.0 / 2.0 => q,
            < 2.0 / 3.0 => p + (q - p) * (2.0 / 3.0 - t) * 6,
            _ => p,
        };
    }
}
```

`src/VyralBot.UI/Services/UiPreferencesService.cs`:
```csharp
using System.IO;
using System.Text.Json;
using VyralBot.UI.Models;

namespace VyralBot.UI.Services;

/// <summary>Persists app-level UI preferences (theme choice) to appdata/ui-settings.json.</summary>
public sealed class UiPreferencesService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

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
            return JsonSerializer.Deserialize<UiPreferences>(json) ?? UiPreferences.Defaults;
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
```
This mirrors `ProfileService`'s "portable appdata at repo root" convention (`ProfileService.cs:31-41`).

- [ ] **Step 5: Run tests, verify they pass**

Run: `dotnet test tests/VyralBot.UI.Tests/VyralBot.UI.Tests.csproj`
Expected: PASS (all).

- [ ] **Step 6: Commit** — `git add -A && git commit -m "feat(themes): theme foundation - preferences, catalog, shade generator (+tests)"`

---

### Task 2: ThemeBrushes.xaml + 9 theme color dictionaries + App.xaml rewire

**Files:**
- Create: `src/VyralBot.UI/Themes/ThemeBrushes.xaml`
- Create: `src/VyralBot.UI/Themes/Colors/{Native,Light,Dark,Crimson,Azure,Emerald,Frost,Carbon,Ember}Colors.xaml` (9 files)
- Delete: `src/VyralBot.UI/Resources/Themes/RedSlate.xaml`
- Modify: `src/VyralBot.UI/App.xaml` (merged dictionaries + scrollbar thumb brushes)

**Interfaces:**
- Consumes: nothing (leaf XAML).
- Produces: the canonical **color key set** (must be identical in all 10 colors dictionaries) and all brush keys (unchanged names from RedSlate.xaml, plus new `Flow*` brushes). ThemeManager (Task 3) swaps `<ResourceDictionary Source="/Themes/Colors/{Id}Colors.xaml"/>` at merged-dictionary index 2.

**Canonical color key set (all 10 dictionaries define every key):**
PrimaryColor, PrimaryHover, PrimaryPressed, PrimaryLight, PrimaryDark, BackgroundPrimary, BackgroundSecondary, BackgroundTertiary, SurfaceColor, SurfaceHover, SurfacePressed, OverlayBackground, ModalBackground, GlassBackground, GlassBorder, TextPrimary, TextSecondary, TextMuted, TextDisabled, TextOnPrimary, TextOnDark, TextOnLight, BorderPrimary, BorderSecondary, BorderLight, BorderAccent, SuccessColor, SuccessHover, SuccessLight, WarningColor, WarningHover, WarningLight, ErrorColor, ErrorHover, ErrorLight, InfoColor, InfoHover, InfoLight, TwitchPrimary, TwitchHover, TwitchPressed, TwitchBackground, TwitchSurface, TwitchDark, KickPrimary, KickHover, KickPressed, KickBackground, KickOnPrimary, YouTubePrimary, YouTubeHover, TikTokPrimary, TikTokAccent, InputBackground, InputBorder, InputFocusBorder, InputDisabled, InputPlaceholder, ModeratorBadge, SubscriberBadge, VipBadge, DiscordColor, OrangeBadge, FlowCanvasBackground, FlowCanvasGridDot, FlowCanvasNodeBackground, FlowCanvasNodeBorder, FlowCanvasNodeHover, FlowCanvasSelection, FlowCanvasTextPrimary, FlowCanvasTextSecondary, FlowCanvasDescriptionText, FlowCanvasPortNeutral, FlowCanvasMainWire, FlowCanvasCasePort, FlowCanvasTriggerAccent, FlowCanvasRejected, FlowCanvasButtonBackground, FlowCanvasButtonHover, FlowCanvasButtonForeground, FlowCanvasChipBackground, FlowCanvasChipBorder, FlowCanvasChipBorderHover.

- [ ] **Step 1: Create ThemeBrushes.xaml**

Take `Resources/Themes/RedSlate.xaml` and move everything **except the `<Color ...>` entries** into `Themes/ThemeBrushes.xaml`:
- All `<SolidColorBrush>` definitions verbatim (they already use `Color="{DynamicResource ...}"`, so they follow any colors dictionary).
- `VyralScrollBarThumb` style, the implicit `ScrollBar` style, `CardShadow`, `HoverShadow` — verbatim.
- Append new Flow-canvas brushes (for Task 7):
```xml
    <!-- ===== Flow Canvas Brushes ===== -->
    <SolidColorBrush x:Key="FlowCanvasBackgroundBrush" Color="{DynamicResource FlowCanvasBackground}"/>
    <SolidColorBrush x:Key="FlowCanvasGridDotBrush" Color="{DynamicResource FlowCanvasGridDot}"/>
    <SolidColorBrush x:Key="FlowCanvasNodeBackgroundBrush" Color="{DynamicResource FlowCanvasNodeBackground}"/>
    <SolidColorBrush x:Key="FlowCanvasNodeBorderBrush" Color="{DynamicResource FlowCanvasNodeBorder}"/>
    <SolidColorBrush x:Key="FlowCanvasNodeHoverBrush" Color="{DynamicResource FlowCanvasNodeHover}"/>
    <SolidColorBrush x:Key="FlowCanvasSelectionBrush" Color="{DynamicResource FlowCanvasSelection}"/>
    <SolidColorBrush x:Key="FlowCanvasTextPrimaryBrush" Color="{DynamicResource FlowCanvasTextPrimary}"/>
    <SolidColorBrush x:Key="FlowCanvasTextSecondaryBrush" Color="{DynamicResource FlowCanvasTextSecondary}"/>
    <SolidColorBrush x:Key="FlowCanvasDescriptionTextBrush" Color="{DynamicResource FlowCanvasDescriptionText}"/>
    <SolidColorBrush x:Key="FlowCanvasPortNeutralBrush" Color="{DynamicResource FlowCanvasPortNeutral}"/>
    <SolidColorBrush x:Key="FlowCanvasMainWireBrush" Color="{DynamicResource FlowCanvasMainWire}"/>
    <SolidColorBrush x:Key="FlowCanvasCasePortBrush" Color="{DynamicResource FlowCanvasCasePort}"/>
    <SolidColorBrush x:Key="FlowCanvasTriggerAccentBrush" Color="{DynamicResource FlowCanvasTriggerAccent}"/>
    <SolidColorBrush x:Key="FlowCanvasRejectedBrush" Color="{DynamicResource FlowCanvasRejected}"/>
    <SolidColorBrush x:Key="FlowCanvasButtonBackgroundBrush" Color="{DynamicResource FlowCanvasButtonBackground}"/>
    <SolidColorBrush x:Key="FlowCanvasButtonHoverBrush" Color="{DynamicResource FlowCanvasButtonHover}"/>
    <SolidColorBrush x:Key="FlowCanvasButtonForegroundBrush" Color="{DynamicResource FlowCanvasButtonForeground}"/>
    <SolidColorBrush x:Key="FlowCanvasChipBackgroundBrush" Color="{DynamicResource FlowCanvasChipBackground}"/>
    <SolidColorBrush x:Key="FlowCanvasChipBorderBrush" Color="{DynamicResource FlowCanvasChipBorder}"/>
    <SolidColorBrush x:Key="FlowCanvasChipBorderHoverBrush" Color="{DynamicResource FlowCanvasChipBorderHover}"/>
    <!-- App logo, replaced at runtime by ThemeManager (pack URI to the active variant PNG) -->
    <ImageSource x:Key="AppLogoSource">/Resources/Images/vyral-logo.png</ImageSource>
```
(`xmlns:sys="clr-namespace:System;assembly=mscorlib"` not needed for ImageSource — a string pack URI converts.)

- [ ] **Step 2: Create the 9 colors dictionaries**

Every file uses this shape (only `Color` entries; NO brushes):
```xml
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <!-- <Theme name> color values -->
    <Color x:Key="PrimaryColor">#14B8A6</Color>
    ...
</ResourceDictionary>
```

`NativeColors.xaml` = copy the Color entries from `Resources/Themes/RedSlate.xaml` **verbatim** (lines 9–97) **plus** the FlowCanvas block below. This guarantees Task 2 is a no-op visually.

Flow canvas block (shared shape, per-theme values):
```xml
    <!-- ===== Flow Canvas ===== -->
    <Color x:Key="FlowCanvasBackground">#1A1A2E</Color>
    <Color x:Key="FlowCanvasGridDot">#2A2A4A</Color>
    <Color x:Key="FlowCanvasNodeBackground">#2B2D42</Color>
    <Color x:Key="FlowCanvasNodeBorder">#374151</Color>
    <Color x:Key="FlowCanvasNodeHover">#3D3F5C</Color>
    <Color x:Key="FlowCanvasSelection">#6366F1</Color>
    <Color x:Key="FlowCanvasTextPrimary">#FFFFFF</Color>
    <Color x:Key="FlowCanvasTextSecondary">#9CA3AF</Color>
    <Color x:Key="FlowCanvasDescriptionText">#9AA0A6</Color>
    <Color x:Key="FlowCanvasPortNeutral">#9CA3AF</Color>
    <Color x:Key="FlowCanvasMainWire">#3B82F6</Color>
    <Color x:Key="FlowCanvasCasePort">#F59E0B</Color>
    <Color x:Key="FlowCanvasTriggerAccent">#F59E0B</Color>
    <Color x:Key="FlowCanvasRejected">#EF4444</Color>
    <Color x:Key="FlowCanvasButtonBackground">#FFFFFF</Color>
    <Color x:Key="FlowCanvasButtonHover">#F3F4F6</Color>
    <Color x:Key="FlowCanvasButtonForeground">#1F2937</Color>
    <Color x:Key="FlowCanvasChipBackground">#1E1F22</Color>
    <Color x:Key="FlowCanvasChipBorder">#4A4C58</Color>
    <Color x:Key="FlowCanvasChipBorderHover">#6B6E80</Color>
```
(Native/Flow values above match `FlowCanvasPalette.cs:13-33` exactly.)

**Palette values for the other 8 dictionaries** — every unlisted key comes from the rule at top: platform + badge colors are IDENTICAL to Native in all themes; semantics (Success/Warning/Error/Info sets) identical to Native in all themes.

**LightColors.xaml** (Light mode):
| Key | Value | Key | Value |
|---|---|---|---|
| PrimaryColor | #0D9488 | OverlayBackground | #33000000 |
| PrimaryHover | #14B8A6 | ModalBackground | #66000000 |
| PrimaryPressed | #0F766E | GlassBackground | #B3FFFFFF |
| PrimaryLight | #5EEAD4 | GlassBorder | #330D9488 |
| PrimaryDark | #115E59 | TextPrimary | #0F172A |
| BackgroundPrimary | #F1F5F9 | TextSecondary | #475569 |
| BackgroundSecondary | #FFFFFF | TextMuted | #64748B |
| BackgroundTertiary | #E2E8F0 | TextDisabled | #94A3B8 |
| SurfaceColor | #FFFFFF | TextOnPrimary | #FFFFFF |
| SurfaceHover | #F1F5F9 | TextOnDark | #0F172A |
| SurfacePressed | #E2E8F0 | TextOnLight | #0F172A |
| BorderPrimary | #CBD5E1 | InputBackground | #FFFFFF |
| BorderSecondary | #E2E8F0 | InputBorder | #CBD5E1 |
| BorderLight | #94A3B8 | InputFocusBorder | #0D9488 |
| BorderAccent | #0D9488 | InputDisabled | #E2E8F0 |
| | | InputPlaceholder | #94A3B8 |
| FlowCanvasBackground | #F8FAFC | FlowCanvasNodeBackground | #FFFFFF |
| FlowCanvasGridDot | #D8DEE9 | FlowCanvasNodeBorder | #CBD5E1 |
| FlowCanvasNodeHover | #E2E8F0 | FlowCanvasSelection | #6366F1 |
| FlowCanvasTextPrimary | #0F172A | FlowCanvasTextSecondary | #475569 |
| FlowCanvasDescriptionText | #64748B | FlowCanvasPortNeutral | #64748B |
| FlowCanvasMainWire | #2563EB | FlowCanvasCasePort | #D97706 |
| FlowCanvasTriggerAccent | #D97706 | FlowCanvasRejected | #DC2626 |
| FlowCanvasButtonBackground | #FFFFFF | FlowCanvasButtonHover | #E2E8F0 |
| FlowCanvasButtonForeground | #0F172A | FlowCanvasChipBackground | #FFFFFF |
| FlowCanvasChipBorder | #CBD5E1 | FlowCanvasChipBorderHover | #94A3B8 |

**DarkColors.xaml** (Dark mode — neutral graphite, teal accent):
| Key | Value | Key | Value |
|---|---|---|---|
| PrimaryColor | #14B8A6 | OverlayBackground | #CC000000 |
| PrimaryHover | #2DD4BF | ModalBackground | #DD000000 |
| PrimaryPressed | #0D9488 | GlassBackground | #1A161B22 |
| PrimaryLight | #5EEAD4 | GlassBorder | #3314B8A6 |
| PrimaryDark | #0F766E | TextPrimary | #E6EDF3 |
| BackgroundPrimary | #0B0F14 | TextSecondary | #8B949E |
| BackgroundSecondary | #161B22 | TextMuted | #6E7681 |
| BackgroundTertiary | #21262D | TextDisabled | #484F58 |
| SurfaceColor | #161B22 | TextOnPrimary | #FFFFFF |
| SurfaceHover | #21262D | TextOnDark | #FFFFFF |
| SurfacePressed | #2D333B | TextOnLight | #161B22 |
| BorderPrimary | #30363D | InputBackground | #161B22 |
| BorderSecondary | #21262D | InputBorder | #30363D |
| BorderLight | #6E7681 | InputFocusBorder | #14B8A6 |
| BorderAccent | #14B8A6 | InputDisabled | #21262D |
| | | InputPlaceholder | #6E7681 |
Flow canvas: Background #0D1117, GridDot #1C2430, NodeBackground #161B22, NodeBorder #30363D, NodeHover #21262D, Selection #6366F1, TextPrimary #E6EDF3, TextSecondary #8B949E, DescriptionText #6E7681, PortNeutral #8B949E, MainWire #3B82F6, CasePort/TriggerAccent #F59E0B, Rejected #EF4444, ButtonBackground #FFFFFF, ButtonHover #F3F4F6, ButtonForeground #1F2937, ChipBackground #161B22, ChipBorder #30363D, ChipBorderHover #6E7681.

**CrimsonColors.xaml** (Crimson Forge — dark red-tinted): Primary #EF4444, PrimaryHover #F87171, PrimaryPressed #DC2626, PrimaryLight #FCA5A5, PrimaryDark #B91C1C, BackgroundPrimary #1A0E10, BackgroundSecondary #241518, BackgroundTertiary #332025, SurfaceColor #241518, SurfaceHover #332025, SurfacePressed #402930, Overlay #CC000000, Modal #DD000000, GlassBackground #1A241518, GlassBorder #33EF4444, TextPrimary #FDF5F5, TextSecondary #B3A8A8, TextMuted #7A6E6E, TextDisabled #524345, TextOnPrimary #FFFFFF, TextOnDark #FFFFFF, TextOnLight #241518, BorderPrimary #402930, BorderSecondary #332025, BorderLight #7A6E6E, BorderAccent #EF4444, InputBackground #241518, InputBorder #402930, InputFocusBorder #EF4444, InputDisabled #332025, InputPlaceholder #7A6E6E. Flow: Background #160B0D, GridDot #2A1518, NodeBackground #241518, NodeBorder #402930, NodeHover #332025, Selection #6366F1, TextPrimary #FDF5F5, TextSecondary #B3A8A8, DescriptionText #7A6E6E, PortNeutral #B3A8A8, MainWire #3B82F6, CasePort/TriggerAccent #F59E0B, Rejected #EF4444, ButtonBackground #FFFFFF, ButtonHover #F3F4F6, ButtonForeground #1F2937, ChipBackground #241518, ChipBorder #402930, ChipBorderHover #7A6E6E.

**AzureColors.xaml** (Azure Depths — dark blue): Primary #3B82F6, PrimaryHover #60A5FA, PrimaryPressed #2563EB, PrimaryLight #93C5FD, PrimaryDark #1D4ED8, BackgroundPrimary #0B1220, BackgroundSecondary #111A2E, BackgroundTertiary #1A2740, SurfaceColor #111A2E, SurfaceHover #1A2740, SurfacePressed #24324E, Overlay #CC000000, Modal #DD000000, GlassBackground #1A111A2E, GlassBorder #333B82F6, TextPrimary #F0F6FF, TextSecondary #94A3B8, TextMuted #64748B, TextDisabled #3B4A63, TextOnPrimary #FFFFFF, TextOnDark #FFFFFF, TextOnLight #111A2E, BorderPrimary #24324E, BorderSecondary #1A2740, BorderLight #64748B, BorderAccent #3B82F6, InputBackground #111A2E, InputBorder #24324E, InputFocusBorder #3B82F6, InputDisabled #1A2740, InputPlaceholder #64748B. Flow: Background #080F1C, GridDot #14203A, NodeBackground #111A2E, NodeBorder #24324E, NodeHover #1A2740, Selection #6366F1, TextPrimary #F0F6FF, TextSecondary #94A3B8, DescriptionText #64748B, PortNeutral #94A3B8, MainWire #60A5FA, CasePort/TriggerAccent #F59E0B, Rejected #EF4444, ButtonBackground #FFFFFF, ButtonHover #F3F4F6, ButtonForeground #1F2937, ChipBackground #111A2E, ChipBorder #24324E, ChipBorderHover #64748B.

**EmeraldColors.xaml** (Emerald Grove — dark green): Primary #10B981, PrimaryHover #34D399, PrimaryPressed #059669, PrimaryLight #6EE7B7, PrimaryDark #047857, BackgroundPrimary #0A1410, BackgroundSecondary #10201A, BackgroundTertiary #173227, SurfaceColor #10201A, SurfaceHover #173227, SurfacePressed #1F4232, Overlay #CC000000, Modal #DD000000, GlassBackground #1A10201A, GlassBorder #3310B981, TextPrimary #F0FDF6, TextSecondary #94A3B8, TextMuted #6B8F7E, TextDisabled #35544A, TextOnPrimary #FFFFFF, TextOnDark #FFFFFF, TextOnLight #10201A, BorderPrimary #1F4232, BorderSecondary #173227, BorderLight #6B8F7E, BorderAccent #10B981, InputBackground #10201A, InputBorder #1F4232, InputFocusBorder #10B981, InputDisabled #173227, InputPlaceholder #6B8F7E. Flow: Background #081009, GridDot #132619, NodeBackground #10201A, NodeBorder #1F4232, NodeHover #173227, Selection #6366F1, TextPrimary #F0FDF6, TextSecondary #94A3B8, DescriptionText #6B8F7E, PortNeutral #94A3B8, MainWire #3B82F6, CasePort/TriggerAccent #F59E0B, Rejected #EF4444, ButtonBackground #FFFFFF, ButtonHover #F3F4F6, ButtonForeground #1F2937, ChipBackground #10201A, ChipBorder #1F4232, ChipBorderHover #6B8F7E.

**FrostColors.xaml** (Arctic Frost — light, cool): Primary #0284C7, PrimaryHover #0EA5E9, PrimaryPressed #0369A1, PrimaryLight #7DD3FC, PrimaryDark #075985, BackgroundPrimary #F8FAFC, BackgroundSecondary #FFFFFF, BackgroundTertiary #EEF2F7, SurfaceColor #FFFFFF, SurfaceHover #EEF2F7, SurfacePressed #E2E8F0, Overlay #33000000, Modal #66000000, GlassBackground #B3FFFFFF, GlassBorder #330284C7, TextPrimary #0F172A, TextSecondary #475569, TextMuted #64748B, TextDisabled #94A3B8, TextOnPrimary #FFFFFF, TextOnDark #0F172A, TextOnLight #0F172A, BorderPrimary #CBD5E1, BorderSecondary #E2E8F0, BorderLight #94A3B8, BorderAccent #0284C7, InputBackground #FFFFFF, InputBorder #CBD5E1, InputFocusBorder #0284C7, InputDisabled #EEF2F7, InputPlaceholder #94A3B8. Flow: Background #F1F5F9, GridDot #D8DEE9, NodeBackground #FFFFFF, NodeBorder #CBD5E1, NodeHover #EEF2F7, Selection #6366F1, TextPrimary #0F172A, TextSecondary #475569, DescriptionText #64748B, PortNeutral #64748B, MainWire #0284C7, CasePort/TriggerAccent #D97706, Rejected #DC2626, ButtonBackground #FFFFFF, ButtonHover #EEF2F7, ButtonForeground #0F172A, ChipBackground #FFFFFF, ChipBorder #CBD5E1, ChipBorderHover #94A3B8.

**CarbonColors.xaml** (Carbon Graphite — true neutral black, gray accent): Primary #94A3B8, PrimaryHover #B0BCCB, PrimaryPressed #64748B, PrimaryLight #CBD5E1, PrimaryDark #475569, BackgroundPrimary #09090B, BackgroundSecondary #131316, BackgroundTertiary #1F1F23, SurfaceColor #131316, SurfaceHover #1F1F23, SurfacePressed #2A2A2E, Overlay #CC000000, Modal #DD000000, GlassBackground #1A131316, GlassBorder #3394A3B8, TextPrimary #FAFAFA, TextSecondary #A1A1AA, TextMuted #71717A, TextDisabled #3F3F46, TextOnPrimary #09090B, TextOnDark #FFFFFF, TextOnLight #131316, BorderPrimary #2A2A2E, BorderSecondary #1F1F23, BorderLight #71717A, BorderAccent #94A3B8, InputBackground #131316, InputBorder #2A2A2E, InputFocusBorder #94A3B8, InputDisabled #1F1F23, InputPlaceholder #71717A. Flow: Background #0C0C0E, GridDot #1B1B1F, NodeBackground #131316, NodeBorder #2A2A2E, NodeHover #1F1F23, Selection #6366F1, TextPrimary #FAFAFA, TextSecondary #A1A1AA, DescriptionText #71717A, PortNeutral #A1A1AA, MainWire #3B82F6, CasePort/TriggerAccent #F59E0B, Rejected #EF4444, ButtonBackground #FFFFFF, ButtonHover #F3F4F6, ButtonForeground #1F2937, ChipBackground #131316, ChipBorder #2A2A2E, ChipBorderHover #71717A.

**EmberColors.xaml** (Sunset Ember — dark warm): Primary #F97316, PrimaryHover #FB923C, PrimaryPressed #EA580C, PrimaryLight #FDBA74, PrimaryDark #C2410C, BackgroundPrimary #170E08, BackgroundSecondary #221610, BackgroundTertiary #322217, SurfaceColor #221610, SurfaceHover #322217, SurfacePressed #40301F, Overlay #CC000000, Modal #DD000000, GlassBackground #1A221610, GlassBorder #33F97316, TextPrimary #FFF7ED, TextSecondary #B8A99C, TextMuted #8A7A6C, TextDisabled #55483C, TextOnPrimary #FFFFFF, TextOnDark #FFFFFF, TextOnLight #221610, BorderPrimary #40301F, BorderSecondary #322217, BorderLight #8A7A6C, BorderAccent #F97316, InputBackground #221610, InputBorder #40301F, InputFocusBorder #F97316, InputDisabled #322217, InputPlaceholder #8A7A6C. Flow: Background #120A05, GridDot #2A1B10, NodeBackground #221610, NodeBorder #40301F, NodeHover #322217, Selection #6366F1, TextPrimary #FFF7ED, TextSecondary #B8A99C, DescriptionText #8A7A6C, PortNeutral #B8A99C, MainWire #3B82F6, CasePort/TriggerAccent #F59E0B, Rejected #EF4444, ButtonBackground #FFFFFF, ButtonHover #F3F4F6, ButtonForeground #1F2937, ChipBackground #221610, ChipBorder #40301F, ChipBorderHover #8A7A6C.

**CustomColors.xaml** = exact clone of NativeColors.xaml (same keys); ThemeManager overwrites Primary* + Backgrounds(when base=Light, clones LightColors values first) at runtime.

- [ ] **Step 3: Rewire App.xaml**

Replace the merged dictionaries block:
```xml
            <ResourceDictionary.MergedDictionaries>
                <!-- MaterialDesign Base Themes -->
                <materialDesign:BundledTheme BaseTheme="Dark" PrimaryColor="Teal" SecondaryColor="Cyan" />
                <ResourceDictionary Source="pack://application:,,,/MaterialDesignThemes.Wpf;component/Themes/MaterialDesign3.Defaults.xaml" />

                <!-- VyralBot Theme System: brushes wrap colors via DynamicResource, so
                     ThemeManager can swap the colors dictionary at runtime. -->
                <ResourceDictionary Source="/Themes/ThemeBrushes.xaml" />
                <ResourceDictionary Source="/Themes/Colors/NativeColors.xaml" />
            </ResourceDictionary.MergedDictionaries>
```
Keep the `PrimaryHue*Color`/`PrimaryHue*Brush` block and converters unchanged. Update App.xaml's own implicit ScrollBar style to use theme brushes instead of hardcoded grays: thumb Background `{DynamicResource SurfaceHoverBrush}`, hover `{DynamicResource BorderLightBrush}`, dragging `{DynamicResource TextMutedBrush}`.

- [ ] **Step 4: Build + visual no-op check**

Run: `dotnet build VyralBot.sln`
Expected: 0 errors. (App visuals identical to before — Native is the extracted palette.)

- [ ] **Step 5: Delete RedSlate.xaml, commit**

`git rm src/VyralBot.UI/Resources/Themes/RedSlate.xaml` (no remaining references — verify with `grep -rn "RedSlate" src/`). Commit: `feat(themes): split theme colors from brushes; add 9 built-in palettes`.

---

### Task 3: ThemeManager — apply/switch, MaterialDesign palette, logo resolution

**Files:**
- Create: `src/VyralBot.UI/Services/ThemeManager.cs`
- Modify: `src/VyralBot.UI/App.xaml.cs` (DI registration + startup apply)
- Modify: `src/VyralBot.UI/MainWindow.xaml.cs` (icon from ThemeManager)

**Interfaces:**
- Consumes: `UiPreferencesService` (Task 1), `ThemeCatalog`, `ColorShadeGenerator`, `CustomColors.xaml` (Task 2).
- Produces:
```csharp
namespace VyralBot.UI.Services;
public sealed class ThemeManager
{
    public static ThemeManager Instance { get; }                 // simple static access for views/windows
    public UiPreferences Preferences { get; }
    public string CurrentThemeId { get; }                        // catalog id of active theme
    public event Action? ThemeChanged;
    public void Initialize();                                    // load prefs + apply saved theme
    public void ApplyTheme(string themeId);                      // built-in id or "custom"
    public void ApplyCustomTheme(string baseTheme, string primaryHex, string logoVariant); // validates + applies + persists
    public string CurrentLogoVariant { get; }
    public ImageSource? ResolveLogoImage(string variant);        // vyral-logo-{v}.png → fallback vyral-logo.png, cached
    public static bool LogoVariantExists(string variant);        // probe pack URI
}
```

- [ ] **Step 1: Implement ThemeManager**

```csharp
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MaterialDesignThemes.Wpf;
using VyralBot.UI.Models;

namespace VyralBot.UI.Services;

/// <summary>
/// Applies and switches application themes at runtime.
/// Brushes live in ThemeBrushes.xaml and wrap colors via DynamicResource, so swapping
/// the merged colors dictionary re-themes every view instantly.
/// </summary>
public sealed class ThemeManager
{
    public static ThemeManager Instance { get; } = new();

    private const string ColorsNamespacePrefix = "/Themes/Colors/";
    private static readonly Dictionary<string, ImageSource> LogoCache = new();

    private readonly UiPreferencesService _preferencesService = new();

    public UiPreferences Preferences { get; private set; } = UiPreferences.Defaults;
    public string CurrentThemeId { get; private set; } = ThemeCatalog.NativeId;
    public string CurrentLogoVariant { get; private set; } = "native";

    /// <summary>Raised after a theme (or logo) change has been applied to Application resources.</summary>
    public event Action? ThemeChanged;

    public void Initialize()
    {
        Preferences = _preferencesService.Load();
        ApplyTheme(Preferences.ThemeId, persist: false);
    }

    public void ApplyTheme(string themeId) => ApplyTheme(themeId, persist: true);

    private void ApplyTheme(string themeId, bool persist)
    {
        var app = Application.Current;
        if (app is null) return;

        var mdPrimary = TryGetAccentFor(themeId, out var accentHex, out var logoVariant, out var colorsSource)
            ? ColorShadeGenerator.TryParseHex(accentHex, out var accent) ? accent : default(Color?)
            : null;

        if (colorsSource is not null)
            SwapColorsDictionary(app, colorsSource);

        ApplyMaterialDesignTheme(app, isLight: TryGetIsLight(themeId), primary: mdPrimary);

        CurrentThemeId = ThemeCatalog.Find(themeId)?.Id ?? ThemeCatalog.NativeId;
        if (CurrentThemeId == ThemeCatalog.CustomId)
            logoVariant = Preferences.Custom.LogoVariant;
        ApplyLogo(app, logoVariant ?? "native");

        if (persist)
        {
            Preferences.ThemeId = CurrentThemeId;
            _preferencesService.Save(Preferences);
        }
        ThemeChanged?.Invoke();
    }

    public void ApplyCustomTheme(string baseTheme, string primaryHex, string logoVariant)
    {
        var app = Application.Current;
        if (app is null) return;
        if (!ColorShadeGenerator.TryParseHex(primaryHex, out var primary))
            primary = Color.FromRgb(0x14, 0xB8, 0xA6);
        var isLight = string.Equals(baseTheme, "Light", StringComparison.OrdinalIgnoreCase);
        var variant = ThemeCatalog.ValidLogoVariants.Contains(logoVariant, StringComparer.OrdinalIgnoreCase)
            ? logoVariant.ToLowerInvariant() : "native";

        // Base palette first (light or dark), then override the accent-derived keys.
        SwapColorsDictionary(app, ColorsNamespacePrefix + (isLight ? "Light" : "Dark") + "Colors.xaml");
        OverrideColor(app, "PrimaryColor", primary);
        OverrideColor(app, "PrimaryHover", ColorShadeGenerator.Hover(primary));
        OverrideColor(app, "PrimaryPressed", ColorShadeGenerator.Pressed(primary));
        OverrideColor(app, "PrimaryLight", ColorShadeGenerator.Light(primary));
        OverrideColor(app, "PrimaryDark", ColorShadeGenerator.Dark(primary));
        OverrideColor(app, "BorderAccent", primary);
        OverrideColor(app, "InputFocusBorder", primary);
        OverrideColor(app, "GlassBorder", Color.FromArgb(0x33, primary.R, primary.G, primary.B));

        ApplyMaterialDesignTheme(app, isLight, primary);

        Preferences.Custom = new CustomThemeSettings
        {
            BaseTheme = isLight ? "Light" : "Dark",
            PrimaryHex = ColorShadeGenerator.ToHex(primary),
            LogoVariant = variant,
        };
        CurrentThemeId = ThemeCatalog.CustomId;
        ApplyLogo(app, variant);

        _preferencesService.Save(Preferences);
        ThemeChanged?.Invoke();
    }

    // ---- internals ----

    private static void SwapColorsDictionary(Application app, string source)
    {
        var dictionaries = app.Resources.MergedDictionaries;
        var index = dictionaries.FindIndex(d => d.Source is not null && d.Source.OriginalString.Contains(ColorsNamespacePrefix));
        var newDict = new ResourceDictionary { Source = new Uri(source, UriKind.Relative) };
        if (index >= 0) dictionaries[index] = newDict; else dictionaries.Add(newDict);
    }

    private static void OverrideColor(Application app, string key, Color color)
        => app.Resources[key] = color; // re-points the resource; brushes re-resolve via DynamicResource

    private void ApplyMaterialDesignTheme(Application app, bool isLight, Color? primary)
    {
        try
        {
            var helper = new PaletteHelper();
            var theme = helper.GetTheme();
            theme.SetBaseTheme(isLight ? BaseTheme.Light : BaseTheme.Dark);
            if (primary is { } p)
            {
                theme.SetPrimaryColor(p);
                theme.SetSecondaryColor(p);
            }
            helper.SetTheme(theme);
        }
        catch
        {
            // MaterialDesign palette failures must never break theme switching —
            // the custom dictionary above still drives VyralBot's own brushes.
        }
    }

    private bool TryGetAccentFor(string themeId, out string accentHex, out string logoVariant, out string colorsSource)
    {
        accentHex = "#14B8A6"; logoVariant = "native"; colorsSource = ColorsNamespacePrefix + "NativeColors.xaml";
        if (string.Equals(themeId, ThemeCatalog.CustomId, StringComparison.OrdinalIgnoreCase))
        {
            accentHex = Preferences.Custom.PrimaryHex;
            logoVariant = Preferences.Custom.LogoVariant;
            colorsSource = null; // custom applies base + overrides itself
            return false;
        }
        var option = ThemeCatalog.Find(themeId) ?? ThemeCatalog.Find(ThemeCatalog.NativeId)!;
        accentHex = option.AccentHex;
        logoVariant = option.LogoVariant;
        colorsSource = ColorsNamespacePrefix + Capitalize(option.Id) + "Colors.xaml";
        return true;
    }

    private bool TryGetIsLight(string themeId)
        => string.Equals(themeId, ThemeCatalog.CustomId, StringComparison.OrdinalIgnoreCase)
            ? string.Equals(Preferences.Custom.BaseTheme, "Light", StringComparison.OrdinalIgnoreCase)
            : ThemeCatalog.Find(themeId)?.IsLight ?? false;

    private void ApplyLogo(Application app, string variant)
    {
        CurrentLogoVariant = variant;
        var logo = ResolveLogoImage(variant);
        if (logo is not null)
            app.Resources["AppLogoSource"] = logo;
    }

    public ImageSource? ResolveLogoImage(string variant)
    {
        if (LogoCache.TryGetValue(variant, out var cached)) return cached;

        var candidates = variant == "native"
            ? new[] { "pack://application:,,,/Resources/Images/vyral-logo.png" }
            : new[]
            {
                $"pack://application:,,,/Resources/Images/vyral-logo-{variant}.png",
                "pack://application:,,,/Resources/Images/vyral-logo.png", // fallback until variant PNG exists
            };

        foreach (var uri in candidates)
        {
            try
            {
                var frame = BitmapFrame.Create(new Uri(uri), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                frame.Freeze();
                LogoCache[variant] = frame;
                return frame;
            }
            catch { /* variant missing → try next */ }
        }
        return null;
    }

    public static bool LogoVariantExists(string variant)
    {
        if (variant == "native") return true;
        try
        {
            using var stream = Application.GetResourceStream(
                new Uri($"pack://application:,,,/Resources/Images/vyral-logo-{variant}.png"));
            return stream is not null;
        }
        catch { return false; }
    }

    private static string Capitalize(string s)
        => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s[1..];
}
```
Note: `dictionaries.FindIndex` — `Collection<ResourceDictionary>` has no FindIndex; implement a small loop:
```csharp
var index = -1;
for (var i = 0; i < dictionaries.Count; i++)
    if (dictionaries[i].Source?.OriginalString.Contains(ColorsNamespacePrefix) == true) { index = i; break; }
```

- [ ] **Step 2: Wire App.xaml.cs**

In `ConfigureServices`, add: `services.AddSingleton<UiPreferencesService>(); services.AddSingleton(_ => ThemeManager.Instance);`
In `OnStartup`, immediately after `ServiceProvider = _host.Services;` and **before** `MainWindow` resolution:
```csharp
            // Apply the saved theme before any window renders (app-level preference).
            try { ThemeManager.Instance.Initialize(); }
            catch (Exception ex) { Log.Warning(ex, "Failed to apply saved theme - using native"); }
```

- [ ] **Step 3: MainWindow icon**

`MainWindow.xaml.cs` constructor (after `InitializeComponent()`):
```csharp
        Icon = ThemeManager.Instance.ResolveLogoImage(ThemeManager.Instance.CurrentLogoVariant) as ImageSource;
        ThemeManager.Instance.ThemeChanged += () => Dispatcher.Invoke(() =>
            Icon = ThemeManager.Instance.ResolveLogoImage(ThemeManager.Instance.CurrentLogoVariant) as ImageSource);
```
(Adjust to actual ctor body; if a ThemeChanged subscription leaks on window close, unsubscribe in `Closing`.)

- [ ] **Step 4: Build + launch smoke test**

Run: `dotnet build VyralBot.sln` → 0 errors.
Launch `RUN.bat` or the built exe briefly; confirm app starts with native theme unchanged and `appdata/ui-settings.json` is NOT yet written (Initialize persists only on explicit ApplyTheme with persist:true — verify no `persist` happens on init unless id != default).

- [ ] **Step 5: Commit** — `feat(themes): ThemeManager with runtime switching, MaterialDesign palette sync, logo variant resolution`

---

### Task 4: Theme picker UI (Settings → Theme) + SettingsViewModel

**Files:**
- Modify: `src/VyralBot.UI/ViewModels/SettingsViewModel.cs`
- Modify: `src/VyralBot.UI/Views/SettingsView.xaml` (replace "Coming Soon..." Theme panel, line ~611)

**Interfaces:**
- Consumes: `ThemeManager` (Task 3), `ThemeCatalog`/`ThemeOption`/`ColorShadeGenerator` (Task 1).
- Produces (on SettingsViewModel): `ObservableCollection<ThemeOption> Themes`, `ThemeOption SelectedTheme`, `IAsyncRelayCommand SelectThemeCommand` (parameter `ThemeOption`), custom-theme section: `bool CustomIsLight`, `string CustomPrimaryHex`, `string CustomLogoVariant`, `IReadOnlyList<string> LogoVariants`, `IAsyncRelayCommand ApplyCustomThemeCommand`.

- [ ] **Step 1: SettingsViewModel — add theme state and commands**

Add fields/properties (constructor gains no new deps — uses `ThemeManager.Instance`):
```csharp
    public ObservableCollection<ThemeOption> Themes { get; } =
        new(ThemeCatalog.BuiltInThemes);

    [ObservableProperty]
    private ThemeOption? _selectedTheme;

    [ObservableProperty]
    private bool _customIsLight;

    [ObservableProperty]
    private string _customPrimaryHex = "#14B8A6";

    [ObservableProperty]
    private string _customLogoVariant = "native";

    public IReadOnlyList<string> LogoVariants => ThemeCatalog.ValidLogoVariants;

    partial void OnSelectedThemeChanged(ThemeOption? value)
    {
        if (value is null) return;
        CustomIsLight = value.IsLight;
        CustomPrimaryHex = value.AccentHex;
        CustomLogoVariant = value.LogoVariant;
    }
```
In `InitializeAsync()` append:
```csharp
            var themeManager = ThemeManager.Instance;
            SelectedTheme = ThemeCatalog.Find(themeManager.CurrentThemeId)
                            ?? ThemeCatalog.Find(ThemeCatalog.NativeId);
            CustomIsLight = string.Equals(themeManager.Preferences.Custom.BaseTheme, "Light", StringComparison.OrdinalIgnoreCase);
            CustomPrimaryHex = themeManager.Preferences.Custom.PrimaryHex;
            CustomLogoVariant = themeManager.Preferences.Custom.LogoVariant;
```
Commands:
```csharp
    [RelayCommand]
    private void SelectTheme(ThemeOption? theme)
    {
        if (theme is null) return;
        SelectedTheme = theme;
        ThemeManager.Instance.ApplyTheme(theme.Id);
        StatusMessage = $"{theme.DisplayName} theme applied";
        _logger.LogInformation("Theme applied: {ThemeId}", theme.Id);
        _ = ClearStatusAfterDelayAsync();
    }

    [RelayCommand]
    private void ApplyCustomTheme()
    {
        if (!ColorShadeGenerator.TryParseHex(CustomPrimaryHex, out _))
        {
            StatusMessage = "Invalid hex color - use #RRGGBB";
            return;
        }
        ThemeManager.Instance.ApplyCustomTheme(
            CustomIsLight ? "Light" : "Dark", CustomPrimaryHex, CustomLogoVariant);
        SelectedTheme = ThemeCatalog.CustomOption;
        StatusMessage = "Custom theme applied";
        _ = ClearStatusAfterDelayAsync();
    }

    private async Task ClearStatusAfterDelayAsync()
    {
        await Task.Delay(2000);
        StatusMessage = null;
    }
```
(needed usings: `VyralBot.UI.Models`, `VyralBot.UI.Services`.)

- [ ] **Step 2: SettingsView — Theme panel XAML**

Replace the Theme placeholder `<StackPanel>` (lines ~611-620) with:
```xml
                        <!-- Theme Panel -->
                        <StackPanel Visibility="{Binding SelectedCategory, Converter={StaticResource EqualsToVisibilityConverter}, ConverterParameter=Theme}">
                            <TextBlock Text="Theme Settings"
                                       FontSize="20" FontWeight="SemiBold"
                                       Foreground="{DynamicResource TextPrimaryBrush}"
                                       Margin="0,0,0,16"/>

                            <!-- Built-in Themes -->
                            <TextBlock Text="Built-in Themes" FontSize="14" FontWeight="SemiBold"
                                       Foreground="{DynamicResource TextSecondaryBrush}" Margin="0,0,0,8"/>
                            <ItemsControl ItemsSource="{Binding Themes}" Margin="0,0,0,24">
                                <ItemsControl.ItemsPanel>
                                    <ItemsPanelTemplate>
                                        <UniformGrid Columns="3"/>
                                    </ItemsPanelTemplate>
                                </ItemsControl.ItemsPanel>
                                <ItemsControl.ItemTemplate>
                                    <DataTemplate>
                                        <Button Command="{Binding DataContext.SelectThemeCommand, RelativeSource={RelativeSource AncestorType=ItemsControl}}"
                                                CommandParameter="{Binding}"
                                                Margin="0,0,12,12" Padding="0" HorizontalContentAlignment="Stretch"
                                                Style="{StaticResource MaterialDesignRaisedButton}"
                                                Background="{DynamicResource CardBackgroundBrush}"
                                                BorderBrush="{DynamicResource BorderPrimaryBrush}" BorderThickness="1">
                                            <StackPanel Margin="12" Orientation="Horizontal">
                                                <!-- theme preview: background swatch + accent dot -->
                                                <Border Width="44" Height="44" CornerRadius="6"
                                                        BorderBrush="{DynamicResource BorderPrimaryBrush}" BorderThickness="1">
                                                    <Border.Background>
                                                        <SolidColorBrush Color="{Binding PreviewBackgroundHex,
                                                            Converter={StaticResource HexToColorConverter}}"/>
                                                    </Border.Background>
                                                    <Ellipse Width="18" Height="18" HorizontalAlignment="Center" VerticalAlignment="Center">
                                                        <Ellipse.Fill>
                                                            <SolidColorBrush Color="{Binding AccentHex,
                                                                Converter={StaticResource HexToColorConverter}}"/>
                                                        </Ellipse.Fill>
                                                    </Ellipse>
                                                </Border>
                                                <StackPanel Margin="12,0,0,0" VerticalAlignment="Center">
                                                    <TextBlock Text="{Binding DisplayName}" FontWeight="SemiBold"
                                                               Foreground="{DynamicResource TextPrimaryBrush}"/>
                                                    <StackPanel Orientation="Horizontal" Visibility="{Binding IsBuiltIn,
                                                        Converter={StaticResource BooleanToVisibilityConverter}}">
                                                        <materialDesign:PackIcon Kind="Check" Width="14" Height="14"
                                                                Foreground="{DynamicResource TextSecondaryBrush}"/>
                                                        <TextBlock Text="Built-in" FontSize="11"
                                                               Foreground="{DynamicResource TextSecondaryBrush}"/>
                                                    </StackPanel>
                                                </StackPanel>
                                                <!-- selected indicator -->
                                                <materialDesign:PackIcon Kind="CheckCircle" Width="20" Height="20" Margin="12,0,0,0"
                                                        VerticalAlignment="Center"
                                                        Foreground="{DynamicResource PrimaryBrush}"
                                                        Visibility="{Binding IsSelected, Converter={StaticResource BooleanToVisibilityConverter}}"/>
                                            </StackPanel>
                                        </Button>
                                    </DataTemplate>
                                </ItemsControl.ItemTemplate>
                            </ItemsControl>
```
**IsSelected problem:** `ThemeOption` is an immutable record; selection is on the VM. Simplest correct approach: replace the record `Visibility` binding with a DataTrigger comparing to `SelectedTheme` via a multi-converter, OR make `SelectedTheme` comparison available by adding a `BoolToVisibility` on a computed property. Choose: add converter `ReferenceEqualsToVisibilityConverter` (checks `ReferenceEquals(value, ConverterParameter)` won't work in DataTemplate binding)... **Decision:** make the selected indicator bind with a DataTrigger on the card Button style comparing `SelectedTheme.Id` using the existing `EqualsToVisibilityConverter` pattern:

Replace indicator binding with a Style on the indicator PackIcon:
```xml
                                                <materialDesign:PackIcon Kind="CheckCircle" Width="20" Height="20" Margin="12,0,0,0"
                                                        VerticalAlignment="Center"
                                                        Foreground="{DynamicResource PrimaryBrush}">
                                                    <materialDesign:PackIcon.Style>
                                                        <Style TargetType="materialDesign:PackIcon">
                                                            <Setter Property="Visibility" Value="Collapsed"/>
                                                            <Style.Triggers>
                                                                <DataTrigger Value="True">
                                                                    <DataTrigger.Binding>
                                                                        <MultiBinding Converter="{StaticResource ThemeEqualsConverter}">
                                                                            <Binding Path="Id"/>
                                                                            <Binding Path="DataContext.SelectedTheme.Id" RelativeSource="{RelativeSource AncestorType=ItemsControl}"/>
                                                                        </MultiBinding>
                                                                    </DataTrigger.Binding>
                                                                </DataTrigger>
                                                            </Style.Triggers>
                                                        </Style>
                                                    </materialDesign:PackIcon.Style>
                                                </materialDesign:PackIcon>
```
And **create** `src/VyralBot.UI/Converters/ThemeEqualsConverter.cs` (IMultiValueConverter, returns `Equals(a?.ToString(), b?.ToString())` case-insensitive) and register in `App.xaml` resources:
```xml
<converters:ThemeEqualsConverter x:Key="ThemeEqualsConverter"/>
```

Continue the panel — Custom Theme section:
```xml
                            <!-- Custom Theme -->
                            <TextBlock Text="Custom Theme" FontSize="14" FontWeight="SemiBold"
                                       Foreground="{DynamicResource TextSecondaryBrush}" Margin="0,0,0,8"/>
                            <materialDesign:Card Background="{DynamicResource CardBackgroundBrush}"
                                                 UniformCornerRadius="8" Padding="16" Margin="0,0,0,16">
                                <StackPanel>
                                    <StackPanel Orientation="Horizontal" Margin="0,0,0,12">
                                        <TextBlock Text="Base:" FontWeight="SemiBold" VerticalAlignment="Center"
                                                   Foreground="{DynamicResource TextPrimaryBrush}" Margin="0,0,8,0"/>
                                        <RadioButton Content="Dark" IsChecked="{Binding CustomIsLight, Converter={StaticResource InvertBooleanConverter}}"
                                                     GroupName="CustomBase" Margin="0,0,12,0"/>
                                        <RadioButton Content="Light" IsChecked="{Binding CustomIsLight}"
                                                     GroupName="CustomBase"/>
                                    </StackPanel>
                                    <StackPanel Orientation="Horizontal" Margin="0,0,0,12">
                                        <TextBlock Text="Accent color:" FontWeight="SemiBold" VerticalAlignment="Center"
                                                   Foreground="{DynamicResource TextPrimaryBrush}" Margin="0,0,8,0"/>
                                        <ItemsControl ItemsSource="{Binding CustomAccentSwatches}">
                                            <ItemsControl.ItemsPanel>
                                                <ItemsPanelTemplate><StackPanel Orientation="Horizontal"/></ItemsPanelTemplate>
                                            </ItemsControl.ItemsPanel>
                                            <ItemsControl.ItemTemplate>
                                                <DataTemplate>
                                                    <Button Command="{Binding DataContext.SelectCustomAccentCommand, RelativeSource={RelativeSource AncestorType=ItemsControl}}"
                                                            CommandParameter="{Binding}" Width="26" Height="26" Margin="2,0" Padding="0"
                                                            BorderThickness="1" BorderBrush="{DynamicResource BorderPrimaryBrush}"
                                                            Style="{StaticResource MaterialDesignFlatButton}">
                                                        <Button.Background>
                                                            <SolidColorBrush Color="{Binding Converter={StaticResource HexToColorConverter}}"/>
                                                        </Button.Background>
                                                    </Button>
                                                </DataTemplate>
                                            </ItemsControl.ItemTemplate>
                                        </ItemsControl>
                                    </StackPanel>
                                    <StackPanel Orientation="Horizontal" Margin="0,0,0,12">
                                        <TextBlock Text="Hex:" FontWeight="SemiBold" VerticalAlignment="Center"
                                                   Foreground="{DynamicResource TextPrimaryBrush}" Margin="0,0,8,0"/>
                                        <TextBox Text="{Binding CustomPrimaryHex, UpdateSourceTrigger=PropertyChanged}" Width="110"
                                                 Style="{StaticResource MaterialDesignTextBox}"/>
                                    </StackPanel>
                                    <StackPanel Orientation="Horizontal" Margin="0,0,0,12">
                                        <TextBlock Text="Logo color:" FontWeight="SemiBold" VerticalAlignment="Center"
                                                   Foreground="{DynamicResource TextPrimaryBrush}" Margin="0,0,8,0"/>
                                        <ComboBox ItemsSource="{Binding LogoVariants}" SelectedItem="{Binding CustomLogoVariant}" Width="120"/>
                                    </StackPanel>
                                    <Button Content="Apply Custom Theme" Style="{StaticResource MaterialDesignRaisedButton}"
                                            Command="{Binding ApplyCustomThemeCommand}" HorizontalAlignment="Left"/>
                                </StackPanel>
                            </materialDesign:Card>

                            <!-- Logo color note -->
                            <TextBlock TextWrapping="Wrap" FontSize="12"
                                       Foreground="{DynamicResource TextMutedBrush}"
                                       Text="Logo colors look for vyral-logo-{color}.png in Resources/Images (red, blue, green, white, black). Missing variants fall back to the native teal logo."/>
                        </StackPanel>
```
Supporting pieces:
- `CustomAccentSwatches` on the VM:
```csharp
    public IReadOnlyList<string> CustomAccentSwatches { get; } = new[]
    {
        "#EF4444", "#F97316", "#F59E0B", "#EAB308", "#84CC16", "#10B981", "#14B8A6", "#06B6D4",
        "#0EA5E9", "#3B82F6", "#6366F1", "#8B5CF6", "#A855F7", "#EC4899", "#F43F5E", "#94A3B8",
    };

    [RelayCommand]
    private void SelectCustomAccent(string hex)
    {
        CustomPrimaryHex = hex;
        ApplyCustomTheme();
    }
```
- New converter `src/VyralBot.UI/Converters/HexToColorConverter.cs`:
```csharp
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using VyralBot.UI.Models;

namespace VyralBot.UI.Converters;

/// <summary>Converts "#RRGGBB" strings to Color for theme swatch previews.</summary>
public sealed class HexToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => ColorShadeGenerator.TryParseHex(value as string, out var color) ? color : Colors.Transparent;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is Color c ? ColorShadeGenerator.ToHex(c) : "#000000";
}
```
- Verify `InvertBooleanConverter` exists in `Converters/` (an `InvertBooleanConverter.cs` exists — check its registered key in App.xaml; if not registered, register as `InvertBooleanConverter`).
- `BooleanToVisibilityConverter`: SettingsView's Theme panel is inside a context where it is referenced elsewhere in the file (used at line ~629) — it's declared in MainWindow Resources; verify SettingsView has access (it does at line 629 today), else declare in App.xaml.

- [ ] **Step 3: Build + manual smoke test**

Run: `dotnet build VyralBot.sln` → 0 errors. Launch app → Settings → Theme: click each theme card; backgrounds/accent change live; window icon changes for themes whose variant PNG exists (currently all fall back to native); `appdata/ui-settings.json` updates; restart → saved theme re-applies.

- [ ] **Step 4: Commit** — `feat(themes): theme picker UI with 9 built-ins + custom theme builder`

---

### Task 5: Dynamic logo across views

**Files:**
- Modify: `src/VyralBot.UI/Views/ChatView.xaml` (3 occurrences, incl. inside Style Setter line ~40)
- Modify: `src/VyralBot.UI/Views/ChatHubView.xaml`, `Views/IntegrationsView.xaml`, `Views/PlatformsView.xaml`, `Views/SettingsView.xaml`, `MainWindow.xaml` (title bar)

- [ ] **Step 1: Swap static logo Source → DynamicResource**

Every `<Image Source="/Resources/Images/vyral-logo.png" .../>` becomes:
```xml
<Image Source="{DynamicResource AppLogoSource}" .../>
```
For the Style Setter at ChatView.xaml:40 — `<Setter Property="Source" Value="{DynamicResource AppLogoSource}"/>`.
Note: `Image.Source` with `DynamicResource` to an `ImageSource` resource works directly; `MainWindow.Icon` was already handled in Task 3 via code-behind (Window.Icon is not a DependencyObject-scoped resource consumer).

- [ ] **Step 2: Build + verify logo still shows, no StaticResource warnings**

`dotnet build VyralBot.sln` → 0 errors; launch → logo visible in title bar + chat views.

- [ ] **Step 3: Commit** — `feat(themes): bind logo images to AppLogoSource for per-theme logo variants`

---

### Task 6: Hardcoded color cleanup (theme-proof the views)

**Files:**
- Modify: `Views/CurrencyView.xaml` (65 hexes — VS-gray palette → theme brushes)
- Modify: `Views/FlowEditorView.xaml` (53), `Views/EventHistoryView.xaml` (33), `Views/GiveawayView.xaml` (12)
- Modify minor files: `ChatView.xaml`, `KickDashboardView.xaml`, `ConditionalRulesView.xaml`, `PollView.xaml`, `VariablesView.xaml`, `DocumentationView.xaml`, `AlertOverlayWindow.xaml`, `WebServerSettingsView.xaml`, `YouTubeWebSetupView.xaml`, `DiscordWebSetupView.xaml`, `NgrokSetupWizardView.xaml`, `MainWindow.xaml` (any remaining hexes)

**Mapping table (mechanical replacements):**
| Hardcoded | Replace with |
|---|---|
| `#1E1E1E` / `#0F172A`-family page backgrounds | `{DynamicResource BackgroundPrimaryBrush}` |
| `#252526` / `#2D2D30` / `#1E293B` cards/panels | `{DynamicResource CardBackgroundBrush}` (alias of BackgroundSecondaryBrush) |
| `#3E3E42` / `#334155` / `#475569` borders | `{DynamicResource BorderPrimaryBrush}` |
| `#B0B0B0` / `#94A3B8` secondary text | `{DynamicResource TextSecondaryBrush}` |
| `#CCCCCC` / `#FFFFFF` foregrounds on dark | `{DynamicResource TextPrimaryBrush}` |
| `#007ACC` / `#3B82F6` buttons/accents | `{DynamicResource PrimaryBrush}` |
| `#007ACC` hover overlays | `{DynamicResource PrimaryHoverBrush}` |
| `#F3F4F6` / `#3F3F46` hovers | `{DynamicResource SurfaceHoverBrush}` |
| `#10B981`-family success | `{DynamicResource SuccessBrush}` |
| `#EF4444`-family errors | `{DynamicResource ErrorBrush}` |
| `#F59E0B`-family warnings | `{DynamicResource WarningBrush}` |
| Semi-transparent blacks (`#AA000000` overlays) | `{DynamicResource OverlayBackgroundBrush}` |

**Rules:**
1. KEEP platform-brand hexes verbatim (`#9146FF` Twitch, `#53FC18` Kick, `#FF0000`/`#CC0000` YouTube, `#00F2EA`/`#FF0050` TikTok, `#5865F2` Discord) and any hex inside `SolidColorBrush` used to tint an avatar/badge by platform identity.
2. `Foreground="White"` inside buttons sitting on PrimaryBrush backgrounds → `{DynamicResource TextOnPrimaryBrush}` (critical for Carbon theme where TextOnPrimary is dark).
3. After each file: build.

- [ ] **Step 1: CurrencyView.xaml** — full conversion (worst offender, zero theme brushes today). Apply mapping table; keep accent `#007ACC`→PrimaryBrush in tab triggers; build.
- [ ] **Step 2: FlowEditorView.xaml + EventHistoryView.xaml** — mapping table; build.
- [ ] **Step 3: GiveawayView.xaml + all minor files** — mapping table; build after each file.
- [ ] **Step 4: Final sweep** — `grep -rn "#[0-9A-Fa-f]\{6\}" Views/*.xaml MainWindow.xaml` → confirm only platform-brand hexes + intentional on-brand colors remain; document the survivors in the tracking plan.
- [ ] **Step 5: Launch smoke test on Light theme** — every converted view readable on light backgrounds (this is where hardcoded darks would break).
- [ ] **Step 6: Commit** — `refactor(themes): replace hardcoded colors with theme brushes across views`

---

### Task 7: FlowCanvas theme-aware palette

**Files:**
- Modify: `src/VyralBot.UI/Controls/FlowCanvas/FlowCanvasPalette.cs`

**Interfaces:**
- Consumes: theme color resources `FlowCanvas*` (Task 2).
- Produces: same `public const string` names for canvas-internal defaults, PLUS a theme lookup used at color-consumption sites.

- [ ] **Step 1: Add theme lookup**

```csharp
    /// <summary>
    /// Returns the themed canvas color for a palette key (e.g. nameof(CanvasBackground)),
    /// falling back to the const default when the theme system is unavailable (designer/tests).
    /// </summary>
    public static string GetThemed(string key, string fallback)
    {
        try
        {
            if (System.Windows.Application.Current?.TryFindResource(key) is System.Windows.Media.Color color)
                return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        }
        catch { }
        return fallback;
    }
```
Then replace each *consumption site* (not the consts — keep them as defaults) where a palette const is used to build a Brush: e.g. `new SolidColorBrush((Color)ColorConverter.ConvertFromString(FlowCanvasPalette.CanvasBackground))` → `FromPalette(nameof(FlowCanvasPalette.CanvasBackground))` helper:
```csharp
    public static System.Windows.Media.Brush FromPalette(string key, string fallback)
    {
        var brush = new System.Windows.Media.SolidColorBrush(
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(GetThemed(key, fallback)));
        brush.Freeze();
        return brush;
    }
```
Locate consumption sites with `grep -n "FlowCanvasPalette\." src/VyralBot.UI/Controls/FlowCanvas/*.cs` and update each to `FromPalette(...)`. For brushes that must update live on theme switch (canvas background), subscribe: `ThemeManager.Instance.ThemeChanged += InvalidateCache` where the canvas caches brushes — if no cache exists (colors computed per-frame/per-node), no subscription needed.

- [ ] **Step 2: Build + smoke test** — Flow editor renders with themed colors; switching theme re-colors canvas (or on next open).
- [ ] **Step 3: Commit** — `feat(themes): theme-aware FlowCanvas palette`

---

### Task 8: Full verification + docs

- [ ] **Step 1:** `dotnet test tests/VyralBot.UI.Tests/VyralBot.UI.Tests.csproj` → all pass.
- [ ] **Step 2:** `dotnet build VyralBot.sln -c Debug` → 0 errors (record warning count).
- [ ] **Step 3:** Launch app; switch through ALL 9 built-ins + custom (Light base + Dark base, hex input, swatch, logo variant); verify: sidebar/content/chat/settings readable, window icon updates, `appdata/ui-settings.json` correct, restart restores last theme, Light themes show no dark-on-dark text in: Dashboard, Chat, Commands, Currency, Statistics, Settings, Event History, Giveaway, Flow Editor.
- [ ] **Step 4:** Add `README` section stub? No — document in `VyralBot/docs/ui/` a short `THEMING.md` (how to add a built-in theme, how logo variants resolve, where prefs live).
- [ ] **Step 5:** Update the tracking plan (`2026-09-01-theming-system-tracking.md`) to final status; commit `docs: theming system complete`.

## Self-Review (done 2026-09-01)

1. **Spec coverage:** Light ✓ (LightColors + Frost), Dark ✓ (DarkColors), custom colors ✓ (Task 4 custom builder + Task 1 shade gen), 6 unique built-ins ✓ (crimson, azure, emerald, frost, carbon, ember + native = catalog), logo same design per-theme color ✓ (AppLogoSource + variant resolver + fallback until user uploads PNGs).
2. **Placeholder scan:** none — all code complete; Task 6 uses an explicit mapping table with per-file verification steps.
3. **Type consistency:** `ThemeOption(Id, DisplayName, IsLight, AccentHex, LogoVariant, PreviewBackgroundHex, IsBuiltIn)` used identically in catalog/picker XAML; `UiPreferences.Custom` = `CustomThemeSettings(BaseTheme, PrimaryHex, LogoVariant)` matches VM + persistence + ThemeManager.
