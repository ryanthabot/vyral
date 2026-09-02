using VyralBot.Core.Services.EventBus;

namespace VyralBot.Integrations.Kick.Events;

/// <summary>
/// Event triggered when a user hosts the Kick channel
/// </summary>
public record KickHostEvent : EventBase
{
    public required string HostUsername { get; init; }
    public required string HostUserId { get; init; }
    public int ViewerCount { get; init; }
    public DateTime HostedAt { get; init; } = DateTime.UtcNow;
}
