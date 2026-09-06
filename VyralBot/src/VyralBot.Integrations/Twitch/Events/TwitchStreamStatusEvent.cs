using VyralBot.Core.Services.EventBus;

namespace VyralBot.Integrations.Twitch.Events;

/// <summary>
/// Event fired when the Twitch channel goes live or offline
/// (EventSub <c>stream.online</c> / <c>stream.offline</c>).
/// </summary>
public record TwitchStreamStatusEvent : EventBase
{
    public required bool IsLive { get; init; }

    /// <summary>UTC time the stream went live; null when going offline.</summary>
    public DateTime? StartedAtUtc { get; init; }
}
