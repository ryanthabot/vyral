using VyralBot.Core.Services.EventBus;

namespace VyralBot.Integrations.Twitch.Events;

/// <summary>
/// Event fired when a user redeems channel points.
/// </summary>
public record TwitchChannelPointsEvent : EventBase
{
    public required string RewardId { get; init; }
    public required string RewardTitle { get; init; }
    public required int RewardCost { get; init; }
    public required string Username { get; init; }
    public required string UserId { get; init; }
    public required string DisplayName { get; init; }
    public string? UserInput { get; init; }
    public string? Status { get; init; } // "unfulfilled", "fulfilled", "canceled"
}
