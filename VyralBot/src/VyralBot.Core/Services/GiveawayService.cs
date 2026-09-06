using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using VyralBot.Core.Services.Currency;
using VyralBot.Core.Services.EventBus;
using VyralBot.Core.Services.EventBus.Events;
using VyralBot.Core.Services.Linking;
using VyralBot.Core.Services.Performance;
using VyralBot.Data;
using VyralBot.Data.Entities;

namespace VyralBot.Core.Services;

/// <summary>
/// Entry intake mode for an active giveaway. "Live" persists every entry
/// immediately; "Timed" queues entries and flushes them in batches when the
/// entry rate spikes.
/// </summary>
public enum GiveawayIntakeMode
{
    /// <summary>Every entry is persisted and confirmed immediately.</summary>
    Live = 0,

    /// <summary>Entries are queued and flushed in batches to absorb floods.</summary>
    Timed = 1
}

/// <summary>
/// Pure odds-boost calculator (public test target). Combines the current
/// subscriber flag, best linked sub tier, follower-anywhere state and the
/// linked-platform count into a bonus-entry total plus a human-readable
/// breakdown such as "Tier 2 (+2), Follower (+1), 3 linked platforms (+2)".
/// Sub rewards follow the tier ladder only: the effective tier is the best
/// linked tier, or tier 1 for a current subscriber with no recorded tier.
/// </summary>
public static class GiveawayOdds
{
    /// <summary>
    /// Calculates the odds-boost bonus entries for one giveaway entry.
    /// </summary>
    /// <param name="isSubscriberNow">Whether the viewer is a subscriber on the entering platform right now.</param>
    /// <param name="subscriberBonusEntries">Ignored: on the odds path the tier ladder is the single source of sub rewards (the legacy flat bonus applies only when the odds boost is off), so the two are never double-counted.</param>
    /// <param name="bestLinkedTier">Best subscription tier across the viewer's linked platforms (0 = none). A current subscriber with no recorded tier is treated as tier 1.</param>
    /// <param name="t1Bonus">Bonus entries for tier 1.</param>
    /// <param name="t2Bonus">Bonus entries for tier 2.</param>
    /// <param name="t3Bonus">Bonus entries for tier 3.</param>
    /// <param name="followerAnywhere">Whether the viewer follows on any linked platform.</param>
    /// <param name="followerBonus">Bonus entries for followers.</param>
    /// <param name="linkedPlatformCount">Number of platforms the viewer has statuses on.</param>
    /// <param name="perExtraPlatformBonus">Bonus entries per linked platform beyond the first.</param>
    /// <param name="perExtraPlatformCap">Maximum number of extra platforms that earn the bonus.</param>
    /// <returns>Total bonus entries plus the breakdown text ("" when zero).</returns>
    public static (int BonusEntries, string Breakdown) Calculate(
        bool isSubscriberNow, int subscriberBonusEntries,
        int bestLinkedTier, int t1Bonus, int t2Bonus, int t3Bonus,
        bool followerAnywhere, int followerBonus,
        int linkedPlatformCount, int perExtraPlatformBonus, int perExtraPlatformCap)
    {
        int total = 0;
        var parts = new List<string>();

        // Sub rewards come from the tier ladder only: a current subscriber with
        // no recorded tier counts as tier 1; a non-subscriber earns no tier bonus.
        int bestTier = Math.Max(bestLinkedTier, isSubscriberNow ? 1 : 0);
        int tierBonus = bestTier switch
        {
            1 => t1Bonus,
            2 => t2Bonus,
            3 => t3Bonus,
            _ => 0
        };
        if (tierBonus > 0)
        {
            total += tierBonus;
            parts.Add($"Tier {bestTier} (+{tierBonus})");
        }

        int followerAmount = followerAnywhere ? Math.Max(0, followerBonus) : 0;
        if (followerAmount > 0)
        {
            total += followerAmount;
            parts.Add($"Follower (+{followerAmount})");
        }

        int platformBonus = Math.Min(perExtraPlatformCap, Math.Max(0, linkedPlatformCount - 1))
                            * Math.Max(0, perExtraPlatformBonus);
        if (platformBonus > 0)
        {
            total += platformBonus;
            parts.Add($"{linkedPlatformCount} linked platforms (+{platformBonus})");
        }

        return (total, string.Join(", ", parts));
    }
}

/// <summary>
/// Service for managing giveaways with cross-platform entry support.
/// Handles giveaway lifecycle, entry validation, winner selection, odds
/// boosting and adaptive (Live/Timed) entry intake, publishing giveaway
/// lifecycle events onto the event bus.
/// </summary>
public class GiveawayService : IDisposable
{
    private const int MaxQueuedEntries = 10_000;
    private const int TimedFlushBatchThreshold = 500;
    private static readonly TimeSpan SettingsCacheTtl = TimeSpan.FromSeconds(30);

    private readonly IDataService _dataService;
    private readonly IEventBus _eventBus;
    private readonly ILinkedAccountService _linkedAccounts;
    private readonly IViewerStatusService _viewerStatus;
    private readonly IMetricsRegistry? _metrics;
    private readonly ILogger<GiveawayService> _logger;
    private readonly Random _random = new();

    // Adaptive intake state.
    private readonly EntryRateWindow _rateWindow = new();
    private readonly ConcurrentQueue<EntryRequest> _pendingEntries = new();
    private GiveawayIntakeMode _intakeMode = GiveawayIntakeMode.Live;

    // Lazily-started flush loop lifecycle (only one loop runs at a time).
    private readonly object _flushGate = new();
    private Task? _flushLoopTask;
    private CancellationTokenSource? _flushLoopCts;
    private bool _disposed;

    // Config cache (30s TTL) shared by odds + intake thresholds.
    private readonly SemaphoreSlim _settingsLock = new(1, 1);
    private OddsSettings _cachedSettings = OddsSettings.Default;
    private DateTime _settingsLoadedAtUtc = DateTime.MinValue;

    public GiveawayService(
        IDataService dataService,
        IEventBus eventBus,
        ILinkedAccountService linkedAccounts,
        IViewerStatusService viewerStatus,
        ILogger<GiveawayService> logger,
        IMetricsRegistry? metrics = null)
    {
        _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        _linkedAccounts = linkedAccounts ?? throw new ArgumentNullException(nameof(linkedAccounts));
        _viewerStatus = viewerStatus ?? throw new ArgumentNullException(nameof(viewerStatus));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _metrics = metrics;
    }

    /// <summary>Gets the current entry intake mode (Live or Timed).</summary>
    public GiveawayIntakeMode CurrentIntakeMode => _intakeMode;

    /// <summary>
    /// Creates a new giveaway
    /// </summary>
    public async Task<Giveaway> CreateGiveawayAsync(
        string name,
        string description,
        string entryKeyword,
        int winnerCount = 1,
        int? maxEntries = null,
        DateTime? scheduledEndTime = null,
        bool subscriberBonusEnabled = false,
        int subscriberBonusEntries = 1,
        int minWatchtimeMinutes = 0,
        string allowedPlatforms = "All",
        bool followersOnly = false,
        bool subscribersOnly = false,
        bool oddsBoostEnabled = false)
    {
        var giveaway = new Giveaway
        {
            Name = name,
            Description = description,
            EntryKeyword = entryKeyword,
            Status = GiveawayStatus.Pending,
            WinnerCount = winnerCount,
            MaxEntries = maxEntries,
            ScheduledEndTime = scheduledEndTime,
            SubscriberBonusEnabled = subscriberBonusEnabled,
            SubscriberBonusEntries = subscriberBonusEntries,
            MinWatchtimeMinutes = minWatchtimeMinutes,
            AllowedPlatforms = allowedPlatforms,
            FollowersOnly = followersOnly,
            SubscribersOnly = subscribersOnly,
            OddsBoostEnabled = oddsBoostEnabled
        };

        await _dataService.SaveGiveawayAsync(giveaway);
        _logger.LogInformation("Created giveaway: {Name} (ID: {Id})", name, giveaway.Id);

        return giveaway;
    }

    /// <summary>
    /// Starts an existing giveaway and begins accepting entries
    /// </summary>
    public async Task<bool> StartGiveawayAsync(Guid giveawayId)
    {
        var giveaway = await _dataService.GetGiveawayAsync(giveawayId);
        if (giveaway == null)
        {
            _logger.LogWarning("Giveaway not found: {Id}", giveawayId);
            return false;
        }

        if (giveaway.Status != GiveawayStatus.Pending)
        {
            _logger.LogWarning("Cannot start giveaway in status: {Status}", giveaway.Status);
            return false;
        }

        // Check if there's already an active giveaway
        var activeGiveaway = await _dataService.GetActiveGiveawayAsync();
        if (activeGiveaway != null)
        {
            _logger.LogWarning("Cannot start giveaway - another is already active: {Name}", activeGiveaway.Name);
            return false;
        }

        giveaway.Status = GiveawayStatus.Active;
        giveaway.StartedAt = DateTime.UtcNow;
        await _dataService.SaveGiveawayAsync(giveaway);

        _logger.LogInformation("Started giveaway: {Name} (ID: {Id})", giveaway.Name, giveaway.Id);

        await PublishSafeAsync(new GiveawayStartedEvent
        {
            GiveawayId = giveaway.Id,
            Title = giveaway.Name,
            EntryKeyword = giveaway.EntryKeyword,
            Source = "System"
        });

        return true;
    }

    /// <summary>
    /// Processes a user entry into the active giveaway
    /// </summary>
    public async Task<(bool Success, string Message)> EnterGiveawayAsync(
        Guid giveawayId,
        string username,
        string platform,
        string? platformUserId = null,
        bool isSubscriber = false,
        bool isFollower = false,
        int watchtimeMinutes = 0,
        string? message = null)
    {
        // Record every keyword entry attempt for the adaptive-intake rate window.
        var now = DateTime.UtcNow;
        _rateWindow.Record(now);
        int rate = _rateWindow.CountInLastMinute(now);

        var giveaway = await _dataService.GetGiveawayAsync(giveawayId);
        if (giveaway == null)
        {
            return (false, "Giveaway not found.");
        }

        if (giveaway.Status != GiveawayStatus.Active)
        {
            return (false, "This giveaway is not currently active.");
        }

        await EvaluateIntakeModeAsync(giveaway, rate);

        // Check platform eligibility
        if (giveaway.AllowedPlatforms != "All")
        {
            var allowedPlatforms = giveaway.AllowedPlatforms.Split(',', StringSplitOptions.RemoveEmptyEntries);
            if (!allowedPlatforms.Contains(platform, StringComparer.OrdinalIgnoreCase))
            {
                return (false, $"This giveaway is not open to {platform} users.");
            }
        }

        // Check follower requirement
        if (giveaway.FollowersOnly && !isFollower)
        {
            return (false, "You must be a follower to enter this giveaway.");
        }

        // Check subscriber requirement
        if (giveaway.SubscribersOnly && !isSubscriber)
        {
            return (false, "You must be a subscriber to enter this giveaway.");
        }

        // Check watchtime requirement
        if (giveaway.MinWatchtimeMinutes > 0 && watchtimeMinutes < giveaway.MinWatchtimeMinutes)
        {
            return (false, $"You need at least {giveaway.MinWatchtimeMinutes} minutes of watchtime to enter.");
        }

        // Check if user already entered
        var existingEntry = await _dataService.GetGiveawayEntryAsync(giveaway.Id, username, platform);
        if (existingEntry != null)
        {
            return (false, "You have already entered this giveaway!");
        }

        // Check max entries limit
        if (giveaway.MaxEntries.HasValue)
        {
            var currentEntryCount = await _dataService.GetGiveawayEntryCountAsync(giveaway.Id);
            if (currentEntryCount >= giveaway.MaxEntries.Value)
            {
                return (false, "This giveaway has reached its maximum number of entries.");
            }
        }

        var request = new EntryRequest(
            giveaway.Id, username, platform, platformUserId,
            isSubscriber, isFollower, watchtimeMinutes, message, now);

        // Timed intake: queue the entry; the flush loop persists it in batches.
        if (_intakeMode == GiveawayIntakeMode.Timed)
        {
            if (_pendingEntries.Count >= MaxQueuedEntries)
            {
                return (false, "Entry queue is full — try again shortly");
            }

            _pendingEntries.Enqueue(request);
            EnsureFlushLoopStarted();
            return (true, "You're entered! Good luck!");
        }

        // Live intake: persist immediately.
        var (persisted, entryCount, _) = await PersistEntryAsync(request);
        if (!persisted)
        {
            return (false, "You have already entered this giveaway!");
        }

        // Update giveaway stats
        giveaway.TotalEntries = await _dataService.GetGiveawayEntryCountAsync(giveaway.Id);
        giveaway.UniqueParticipants = await _dataService.GetGiveawayUniqueParticipantsAsync(giveaway.Id);
        await _dataService.SaveGiveawayAsync(giveaway);

        await PublishSafeAsync(new GiveawayEntryEvent
        {
            GiveawayId = giveaway.Id,
            Username = username,
            Platform = platform,
            TotalEntries = giveaway.TotalEntries,
            UniqueParticipants = giveaway.UniqueParticipants,
            IsTimed = false,
            Source = platform
        });

        string responseMessage = entryCount > 1
            ? $"You're entered! You have {entryCount} entries (subscriber bonus)."
            : "You're entered! Good luck!";

        return (true, responseMessage);
    }

    /// <summary>
    /// Draws random winner(s) from the active giveaway entries
    /// </summary>
    public async Task<(bool Success, List<string> Winners, string Message)> DrawWinnersAsync(Guid giveawayId)
    {
        var giveaway = await _dataService.GetGiveawayAsync(giveawayId);
        if (giveaway == null)
        {
            return (false, new List<string>(), "Giveaway not found.");
        }

        if (giveaway.Status != GiveawayStatus.Active)
        {
            return (false, new List<string>(), "This giveaway is not active.");
        }

        // Flush any pending timed entries so the draw sees every queued entry.
        await DrainTimedQueueAsync(CancellationToken.None);

        var entries = (await _dataService.GetGiveawayEntriesAsync(giveaway.Id)).ToList();
        if (entries.Count == 0)
        {
            return (false, new List<string>(), "No entries to draw from!");
        }

        // Create weighted pool (each entry gets EntryCount tickets)
        var entryPool = new List<GiveawayEntry>();
        foreach (var entry in entries)
        {
            for (int i = 0; i < entry.EntryCount; i++)
            {
                entryPool.Add(entry);
            }
        }

        var winners = new List<string>();
        var winnerEntries = new HashSet<Guid>(); // Track to prevent duplicate winners

        int winnersToDraw = Math.Min(giveaway.WinnerCount, entries.Count);

        for (int i = 0; i < winnersToDraw; i++)
        {
            GiveawayEntry? winnerEntry = null;
            int attempts = 0;
            // High cap so a heavily-weighted pool (one dominant entrant)
            // still fills every slot; 100 could silently come up short.
            const int maxAttempts = 1000;

            // Draw until we find a unique winner or hit max attempts
            while (winnerEntry == null && attempts < maxAttempts)
            {
                int randomIndex = _random.Next(entryPool.Count);
                var candidate = entryPool[randomIndex];

                if (!winnerEntries.Contains(candidate.Id))
                {
                    winnerEntry = candidate;
                }

                attempts++;
            }

            if (winnerEntry != null)
            {
                winnerEntries.Add(winnerEntry.Id);
                winners.Add($"{winnerEntry.Username} ({winnerEntry.Platform})");

                // Mark as winner
                winnerEntry.IsWinner = true;
                winnerEntry.WonAt = DateTime.UtcNow;
                await _dataService.SaveGiveawayEntryAsync(winnerEntry);
            }
        }

        // Surface shortfalls: with the attempt cap exhausted the draw can
        // still produce fewer winners than requested — never silent.
        if (winners.Count < winnersToDraw)
        {
            _logger.LogWarning(
                "Giveaway {Name}: only {Filled} of {Requested} winner slot(s) filled — " +
                "the weighted pool ran out of unique candidates",
                giveaway.Name, winners.Count, winnersToDraw);
        }

        // End giveaway
        giveaway.Status = GiveawayStatus.Completed;
        giveaway.EndedAt = DateTime.UtcNow;
        giveaway.Winners = string.Join(", ", winners);
        await _dataService.SaveGiveawayAsync(giveaway);

        _logger.LogInformation("Giveaway completed: {Name} - Winners: {Winners}", giveaway.Name, giveaway.Winners);

        await PublishSafeAsync(new GiveawayWinnersDrawnEvent
        {
            GiveawayId = giveaway.Id,
            Title = giveaway.Name,
            WinnersText = giveaway.Winners ?? string.Empty,
            Source = "System"
        });

        string message = winners.Count == 1
            ? $"🎉 Winner: {winners[0]}!"
            : $"🎉 Winners: {string.Join(", ", winners)}!";

        return (true, winners, message);
    }

    /// <summary>
    /// Cancels an active giveaway
    /// </summary>
    public async Task<bool> CancelGiveawayAsync(Guid giveawayId)
    {
        var giveaway = await _dataService.GetGiveawayAsync(giveawayId);
        if (giveaway == null)
        {
            return false;
        }

        if (giveaway.Status == GiveawayStatus.Completed || giveaway.Status == GiveawayStatus.Cancelled)
        {
            _logger.LogWarning("Cannot cancel giveaway in status: {Status}", giveaway.Status);
            return false;
        }

        giveaway.Status = GiveawayStatus.Cancelled;
        giveaway.EndedAt = DateTime.UtcNow;
        await _dataService.SaveGiveawayAsync(giveaway);

        _logger.LogInformation("Cancelled giveaway: {Name} (ID: {Id})", giveaway.Name, giveaway.Id);

        await PublishSafeAsync(new GiveawayCancelledEvent
        {
            GiveawayId = giveaway.Id,
            Title = giveaway.Name,
            Source = "System"
        });

        return true;
    }

    /// <summary>
    /// Deletes a giveaway. Active giveaways cannot be deleted (cancel or draw first).
    /// </summary>
    public async Task<bool> DeleteGiveawayAsync(Guid giveawayId)
    {
        var giveaway = await _dataService.GetGiveawayAsync(giveawayId);
        if (giveaway == null)
        {
            _logger.LogWarning("Cannot delete giveaway — not found: {Id}", giveawayId);
            return false;
        }

        if (giveaway.Status == GiveawayStatus.Active)
        {
            _logger.LogWarning("Cannot delete active giveaway: {Name}", giveaway.Name);
            return false;
        }

        await _dataService.DeleteGiveawayAsync(giveawayId);
        _logger.LogInformation("Deleted giveaway: {Name} (ID: {Id})", giveaway.Name, giveawayId);
        return true;
    }

    /// <summary>
    /// Gets the number of keyword entry attempts recorded in the last minute.
    /// </summary>
    public Task<int> GetCurrentEntriesPerMinuteAsync()
    {
        return Task.FromResult(_rateWindow.CountInLastMinute(DateTime.UtcNow));
    }

    /// <summary>
    /// Gets the currently active giveaway
    /// </summary>
    public async Task<Giveaway?> GetActiveGiveawayAsync()
    {
        return await _dataService.GetActiveGiveawayAsync();
    }

    /// <summary>
    /// Gets all giveaways ordered by creation date
    /// </summary>
    public async Task<IEnumerable<Giveaway>> GetAllGiveawaysAsync()
    {
        return await _dataService.GetGiveawaysAsync();
    }

    /// <summary>
    /// Gets entries for a specific giveaway
    /// </summary>
    public async Task<IEnumerable<GiveawayEntry>> GetGiveawayEntriesAsync(Guid giveawayId)
    {
        return await _dataService.GetGiveawayEntriesAsync(giveawayId);
    }

    /// <summary>
    /// Stops the timed-intake flush loop. Queued-but-unflushed entries are lost
    /// (the queue is in-memory by design).
    /// </summary>
    public void Dispose()
    {
        lock (_flushGate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            try
            {
                _flushLoopCts?.Cancel();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to cancel giveaway flush loop during dispose");
            }
        }

        _settingsLock.Dispose();
        GC.SuppressFinalize(this);
    }

    // ------------------------------------------------------------------
    // Adaptive intake (Live/Timed)
    // ------------------------------------------------------------------

    /// <summary>
    /// Evaluates the entry rate against config thresholds and switches the
    /// intake mode (and flush loop) as needed.
    /// </summary>
    private async Task EvaluateIntakeModeAsync(Giveaway giveaway, int rate)
    {
        var settings = await GetSettingsAsync();

        if (_intakeMode == GiveawayIntakeMode.Live && rate >= settings.LiveMaxEntriesPerMinute)
        {
            _intakeMode = GiveawayIntakeMode.Timed;
            EnsureFlushLoopStarted();
            _metrics?.Increment("Giveaway", "mode_switches");
            _logger.LogWarning(
                "Giveaway {Name}: entry rate {Rate}/min exceeded {Max}/min — switching intake to Timed",
                giveaway.Name, rate, settings.LiveMaxEntriesPerMinute);

            await PublishSafeAsync(new GiveawayIntakeModeChangedEvent
            {
                GiveawayId = giveaway.Id,
                Mode = "Timed",
                EntriesPerMinute = rate,
                Source = "System"
            });
        }
        else if (_intakeMode == GiveawayIntakeMode.Timed && rate <= settings.TimedResumeEntriesPerMinute)
        {
            _intakeMode = GiveawayIntakeMode.Live;
            _logger.LogInformation(
                "Giveaway {Name}: entry rate {Rate}/min fell to {Resume}/min — switching intake to Live",
                giveaway.Name, rate, settings.TimedResumeEntriesPerMinute);

            await PublishSafeAsync(new GiveawayIntakeModeChangedEvent
            {
                GiveawayId = giveaway.Id,
                Mode = "Live",
                EntriesPerMinute = rate,
                Source = "System"
            });

            // Flush immediately on Timed→Live; the loop then exits (Live + empty).
            await DrainTimedQueueAsync(CancellationToken.None);
        }
    }

    /// <summary>
    /// Lazily starts the background flush loop (at most one at a time).
    /// </summary>
    private void EnsureFlushLoopStarted()
    {
        lock (_flushGate)
        {
            if (_flushLoopTask != null || _disposed)
            {
                return;
            }

            _flushLoopCts = new CancellationTokenSource();
            _flushLoopTask = Task.Run(() => RunFlushLoopAsync(_flushLoopCts.Token));
        }
    }

    /// <summary>
    /// Background loop that drains the timed-entry queue every second when the
    /// oldest queued entry is old enough or the queue exceeds the batch
    /// threshold. Exits cleanly when cancelled, disposed, or when the service
    /// is back in Live mode with an empty queue.
    /// </summary>
    private async Task RunFlushLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                if (_pendingEntries.IsEmpty)
                {
                    if (_intakeMode == GiveawayIntakeMode.Live)
                    {
                        break; // Idle in Live mode — stop the loop; it restarts lazily.
                    }

                    continue;
                }

                int flushSeconds = GetCachedSettings().TimedFlushSeconds;
                if (_pendingEntries.TryPeek(out var oldest)
                    && (DateTime.UtcNow - oldest.RequestedAtUtc >= TimeSpan.FromSeconds(flushSeconds)
                        || _pendingEntries.Count > TimedFlushBatchThreshold))
                {
                    await DrainTimedQueueAsync(cancellationToken);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Clean shutdown (dispose or loop teardown) — not an error.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Giveaway timed-intake flush loop stopped unexpectedly");
        }
        finally
        {
            lock (_flushGate)
            {
                _flushLoopTask = null;
                _flushLoopCts?.Dispose();
                _flushLoopCts = null;
            }
        }
    }

    /// <summary>
    /// Drains the entire timed-entry queue: dedupes per (username, platform)
    /// keeping the latest request, persists each entry, recomputes giveaway
    /// totals once per giveaway and publishes one aggregated entry event.
    /// </summary>
    private async Task DrainTimedQueueAsync(CancellationToken cancellationToken)
    {
        if (_pendingEntries.IsEmpty)
        {
            return;
        }

        var batch = new List<EntryRequest>();
        while (_pendingEntries.TryDequeue(out var request))
        {
            batch.Add(request);
        }

        if (batch.Count == 0)
        {
            return;
        }

        _metrics?.Increment("Giveaway", "entries_batched", batch.Count);
        _logger.LogInformation("Flushing {Count} queued giveaway entr(y/ies) from timed intake", batch.Count);

        foreach (var group in batch.GroupBy(r => r.GiveawayId))
        {
            EntryRequest? lastPersisted = null;
            int persisted = 0;

            foreach (var request in DedupeLatest(group))
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    // Re-queue the remainder; nothing is lost in-memory.
                    _pendingEntries.Enqueue(request);
                    continue;
                }

                try
                {
                    var (persistedThis, _, _) = await PersistEntryAsync(request);
                    if (persistedThis)
                    {
                        lastPersisted = request;
                        persisted++;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Failed to persist queued giveaway entry for {Username} ({Platform})",
                        request.Username, request.Platform);
                }
            }

            if (persisted == 0 || lastPersisted == null)
            {
                continue;
            }

            try
            {
                var giveaway = await _dataService.GetGiveawayAsync(group.Key);
                if (giveaway != null)
                {
                    giveaway.TotalEntries = await _dataService.GetGiveawayEntryCountAsync(giveaway.Id);
                    giveaway.UniqueParticipants = await _dataService.GetGiveawayUniqueParticipantsAsync(giveaway.Id);
                    await _dataService.SaveGiveawayAsync(giveaway);

                    // One aggregated event per flush: last username/platform, updated totals.
                    await PublishSafeAsync(new GiveawayEntryEvent
                    {
                        GiveawayId = giveaway.Id,
                        Username = lastPersisted.Username,
                        Platform = lastPersisted.Platform,
                        TotalEntries = giveaway.TotalEntries,
                        UniqueParticipants = giveaway.UniqueParticipants,
                        IsTimed = true,
                        Source = lastPersisted.Platform
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to update totals after timed-intake flush for giveaway {Id}", group.Key);
            }
        }
    }

    /// <summary>
    /// Dedupes a flush batch per (username, platform), keeping the latest
    /// request's flags.
    /// </summary>
    private static IEnumerable<EntryRequest> DedupeLatest(IEnumerable<EntryRequest> requests)
    {
        return requests
            .GroupBy(
                r => $"{r.Username}:{r.Platform}",
                r => r,
                StringComparer.OrdinalIgnoreCase)
            .Select(g => g.Last());
    }

    /// <summary>
    /// Single-entry persist path: recomputes entry count (legacy sub bonus +
    /// odds boost), saves the entry and returns the resulting entry count.
    /// </summary>
    private async Task<(bool Persisted, int EntryCount, string Breakdown)> PersistEntryAsync(EntryRequest request)
    {
        // Re-check duplicates at persist time (the queue may hold stale requests).
        var existing = await _dataService.GetGiveawayEntryAsync(request.GiveawayId, request.Username, request.Platform);
        if (existing != null)
        {
            return (false, 0, string.Empty);
        }

        var giveaway = await _dataService.GetGiveawayAsync(request.GiveawayId);
        if (giveaway == null)
        {
            return (false, 0, string.Empty);
        }

        // Calculate entry count (base + subscriber bonus + odds boost).
        // When the odds boost is enabled, the odds path below governs sub bonuses
        // via the tier ladder only (Calculate ignores the flat bonus; a current
        // subscriber with no recorded tier earns the tier-1 bonus) — applying the
        // legacy flat bonus here as well would double-count subscribers.
        int entryCount = 1;
        if (giveaway.SubscriberBonusEnabled && !giveaway.OddsBoostEnabled && request.IsSubscriber)
        {
            entryCount += giveaway.SubscriberBonusEntries;
        }

        string breakdown = string.Empty;
        if (giveaway.OddsBoostEnabled)
        {
            var settings = await GetSettingsAsync();

            string? viewerId = await _linkedAccounts.GetViewerIdAsync(request.Username, request.Platform);
            int bestTier = viewerId != null
                ? await _viewerStatus.GetBestSubTierAcrossLinkedAsync(viewerId)
                : 0;
            bool followerAnywhere = viewerId != null
                && await _viewerStatus.IsFollowerAnywhereAsync(viewerId);
            int linkedCount = viewerId != null
                ? await _viewerStatus.GetLinkedPlatformCountAsync(viewerId)
                : 1;

            var odds = GiveawayOdds.Calculate(
                request.IsSubscriber,
                giveaway.SubscriberBonusEntries, // ignored on the odds path — tier ladder governs sub rewards
                bestTier,
                settings.Tier1Bonus, settings.Tier2Bonus, settings.Tier3Bonus,
                followerAnywhere, settings.FollowerBonus,
                linkedCount, settings.LinkedPlatformBonus, settings.LinkedPlatformCap);

            entryCount += odds.BonusEntries;
            breakdown = odds.Breakdown;
        }

        var entry = new GiveawayEntry
        {
            GiveawayId = request.GiveawayId,
            Username = request.Username,
            Platform = request.Platform,
            PlatformUserId = request.PlatformUserId,
            EntryCount = entryCount,
            IsSubscriber = request.IsSubscriber,
            IsFollower = request.IsFollower,
            WatchtimeMinutes = request.WatchtimeMinutes,
            Message = CombineMessageWithBreakdown(request.Message, breakdown)
        };

        await _dataService.SaveGiveawayEntryAsync(entry);

        _logger.LogInformation(
            "User entered giveaway: {Username} ({Platform}) - {EntryCount} entries{Odds}",
            request.Username, request.Platform, entryCount,
            breakdown.Length > 0 ? $" [{breakdown}]" : string.Empty);

        return (true, entryCount, breakdown);
    }

    /// <summary>Prefixes the stored message with the odds breakdown ("breakdown; message").</summary>
    private static string? CombineMessageWithBreakdown(string? message, string breakdown)
    {
        if (string.IsNullOrEmpty(breakdown))
        {
            return message;
        }

        return string.IsNullOrEmpty(message) ? breakdown : $"{breakdown}; {message}";
    }

    // ------------------------------------------------------------------
    // Config (30s cache)
    // ------------------------------------------------------------------

    private async Task<OddsSettings> GetSettingsAsync()
    {
        if (DateTime.UtcNow - _settingsLoadedAtUtc < SettingsCacheTtl)
        {
            return _cachedSettings;
        }

        await _settingsLock.WaitAsync();
        try
        {
            if (DateTime.UtcNow - _settingsLoadedAtUtc < SettingsCacheTtl)
            {
                return _cachedSettings;
            }

            _cachedSettings = new OddsSettings(
                Tier1Bonus: ParseInt(await _dataService.GetConfigValueAsync("Odds.Tier1BonusEntries"), 1),
                Tier2Bonus: ParseInt(await _dataService.GetConfigValueAsync("Odds.Tier2BonusEntries"), 2),
                Tier3Bonus: ParseInt(await _dataService.GetConfigValueAsync("Odds.Tier3BonusEntries"), 3),
                FollowerBonus: ParseInt(await _dataService.GetConfigValueAsync("Odds.FollowerBonusEntries"), 1),
                LinkedPlatformBonus: ParseInt(await _dataService.GetConfigValueAsync("Odds.LinkedPlatformBonusEntries"), 1),
                LinkedPlatformCap: ParseInt(await _dataService.GetConfigValueAsync("Odds.LinkedPlatformBonusCap"), 3),
                LiveMaxEntriesPerMinute: ParseInt(await _dataService.GetConfigValueAsync("Giveaway.LiveMaxEntriesPerMinute"), 120),
                TimedResumeEntriesPerMinute: ParseInt(await _dataService.GetConfigValueAsync("Giveaway.TimedResumeEntriesPerMinute"), 60),
                TimedFlushSeconds: ParseInt(await _dataService.GetConfigValueAsync("Giveaway.TimedFlushSeconds"), 10));
            _settingsLoadedAtUtc = DateTime.UtcNow;

            return _cachedSettings;
        }
        finally
        {
            _settingsLock.Release();
        }
    }

    /// <summary>Synchronous snapshot for the flush loop thread (defaults until first load).</summary>
    private OddsSettings GetCachedSettings() => _cachedSettings;

    private static int ParseInt(string? raw, int fallback)
    {
        return int.TryParse(raw, out var value) ? value : fallback;
    }

    /// <summary>Publishes an event, swallowing and logging any bus failure.</summary>
    private async Task PublishSafeAsync<TEvent>(TEvent evt) where TEvent : IEvent
    {
        try
        {
            await _eventBus.PublishAsync(evt);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish {EventType}", typeof(TEvent).Name);
        }
    }

    /// <summary>One queued keyword entry awaiting a timed-intake flush.</summary>
    private sealed record EntryRequest(
        Guid GiveawayId,
        string Username,
        string Platform,
        string? PlatformUserId,
        bool IsSubscriber,
        bool IsFollower,
        int WatchtimeMinutes,
        string? Message,
        DateTime RequestedAtUtc);

    /// <summary>Cached config snapshot for odds and intake thresholds.</summary>
    private sealed record OddsSettings(
        int Tier1Bonus,
        int Tier2Bonus,
        int Tier3Bonus,
        int FollowerBonus,
        int LinkedPlatformBonus,
        int LinkedPlatformCap,
        int LiveMaxEntriesPerMinute,
        int TimedResumeEntriesPerMinute,
        int TimedFlushSeconds)
    {
        public static OddsSettings Default { get; } = new(1, 2, 3, 1, 1, 3, 120, 60, 10);
    }
}
