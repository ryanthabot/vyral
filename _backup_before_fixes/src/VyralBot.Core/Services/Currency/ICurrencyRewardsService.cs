using VyralBot.Data.Entities;

namespace VyralBot.Core.Services.Currency;

/// <summary>
/// Service for managing currency rewards and redemptions.
/// </summary>
public interface ICurrencyRewardsService
{
    /// <summary>
    /// Get all available rewards.
    /// </summary>
    Task<IEnumerable<CurrencyReward>> GetRewardsAsync(bool enabledOnly = true);

    /// <summary>
    /// Get a specific reward by ID.
    /// </summary>
    Task<CurrencyReward?> GetRewardAsync(string rewardId);

    /// <summary>
    /// Create or update a reward.
    /// </summary>
    Task SaveRewardAsync(CurrencyReward reward);

    /// <summary>
    /// Delete a reward.
    /// </summary>
    Task DeleteRewardAsync(string rewardId);

    /// <summary>
    /// Redeem a reward for a user.
    /// </summary>
    /// <returns>Redemption ID if successful, null if failed</returns>
    Task<string?> RedeemRewardAsync(string rewardId, string username, string platform, string? userInput = null);

    /// <summary>
    /// Get all redemptions (optionally filtered by status or username).
    /// </summary>
    Task<IEnumerable<CurrencyRedemption>> GetRedemptionsAsync(string? status = null, string? username = null);

    /// <summary>
    /// Get a specific redemption by ID.
    /// </summary>
    Task<CurrencyRedemption?> GetRedemptionAsync(string redemptionId);

    /// <summary>
    /// Approve a redemption (moderator action).
    /// </summary>
    Task<bool> ApproveRedemptionAsync(string redemptionId, string approvedBy, string? notes = null);

    /// <summary>
    /// Reject a redemption and refund currency (moderator action).
    /// </summary>
    Task<bool> RejectRedemptionAsync(string redemptionId, string rejectedBy, string? notes = null);

    /// <summary>
    /// Mark a redemption as completed.
    /// </summary>
    Task<bool> CompleteRedemptionAsync(string redemptionId, string? notes = null);

    /// <summary>
    /// Check if a user can redeem a reward (cooldown, limits, balance).
    /// </summary>
    Task<(bool CanRedeem, string? Reason)> CanRedeemAsync(string rewardId, string username, string platform);

    /// <summary>
    /// Get rewards filtered by category.
    /// </summary>
    Task<IEnumerable<CurrencyReward>> GetRewardsByCategoryAsync(string category);

    /// <summary>
    /// Get currently available rewards (respecting time-limited availability).
    /// </summary>
    Task<IEnumerable<CurrencyReward>> GetAvailableRewardsAsync(string? category = null);

    /// <summary>
    /// Get paginated store listing for chat command.
    /// </summary>
    Task<(IEnumerable<CurrencyReward> Rewards, int TotalPages)> GetStorePageAsync(int page = 1, int pageSize = 5, string? category = null);
}
