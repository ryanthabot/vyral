using VyralBot.Core.Services.EventBus;

namespace VyralBot.Core.Models;

/// <summary>
/// Platform-agnostic chat message event published by every chat integration
/// (Twitch IRC, Kick Pusher, YouTube polling) alongside the platform-specific
/// chat events. Unified pipelines — commands, currency, giveaways, polls and
/// watchtime — subscribe to this single event so they work regardless of origin.
/// </summary>
public record ChatMessageReceivedEvent : EventBase
{
    /// <summary>
    /// Platform the message originated from: "Twitch", "Kick", "YouTube", ...
    /// </summary>
    public required string Platform { get; init; }

    /// <summary>
    /// Channel the message was received in (platform-specific format).
    /// </summary>
    public string Channel { get; init; } = "";

    /// <summary>
    /// Username (login name) of the sender.
    /// </summary>
    public required string Username { get; init; }

    /// <summary>
    /// Platform-assigned user id of the sender when the source platform
    /// provides one (Twitch user id, Kick user id, YouTube channel id,
    /// Discord snowflake). Null for platforms that do not expose it.
    /// </summary>
    public string? UserId { get; init; }

    /// <summary>
    /// Raw message text.
    /// </summary>
    public required string Message { get; init; }

    /// <summary>Sender holds a moderator badge.</summary>
    public bool IsModerator { get; init; }

    /// <summary>Sender holds a subscriber/member badge.</summary>
    public bool IsSubscriber { get; init; }

    /// <summary>Sender holds a VIP badge.</summary>
    public bool IsVip { get; init; }

    /// <summary>Sender is the channel's broadcaster/owner.</summary>
    public bool IsBroadcaster { get; init; }

    /// <summary>
    /// Platform-assigned message id when available. Used to deduplicate
    /// messages that can arrive through more than one transport
    /// (e.g. Kick Pusher plus webhooks).
    /// </summary>
    public string? MessageId { get; init; }
}
