using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using VyralBot.Core.Services.Linking;
using VyralBot.Core.Services.StreamStatus;
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
    private readonly IMultiPlatformBonusService? _multiPlatformBonus;
    private readonly IStreamLiveRegistry? _streamLiveRegistry;
    private CurrencySetting? _cachedSettings;
    private DateTime _settingsCacheTime = DateTime.MinValue;
    private readonly TimeSpan _settingsCacheDuration = TimeSpan.FromMinutes(5);

    public CurrencyService(
        ILogger<CurrencyService> logger,
        IDataService dataService,
        ICurrencyBoostService boostService,
        ILoyaltyTierService loyaltyTierService,
        IMultiPlatformBonusService? multiPlatformBonus = null,
        IStreamLiveRegistry? streamLiveRegistry = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
        _boostService = boostService ?? throw new ArgumentNullException(nameof(boostService));
        _loyaltyTierService = loyaltyTierService ?? throw new ArgumentNullException(nameof(loyaltyTierService));
        _multiPlatformBonus = multiPlatformBonus;
        _streamLiveRegistry = streamLiveRegistry;
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

    // ===== Unified balances =====
    // When a viewer has linked accounts, all earning and spending is routed to
    // ONE canonical account row so the balance is a single shared total that
    // works from any linked platform. Gated by the Linking.UnifiedBalances
    // config key (default on). Resolutions are cached briefly per account.
    private sealed record UnifiedTarget(string Username, string Platform);

    private static readonly TimeSpan UnifiedCacheTtl = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan UnifiedConfigCacheTtl = TimeSpan.FromSeconds(30);
    private readonly ConcurrentDictionary<string, (UnifiedTarget? Target, DateTime At)> _unifiedCache = new();
    private DateTime _unifiedConfigCheckedAtUtc = DateTime.MinValue;
    private bool _unifiedConfigEnabled = true;

    private async Task<bool> IsUnifiedBalancesEnabledAsync()
    {
        if (DateTime.UtcNow - _unifiedConfigCheckedAtUtc < UnifiedConfigCacheTtl)
        {
            return _unifiedConfigEnabled;
        }

        var raw = await _dataService.GetConfigValueAsync("Linking.UnifiedBalances");
        _unifiedConfigEnabled = raw == null || !bool.TryParse(raw, out var parsed) || parsed;
        _unifiedConfigCheckedAtUtc = DateTime.UtcNow;
        return _unifiedConfigEnabled;
    }

    /// <summary>
    /// Returns the canonical (username, platform) this account's balance lives
    /// on when unified balances are on and the account belongs to a linked
    /// viewer with 2+ accounts; otherwise null (operate as-is).
    /// </summary>
    private async Task<UnifiedTarget?> ResolveUnifiedTargetAsync(string username, string platform)
    {
        if (!await IsUnifiedBalancesEnabledAsync())
        {
            return null;
        }

        var cacheKey = username + ":" + platform;
        if (_unifiedCache.TryGetValue(cacheKey, out var cached)
            && DateTime.UtcNow - cached.At < UnifiedCacheTtl)
        {
            return cached.Target.Username == username && cached.Target.Platform == platform ? null : cached.Target;
        }

        UnifiedTarget? result = null;
        try
        {
            var viewerId = await _dataService.GetViewerIdAsync(username, platform);
            if (viewerId != null)
            {
                var accounts = (await _dataService.GetLinkedAccountsByViewerIdAsync(viewerId)).ToList();
                if (accounts.Count >= 2)
                {
                    // Canonical account: prefer Discord (DMs reach the viewer
                    // there), otherwise the earliest-linked account.
                    var canonical = accounts.FirstOrDefault(a => a.Platform.Equals("Discord", StringComparison.OrdinalIgnoreCase))
                                    ?? accounts.OrderBy(a => a.CreatedAt).First();
                    if (!canonical.Username.Equals(username, StringComparison.OrdinalIgnoreCase)
                        || !canonical.Platform.Equals(platform, StringComparison.OrdinalIgnoreCase))
                    {
                        result = new UnifiedTarget(canonical.Username, canonical.Platform);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unified balance resolution failed for {Username}/{Platform}", username, platform);
        }

        _unifiedCache[cacheKey] = (new UnifiedTarget(result?.Username ?? username, result?.Platform ?? platform), DateTime.UtcNow);
        return result;
    }

    /// <summary>Row read that follows unified-balance redirection.</summary>
    private async Task<UserCurrency?> GetUserCurrencyRowResolvedAsync(string username, string platform)
    {
        var target = await ResolveUnifiedTargetAsync(username, platform);
        return await _dataService.GetUserCurrencyAsync(target?.Username ?? username, target?.Platform ?? platform);
    }

    /// <summary>
    /// Merges every linked account's balance into the canonical account and
    /// zeroes the others, so the viewer has a single spendable total.
    /// </summary>
    public async Task ConsolidateViewerBalancesAsync(string viewerId)
    {
        if (!await IsUnifiedBalancesEnabledAsync())
        {
            return;
        }

        var accounts = (await _dataService.GetLinkedAccountsByViewerIdAsync(viewerId)).ToList();
        if (accounts.Count < 2)
        {
            return;
        }

        var canonical = accounts.FirstOrDefault(a => a.Platform.Equals("Discord", StringComparison.OrdinalIgnoreCase))
                        ?? accounts.OrderBy(a => a.CreatedAt).First();
        var canonicalRow = await _dataService.GetUserCurrencyAsync(canonical.Username, canonical.Platform);

        foreach (var account in accounts)
        {
            if (account.Username.Equals(canonical.Username, StringComparison.OrdinalIgnoreCase)
                && account.Platform.Equals(canonical.Platform, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var row = await _dataService.GetUserCurrencyAsync(account.Username, account.Platform);
            if (row == null)
            {
                continue;
            }

            canonicalRow ??= new UserCurrency
            {
                Username = canonical.Username,
                Platform = canonical.Platform,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            };

            canonicalRow.Balance += row.Balance;
            canonicalRow.TotalEarned += row.TotalEarned;
            canonicalRow.TotalSpent += row.TotalSpent;

            row.Balance = 0;
            row.TotalEarned = 0;
            row.TotalSpent = 0;
            row.UpdatedAt = DateTime.UtcNow;
            await _dataService.SaveUserCurrencyAsync(row);
        }

        if (canonicalRow != null)
        {
            canonicalRow.UpdatedAt = DateTime.UtcNow;
            await _dataService.SaveUserCurrencyAsync(canonicalRow);
            _logger.LogInformation("Unified balances for viewer {ViewerId}: total {Balance} on {Username}/{Platform}",
                viewerId, canonicalRow.Balance, canonical.Username, canonical.Platform);
        }

        // W2T5: the per-platform PlatformEarnings ledger is keyed by
        // (ViewerId, Platform) and is NOT stored in UserCurrency, so merging and
        // zeroing UserCurrency rows above never touches it — per-platform
        // earned/spent history survives consolidation unchanged. Nothing to
        // aggregate here; only the unified-target cache needs invalidating in
        // case the canonical row moved.
        _unifiedCache.Clear();
    }

    public async Task<int> GetBalanceAsync(string username, string platform)
    {
        var target = await ResolveUnifiedTargetAsync(username, platform);
        username = target?.Username ?? username;
        platform = target?.Platform ?? platform;
        var userCurrency = await GetUserCurrencyRowResolvedAsync(username, platform);
        return userCurrency?.Balance ?? 0;
    }

    public async Task<IEnumerable<UserCurrency>> GetAllBalancesAsync(string username)
    {
        return await _dataService.GetUserCurrenciesAsync(username);
    }

    // ===== Per-account mutation gates =====
    // Balance updates are read-modify-write on the whole UserCurrency row
    // (SaveUserCurrencyAsync persists Balance, TotalEarned and TotalSpent
    // together), and callers genuinely overlap: the EventBus runs up to 4
    // handlers concurrently, so chat-activity awards, watch-time ticks,
    // commands, redemptions and bank operations can hit the same viewer at
    // the same instant. These gates serialize every mutation for one
    // canonical (post-unification) account; different viewers proceed in
    // parallel. Keyed on the lower-cased "username:platform" identity.
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _accountGates = new();

    private static SemaphoreSlim GetAccountGate(string username, string platform)
        => _accountGates.GetOrAdd(
            $"{username.ToLowerInvariant()}:{platform.ToLowerInvariant()}",
            _ => new SemaphoreSlim(1, 1));

    /// <summary>
    /// Saturating add for the int balance/total columns: at the int ceiling a
    /// further award clamps to <see cref="int.MaxValue"/> instead of silently
    /// wrapping negative in unchecked arithmetic.
    /// </summary>
    private static int SaturatingAdd(int current, int delta)
    {
        var sum = (long)current + delta;
        return sum > int.MaxValue ? int.MaxValue : (int)sum;
    }

    public async Task<bool> AddCurrencyAsync(string username, string platform, int amount, string reason, string transactionType = "Earn")
    {
        if (amount <= 0)
        {
            _logger.LogWarning("Attempted to add non-positive amount: {Amount}", amount);
            return false;
        }

        // W2T5: capture where this award actually happened BEFORE unified-balance
        // redirection moves the balance write to the canonical account — the
        // per-platform ledger must record the caller's platform.
        var earnedOnPlatform = platform;
        var earnedByUsername = username;

        var unified = await ResolveUnifiedTargetAsync(username, platform);
        if (unified != null)
        {
            (username, platform) = (unified.Username, unified.Platform);
        }

        var gate = GetAccountGate(username, platform);
        await gate.WaitAsync();
        try
        {
            return await AddCurrencyUngatedAsync(username, platform, amount, reason, transactionType,
                earnedOnPlatform, earnedByUsername);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// Balance add WITHOUT acquiring the per-account gate — the caller must
    /// already hold it (SemaphoreSlim is not reentrant, so the public
    /// <see cref="AddCurrencyAsync"/> delegates here while holding the gate,
    /// and so do the award paths that must keep check + award + cooldown
    /// atomic). Expects an already-unified (canonical) identity.
    /// </summary>
    private async Task<bool> AddCurrencyUngatedAsync(string username, string platform, int amount,
        string reason, string transactionType, string earnedOnPlatform, string earnedByUsername)
    {
        try
        {
            var userCurrency = await GetUserCurrencyRowResolvedAsync(username, platform);

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
            userCurrency.Balance = SaturatingAdd(userCurrency.Balance, amount);
            userCurrency.TotalEarned = SaturatingAdd(userCurrency.TotalEarned, amount);

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

            // W2T5: append the per-platform ledger delta under the canonical
            // (pool-owner) viewer identity, tagged with the ORIGINAL earn
            // platform so per-platform history survives unified-balance row
            // zeroing. Never throws (see RecordPlatformLedgerDeltaAsync).
            await RecordPlatformLedgerDeltaAsync(username, platform, earnedOnPlatform,
                earned: amount, spent: 0, originalUsername: earnedByUsername);

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

        // W2T5: capture where this spend actually happened BEFORE unified-balance
        // redirection moves the balance write to the canonical account — the
        // per-platform ledger must record the caller's platform.
        var spentOnPlatform = platform;
        var spentByUsername = username;

        var unified = await ResolveUnifiedTargetAsync(username, platform);
        if (unified != null)
        {
            (username, platform) = (unified.Username, unified.Platform);
        }

        var gate = GetAccountGate(username, platform);
        await gate.WaitAsync();
        try
        {
            return await RemoveCurrencyUngatedAsync(username, platform, amount, reason, transactionType,
                spentOnPlatform, spentByUsername);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// Balance remove WITHOUT acquiring the per-account gate — the caller must
    /// already hold it. The sufficient-balance check and the write run together
    /// under the gate, so two concurrent spends cannot both pass the check and
    /// double-spend the same balance. Expects an already-unified identity.
    /// </summary>
    private async Task<bool> RemoveCurrencyUngatedAsync(string username, string platform, int amount,
        string reason, string transactionType, string spentOnPlatform, string spentByUsername)
    {
        try
        {
            var userCurrency = await GetUserCurrencyRowResolvedAsync(username, platform);

            if (userCurrency == null || userCurrency.Balance < amount)
            {
                _logger.LogWarning("Insufficient balance for {Username} ({Platform}): has {Balance}, needs {Amount}",
                    username, platform, userCurrency?.Balance ?? 0, amount);
                return false;
            }

            var balanceBefore = userCurrency.Balance;
            userCurrency.Balance -= amount;
            userCurrency.TotalSpent = SaturatingAdd(userCurrency.TotalSpent, amount);

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

            // W2T5: append the per-platform ledger delta under the canonical
            // (pool-owner) viewer identity, tagged with the ORIGINAL spend
            // platform so per-platform history survives unified-balance row
            // zeroing. Never throws (see RecordPlatformLedgerDeltaAsync).
            await RecordPlatformLedgerDeltaAsync(username, platform, spentOnPlatform,
                earned: 0, spent: amount, originalUsername: spentByUsername);

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

    // ===== Per-platform earnings ledger (W2T5) =====
    // Unified balances route all earn/spend to ONE canonical UserCurrency row
    // and zero the others, which would silently destroy per-platform history.
    // The PlatformEarnings ledger (keyed by (ViewerId, Platform), owned by the
    // data layer) records earned/spent per platform alongside the unified pool.

    /// <summary>
    /// Appends a per-platform ledger delta for the viewer who owns the canonical
    /// (post-redirect) balance row. The delta is tagged with the ORIGINAL
    /// platform (captured before redirection) so the ledger shows where the
    /// points were actually earned or spent. Unlinked viewers have no viewerId
    /// and therefore no ledger — their single UserCurrency row is the record.
    /// Never throws: this runs AFTER the balance save succeeded, and a ledger
    /// failure must never break earning or spending.
    /// </summary>
    private async Task RecordPlatformLedgerDeltaAsync(string canonicalUsername, string canonicalPlatform,
        string originalPlatform, long earned, long spent, string originalUsername)
    {
        try
        {
            var viewerId = await _dataService.GetViewerIdAsync(canonicalUsername, canonicalPlatform);
            if (viewerId == null)
            {
                _logger.LogDebug(
                    "No viewer id for {Username}/{Platform} — skipping platform earnings ledger",
                    canonicalUsername, canonicalPlatform);
                return;
            }

            await _dataService.AddPlatformEarningsAsync(viewerId, originalPlatform, earned, spent);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Platform earnings ledger update failed for {OriginalUsername} on {Platform} (earned: {Earned}, spent: {Spent}) — the balance was already saved",
                originalUsername, originalPlatform, earned, spent);
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<(string Platform, long Earned, long Spent)>> GetPerPlatformEarningsAsync(string viewerId)
    {
        if (string.IsNullOrEmpty(viewerId))
        {
            return Array.Empty<(string Platform, long Earned, long Spent)>();
        }

        var rows = await _dataService.GetPlatformEarningsAsync(viewerId);
        return rows
            .Select(r => (r.Platform, r.Earned, r.Spent))
            .OrderBy(t => t.Platform, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// W2T5 pure ledger math (unit-tested in PlatformEarningsTests): applies an
    /// (earned, spent) delta to the running per-platform counters. Counters are
    /// monotonic per component — negative deltas are ignored, never subtracted —
    /// and accumulate as long so lifetime totals can exceed int range.
    /// DapperDataService.AddPlatformEarningsAsync performs this exact
    /// read-add-write atomically in SQL; this helper is the shared, testable
    /// specification of that math. Public (like IsDiscordChannelAllowed)
    /// because VyralBot.Core does not grant InternalsVisibleTo to the tests.
    /// </summary>
    public static class PlatformLedgerMath
    {
        public static void Apply(long earnedDelta, long spentDelta, ref long currentEarned, ref long currentSpent)
        {
            if (earnedDelta > 0)
            {
                currentEarned += earnedDelta;
            }

            if (spentDelta > 0)
            {
                currentSpent += spentDelta;
            }
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

    public async Task AwardChatActivityAsync(string username, string platform, string? channelId = null)
    {
        // W1T4: Discord chat activity points are paid ONLY in channels the
        // streamer assigned (per-bot AssignedChannelIds). Callers without
        // channel info (channelId == null) keep working (back-compat) and are
        // logged at Debug.
        if (platform != null && platform.Equals("Discord", StringComparison.OrdinalIgnoreCase))
        {
            if (channelId == null)
            {
                _logger.LogDebug(
                    "Discord chat activity for {Username}: no channel id supplied by caller — awarding (legacy path)",
                    username);
            }
            else if (!await IsDiscordChannelAssignedAsync(channelId))
            {
                _logger.LogDebug(
                    "Discord chat activity for {Username}: channel {ChannelId} is not in the streamer's assigned channels — skipping points",
                    username, channelId);
                return;
            }
        }

        var settings = await GetSettingsRequiredAsync();

        if (!settings.ChatActivityEnabled)
        {
            return;
        }

        // Hold the account gate across cooldown check -> award -> cooldown
        // timestamp so two chat messages processed concurrently (the EventBus
        // runs up to 4 handlers in parallel) cannot both pass the cooldown
        // check before either stamps it — the classic double-award window.
        var chatTarget = await ResolveUnifiedTargetAsync(username, platform);
        var chatCanonicalUser = chatTarget?.Username ?? username;
        var chatCanonicalPlatform = chatTarget?.Platform ?? platform;

        var chatGate = GetAccountGate(chatCanonicalUser, chatCanonicalPlatform);
        await chatGate.WaitAsync();
        try
        {
            var userCurrency = await _dataService.GetUserCurrencyAsync(chatCanonicalUser, chatCanonicalPlatform);

            // Check cooldown
            if (userCurrency?.LastChatActivityUpdate != null)
            {
                var timeSinceLastUpdate = DateTime.UtcNow - userCurrency.LastChatActivityUpdate.Value;
                if (timeSinceLastUpdate.TotalSeconds < settings.ChatActivityCooldown)
                {
                    return; // Still on cooldown
                }
            }

            // Award currency (platform x boost x tier multipliers applied; floored
            // to int). Ungated add: this method already holds the account gate.
            var awardAmount = await ApplyEarnMultipliersAsync(settings.ChatActivityAmount, username, platform);
            if (awardAmount > 0)
            {
                await AddCurrencyUngatedAsync(chatCanonicalUser, chatCanonicalPlatform, awardAmount,
                    "Chat activity", "Earn", platform, username);
            }

            // Update cooldown timestamp. Re-fetch first: a brand-new viewer's record was
            // null above and is only created inside the add — without the re-fetch
            // LastChatActivityUpdate would stay null and the next message would double-pay.
            var currencyForCooldown = await _dataService.GetUserCurrencyAsync(chatCanonicalUser, chatCanonicalPlatform);
            if (currencyForCooldown != null)
            {
                currencyForCooldown.LastChatActivityUpdate = DateTime.UtcNow;
                await _dataService.SaveUserCurrencyAsync(currencyForCooldown);
            }
        }
        finally
        {
            chatGate.Release();
        }
    }

    public async Task AwardWatchTimeAsync(string username, string platform)
    {
        // W1T4 gating: Discord viewers never earn watch-time points, and stream
        // platforms earn only while that platform is live. When no live
        // registry is wired the live check is skipped (back-compat).
        var isLive = _streamLiveRegistry is null
            ? (bool?)null
            : _streamLiveRegistry.IsLive(platform);
        if (!WatchtimeService.ShouldAccrueWatchtime(platform, isLive))
        {
            return;
        }

        var settings = await GetSettingsRequiredAsync();

        if (!settings.WatchTimeEnabled)
        {
            return;
        }

        // Hold the account gate across interval check -> award -> timestamp so
        // a concurrent earning path (chat activity, commands) cannot interleave
        // with the read-modify-write below and lose an update.
        var watchTarget = await ResolveUnifiedTargetAsync(username, platform);
        var watchCanonicalUser = watchTarget?.Username ?? username;
        var watchCanonicalPlatform = watchTarget?.Platform ?? platform;

        var watchGate = GetAccountGate(watchCanonicalUser, watchCanonicalPlatform);
        await watchGate.WaitAsync();
        try
        {
            var userCurrency = await _dataService.GetUserCurrencyAsync(watchCanonicalUser, watchCanonicalPlatform);

            // Check interval
            if (userCurrency?.LastWatchTimeUpdate != null)
            {
                var timeSinceLastUpdate = DateTime.UtcNow - userCurrency.LastWatchTimeUpdate.Value;
                if (timeSinceLastUpdate.TotalSeconds < settings.WatchTimeInterval)
                {
                    return; // Not enough time passed
                }
            }

            // Award currency (platform x boost x tier multipliers applied; floored
            // to int). Ungated add: this method already holds the account gate.
            var awardAmount = await ApplyEarnMultipliersAsync(settings.WatchTimeAmount, username, platform);
            if (awardAmount > 0)
            {
                await AddCurrencyUngatedAsync(watchCanonicalUser, watchCanonicalPlatform, awardAmount,
                    "Watch time", "Earn", platform, username);
            }

            // Update watch time timestamp. Re-fetch first: a brand-new viewer's record was
            // null above and is only created inside the add — without the re-fetch
            // LastWatchTimeUpdate would stay null and the next tick would double-pay.
            var currencyForCooldown = await _dataService.GetUserCurrencyAsync(watchCanonicalUser, watchCanonicalPlatform);
            if (currencyForCooldown != null)
            {
                currencyForCooldown.LastWatchTimeUpdate = DateTime.UtcNow;
                await _dataService.SaveUserCurrencyAsync(currencyForCooldown);
            }
        }
        finally
        {
            watchGate.Release();
        }
    }

    /// <summary>
    /// W1T4 gating decision (pure, unit-tested in WatchtimeGatingTests):
    /// whether <paramref name="channelId"/> appears in the streamer's assigned
    /// channel list. Membership is comma-split and case-insensitive with
    /// surrounding whitespace tolerated. A null/empty channel id or an
    /// empty/missing assigned list is NOT allowed (strict per the approved
    /// rules — callers that have no channel info must decide back-compat
    /// themselves before consulting this helper).
    /// </summary>
    public static bool IsDiscordChannelAllowed(string? channelId, string? assignedCsv)
    {
        if (string.IsNullOrWhiteSpace(channelId) || string.IsNullOrWhiteSpace(assignedCsv))
        {
            return false;
        }

        var assigned = assignedCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return assigned.Contains(channelId.Trim(), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Checks whether a Discord channel is assigned on ANY bot row (the chat
    /// event does not carry which bot received it, so the per-bot assignment
    /// lists are unioned). Data-layer failures fail OPEN (log + allow) so a
    /// transient read error never silently stops earning.
    /// </summary>
    private async Task<bool> IsDiscordChannelAssignedAsync(string channelId)
    {
        try
        {
            var bots = await _dataService.GetDiscordBotsAsync();
            foreach (var bot in bots)
            {
                var assignedCsv = await _dataService.GetAssignedChannelIdsAsync(bot.Id);
                if (IsDiscordChannelAllowed(channelId, assignedCsv))
                {
                    return true;
                }
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to read DiscordBots.AssignedChannelIds — allowing Discord chat activity points for channel {ChannelId}",
                channelId);
            return true;
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

        var dailyUnified = await ResolveUnifiedTargetAsync(username, platform);
        if (dailyUnified != null)
        {
            (username, platform) = (dailyUnified.Username, dailyUnified.Platform);
        }

        // Hold the account gate across the claimed-today check -> streak save ->
        // payout so two concurrent !daily commands cannot both pass the check
        // and double-claim (each bumping the streak twice).
        var dailyGate = GetAccountGate(username, platform);
        await dailyGate.WaitAsync();
        try
        {
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
            // Ungated add: this method already holds the account gate.
            await AddCurrencyUngatedAsync(username, platform, bonus,
                $"Daily bonus (streak: {streak.CurrentStreak})", "DailyBonus", platform, username);

            var streakText = streak.CurrentStreak > 1 ? $" 🔥 {streak.CurrentStreak}-day streak!" : "";
            return (true, bonus, streak.CurrentStreak,
                $"@{username} claimed {bonus:N0} {currencyName}!{streakText}");
        }
        finally
        {
            dailyGate.Release();
        }
    }

    public async Task AwardFirstChatBonusAsync(string username, string platform)
    {
        var settings = await GetSettingsRequiredAsync();
        if (!settings.FirstChatBonusEnabled) return;

        // Hold the account gate across the record-exists check and the award:
        // a viewer's first two messages can be handled concurrently (EventBus
        // runs up to 4 handlers), and both would otherwise see no record and
        // double-pay the first-chat bonus.
        var firstTarget = await ResolveUnifiedTargetAsync(username, platform);
        var firstCanonicalUser = firstTarget?.Username ?? username;
        var firstCanonicalPlatform = firstTarget?.Platform ?? platform;

        var firstGate = GetAccountGate(firstCanonicalUser, firstCanonicalPlatform);
        await firstGate.WaitAsync();
        try
        {
            // Check if user already has a currency record (not first time)
            var existing = await _dataService.GetUserCurrencyAsync(firstCanonicalUser, firstCanonicalPlatform);
            if (existing != null) return;

            await AddCurrencyUngatedAsync(firstCanonicalUser, firstCanonicalPlatform,
                settings.FirstChatBonusAmount, "First-time chatter bonus", "FirstChat", platform, username);

            _logger.LogInformation("Awarded first-chat bonus to {Username} ({Platform})", username, platform);
        }
        finally
        {
            firstGate.Release();
        }
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
        // Page through ALL currency users on a stable identity key (Username,
        // Platform) instead of a single capped leaderboard read: the old
        // GetCurrencyLeaderboardAsync(10000) silently skipped every viewer past
        // 10k. Stable-key paging also keeps OFFSET pagination correct while the
        // awards themselves mutate balances (which would reshuffle Balance order).
        const int pageSize = 500;
        int count = 0;
        int offset = 0;

        while (true)
        {
            var page = (await _dataService.GetCurrencyUsersPagedAsync(pageSize, offset, platform)).ToList();

            foreach (var user in page)
            {
                await AddCurrencyAsync(user.Username, user.Platform, amount, reason, "Admin");
                count++;
            }

            if (page.Count < pageSize)
            {
                break;
            }

            offset += pageSize;
        }

        _logger.LogInformation("Gave {Amount} currency to {Count} users on {Platform}: {Reason}",
            amount, count, platform, reason);
        return count;
    }

    public async Task<EconomyStats> GetEconomyStatsAsync()
    {
        // SQL-side aggregation (COUNT/SUM pushed into SQLite via IDataService) —
        // the old implementation loaded up to 100k UserCurrency rows and only the
        // newest 10k transactions, so totals went silently wrong past 10k/day.
        var (userCount, totalInCirculation) = await _dataService.GetCurrencyEconomyTotalsAsync();
        var activeUsers = (int)Math.Min(userCount, int.MaxValue);
        var averageBalance = activeUsers > 0 ? (double)totalInCirculation / activeUsers : 0;

        var todayStart = DateTime.UtcNow.Date;
        var (totalEarnedToday, totalSpentToday) = await _dataService.GetCurrencyTransactionTotalsSinceAsync(todayStart);

        return new EconomyStats(totalInCirculation, totalEarnedToday, totalSpentToday, activeUsers, averageBalance);
    }

    /// <summary>
    /// Applies the full earn-multiplier stack to a base award amount:
    /// platform multiplier x active boost (highest wins) x loyalty-tier multiplier
    /// x role multiplier (only where role flags are known; highest role wins)
    /// x multi-platform link bonus (when available). A bonus failure is logged
    /// and treated as 1.0 so earning never breaks. The combined result is
    /// consistently floored to an integer.
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

        double final = amount * effectiveMultiplier * boostMultiplier * tierMultiplier;

        // Multi-platform link bonus (optional dependency; null when not wired by DI).
        if (_multiPlatformBonus != null)
        {
            try
            {
                final *= await _multiPlatformBonus.GetMultiplierAsync(username, platform);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Multi-platform bonus failed");
            }
        }

        return (int)Math.Floor(final);
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
