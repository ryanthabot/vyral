using Microsoft.Extensions.Logging;
using VyralBot.Core.Services.EventBus;
using VyralBot.Core.Services.StreamStatus;
using VyralBot.Integrations.Kick.Events;
using VyralBot.Integrations.Twitch.Events;
using VyralBot.Integrations.YouTube.Events;

namespace VyralBot.Integrations.StreamStatus;

/// <summary>
/// Singleton registry of per-platform live status, fed by the platform stream-status
/// events on the EventBus. Lives in VyralBot.Integrations (not Core) because only this
/// project can reference the Twitch/Kick/YouTube event types; Core owns the
/// <see cref="IStreamLiveRegistry"/> contract that gates/UI consume.
///
/// Wiring (DI ctor):
/// - TwitchStreamStatusEvent.IsLive        → SetLive("Twitch", IsLive)
/// - KickStreamStatusEvent.IsLive          → SetLive("Kick", IsLive)
/// - YouTubeLiveChatStateEvent.State       → SetLive("YouTube", State == "Listening")
///   ("Listening" is the only YouTube state polling an active live chat; all other
///   states — "Connecting", "WaitingForBroadcast", "ReauthRequired" — are not live.)
///
/// Discord is never registered: IsLive("Discord") is always false; the watchtime gate
/// excludes Discord separately.
/// </summary>
public class StreamLiveRegistry : IStreamLiveRegistry
{
    private readonly object _gate = new();
    private readonly Dictionary<string, bool> _liveState = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Task<Guid>> _subscriptions = [];
    private readonly IEventBus? _eventBus;
    private readonly ILogger<StreamLiveRegistry>? _logger;

    public event EventHandler<string>? StreamLiveChanged;

    /// <summary>
    /// Parameterless ctor for unit tests ONLY: skips all EventBus subscriptions so
    /// tests can exercise SetLive/IsLive without constructing an IEventBus fake or
    /// referencing platform events. Production code must use the DI ctor.
    /// </summary>
    public StreamLiveRegistry()
    {
    }

    /// <summary>DI ctor: subscribes the platform stream-status events on the bus.</summary>
    public StreamLiveRegistry(IEventBus eventBus, ILogger<StreamLiveRegistry>? logger = null)
    {
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        _logger = logger;

        _subscriptions.Add(SubscribeSafe<TwitchStreamStatusEvent>("Twitch", e => e.IsLive));
        _subscriptions.Add(SubscribeSafe<KickStreamStatusEvent>("Kick", e => e.IsLive));
        _subscriptions.Add(SubscribeSafe<YouTubeLiveChatStateEvent>("YouTube", e => e.State == "Listening"));
    }

    /// <inheritdoc />
    public bool IsLive(string platform)
    {
        if (string.IsNullOrWhiteSpace(platform))
        {
            return false;
        }

        lock (_gate)
        {
            return _liveState.TryGetValue(platform, out var isLive) && isLive;
        }
    }

    /// <inheritdoc />
    public void SetLive(string platform, bool isLive)
    {
        if (string.IsNullOrWhiteSpace(platform))
        {
            return;
        }

        EventHandler<string>? handlers;
        lock (_gate)
        {
            // Platforms start NOT live, so an unseen platform reports as a
            // no-change when first set to false (e.g. YouTube "Connecting").
            _liveState.TryGetValue(platform, out var current);
            if (current == isLive)
            {
                return; // no transition — do not spam subscribers
            }

            _liveState[platform] = isLive;
            handlers = StreamLiveChanged;
        }

        // Fire outside the lock; a subscriber throwing must not break the registry.
        try
        {
            handlers?.Invoke(this, platform);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "StreamLiveChanged subscriber threw for {Platform}", platform);
        }
    }

    /// <summary>
    /// Unsubscribes every bus subscription taken in the DI ctor.
    /// Safe to call on the parameterless (test) instance or twice.
    /// </summary>
    public void UnsubscribeAll()
    {
        if (_eventBus is null)
        {
            return;
        }

        foreach (var subscription in _subscriptions)
        {
            try
            {
                // EventBus.SubscribeAsync completes synchronously (Task.FromResult),
                // so the task is always completed here in practice.
                if (subscription.IsCompletedSuccessfully)
                {
                    _eventBus.Unsubscribe(subscription.Result);
                }
                else
                {
                    _logger?.LogWarning("StreamLiveRegistry subscription not completed; cannot unsubscribe.");
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to unsubscribe StreamLiveRegistry subscription");
            }
        }

        _subscriptions.Clear();
    }

    private Task<Guid> SubscribeSafe<TEvent>(string platform, Func<TEvent, bool> isLive)
        where TEvent : IEvent
    {
        return _eventBus!.SubscribeAsync<TEvent>(async e =>
        {
            try
            {
                SetLive(platform, isLive(e));
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to apply {Platform} stream status", platform);
            }
            await Task.CompletedTask;
        });
    }
}
