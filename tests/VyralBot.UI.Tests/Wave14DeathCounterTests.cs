using Xunit;
using Microsoft.Extensions.Logging.Abstractions;
using VyralBot.Core.Services.Counters;

namespace VyralBot.UI.Tests;

/// <summary>
/// Wave-14 coverage for <see cref="DeathCounterService"/> — the shared !death
/// counter family on top of the per-profile config rows. Exercises the
/// lifecycle math (+n / floor-at-0 / set-clamped / reset), every command
/// variant (!death, !death+, !death add [n], !death-, !death sub, !death set,
/// !death reset, !death help, unknown sub → usage), moderator gating, the
/// 50-event history cap with newest-first ordering, GetCountAsync defaults
/// and persistence across service instances (new service, same config rows).
/// Uses the wave-8 in-memory IDataService (real config-table behavior).
/// </summary>
public class Wave14DeathCounterTests
{
    private const string Viewer = "plainviewer";
    private const string Mod = "modperson";

    private static DeathCounterService Service(IntegrationWave8DataService? data = null)
        => new(NullLogger<DeathCounterService>.Instance, data ?? new IntegrationWave8DataService());

    private static Task<string?> Run(
        DeathCounterService service,
        string message,
        bool isModerator = true,
        string username = Mod,
        string platform = "Twitch")
        => service.ProcessCommandAsync(username, platform, isModerator, message);

    // ── GetCountAsync / read command ───────────────────────────────────

    [Fact]
    public async Task Count_StartsAtZero_WithoutStoredValue()
    {
        Assert.Equal(0, await Service().GetCountAsync());
    }

    [Fact]
    public async Task DeathCommand_Read_IsAvailableToEveryone()
    {
        Assert.Equal("💀 Deaths: 0", await Run(Service(), "!death", isModerator: false, username: Viewer));

        var data = new IntegrationWave8DataService();
        var service = Service(data);
        await service.SetAsync(12, "Tools page");

        Assert.Equal("💀 Deaths: 12", await Run(service, "!death", isModerator: false, username: Viewer));
    }

    [Theory]
    [InlineData("!dice 20")]
    [InlineData("!deathnote")]
    [InlineData("just talking")]
    public async Task NonDeathMessages_FallThrough_Null(string message)
    {
        Assert.Null(await Run(Service(), message));
    }

    // ── lifecycle math ─────────────────────────────────────────────────

    [Fact]
    public async Task Add_Increments_AndClampsToMax()
    {
        var service = Service();

        Assert.Equal(1, await service.AddAsync(1, Mod));
        Assert.Equal(6, await service.AddAsync(5, Mod));
        // Negative/zero deltas clamp up to +1, never subtract.
        Assert.Equal(7, await service.AddAsync(-4, Mod));
        Assert.Equal(8, await service.AddAsync(0, Mod));
    }

    [Fact]
    public async Task Subtract_FloorsAtZero()
    {
        var service = Service();

        Assert.Equal(0, await service.SubtractAsync(1, Mod)); // 0 - 1 floored
        await service.SetAsync(3, Mod);
        Assert.Equal(2, await service.SubtractAsync(1, Mod));
        Assert.Equal(0, await service.SubtractAsync(99, Mod)); // 2 - 99 floored
    }

    [Fact]
    public async Task Set_ClampsIntoZeroToMaxRange()
    {
        var service = Service();

        Assert.Equal(42, await service.SetAsync(42, Mod));
        Assert.Equal(0, await service.SetAsync(-5, Mod));
        // 999_999 is the hard ceiling even for absurd direct-set values.
        Assert.Equal(999_999, await service.SetAsync(2_000_000, Mod));
    }

    [Fact]
    public async Task Reset_ReturnsToZero()
    {
        var service = Service();
        await service.SetAsync(99, Mod);

        Assert.Equal(0, await service.ResetAsync(Mod));
        Assert.Equal(0, await service.GetCountAsync());
    }

    [Fact]
    public async Task Count_RaisesCountChangedWithNewValue()
    {
        var service = Service();
        var seen = new List<int>();
        service.CountChanged += (_, count) => seen.Add(count);

        await service.AddAsync(2, Mod);   // → 2
        await service.SubtractAsync(1, Mod); // → 1
        await service.ResetAsync(Mod);    // → 0

        Assert.Equal(new[] { 2, 1, 0 }, seen);
    }

    [Fact]
    public async Task Count_PersistsAcrossServiceInstances()
    {
        var data = new IntegrationWave8DataService();
        await Service(data).SetAsync(31, Mod);

        // New service over the same config rows = app restart.
        Assert.Equal(31, await Service(data).GetCountAsync());
    }

    // ── command variants ───────────────────────────────────────────────

    [Fact]
    public async Task DeathPlus_AddsOne()
    {
        var service = Service();

        Assert.Equal("💀 Deaths: 1", await Run(service, "!death+"));
        Assert.Equal("💀 Deaths: 2", await Run(service, "!death+"));
    }

    [Theory]
    [InlineData("!death+")]
    [InlineData("!death add")]
    [InlineData("!death-")]
    [InlineData("!death set 5")]
    [InlineData("!death reset")]
    public async Task Mutations_ByViewers_AreRejected(string message)
    {
        var service = Service();

        Assert.Equal(
            "Only moderators can change the death counter",
            await Run(service, message, isModerator: false, username: Viewer));
        Assert.Equal(0, await service.GetCountAsync()); // nothing leaked through
    }

    [Fact]
    public async Task DeathAdd_DefaultsToOne_ExplicitNumberAddsN()
    {
        var service = Service();

        Assert.Equal("💀 Deaths: 1", await Run(service, "!death add"));
        Assert.Equal("💀 Deaths: 4", await Run(service, "!death add 3"));
    }

    [Theory]
    [InlineData("!death add 5x")]
    [InlineData("!death add -1")]
    [InlineData("!death set")]
    [InlineData("!death set abc")]
    [InlineData("!death set -3")]
    public async Task MalformedNumbers_GetUsage(string message)
    {
        Assert.Contains("Usage:", await Run(Service(), message));
    }

    [Fact]
    public async Task DeathMinus_SubtractsOne_FlooredAtZero()
    {
        var service = Service();
        await service.SetAsync(2, Mod);

        Assert.Equal("💀 Deaths: 1", await Run(service, "!death-"));
        Assert.Equal("💀 Deaths: 0", await Run(service, "!death-"));
    }

    [Fact]
    public async Task DeathSub_RemovesExplicitDelta()
    {
        var service = Service();
        await service.SetAsync(10, Mod);

        Assert.Equal("💀 Deaths: 7", await Run(service, "!death sub 3"));
    }

    [Fact]
    public async Task DeathSet_SetsExactValue()
    {
        var service = Service();

        Assert.Equal("💀 Death counter set to 1337", await Run(service, "!death set 1337"));
        Assert.Equal(1337, await service.GetCountAsync());
    }

    [Fact]
    public async Task DeathReset_ResetsToZero()
    {
        var service = Service();
        await service.SetAsync(55, Mod);

        Assert.Equal("💀 Death counter reset to 0", await Run(service, "!death reset"));
        Assert.Equal(0, await service.GetCountAsync());
    }

    [Fact]
    public async Task DeathHelp_AndUnknownSub_GetUsage()
    {
        var service = Service();

        var help = await Run(service, "!death help", isModerator: false, username: Viewer);
        var unknown = await Run(service, "!death frobnicate");

        Assert.Equal(help, unknown);
        Assert.Contains("!death+", help);
        Assert.Contains("!death reset", help);
    }

    // ── history ────────────────────────────────────────────────────────

    [Fact]
    public async Task History_IsNewestFirst_AndCappedAtFifty()
    {
        var service = Service();

        // 55 mutations: history keeps only the 50 newest.
        for (var i = 1; i <= 55; i++)
        {
            await service.AddAsync(1, Mod);
        }

        Assert.Equal(55, await service.GetCountAsync());
        var history = await service.GetHistoryAsync(50);
        Assert.Equal(50, history.Count);
        Assert.All(history, e => Assert.Equal(1, e.Delta));

        // Newest first: the head is the 55th mutation, the tail the 6th.
        Assert.True(history[0].At >= history[^1].At);
    }

    [Fact]
    public async Task History_HeadIsNewestChange_WithSignedDeltaAndActor()
    {
        var service = Service();

        await service.AddAsync(3, "alice");   // 0 → 3
        await service.SubtractAsync(2, "bob"); // 3 → 1
        await service.ResetAsync("carol");     // 1 → 0

        var history = await service.GetHistoryAsync(10);
        Assert.Equal(3, history.Count);
        Assert.Equal("carol", history[0].By);
        Assert.Equal(-1, history[0].Delta);
        Assert.Equal("bob", history[1].By);
        Assert.Equal(-2, history[1].Delta);
        Assert.Equal("alice", history[2].By);
        Assert.Equal(3, history[2].Delta);
    }

    [Fact]
    public async Task History_LimitDefaultsToTen()
    {
        var service = Service();

        for (var i = 0; i < 15; i++)
        {
            await service.AddAsync(1, Mod);
        }

        Assert.Equal(10, (await service.GetHistoryAsync()).Count);
        Assert.Equal(15, (await service.GetHistoryAsync(50)).Count);
    }
}
