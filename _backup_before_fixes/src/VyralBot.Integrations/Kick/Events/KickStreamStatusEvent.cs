using VyralBot.Core.Services.EventBus;

namespace VyralBot.Integrations.Kick.Events;

/// <summary>
/// Event triggered when stream status changes (goes live or offline)
/// </summary>
public record KickStreamStatusEvent : EventBase
{
    public required bool IsLive { get; init; }
    public required string Title { get; init; }
    public string? Category { get; init; }
    public int? ViewerCount { get; init; }
    public DateTime StatusChangedAt { get; init; } = DateTime.UtcNow;
}
