using Microsoft.Extensions.Logging;
using VyralBot.Core.Services.Currency;
using VyralBot.Core.Services.Statistics;
using VyralBot.Data;
using VyralBot.Data.Entities;

namespace VyralBot.Core.Services.Linking;

/// <summary>
/// Default IViewerProfileService: aggregates linked accounts, per-platform
/// status, economy, watchtime/streaks, Vyral goals, giveaway entries, perks and
/// event statistics for one viewer identity into an immutable snapshot.
/// All I/O happens through injected services so it can run on a background
/// thread while the UI waits on the returned graph.
/// </summary>
public class ViewerProfileService : IViewerProfileService
{
    private const int RecentTransactionLimit = 25;
    private const int ModActionLimit = 100;

    private readonly ILogger<ViewerProfileService> _logger;
    private readonly IDataService _dataService;
    private readonly ILinkedAccountService _linkedAccounts;
    private readonly ISocialGoalService _socialGoals;
    private readonly IStatisticsService _statistics;

    public ViewerProfileService(
        ILogger<ViewerProfileService> logger,
        IDataService dataService,
        ILinkedAccountService linkedAccounts,
        ISocialGoalService socialGoals,
        IStatisticsService statistics)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
        _linkedAccounts = linkedAccounts ?? throw new ArgumentNullException(nameof(linkedAccounts));
        _socialGoals = socialGoals ?? throw new ArgumentNullException(nameof(socialGoals));
        _statistics = statistics ?? throw new ArgumentNullException(nameof(statistics));
    }

    public async Task<ViewerProfileSnapshot> GetProfileAsync(string username, string platform)
    {
        var requestedUsername = (username ?? string.Empty).Trim();
        var requestedPlatform = (platform ?? string.Empty).Trim();

        try
        {
            var viewerId = await _linkedAccounts.GetViewerIdAsync(requestedUsername, requestedPlatform);

            if (string.IsNullOrEmpty(viewerId))
            {
                // Not linked: snapshot with just the raw account info.
                return new ViewerProfileSnapshot
                {
                    RequestedUsername = requestedUsername,
                    RequestedPlatform = requestedPlatform,
                    ViewerId = null,
                    Accounts = new[]
                    {
                        new ViewerProfileAccountSnapshot
                        {
                            Username = requestedUsername,
                            Platform = requestedPlatform,
                            IsCurrent = true,
                        },
                    },
                };
            }

            // Group-wide lookups (one round trip each), shared with GetAccountDetailAsync.
            var accounts = (await _linkedAccounts.GetLinkedAccountsAsync(viewerId)).ToList();
            var (statuses, giveawayNames, platformEarnings) = await LoadViewerContextAsync(viewerId);
            var goalNames = (await _socialGoals.GetGoalsAsync())
                .GroupBy(g => g.Id)
                .ToDictionary(g => g.Key, g => g.First().Name);

            var accountSnapshots = new List<ViewerProfileAccountSnapshot>(accounts.Count);
            foreach (var account in accounts)
            {
                accountSnapshots.Add(await BuildAccountSnapshotAsync(
                    account,
                    isCurrent: SameAccount(account, requestedUsername, requestedPlatform),
                    statuses,
                    giveawayNames,
                    platformEarnings));
            }

            // Vyral goals completed by this viewer (newest first), joined with goal names.
            var completions = await _dataService.GetViewerGoalCompletionsByViewerIdAsync(viewerId);
            var goals = completions
                .OrderByDescending(c => c.CompletedAt)
                .Select(c => new ViewerProfileGoalSnapshot(
                    goalNames.TryGetValue(c.GoalId, out var name) ? name : "Goal",
                    c.CompletedAt,
                    c.Platform))
                .ToList();

            // Event statistics highlight for the requested username.
            var eventDetail = await _statistics.GetUserStatisticsAsync(requestedUsername);
            var eventsByType = eventDetail.EventsByType
                .OrderByDescending(kvp => kvp.Value)
                .ThenBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase)
                .Select(kvp => new ViewerProfileEventStatRow(kvp.Key, kvp.Value))
                .ToList();

            // Moderation audit rows for this viewer (already newest-first from the store).
            var modActions = (await _dataService.GetModActionsForViewerAsync(requestedUsername, viewerId, ModActionLimit))
                .ToList();
            var dmHistory = await LoadDmHistoryAsync(requestedUsername);

            _logger.LogInformation(
                "Viewer profile built for {Username} ({Platform}): viewer {ViewerId}, {Accounts} account(s), {Goals} goal(s), {Events} event(s), {ModActions} mod action(s), {Dms} dm(s)",
                requestedUsername, requestedPlatform, viewerId, accountSnapshots.Count, goals.Count, eventDetail.TotalEvents,
                modActions.Count, dmHistory.Count);

            return new ViewerProfileSnapshot
            {
                RequestedUsername = requestedUsername,
                RequestedPlatform = requestedPlatform,
                ViewerId = viewerId,
                Accounts = accountSnapshots,
                GoalsCompleted = goals,
                TotalEvents = eventDetail.TotalEvents,
                EventsByType = eventsByType,
                FirstEventAt = eventDetail.FirstEventDate,
                LastEventAt = eventDetail.LastEventDate,
                DmHistory = dmHistory,
                ModActions = modActions,
                PlatformEarnings = platformEarnings,
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error building viewer profile for {Username} ({Platform})",
                requestedUsername, requestedPlatform);
            throw;
        }
    }

    private async Task<ViewerProfileAccountSnapshot> BuildAccountSnapshotAsync(
        LinkedAccount account,
        bool isCurrent,
        List<ViewerPlatformStatus> statuses,
        IReadOnlyDictionary<Guid, string> giveawayNames,
        List<PlatformEarnings> platformEarnings)
    {
        var status = statuses.FirstOrDefault(s =>
            SameAccount(s.Username, account.Username, s.Platform, account.Platform));

        // Earnings are tracked per VIEWER; pick this account's platform row.
        var earnings = platformEarnings.FirstOrDefault(e =>
            SamePlatform(e.Platform, account.Platform));

        var currency = await _dataService.GetUserCurrencyAsync(account.Username, account.Platform);

        var transactions = (await _dataService.GetCurrencyTransactionsAsync(
                account.Username, account.Platform, RecentTransactionLimit))
            .OrderByDescending(t => t.Timestamp)
            .ToList();

        var watchtime = await _dataService.GetUserWatchtimeAsync(account.Username, account.Platform);
        var streak = await _dataService.GetViewerStreakAsync(account.Username, account.Platform);
        var perks = (await _dataService.GetActiveViewerPerksAsync(account.Username, account.Platform))
            .OrderByDescending(p => p.ExpiresAt)
            .ToList();

        var entries = await _dataService.GetGiveawayEntriesForUserAsync(account.Username, account.Platform);
        var entrySnapshots = entries
            .OrderByDescending(e => e.EnteredAt)
            .Select(e => new ViewerProfileGiveawayEntrySnapshot(
                giveawayNames.TryGetValue(e.GiveawayId, out var giveawayName) ? giveawayName : "Giveaway",
                e.Platform,
                e.EnteredAt,
                e.EntryCount,
                e.IsWinner))
            .ToList();

        return new ViewerProfileAccountSnapshot
        {
            Username = account.Username,
            Platform = account.Platform,
            IsCurrent = isCurrent,
            IsVerified = account.IsVerified,
            LinkedAt = account.CreatedAt,
            Status = status,
            PlatformUserId = status?.PlatformUserId,
            PlatformEarned = earnings?.Earned ?? 0,
            PlatformSpent = earnings?.Spent ?? 0,
            Currency = currency,
            Transactions = transactions,
            Watchtime = watchtime,
            Streak = streak,
            Perks = perks,
            GiveawayEntries = entrySnapshots,
        };
    }

    /// <summary>
    /// Resolves one linked account to its detail snapshot, or null when the
    /// (username, platform) pair is not linked to a viewer identity.
    /// </summary>
    public async Task<ViewerProfileAccountSnapshot?> GetAccountDetailAsync(string username, string platform)
    {
        var requestedUsername = (username ?? string.Empty).Trim();
        var requestedPlatform = (platform ?? string.Empty).Trim();

        try
        {
            var viewerId = await _linkedAccounts.GetViewerIdAsync(requestedUsername, requestedPlatform);
            if (string.IsNullOrEmpty(viewerId))
            {
                viewerId = await _dataService.GetViewerIdAsync(requestedUsername, requestedPlatform);
            }

            if (string.IsNullOrEmpty(viewerId))
            {
                return null;
            }

            var accounts = await _linkedAccounts.GetLinkedAccountsAsync(viewerId);
            var account = accounts.FirstOrDefault(a =>
                SameAccount(a, requestedUsername, requestedPlatform));
            if (account is null)
            {
                return null;
            }

            var (statuses, giveawayNames, platformEarnings) = await LoadViewerContextAsync(viewerId);
            var snapshot = await BuildAccountSnapshotAsync(
                account,
                isCurrent: true,
                statuses,
                giveawayNames,
                platformEarnings);

            // The profile-wide DM list lives on the snapshot; the detail view
            // gets the same history scoped to this account's username.
            snapshot = snapshot with { DmHistory = await LoadDmHistoryAsync(account.Username) };

            _logger.LogInformation(
                "Account detail built for {Username} ({Platform}): viewer {ViewerId}, {Dms} dm(s)",
                requestedUsername, requestedPlatform, viewerId, snapshot.DmHistory.Count);

            return snapshot;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error building account detail for {Username} ({Platform})",
                requestedUsername, requestedPlatform);
            throw;
        }
    }

    /// <summary>Shared per-viewer lookups used by both profile and account-detail builds.</summary>
    private async Task<(List<ViewerPlatformStatus> Statuses, IReadOnlyDictionary<Guid, string> GiveawayNames, List<PlatformEarnings> Earnings)>
        LoadViewerContextAsync(string viewerId)
    {
        var statuses = (await _dataService.GetViewerStatusesByViewerIdAsync(viewerId)).ToList();
        var giveawayNames = (await _dataService.GetGiveawaysAsync())
            .GroupBy(g => g.Id)
            .ToDictionary(g => g.Key, g => g.First().Name);
        var earnings = (await _dataService.GetPlatformEarningsAsync(viewerId)).ToList();
        return (statuses, giveawayNames, earnings);
    }

    /// <summary>DM history for one username, newest first (matches snapshot conventions).</summary>
    private async Task<List<DiscordDmMessage>> LoadDmHistoryAsync(string username) =>
        (await _dataService.GetDiscordDmHistoryAsync(username))
            .OrderByDescending(m => m.Timestamp)
            .ToList();

    private static bool SamePlatform(string left, string right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static bool SameAccount(LinkedAccount account, string username, string platform) =>
        SameAccount(account.Username, username, account.Platform, platform);

    private static bool SameAccount(string leftUser, string rightUser, string leftPlatform, string rightPlatform) =>
        string.Equals(leftUser, rightUser, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(leftPlatform, rightPlatform, StringComparison.OrdinalIgnoreCase);
}
