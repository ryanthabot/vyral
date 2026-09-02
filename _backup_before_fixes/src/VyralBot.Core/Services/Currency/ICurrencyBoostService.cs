using VyralBot.Data.Entities;

namespace VyralBot.Core.Services.Currency;

/// <summary>
/// Service for managing temporary currency earning boosts and multiplier events.
/// </summary>
public interface ICurrencyBoostService
{
    /// <summary>
    /// Start a new boost (streamer-triggered or automatic).
    /// </summary>
    Task<CurrencyBoost> StartBoostAsync(double multiplier, int durationMinutes, string reason, string source = "Manual", string? platform = null);

    /// <summary>
    /// End an active boost early.
    /// </summary>
    Task<bool> EndBoostAsync(string boostId);

    /// <summary>
    /// Get all currently active boosts.
    /// </summary>
    Task<IEnumerable<CurrencyBoost>> GetActiveBoostsAsync(string? platform = null);

    /// <summary>
    /// Get the combined boost multiplier for a platform (product of all active boosts).
    /// </summary>
    Task<double> GetActiveMultiplierAsync(string? platform = null);

    /// <summary>
    /// Get boost history.
    /// </summary>
    Task<IEnumerable<CurrencyBoost>> GetBoostHistoryAsync(int limit = 50);

    /// <summary>
    /// Expire any boosts that have passed their end time.
    /// </summary>
    Task ExpireBoostsAsync();

    /// <summary>
    /// Trigger a hype train escalation boost based on consecutive events.
    /// </summary>
    Task<CurrencyBoost?> TriggerHypeTrainBoostAsync(string platform, int level);
}
