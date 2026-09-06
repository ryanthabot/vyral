using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using VyralBot.Core.Services.EventBus;
using VyralBot.Core.Services.EventBus.Events;
using VyralBot.Data;
using VyralBot.Data.Entities;

namespace VyralBot.Core.Services.Linking;

/// <summary>
/// Static bonus ladder resolving the multi-platform watch bonus for a
/// linked-platform count (public test target).
/// </summary>
public static class MultiPlatformBonusLadder
{
    /// <summary>
    /// Fewer than 2 linked platforms → 1.0 (no bonus);
    /// 2..4 linked platforms → the respective configured bonus;
    /// 5 or more → <paramref name="b5"/>.
    /// </summary>
    public static double Resolve(int platformCount, double b2, double b3, double b4, double b5)
    {
        if (platformCount < 2)
        {
            return 1.0;
        }

        if (platformCount == 2)
        {
            return b2;
        }

        if (platformCount == 3)
        {
            return b3;
        }

        if (platformCount == 4)
        {
            return b4;
        }

        return b5;
    }
}

/// <summary>
/// Default IMultiPlatformBonusService: resolves the multi-platform earnings
/// multiplier from config (Off/OptIn/Auto), the linked-platform count and the
/// bonus ladder, optionally stacked with completed "Multiplier" social goals.
/// Config reads are cached for 30 seconds; the final multiplier is cached per
/// username:platform for 5 minutes.
/// </summary>
public class MultiPlatformBonusService : IMultiPlatformBonusService
{
    private const string ModeKey = "MultiPlatform.Mode";
    private const string Bonus2Key = "MultiPlatform.Bonus2";
    private const string ActiveWatchingBonusKey = "MultiPlatform.ActiveWatchingBonus";
    private const double ActiveWatchingBonusDefault = 1.25;
    // Watchtime marks a viewer inactive after ~5 idle minutes; allow slack.
    private static readonly TimeSpan ActiveWatchingWindow = TimeSpan.FromMinutes(6);
    private const string Bonus3Key = "MultiPlatform.Bonus3";
    private const string Bonus4Key = "MultiPlatform.Bonus4";
    private const string Bonus5Key = "MultiPlatform.Bonus5";

    private const string ModeOff = "Off";
    private const string ModeOptIn = "OptIn";
    private const string ModeAuto = "Auto";

    private const string MultiplierRewardType = "Multiplier";

    private static readonly TimeSpan SettingsCacheTtl = TimeSpan.FromSeconds(30);
    // Live-presence aware: the active-watching factor changes minute to
    // minute, so the cached result lives only briefly.
    private static readonly TimeSpan ResultCacheTtl = TimeSpan.FromSeconds(60);

    private sealed record Settings(string Mode, double Bonus2, double Bonus3, double Bonus4, double Bonus5, double ActiveWatchingBonus);

    private sealed record CachedMultiplier(double Value, DateTime CachedAtUtc);

    private readonly ILogger<MultiPlatformBonusService> _logger;
    private readonly IDataService _dataService;
    private readonly IEventBus _eventBus;

    // viewerId → highest completed "Multiplier"-goal reward value.
    // LIMITATION: this map is in-memory only and is NOT hydrated from the
    // database on start — Multiplier-goal rewards therefore only apply from
    // process start onward.
    private readonly ConcurrentDictionary<string, double> _goalMultipliers = new();

    // "username:platform" → last resolved multiplier (5-minute TTL).
    private readonly ConcurrentDictionary<string, CachedMultiplier> _resultCache = new();

    private readonly SemaphoreSlim _settingsLock = new(1, 1);
    private Settings _settings = new(ModeOff, 1.25, 1.5, 2.0, 2.5, ActiveWatchingBonusDefault);
    private DateTime _settingsLoadedAtUtc = DateTime.MinValue;

    // Subscription id for the ViewerGoalCompletedEvent handler. SubscribeAsync
    // returns Task<Guid> and constructors cannot await, so the Guid becomes
    // available once the bus registers the handler; the task is kept so a
    // cleanup host can unsubscribe via Unsubscribe(await _goalSubscriptionTask).
    private readonly Task<Guid> _goalSubscriptionTask;

    public MultiPlatformBonusService(
        ILogger<MultiPlatformBonusService> logger,
        IDataService dataService,
        IEventBus eventBus)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));

        // SocialGoalService publishes ViewerGoalCompletedEvent when a "Multiplier"
        // goal completes; track the max reward value per viewer from here on.
        _goalSubscriptionTask = _eventBus.SubscribeAsync<ViewerGoalCompletedEvent>(OnViewerGoalCompletedAsync);
    }

    public async Task<double> GetMultiplierAsync(string username, string platform)
    {
        var cacheKey = $"{username}:{platform}";
        if (_resultCache.TryGetValue(cacheKey, out var cached) &&
            DateTime.UtcNow - cached.CachedAtUtc < ResultCacheTtl)
        {
            return cached.Value;
        }

        var multiplier = await ResolveMultiplierAsync(username, platform);
        _resultCache[cacheKey] = new CachedMultiplier(multiplier, DateTime.UtcNow);
        return multiplier;
    }

    public async Task SetOptInAsync(string username, string platform, bool optedIn)
    {
        var viewerId = await _dataService.GetViewerIdAsync(username, platform);
        if (viewerId == null)
        {
            _logger.LogWarning("Cannot store multi-platform opt-in for unlinked account {Username} ({Platform})",
                username, platform);
            return;
        }

        var preferences = await _dataService.GetViewerPreferencesAsync(viewerId);
        preferences ??= new ViewerPreferences { ViewerId = viewerId };

        if (preferences.MultiPlatformBonusOptIn != optedIn)
        {
            preferences.MultiPlatformBonusOptIn = optedIn;
            preferences.UpdatedAt = DateTime.UtcNow;
            await _dataService.SaveViewerPreferencesAsync(preferences);
            _logger.LogInformation("Viewer {ViewerId} multi-platform opt-in set to {OptedIn}", viewerId, optedIn);
        }

        // The previously cached multiplier may no longer reflect the opt-in state.
        _resultCache.TryRemove($"{username}:{platform}", out _);
    }

    public async Task<bool> GetOptInAsync(string username, string platform)
    {
        var viewerId = await _dataService.GetViewerIdAsync(username, platform);
        if (viewerId == null)
        {
            return false;
        }

        var preferences = await _dataService.GetViewerPreferencesAsync(viewerId);
        return preferences?.MultiPlatformBonusOptIn ?? false;
    }

    private async Task<double> ResolveMultiplierAsync(string username, string platform)
    {
        var settings = await GetSettingsAsync();
        if (settings.Mode.Equals(ModeOff, StringComparison.OrdinalIgnoreCase))
        {
            return 1.0;
        }

        var viewerId = await _dataService.GetViewerIdAsync(username, platform);
        if (viewerId == null)
        {
            return 1.0;
        }

        if (settings.Mode.Equals(ModeOptIn, StringComparison.OrdinalIgnoreCase) &&
            !await GetOptInAsync(username, platform))
        {
            return 1.0;
        }

        var accounts = await _dataService.GetLinkedAccountsByViewerIdAsync(viewerId);
        var platformCount = accounts
            .Select(a => a.Platform)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        var multiplier = MultiPlatformBonusLadder.Resolve(
            platformCount, settings.Bonus2, settings.Bonus3, settings.Bonus4, settings.Bonus5);

        // ACTIVE-WATCHING BONUS: linked is good, actually watching is better.
        // Count the viewer's accounts whose watchtime row is still marked
        // active (chat/watch presence within the idle window).
        var activeWatching = 0;
        foreach (var account in accounts)
        {
            UserWatchtime? watchtime;
            try
            {
                watchtime = await _dataService.GetUserWatchtimeAsync(account.Username, account.Platform);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Active-watch check failed for {Username}/{Platform}", account.Username, account.Platform);
                continue;
            }

            if (watchtime is { IsActive: true }
                && DateTime.UtcNow - watchtime.LastSeenAt <= ActiveWatchingWindow)
            {
                activeWatching++;
            }
        }

        if (activeWatching >= 2 && settings.ActiveWatchingBonus != 1.0)
        {
            multiplier *= settings.ActiveWatchingBonus;
        }

        // Completed "Multiplier" social goals stack on top of the ladder bonus.
        if (_goalMultipliers.TryGetValue(viewerId, out var goalMultiplier))
        {
            multiplier *= goalMultiplier;
        }

        return multiplier;
    }

    private async Task OnViewerGoalCompletedAsync(ViewerGoalCompletedEvent evt)
    {
        try
        {
            var goals = await _dataService.GetSocialGoalsAsync();
            var goal = goals.FirstOrDefault(g =>
                g.RewardType.Equals(MultiplierRewardType, StringComparison.OrdinalIgnoreCase) &&
                g.Name.Equals(evt.GoalName, StringComparison.OrdinalIgnoreCase));

            if (goal == null)
            {
                return;
            }

            _goalMultipliers.AddOrUpdate(
                evt.ViewerId,
                goal.RewardValue,
                (_, existing) => Math.Max(existing, goal.RewardValue));

            _logger.LogInformation("Viewer {ViewerId} earned a {Value}x goal multiplier from goal {Goal}",
                evt.ViewerId, goal.RewardValue, evt.GoalName);

            // Cached multipliers may now be stale for this viewer.
            _resultCache.Clear();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to apply goal multiplier for viewer {ViewerId}", evt.ViewerId);
        }
    }

    private async Task<Settings> GetSettingsAsync()
    {
        await _settingsLock.WaitAsync();
        try
        {
            if (DateTime.UtcNow - _settingsLoadedAtUtc < SettingsCacheTtl)
            {
                return _settings;
            }

            var activeRaw = (await _dataService.GetConfigValueAsync(ActiveWatchingBonusKey))?.Trim();
            var activeBonus = ActiveWatchingBonusDefault;
            if (double.TryParse(activeRaw, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var parsedActive))
            {
                activeBonus = parsedActive;
            }

            var modeRaw = (await _dataService.GetConfigValueAsync(ModeKey))?.Trim();
            var mode = ModeOff;
            if (string.Equals(modeRaw, ModeOptIn, StringComparison.OrdinalIgnoreCase))
            {
                mode = ModeOptIn;
            }
            else if (string.Equals(modeRaw, ModeAuto, StringComparison.OrdinalIgnoreCase))
            {
                mode = ModeAuto;
            }

            _settings = new Settings(
                mode,
                ParseBonus(await _dataService.GetConfigValueAsync(Bonus2Key), 1.25),
                ParseBonus(await _dataService.GetConfigValueAsync(Bonus3Key), 1.5),
                ParseBonus(await _dataService.GetConfigValueAsync(Bonus4Key), 2.0),
                ParseBonus(await _dataService.GetConfigValueAsync(Bonus5Key), 2.5),
                activeBonus);
            _settingsLoadedAtUtc = DateTime.UtcNow;

            return _settings;
        }
        finally
        {
            _settingsLock.Release();
        }
    }

    private static double ParseBonus(string? raw, double fallback)
    {
        return double.TryParse(raw, out var value) && value > 0 ? value : fallback;
    }
}
