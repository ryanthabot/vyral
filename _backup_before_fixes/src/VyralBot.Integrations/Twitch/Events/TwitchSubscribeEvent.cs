using VyralBot.Core.Services.EventBus;

namespace VyralBot.Integrations.Twitch.Events;

/// <summary>
/// Event fired when a user subscribes to the channel.
/// </summary>
public record TwitchSubscribeEvent : EventBase
{
    public required string Username { get; init; }
    public required string UserId { get; init; }
    public required string DisplayName { get; init; }
    public required string Tier { get; init; } // "1000", "2000", "3000"
    public bool IsGift { get; init; }
    public int MonthsTotal { get; init; }
    public int StreakMonths { get; init; }
    public string? Message { get; init; }
}
