using VyralBot.Core.Services.EventBus;

namespace VyralBot.Integrations.YouTube.Events;

/// <summary>
/// Event fired when a viewer sends a Super Chat ("superChatEvent") or Super
/// Sticker ("superStickerEvent") in YouTube live chat — YouTube's cheer
/// equivalent. Published alongside the synthetic chat text (💎 prefix, same
/// pattern as <see cref="YouTubeMembershipEvent"/>) so alert, statistics,
/// currency and workflow pipelines get the structured amount without parsing
/// display strings. Consumers should mirror the Twitch cheer pipeline
/// (<c>TwitchCheerEvent</c> handler).
/// </summary>
public record YouTubeSuperChatEvent : EventBase
{
    public required string Username { get; init; }
    public required string UserId { get; init; }

    /// <summary>
    /// Purchase amount in millionths of <see cref="Currency"/> (YouTube's
    /// "amountMicros" — e.g. 5_000_000 = 5.00 USD). One cent = 10_000 micros,
    /// which is the bits-equivalent used for currency bonuses (1 bit ≈ 1 cent).
    /// </summary>
    public long AmountMicros { get; init; }

    /// <summary>User-presentable amount exactly as YouTube rendered it (e.g. "$5.00").</summary>
    public required string AmountDisplayString { get; init; }

    /// <summary>ISO currency code for <see cref="AmountMicros"/> (e.g. "USD").</summary>
    public required string Currency { get; init; }

    /// <summary>The viewer's attached comment (Super Chats only; null for Super Stickers).</summary>
    public string? Message { get; init; }

    /// <summary>True for Super Sticker purchases (sticker art instead of a comment).</summary>
    public bool IsSuperSticker { get; init; }

    /// <summary>YouTube's sticker display text when <see cref="IsSuperSticker"/> is true.</summary>
    public string? StickerDisplayText { get; init; }

    /// <summary>When YouTube published the event (null when the timestamp was unparseable).</summary>
    public DateTime? CheeredAt { get; init; }
}
