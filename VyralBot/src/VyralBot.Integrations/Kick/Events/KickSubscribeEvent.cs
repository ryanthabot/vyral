using VyralBot.Core.Services.EventBus;

namespace VyralBot.Integrations.Kick.Events;

/// <summary>
/// Event triggered when a user subscribes to the Kick channel
/// </summary>
public record KickSubscribeEvent : EventBase
{
    public required string Username { get; init; }
    public required string UserId { get; init; }
    public required int MonthsSubscribed { get; init; }
    public string? Message { get; init; }
    public DateTime SubscribedAt { get; init; } = DateTime.UtcNow;
}
