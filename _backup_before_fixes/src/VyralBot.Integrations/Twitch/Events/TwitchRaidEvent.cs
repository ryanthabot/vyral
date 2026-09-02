using VyralBot.Core.Services.EventBus;

namespace VyralBot.Integrations.Twitch.Events;

/// <summary>
/// Event published when a channel receives a raid from another broadcaster.
/// </summary>
public record TwitchRaidEvent : EventBase
{
    public required string FromBroadcasterUsername { get; init; }
    public required string FromBroadcasterUserId { get; init; }
    public required string ToBroadcasterUsername { get; init; }
    public required string ToBroadcasterUserId { get; init; }
    public required int Viewers { get; init; }
    public required DateTime RaidedAt { get; init; }
}
