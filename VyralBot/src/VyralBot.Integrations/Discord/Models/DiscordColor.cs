namespace VyralBot.Integrations.Discord;

/// <summary>
/// Plain RGB color for Discord resources (roles), kept free of Discord.Net
/// types so callers of <see cref="IDiscordService"/> (UI, workflows) never
/// need a Discord.Net reference. Converted into Discord's color types by the
/// service implementation.
/// </summary>
/// <param name="R">Red component (0-255).</param>
/// <param name="G">Green component (0-255).</param>
/// <param name="B">Blue component (0-255).</param>
public readonly record struct DiscordColor(byte R, byte G, byte B);
