using VyralBot.Core.Services.EventBus;

namespace VyralBot.Integrations.Kick.Events;

/// <summary>
/// Event triggered when a user gifts Kicks (Kick's virtual currency) to the streamer
/// </summary>
public record KickKicksGiftedEvent : EventBase
{
    public required string SenderUsername { get; init; }
    public required string SenderUserId { get; init; }
    public required int Amount { get; init; }
    public string? Message { get; init; }
    public DateTime GiftedAt { get; init; } = DateTime.UtcNow;
}
