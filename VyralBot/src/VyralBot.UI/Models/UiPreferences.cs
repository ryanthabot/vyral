namespace VyralBot.UI.Models;

/// <summary>App-level UI preferences persisted to appdata/ui-settings.json.</summary>
public sealed class UiPreferences
{
    public string ThemeId { get; set; } = ThemeCatalog.NativeId;
    public CustomThemeSettings Custom { get; set; } = new();

    /// <summary>
    /// Brightness offset applied to the neutral colors of DARK base palettes:
    /// -100 (much darker) .. +100 (much lighter), 0 = native palette.
    /// </summary>
    public int DarkShadeLevel { get; set; } = 0;

    /// <summary>
    /// Brightness offset applied to the neutral colors of LIGHT base palettes:
    /// -100 (much darker) .. +100 (much lighter), 0 = native palette.
    /// </summary>
    public int LightShadeLevel { get; set; } = 0;

    public static UiPreferences Defaults { get; } = new();
}

public sealed class CustomThemeSettings
{
    /// <summary>"Light" or "Dark" — the base palette custom accents are applied over.</summary>
    public string BaseTheme { get; set; } = "Dark";

    public string PrimaryHex { get; set; } = "#14B8A6";

    public string LogoVariant { get; set; } = "native";
}
