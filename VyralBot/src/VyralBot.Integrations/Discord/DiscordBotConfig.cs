namespace VyralBot.Integrations.Discord;

/// <summary>
/// Persisted configuration for one Discord bot managed by
/// <see cref="IDiscordBotManager"/>. Mirrors a row in the DiscordBots
/// database table (Id, Name, Token, Status, SelectedChannelId).
/// </summary>
/// <param name="Id">Manager-assigned stable ID (GUID "N" string). Primary key.</param>
/// <param name="Name">Display name shown in the bots list/dashboard.</param>
/// <param name="Token">Raw bot token (stored as-is, matching the legacy Discord Platform row).</param>
/// <param name="Status">Last known status: "Connected", "Disconnected" or "Error". Null until first start.</param>
/// <param name="SelectedChannelId">Per-bot snowflake channel applied after connect for sends.</param>
public record DiscordBotConfig(
    string Id,
    string Name,
    string? Token = null,
    string? Status = null,
    string? SelectedChannelId = null);
