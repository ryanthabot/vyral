using VyralBot.Core.Services.EventBus;

namespace VyralBot.Integrations.Kick.Events;

/// <summary>
/// Event triggered when a user gifts subscriptions to other users
/// </summary>
public record KickGiftSubEvent : EventBase
{
    public required string GifterUsername { get; init; }
    public required string GifterUserId { get; init; }
    public required int Count { get; init; }
    public List<string> RecipientUsernames { get; init; } = new();
    public DateTime GiftedAt { get; init; } = DateTime.UtcNow;
}
