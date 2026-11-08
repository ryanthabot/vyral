namespace VyralBot.Core.Services.EventBus.Events;

/// <summary>
/// Published when a tracked platform transitions between offline and live.
/// The Integrations <c>StreamLiveRegistry</c> implementation of
/// <see cref="VyralBot.Core.Services.StreamStatus.IStreamLiveRegistry"/>
/// publishes this on every ACTUAL transition (repeat status pings with the
/// same value never re-fire it), so consumers — workflow triggers (dispatch
/// keys "streamlive"/"streamoffline"), overlays, notifications — subscribe to
/// this instead of polling <c>IsLive</c> or the registry's .NET event.
/// </summary>
public record StreamLiveChangedEvent : EventBase
{
    /// <summary>Platform identity string that transitioned ("Twitch", "Kick", "YouTube").</summary>
    public required string Platform { get; init; }

    /// <summary>True when the platform just went live; false when it went offline.</summary>
    public required bool IsLive { get; init; }
}
