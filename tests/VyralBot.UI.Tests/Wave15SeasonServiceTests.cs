using System.IO;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using VyralBot.Core.Services.Currency;
using VyralBot.Data;
using VyralBot.Data.Entities;
using Xunit;

namespace VyralBot.UI.Tests;

/// <summary>
/// Wave-15 coverage for loyalty seasons (<see cref="SeasonService"/> on the
/// CurrencySeasons table): first-season open without reset, the competitive
/// reset semantics (balances zeroed ONLY — TotalEarned/TotalSpent aggregates
/// and the CurrencyTransactions audit trail are never touched, rows are never
/// deleted), the snapshot-before-reset ordering (closing season records the
/// pre-reset top-20 standings), season history ordering, day counting, the
/// "!season" reply formatting and SeasonService.ParseTopCount tolerance.
///
/// Runs the REAL service on the REAL DapperDataService backed by a temp
/// SQLite file (same style as <see cref="DataLayerNewTablesTests"/>) so the
/// single-transaction close+insert+reset path is exercised against actual SQL.
/// </summary>
public class Wave15SeasonServiceTests
{
    private sealed class Harness
    {
        public Harness()
        {
            DbPath = Path.Combine(Path.GetTempPath(), $"vyral-w15-{Guid.NewGuid():N}.db");
            Data = new DapperDataService($"Data Source={DbPath}", NullLogger<DapperDataService>.Instance);
            Season = new SeasonService(NullLogger<SeasonService>.Instance, Data);
        }

        public string DbPath { get; }
        public DapperDataService Data { get; }
        public SeasonService Season { get; }

        public Task InitAsync() => Data.EnsureDatabaseCreatedAsync();

        public async Task SeedBalanceAsync(string username, int balance, int totalEarned = 0, int totalSpent = 0, string platform = "Twitch")
            => await Data.SaveUserCurrencyAsync(new UserCurrency
            {
                Username = username,
                Platform = platform,
                Balance = balance,
                TotalEarned = totalEarned,
                TotalSpent = totalSpent
            });

        /// <summary>Backdates the open season's start (day-count test) with the same
        /// TEXT format the SQLite driver writes for DateTime parameters.</summary>
        public void BackdateOpenSeason(DateTime startedAtUtc)
        {
            using var conn = new SqliteConnection($"Data Source={DbPath}");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE CurrencySeasons SET StartedAt = @Started WHERE EndedAt IS NULL";
            cmd.Parameters.AddWithValue("@Started", startedAtUtc);
            cmd.ExecuteNonQuery();
        }
    }

    // ── name validation ────────────────────────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task StartSeason_EmptyName_Rejected_NothingWritten(string name)
    {
        var h = new Harness();
        await h.InitAsync();

        var result = await h.Season.StartSeasonAsync(name, resetBalances: false);

        Assert.False(result.Success);
        Assert.Null(result.Season);
        Assert.Null(await h.Season.GetCurrentSeasonAsync());
        Assert.Empty(await h.Season.GetSeasonsAsync());
    }

    // ── first season / no reset ────────────────────────────────────────

    [Fact]
    public async Task FirstSeason_Opens_WithoutTouchingBalances()
    {
        var h = new Harness();
        await h.InitAsync();
        await h.SeedBalanceAsync("alice", 500);
        await h.SeedBalanceAsync("bob", 200);

        var result = await h.Season.StartSeasonAsync("Season 1", resetBalances: false);

        Assert.True(result.Success);
        Assert.Equal(0, result.ResetBalances);
        var current = await h.Season.GetCurrentSeasonAsync();
        Assert.NotNull(current);
        Assert.Equal("Season 1", current!.Name);
        Assert.Null(current.EndedAt);
        Assert.Equal(500, (await h.Data.GetUserCurrencyAsync("alice", "Twitch"))!.Balance);
        Assert.Equal(200, (await h.Data.GetUserCurrencyAsync("bob", "Twitch"))!.Balance);
    }

    [Fact]
    public async Task NoSeason_CurrentAndStatusAndMessage_AreNull()
    {
        var h = new Harness();
        await h.InitAsync();

        Assert.Null(await h.Season.GetCurrentSeasonAsync());
        Assert.Null(await h.Season.GetStatusAsync());
        Assert.Null(await h.Season.GetSeasonMessageAsync());
    }

    // ── snapshot-before-reset + competitive reset semantics ────────────

    [Fact]
    public async Task StartNextSeason_SnapshotsPreResetTop20_IntoClosingSeason()
    {
        var h = new Harness();
        await h.InitAsync();
        // 25 users so the snapshot cap (20) is exercised.
        for (var i = 1; i <= 25; i++)
        {
            await h.SeedBalanceAsync($"viewer{i}", balance: 1000 - i * 10);
        }
        await h.Season.StartSeasonAsync("Season 1", resetBalances: false);

        var result = await h.Season.StartSeasonAsync("Season 2", resetBalances: true);

        Assert.True(result.Success);
        Assert.Equal(25, result.ResetBalances);

        // History: newest first — the open Season 2, then the closed Season 1
        // carrying the PRE-RESET standings.
        var history = await h.Season.GetSeasonsAsync();
        Assert.Equal(2, history.Count);
        var open = history[0];
        var closed = history[1];
        Assert.Equal("Season 2", open.Name);
        Assert.Null(open.EndedAt);
        Assert.Equal("Season 1", closed.Name);
        Assert.NotNull(closed.EndedAt);

        var top = SeasonService.ParseTopCount(closed.TopCount);
        Assert.NotNull(top);
        Assert.Equal(SeasonService.SnapshotSize, top!.Count);
        Assert.Equal("viewer1", top[0].Username);           // highest pre-reset balance
        Assert.Equal(990, top[0].Balance);
        Assert.Equal("viewer20", top[^1].Username);          // cap cuts viewer21..25

        // Every balance was zeroed in the same transaction.
        for (var i = 1; i <= 25; i++)
        {
            Assert.Equal(0, (await h.Data.GetUserCurrencyAsync($"viewer{i}", "Twitch"))!.Balance);
        }
    }

    [Fact]
    public async Task ResetZeroesBalancesOnly_EarnedHistoryAndTransactionsStay()
    {
        var h = new Harness();
        await h.InitAsync();
        await h.SeedBalanceAsync("alice", balance: 500, totalEarned: 800, totalSpent: 300);
        await h.Data.SaveCurrencyTransactionAsync(new CurrencyTransaction
        {
            Id = Guid.NewGuid().ToString(),
            Username = "alice",
            Platform = "Twitch",
            Amount = 100,
            TransactionType = "Earn",
            Reason = "watch time",
            BalanceBefore = 400,
            BalanceAfter = 500,
            Timestamp = DateTime.UtcNow
        });

        await h.Season.StartSeasonAsync("Season 1", resetBalances: false);
        await h.Season.StartSeasonAsync("Season 2", resetBalances: true);

        var row = await h.Data.GetUserCurrencyAsync("alice", "Twitch");
        Assert.NotNull(row);
        Assert.Equal(0, row!.Balance);          // reset target: the balance only
        Assert.Equal(800, row.TotalEarned);     // lifetime aggregates are history
        Assert.Equal(300, row.TotalSpent);

        // The audit trail is never deleted.
        var transactions = await h.Data.GetCurrencyTransactionsAsync("alice");
        Assert.Single(transactions);
    }

    // ── status / "!season" message ─────────────────────────────────────

    [Fact]
    public async Task GetSeasonMessage_WithPreviousWinner_ExactFormat()
    {
        var h = new Harness();
        await h.InitAsync();
        await h.SeedBalanceAsync("alice", 900);
        await h.SeedBalanceAsync("bob", 400);
        await h.Season.StartSeasonAsync("Season 1", resetBalances: false);
        await h.Season.StartSeasonAsync("Season 2", resetBalances: false);

        var message = await h.Season.GetSeasonMessageAsync();

        Assert.Equal("Season: Season 2 — day 1. Previous winner: alice.", message);
    }

    [Fact]
    public async Task GetSeasonMessage_FirstSeason_OmitsWinnerSentence()
    {
        var h = new Harness();
        await h.InitAsync();
        await h.Season.StartSeasonAsync("Season 1", resetBalances: false);

        Assert.Equal("Season: Season 1 — day 1.", await h.Season.GetSeasonMessageAsync());
    }

    [Fact]
    public async Task GetStatus_DayCounts_ElapsedDays()
    {
        var h = new Harness();
        await h.InitAsync();
        await h.Season.StartSeasonAsync("Season 1", resetBalances: false);
        h.BackdateOpenSeason(DateTime.UtcNow.AddDays(-3));

        var status = await h.Season.GetStatusAsync();

        Assert.NotNull(status);
        Assert.Equal("Season 1", status!.Name);
        Assert.Equal(4, status.Day); // day 1 on the start date + 3 elapsed
    }

    // ── snapshot parsing tolerance ─────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json [")]
    public void ParseTopCount_TolerantOfBadColumns(string? json)
    {
        Assert.Null(SeasonService.ParseTopCount(json));
    }

    [Fact]
    public void ParseTopCount_RoundTripsEntries()
    {
        var parsed = SeasonService.ParseTopCount(
            "[{\"username\":\"alice\",\"platform\":\"Twitch\",\"balance\":1200}]");

        Assert.NotNull(parsed);
        var entry = Assert.Single(parsed!);
        Assert.Equal("alice", entry.Username);
        Assert.Equal("Twitch", entry.Platform);
        Assert.Equal(1200, entry.Balance);
    }
}
