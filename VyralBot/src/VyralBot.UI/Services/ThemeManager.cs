using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MaterialDesignThemes.Wpf;
using VyralBot.UI.Models;

namespace VyralBot.UI.Services;

/// <summary>
/// Applies and switches application themes at runtime.
/// Brushes live in Themes/ThemeBrushes.xaml and wrap colors via DynamicResource, so
/// swapping the merged colors dictionary re-themes every view instantly.
/// </summary>
public sealed class ThemeManager
{
    public static ThemeManager Instance { get; } = new();

    private const string ColorsNamespaceMarker = "/Themes/Colors/";
    private const string BrushesDictionarySource = "/Themes/ThemeBrushes.xaml";

    /// <summary>
    /// The accent keys ApplyCustomTheme writes DIRECTLY into app.Resources.
    /// A top-level entry SHADOWS every merged dictionary forever, so after a
    /// custom theme was used once, the built-in themes must remove these keys
    /// again (see <see cref="ClearOverrideColors"/>) or the custom accent stays
    /// stamped over them ("restart to fully apply" symptom).
    /// </summary>
    private static readonly string[] OverrideColorKeys =
    {
        "PrimaryColor",
        "PrimaryHover",
        "PrimaryPressed",
        "PrimaryLight",
        "PrimaryDark",
        "BorderAccent",
        "InputFocusBorder",
        "GlassBorder",
    };

    private static readonly Dictionary<string, ImageSource> LogoCache = new();

    /// <summary>
    /// NEUTRAL background/surface/border keys that shift with the brightness level.
    /// Key names verified against every Themes/Colors/*.xaml palette (all 20 exist in
    /// all 10 dictionaries). Text keys and semantic accents (Primary*/Error*/Success*/
    /// Info*/Warning*/DiscordColor, FlowCanvas port/wire/selection/accent colors) are
    /// intentionally NOT listed — readability and semantics stay fixed.
    /// </summary>
    private static readonly string[] ShadeNeutralKeys =
    {
        "BackgroundPrimary", "BackgroundSecondary", "BackgroundTertiary",
        "SurfaceColor", "SurfaceHover", "SurfacePressed",
        "BorderLight", "BorderPrimary", "BorderSecondary",
        "GlassBackground", "InputBackground", "InputBorder",
        "FlowCanvasBackground", "FlowCanvasGridDot",
        "FlowCanvasNodeBackground", "FlowCanvasNodeBorder", "FlowCanvasNodeHover",
        "FlowCanvasChipBackground", "FlowCanvasChipBorder", "FlowCanvasChipBorderHover",
    };

    /// <summary>
    /// Cap of the shade effect at |level| == 100, so even the extremes never fully
    /// invert borders/surfaces against fixed text colors.
    /// </summary>
    private const double MaxShadeShift = 0.55;

    /// <summary>
    /// Pristine colors of the ACTIVE merged colors dictionary, re-snapshotted on every
    /// colors-dictionary swap. Shade levels are ALWAYS recomputed from this snapshot —
    /// never from app.Resources, where previous shade overrides live top-level and
    /// would compound on every slider tick.
    /// </summary>
    private Dictionary<string, Color>? _basePaletteSnapshot;

    /// <summary>
    /// The colors dictionary currently merged into app.Resources. After <see cref="ApplyShade"/>
    /// the merged dictionary is a hand-built instance with Source == null, so the Source-marker
    /// scan in <see cref="SwapColorsDictionary"/> can no longer find it — tracking the instance
    /// lets the swap REPLACE it instead of stacking a new dictionary on every theme change (leak).
    /// </summary>
    private ResourceDictionary? _activeColorsDictionary;

    /// <summary>Shade override keys currently present in app.Resources (removed exactly on restore/swap).</summary>

    private readonly UiPreferencesService _preferencesService = new();

    public UiPreferences Preferences { get; private set; } = UiPreferences.Defaults;

    /// <summary>Catalog id of the active theme ("native", "crimson", ..., or "custom").</summary>
    public string CurrentThemeId { get; private set; } = ThemeCatalog.NativeId;

    public string CurrentLogoVariant { get; private set; } = "native";

    /// <summary>Raised after a theme (or logo) change has been applied to the application resources.</summary>
    public event Action? ThemeChanged;

    /// <summary>Loads persisted preferences and applies the saved theme. Call once at startup, before any window renders.</summary>
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

        if (string.Equals(themeId, ThemeCatalog.CustomId, StringComparison.OrdinalIgnoreCase))
        {
            // Re-apply the persisted custom definition so its overrides land on the fresh base dictionary.
            ApplyCustomTheme(Preferences.Custom.BaseTheme, Preferences.Custom.PrimaryHex, Preferences.Custom.LogoVariant, persist: false);
            return;
        }

        var option = ThemeCatalog.Find(themeId) ?? ThemeCatalog.Find(ThemeCatalog.NativeId)!;
        SwapColorsDictionary(app, ColorsNamespaceMarker + Capitalize(option.Id) + "Colors.xaml");

        // A previous custom theme may have stamped its accent shades DIRECTLY
        // into app.Resources — those top-level entries shadow the merged
        // dictionary forever. Remove them so the built-in palette's values win
        // again (the custom path re-writes them on demand).
        ClearOverrideColors(app);

        ColorShadeGenerator.TryParseHex(option.AccentHex, out var accent);
        ApplyMaterialDesignTheme(app, option.IsLight, accent);

        CurrentThemeId = option.Id;
        ApplyLogo(app, option.LogoVariant);

        if (persist)
        {
            Preferences.ThemeId = CurrentThemeId;
            _preferencesService.Save(Preferences);
        }

        // Carry the user's brightness level for this base onto the new palette.
        ApplyPersistedShade(app);

        ThemeChanged?.Invoke();
    }

    /// <summary>
    /// Applies a user-defined theme: loads the Light or Dark base palette, then overrides
    /// the accent-derived colors with shades generated from the picked accent.
    /// </summary>
    public void ApplyCustomTheme(string baseTheme, string primaryHex, string logoVariant, bool persist = true)
    {
        var app = Application.Current;
        if (app is null) return;

        if (!ColorShadeGenerator.TryParseHex(primaryHex, out var primary))
            primary = Color.FromRgb(0x14, 0xB8, 0xA6);

        var isLight = string.Equals(baseTheme, "Light", StringComparison.OrdinalIgnoreCase);
        var variant = ThemeCatalog.ValidLogoVariants
            .FirstOrDefault(v => string.Equals(v, logoVariant, StringComparison.OrdinalIgnoreCase)) ?? "native";

        // Base palette first, then override the accent-derived keys.
        SwapColorsDictionary(app, ColorsNamespaceMarker + (isLight ? "Light" : "Dark") + "Colors.xaml");
        OverrideColor(app, "PrimaryColor", primary);
        OverrideColor(app, "PrimaryHover", ColorShadeGenerator.Hover(primary));
        OverrideColor(app, "PrimaryPressed", ColorShadeGenerator.Pressed(primary));
        OverrideColor(app, "PrimaryLight", ColorShadeGenerator.Light(primary));
        OverrideColor(app, "PrimaryDark", ColorShadeGenerator.Dark(primary));
        OverrideColor(app, "BorderAccent", primary);
        OverrideColor(app, "InputFocusBorder", primary);
        OverrideColor(app, "GlassBorder", Color.FromArgb(0x33, primary.R, primary.G, primary.B));

        ApplyMaterialDesignTheme(app, isLight, primary);

        CurrentThemeId = ThemeCatalog.CustomId;
        Preferences.Custom = new CustomThemeSettings
        {
            BaseTheme = isLight ? "Light" : "Dark",
            PrimaryHex = ColorShadeGenerator.ToHex(primary),
            LogoVariant = variant,
        };
        ApplyLogo(app, variant);

        if (persist)
        {
            Preferences.ThemeId = ThemeCatalog.CustomId;
            _preferencesService.Save(Preferences);
        }

        // Carry the user's brightness level for this base onto the new palette.
        ApplyPersistedShade(app);

        ThemeChanged?.Invoke();
    }

    /// <summary>
    /// Lightweight LIVE accent update for the custom theme: rewrites the 8 accent overrides
    /// (same keys as <see cref="ApplyCustomTheme"/>) and the MaterialDesign palette on top of
    /// the CURRENTLY loaded base palette.
    /// <para>
    /// Unlike <see cref="ApplyCustomTheme"/> this deliberately does NOT re-swap the Light/Dark
    /// base colors dictionary — a full swap re-snapshots the whole palette, re-creates the
    /// brushes dictionary and re-applies the persisted shade, which is far too heavy for
    /// continuous color-picker drags, and the neutral base palette does not change when only
    /// the accent changes. Use <see cref="ApplyCustomTheme"/> when the base theme changes.
    /// </para>
    /// Selecting an accent also switches the active theme to the custom slot (same behavior as
    /// the accent swatch row). Safe to call repeatedly, e.g. once per debounced drag tick.
    /// </summary>
    public void UpdateCustomAccent(string primaryHex)
    {
        var app = Application.Current;
        if (app is null) return;

        // Garbage input falls back to the currently stored custom accent, then to the teal default.
        if (!ColorShadeGenerator.TryParseHex(primaryHex, out var primary))
        {
            if (!ColorShadeGenerator.TryParseHex(Preferences.Custom.PrimaryHex, out primary))
                primary = Color.FromRgb(0x14, 0xB8, 0xA6);
        }

        // Accent overrides only — the base colors dictionary and its pristine palette
        // snapshot stay untouched, so the brightness shade engine keeps working unchanged.
        OverrideColor(app, "PrimaryColor", primary);
        OverrideColor(app, "PrimaryHover", ColorShadeGenerator.Hover(primary));
        OverrideColor(app, "PrimaryPressed", ColorShadeGenerator.Pressed(primary));
        OverrideColor(app, "PrimaryLight", ColorShadeGenerator.Light(primary));
        OverrideColor(app, "PrimaryDark", ColorShadeGenerator.Dark(primary));
        OverrideColor(app, "BorderAccent", primary);
        OverrideColor(app, "InputFocusBorder", primary);
        OverrideColor(app, "GlassBorder", Color.FromArgb(0x33, primary.R, primary.G, primary.B));

        var isLight = IsLightBaseActive(); // the accent layers over the CURRENTLY applied base, not a stale Custom.BaseTheme
        ApplyMaterialDesignTheme(app, isLight, primary);

        CurrentThemeId = ThemeCatalog.CustomId;
        Preferences.Custom.BaseTheme = isLight ? "Light" : "Dark"; // persist the base actually on screen so the next launch matches
        Preferences.Custom.PrimaryHex = ColorShadeGenerator.ToHex(primary);
        Preferences.ThemeId = ThemeCatalog.CustomId;
        _preferencesService.Save(Preferences);

        ApplyLogo(app, Preferences.Custom.LogoVariant); // variant unchanged — keeps logo logic in one place

        ThemeChanged?.Invoke();
    }

    // ---- brightness shade engine ----

    /// <summary>
    /// Applies a brightness level (-100..+100) to the neutral colors of the given base:
    /// negative shifts toward black, positive toward white, 0 restores the native palette.
    /// Cheap (pure in-memory color math + dictionary writes) and safe on the UI thread.
    /// Fires <see cref="ThemeChanged"/> so every view — including the flow canvas —
    /// re-resolves live.
    /// </summary>
    public void ApplyShadeLevel(bool isLightBase, int level)
    {
        var app = Application.Current;
        if (app is null) return;

        level = Math.Clamp(level, -100, 100);
        if (isLightBase)
            Preferences.LightShadeLevel = level;
        else
            Preferences.DarkShadeLevel = level;
        _preferencesService.Save(Preferences);

        ApplyShade(app, level);
        ThemeChanged?.Invoke();
    }

    /// <summary>The persisted shade level for the given base (clamped) — used to populate sliders.</summary>
    public int GetShadeLevel(bool isLightBase)
        => Math.Clamp(isLightBase ? Preferences.LightShadeLevel : Preferences.DarkShadeLevel, -100, 100);

    /// <summary>
    /// True when the ACTIVE theme uses the light base palette. Built-in themes use
    /// <see cref="ThemeOption.IsLight"/>; the custom theme honors Custom.BaseTheme.
    /// </summary>
    public bool IsLightBaseActive()
    {
        if (string.Equals(CurrentThemeId, ThemeCatalog.CustomId, StringComparison.OrdinalIgnoreCase))
            return string.Equals(Preferences.Custom.BaseTheme, "Light", StringComparison.OrdinalIgnoreCase);
        return ThemeCatalog.Find(CurrentThemeId)?.IsLight ?? false;
    }

    /// <summary>
    /// End of the theme-apply pipeline: re-apply (or clear) the persisted shade for the
    /// NEW base, so switching themes keeps the user's chosen darkness level. Runs BEFORE
    /// the caller's ThemeChanged so no extra event is fired.
    /// </summary>
    private void ApplyPersistedShade(Application app)
        => ApplyShade(app, GetShadeLevel(IsLightBaseActive()));

    private void ApplyShade(Application app, int level)
    {
        // THE MECHANISM: theme switches work live everywhere because they replace
        // the merged COLORS dictionary AND the ThemeBrushes dictionary with fresh
        // instances (brush instances in merged dictionaries are sealed after load
        // and never re-read their {DynamicResource Color} values — proven by a
        // headless harness: writing a top-level color override alone does NOT
        // update existing brushes). Shading therefore uses the exact same
        // mechanism: build a FRESH colors dictionary from the pristine snapshot
        // with the neutral keys shaded, replace the merged instance, then replace
        // the brushes dictionary. Full-coverage by construction.
        if (_basePaletteSnapshot is null)
            return;

        level = Math.Clamp(level, -100, 100);
        var t = Math.Abs(level) / 100.0 * MaxShadeShift;

        var shaded = new ResourceDictionary();
        foreach (var kvp in _basePaletteSnapshot)
        {
            var color = ShadeNeutralKeys.Contains(kvp.Key)
                ? ShadeColor(kvp.Value, t, towardWhite: level > 0)
                : kvp.Value; // non-neutral keys copy through verbatim
            shaded[kvp.Key] = color;
        }

        // Replace the merged colors dictionary (by its marker) with the shaded one.
        var dictionaries = app.Resources.MergedDictionaries;
        var index = -1;
        for (var i = 0; i < dictionaries.Count; i++)
        {
            if (dictionaries[i].Source?.OriginalString.Contains(ColorsNamespaceMarker) == true)
            {
                index = i;
                break;
            }
        }
        if (index >= 0)
            dictionaries[index] = shaded;
        else
            dictionaries.Add(shaded);
        _activeColorsDictionary = shaded; // Source == null — the next swap must find this by reference

        ReplaceBrushesDictionary(dictionaries);

        // NOTE: Custom-theme accent overrides are TOP-LEVEL app resources and are
        // not part of the snapshot, so they keep shadowing the fresh native
        // accent colors — custom accents survive shading by design.
    }

    /// <summary>Channel-wise lerp toward white or black; alpha is preserved (GlassBackground is semi-transparent).</summary>
    private static Color ShadeColor(Color c, double t, bool towardWhite)
    {
        byte Shift(byte channel) => towardWhite
            ? (byte)Math.Round(channel + (255 - channel) * t)
            : (byte)Math.Round(channel * (1.0 - t));
        return Color.FromArgb(c.A, Shift(c.R), Shift(c.G), Shift(c.B));
    }

    // ---- internals ----

    private void SwapColorsDictionary(Application app, string source)
    {
        var dictionaries = app.Resources.MergedDictionaries;
        var index = -1;
        for (var i = 0; i < dictionaries.Count; i++)
        {
            if (dictionaries[i].Source?.OriginalString.Contains(ColorsNamespaceMarker) == true
                || ReferenceEquals(dictionaries[i], _activeColorsDictionary)) // shaded dictionaries have Source == null
            {
                index = i;
                break;
            }
        }

        var newDict = new ResourceDictionary { Source = new Uri(source, UriKind.Relative) };
        if (index >= 0)
            dictionaries[index] = newDict; // replaces/removes the tracked old dictionary
        else
            dictionaries.Add(newDict);

        _activeColorsDictionary = newDict;

        ReplaceBrushesDictionary(dictionaries);
        SnapshotBasePalette(newDict);
    }

    /// <summary>
    /// Copies every Color entry of the freshly swapped colors dictionary into a pristine
    /// snapshot. Runs BEFORE any accent/shade overrides land, so the snapshot is always
    /// the untouched native palette of the active theme.
    /// </summary>
    private void SnapshotBasePalette(ResourceDictionary colorsDictionary)
    {
        var snapshot = new Dictionary<string, Color>(colorsDictionary.Count);
        foreach (var key in colorsDictionary.Keys)
        {
            if (key is string name && colorsDictionary[name] is Color color)
                snapshot[name] = color;
        }
        _basePaletteSnapshot = snapshot;
    }

    /// <summary>
    /// Replaces the ThemeBrushes merged dictionary with a FRESH instance of the
    /// same source on every theme swap. The brushes wrap colors via
    /// DynamicResource (so they would re-resolve anyway), but recreating the
    /// dictionary guarantees every brush is a brand-new object per theme —
    /// belt-and-braces against any frozen/SharedMode brush corner cases.
    /// </summary>
    private static void ReplaceBrushesDictionary(System.Collections.ObjectModel.Collection<ResourceDictionary> dictionaries)
    {
        for (var i = 0; i < dictionaries.Count; i++)
        {
            if (dictionaries[i].Source?.OriginalString.Contains(BrushesDictionarySource) == true)
            {
                dictionaries[i] = new ResourceDictionary { Source = new Uri(BrushesDictionarySource, UriKind.Relative) };
                return;
            }
        }
    }

    /// <summary>
    /// Removes the custom-theme accent keys that <see cref="ApplyCustomTheme"/>
    /// writes directly into app.Resources. Top-level entries shadow the merged
    /// colors dictionaries indefinitely, so the built-in theme path clears them
    /// to let the freshly swapped palette win again.
    /// </summary>
    private static void ClearOverrideColors(Application app)
    {
        foreach (var key in OverrideColorKeys)
            app.Resources.Remove(key);
    }

    private static void OverrideColor(Application app, string key, Color color)
        => app.Resources[key] = color; // brushes re-resolve via DynamicResource

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
            // the custom dictionary still drives VyralBot's own brushes.
        }
    }

    private void ApplyLogo(Application app, string variant)
    {
        CurrentLogoVariant = variant;
        var logo = ResolveLogoImage(variant);
        if (logo is not null)
            app.Resources["AppLogoSource"] = logo;
    }

    /// <summary>
    /// Resolves the logo image for a variant: vyral-logo-{variant}.png when present,
    /// otherwise the native vyral-logo.png. Results are cached.
    /// </summary>
    public ImageSource? ResolveLogoImage(string variant)
    {
        if (LogoCache.TryGetValue(variant, out var cached)) return cached;

        var candidates = string.Equals(variant, "native", StringComparison.OrdinalIgnoreCase)
            ? new[] { "pack://application:,,,/Resources/Images/vyral-logo.png" }
            : new[]
            {
                $"pack://application:,,,/Resources/Images/vyral-logo-{variant}.png",
                "pack://application:,,,/Resources/Images/vyral-logo.png", // fallback until the variant PNG is added
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
            catch
            {
                // variant missing → try the next candidate
            }
        }

        return null;
    }

    /// <summary>True when vyral-logo-{variant}.png exists as an embedded resource (native always true).</summary>
    public static bool LogoVariantExists(string variant)
    {
        if (string.Equals(variant, "native", StringComparison.OrdinalIgnoreCase)) return true;
        try
        {
            var stream = Application.GetResourceStream(
                new Uri($"pack://application:,,,/Resources/Images/vyral-logo-{variant}.png"));
            return stream is not null;
        }
        catch
        {
            return false;
        }
    }

    private static string Capitalize(string s)
        => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s[1..];
}
