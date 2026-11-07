using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using VyralBot.Core.Services.Currency;
using VyralBot.Core.Services.Profiles;
using VyralBot.Data.Entities;

namespace VyralBot.UI.ViewModels;

public partial class CurrencyViewModel : ViewModelBase
{
    private readonly ILogger<CurrencyViewModel> _logger;
    private readonly ICurrencyService _currencyService;
    private readonly ICurrencyRewardsService _rewardsService;
    private readonly IProfileService _profileService;

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

    public CurrencyViewModel(
        ILogger<CurrencyViewModel> logger,
        ICurrencyService currencyService,
        ICurrencyRewardsService rewardsService,
        IProfileService profileService)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _currencyService = currencyService ?? throw new ArgumentNullException(nameof(currencyService));
        _rewardsService = rewardsService ?? throw new ArgumentNullException(nameof(rewardsService));
        _profileService = profileService ?? throw new ArgumentNullException(nameof(profileService));
    }

    public async Task InitializeAsync()
    {
        try
        {
            await LoadSettingsAsync();
            await LoadLeaderboardAsync();
            await LoadRewardsAsync();
            await LoadRedemptionsAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize CurrencyViewModel");
            StatusMessage = "Failed to load currency data.";
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
}
