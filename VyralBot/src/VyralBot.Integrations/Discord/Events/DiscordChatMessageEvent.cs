using VyralBot.Core.Services.EventBus;

namespace VyralBot.Integrations.Discord.Events;

/// <summary>
/// Discord-specific chat message event published for every user message the
/// bot receives (guild text channels and direct messages). Published alongside
/// the platform-agnostic <see cref="VyralBot.Core.Models.ChatMessageReceivedEvent"/>
/// so unified pipelines (commands, currency, giveaways, polls, watchtime) work
/// for Discord as well.
/// </summary>
public record DiscordChatMessageEvent : EventBase
{
    /// <summary>
    /// Id of the Discord bot instance that received the message (the
    /// DiscordBots row id, or "primary" for the setup wizard's singleton).
    /// Null only for events published by instances built before tagging
    /// existed — treat null as "origin unknown" and fall back to the
    /// primary routing target.
    /// </summary>
    public string? BotId { get; init; }

    /// <summary>
    /// Name of the guild (server) the message was posted in.
    /// Null for direct messages.
    /// </summary>
    public string? GuildName { get; init; }

    /// <summary>
    /// Channel name the message was posted in ("DirectMessage" fallback for DMs).
    /// </summary>
    public required string Channel { get; init; }

    /// <summary>
    /// Snowflake ID of the channel the message was posted in.
    /// </summary>
    public string? ChannelId { get; init; }

    /// <summary>
    /// Username of the sender.
    /// </summary>
    public required string Username { get; init; }

    /// <summary>
    /// Discord snowflake ID of the sender when available.
    /// </summary>
    public string? SenderUserId { get; init; }

    /// <summary>
    /// Raw message text.
    /// </summary>
    public required string Message { get; init; }

    /// <summary>Sender has the ManageMessages guild permission.</summary>
    public bool IsModerator { get; init; }

    /// <summary>Sender is the guild owner or holds the Administrator permission.</summary>
    public bool IsBroadcaster { get; init; }

    /// <summary>
    /// Discord message snowflake ID ("discord:{id}") when available.
    /// </summary>
    public string? MessageId { get; init; }
}
