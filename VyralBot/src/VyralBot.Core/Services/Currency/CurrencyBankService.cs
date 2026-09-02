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

        // Check wallet balance
        var balance = await _currencyService.GetBalanceAsync(username, platform);
        if (balance < amount)
            return (false, $"Insufficient wallet balance (have {balance:N0}, need {amount:N0}).");

        // Check max bank balance
        var account = await GetOrCreateAccountAsync(username, platform);
        if (bankSettings.MaxBankBalance > 0 && account.BankBalance + amount > bankSettings.MaxBankBalance)
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

    public async Task<(bool Success, string Message)> WithdrawAsync(string username, string platform, int amount)
    {
        if (amount <= 0)
            return (false, "Withdrawal amount must be positive.");

        var bankSettings = await GetBankSettingsAsync();
        if (!bankSettings.Enabled)
            return (false, "Banking is currently disabled.");

        var account = await _dataService.GetUserBankAccountAsync(username, platform);
        if (account == null || account.BankBalance < amount)
            return (false, $"Insufficient bank balance (have {account?.BankBalance ?? 0:N0}).");

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

        var settings = await _currencyService.GetSettingsAsync();
        var currencyName = settings?.CurrencyName ?? "Points";

        var penaltyText = penalty > 0 ? $" (penalty: {penalty:N0})" : "";
        _logger.LogInformation("{Username} ({Platform}) withdrew {Amount} from bank (penalty: {Penalty})",
            username, platform, amount, penalty);

        return (true, $"🏦 Withdrew {netAmount:N0} {currencyName}{penaltyText}. Bank balance: {account.BankBalance:N0}");
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
