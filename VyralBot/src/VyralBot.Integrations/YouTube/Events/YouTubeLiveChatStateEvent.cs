using VyralBot.Core.Services.EventBus;

namespace VyralBot.Integrations.YouTube.Events;

/// <summary>
/// Fired when the YouTube live-chat state changes so the UI can explain WHY
/// messages may (or may not) be flowing: "Connecting", "WaitingForBroadcast"
/// (connected but the channel isn't live), "Listening" (polling an active live
/// chat) or "ReauthRequired" (Google sign-in needed).
/// </summary>
public record YouTubeLiveChatStateEvent : EventBase
{
    public required string State { get; init; }
    public required string Message { get; init; }
}
