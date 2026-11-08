using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using VyralBot.Core.Models;
using VyralBot.Core.Services.EventBus;
using VyralBot.Core.Services.EventBus.Events;
using VyralBot.Core.Services.StreamStatus;
using VyralBot.Data;
using VyralBot.Data.Entities;

namespace VyralBot.Core.Services.Statistics;

/// <summary>
/// One compiled post-stream report (stream session summary feature).
/// Persisted as part of a capped JSON list in the Configs table (key
/// <c>Statistics.SessionSummaries</c>) and carried by
/// <see cref="SessionEndedEvent"/>.
/// </summary>
public class StreamSessionSummary
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>UTC moment the first platform went live (session start).</summary>
    public DateTime StartedAtUtc { get; set; }

    /// <summary>UTC moment the last live platform went offline (session end).</summary>
    public DateTime EndedAtUtc { get; set; }

    /// <summary>Comma-separated platform names that were live during the session ("Twitch, Kick").</summary>
    public string Platforms { get; set; } = string.Empty;

    /// <summary>Whole minutes between start and end (rounded, never negative).</summary>
    public int DurationMinutes { get; set; }

    public int NewFollowers { get; set; }

    /// <summary>Subscribe + Resub + GiftSub event count.</summary>
    public int NewSubs { get; set; }

    public int Raids { get; set; }

    /// <summary>Sum of the raid "viewers" fields across Raid events.</summary>
    public int RaidViewers { get; set; }

    public int Cheers { get; set; }

    /// <summary>Sum of the cheer EventData value (bits) across Cheer events.</summary>
    public int Bits { get; set; }

    /// <summary>
    /// Chat messages counted IN MEMORY while the session was active. Honest
    /// omission: messages sent before the app started (or while it was
    /// restarted mid-stream) are not reconstructible — no durable per-message
    /// counter exists — so the count only covers messages this app instance
    /// saw. Event and currency numbers come from the database and are
    /// window-complete.
    /// </summary>
    public int ChatMessages { get; set; }

    /// <summary>Currency awarded (Earned side of the transaction ledger) since the session start.</summary>
    public long CurrencyAwarded { get; set; }

    /// <summary>Top chatters of the session (by in-memory message count, max 3).</summary>
    public List<SessionChatterSummary> TopChatters { get; set; } = new();

    /// <summary>Full per-event-type breakdown (canonical writer casing, e.g. "Follow", "GiftSub").</summary>
    public Dictionary<string, int> EventCounts { get; set; } = new();

    // ----- Display helpers (UI-only, never persisted) -----

    /// <summary>Duration as "2h 05m" / "45m" for the Statistics page cards.</summary>
    [JsonIgnore]
    public string DurationText =>
        DurationMinutes >= 60
            ? $"{DurationMinutes / 60}h {DurationMinutes % 60:00}m"
            : $"{DurationMinutes}m";

    /// <summary>Session start in local time for list rows ("Fri 06 Nov 19:30").</summary>
    [JsonIgnore]
    public string StartedLocalText => StartedAtUtc.ToLocalTime().ToString("ddd dd MMM HH:mm");

    /// <summary>Three headline numbers for the recent-sessions rows.</summary>
    [JsonIgnore]
    public string HeadlineText => $"{NewFollowers} follows · {NewSubs} subs · {ChatMessages} messages";
}

/// <summary>One row of the session's top-chatter breakdown.</summary>
public class SessionChatterSummary
{
    public string Username { get; set; } = string.Empty;
    public int Messages { get; set; }
}

/// <summary>
/// Pure compilation of <see cref="StreamSessionSummary"/> from rows the caller
/// supplies (no data access) so the math — breakdowns, duration rounding,
/// top-chatter ordering — is directly unit-testable (Wave16).
/// </summary>
public static class SessionSummaryCompiler
{
    /// <summary>
    /// Compile a summary for the session window. Event rows are matched by
    /// EventType OrdinalIgnoreCase (same parity as the SQL UPPER(EventType)
    /// comparison in StatisticsService).
    /// </summary>
    /// <param name="startedUtc">Session start (first platform went live).</param>
    /// <param name="endedUtc">Session end (last platform went offline).</param>
    /// <param name="platforms">Platforms that were live, comma-separated.</param>
    /// <param name="events">Event statistics rows inside the window.</param>
    /// <param name="currencyEarned">Currency awarded since the session start (ledger Earned total).</param>
    /// <param name="chatCounts">Per-username chat message counts of the session (in-memory); null/empty when none were tracked.</param>
    public static StreamSessionSummary Compile(
        DateTime startedUtc,
        DateTime endedUtc,
        string platforms,
        IEnumerable<EventStatistic> events,
        long currencyEarned,
        IReadOnlyDictionary<string, int>? chatCounts = null)
    {
        ArgumentNullException.ThrowIfNull(events);

        var rows = events.ToList();

        // Group OrdinalIgnoreCase but keep the first-seen writer casing so the
        // breakdown keys match the exact pipeline names ("Follow", "GiftSub").
        var breakdown = rows
            .GroupBy(r => r.EventType, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.First().EventType,
                g => g.Count(),
                StringComparer.OrdinalIgnoreCase);

        long SumValues(string eventType) => rows
            .Where(r => string.Equals(r.EventType, eventType, StringComparison.OrdinalIgnoreCase))
            .Sum(r => (long)ExtractValue(r.EventData));

        var summary = new StreamSessionSummary
        {
            StartedAtUtc = startedUtc,
            EndedAtUtc = endedUtc,
            Platforms = platforms,
            DurationMinutes = Math.Max(0, (int)Math.Round((endedUtc - startedUtc).TotalMinutes)),
            NewFollowers = Count(breakdown, "Follow"),
            NewSubs = Count(breakdown, "Subscribe") + Count(breakdown, "Resub") + Count(breakdown, "GiftSub"),
            Raids = Count(breakdown, "Raid"),
            RaidViewers = (int)SumValues("Raid"),
            Cheers = Count(breakdown, "Cheer"),
            Bits = (int)SumValues("Cheer"),
            ChatMessages = chatCounts?.Values.Sum() ?? 0,
            CurrencyAwarded = currencyEarned,
            EventCounts = breakdown
        };

        summary.TopChatters = (chatCounts ?? new Dictionary<string, int>())
            .Where(kvp => kvp.Value > 0)
            .OrderByDescending(kvp => kvp.Value)
            .ThenBy(kvp => kvp.Key, StringComparer.Ordinal)
            .Take(3)
            .Select(kvp => new SessionChatterSummary { Username = kvp.Key, Messages = kvp.Value })
            .ToList();

        return summary;
    }

    private static int Count(Dictionary<string, int> breakdown, string eventType) =>
        breakdown.TryGetValue(eventType, out var count) ? count : 0;

    /// <summary>
    /// Extract the numeric payload of an event row (raid viewers, cheer bits,
    /// gift count). Same field precedence as StatisticsService.ExtractValueFromEventData
    /// so session numbers always agree with the Statistics page.
    /// </summary>
    internal static int ExtractValue(string? eventData)
    {
        if (string.IsNullOrEmpty(eventData))
        {
            return 0;
        }

        try
        {
            using var json = JsonDocument.Parse(eventData);
            var root = json.RootElement;

            foreach (var field in new[] { "count", "amount", "viewers", "bits", "value" })
            {
                if (root.TryGetProperty(field, out var prop) &&
                    prop.ValueKind == JsonValueKind.Number &&
                    prop.TryGetInt32(out var value))
                {
                    return value;
                }
            }
        }
        catch (JsonException)
        {
            // Malformed EventData — count the event but contribute 0 to sums.
        }

        return 0;
    }
}

/// <summary>
/// Tracks stream sessions and compiles the post-stream report: when the FIRST
/// platform goes live a session opens; when the LAST live platform goes
/// offline the summary is compiled from the profile database plus the
/// in-memory chat counters, persisted (last 20) and published as
/// <see cref="SessionEndedEvent"/>.
///
/// Data sources (at compile time):
/// - EventStatistics rows in the session window
///   (<see cref="IDataService.GetEventStatisticsAsync"/>) → new
///   followers/subs/raids/cheers breakdown + full per-type counts.
/// - Currency transaction ledger
///   (<see cref="IDataService.GetCurrencyTransactionTotalsSinceAsync"/>) →
///   currency awarded since the session start (Earned side only).
/// - In-memory per-username chat message counts (ChatMessageReceivedEvent
///   subscription) → chat message total + top chatter.
///
/// Lifecycle: registered as a singleton and started/stopped by the generic
/// host (IHostedService, FirstTimeChatterService precedent). A subscriber
/// must never break the pipeline it listens to — every handler catches,
/// logs and swallows.
/// </summary>
public interface ISessionSummaryService
{
    /// <summary>
    /// Subscribes to <see cref="ChatMessageReceivedEvent"/> and hooks
    /// <see cref="IStreamLiveRegistry.StreamLiveChanged"/>. Idempotent — a
    /// second call while running is a no-op.
    /// </summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>Unsubscribes and drops any open session tracking. Safe to call twice.</summary>
    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// The persisted session summaries, newest first (at most
    /// <paramref name="limit"/> / the persisted cap of 20). Empty before the
    /// first tracked stream ends.
    /// </summary>
    Task<IReadOnlyList<StreamSessionSummary>> GetRecentSummariesAsync(int limit = 20, CancellationToken cancellationToken = default);
}

public sealed class SessionSummaryService : ISessionSummaryService, IHostedService
{
    /// <summary>Persisted summaries are capped at this many (newest kept).</summary>
    public const int MaxStoredSummaries = 20;

    // Sessions shorter than this are discarded: the YouTube live-chat state
    // machine ("Connecting" → "Listening") can flap, and a sub-minute
    // "stream" is noise, not a session.
    private static readonly TimeSpan MinSessionDuration = TimeSpan.FromMinutes(1);

    private const string StorageKey = "Statistics.SessionSummaries";
    private const string StorageCategory = "Statistics";

    // Case-insensitive reads (tolerant of future format tweaks), indented
    // writes (the list is small and user-inspectable).
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly ILogger<SessionSummaryService> _logger;
    private readonly IEventBus _eventBus;
    private readonly IDataService _dataService;
    private readonly IStreamLiveRegistry _liveRegistry;
    private readonly Func<DateTime> _clock;

    private readonly object _gate = new();
    // Canonical platform name → itself (OrdinalIgnoreCase key set of live platforms).
    private readonly Dictionary<string, string> _livePlatforms = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Union of every platform live during the CURRENT session —
    /// a platform that goes offline mid-session still belongs in the report.</summary>
    private readonly Dictionary<string, string> _sessionPlatforms = new(StringComparer.OrdinalIgnoreCase);
    private DateTime _sessionStartedUtc;
    // Username → message count of the open session (OrdinalIgnoreCase keys:
    // "User" and "user" are the same chatter, matching SQL COLLATE NOCASE rows).
    private Dictionary<string, int> _sessionChatCounts = new(StringComparer.OrdinalIgnoreCase);

    private Guid? _chatSubscriptionId;
    private bool _started;

    public SessionSummaryService(
        ILogger<SessionSummaryService> logger,
        IEventBus eventBus,
        IDataService dataService,
        IStreamLiveRegistry liveRegistry,
        Func<DateTime>? clock = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
        _liveRegistry = liveRegistry ?? throw new ArgumentNullException(nameof(liveRegistry));
        _clock = clock ?? (() => DateTime.UtcNow);
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_started)
        {
            return;
        }

        _started = true;
        _chatSubscriptionId = await _eventBus.SubscribeAsync<ChatMessageReceivedEvent>(OnChatMessageReceivedAsync);
        _liveRegistry.StreamLiveChanged += OnStreamLiveChanged;
        _logger.LogInformation("SessionSummaryService started (tracking stream sessions for post-stream reports)");
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (!_started)
        {
            return;
        }

        _started = false;
        _liveRegistry.StreamLiveChanged -= OnStreamLiveChanged;
        if (_chatSubscriptionId is { } subscriptionId)
        {
            _eventBus.Unsubscribe(subscriptionId);
            _chatSubscriptionId = null;
        }

        // Best-effort: when the app closes mid-stream, compile the summary
        // from the data up to this moment so the streamer still gets a
        // report (CloseSessionAsync catches its own errors; shutdown never
        // hangs on this).
        var openSession = SnapshotOpenSession(forceClose: true);
        if (openSession is not null)
        {
            await CloseSessionAsync(openSession.Value.StartedUtc, openSession.Value.Platforms, "app shutdown")
                .ConfigureAwait(false);
        }

        _logger.LogInformation("SessionSummaryService stopped");
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<StreamSessionSummary>> GetRecentSummariesAsync(int limit = 20, CancellationToken cancellationToken = default)
    {
        var summaries = await LoadSummariesAsync().ConfigureAwait(false);
        return summaries.Take(Math.Max(0, limit)).ToList();
    }

    private void OnStreamLiveChanged(object? sender, string platform)
    {
        try
        {
            // The registry has already flipped; query the NEW state. The
            // platform name is canonicalized (writer casing, e.g. "Twitch")
            // so the Platforms CSV stores stable names.
            var canonical = platform.Trim();
            var isLive = _liveRegistry.IsLive(platform);

            (DateTime StartedUtc, string Platforms)? toClose = null;

            lock (_gate)
            {
                if (isLive)
                {
                    _livePlatforms[canonical] = canonical;
                    _sessionPlatforms[canonical] = canonical;
                    if (_livePlatforms.Count == 1)
                    {
                        // First platform went live → a session opens.
                        _sessionStartedUtc = _clock();
                        _sessionChatCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                        _logger.LogInformation("Stream session started at {StartedUtc} ({Platform})",
                            _sessionStartedUtc, canonical);
                    }
                }
                else
                {
                    if (_livePlatforms.Remove(canonical) &&
                        _livePlatforms.Count == 0 &&
                        _sessionStartedUtc != default)
                    {
                        // Report every platform that was live at ANY point this
                        // session, not just the ones still up at the end.
                        var platforms = string.Join(", ", _sessionPlatforms.Values.OrderBy(p => p, StringComparer.Ordinal));
                        toClose = (_sessionStartedUtc, platforms);
                        _sessionStartedUtc = default;
                        _sessionPlatforms.Clear();
                    }
                }
            }

            if (toClose is { } session)
            {
                // Fire-and-forget: the registry handler must not block on the
                // compile's DB IO. CloseSessionAsync catches everything.
                _ = CloseSessionAsync(session.StartedUtc, session.Platforms, "stream offline");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "StreamLiveChanged handling failed for {Platform}", platform);
        }
    }

    private async Task OnChatMessageReceivedAsync(ChatMessageReceivedEvent evt)
    {
        try
        {
            var username = evt.Username?.Trim();
            if (!_started || string.IsNullOrEmpty(username) || string.IsNullOrEmpty(evt.Platform))
            {
                return;
            }

            lock (_gate)
            {
                // Only messages seen while a session is open are counted (the
                // in-memory counter cannot reconstruct pre-start messages).
                if (_livePlatforms.Count == 0)
                {
                    return;
                }

                _sessionChatCounts[username] = _sessionChatCounts.TryGetValue(username, out var count) ? count + 1 : 1;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Session chat counting failed for {Username}", evt.Username);
        }
    }

    /// <summary>
    /// Snapshot (and by default close) the currently open session under the
    /// gate. Null when no session is open. With <paramref name="forceClose"/>
    /// the live set is cleared too (shutdown path: platforms stay "live" in
    /// the registry but the session ends here).
    /// </summary>
    private (DateTime StartedUtc, string Platforms)? SnapshotOpenSession(bool forceClose)
    {
        lock (_gate)
        {
            if (_sessionStartedUtc == default)
            {
                return null;
            }

            // Session-wide union (a platform that left mid-session still
            // belongs in the report), matching the offline-close path.
            var platforms = string.Join(", ", _sessionPlatforms.Values.OrderBy(p => p, StringComparer.Ordinal));
            var started = _sessionStartedUtc;
            _sessionStartedUtc = default;
            _sessionPlatforms.Clear();

            if (forceClose)
            {
                _livePlatforms.Clear();
            }

            return (started, platforms);
        }
    }

    /// <summary>
    /// Compile, persist and publish one session. All failures are caught and
    /// logged — this runs fire-and-forget from the registry handler and must
    /// never surface an exception there.
    /// </summary>
    private async Task CloseSessionAsync(DateTime startedUtc, string platforms, string reason)
    {
        try
        {
            Dictionary<string, int> chatCounts;
            lock (_gate)
            {
                chatCounts = _sessionChatCounts;
                _sessionChatCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            }

            var endedUtc = _clock();
            if (endedUtc - startedUtc < MinSessionDuration)
            {
                _logger.LogInformation("Discarded sub-minute stream session ({Seconds:F0}s, {Reason})",
                    (endedUtc - startedUtc).TotalSeconds, reason);
                return;
            }

            var events = await _dataService.GetEventStatisticsAsync(limit: 10000, startDate: startedUtc, endDate: endedUtc)
                .ConfigureAwait(false);
            var (earned, _) = await _dataService.GetCurrencyTransactionTotalsSinceAsync(startedUtc)
                .ConfigureAwait(false);

            var summary = SessionSummaryCompiler.Compile(startedUtc, endedUtc, platforms, events, earned, chatCounts);

            await PersistAsync(summary).ConfigureAwait(false);
            await _eventBus.PublishAsync(new SessionEndedEvent
            {
                Source = "System",
                Summary = summary
            }).ConfigureAwait(false);

            _logger.LogInformation(
                "Stream session summary compiled ({Reason}): {Duration} min on {Platforms}, {Followers} follows, {Subs} subs, {Raids} raids, {Cheers} cheers, {Messages} chat messages, {Currency} currency awarded",
                reason, summary.DurationMinutes, platforms, summary.NewFollowers, summary.NewSubs,
                summary.Raids, summary.Cheers, summary.ChatMessages, summary.CurrencyAwarded);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to compile the stream session summary ({Reason})", reason);
        }
    }

    private async Task PersistAsync(StreamSessionSummary summary)
    {
        var summaries = await LoadSummariesAsync().ConfigureAwait(false);
        summaries.Insert(0, summary);
        if (summaries.Count > MaxStoredSummaries)
        {
            summaries.RemoveRange(MaxStoredSummaries, summaries.Count - MaxStoredSummaries);
        }

        var json = JsonSerializer.Serialize(summaries, JsonOptions);
        await _dataService.SetConfigValueAsync(StorageKey, json, StorageCategory).ConfigureAwait(false);
    }

    private async Task<List<StreamSessionSummary>> LoadSummariesAsync()
    {
        var json = await _dataService.GetConfigValueAsync(StorageKey).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json))
        {
            return new List<StreamSessionSummary>();
        }

        try
        {
            return JsonSerializer.Deserialize<List<StreamSessionSummary>>(json, JsonOptions) ?? new List<StreamSessionSummary>();
        }
        catch (JsonException ex)
        {
            // Corrupt list: start over rather than break the feature (the
            // summaries are derived data — every number is recomputable).
            _logger.LogWarning(ex, "Stored session summaries are unreadable; starting a fresh list");
            return new List<StreamSessionSummary>();
        }
    }
}
