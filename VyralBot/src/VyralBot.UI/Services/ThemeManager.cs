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

    private static readonly Dictionary<string, ImageSource> LogoCache = new();

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

        ColorShadeGenerator.TryParseHex(option.AccentHex, out var accent);
        ApplyMaterialDesignTheme(app, option.IsLight, accent);

        CurrentThemeId = option.Id;
        ApplyLogo(app, option.LogoVariant);

        if (persist)
        {
            Preferences.ThemeId = CurrentThemeId;
            _preferencesService.Save(Preferences);
        }

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

        ThemeChanged?.Invoke();
    }

    // ---- internals ----

    private static void SwapColorsDictionary(Application app, string source)
    {
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

        var newDict = new ResourceDictionary { Source = new Uri(source, UriKind.Relative) };
        if (index >= 0)
            dictionaries[index] = newDict;
        else
            dictionaries.Add(newDict);
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
