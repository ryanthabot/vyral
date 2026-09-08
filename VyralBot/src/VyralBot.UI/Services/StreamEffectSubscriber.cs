using System.Text.Json;
using Microsoft.Extensions.Logging;
using VyralBot.Core.Services.EventBus;
using VyralBot.Core.Services.StreamEffects;
using VyralBot.Integrations.Lights;
using VyralBot.Integrations.StreamDeck;

namespace VyralBot.UI.Services;

/// <summary>Bridges IStreamDeckStore to the StreamEffects config KV.</summary>
public sealed class DelegateStreamDeckStore : IStreamDeckStore
{
    private readonly Func<Task<string>> _load;
    private readonly Func<string, Task> _save;

    public DelegateStreamDeckStore(Func<Task<string>> load, Func<string, Task> save)
    {
        _load = load ?? throw new ArgumentNullException(nameof(load));
        _save = save ?? throw new ArgumentNullException(nameof(save));
    }

    public Task<string> LoadAsync() => _load();

    public Task SaveAsync(string json) => _save(json);
}

/// <summary>
/// Executes StreamEffectEvents on the configured lights / Stream Deck. The
/// StreamEffectCommandRouter publishes tier-gated moderator commands
/// ("!flashlights", "!lightson", "!lightsoff", "!streamdeck") as events; this
/// subscriber turns them into ILightsClient / IStreamDeckService calls.
///
/// Started once at app startup and never unsubscribed until exit — a
/// subscriber must never break the event bus, so OnEffectAsync catches
/// everything.
/// </summary>
public sealed class StreamEffectSubscriber
{
    private readonly ILogger<StreamEffectSubscriber> _logger;
    private readonly IEventBus _eventBus;
    private readonly ILightsClient _lights;
    private readonly IStreamDeckService _deck;
    private readonly StreamEffectsConfigService _config;
    private Guid _subscription;

    public StreamEffectSubscriber(
        IEventBus eventBus,
        ILightsClient lights,
        IStreamDeckService deck,
        StreamEffectsConfigService config,
        ILogger<StreamEffectSubscriber> logger)
    {
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        _lights = lights ?? throw new ArgumentNullException(nameof(lights));
        _deck = deck ?? throw new ArgumentNullException(nameof(deck));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Subscribes to StreamEffectEvent. Idempotent — a second call is a no-op.</summary>
    public async Task StartAsync()
    {
        if (_subscription != Guid.Empty)
        {
            return;
        }

        _subscription = await _eventBus.SubscribeAsync<StreamEffectEvent>(OnEffectAsync).ConfigureAwait(false);
        _logger.LogInformation("StreamEffectSubscriber started (lights + Stream Deck effects)");
    }

    /// <summary>Unsubscribes from StreamEffectEvent. Safe to call twice.</summary>
    public Task StopAsync()
    {
        if (_subscription == Guid.Empty)
        {
            return Task.CompletedTask;
        }

        try
        {
            _eventBus.Unsubscribe(_subscription);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to unsubscribe stream effect subscriber {SubscriptionId}", _subscription);
        }

        _subscription = Guid.Empty;
        _logger.LogInformation("StreamEffectSubscriber stopped");
        return Task.CompletedTask;
    }

    private async Task OnEffectAsync(StreamEffectEvent evt)
    {
        try
        {
            var snapshot = await _config.GetSnapshotAsync().ConfigureAwait(false);

            // Payload is effect-specific JSON; every field is optional and the
            // whole body may be invalid JSON (tolerated — defaults apply).
            string? color = null;
            int? times = null;
            var state = "on";
            var action = string.Empty;
            try
            {
                using var doc = JsonDocument.Parse(evt.Payload ?? string.Empty);
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    var root = doc.RootElement;
                    if (root.TryGetProperty("color", out var colorEl) && colorEl.ValueKind == JsonValueKind.String)
                    {
                        color = colorEl.GetString();
                    }

                    if (root.TryGetProperty("times", out var timesEl)
                        && timesEl.ValueKind == JsonValueKind.Number
                        && timesEl.TryGetInt32(out var parsedTimes))
                    {
                        times = parsedTimes;
                    }

                    if (root.TryGetProperty("state", out var stateEl) && stateEl.ValueKind == JsonValueKind.String)
                    {
                        state = stateEl.GetString() ?? "on";
                    }

                    if (root.TryGetProperty("action", out var actionEl) && actionEl.ValueKind == JsonValueKind.String)
                    {
                        action = actionEl.GetString() ?? string.Empty;
                    }
                }
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Stream effect payload was not valid JSON ('{EffectType}' from {By}): {Payload}",
                    evt.EffectType, evt.RequestedBy, evt.Payload);
            }

            string result;
            switch ((evt.EffectType ?? string.Empty).Trim())
            {
                case "LightFlash":
                    result = await _lights.FlashAsync(color, times ?? snapshot.FlashTimes, snapshot.FlashIntervalMs).ConfigureAwait(false);
                    break;

                case "LightScene":
                    result = await _lights.SetPowerAsync(!string.Equals(state.Trim(), "off", StringComparison.OrdinalIgnoreCase)).ConfigureAwait(false);
                    break;

                case "StreamDeckAction":
                    if (string.IsNullOrWhiteSpace(action))
                    {
                        _logger.LogWarning("Stream Deck effect from {By} in {Channel} skipped: payload has no action", evt.RequestedBy, evt.ChannelId);
                        return;
                    }

                    result = await _deck.TriggerAsync(action.Trim()).ConfigureAwait(false);
                    break;

                default:
                    _logger.LogWarning("Unknown stream effect type '{EffectType}' requested by {By} in {Channel}",
                        evt.EffectType, evt.RequestedBy, evt.ChannelId);
                    return;
            }

            _logger.LogInformation("Stream effect {EffectType} requested by {By} in {Channel}: {Result}",
                evt.EffectType, evt.RequestedBy, evt.ChannelId, result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Stream effect '{EffectType}' requested by {By} in {Channel} failed",
                evt.EffectType, evt.RequestedBy, evt.ChannelId);
        }
    }
}
