using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using VyralBot.Data;
using VyralBot.Data.Entities;

namespace VyralBot.Core.Services.Currency;

/// <summary>
/// Service for managing user bank accounts with deposits, withdrawals, and interest.
/// </summary>
public class CurrencyBankService : ICurrencyBankService
{
    private readonly ILogger<CurrencyBankService> _logger;
    private readonly IDataService _dataService;
    private readonly ICurrencyService _currencyService;

    // Session-scoped map of the last successful withdrawal per viewer. Keyed on
    // the lower-cased "username:platform" identity the bank was called with, so
    // the cooldown cannot be bypassed by casing changes. Durable across app
    // restarts via the CurrencyTransactions audit trail (see GetLastWithdrawalUtcAsync).
    private readonly ConcurrentDictionary<string, DateTime> _lastWithdrawalUtc = new();

    // Bank rows are mutated read-modify-write (read BankBalance, add/subtract,
    // save the whole row). Two concurrent !withdraw commands would both read
    // the same balance and double-withdraw (the wallet credit makes it real
    // money creation); two concurrent deposits can lose one. These per-account
    // gates serialize bank mutations for one viewer while different viewers
    // proceed in parallel. Lock order is always bank gate -> currency gate
    // (Deposit/Withdraw call into CurrencyService while holding it), so it can
    // never deadlock against CurrencyService's own account gates.
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _bankAccountGates = new();

    // Bounded eviction (mirrors CurrencyService._accountGates): this static map
    // would otherwise grow forever — one semaphore per viewer ever seen — in a
    // 24/7 process. Past the threshold, a rate-limited sweep on gate access
    // removes gates nobody holds (CurrentCount == 1); held gates are never
    // evicted, so in-flight bank mutations stay serialized. An evicted idle
    // viewer simply gets a fresh gate on its next deposit/withdraw.
    private const int BankGateEvictionThreshold = 10_000;
    private static readonly long BankGateSweepIntervalTicks = TimeSpan.FromMinutes(1).Ticks;
    private static long _nextBankGateSweepTicksUtc;

    private static SemaphoreSlim GetBankAccountGate(string username, string platform)
    {
        var gate = _bankAccountGates.GetOrAdd(
            $"{username.ToLowerInvariant()}:{platform.ToLowerInvariant()}",
            _ => new SemaphoreSlim(1, 1));
        EvictIdleBankGatesIfNeeded();
        return gate;
    }

    /// <summary>
    /// Trims <see cref="_bankAccountGates"/> when it grows past
    /// <see cref="BankGateEvictionThreshold"/>: at most one sweep per minute
    /// (Interlocked claim on the next-sweep timestamp), removing only
    /// semaphores nobody holds. Best-effort and allocation-free when idle.
    /// </summary>
    private static void EvictIdleBankGatesIfNeeded()
    {
        if (_bankAccountGates.Count <= BankGateEvictionThreshold)
        {
            return;
        }

        var now = DateTime.UtcNow.Ticks;
        var next = Interlocked.Read(ref _nextBankGateSweepTicksUtc);
        if (now < next)
        {
            return;
        }

        // Claim the sweep slot; concurrent callers keep using their gates.
        if (Interlocked.CompareExchange(
                ref _nextBankGateSweepTicksUtc, now + BankGateSweepIntervalTicks, next) != next)
        {
            return;
        }

        foreach (var pair in _bankAccountGates)
        {
            if (pair.Value.CurrentCount == 1)
            {
                // Nothing to dispose: an unheld SemaphoreSlim owns no handles.
                _bankAccountGates.TryRemove(pair.Key, out _);
            }
        }
    }

    public CurrencyBankService(
        ILogger<CurrencyBankService> logger,
        IDataService dataService,
        ICurrencyService currencyService)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
        _currencyService = currencyService ?? throw new ArgumentNullException(nameof(currencyService));
    }

    public async Task<BankSettings> GetBankSettingsAsync()
    {
        var settings = await _currencyService.GetSettingsAsync();
        return new BankSettings(
            Enabled: settings?.BankEnabled ?? false,
            DailyInterestRate: settings?.BankInterestRate ?? 1.0,
            MaxBankBalance: settings?.BankMaxBalance ?? 1000000,
            WithdrawalPenaltyPercent: settings?.BankWithdrawalPenalty ?? 0.0,
            WithdrawalCooldownMinutes: settings?.BankWithdrawalCooldownMinutes ?? 0
        );
    }

    public async Task<(bool Success, string Message)> DepositAsync(string username, string platform, int amount)
    {
        if (amount <= 0)
            return (false, "Deposit amount must be positive.");

        var bankSettings = await GetBankSettingsAsync();
        if (!bankSettings.Enabled)
            return (false, "Banking is currently disabled.");

        var gate = GetBankAccountGate(username, platform);
        await gate.WaitAsync();
        try
        {
            // Check wallet balance
            var balance = await _currencyService.GetBalanceAsync(username, platform);
            if (balance < amount)
                return (false, $"Insufficient wallet balance (have {balance:N0}, need {amount:N0}).");

            // Check max bank balance
            var account = await GetOrCreateAccountAsync(username, platform);
            if (bankSettings.MaxBankBalance > 0 && (long)account.BankBalance + amount > bankSettings.MaxBankBalance)
                return (false, $"Deposit would exceed bank cap of {bankSettings.MaxBankBalance:N0}.");

            // Remove from wallet
            var removed = await _currencyService.RemoveCurrencyAsync(
                username, platform, amount, "Bank deposit", "BankDeposit");
            if (!removed)
                return (false, "Failed to deduct from wallet.");

            // Add to bank
            account.BankBalance += amount;
            account.TotalDeposited += amount;
            account.UpdatedAt = DateTime.UtcNow;
            await _dataService.SaveUserBankAccountAsync(account);

            var settings = await _currencyService.GetSettingsAsync();
            var currencyName = settings?.CurrencyName ?? "Points";

            _logger.LogInformation("{Username} ({Platform}) deposited {Amount} to bank", username, platform, amount);
            return (true, $"🏦 Deposited {amount:N0} {currencyName}. Bank balance: {account.BankBalance:N0}");
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<(bool Success, string Message)> WithdrawAsync(string username, string platform, int amount)
    {
        if (amount <= 0)
            return (false, "Withdrawal amount must be positive.");

        var bankSettings = await GetBankSettingsAsync();
        if (!bankSettings.Enabled)
            return (false, "Banking is currently disabled.");

        var gate = GetBankAccountGate(username, platform);
        await gate.WaitAsync();
        try
        {
            var account = await _dataService.GetUserBankAccountAsync(username, platform);
            if (account == null || account.BankBalance < amount)
                return (false, $"Insufficient bank balance (have {account?.BankBalance ?? 0:N0}).");

            // Enforce the configured withdrawal cooldown: reject while the viewer's
            // last successful withdrawal is still inside the cooldown window.
            // A setting of 0 (or unset) keeps withdrawals permissive (no cooldown).
            if (bankSettings.WithdrawalCooldownMinutes > 0)
            {
                var lastWithdrawal = await GetLastWithdrawalUtcAsync(username, platform);
                if (lastWithdrawal.HasValue)
                {
                    var cooldownEnd = lastWithdrawal.Value.AddMinutes(bankSettings.WithdrawalCooldownMinutes);
                    if (cooldownEnd > DateTime.UtcNow)
                    {
                        var minutesLeft = (int)Math.Ceiling((cooldownEnd - DateTime.UtcNow).TotalMinutes);
                        _logger.LogInformation(
                            "Withdrawal for {Username} ({Platform}) rejected: cooldown active for {Minutes} more minute(s)",
                            username, platform, minutesLeft);
                        return (false, $"🏦 Withdrawal on cooldown — try again in ~{minutesLeft} min.");
                    }
                }
            }

            // Calculate penalty
            var penalty = (int)(amount * bankSettings.WithdrawalPenaltyPercent / 100.0);
            var netAmount = amount - penalty;

            // Deduct from bank
            account.BankBalance -= amount;
            account.TotalWithdrawn += amount;
            account.UpdatedAt = DateTime.UtcNow;
            await _dataService.SaveUserBankAccountAsync(account);

            // Add net amount to wallet
            await _currencyService.AddCurrencyAsync(
                username, platform, netAmount, "Bank withdrawal", "BankWithdrawal");

            _lastWithdrawalUtc[GetWithdrawalKey(username, platform)] = DateTime.UtcNow;

            var settings = await _currencyService.GetSettingsAsync();
            var currencyName = settings?.CurrencyName ?? "Points";

            var penaltyText = penalty > 0 ? $" (penalty: {penalty:N0})" : "";
            _logger.LogInformation("{Username} ({Platform}) withdrew {Amount} from bank (penalty: {Penalty})",
                username, platform, amount, penalty);

            return (true, $"🏦 Withdrew {netAmount:N0} {currencyName}{penaltyText}. Bank balance: {account.BankBalance:N0}");
        }
        finally
        {
            gate.Release();
        }
    }

    private static string GetWithdrawalKey(string username, string platform)
        => $"{username.ToLowerInvariant()}:{platform.ToLowerInvariant()}";

    /// <summary>
    /// Resolve the viewer's last successful withdrawal time. The in-memory map
    /// covers the current session (including unified-balance redirected users,
    /// whose audit row is written under the canonical identity); the
    /// "BankWithdrawal" transaction trail backs it up across app restarts.
    /// </summary>
    private async Task<DateTime?> GetLastWithdrawalUtcAsync(string username, string platform)
    {
        if (_lastWithdrawalUtc.TryGetValue(GetWithdrawalKey(username, platform), out var recent))
            return recent;

        var transactions = await _dataService.GetCurrencyTransactionsAsync(username, platform, 100);
        var lastBankWithdrawal = transactions
            .FirstOrDefault(t => t.TransactionType == "BankWithdrawal");
        return lastBankWithdrawal?.Timestamp;
    }

    public async Task<UserBankAccount?> GetBankAccountAsync(string username, string platform)
    {
        return await _dataService.GetUserBankAccountAsync(username, platform);
    }

    public async Task<int> ProcessInterestPayoutsAsync()
    {
        var bankSettings = await GetBankSettingsAsync();
        if (!bankSettings.Enabled || bankSettings.DailyInterestRate <= 0)
            return 0;

        var accounts = await _dataService.GetAllBankAccountsAsync();
        int processed = 0;

        foreach (var account in accounts)
        {
            if (account.BankBalance <= 0)
                continue;

            // Only pay once per day
            if (account.LastInterestPayout.HasValue &&
                account.LastInterestPayout.Value.Date == DateTime.UtcNow.Date)
                continue;

            var interest = (int)(account.BankBalance * bankSettings.DailyInterestRate / 100.0);
            if (interest <= 0)
                continue;

            // Cap interest payout if it would exceed max
            if (bankSettings.MaxBankBalance > 0 &&
                account.BankBalance + interest > bankSettings.MaxBankBalance)
            {
                interest = bankSettings.MaxBankBalance - account.BankBalance;
                if (interest <= 0) continue;
            }

            var bankBalanceBefore = account.BankBalance;

            account.BankBalance += interest;
            account.TotalInterestEarned += interest;
            account.LastInterestPayout = DateTime.UtcNow;
            account.UpdatedAt = DateTime.UtcNow;
            await _dataService.SaveUserBankAccountAsync(account);

            // Record the interest in the transaction audit trail, consistent with
            // the other award paths (positive amount, balance before/after).
            await _dataService.SaveCurrencyTransactionAsync(new CurrencyTransaction
            {
                Username = account.Username,
                Platform = account.Platform,
                Amount = interest,
                TransactionType = "Interest",
                Reason = $"Bank interest ({bankSettings.DailyInterestRate}% daily)",
                BalanceBefore = bankBalanceBefore,
                BalanceAfter = account.BankBalance,
                Timestamp = DateTime.UtcNow
            });

            processed++;
        }

        if (processed > 0)
        {
            _logger.LogInformation("Processed interest payouts for {Count} bank accounts", processed);
        }

        return processed;
    }

    private async Task<UserBankAccount> GetOrCreateAccountAsync(string username, string platform)
    {
        var account = await _dataService.GetUserBankAccountAsync(username, platform);
        if (account != null)
            return account;

        account = new UserBankAccount
        {
            Username = username,
            Platform = platform
        };
        await _dataService.SaveUserBankAccountAsync(account);
        return account;
    }
}
