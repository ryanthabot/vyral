using System.IO;
using Microsoft.Extensions.Logging.Abstractions;
using VyralBot.Data;
using VyralBot.Data.Entities;
using Xunit;

namespace VyralBot.UI.Tests;

/// <summary>
/// Wave-12 coverage for the wave-11 watchtime deterministic ordering fix:
/// every UserWatchtime query now carries explicit tie-breakers
/// (Username COLLATE NOCASE ASC, then Platform ASC) behind the primary sort,
/// so leaderboards are stable across queries and machines instead of
/// depending on SQLite's row-visit order.
///
/// Runs against a real <see cref="DapperDataService"/> on a temp SQLite file
/// (the DataLayerNewTablesTests pattern). Rows are inserted in deliberately
/// scrambled order; without the tie-breakers the equal-minutes block would
/// come back in rowid order (zeta, alice, Bob, ALICE, dave) and the
/// case-sensitive spelling ("ALICE" before "alice" before "Bob") would
/// betray a BINARY collation leak.
/// </summary>
public class Wave12WatchtimeTieBreakerTests
{
    private static async Task<DapperDataService> CreateServiceAsync()
    {
        var path = Path.Combine(Path.GetTempPath(), $"vyral-wave12-watch-{Guid.NewGuid():N}.db");
        var svc = new DapperDataService($"Data Source={path}", NullLogger<DapperDataService>.Instance);
        await svc.EnsureDatabaseCreatedAsync();
        return svc;
    }

    private static UserWatchtime Row(
        string username,
        string platform,
        int totalMinutes,
        bool isActive = false,
        DateTime? lastSeenAt = null) => new()
    {
        Username = username,
        Platform = platform,
        TotalMinutes = totalMinutes,
        IsActive = isActive,
        LastSeenAt = lastSeenAt ?? DateTime.UtcNow
    };

    private static async Task<List<UserWatchtime>> SeedScrambledTiesAsync(DapperDataService svc)
    {
        // Everything except "carol" ties at 100 minutes; "eve" outranks the
        // tie block; "carol" sits below it. Insertion order is scrambled and
        // mixes character cases.
        var rows = new[]
        {
            Row("zeta", "Kick", 100),
            Row("alice", "Twitch", 100),
            Row("Bob", "Discord", 100),
            Row("ALICE", "Kick", 100),
            Row("dave", "Twitch", 100),
            Row("carol", "Twitch", 90),
            Row("eve", "Twitch", 500)
        };
        foreach (var row in rows)
        {
            await svc.SaveUserWatchtimeAsync(row);
        }
        return rows.ToList();
    }

    [Fact]
    public async Task GetAllUserWatchtime_TiesBreakByUsernameNocaseThenPlatform()
    {
        var svc = await CreateServiceAsync();
        await SeedScrambledTiesAsync(svc);

        var ordered = (await svc.GetAllUserWatchtimeAsync()).ToList();

        // TotalMinutes DESC first: eve(500), then the 100-minute tie block
        // sorted Username COLLATE NOCASE ASC (alice < bob < dave < zeta —
        // case-insensitive, NOT binary where "ALICE" < "Bob" < "alice"),
        // and the alice/ALICE pair breaks to Platform ASC (Kick < Twitch).
        Assert.Equal(
            new[] { "eve", "ALICE", "alice", "Bob", "dave", "zeta", "carol" },
            ordered.Select(w => w.Username));
        Assert.Equal("Kick", ordered[1].Platform);
        Assert.Equal("Twitch", ordered[2].Platform);
        // The lower-minutes row is behind the entire tie block.
        Assert.Equal(90, ordered[^1].TotalMinutes);
    }

    [Fact]
    public async Task GetTopWatchtimeUsers_LimitAppliesAfterTieBreak()
    {
        var svc = await CreateServiceAsync();
        await SeedScrambledTiesAsync(svc);

        var top = (await svc.GetTopWatchtimeUsersAsync(3)).ToList();

        Assert.Equal(3, top.Count);
        // Same deterministic order as the unbounded query, truncated.
        Assert.Equal(
            new[] { "eve", "ALICE", "alice" },
            top.Select(w => w.Username));
        Assert.Equal("Kick", top[1].Platform);
        Assert.Equal("Twitch", top[2].Platform);
    }

    [Fact]
    public async Task GetTopWatchtimeUsers_LimitLargerThanTable_ReturnsAll()
    {
        var svc = await CreateServiceAsync();
        await SeedScrambledTiesAsync(svc);

        Assert.Equal(7, (await svc.GetTopWatchtimeUsersAsync(50)).Count());
    }

    [Fact]
    public async Task GetActiveViewers_TiesOnLastSeenBreakByUsernameThenPlatform()
    {
        var svc = await CreateServiceAsync();
        var seen = new DateTime(2026, 11, 7, 12, 0, 0, DateTimeKind.Utc);

        // Four viewers (zeta/BETA/beta/alpha) share the exact same LastSeenAt;
        // "older" was seen an hour earlier; "ghost" has by far the most minutes
        // but is inactive — it must not appear at all.
        foreach (var row in new[]
        {
            Row("zeta", "Kick", 10, isActive: true, lastSeenAt: seen),
            Row("BETA", "Kick", 10, isActive: true, lastSeenAt: seen),
            Row("beta", "Twitch", 10, isActive: true, lastSeenAt: seen),
            Row("alpha", "Twitch", 10, isActive: true, lastSeenAt: seen),
            Row("older", "Discord", 10, isActive: true, lastSeenAt: seen.AddHours(-1)),
            Row("ghost", "Twitch", 9999, isActive: false, lastSeenAt: seen)
        })
        {
            await svc.SaveUserWatchtimeAsync(row);
        }

        var active = (await svc.GetActiveViewersAsync()).ToList();

        // LastSeenAt DESC (alpha/BETA/beta block before "older"), then
        // Username COLLATE NOCASE ASC (alpha < beta), with the beta pair
        // breaking to Platform ASC (Kick < Twitch).
        Assert.Equal(
            new[] { "alpha", "BETA", "beta", "zeta", "older" },
            active.Select(w => w.Username));
        Assert.Equal("Kick", active[1].Platform);
        Assert.Equal("Twitch", active[2].Platform);
    }

    [Fact]
    public async Task GetAllUserWatchtime_EmptyTable_ReturnsEmpty()
    {
        var svc = await CreateServiceAsync();

        Assert.Empty(await svc.GetAllUserWatchtimeAsync());
        Assert.Empty(await svc.GetTopWatchtimeUsersAsync());
        Assert.Empty(await svc.GetActiveViewersAsync());
    }
}
