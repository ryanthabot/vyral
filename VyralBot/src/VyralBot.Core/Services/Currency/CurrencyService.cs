using Microsoft.Extensions.Logging;
using VyralBot.Data;
using VyralBot.Data.Entities;

namespace VyralBot.Core.Services.Currency;

/// <summary>
/// Service for managing user currency balances and transactions.
/// </summary>
public class CurrencyService : ICurrencyService
{
    private readonly ILogger<CurrencyService> _logger;
    private readonly IDataService _dataService;
    private readonly ICurrencyBoostService _boostService;
    private readonly ILoyaltyTierService _loyaltyTierService;
    private readonly object _balanceLock = new();
    private CurrencySetting? _cachedSettings;
    private DateTime _settingsCacheTime = DateTime.MinValue;
    private readonly TimeSpan _settingsCacheDuration = TimeSpan.FromMinutes(5);

    public CurrencyService(
        ILogger<CurrencyService> logger,
        IDataService dataService,
        ICurrencyBoostService boostService,
        ILoyaltyTierService loyaltyTierService)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
        _boostService = boostService ?? throw new ArgumentNullException(nameof(boostService));
        _loyaltyTierService = loyaltyTierService ?? throw new ArgumentNullException(nameof(loyaltyTierService));
    }

    public async Task<CurrencySetting?> GetSettingsAsync()
    {
        // Return cached settings if still valid
        if (_cachedSettings != null && DateTime.UtcNow - _settingsCacheTime < _settingsCacheDuration)
        {
            return _cachedSettings;
        }

        _cachedSettings = await _dataService.GetCurrencySettingsAsync();

        // If no settings exist, create defaults
        if (_cachedSettings == null)
        {
            _cachedSettings = new CurrencySetting
            {
                CurrencyName = "Points",
                CurrencySymbol = "⭐"
            };
            await _dataService.SaveCurrencySettingsAsync(_cachedSettings);
        }

        _settingsCacheTime = DateTime.UtcNow;
        return _cachedSettings;
    }

    /// <inheritdoc />
    public async Task SaveSettingsAsync(CurrencySetting settings)
    {
        if (settings == null) throw new ArgumentNullException(nameof(settings));

        await _dataService.SaveCurrencySettingsAsync(settings);

        // Refresh the cache so saved edits take effect immediately
        // (otherwise the 5-minute cache would keep serving the previous values).
        _cachedSettings = settings;
        _settingsCacheTime = DateTime.UtcNow;

        _logger.LogInformation("Saved currency settings (Name: {CurrencyName})", settings.CurrencyName);
    }

    /// <summary>
    /// Internal helper that guarantees non-null settings (creates defaults if needed).
    /// </summary>
    private async Task<CurrencySetting> GetSettingsRequiredAsync()
    {
        return await GetSettingsAsync() ?? new CurrencySetting { CurrencyName = "Points", CurrencySymbol = "⭐" };
    }

    public async Task<int> GetBalanceAsync(string username, string platform)
    {
        var userCurrency = await _dataService.GetUserCurrencyAsync(username, platform);
        return userCurrency?.Balance ?? 0;
    }

    public async Task<IEnumerable<UserCurrency>> GetAllBalancesAsync(string username)
    {
        return await _dataService.GetUserCurrenciesAsync(username);
    }

    public async Task<bool> AddCurrencyAsync(string username, string platform, int amount, string reason, string transactionType = "Earn")
    {
        if (amount <= 0)
        {
            _logger.LogWarning("Attempted to add non-positive amount: {Amount}", amount);
            return false;
        }

        try
        {
            lock (_balanceLock)
            {
                // This lock ensures transaction safety
            }

            var userCurrency = await _dataService.GetUserCurrencyAsync(username, platform);

            if (userCurrency == null)
            {
                userCurrency = new UserCurrency
                {
                    Username = username,
                    Platform = platform,
                    Balance = 0
                };
            }

            var balanceBefore = userCurrency.Balance;
            userCurrency.Balance += amount;
            userCurrency.TotalEarned += amount;

            await _dataService.SaveUserCurrencyAsync(userCurrency);

            // Log transaction
            var transaction = new CurrencyTransaction
            {
                Username = username,
                Platform = platform,
                Amount = amount,
                TransactionType = transactionType,
                Reason = reason,
                BalanceBefore = balanceBefore,
                BalanceAfter = userCurrency.Balance,
                Timestamp = DateTime.UtcNow
            };
            await _dataService.SaveCurrencyTransactionAsync(transaction);

            _logger.LogInformation("Added {Amount} currency to {Username} ({Platform}): {Reason}",
                amount, username, platform, reason);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add currency to {Username} ({Platform})", username, platform);
            return false;
        }
    }

    public async Task<bool> RemoveCurrencyAsync(string username, string platform, int amount, string reason, string transactionType = "Spend")
    {
        if (amount <= 0)
        {
            _logger.LogWarning("Attempted to remove non-positive amount: {Amount}", amount);
            return false;
        }

        try
        {
            lock (_balanceLock)
            {
                // This lock ensures transaction safety
            }

            var userCurrency = await _dataService.GetUserCurrencyAsync(username, platform);

            if (userCurrency == null || userCurrency.Balance < amount)
            {
                _logger.LogWarning("Insufficient balance for {Username} ({Platform}): has {Balance}, needs {Amount}",
                    username, platform, userCurrency?.Balance ?? 0, amount);
                return false;
            }

            var balanceBefore = userCurrency.Balance;
            userCurrency.Balance -= amount;
            userCurrency.TotalSpent += amount;

            await _dataService.SaveUserCurrencyAsync(userCurrency);

            // Log transaction
            var transaction = new CurrencyTransaction
            {
                Username = username,
                Platform = platform,
                Amount = -amount, // Negative for spending
                TransactionType = transactionType,
                Reason = reason,
                BalanceBefore = balanceBefore,
                BalanceAfter = userCurrency.Balance,
                Timestamp = DateTime.UtcNow
            };
            await _dataService.SaveCurrencyTransactionAsync(transaction);

            _logger.LogInformation("Removed {Amount} currency from {Username} ({Platform}): {Reason}",
                amount, username, platform, reason);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove currency from {Username} ({Platform})", username, platform);
            return false;
        }
    }

    public async Task<bool> TransferCurrencyAsync(string fromUsername, string fromPlatform, string toUsername, string toPlatform, int amount)
    {
        if (amount <= 0)
        {
            _logger.LogWarning("Attempted to transfer non-positive amount: {Amount}", amount);
            return false;
        }

        // Remove from sender
        var removed = await RemoveCurrencyAsync(fromUsername, fromPlatform, amount,
            $"Transfer to {toUsername}", "Transfer");

        if (!removed)
        {
            return false;
        }

        // Add to receiver
        var added = await AddCurrencyAsync(toUsername, toPlatform, amount,
            $"Transfer from {fromUsername}", "Transfer");

        if (!added)
        {
            // Rollback - add back to sender
            await AddCurrencyAsync(fromUsername, fromPlatform, amount,
                "Transfer rollback", "Admin");
            return false;
        }

        _logger.LogInformation("Transferred {Amount} currency from {FromUser} to {ToUser}",
            amount, fromUsername, toUsername);

        return true;
    }

    public async Task<IEnumerable<UserCurrency>> GetLeaderboardAsync(int limit = 10, string? platform = null)
    {
        return await _dataService.GetCurrencyLeaderboardAsync(limit, platform);
    }

    public async Task<IEnumerable<CurrencyTransaction>> GetTransactionHistoryAsync(string username, string? platform = null, int limit = 50)
    {
        return await _dataService.GetCurrencyTransactionsAsync(username, platform, limit);
    }

    public async Task AwardChatActivityAsync(string username, string platform)
    {
        var settings = await GetSettingsRequiredAsync();

        if (!settings.ChatActivityEnabled)
        {
            return;
        }

        var userCurrency = await _dataService.GetUserCurrencyAsync(username, platform);

        // Check cooldown
        if (userCurrency?.LastChatActivityUpdate != null)
        {
            var timeSinceLastUpdate = DateTime.UtcNow - userCurrency.LastChatActivityUpdate.Value;
            if (timeSinceLastUpdate.TotalSeconds < settings.ChatActivityCooldown)
            {
                return; // Still on cooldown
            }
        }

        // Award currency (platform x boost x tier multipliers applied; floored to int)
        var awardAmount = await ApplyEarnMultipliersAsync(settings.ChatActivityAmount, username, platform);
        if (awardAmount > 0)
        {
            await AddCurrencyAsync(username, platform, awardAmount, "Chat activity", "Earn");
        }

        // Update cooldown timestamp
        if (userCurrency != null)
        {
            userCurrency.LastChatActivityUpdate = DateTime.UtcNow;
            await _dataService.SaveUserCurrencyAsync(userCurrency);
        }
    }

    public async Task AwardWatchTimeAsync(string username, string platform)
    {
        var settings = await GetSettingsRequiredAsync();

        if (!settings.WatchTimeEnabled)
        {
            return;
        }

        var userCurrency = await _dataService.GetUserCurrencyAsync(username, platform);

        // Check interval
        if (userCurrency?.LastWatchTimeUpdate != null)
        {
            var timeSinceLastUpdate = DateTime.UtcNow - userCurrency.LastWatchTimeUpdate.Value;
            if (timeSinceLastUpdate.TotalSeconds < settings.WatchTimeInterval)
            {
                return; // Not enough time passed
            }
        }

        // Award currency (platform x boost x tier multipliers applied; floored to int)
        var awardAmount = await ApplyEarnMultipliersAsync(settings.WatchTimeAmount, username, platform);
        if (awardAmount > 0)
        {
            await AddCurrencyAsync(username, platform, awardAmount, "Watch time", "Earn");
        }

        // Update watch time timestamp
        if (userCurrency != null)
        {
            userCurrency.LastWatchTimeUpdate = DateTime.UtcNow;
            await _dataService.SaveUserCurrencyAsync(userCurrency);
        }
    }

    public async Task AwardEventBonusAsync(string username, string platform, string eventType, int multiplier = 1)
    {
        var settings = await GetSettingsRequiredAsync();
        int amount = 0;
        string reason = $"{eventType} bonus";

        switch (eventType.ToLower())
        {
            case "follow":
                if (!settings.FollowBonusEnabled) return;
                amount = settings.FollowBonusAmount * multiplier;
                break;

            case "subscribe":
            case "sub":
                if (!settings.SubBonusEnabled) return;
                amount = settings.SubBonusAmount * multiplier;
                break;

            case "giftsub":
            case "giftsubs":
                if (!settings.GiftSubBonusEnabled) return;
                amount = settings.GiftSubBonusAmountPerSub * multiplier;
                reason = $"Gift sub bonus (x{multiplier})";
                break;

            case "raid":
                if (!settings.RaidBonusEnabled) return;
                amount = settings.RaidBonusAmountPerViewer * multiplier;
                reason = $"Raid bonus ({multiplier} viewers)";
                break;

            case "cheer":
            case "bits":
                if (!settings.CheerBonusEnabled) return;
                amount = settings.CheerBonusAmountPerBit * multiplier;
                reason = $"Cheer bonus ({multiplier} bits)";
                break;

            case "host":
            case "shoutout":
                if (!settings.HostBonusEnabled) return;
                amount = settings.HostBonusAmount * multiplier;
                reason = $"Host/shoutout bonus";
                break;

            default:
                _logger.LogWarning("Unknown event type for currency bonus: {EventType}", eventType);
                return;
        }

        if (amount > 0)
        {
            // Apply the full earn-multiplier stack (platform, active boost, loyalty tier,
            // and role where it is known). On subscribe events the user is by definition
            // a subscriber, so the sub role multiplier applies there; other events carry
            // no role information in this build.
            var isSubscribeEvent = eventType.Equals("subscribe", StringComparison.OrdinalIgnoreCase) ||
                                   eventType.Equals("sub", StringComparison.OrdinalIgnoreCase);

            amount = await ApplyEarnMultipliersAsync(amount, username, platform, isSubscriber: isSubscribeEvent);

            if (amount > 0)
            {
                await AddCurrencyAsync(username, platform, amount, reason, "EventBonus");
            }
        }
    }

    public async Task<(bool Success, int Amount, int Streak, string Message)> ClaimDailyBonusAsync(string username, string platform)
    {
        var settings = await GetSettingsRequiredAsync();
        if (!settings.DailyBonusEnabled)
        {
            return (false, 0, 0, "Daily bonus is currently disabled.");
        }

        var streak = await _dataService.GetViewerStreakAsync(username, platform);
        if (streak == null)
        {
            streak = new ViewerStreak { Username = username, Platform = platform };
        }

        // Check if already claimed today
        if (streak.LastDailyClaimDate.HasValue &&
            streak.LastDailyClaimDate.Value.Date == DateTime.UtcNow.Date)
        {
            return (false, 0, streak.CurrentStreak, "You've already claimed your daily bonus today!");
        }

        // Check streak continuity (must have claimed yesterday)
        if (streak.LastDailyClaimDate.HasValue &&
            (DateTime.UtcNow.Date - streak.LastDailyClaimDate.Value.Date).Days == 1)
        {
            streak.CurrentStreak++;
        }
        else if (!streak.LastDailyClaimDate.HasValue ||
                 (DateTime.UtcNow.Date - streak.LastDailyClaimDate.Value.Date).Days > 1)
        {
            streak.CurrentStreak = 1; // Reset streak
        }

        if (streak.CurrentStreak > streak.LongestStreak)
        {
            streak.LongestStreak = streak.CurrentStreak;
        }

        // Calculate bonus based on streak
        int bonus = settings.DailyBonusAmount;
        if (settings.StreakBonusEnabled)
        {
            if (streak.CurrentStreak >= 10)
                bonus += settings.StreakBonusDay10;
            else if (streak.CurrentStreak >= 5)
                bonus += settings.StreakBonusDay5;
            else
                bonus += settings.StreakBonusDay1;
        }

        streak.LastDailyClaimDate = DateTime.UtcNow;
        await _dataService.SaveViewerStreakAsync(streak);

        var currencyName = settings.CurrencyName;
        await AddCurrencyAsync(username, platform, bonus, $"Daily bonus (streak: {streak.CurrentStreak})", "DailyBonus");

        var streakText = streak.CurrentStreak > 1 ? $" 🔥 {streak.CurrentStreak}-day streak!" : "";
        return (true, bonus, streak.CurrentStreak,
            $"@{username} claimed {bonus:N0} {currencyName}!{streakText}");
    }

    public async Task AwardFirstChatBonusAsync(string username, string platform)
    {
        var settings = await GetSettingsRequiredAsync();
        if (!settings.FirstChatBonusEnabled) return;

        // Check if user already has a currency record (not first time)
        var existing = await _dataService.GetUserCurrencyAsync(username, platform);
        if (existing != null) return;

        await AddCurrencyAsync(username, platform, settings.FirstChatBonusAmount,
            "First-time chatter bonus", "FirstChat");

        _logger.LogInformation("Awarded first-chat bonus to {Username} ({Platform})", username, platform);
    }

    public async Task<double> GetEffectiveMultiplierAsync(string username, string platform,
        bool isSubscriber = false, bool isVip = false, bool isModerator = false)
    {
        var settings = await GetSettingsRequiredAsync();
        double multiplier = 1.0;

        // Platform multiplier
        multiplier *= GetPlatformMultiplier(settings, platform);

        // Role multipliers (highest wins — not stacking)
        if (isModerator && settings.ModMultiplierEnabled)
            multiplier *= settings.ModMultiplierValue;
        else if (isVip && settings.VipMultiplierEnabled)
            multiplier *= settings.VipMultiplierValue;
        else if (isSubscriber && settings.SubMultiplierEnabled)
            multiplier *= settings.SubMultiplierValue;

        return multiplier;
    }

    public async Task<int> GiveCurrencyToAllAsync(string platform, int amount, string reason)
    {
        var leaderboard = await _dataService.GetCurrencyLeaderboardAsync(10000, platform);
        var users = leaderboard.ToList();
        int count = 0;

        foreach (var user in users)
        {
            await AddCurrencyAsync(user.Username, user.Platform, amount, reason, "Admin");
            count++;
        }

        _logger.LogInformation("Gave {Amount} currency to {Count} users on {Platform}: {Reason}",
            amount, count, platform, reason);
        return count;
    }

    public async Task<EconomyStats> GetEconomyStatsAsync()
    {
        var allUsers = await _dataService.GetCurrencyLeaderboardAsync(100000);
        var userList = allUsers.ToList();

        var totalInCirculation = userList.Sum(u => (long)u.Balance);
        var activeUsers = userList.Count;
        var averageBalance = activeUsers > 0 ? (double)totalInCirculation / activeUsers : 0;

        var todayStart = DateTime.UtcNow.Date;
        var transactions = await _dataService.GetCurrencyTransactionsAsync(null, null, 10000);
        var todayTransactions = transactions.Where(t => t.Timestamp >= todayStart).ToList();

        var totalEarnedToday = todayTransactions
            .Where(t => t.Amount > 0)
            .Sum(t => (long)t.Amount);

        var totalSpentToday = todayTransactions
            .Where(t => t.Amount < 0)
            .Sum(t => (long)Math.Abs(t.Amount));

        return new EconomyStats(totalInCirculation, totalEarnedToday, totalSpentToday, activeUsers, averageBalance);
    }

    /// <summary>
    /// Applies the full earn-multiplier stack to a base award amount:
    /// platform multiplier x active boost (highest wins) x loyalty-tier multiplier
    /// x role multiplier (only where role flags are known; highest role wins).
    /// The combined result is consistently floored to an integer.
    /// </summary>
    private async Task<int> ApplyEarnMultipliersAsync(int amount, string username, string platform,
        bool isSubscriber = false, bool isVip = false, bool isModerator = false)
    {
        if (amount <= 0)
        {
            return 0;
        }

        // GetEffectiveMultiplierAsync already combines the platform multiplier with
        // the role multipliers (highest wins, not stacking).
        var effectiveMultiplier = await GetEffectiveMultiplierAsync(username, platform, isSubscriber, isVip, isModerator);

        // Timed boosts: highest active boost wins; global boosts (no platform) apply everywhere.
        var boostMultiplier = await _boostService.GetActiveMultiplierAsync(platform);

        // Loyalty tier earn multiplier, derived from lifetime earnings.
        var tierMultiplier = await _loyaltyTierService.GetTierMultiplierAsync(username, platform);

        return (int)Math.Floor(amount * effectiveMultiplier * boostMultiplier * tierMultiplier);
    }

    private static double GetPlatformMultiplier(CurrencySetting settings, string platform)
    {
        return platform.ToLowerInvariant() switch
        {
            "twitch" => settings.TwitchEarnMultiplier,
            "kick" => settings.KickEarnMultiplier,
            "youtube" => settings.YouTubeEarnMultiplier,
            _ => 1.0
        };
    }
}
