using VyralBot.Core.Services.EventBus;

namespace VyralBot.Integrations.Discord.Events;

/// <summary>
/// Published when a guild member starts boosting a server (Discord.Net
/// <c>GuildMemberUpdated</c> with a null → value <c>PremiumSince</c>
/// transition). Published ONLY on the observed transition — never for
/// reconnect snapshots or later profile updates of an existing booster.
/// </summary>
public record DiscordBoostEvent : EventBase
{
    /// <summary>
    /// Id of the Discord bot instance that observed the boost (the
    /// DiscordBots row id, or "primary" for the setup wizard's singleton).
    /// Mirrors <see cref="DiscordChatMessageEvent.BotId"/>.
    /// </summary>
    public string? BotId { get; init; }

    /// <summary>
    /// Snowflake ID of the guild (server) that was boosted.
    /// </summary>
    public string? GuildId { get; init; }

    /// <summary>
    /// Name of the guild (server) that was boosted.
    /// </summary>
    public string? GuildName { get; init; }

    /// <summary>
    /// Username of the member that boosted the server.
    /// </summary>
    public required string Username { get; init; }

    /// <summary>
    /// Display name (nickname / global display name) of the boosting member
    /// when available; null when Discord did not provide one.
    /// </summary>
    public string? DisplayName { get; init; }

    /// <summary>
    /// Discord snowflake ID of the boosting member when available.
    /// </summary>
    public string? UserId { get; init; }

    /// <summary>
    /// When the boost started (Discord's <c>premium_since</c> timestamp).
    /// Null only when Discord omitted it; the event is still a new boost.
    /// </summary>
    public DateTimeOffset? PremiumSince { get; init; }
}
