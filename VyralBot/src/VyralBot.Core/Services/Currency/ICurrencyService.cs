using VyralBot.Data.Entities;

namespace VyralBot.Core.Services.Currency;

/// <summary>
/// Service for managing user currency balances and transactions.
/// </summary>
public interface ICurrencyService
{
    /// <summary>
    /// Get currency settings.
    /// </summary>
    Task<CurrencySetting?> GetSettingsAsync();

    /// <summary>
    /// Persist currency settings and refresh the service's cached copy.
    /// </summary>
    Task SaveSettingsAsync(CurrencySetting settings);

    /// <summary>
    /// Get user's currency balance for a specific platform.
    /// </summary>
    Task<int> GetBalanceAsync(string username, string platform);

    /// <summary>
    /// Get all currency balances for a user across all platforms.
    /// </summary>
    Task<IEnumerable<UserCurrency>> GetAllBalancesAsync(string username);

    /// <summary>
    /// Add currency to a user's balance.
    /// </summary>
    /// <param name="username">Username</param>
    /// <param name="platform">Platform (Twitch, Kick, etc.)</param>
    /// <param name="amount">Amount to add</param>
    /// <param name="reason">Reason for transaction</param>
    /// <param name="transactionType">Transaction type (Earn, Admin, etc.)</param>
    Task<bool> AddCurrencyAsync(string username, string platform, int amount, string reason, string transactionType = "Earn");

    /// <summary>
    /// Remove currency from a user's balance.
    /// </summary>
    Task<bool> RemoveCurrencyAsync(string username, string platform, int amount, string reason, string transactionType = "Spend");

    /// <summary>
    /// Transfer currency from one user to another.
    /// </summary>
    Task<bool> TransferCurrencyAsync(string fromUsername, string fromPlatform, string toUsername, string toPlatform, int amount);

    /// <summary>
    /// Get currency leaderboard (top earners).
    /// </summary>
    Task<IEnumerable<UserCurrency>> GetLeaderboardAsync(int limit = 10, string? platform = null);

    /// <summary>
    /// Get transaction history for a user.
    /// </summary>
    Task<IEnumerable<CurrencyTransaction>> GetTransactionHistoryAsync(string username, string? platform = null, int limit = 50);

    /// <summary>
    /// Award currency for chat activity (with cooldown check).
    /// </summary>
    Task AwardChatActivityAsync(string username, string platform);

    /// <summary>
    /// Award currency for watch time (with interval check).
    /// </summary>
    Task AwardWatchTimeAsync(string username, string platform);

    /// <summary>
    /// Award currency bonus for events (follow, sub, raid, etc.).
    /// </summary>
    Task AwardEventBonusAsync(string username, string platform, string eventType, int multiplier = 1);

    /// <summary>
    /// Award daily check-in bonus (!daily command).
    /// </summary>
    Task<(bool Success, int Amount, int Streak, string Message)> ClaimDailyBonusAsync(string username, string platform);

    /// <summary>
    /// Award first-time chatter bonus.
    /// </summary>
    Task AwardFirstChatBonusAsync(string username, string platform);

    /// <summary>
    /// Get the effective earn multiplier for a user (role + tier + platform multipliers).
    /// </summary>
    Task<double> GetEffectiveMultiplierAsync(string username, string platform, bool isSubscriber = false, bool isVip = false, bool isModerator = false);

    /// <summary>
    /// Give currency to all online users (bulk operation).
    /// </summary>
    Task<int> GiveCurrencyToAllAsync(string platform, int amount, string reason);

    /// <summary>
    /// Get economy statistics (total in circulation, earned today, spent today).
    /// </summary>
    Task<EconomyStats> GetEconomyStatsAsync();
}

public record EconomyStats(long TotalInCirculation, long TotalEarnedToday, long TotalSpentToday, int ActiveUsers, double AverageBalance);
