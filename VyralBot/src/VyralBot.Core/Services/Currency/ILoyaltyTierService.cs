using VyralBot.Data.Entities;

namespace VyralBot.Core.Services.Currency;

/// <summary>
/// Service for managing loyalty tiers and rank evaluation.
/// </summary>
public interface ILoyaltyTierService
{
    /// <summary>
    /// Get all configured loyalty tiers ordered by MinPoints.
    /// </summary>
    Task<IEnumerable<LoyaltyTier>> GetTiersAsync();

    /// <summary>
    /// Get the current loyalty tier for a user based on their total earned points.
    /// </summary>
    Task<LoyaltyTier?> GetUserTierAsync(string username, string platform);

    /// <summary>
    /// Get the next tier after the user's current tier (for progress display).
    /// </summary>
    Task<LoyaltyTier?> GetNextTierAsync(string username, string platform);

    /// <summary>
    /// Evaluate and return the appropriate tier for a given point total.
    /// </summary>
    Task<LoyaltyTier?> EvaluateTierAsync(int totalEarned);

    /// <summary>
    /// Get the earn multiplier for a user based on their loyalty tier.
    /// </summary>
    Task<double> GetTierMultiplierAsync(string username, string platform);

    /// <summary>
    /// Save a loyalty tier configuration.
    /// </summary>
    Task SaveTierAsync(LoyaltyTier tier);

    /// <summary>
    /// Delete a loyalty tier.
    /// </summary>
    Task DeleteTierAsync(string id);
}
