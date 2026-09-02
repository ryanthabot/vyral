using VyralBot.Core.Services.EventBus;

namespace VyralBot.Integrations.Twitch.Events;

/// <summary>
/// Event published when a user resubscribes (resub) with a message.
/// </summary>
public record TwitchResubEvent : EventBase
{
    public required string Username { get; init; }
    public required string UserId { get; init; }
    public required string Tier { get; init; } // "1000", "2000", "3000", or "Prime"
    public required int CumulativeMonths { get; init; }
    public required int StreakMonths { get; init; }
    public required int DurationMonths { get; init; }
    public string? Message { get; init; }
    public required DateTime ResubscribedAt { get; init; }
}
