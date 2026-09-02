using VyralBot.Core.Services.EventBus;

namespace VyralBot.Integrations.YouTube.Events;

/// <summary>
/// Event fired when a chat message is received from YouTube Live Chat.
/// </summary>
public record YouTubeChatMessageEvent : EventBase
{
    public required string Channel { get; init; }
    public required string Username { get; init; }
    public required string UserId { get; init; }
    public required string Message { get; init; }
    public required string DisplayName { get; init; }
    public string? Color { get; init; }
    public string? ProfileImageUrl { get; init; }
    public bool IsModerator { get; init; }
    public bool IsOwner { get; init; }
    public bool IsMember { get; init; }
    public string? MessageId { get; init; }
    public DateTime? PublishedAt { get; init; }
    public string? LiveChatId { get; init; }
}
