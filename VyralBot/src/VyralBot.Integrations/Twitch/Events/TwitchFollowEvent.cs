using VyralBot.Core.Services.EventBus;

namespace VyralBot.Integrations.Twitch.Events;

/// <summary>
/// Event fired when a user follows the channel.
/// </summary>
public record TwitchFollowEvent : EventBase
{
    public required string Username { get; init; }
    public required string UserId { get; init; }
    public required string DisplayName { get; init; }
}
