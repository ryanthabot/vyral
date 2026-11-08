using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using VyralBot.Core.Services.EventBus;
using VyralBot.Core.Services.StreamStatus;
using VyralBot.Integrations.Discord;
using VyralBot.Integrations.Kick;
using VyralBot.Integrations.Kick.Events;
using VyralBot.Integrations.Obs.Engine;
using VyralBot.Integrations.Restream;
using VyralBot.Integrations.Twitch;
using VyralBot.Integrations.YouTube;

namespace VyralBot.Api.Services;

/// <summary>
/// Aggregates a read-only runtime status snapshot for the token-gated control
/// API (<c>GET /api/status</c>). Registered as a singleton in the WPF host DI
/// so it outlives web-server restarts; <see cref="WebServerService"/> forwards
/// it into the embedded Kestrel child container.
///
/// Platform/OBS/restream services are resolved lazily from the injected
/// provider so constructing this service never forces early initialization of
/// engines (OBS boot, restream process) at app startup.
///
/// Viewer counts: only Kick currently reports live viewer counts on the bus
/// (<see cref="KickStreamStatusEvent"/>); the latest value is cached here.
/// Subscriptions follow the StreamLiveRegistry pattern: the SubscribeAsync
/// tasks complete synchronously and are unwrapped at unsubscribe time.
/// </summary>
public sealed class ControlApiStatusService : IDisposable
{
    private readonly IServiceProvider _services;
    private readonly IEventBus _eventBus;
    private readonly ILogger<ControlApiStatusService>? _logger;
    private readonly DateTime _startedAtUtc = DateTime.UtcNow;

    private readonly ConcurrentDictionary<string, int> _viewerCounts = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, DateTime> _viewerCountSeenUtc = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Task<Guid>> _subscriptions = [];

    public ControlApiStatusService(IServiceProvider services, IEventBus eventBus, ILogger<ControlApiStatusService>? logger = null)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        _logger = logger;

        _subscriptions.Add(SubscribeSafe<KickStreamStatusEvent>("Kick", e => e.ViewerCount));
    }

    /// <summary>Builds a JSON-safe status object (booleans, counters, timestamps only).</summary>
    public object BuildStatus()
    {
        var twitch = _services.GetService<ITwitchService>();
        var kick = _services.GetService<IKickService>();
        var youtube = _services.GetService<IYouTubeService>();
        var discord = _services.GetService<IDiscordService>();
        var discordBots = _services.GetService<IDiscordBotManager>();
        var obs = _services.GetService<ObsEngine>();
        var restream = _services.GetService<RestreamEngine>();
        var live = _services.GetService<IStreamLiveRegistry>();

        var nowUtc = DateTime.UtcNow;

        var obsStatus = obs?.GetStatus();
        var restreamStatus = restream?.Status ?? RestreamEngineStatus.Idle;

        return new
        {
            startedAtUtc = _startedAtUtc,
            uptimeSeconds = (long)(nowUtc - _startedAtUtc).TotalSeconds,
            platforms = new
            {
                twitch = new { connected = twitch?.IsConnected == true, live = live?.IsLive("Twitch") == true },
                kick = new { connected = kick?.IsConnected == true, live = live?.IsLive("Kick") == true },
                youtube = new { connected = youtube?.IsConnected == true, live = live?.IsLive("YouTube") == true },
                discord = new
                {
                    connected = (discord?.IsConnected == true) || (discordBots?.Connected.Count > 0),
                    live = false
                }
            },
            obs = new
            {
                state = obsStatus?.State.ToString() ?? ObsEngineState.NotInitialized.ToString(),
                version = obsStatus?.Version
            },
            restream = new
            {
                state = restreamStatus.State.ToString(),
                running = restream?.IsRunning == true,
                streamingSinceUtc = restreamStatus.StreamingSince,
                stats = restreamStatus.Stats is null
                    ? null
                    : new
                    {
                        fps = restreamStatus.Stats.Fps,
                        bitrateKbps = restreamStatus.Stats.BitrateKbps,
                        speedX = restreamStatus.Stats.SpeedX,
                        dropFrames = restreamStatus.Stats.DropFrames
                    }
            },
            viewers = new
            {
                kick = _viewerCounts.TryGetValue("Kick", out var kickViewers) ? kickViewers : (int?)null,
                // Twitch/YouTube do not publish viewer counts on the EventBus today
                twitch = (int?)null,
                youtube = (int?)null,
                lastUpdateUtc = _viewerCountSeenUtc.TryGetValue("Kick", out var seen) ? seen : (DateTime?)null
            }
        };
    }

    /// <summary>
    /// Unsubscribes every bus subscription taken in the ctor.
    /// EventBus.SubscribeAsync completes synchronously (Task.FromResult), so the
    /// tasks are always completed here in practice.
    /// </summary>
    public void Dispose()
    {
        foreach (var subscription in _subscriptions)
        {
            try
            {
                if (subscription.IsCompletedSuccessfully)
                {
                    _eventBus.Unsubscribe(subscription.Result);
                }
                else
                {
                    _logger?.LogWarning("ControlApiStatusService subscription not completed; cannot unsubscribe.");
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to unsubscribe ControlApiStatusService subscription");
            }
        }

        _subscriptions.Clear();
    }

    private Task<Guid> SubscribeSafe<TEvent>(string platform, Func<TEvent, int?> extractViewers)
        where TEvent : IEvent
    {
        try
        {
            return _eventBus.SubscribeAsync<TEvent>(e => OnStreamStatus(e, extractViewers, platform));
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to subscribe to {Platform} stream status for the control API", platform);
            return Task.FromResult(Guid.Empty);
        }
    }

    private Task OnStreamStatus<TEvent>(TEvent @event, Func<TEvent, int?> extractViewers, string platform)
    {
        try
        {
            var viewers = extractViewers(@event);
            if (viewers is >= 0)
            {
                _viewerCounts[platform] = viewers.Value;
                _viewerCountSeenUtc[platform] = DateTime.UtcNow;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to cache {Platform} viewer count", platform);
        }

        return Task.CompletedTask;
    }
}
