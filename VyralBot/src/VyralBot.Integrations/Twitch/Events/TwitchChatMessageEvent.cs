using VyralBot.Core.Services.EventBus;

namespace VyralBot.Integrations.Twitch.Events;

/// <summary>
/// Event fired when a chat message is received from Twitch.
/// </summary>
public record TwitchChatMessageEvent : EventBase
{
    public required string Channel { get; init; }
    public required string Username { get; init; }
    public required string UserId { get; init; }
    public required string Message { get; init; }
    public required string DisplayName { get; init; }
    public string? Color { get; init; }
    public bool IsSubscriber { get; init; }
    public bool IsModerator { get; init; }
    public bool IsBroadcaster { get; init; }
    public bool IsVip { get; init; }
    public int SubscriberMonths { get; init; }
    public Dictionary<string, string> Badges { get; init; } = new();
    public List<string> Emotes { get; init; } = new();
}
