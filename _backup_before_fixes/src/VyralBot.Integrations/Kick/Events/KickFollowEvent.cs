using VyralBot.Core.Services.EventBus;

namespace VyralBot.Integrations.Kick.Events;

/// <summary>
/// Event triggered when a user follows the Kick channel
/// </summary>
public record KickFollowEvent : EventBase
{
    public required string Username { get; init; }
    public required string UserId { get; init; }
    public required int FollowerCount { get; init; }
    public DateTime FollowedAt { get; init; } = DateTime.UtcNow;
}
