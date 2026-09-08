using VyralBot.Core.Services.EventBus;

namespace VyralBot.Core.Services.StreamEffects;

/// <summary>
/// Published on the EventBus when a tier-gated moderator command triggers a
/// stream effect (smart lights or a Stream Deck action).
/// </summary>
public record StreamEffectEvent : EventBase
{
    /// <summary>"LightFlash" | "LightScene" | "StreamDeckAction".</summary>
    public required string EffectType { get; init; }

    /// <summary>Effect-specific JSON: {"color":"#FF00AA","times":5} | {"state":"on"} | {"action":"hype"}.</summary>
    public required string Payload { get; init; }

    /// <summary>Display name of the moderator who ran the command.</summary>
    public required string RequestedBy { get; init; }

    /// <summary>Discord channel the command was run in.</summary>
    public required string ChannelId { get; init; }
}
