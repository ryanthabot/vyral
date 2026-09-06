using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using VyralBot.Core.Services.Linking;
using VyralBot.Data.Entities;

namespace VyralBot.UI.ViewModels;

/// <summary>
/// ViewModel for the admin viewer-profile window (opened from Viewer Hub /
/// elsewhere with a username+platform). Loads everything through
/// <see cref="IViewerProfileService"/> on a background thread and marshals the
/// results onto the UI dispatcher; the Refresh button re-runs the load.
/// Per-row Unlink removes a single account from the link group via
/// <see cref="ILinkCodeService.UnlinkAsync"/> (banned-user override).
/// </summary>
public partial class ViewerProfileViewModel : ViewModelBase
{
    private readonly ILogger<ViewerProfileViewModel> _logger;
    private readonly IViewerProfileService _profileService;
    private readonly ILinkCodeService _linkCodeService;

    private string _username = string.Empty;
    private string _platform = string.Empty;
    private bool _isRefreshing;

    [ObservableProperty]
    private string _windowTitle = "Viewer";

    [ObservableProperty]
    private string _headerUsername = "—";

    [ObservableProperty]
    private string _headerPlatform = "—";

    [ObservableProperty]
    private string _viewerIdDisplay = "—";

    /// <summary>False when the account has no unified ViewerId (renders the "not linked" state).</summary>
    [ObservableProperty]
    private bool _isLinked;

    [ObservableProperty]
    private bool _isLoading;

    /// <summary>False while a load is running (drives the Refresh button's IsEnabled).</summary>
    public bool CanRefresh => !IsLoading;

    partial void OnIsLoadingChanged(bool value) => OnPropertyChanged(nameof(CanRefresh));

    [ObservableProperty]
    private string? _statusMessage;

    // ===== Section collections =====

    public ObservableCollection<ViewerProfileAccountRow> Accounts { get; } = new();

    public ObservableCollection<ViewerProfileStatusRow> StatusRows { get; } = new();

    public ObservableCollection<ViewerProfileEconomyRow> EconomyRows { get; } = new();

    public ObservableCollection<ViewerProfileTransactionRow> Transactions { get; } = new();

    public ObservableCollection<ViewerProfileActivityRow> ActivityRows { get; } = new();

    public ObservableCollection<ViewerProfileGoalRow> Goals { get; } = new();

    public ObservableCollection<ViewerProfileGiveawayRow> Giveaways { get; } = new();

    public ObservableCollection<ViewerProfilePerkRow> Perks { get; } = new();

    public ObservableCollection<ViewerProfileEventStatRowVm> EventStats { get; } = new();

    // ===== Section empty-state flags =====

    [ObservableProperty] private bool _hasAccounts;
    [ObservableProperty] private string _watchingNowSummary = string.Empty;
    [ObservableProperty] private bool _hasStatusRows;
    [ObservableProperty] private bool _hasEconomyRows;
    [ObservableProperty] private bool _hasTransactions;
    [ObservableProperty] private bool _hasActivityRows;
    [ObservableProperty] private bool _hasGoals;
    [ObservableProperty] private bool _hasGiveaways;
    [ObservableProperty] private bool _hasPerks;
    [ObservableProperty] private bool _hasEventStats;

    [ObservableProperty]
    private int _totalEvents;

    [ObservableProperty]
    private string _eventsWindowDisplay = "—";

    public ViewerProfileViewModel(
        ILogger<ViewerProfileViewModel> logger,
        IViewerProfileService profileService,
        ILinkCodeService linkCodeService)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _profileService = profileService ?? throw new ArgumentNullException(nameof(profileService));
        _linkCodeService = linkCodeService ?? throw new ArgumentNullException(nameof(linkCodeService));
    }

    /// <summary>
    /// Loads the full profile for username+platform. Call before showing the
    /// window; the Refresh button re-runs it.
    /// </summary>
    public async Task InitializeAsync(string username, string platform)
    {
        _username = username ?? string.Empty;
        _platform = platform ?? string.Empty;
        WindowTitle = $"Viewer — {_username} ({_platform})";
        HeaderUsername = string.IsNullOrWhiteSpace(_username) ? "—" : _username;
        HeaderPlatform = string.IsNullOrWhiteSpace(_platform) ? "—" : _platform;

        await LoadAsync();
    }

    [RelayCommand]
    private async Task RefreshAsync() => await LoadAsync();

    private async Task LoadAsync()
    {
        if (_isRefreshing)
        {
            return;
        }

        _isRefreshing = true;
        IsLoading = true;

        try
        {
            var snapshot = await Task.Run(() =>
                _profileService.GetProfileAsync(_username, _platform));

            // Build every row off the UI thread, then apply in one dispatcher pass.
            var accountRows = snapshot.Accounts
                .Select(a => new ViewerProfileAccountRow(this, a.Username, a.Platform, a.IsCurrent, a.IsVerified, a.LinkedAt, a.Watchtime))
                .ToList();

            // LIVE SUMMARY: how many of the linked accounts are currently in chat.
            var liveCount = accountRows.Count(r => r.IsLive);
            WatchingNowSummary = liveCount > 0
                ? $"🟢 Watching now on {liveCount} of {accountRows.Count} linked platforms"
                : "⚪ Not in chat on any linked platform right now";

            var statusRows = snapshot.Accounts
                .Where(a => a.Status is not null)
                .Select(a => new ViewerProfileStatusRow(a.Username, a.Platform, a.Status!))
                .ToList();

            var economyRows = snapshot.Accounts
                .Where(a => a.Currency is not null)
                .Select(a => new ViewerProfileEconomyRow(a.Username, a.Platform, a.Currency!))
                .ToList();

            var transactionRows = snapshot.Accounts
                .SelectMany(a => a.Transactions.Select(t => new { Account = a, Transaction = t }))
                .OrderByDescending(x => x.Transaction.Timestamp)
                .Take(50)
                .Select(x => new ViewerProfileTransactionRow(x.Account.Username, x.Account.Platform, x.Transaction))
                .ToList();

            var activityRows = snapshot.Accounts
                .Select(a => new ViewerProfileActivityRow(a.Username, a.Platform, a.Watchtime, a.Streak))
                .ToList();

            var goalRows = snapshot.GoalsCompleted
                .Select(g => new ViewerProfileGoalRow(g.GoalName, g.CompletedAt, g.Platform))
                .ToList();

            var giveawayRows = snapshot.Accounts
                .SelectMany(a => a.GiveawayEntries)
                .OrderByDescending(e => e.EnteredAt)
                .Select(e => new ViewerProfileGiveawayRow(e))
                .ToList();

            var perkRows = snapshot.Accounts
                .SelectMany(a => a.Perks.Select(p => new { Account = a, Perk = p }))
                .OrderByDescending(x => x.Perk.ExpiresAt)
                .Select(x => new ViewerProfilePerkRow(x.Account.Username, x.Account.Platform, x.Perk))
                .ToList();

            var eventRows = snapshot.EventsByType
                .Select(e => new ViewerProfileEventStatRowVm(e.EventType, e.Count))
                .ToList();

            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                ReplaceCollection(Accounts, accountRows);
                ReplaceCollection(StatusRows, statusRows);
                ReplaceCollection(EconomyRows, economyRows);
                ReplaceCollection(Transactions, transactionRows);
                ReplaceCollection(ActivityRows, activityRows);
                ReplaceCollection(Goals, goalRows);
                ReplaceCollection(Giveaways, giveawayRows);
                ReplaceCollection(Perks, perkRows);
                ReplaceCollection(EventStats, eventRows);

                HasAccounts = accountRows.Count > 0;
                HasStatusRows = statusRows.Count > 0;
                HasEconomyRows = economyRows.Count > 0;
                HasTransactions = transactionRows.Count > 0;
                HasActivityRows = activityRows.Count > 0;
                HasGoals = goalRows.Count > 0;
                HasGiveaways = giveawayRows.Count > 0;
                HasPerks = perkRows.Count > 0;
                HasEventStats = eventRows.Count > 0;

                ViewerIdDisplay = snapshot.ViewerId ?? "—";
                IsLinked = snapshot.IsLinked;
                TotalEvents = snapshot.TotalEvents;
                EventsWindowDisplay = snapshot.FirstEventAt.HasValue && snapshot.LastEventAt.HasValue
                    ? $"{FormatDate(snapshot.FirstEventAt)} → {FormatDate(snapshot.LastEventAt)}"
                    : "—";
            });

            StatusMessage = null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading viewer profile for {Username} ({Platform})", _username, _platform);
            StatusMessage = $"Error loading profile: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
            _isRefreshing = false;
        }
    }

    internal async Task UnlinkAccountAsync(ViewerProfileAccountRow row)
    {
        try
        {
            var removed = await _linkCodeService.UnlinkAsync(row.Username, row.Platform);
            if (!removed)
            {
                StatusMessage = $"Could not unlink {row.Username} ({row.Platform}) — no matching linked account";
                return;
            }

            StatusMessage = row.IsCurrent
                ? $"Unlinked {row.Username} ({row.Platform}) — this was the account the profile was opened for; reopen it from Viewer Hub for its new state"
                : $"Unlinked {row.Username} ({row.Platform})";

            _logger.LogInformation(
                "Admin unlinked {Username} ({Platform}) from the viewer profile window",
                row.Username, row.Platform);

            await LoadAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error unlinking {Username} ({Platform}) from the viewer profile window", row.Username, row.Platform);
            StatusMessage = $"Failed to unlink {row.Username} ({row.Platform})";
        }
    }

    private static void ReplaceCollection<T>(ObservableCollection<T> target, IReadOnlyList<T> source)
    {
        target.Clear();
        foreach (var item in source)
        {
            target.Add(item);
        }
    }

    internal static string FormatDate(DateTime? value) =>
        value.HasValue ? value.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm") : "—";
}

/// <summary>
/// One linked account row in the profile window with its own per-account Unlink
/// command (single-account removal — the banned-user override).
/// </summary>
public partial class ViewerProfileAccountRow : ObservableObject
{
    private readonly ViewerProfileViewModel _owner;

    public ViewerProfileAccountRow(
        ViewerProfileViewModel owner,
        string username,
        string platform,
        bool isCurrent,
        bool isVerified,
        DateTime? linkedAt,
        UserWatchtime? watchtime)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        Username = username;
        Platform = platform;
        IsCurrent = isCurrent;
        IsVerified = isVerified;
        LinkedAtDisplay = ViewerProfileViewModel.FormatDate(linkedAt);

        // LIVE PRESENCE: watchtime marks a viewer inactive after ~5 idle
        // minutes, so an active row (recently seen) means they are in chat
        // right now on this platform.
        IsLive = watchtime is { IsActive: true }
                 && DateTime.UtcNow - watchtime.LastSeenAt <= LiveWindow;
        LiveDisplay = IsLive
            ? "🟢 In chat now"
            : watchtime is null
                ? "never seen"
                : $"seen {ViewerProfileViewModel.FormatDate(watchtime.LastSeenAt)}";
    }

    private static readonly TimeSpan LiveWindow = TimeSpan.FromMinutes(6);

    public bool IsLive { get; }

    public string LiveDisplay { get; }

    public string Username { get; }
    public string Platform { get; }
    public bool IsCurrent { get; }
    public bool IsVerified { get; }

    /// <summary>Marker rendered next to the account the profile was opened for.</summary>
    public string CurrentMarker => IsCurrent ? " (current)" : string.Empty;

    public string VerifiedDisplay => IsVerified ? "verified" : "unverified";

    public string LinkedAtDisplay { get; }

    [RelayCommand]
    private Task UnlinkAsync() => _owner.UnlinkAccountAsync(this);
}

/// <summary>Per-account platform status row (sub tier / follower / last seen).</summary>
public class ViewerProfileStatusRow
{
    public ViewerProfileStatusRow(string username, string platform, ViewerPlatformStatus status)
    {
        Username = username;
        Platform = platform;
        SubTierDisplay = status.SubTier > 0 ? $"Tier {status.SubTier}" : "—";
        SubscriberMonthsDisplay = status.SubscriberMonths > 0 ? status.SubscriberMonths.ToString("N0") : "—";
        FollowerDisplay = status.IsFollower ? "Yes" : "No";
        SubscriberDisplay = status.IsSubscriber ? "Yes" : "No";
        LastUpdatedDisplay = ViewerProfileViewModel.FormatDate(status.LastUpdatedAt);
    }

    public string Username { get; }
    public string Platform { get; }
    public string SubTierDisplay { get; }
    public string SubscriberMonthsDisplay { get; }
    public string FollowerDisplay { get; }
    public string SubscriberDisplay { get; }
    public string LastUpdatedDisplay { get; }
}

/// <summary>Per-account economy row (balance / earned / spent).</summary>
public class ViewerProfileEconomyRow
{
    public ViewerProfileEconomyRow(string username, string platform, UserCurrency currency)
    {
        Username = username;
        Platform = platform;
        BalanceDisplay = currency.Balance.ToString("N0");
        EarnedDisplay = currency.TotalEarned.ToString("N0");
        SpentDisplay = currency.TotalSpent.ToString("N0");
    }

    public string Username { get; }
    public string Platform { get; }
    public string BalanceDisplay { get; }
    public string EarnedDisplay { get; }
    public string SpentDisplay { get; }
}

/// <summary>One currency transaction row (aggregated across the viewer's accounts).</summary>
public class ViewerProfileTransactionRow
{
    public ViewerProfileTransactionRow(string username, string platform, CurrencyTransaction transaction)
    {
        Username = username;
        Platform = platform;
        TransactionType = transaction.TransactionType;
        AmountDisplay = (transaction.Amount >= 0 ? "+" : string.Empty) + transaction.Amount.ToString("N0");
        Reason = string.IsNullOrWhiteSpace(transaction.Reason) ? "—" : transaction.Reason;
        TimestampDisplay = ViewerProfileViewModel.FormatDate(transaction.Timestamp);
        IsEarn = transaction.Amount >= 0;
    }

    public string Username { get; }
    public string Platform { get; }
    public string TransactionType { get; }
    public string AmountDisplay { get; }
    public string Reason { get; }
    public string TimestampDisplay { get; }

    /// <summary>Drives the green/red amount foreground in the view.</summary>
    public bool IsEarn { get; }
}

/// <summary>Per-account watchtime + streak row.</summary>
public class ViewerProfileActivityRow
{
    public ViewerProfileActivityRow(string username, string platform, UserWatchtime? watchtime, ViewerStreak? streak)
    {
        Username = username;
        Platform = platform;
        TotalWatchtimeDisplay = watchtime is { TotalMinutes: > 0 }
            ? $"{watchtime.TotalMinutes:N0} min"
            : "—";
        SessionMinutesDisplay = watchtime is { StreamSessionMinutes: > 0 }
            ? $"{watchtime.StreamSessionMinutes:N0} min"
            : "—";
        FirstSeenDisplay = ViewerProfileViewModel.FormatDate(watchtime?.FirstSeenAt);
        LastSeenDisplay = ViewerProfileViewModel.FormatDate(watchtime?.LastSeenAt);
        ActiveDisplay = watchtime?.IsActive == true ? "Active" : "—";
        StreakDisplay = streak is { CurrentStreak: > 0 }
            ? $"{streak.CurrentStreak} (best {streak.LongestStreak})"
            : "—";
    }

    public string Username { get; }
    public string Platform { get; }
    public string TotalWatchtimeDisplay { get; }
    public string SessionMinutesDisplay { get; }
    public string FirstSeenDisplay { get; }
    public string LastSeenDisplay { get; }
    public string ActiveDisplay { get; }
    public string StreakDisplay { get; }
}

/// <summary>One completed Vyral goal row.</summary>
public class ViewerProfileGoalRow
{
    public ViewerProfileGoalRow(string name, DateTime completedAt, string platform)
    {
        Name = name;
        Platform = platform;
        CompletedDisplay = ViewerProfileViewModel.FormatDate(completedAt);
    }

    public string Name { get; }
    public string Platform { get; }
    public string CompletedDisplay { get; }
}

/// <summary>One giveaway entry row (joined with the giveaway name).</summary>
public class ViewerProfileGiveawayRow
{
    public ViewerProfileGiveawayRow(ViewerProfileGiveawayEntrySnapshot entry)
    {
        GiveawayName = entry.GiveawayName;
        Platform = entry.Platform;
        EntryCountDisplay = entry.EntryCount.ToString("N0");
        WinnerDisplay = entry.IsWinner ? "Winner" : string.Empty;
        EnteredDisplay = ViewerProfileViewModel.FormatDate(entry.EnteredAt);
        IsWinner = entry.IsWinner;
    }

    public string GiveawayName { get; }
    public string Platform { get; }
    public string EntryCountDisplay { get; }
    public string WinnerDisplay { get; }
    public string EnteredDisplay { get; }

    /// <summary>Drives the green winner highlight in the view.</summary>
    public bool IsWinner { get; }
}

/// <summary>One active perk row.</summary>
public class ViewerProfilePerkRow
{
    public ViewerProfilePerkRow(string username, string platform, ViewerPerk perk)
    {
        Username = username;
        Platform = platform;
        PerkType = perk.PerkType;
        CostDisplay = perk.Cost > 0 ? perk.Cost.ToString("N0") : "—";
        ExpiresDisplay = ViewerProfileViewModel.FormatDate(perk.ExpiresAt);
    }

    public string Username { get; }
    public string Platform { get; }
    public string PerkType { get; }
    public string CostDisplay { get; }
    public string ExpiresDisplay { get; }
}

/// <summary>One event-type highlight row.</summary>
public class ViewerProfileEventStatRowVm
{
    public ViewerProfileEventStatRowVm(string eventType, int count)
    {
        EventType = string.IsNullOrWhiteSpace(eventType) ? "—" : eventType;
        CountDisplay = count.ToString("N0");
    }

    public string EventType { get; }
    public string CountDisplay { get; }
}
