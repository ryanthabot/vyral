using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using VyralBot.Core.Services.Currency;
using VyralBot.Core.Services.Profiles;
using VyralBot.Data;
using VyralBot.Data.Entities;

namespace VyralBot.UI.ViewModels;

public partial class CurrencyViewModel : ViewModelBase
{
    private readonly ILogger<CurrencyViewModel> _logger;
    private readonly ICurrencyService _currencyService;
    private readonly ICurrencyRewardsService _rewardsService;
    private readonly ISeasonService _seasonService;
    private readonly IProfileService _profileService;
    private readonly IDataService _dataService;

    // Port of the embedded web server (source of the leaderboard overlay URL);
    // 5000 fallback, same convention as AudioSettingsViewModel.
    private int _serverPort = 5000;

    [ObservableProperty]
    private CurrencySetting? _settings;

    [ObservableProperty]
    private ObservableCollection<UserCurrency> _leaderboard = new();

    [ObservableProperty]
    private ObservableCollection<CurrencyReward> _rewards = new();

    [ObservableProperty]
    private ObservableCollection<CurrencyRedemption> _redemptions = new();

    [ObservableProperty]
    private ObservableCollection<CurrencyTransaction> _transactions = new();

    [ObservableProperty]
    private CurrencyReward? _selectedReward;

    [ObservableProperty]
    private CurrencyRedemption? _selectedRedemption;

    [ObservableProperty]
    private bool _isEditingReward;

    [ObservableProperty]
    private string _newRewardName = "";

    [ObservableProperty]
    private string _newRewardDescription = "";

    [ObservableProperty]
    private int _newRewardCost = 100;

    [ObservableProperty]
    private bool _newRewardRequiresApproval = true;

    [ObservableProperty]
    private int _newRewardCooldown = 0;

    [ObservableProperty]
    private string _filterUsername = "";

    [ObservableProperty]
    private string _statusMessage = "";

    // --- Leaderboard overlay (OBS browser source) ----------------------------

    /// <summary>Row count baked into the overlay URL (widget caps at 25).</summary>
    [ObservableProperty]
    private int _overlayCount = 10;

    /// <summary>Heading text baked into the overlay URL (?title=...).</summary>
    [ObservableProperty]
    private string _overlayTitle = "Top Points";

    /// <summary>Full overlay URL to paste into an OBS browser source.</summary>
    [ObservableProperty]
    private string _leaderboardOverlayUrl = "http://localhost:5000/leaderboard.html?count=10&title=Top%20Points";

    // --- Seasons (loyalty seasons / competitive resets) ---------------------

    /// <summary>Current season + day summary, or a placeholder before the first season.</summary>
    [ObservableProperty]
    private string _currentSeasonDisplay = "No active season";

    /// <summary>Season history rows (newest first) for the Seasons card list.</summary>
    [ObservableProperty]
    private ObservableCollection<SeasonHistoryRow> _seasonHistory = new();

    /// <summary>True when at least one season row exists (toggles the empty hint).</summary>
    [ObservableProperty]
    private bool _hasSeasonHistory;

    /// <summary>Name for the next season, typed into the Seasons card.</summary>
    [ObservableProperty]
    private string _newSeasonName = "";

    /// <summary>Confirm-dialog visibility for Start New Season.</summary>
    [ObservableProperty]
    private bool _isConfirmingSeason;

    /// <summary>Competitive reset toggle in the confirm dialog (default OFF — balances are kept).</summary>
    [ObservableProperty]
    private bool _seasonResetBalances;

    /// <summary>Busy guard so the confirm button cannot double-fire StartSeasonAsync.</summary>
    [ObservableProperty]
    private bool _isStartingSeason;

    /// <summary>One-line recap shown inside the confirm dialog.</summary>
    [ObservableProperty]
    private string _seasonConfirmSummary = "";

    public CurrencyViewModel(
        ILogger<CurrencyViewModel> logger,
        ICurrencyService currencyService,
        ICurrencyRewardsService rewardsService,
        ISeasonService seasonService,
        IProfileService profileService,
        IDataService dataService)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _currencyService = currencyService ?? throw new ArgumentNullException(nameof(currencyService));
        _rewardsService = rewardsService ?? throw new ArgumentNullException(nameof(rewardsService));
        _seasonService = seasonService ?? throw new ArgumentNullException(nameof(seasonService));
        _profileService = profileService ?? throw new ArgumentNullException(nameof(profileService));
        _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
    }

    public async Task InitializeAsync()
    {
        try
        {
            await LoadSettingsAsync();
            await LoadLeaderboardAsync();
            await LoadRewardsAsync();
            await LoadRedemptionsAsync();
            await LoadOverlayPortAsync();
            await LoadSeasonsAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize CurrencyViewModel");
            StatusMessage = "Failed to load currency data.";
        }
    }

    /// <summary>
    /// The overlay URL must point at the embedded web server, whose port is
    /// owned by the Web Server settings — read it once per navigation (5000
    /// fallback) and rebuild the URL.
    /// </summary>
    private async Task LoadOverlayPortAsync()
    {
        try
        {
            var webSettings = await _dataService.GetWebServerSettingsAsync();
            if (webSettings is not null && webSettings.Port > 0)
            {
                _serverPort = webSettings.Port;
            }
        }
        catch (Exception portEx)
        {
            _logger.LogWarning(portEx, "Could not load web-server settings - using default port 5000 for the leaderboard overlay URL");
        }

        UpdateLeaderboardOverlayUrl();
    }

    partial void OnOverlayCountChanged(int value)
    {
        // Clamp to the endpoint's 1..25 range (re-enters once with the clamped value).
        var clamped = Math.Clamp(value, 1, 25);
        if (clamped != value)
        {
            OverlayCount = clamped;
            return;
        }

        UpdateLeaderboardOverlayUrl();
    }

    partial void OnOverlayTitleChanged(string value) => UpdateLeaderboardOverlayUrl();

    private void UpdateLeaderboardOverlayUrl()
    {
        LeaderboardOverlayUrl =
            $"http://localhost:{_serverPort}/leaderboard.html?count={OverlayCount}" +
            $"&title={Uri.EscapeDataString(OverlayTitle)}";
    }

    [RelayCommand]
    private void CopyLeaderboardOverlayUrl()
    {
        try
        {
            System.Windows.Clipboard.SetText(LeaderboardOverlayUrl);
            StatusMessage = "Leaderboard overlay URL copied - add it as a Browser Source in OBS.";
            _logger.LogInformation("Leaderboard overlay URL copied to clipboard");
        }
        catch (Exception ex)
        {
            // Clipboard can be locked by another process - non-fatal.
            _logger.LogWarning(ex, "Could not copy leaderboard overlay URL to clipboard");
            StatusMessage = "Could not copy - the clipboard is busy.";
        }
    }

    // --- Seasons (loyalty seasons / competitive resets) ---------------------

    /// <summary>1-based day number of a season (day 1 on the start date).</summary>
    private static int GetSeasonDay(CurrencySeason season)
        => Math.Max(1, (int)(DateTime.UtcNow - season.StartedAt).TotalDays + 1);

    /// <summary>
    /// Reloads the current season summary and the history rows (name, date
    /// range, top-3 winners parsed from each closing snapshot).
    /// </summary>
    private async Task LoadSeasonsAsync()
    {
        try
        {
            var seasons = await _seasonService.GetSeasonsAsync(20);

            CurrentSeasonDisplay = seasons.FirstOrDefault(s => s.EndedAt is null) is { } current
                ? $"{current.Name} — day {GetSeasonDay(current)}"
                : "No active season";

            SeasonHistory.Clear();
            foreach (var season in seasons)
            {
                SeasonHistory.Add(BuildSeasonHistoryRow(season));
            }
            HasSeasonHistory = SeasonHistory.Count > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load currency seasons");
            StatusMessage = "Failed to load seasons.";
        }
    }

    /// <summary>One display row of the season history list (immutable, rebuilt per load).</summary>
    private static SeasonHistoryRow BuildSeasonHistoryRow(CurrencySeason season)
    {
        var endedAt = season.EndedAt;
        var dates = endedAt is null
            ? $"{season.StartedAt:yyyy-MM-dd} — running"
            : $"{season.StartedAt:yyyy-MM-dd} — {endedAt:yyyy-MM-dd}";

        var medals = new[] { "🥇", "🥈", "🥉" };
        var top = SeasonService.ParseTopCount(season.TopCount);
        var winners = top is { Count: > 0 }
            ? string.Join("  ", top.Take(3).Select((entry, i) =>
                $"{medals[i]} {entry.Username} ({entry.Balance:N0})"))
            : "No balances";

        return new SeasonHistoryRow(season.Name, dates, winners);
    }

    /// <summary>Opens the in-card confirm dialog (reset checkbox always starts OFF).</summary>
    [RelayCommand]
    private void StartSeason()
    {
        if (string.IsNullOrWhiteSpace(NewSeasonName))
        {
            StatusMessage = "Enter a name for the new season first.";
            return;
        }

        SeasonResetBalances = false;
        SeasonConfirmSummary =
            $"Close the current season (its top-20 leaderboard is saved to history) and start \"{NewSeasonName.Trim()}\"?";
        IsConfirmingSeason = true;
    }

    [RelayCommand]
    private void CancelStartSeason() => IsConfirmingSeason = false;

    [RelayCommand]
    private async Task ConfirmStartSeasonAsync()
    {
        if (IsStartingSeason) return;

        IsStartingSeason = true;
        try
        {
            var result = await _seasonService.StartSeasonAsync(NewSeasonName, SeasonResetBalances);
            StatusMessage = result.Message;

            if (result.Success)
            {
                IsConfirmingSeason = false;
                NewSeasonName = "";
                // Balances may have been zeroed — refresh both panels.
                await LoadSeasonsAsync();
                await LoadLeaderboardAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start currency season");
            StatusMessage = "Failed to start the season.";
        }
        finally
        {
            IsStartingSeason = false;
        }
    }

    [RelayCommand]
    private async Task LoadSettingsAsync()
    {
        try
        {
            Settings = await _currencyService.GetSettingsAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load currency settings");
            StatusMessage = "Failed to load settings.";
        }
    }

    [RelayCommand]
    private async Task SaveSettingsAsync()
    {
        if (Settings == null) return;

        try
        {
            // Persist the full CurrencySetting entity (name/symbol, earning rates,
            // event bonuses...) and refresh the service's cached copy so the
            // edits take effect immediately.
            await _currencyService.SaveSettingsAsync(Settings);

            StatusMessage = "Settings saved successfully!";
            _logger.LogInformation("Currency settings saved");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save currency settings");
            StatusMessage = "Failed to save settings.";
        }
    }

    [RelayCommand]
    private async Task LoadLeaderboardAsync()
    {
        try
        {
            var leaders = await _currencyService.GetLeaderboardAsync(10);
            Leaderboard.Clear();
            foreach (var leader in leaders)
            {
                Leaderboard.Add(leader);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load leaderboard");
            StatusMessage = "Failed to load leaderboard.";
        }
    }

    [RelayCommand]
    private async Task LoadRewardsAsync()
    {
        try
        {
            var allRewards = await _rewardsService.GetRewardsAsync(enabledOnly: false);
            Rewards.Clear();
            foreach (var reward in allRewards)
            {
                Rewards.Add(reward);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load rewards");
            StatusMessage = "Failed to load rewards.";
        }
    }

    [RelayCommand]
    private async Task LoadRedemptionsAsync()
    {
        try
        {
            var allRedemptions = await _rewardsService.GetRedemptionsAsync();
            Redemptions.Clear();
            foreach (var redemption in allRedemptions.OrderByDescending(r => r.RedeemedAt))
            {
                Redemptions.Add(redemption);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load redemptions");
            StatusMessage = "Failed to load redemptions.";
        }
    }

    [RelayCommand]
    private async Task LoadTransactionsAsync()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(FilterUsername))
            {
                StatusMessage = "Enter a username to view transactions.";
                return;
            }

            var userTransactions = await _currencyService.GetTransactionHistoryAsync(FilterUsername, null, 100);
            Transactions.Clear();
            foreach (var transaction in userTransactions)
            {
                Transactions.Add(transaction);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load transactions for {Username}", FilterUsername);
            StatusMessage = "Failed to load transactions.";
        }
    }

    [RelayCommand]
    private void NewReward()
    {
        IsEditingReward = true;
        SelectedReward = null;
        NewRewardName = "";
        NewRewardDescription = "";
        NewRewardCost = 100;
        NewRewardRequiresApproval = true;
        NewRewardCooldown = 0;
    }

    [RelayCommand]
    private void EditReward()
    {
        if (SelectedReward == null) return;

        IsEditingReward = true;
        NewRewardName = SelectedReward.Name;
        NewRewardDescription = SelectedReward.Description ?? "";
        NewRewardCost = SelectedReward.Cost;
        NewRewardRequiresApproval = SelectedReward.RequiresApproval;
        NewRewardCooldown = SelectedReward.Cooldown;
    }

    [RelayCommand]
    private async Task SaveRewardAsync()
    {
        if (string.IsNullOrWhiteSpace(NewRewardName))
        {
            StatusMessage = "Reward name is required.";
            return;
        }

        if (NewRewardCost <= 0)
        {
            StatusMessage = "Reward cost must be greater than 0.";
            return;
        }

        try
        {
            var reward = SelectedReward ?? new CurrencyReward { Name = NewRewardName, Cost = NewRewardCost };
            reward.Name = NewRewardName;
            reward.Description = NewRewardDescription;
            reward.Cost = NewRewardCost;
            reward.RequiresApproval = NewRewardRequiresApproval;
            reward.Cooldown = NewRewardCooldown;
            // Preserve the entity's current IsEnabled — forcing true would silently
            // re-enable rewards the user deliberately disabled.

            await _rewardsService.SaveRewardAsync(reward);
            await LoadRewardsAsync();

            IsEditingReward = false;
            StatusMessage = "Reward saved successfully!";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save reward");
            StatusMessage = "Failed to save reward.";
        }
    }

    [RelayCommand]
    private void CancelEditReward()
    {
        IsEditingReward = false;
        SelectedReward = null;
    }

    [RelayCommand]
    private async Task DeleteRewardAsync()
    {
        if (SelectedReward == null) return;

        try
        {
            await _rewardsService.DeleteRewardAsync(SelectedReward.Id);
            await LoadRewardsAsync();
            StatusMessage = "Reward deleted successfully!";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete reward");
            StatusMessage = "Failed to delete reward.";
        }
    }

    [RelayCommand]
    private async Task ToggleRewardEnabledAsync()
    {
        if (SelectedReward == null) return;

        try
        {
            SelectedReward.IsEnabled = !SelectedReward.IsEnabled;
            await _rewardsService.SaveRewardAsync(SelectedReward);
            // LoadRewardsAsync replaces every instance, which drops the DataGrid
            // selection — capture the Id and re-select the matching row.
            var rewardId = SelectedReward.Id;
            await LoadRewardsAsync();
            SelectedReward = Rewards.FirstOrDefault(r => r.Id == rewardId);
            StatusMessage = SelectedReward?.IsEnabled == true ? "Reward enabled." : "Reward disabled.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to toggle reward enabled state");
            StatusMessage = "Failed to update reward.";
        }
    }

    [RelayCommand]
    private async Task ApproveRedemptionAsync()
    {
        if (SelectedRedemption == null) return;

        try
        {
            // The service only approves Pending rows — surface the result so a
            // double click (or a row already handled elsewhere) is not reported
            // as a success.
            var approved = await _rewardsService.ApproveRedemptionAsync(
                SelectedRedemption.Id, await GetApproverNameAsync(), "Approved via UI");
            await LoadRedemptionsAsync();
            StatusMessage = approved
                ? "Redemption approved!"
                : "Redemption was not approved (it may already be handled).";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to approve redemption");
            StatusMessage = "Failed to approve redemption.";
        }
    }

    [RelayCommand]
    private async Task RejectRedemptionAsync()
    {
        if (SelectedRedemption == null) return;

        try
        {
            // The service refuses to refund a redemption that was already
            // rejected/completed — the bool tells the UI which happened.
            var rejected = await _rewardsService.RejectRedemptionAsync(
                SelectedRedemption.Id, await GetApproverNameAsync(), "Rejected via UI");
            await LoadRedemptionsAsync();
            StatusMessage = rejected
                ? "Redemption rejected and currency refunded."
                : "Redemption was not rejected (already handled — no refund issued).";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reject redemption");
            StatusMessage = "Failed to reject redemption.";
        }
    }

    [RelayCommand]
    private async Task CompleteRedemptionAsync()
    {
        if (SelectedRedemption == null) return;

        try
        {
            var completed = await _rewardsService.CompleteRedemptionAsync(SelectedRedemption.Id, "Completed via UI");
            await LoadRedemptionsAsync();
            StatusMessage = completed
                ? "Redemption marked as completed!"
                : "Redemption was not completed (already handled).";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to complete redemption");
            StatusMessage = "Failed to complete redemption.";
        }
    }

    /// <summary>
    /// Approver identity for redemption approve/reject audit entries: the active
    /// profile's display name (falling back to its name), or "Admin" when no
    /// profile is active.
    /// </summary>
    private async Task<string> GetApproverNameAsync()
    {
        try
        {
            var profile = await _profileService.GetCurrentProfileAsync();
            return !string.IsNullOrWhiteSpace(profile?.DisplayName)
                ? profile.DisplayName
                : !string.IsNullOrWhiteSpace(profile?.Name)
                    ? profile.Name
                    : "Admin";
        }
        catch (Exception ex)
        {
            _logger.LogInformation(ex, "Could not resolve current profile for approver name; using Admin");
            return "Admin";
        }
    }

    /// <summary>
    /// One immutable display row of the Seasons card history list (built by
    /// <see cref="BuildSeasonHistoryRow"/>; plain properties are enough — rows
    /// are replaced wholesale on every load).
    /// </summary>
    public sealed class SeasonHistoryRow
    {
        public SeasonHistoryRow(string name, string dates, string winners)
        {
            Name = name;
            Dates = dates;
            Winners = winners;
        }

        /// <summary>Season display name.</summary>
        public string Name { get; }

        /// <summary>Date range ("yyyy-MM-dd — yyyy-MM-dd", or "— running" while open).</summary>
        public string Dates { get; }

        /// <summary>Top-3 winners with medals ("🥇 user (1,200)  🥈 …"), or "No balances".</summary>
        public string Winners { get; }
    }
}
