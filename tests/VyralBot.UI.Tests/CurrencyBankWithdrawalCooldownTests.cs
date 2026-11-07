using Microsoft.Extensions.Logging.Abstractions;
using VyralBot.Core.Services.Currency;
using VyralBot.Data.Entities;
using Xunit;

namespace VyralBot.UI.Tests;

/// <summary>
/// Marathon wave-1 fix: the bank WITHDRAWAL COOLDOWN in
/// <see cref="CurrencyBankService.WithdrawAsync"/>. The cooldown must:
///
///   1. reject a withdrawal while the viewer's last SUCCESSFUL withdrawal is
///      still inside the window (both the in-memory session map and, after a
///      restart, the "BankWithdrawal" audit-row fallback),
///   2. allow it once the window has passed (audit-row fallback path),
///   3. stay PERMISSIVE when the cooldown is unset/0 — no cooldown at all.
///
/// Runs the real service on the wave-2 fakes
/// (<see cref="Wave2TestDataService"/> + <see cref="Wave2TestCurrencyService"/>);
/// the fakes mirror the production audit trail, where every successful
/// withdrawal writes a "BankWithdrawal" CurrencyTransaction row. Test rows are
/// seeded directly into the fake with a controlled Timestamp because the
/// in-session path stamps DateTime.UtcNow itself.
/// </summary>
public class CurrencyBankWithdrawalCooldownTests
{
    private const string Platform = "Twitch";

    private sealed class Harness
    {
        public Harness(int cooldownMinutes)
        {
            Data = new Wave2TestDataService();
            Currency = new Wave2TestCurrencyService(Data)
            {
                Settings = new CurrencySetting
                {
                    BankEnabled = true,
                    BankWithdrawalCooldownMinutes = cooldownMinutes
                }
            };
            Bank = new CurrencyBankService(
                NullLogger<CurrencyBankService>.Instance, Data, Currency);
        }

        public Wave2TestDataService Data { get; }
        public Wave2TestCurrencyService Currency { get; }
        public CurrencyBankService Bank { get; }

        public void SeedAccount(string username, int balance)
            => Data.BankAccounts[username + "|" + Platform] = new UserBankAccount
            {
                Username = username,
                Platform = Platform,
                BankBalance = balance
            };

        /// <summary>Seeds a cross-restart audit row exactly like a past successful withdrawal.</summary>
        public void SeedWithdrawalAuditRow(string username, DateTime withdrawnAtUtc)
            => Data.Transactions.Add(new CurrencyTransaction
            {
                Username = username,
                Platform = Platform,
                Amount = 100,
                TransactionType = "BankWithdrawal",
                Reason = "Bank withdrawal",
                Timestamp = withdrawnAtUtc
            });
    }

    [Fact]
    public async Task FirstWithdrawal_NoPriorState_IsAllowed()
    {
        var t = new Harness(cooldownMinutes: 30);
        t.SeedAccount("fresh", 1000);

        var (success, message) = await t.Bank.WithdrawAsync("fresh", Platform, 100);

        Assert.True(success, message);
        Assert.Equal(900, t.Data.BankAccounts["fresh|" + Platform].BankBalance);
        Assert.Equal(100, t.Currency.Balances["fresh|" + Platform]); // wallet credited
    }

    [Fact]
    public async Task SecondWithdrawal_WithinCooldown_IsRejected_InMemoryPath()
    {
        var t = new Harness(cooldownMinutes: 30);
        t.SeedAccount("rapid", 1000);

        var first = await t.Bank.WithdrawAsync("rapid", Platform, 100);
        var (success, message) = await t.Bank.WithdrawAsync("rapid", Platform, 100);

        Assert.True(first.Success, first.Message);
        Assert.False(success);
        Assert.Contains("cooldown", message, StringComparison.OrdinalIgnoreCase);
        // The rejection mutated nothing: bank balance and wallet unchanged.
        Assert.Equal(900, t.Data.BankAccounts["rapid|" + Platform].BankBalance);
        Assert.Equal(100, t.Currency.Balances["rapid|" + Platform]);
        Assert.Single(t.Currency.Adds, a => a.TransactionType == "BankWithdrawal");
    }

    [Fact]
    public async Task Withdrawal_AfterCooldownPassed_IsAllowed_AuditRowFallbackPath()
    {
        // Simulates an app restart: no in-memory state, only the audit trail.
        var t = new Harness(cooldownMinutes: 30);
        t.SeedAccount("returner", 1000);
        t.SeedWithdrawalAuditRow("returner", DateTime.UtcNow.AddHours(-2)); // window long gone

        var (success, message) = await t.Bank.WithdrawAsync("returner", Platform, 100);

        Assert.True(success, message);
        Assert.Equal(900, t.Data.BankAccounts["returner|" + Platform].BankBalance);
        Assert.Equal(100, t.Currency.Balances["returner|" + Platform]);
    }

    [Fact]
    public async Task Withdrawal_WithinCooldown_IsRejected_AuditRowFallbackPath()
    {
        var t = new Harness(cooldownMinutes: 30);
        t.SeedAccount("restarted", 1000);
        t.SeedWithdrawalAuditRow("restarted", DateTime.UtcNow.AddMinutes(-5)); // 25 min left

        var (success, message) = await t.Bank.WithdrawAsync("restarted", Platform, 100);

        Assert.False(success);
        Assert.Contains("cooldown", message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("25", message); // ~25 minutes remaining
        // Nothing was debited or credited on a rejection.
        Assert.Equal(1000, t.Data.BankAccounts["restarted|" + Platform].BankBalance);
        Assert.False(t.Currency.Balances.ContainsKey("restarted|" + Platform));
        Assert.Equal(0, t.Currency.Adds.Count(a => a.TransactionType == "BankWithdrawal"));
    }

    [Fact]
    public async Task CooldownUnset_IsPermissive_EvenWithFreshAuditRow()
    {
        // 0 (or never configured — the CurrencySetting default) means NO cooldown.
        var t = new Harness(cooldownMinutes: 0);
        t.SeedAccount("chill", 1000);
        t.SeedWithdrawalAuditRow("chill", DateTime.UtcNow.AddMinutes(-1));

        var (success, message) = await t.Bank.WithdrawAsync("chill", Platform, 100);

        Assert.True(success, message);
        Assert.Equal(900, t.Data.BankAccounts["chill|" + Platform].BankBalance);
    }
}
