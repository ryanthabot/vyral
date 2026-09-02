using VyralBot.Core.Services.EventBus;

namespace VyralBot.Integrations.Twitch.Events;

/// <summary>
/// Event published when a viewer redeems a channel points reward.
/// </summary>
public record TwitchChannelPointsRedeemEvent : EventBase
{
    public required string Username { get; init; }
    public required string UserId { get; init; }
    public required string RewardId { get; init; }
    public required string RewardTitle { get; init; }
    public required int RewardCost { get; init; }
    public string? UserInput { get; init; }
    public required string Status { get; init; } // "unfulfilled", "fulfilled", "canceled"
    public required DateTime RedeemedAt { get; init; }
}
