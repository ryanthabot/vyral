namespace VyralBot.Integrations.Discord;

/// <summary>
/// Persisted configuration for one Discord bot managed by
/// <see cref="IDiscordBotManager"/>. Mirrors a row in the DiscordBots
/// database table (Id, Name, Token, Status, SelectedChannelId plus the
/// auto-detected identity, single-assignment bot roles and the per-bot
/// AutoStart launch flag).
/// </summary>
/// <param name="Id">Manager-assigned stable ID (GUID "N" string). Primary key.</param>
/// <param name="Name">Display name shown in the bots list/dashboard.</param>
/// <param name="Token">Raw bot token (stored as-is, matching the legacy Discord Platform row).</param>
/// <param name="Status">Last known status: "Connected", "Disconnected" or "Error". Null until first start.</param>
/// <param name="SelectedChannelId">Per-bot snowflake channel applied after connect for sends.</param>
/// <param name="ApplicationId">Discord application/user snowflake ID, auto-detected from the gateway at connect time.</param>
/// <param name="BotUsername">Discord username, auto-detected from the gateway at connect time.</param>
/// <param name="AvatarUrl">Avatar URL (PNG, 128px), auto-detected from the gateway at connect time.</param>
/// <param name="RoleLinking">This bot handles account-link DMs (single-assignment role).</param>
/// <param name="RoleGiveaway">This bot sends giveaway announcements (single-assignment role).</param>
/// <param name="ModChannelId">Per-bot snowflake channel used for mod-action announcements.</param>
/// <param name="ModChannelEnabled">Whether this bot routes mod actions to its mod channel (defaults to on).</param>
/// <param name="AssignedChannelIds">Comma-separated snowflake channel IDs this bot listens in for mod commands.</param>
public record DiscordBotConfig(
    string Id,
    string Name,
    string? Token = null,
    string? Status = null,
    string? SelectedChannelId = null,
    string? ApplicationId = null,
    string? BotUsername = null,
    string? AvatarUrl = null,
    bool RoleLinking = false,
    bool RoleGiveaway = false,
    string? ModChannelId = null,
    bool ModChannelEnabled = true,
    string? AssignedChannelIds = null)
{
    /// <summary>
    /// Start this bot automatically at app launch. The PRIMARY bot does not need
    /// this flag: when the Platform row's DiscordChatAutoStart is on, the startup
    /// path alone starts the bot matching the Platform row token (or the first
    /// saved bot). AutoStart is opt-in per extra bot; defaults to false and is
    /// never seeded to true on migration. Carried by the manager's row↔config
    /// conversions (DiscordBots.AutoStart column).
    /// </summary>
    public bool AutoStart { get; set; } = false;
}
