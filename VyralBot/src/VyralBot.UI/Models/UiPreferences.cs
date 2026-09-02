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
