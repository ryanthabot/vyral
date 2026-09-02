using VyralBot.Core.Services.EventBus;

namespace VyralBot.Integrations.Kick.Events;

public record KickChatMessageEvent : EventBase
{
    public required string Channel { get; init; }
    public required string Username { get; init; }
    public required string UserId { get; init; }
    public required string Message { get; init; }
    public required string DisplayName { get; init; }
    public string? Color { get; init; }
    public bool IsSubscriber { get; init; }
    public bool IsModerator { get; init; }
    public bool IsOwner { get; init; }
    public bool IsVerified { get; init; }
    public int? SubscriberMonths { get; init; }
    public Dictionary<string, string> Badges { get; init; } = new();
    
    // Extended fields from Kick API
    public string? MessageId { get; init; }
    public string? ProfilePicture { get; init; }
    public string? ChannelSlug { get; init; }
    public DateTime? CreatedAt { get; init; }
    public List<KickEmote> Emotes { get; init; } = new();
    public KickReplyInfo? ReplyTo { get; init; }
}

public record KickEmote
{
    public required string EmoteId { get; init; }
    public List<EmotePosition> Positions { get; init; } = new();
}

public record EmotePosition
{
    public int Start { get; init; }
    public int End { get; init; }
}

public record KickReplyInfo
{
    public required string MessageId { get; init; }
    public required string Content { get; init; }
    public required string Username { get; init; }
    public required string UserId { get; init; }
}
