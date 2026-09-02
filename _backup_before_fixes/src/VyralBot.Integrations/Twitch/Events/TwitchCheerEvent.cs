using VyralBot.Core.Services.EventBus;

namespace VyralBot.Integrations.Twitch.Events;

/// <summary>
/// Event published when a user cheers bits in chat.
/// </summary>
public record TwitchCheerEvent : EventBase
{
    public required string Username { get; init; }
    public required string UserId { get; init; }
    public required int Bits { get; init; }
    public required bool IsAnonymous { get; init; }
    public string? Message { get; init; }
    public required DateTime CheeredAt { get; init; }
}
