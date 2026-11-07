using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using VyralBot.Data;
using VyralBot.Data.Entities;

namespace VyralBot.Core.Services.Currency;

/// <summary>
/// Service for managing currency rewards and redemptions.
/// </summary>
public class CurrencyRewardsService : ICurrencyRewardsService
{
    private readonly ILogger<CurrencyRewardsService> _logger;
    private readonly IDataService _dataService;
    private readonly ICurrencyService _currencyService;

    // Redemptions are check-then-act sequences (per-user limit, cooldown,
    // MaxRedemptionsTotal, CurrentRedemptions++ all read state that the
    // redemption itself writes). Two rapid !redeem commands from the same
    // viewer would both pass every check before either row is saved and
    // double-spend. These per-reward gates serialize redemptions of the same
    // reward (different rewards proceed in parallel).
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _rewardGates = new();

    private static SemaphoreSlim GetRewardGate(string rewardId)
        => _rewardGates.GetOrAdd(rewardId, _ => new SemaphoreSlim(1, 1));

    public CurrencyRewardsService(
        ILogger<CurrencyRewardsService> logger,
        IDataService dataService,
        ICurrencyService currencyService)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
        _currencyService = currencyService ?? throw new ArgumentNullException(nameof(currencyService));
    }

    public async Task<IEnumerable<CurrencyReward>> GetRewardsAsync(bool enabledOnly = true)
    {
        return await _dataService.GetCurrencyRewardsAsync(enabledOnly);
    }

    public async Task<CurrencyReward?> GetRewardAsync(string rewardId)
    {
        return await _dataService.GetCurrencyRewardAsync(rewardId);
    }

    public async Task SaveRewardAsync(CurrencyReward reward)
    {
        await _dataService.SaveCurrencyRewardAsync(reward);
        _logger.LogInformation("Saved reward: {RewardName} ({Cost} points)", reward.Name, reward.Cost);
    }

    public async Task DeleteRewardAsync(string rewardId)
    {
        await _dataService.DeleteCurrencyRewardAsync(rewardId);
        _logger.LogInformation("Deleted reward: {RewardId}", rewardId);
    }

    public async Task<string?> RedeemRewardAsync(string rewardId, string username, string platform, string? userInput = null)
    {
        // Serialize redemptions of the same reward so the cooldown, per-user
        // limit and MaxRedemptionsTotal checks cannot be beaten by two rapid
        // concurrent !redeem commands (both would pass before either row is saved).
        var gate = GetRewardGate(rewardId);
        await gate.WaitAsync();
        try
        {
            // Check if user can redeem
            var (canRedeem, reason) = await CanRedeemAsync(rewardId, username, platform);
            if (!canRedeem)
            {
                _logger.LogWarning("User {Username} cannot redeem reward {RewardId}: {Reason}",
                    username, rewardId, reason);
                return null;
            }

            var reward = await _dataService.GetCurrencyRewardAsync(rewardId);
            if (reward == null)
            {
                return null;
            }

            // Deduct currency
            var removed = await _currencyService.RemoveCurrencyAsync(
                username, platform, reward.Cost, $"Redeemed: {reward.Name}", "Spend");

            if (!removed)
            {
                return null;
            }

            // Create redemption record
            var redemption = new CurrencyRedemption
            {
                RewardId = rewardId,
                RewardName = reward.Name,
                Username = username,
                Platform = platform,
                Cost = reward.Cost,
                UserInput = userInput,
                Status = reward.RequiresApproval ? "Pending" : "Approved",
                RedeemedAt = DateTime.UtcNow
            };

            await _dataService.SaveCurrencyRedemptionAsync(redemption);

            // Update reward redemption count
            reward.CurrentRedemptions++;
            await _dataService.SaveCurrencyRewardAsync(reward);

            _logger.LogInformation("User {Username} redeemed reward {RewardName} (ID: {RedemptionId})",
                username, reward.Name, redemption.Id);

            return redemption.Id;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to redeem reward {RewardId} for user {Username}",
                rewardId, username);
            return null;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IEnumerable<CurrencyRedemption>> GetRedemptionsAsync(string? status = null, string? username = null)
    {
        return await _dataService.GetCurrencyRedemptionsAsync(status, username);
    }

    public async Task<CurrencyRedemption?> GetRedemptionAsync(string redemptionId)
    {
        return await _dataService.GetCurrencyRedemptionAsync(redemptionId);
    }

    public async Task<bool> ApproveRedemptionAsync(string redemptionId, string approvedBy, string? notes = null)
    {
        var redemption = await _dataService.GetCurrencyRedemptionAsync(redemptionId);
        if (redemption == null)
        {
            return false;
        }

        // Only pending redemptions can be approved — a second click (or two
        // moderators racing) must not re-process an already handled row.
        if (redemption.Status != "Pending")
        {
            _logger.LogInformation("Redemption {RedemptionId} not approved: status is {Status}, not Pending",
                redemptionId, redemption.Status);
            return false;
        }

        redemption.Status = "Approved";
        redemption.ProcessedAt = DateTime.UtcNow;
        redemption.ProcessedBy = approvedBy;
        redemption.Notes = notes;

        await _dataService.SaveCurrencyRedemptionAsync(redemption);

        _logger.LogInformation("Redemption {RedemptionId} approved by {ApprovedBy}",
            redemptionId, approvedBy);

        return true;
    }

    public async Task<bool> RejectRedemptionAsync(string redemptionId, string rejectedBy, string? notes = null)
    {
        var redemption = await _dataService.GetCurrencyRedemptionAsync(redemptionId);
        if (redemption == null)
        {
            return false;
        }

        // Guard the refund: rejecting an already-rejected redemption refunded
        // the cost EVERY time, so spamming the reject button minted points.
        // Terminal states (Rejected/Completed) are never re-processed.
        if (redemption.Status == "Rejected" || redemption.Status == "Completed")
        {
            _logger.LogInformation("Redemption {RedemptionId} not rejected: already {Status}",
                redemptionId, redemption.Status);
            return false;
        }

        // Refund currency
        await _currencyService.AddCurrencyAsync(
            redemption.Username, redemption.Platform, redemption.Cost,
            $"Refund: {redemption.RewardName} (rejected)", "Refund");

        redemption.Status = "Rejected";
        redemption.ProcessedAt = DateTime.UtcNow;
        redemption.ProcessedBy = rejectedBy;
        redemption.Notes = notes;

        await _dataService.SaveCurrencyRedemptionAsync(redemption);

        _logger.LogInformation("Redemption {RedemptionId} rejected by {RejectedBy}, currency refunded",
            redemptionId, rejectedBy);

        return true;
    }

    public async Task<bool> CompleteRedemptionAsync(string redemptionId, string? notes = null)
    {
        var redemption = await _dataService.GetCurrencyRedemptionAsync(redemptionId);
        if (redemption == null)
        {
            return false;
        }

        // Completing a rejected redemption (already refunded) would make the
        // audit trail claim the reward was fulfilled — refuse instead.
        if (redemption.Status == "Rejected" || redemption.Status == "Completed")
        {
            _logger.LogInformation("Redemption {RedemptionId} not completed: status is {Status}",
                redemptionId, redemption.Status);
            return false;
        }

        redemption.Status = "Completed";
        redemption.ProcessedAt = DateTime.UtcNow;
        redemption.Notes = notes;

        await _dataService.SaveCurrencyRedemptionAsync(redemption);

        _logger.LogInformation("Redemption {RedemptionId} marked as completed", redemptionId);

        return true;
    }

    public async Task<(bool CanRedeem, string? Reason)> CanRedeemAsync(string rewardId, string username, string platform)
    {
        var reward = await _dataService.GetCurrencyRewardAsync(rewardId);
        if (reward == null)
        {
            return (false, "Reward not found");
        }

        if (!reward.IsEnabled)
        {
            return (false, "Reward is currently disabled");
        }

        // Check time-limited availability
        var now = DateTime.UtcNow;
        if (reward.AvailableFrom.HasValue && now < reward.AvailableFrom.Value)
        {
            return (false, "This reward is not yet available");
        }
        if (reward.AvailableUntil.HasValue && now > reward.AvailableUntil.Value)
        {
            return (false, "This reward is no longer available");
        }

        // Check balance
        var balance = await _currencyService.GetBalanceAsync(username, platform);
        if (balance < reward.Cost)
        {
            return (false, $"Insufficient balance (need {reward.Cost}, have {balance})");
        }

        // Check total redemptions limit
        if (reward.MaxRedemptionsTotal > 0 && reward.CurrentRedemptions >= reward.MaxRedemptionsTotal)
        {
            return (false, "Reward has reached maximum redemptions");
        }

        // The per-user limit and cooldown both read the viewer's non-rejected
        // history for THIS reward — fetch it once when either check is enabled
        // (previously this ran two full-history queries per redemption).
        List<CurrencyRedemption>? userRewardRedemptions = null;
        if (reward.MaxRedemptionsPerUser > 0 || reward.Cooldown > 0)
        {
            userRewardRedemptions = (await _dataService.GetCurrencyRedemptionsAsync(null, username))
                .Where(r => r.RewardId == rewardId && r.Status != "Rejected")
                .ToList();
        }

        // Check per-user redemptions limit
        if (reward.MaxRedemptionsPerUser > 0 &&
            userRewardRedemptions!.Count >= reward.MaxRedemptionsPerUser)
        {
            return (false, $"You have reached the maximum redemptions for this reward ({reward.MaxRedemptionsPerUser})");
        }

        // Check cooldown
        if (reward.Cooldown > 0)
        {
            var lastRedemption = userRewardRedemptions!
                .OrderByDescending(r => r.RedeemedAt)
                .FirstOrDefault();

            if (lastRedemption != null)
            {
                var timeSinceLastRedemption = DateTime.UtcNow - lastRedemption.RedeemedAt;
                if (timeSinceLastRedemption.TotalSeconds < reward.Cooldown)
                {
                    var remainingSeconds = (int)(reward.Cooldown - timeSinceLastRedemption.TotalSeconds);
                    return (false, $"Reward is on cooldown ({remainingSeconds}s remaining)");
                }
            }
        }

        // Check required loyalty tier (Phase 5)
        if (!string.IsNullOrEmpty(reward.RequiredLoyaltyTier))
        {
            var tiers = (await _dataService.GetLoyaltyTiersAsync()).OrderBy(t => t.MinPoints).ToList();
            var requiredTier = tiers.FirstOrDefault(t =>
                t.Name.Equals(reward.RequiredLoyaltyTier, StringComparison.OrdinalIgnoreCase));

            if (requiredTier != null)
            {
                var userCurrency = await _dataService.GetUserCurrencyAsync(username, platform);
                var totalEarned = userCurrency?.TotalEarned ?? 0;
                if (totalEarned < requiredTier.MinPoints)
                {
                    return (false, $"Requires loyalty tier '{requiredTier.Name}' ({requiredTier.MinPoints:N0} total earned)");
                }
            }
        }

        return (true, null);
    }

    public async Task<IEnumerable<CurrencyReward>> GetRewardsByCategoryAsync(string category)
    {
        var rewards = await _dataService.GetCurrencyRewardsAsync(true);
        return rewards.Where(r => r.Category != null &&
            r.Category.Equals(category, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<IEnumerable<CurrencyReward>> GetAvailableRewardsAsync(string? category = null)
    {
        var rewards = await _dataService.GetCurrencyRewardsAsync(true);
        var now = DateTime.UtcNow;

        var available = rewards.Where(r =>
            (!r.AvailableFrom.HasValue || now >= r.AvailableFrom.Value) &&
            (!r.AvailableUntil.HasValue || now <= r.AvailableUntil.Value));

        if (!string.IsNullOrEmpty(category))
        {
            available = available.Where(r => r.Category != null &&
                r.Category.Equals(category, StringComparison.OrdinalIgnoreCase));
        }

        return available;
    }

    public async Task<(IEnumerable<CurrencyReward> Rewards, int TotalPages)> GetStorePageAsync(
        int page = 1, int pageSize = 5, string? category = null)
    {
        var all = (await GetAvailableRewardsAsync(category)).ToList();
        var totalPages = Math.Max(1, (int)Math.Ceiling((double)all.Count / pageSize));
        page = Math.Clamp(page, 1, totalPages);

        var pageItems = all
            .Skip((page - 1) * pageSize)
            .Take(pageSize);

        return (pageItems, totalPages);
    }
}
