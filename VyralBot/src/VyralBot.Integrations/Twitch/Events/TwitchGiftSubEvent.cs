using VyralBot.Core.Services.EventBus;

namespace VyralBot.Integrations.Twitch.Events;

/// <summary>
/// Event published when a user gifts subscriptions to the community.
/// </summary>
public record TwitchGiftSubEvent : EventBase
{
    public required string GifterUsername { get; init; }
    public required string GifterUserId { get; init; }
    public required string Tier { get; init; } // "1000", "2000", "3000"
    public required int Total { get; init; }
    public required int CumulativeTotal { get; init; }
    public required bool IsAnonymous { get; init; }
    public required DateTime GiftedAt { get; init; }
}
