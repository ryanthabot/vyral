using VyralBot.Data.Entities;

namespace VyralBot.Core.Services.Currency;

/// <summary>
/// Service for managing user bank accounts with deposits, withdrawals, and interest.
/// </summary>
public interface ICurrencyBankService
{
    /// <summary>
    /// Deposit currency from wallet to bank.
    /// </summary>
    Task<(bool Success, string Message)> DepositAsync(string username, string platform, int amount);

    /// <summary>
    /// Withdraw currency from bank to wallet.
    /// </summary>
    Task<(bool Success, string Message)> WithdrawAsync(string username, string platform, int amount);

    /// <summary>
    /// Get user's bank account info.
    /// </summary>
    Task<UserBankAccount?> GetBankAccountAsync(string username, string platform);

    /// <summary>
    /// Process daily interest payouts for all bank accounts.
    /// </summary>
    Task<int> ProcessInterestPayoutsAsync();

    /// <summary>
    /// Get bank settings from currency settings.
    /// </summary>
    Task<BankSettings> GetBankSettingsAsync();
}

public record BankSettings(
    bool Enabled,
    double DailyInterestRate,
    int MaxBankBalance,
    double WithdrawalPenaltyPercent,
    int WithdrawalCooldownMinutes);
