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
