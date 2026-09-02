using VyralBot.Core.Services.EventBus;

namespace VyralBot.Integrations.Kick.Events;

/// <summary>
/// Event triggered when a user redeems a channel reward
/// </summary>
public record KickRewardRedeemedEvent : EventBase
{
    public required string Username { get; init; }
    public required string UserId { get; init; }
    public required string RewardName { get; init; }
    public required int Cost { get; init; }
    public string? UserInput { get; init; }
    public DateTime RedeemedAt { get; init; } = DateTime.UtcNow;
}
