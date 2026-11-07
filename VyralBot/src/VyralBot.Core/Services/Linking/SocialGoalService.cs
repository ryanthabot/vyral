using Microsoft.Extensions.Logging;
using VyralBot.Core.Services.Currency;
using VyralBot.Core.Services.EventBus;
using VyralBot.Core.Services.EventBus.Events;
using VyralBot.Data;
using VyralBot.Data.Entities;

namespace VyralBot.Core.Services.Linking;

/// <summary>
/// Static evaluator deciding whether a SocialGoal's completion condition is met
/// from aggregate viewer counters (public test target).
/// </summary>
public static class GoalEvaluator
{
    /// <summary>
    /// Returns true when the goal is satisfied by the supplied aggregates.
    /// Supported goal types: "LinkedPlatforms", "FollowPlatforms",
    /// "SubAnyPlatform", "SubTier". Unknown goal types are never met.
    /// </summary>
    public static bool IsMet(SocialGoal goal, int linkedPlatformCount, int followedPlatformCount, int subbedPlatformCount, int bestSubTier)
    {
        var type = goal.GoalType?.Trim() ?? string.Empty;

        if (type.Equals("LinkedPlatforms", StringComparison.OrdinalIgnoreCase))
        {
            return linkedPlatformCount >= goal.TargetCount;
        }

        if (type.Equals("FollowPlatforms", StringComparison.OrdinalIgnoreCase))
        {
            return followedPlatformCount >= goal.TargetCount;
        }

        if (type.Equals("SubAnyPlatform", StringComparison.OrdinalIgnoreCase))
        {
            return subbedPlatformCount >= goal.TargetCount;
        }

        if (type.Equals("SubTier", StringComparison.OrdinalIgnoreCase))
        {
            return bestSubTier >= goal.TargetCount;
        }

        return false;
    }
}

/// <summary>
/// Default ISocialGoalService: seeds default goals on first run, evaluates
/// viewers against goal conditions and grants rewards (currency directly;
/// badge/multiplier goals are declarative only).
/// </summary>
public class SocialGoalService : ISocialGoalService
{
    private const string CurrencyRewardType = "Currency";
    private const string MultiplierRewardType = "Multiplier";
    private const string BadgeRewardType = "Badge";
    private const string BonusTransactionType = "Bonus";

    private static readonly SemaphoreSlim SeedLock = new(1, 1);

    private const string GoalsEnabledKey = "Goals.Enabled";
    private static readonly TimeSpan SettingsCacheTtl = TimeSpan.FromSeconds(30);

    private readonly ILogger<SocialGoalService> _logger;
    private readonly IDataService _dataService;
    private readonly IViewerStatusService _viewerStatus;
    private readonly ICurrencyService _currencyService;
    private readonly ILinkedAccountService _linkedAccounts;
    private readonly IEventBus _eventBus;

    // 30s-cached "Goals.Enabled" config (default true): evaluation gate only —
    // GetGoalsAsync intentionally stays ungated so the admin UI always lists goals.
    private readonly SemaphoreSlim _enabledLock = new(1, 1);
    private bool _goalsEnabled = true;
    private DateTime _goalsEnabledLoadedAtUtc = DateTime.MinValue;

    // Subscription id for the ViewerStatusUpdatedEvent handler. SubscribeAsync
    // returns Task<Guid> and constructors cannot await, so the Guid becomes
    // available once the bus registers the handler; the task is kept so a
    // cleanup host can unsubscribe via Unsubscribe(await _statusSubscriptionTask).
    private readonly Task<Guid> _statusSubscriptionTask;

    // Same pattern for AccountsLinkedEvent: linking platforms changes
    // linkedPlatformCount, which drives the "LinkedPlatforms" goal type
    // (e.g. the seeded "Bridge Builder") — without this, those goals would
    // only be evaluated on the viewer's NEXT sub/follow status update.
    private readonly Task<Guid> _linkedSubscriptionTask;

    public SocialGoalService(
        ILogger<SocialGoalService> logger,
        IDataService dataService,
        IViewerStatusService viewerStatus,
        ICurrencyService currencyService,
        ILinkedAccountService linkedAccounts,
        IEventBus eventBus)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
        _viewerStatus = viewerStatus ?? throw new ArgumentNullException(nameof(viewerStatus));
        _currencyService = currencyService ?? throw new ArgumentNullException(nameof(currencyService));
        _linkedAccounts = linkedAccounts ?? throw new ArgumentNullException(nameof(linkedAccounts));
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));

        // ViewerStatusService publishes ViewerStatusUpdatedEvent on every
        // RecordSubscription/RecordFollow — evaluate this viewer's goals then.
        // No recursion: EvaluateViewerAsync only ever publishes
        // ViewerGoalCompletedEvent, which this handler does not subscribe to.
        _statusSubscriptionTask = _eventBus.SubscribeAsync<ViewerStatusUpdatedEvent>(OnViewerStatusUpdatedAsync);

        // LinkCodeService publishes AccountsLinkedEvent after every link/delink
        // merge — evaluate the merged identity's goals right away. The event
        // carries the ViewerId directly, so no re-resolution is needed.
        _linkedSubscriptionTask = _eventBus.SubscribeAsync<AccountsLinkedEvent>(OnAccountsLinkedAsync);
    }

    /// <summary>
    /// Evaluates goals for the viewer identity behind a link/delink merge.
    /// Delinks can only lower counts and completions are permanent rows, so
    /// re-evaluating after a delink is a harmless no-op. Exceptions are
    /// contained so they never propagate back into the event bus.
    /// </summary>
    private async Task OnAccountsLinkedAsync(AccountsLinkedEvent evt)
    {
        try
        {
            await EvaluateViewerAsync(evt.ViewerId, evt.Username, evt.Platform);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to evaluate social goals after link event for {Username} ({Platform})",
                evt.Username, evt.Platform);
        }
    }

    /// <summary>
    /// Evaluates goals for the viewer behind a sub/follow status update.
    /// Unlinked accounts (no unified viewer id yet) are skipped; exceptions are
    /// contained so they never propagate back into the event bus.
    /// </summary>
    private async Task OnViewerStatusUpdatedAsync(ViewerStatusUpdatedEvent evt)
    {
        try
        {
            var viewerId = await _linkedAccounts.GetViewerIdAsync(evt.Username, evt.Platform);
            if (viewerId == null)
            {
                return;
            }

            await EvaluateViewerAsync(viewerId, evt.Username, evt.Platform);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to evaluate social goals after status update for {Username} ({Platform})",
                evt.Username, evt.Platform);
        }
    }

    /// <summary>
    /// 30s-cached read of the "Goals.Enabled" config key (default true when
    /// unset or unparsable), matching the other settings caches.
    /// </summary>
    private async Task<bool> IsGoalsEnabledAsync()
    {
        await _enabledLock.WaitAsync();
        try
        {
            if (DateTime.UtcNow - _goalsEnabledLoadedAtUtc < SettingsCacheTtl)
            {
                return _goalsEnabled;
            }

            var raw = await _dataService.GetConfigValueAsync(GoalsEnabledKey);
            _goalsEnabled = !bool.TryParse(raw?.Trim(), out var enabled) || enabled;
            _goalsEnabledLoadedAtUtc = DateTime.UtcNow;

            return _goalsEnabled;
        }
        finally
        {
            _enabledLock.Release();
        }
    }

    public async Task SeedDefaultsAsync()
    {
        await SeedLock.WaitAsync();
        try
        {
            var existing = await _dataService.GetSocialGoalsAsync();
            if (existing.Any())
            {
                // Idempotent: never touch the table once any goal exists.
                return;
            }

            var defaults = new[]
            {
                new SocialGoal
                {
                    Name = "Bridge Builder",
                    Description = "Link 2 platforms to your viewer identity.",
                    GoalType = "LinkedPlatforms",
                    TargetCount = 2,
                    RewardType = CurrencyRewardType,
                    RewardValue = 500,
                    SortOrder = 1
                },
                new SocialGoal
                {
                    Name = "Everywhere At Once",
                    Description = "Link 4 platforms to your viewer identity.",
                    GoalType = "LinkedPlatforms",
                    TargetCount = 4,
                    RewardType = BadgeRewardType,
                    RewardValue = 0,
                    SortOrder = 2
                },
                new SocialGoal
                {
                    Name = "Loyal Follower",
                    Description = "Follow the stream on 3 platforms.",
                    GoalType = "FollowPlatforms",
                    TargetCount = 3,
                    RewardType = CurrencyRewardType,
                    RewardValue = 250,
                    SortOrder = 3
                },
                new SocialGoal
                {
                    Name = "Tier 2 Supporter",
                    Description = "Reach Tier 2 subscriber on any linked platform.",
                    GoalType = "SubTier",
                    TargetCount = 2,
                    RewardType = CurrencyRewardType,
                    RewardValue = 1000,
                    SortOrder = 4
                },
            };

            foreach (var goal in defaults)
            {
                await _dataService.SaveSocialGoalAsync(goal);
            }

            _logger.LogInformation("Seeded {Count} default social goals", defaults.Length);
        }
        finally
        {
            SeedLock.Release();
        }
    }

    public async Task EvaluateViewerAsync(string viewerId, string username, string platform)
    {
        // Master gate: the Viewer Hub "Goals.Enabled" toggle (default true)
        // stops new completions/rewards. GetGoalsAsync stays ungated on purpose.
        if (!await IsGoalsEnabledAsync())
        {
            return;
        }

        var linkedPlatforms = (await _dataService.GetLinkedAccountsByViewerIdAsync(viewerId))
            .Select(a => a.Platform)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var statuses = (await _dataService.GetViewerStatusesByViewerIdAsync(viewerId)).ToList();
        var bestSubTierUnfiltered = await _viewerStatus.GetBestSubTierAcrossLinkedAsync(viewerId);

        var goals = await _dataService.GetSocialGoalsAsync();
        foreach (var goal in goals.Where(g => g.IsEnabled))
        {
            // Idempotent: completions are unique per GoalId+ViewerId.
            var completion = await _dataService.GetViewerGoalCompletionAsync(goal.Id, viewerId);
            if (completion != null)
            {
                continue;
            }

            // RequiredPlatforms restricts which platforms count toward THIS goal
            // ("All" or empty → every platform counts).
            var required = ParseRequiredPlatforms(goal.RequiredPlatforms);

            var linkedPlatformCount = required is null
                ? linkedPlatforms.Count
                : linkedPlatforms.Count(required.Contains);

            var followedPlatformCount = CountStatusPlatforms(statuses, s => s.IsFollower, required);
            var subbedPlatformCount = CountStatusPlatforms(statuses, s => s.IsSubscriber, required);

            var bestSubTier = required is null
                ? bestSubTierUnfiltered
                : statuses
                    .Where(s => !string.IsNullOrEmpty(s.ViewerId))
                    .Where(s => s.Platform != null && required.Contains(s.Platform))
                    .Select(s => s.SubTier)
                    .DefaultIfEmpty(0)
                    .Max();

            if (!GoalEvaluator.IsMet(goal, linkedPlatformCount, followedPlatformCount, subbedPlatformCount, bestSubTier))
            {
                continue;
            }

            await _dataService.SaveViewerGoalCompletionAsync(new ViewerGoalCompletion
            {
                GoalId = goal.Id,
                ViewerId = viewerId,
                Username = username,
                Platform = platform,
                RewardApplied = true
            });

            if (goal.RewardType.Equals(CurrencyRewardType, StringComparison.OrdinalIgnoreCase) && goal.RewardValue > 0)
            {
                await _currencyService.AddCurrencyAsync(
                    username, platform, (int)goal.RewardValue, "Vyral goal: " + goal.Name, BonusTransactionType);
            }

            // Badge/Multiplier rewards are declarative — no direct action here.

            _logger.LogInformation("Viewer {ViewerId} completed social goal {Goal}", viewerId, goal.Name);

            try
            {
                await _eventBus.PublishAsync(new ViewerGoalCompletedEvent
                {
                    Source = platform,
                    ViewerId = viewerId,
                    Username = username,
                    Platform = platform,
                    GoalName = goal.Name,
                    RewardDescription = DescribeReward(goal)
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to publish ViewerGoalCompletedEvent for goal {Goal} (viewer {ViewerId})",
                    goal.Name, viewerId);
            }
        }
    }

    /// <summary>
    /// Parses a goal's <see cref="SocialGoal.RequiredPlatforms"/> csv
    /// ("All", empty or whitespace → null = no platform restriction).
    /// Comparisons are case-insensitive.
    /// </summary>
    private static HashSet<string>? ParseRequiredPlatforms(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv))
        {
            return null;
        }

        var tokens = csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0 || tokens.Any(t => t.Equals("All", StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        return tokens.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Distinct platforms among the statuses matching <paramref name="predicate"/>,
    /// restricted to <paramref name="required"/> when it is not null.
    /// </summary>
    private static int CountStatusPlatforms(
        IEnumerable<ViewerPlatformStatus> statuses,
        Func<ViewerPlatformStatus, bool> predicate,
        HashSet<string>? required)
    {
        return statuses
            .Where(predicate)
            .Select(s => s.Platform)
            .Where(p => !string.IsNullOrWhiteSpace(p) && (required is null || required.Contains(p)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
    }

    public Task<IEnumerable<SocialGoal>> GetGoalsAsync()
    {
        return _dataService.GetSocialGoalsAsync();
    }

    public async Task SaveGoalAsync(SocialGoal goal)
    {
        goal.UpdatedAt = DateTime.UtcNow;
        await _dataService.SaveSocialGoalAsync(goal);
    }

    public Task DeleteGoalAsync(Guid id)
    {
        return _dataService.DeleteSocialGoalAsync(id);
    }

    public Task<int> GetCompletionCountAsync(Guid goalId)
    {
        return _dataService.GetGoalCompletionCountAsync(goalId);
    }

    private static string DescribeReward(SocialGoal goal)
    {
        if (goal.RewardType.Equals(CurrencyRewardType, StringComparison.OrdinalIgnoreCase))
        {
            return $"{(int)goal.RewardValue} points";
        }

        if (goal.RewardType.Equals(MultiplierRewardType, StringComparison.OrdinalIgnoreCase))
        {
            return $"{goal.RewardValue:0.##}x earnings multiplier";
        }

        if (goal.RewardType.Equals(BadgeRewardType, StringComparison.OrdinalIgnoreCase))
        {
            return "a new badge";
        }

        return goal.RewardType;
    }
}
