using VyralBot.Core.Services.EventBus;

namespace VyralBot.Integrations.Discord.Events;

/// <summary>
/// Published when a member joins a guild the bot is in (Discord.Net
/// <c>UserJoined</c> / gateway GUILD_MEMBER_ADD). Celebrate-worthy for
/// streamers (alerts) and trackable (stats, workflow event triggers), the
/// Discord counterpart of the Twitch/Kick follow events.
/// </summary>
public record DiscordMemberJoinedEvent : EventBase
{
    /// <summary>
    /// Id of the Discord bot instance that observed the join (the
    /// DiscordBots row id, or "primary" for the setup wizard's singleton).
    /// Mirrors <see cref="DiscordChatMessageEvent.BotId"/>.
    /// </summary>
    public string? BotId { get; init; }

    /// <summary>
    /// Snowflake ID of the guild (server) the member joined.
    /// </summary>
    public string? GuildId { get; init; }

    /// <summary>
    /// Name of the guild (server) the member joined.
    /// </summary>
    public string? GuildName { get; init; }

    /// <summary>
    /// Username of the member that joined.
    /// </summary>
    public required string Username { get; init; }

    /// <summary>
    /// Display name (nickname / global display name) of the member when
    /// available; null when Discord did not provide one.
    /// </summary>
    public string? DisplayName { get; init; }

    /// <summary>
    /// Discord snowflake ID of the member that joined when available.
    /// </summary>
    public string? UserId { get; init; }
}
