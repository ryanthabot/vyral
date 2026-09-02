namespace VyralBot.UI.Models;

/// <summary>
/// Catalog of the built-in themes and the custom theme slot.
/// Accent/logo values here mirror the dictionaries under Themes/Colors/.
/// </summary>
public static class ThemeCatalog
{
    public const string NativeId = "native";
    public const string CustomId = "custom";

    /// <summary>Logo PNG variants the app looks for: vyral-logo-{variant}.png ("native" = vyral-logo.png).</summary>
    public static IReadOnlyList<string> ValidLogoVariants { get; } =
        new[] { "native", "red", "blue", "green", "white", "black" };

    public static IReadOnlyList<ThemeOption> BuiltInThemes { get; } = new[]
    {
        new ThemeOption("native",  "Native Teal",     IsLight: false, "#14B8A6", "native", "#0F172A", IsBuiltIn: true),
        new ThemeOption("light",   "Light",           IsLight: true,  "#0D9488", "native", "#F1F5F9", IsBuiltIn: true),
        new ThemeOption("dark",    "Dark",            IsLight: false, "#14B8A6", "white",  "#0B0F14", IsBuiltIn: true),
        new ThemeOption("crimson", "Crimson Forge",   IsLight: false, "#EF4444", "red",    "#1A0E10", IsBuiltIn: true),
        new ThemeOption("azure",   "Azure Depths",    IsLight: false, "#3B82F6", "blue",   "#0B1220", IsBuiltIn: true),
        new ThemeOption("emerald", "Emerald Grove",   IsLight: false, "#10B981", "green",  "#0A1410", IsBuiltIn: true),
        new ThemeOption("frost",   "Arctic Frost",    IsLight: true,  "#0284C7", "black",  "#F8FAFC", IsBuiltIn: true),
        new ThemeOption("carbon",  "Carbon Graphite", IsLight: false, "#94A3B8", "white",  "#09090B", IsBuiltIn: true),
        new ThemeOption("ember",   "Sunset Ember",    IsLight: false, "#F97316", "native", "#170E08", IsBuiltIn: true),
    };

    public static ThemeOption CustomOption { get; } =
        new(CustomId, "Custom", IsLight: false, "#14B8A6", "native", "#0F172A", IsBuiltIn: false);

    public static ThemeOption? Find(string id) =>
        BuiltInThemes.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase))
        ?? (string.Equals(id, CustomId, StringComparison.OrdinalIgnoreCase) ? CustomOption : null);
}
