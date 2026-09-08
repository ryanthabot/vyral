using System.Text.Json;

namespace VyralBot.Core.Services.StreamEffects;

/// <summary>
/// Builds the effect type + JSON payload for the stream-effect command verbs
/// (flashlights, lightson, lightsoff, streamdeck). Shared by
/// <see cref="StreamEffectCommandRouter"/> and the ModCommandEngine so both
/// entry points produce identical events.
/// </summary>
public static class StreamEffectPayloads
{
    /// <summary>The stream-effect command verbs, matched case-insensitively.</summary>
    public static readonly string[] Verbs = { "flashlights", "lightson", "lightsoff", "streamdeck" };

    /// <summary>
    /// Builds the effect for <paramref name="verb"/>; tokens[0] is the verb and
    /// tokens[1..] are its arguments (the router's message tokenization shape).
    /// </summary>
    public static (string EffectType, string Payload) Build(string verb, string[] tokens)
    {
        switch (verb)
        {
            case "flashlights":
                return ("LightFlash", JsonSerializer.Serialize(new
                {
                    color = tokens.Length > 1 && tokens[1].StartsWith('#') ? tokens[1] : null,
                    times = tokens.Length > 2 && int.TryParse(tokens[2], out var times) && times is >= 1 and <= 20
                        ? times
                        : (int?)null
                }));
            case "lightson":
            case "lightsoff":
                return ("LightScene", JsonSerializer.Serialize(new { state = verb == "lightson" ? "on" : "off" }));
            default: // "streamdeck" (the only remaining verb in Verbs)
                return ("StreamDeckAction", JsonSerializer.Serialize(new { action = string.Join(' ', tokens.Skip(1)) }));
        }
    }
}
