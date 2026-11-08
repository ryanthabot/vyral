using System.Collections.Concurrent;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using VyralBot.Core.Models;
using VyralBot.Core.Services.EventBus;
using VyralBot.Core.Services.EventBus.Events;
using VyralBot.Data;

namespace VyralBot.Core.Services.Chat;

/// <summary>
/// Welcome-newcomer detection (retention feature): publishes
/// <see cref="FirstTimeChatterEvent"/> the first time a viewer ever chats so
/// welcome alerts/replies (ChatViewModel) and workflows (WorkflowTriggerService
/// dispatch key "firstmessage") can greet them.
/// </summary>
public interface IFirstTimeChatterService
{
    /// <summary>
    /// Subscribes to <see cref="ChatMessageReceivedEvent"/>. Idempotent — a
    /// second call while running is a no-op.
    /// </summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Unsubscribes and clears the dedup map. Safe to call twice.
    /// </summary>
    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>Inline check for the ChatViewModel message path (pre-watchtime ordering).</summary>
    Task CheckAndPublishAsync(ChatMessageReceivedEvent evt);
}

/// <summary>
/// Detects first-time chatters (welcome-newcomer retention feature).
///
/// A message is FIRST-TIME when the viewer has NO prior activity, source of
/// truth = the profile database: no UserWatchtime row AND no UserCurrency row
/// on ANY platform (single read-only EXISTS pair,
/// <see cref="IDataService.HasAnyActivityAsync"/>). Those are exactly the rows
/// the app's own pipelines create the first time a viewer participates:
/// WatchtimeService.RecordUserActivityAsync runs on every chat message and
/// UserCurrency rows appear with the first earned/watch-time point, so a
/// viewer with neither row has genuinely never been seen.
///
/// The check is READ-ONLY by design — it must never create rows itself,
/// otherwise the lookup would mark every newcomer "known" and stop firing
/// (the watchtime recorder creates the row for the SAME message, so the
/// durable record appears without this service's help).
///
/// BURST DEDUP: a bounded in-memory map ("platform|username" → last sighting,
/// 60-second window, pruned past a fixed capacity) suppresses re-checks and
/// re-fires while a burst of messages from the same newcomer is being
/// processed, before the watchtime row from the first message has landed.
///
/// Lifecycle: registered as a singleton and started/stopped by the generic
/// host (IHostedService, WatchtimeBackgroundService precedent) — the handler
/// only does read-only lookups against the active profile's IDataService, so
/// an app-lifetime subscription is safe; every failure is logged and swallowed
/// (a detection bug must never disturb the chat pipeline it listens to).
/// </summary>
public sealed class FirstTimeChatterService : IFirstTimeChatterService, IHostedService
{
    // Burst-dedup window: repeated messages from the same viewer within this
    // window are skipped entirely (no EXISTS query, no second fire).
    private static readonly TimeSpan DedupWindow = TimeSpan.FromSeconds(60);
    // Bounded-map safety valve: prune expired sightings past this capacity.
    private const int DedupPruneThreshold = 1024;

    private readonly ILogger<FirstTimeChatterService> _logger;
    private readonly IEventBus _eventBus;
    private readonly IDataService _dataService;
    // "platform|username" (lowercased) → last sighting (UTC).
    private readonly ConcurrentDictionary<string, DateTime> _recentSightings = new(StringComparer.Ordinal);
    private Guid? _subscriptionId;
    private bool _started;

    public FirstTimeChatterService(
        ILogger<FirstTimeChatterService> logger,
        IEventBus eventBus,
        IDataService dataService)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_started)
        {
            return;
        }

        _started = true;
        _subscriptionId = await _eventBus.SubscribeAsync<ChatMessageReceivedEvent>(OnChatMessageReceivedAsync);
        _logger.LogInformation("FirstTimeChatterService started (watching chat for newcomers, dedup window {Seconds}s)",
            DedupWindow.TotalSeconds);
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (!_started)
        {
            return Task.CompletedTask;
        }

        _started = false;
        if (_subscriptionId is { } subscriptionId)
        {
            _eventBus.Unsubscribe(subscriptionId);
            _subscriptionId = null;
        }

        _recentSightings.Clear();
        _logger.LogInformation("FirstTimeChatterService stopped");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Inline check for the ChatViewModel's message path — called BEFORE
    /// watchtime records activity for the same message, so the "is this a
    /// newcomer" read can never race the row's creation (the hosted
    /// subscription remains as a fallback for platforms routed elsewhere).
    /// </summary>
    public Task CheckAndPublishAsync(ChatMessageReceivedEvent evt)
        => OnChatMessageReceivedAsync(evt);

    private async Task OnChatMessageReceivedAsync(ChatMessageReceivedEvent evt)
    {
        try
        {
            var username = evt.Username?.Trim();
            if (!_started || string.IsNullOrEmpty(username) || string.IsNullOrEmpty(evt.Platform))
            {
                return;
            }

            // In-memory burst guard first (no DB work for repeated sightings).
            if (!ShouldCheckUser($"{evt.Platform}|{username}"))
            {
                return;
            }

            // Read-only source-of-truth check: known viewers have a watchtime
            // or currency row on some platform. Never creates rows.
            if (await _dataService.HasAnyActivityAsync(username).ConfigureAwait(false))
            {
                return;
            }

            // Newcomer: publish the welcome moment. The watchtime recorder
            // (a parallel ChatMessageReceivedEvent handler) creates the row
            // for this very message, so this fires exactly once per viewer.
            await _eventBus.PublishAsync(new FirstTimeChatterEvent
            {
                Source = evt.Platform,
                Platform = evt.Platform,
                Username = username,
                Message = evt.Message ?? string.Empty,
                DisplayName = null // ChatMessageReceivedEvent carries no display name
            }).ConfigureAwait(false);

            _logger.LogInformation("First-time chatter detected: {Username} on {Platform}", username, evt.Platform);
        }
        catch (Exception ex)
        {
            // Detection must never disturb the chat pipeline.
            _logger.LogError(ex, "First-time chatter detection failed for {Username} on {Platform}",
                evt.Username, evt.Platform);
        }
    }

    /// <summary>
    /// True when this viewer was not sighted within the dedup window — and
    /// stamps the sighting either way, so a burst of messages from one user
    /// performs at most one EXISTS check per window. The map is bounded:
    /// once past the capacity threshold, entries older than the window are
    /// dropped (they are inert anyway).
    /// </summary>
    private bool ShouldCheckUser(string key)
    {
        var now = DateTime.UtcNow;

        if (_recentSightings.TryGetValue(key, out var lastSeen) && now - lastSeen < DedupWindow)
        {
            return false; // burst duplicate — already checked/fired recently
        }

        _recentSightings[key] = now;

        if (_recentSightings.Count > DedupPruneThreshold)
        {
            foreach (var stale in _recentSightings
                         .Where(kvp => now - kvp.Value >= DedupWindow)
                         .Select(kvp => kvp.Key)
                         .ToList())
            {
                _recentSightings.TryRemove(stale, out _);
            }
        }

        return true;
    }
}
