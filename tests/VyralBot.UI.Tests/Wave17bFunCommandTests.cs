using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using VyralBot.Core.Services.Commands;
using Xunit;

namespace VyralBot.UI.Tests;

/// <summary>
/// Wave-17b close-out for <see cref="FunCommandService"/> — the gaps the wave-17
/// pass left open. Where <c>Wave17FunCommandTests</c> proved the reply sets and
/// the spam guard's shape, this file pins the seams underneath: the seeded
/// <see cref="Random"/> actually drives every draw (same seed → byte-identical
/// reply sequence, matching an oracle built on the same seed), the spam window
/// expires at EXACTLY 5 s (the guard is a strict <c>&lt;</c>), null/blank
/// usernames share the "anonymous" slot and render as "Someone", the !rng span
/// cap admits exactly one billion and rejects one-billion-and-one, both !rng
/// endpoints are reachable (inclusive bounds), and the 256-entry registry cap
/// is enforced for real — read back through reflection after 300 users, the
/// dictionary must sit at 256 holding the newest entries, not 300.
/// </summary>
public class Wave17bFunCommandTests
{
    /// <summary>Controllable clock so spam-window tests jump time, never sleep.</summary>
    private sealed class Wave17bClock
    {
        public DateTime UtcNow { get; set; } = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
    }

    private static FunCommandService Service(Wave17bClock? clock = null, int seed = 17) =>
        new(NullLogger<FunCommandService>.Instance, new Random(seed), () => (clock ?? new Wave17bClock()).UtcNow);

    private static Task<string?> Run(
        FunCommandService service,
        string message,
        string? username = "randomviewer") =>
        service.ProcessCommandAsync(username!, "Twitch", isModerator: false, message);

    /// <summary>The private spam-guard registry (capacity assertions read it back).</summary>
    private static Dictionary<string, DateTime> SpamRegistry(FunCommandService service)
    {
        var field = typeof(FunCommandService).GetField("_lastFunUse", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(field);
        return (Dictionary<string, DateTime>)field!.GetValue(service)!;
    }

    // ── seeded Random determinism ──────────────────────────────────────

    [Fact]
    public async Task SameSeed_ProducesIdenticalReplySequences()
    {
        const int seed = 4242;
        var a = Service(seed: seed);
        var b = Service(seed: seed);

        for (var i = 0; i < 12; i++)
        {
            // Fresh username per draw — the per-user guard must never eat one.
            var fromA = await Run(a, "!dice", username: $"viewer{i}");
            var fromB = await Run(b, "!dice", username: $"viewer{i}");
            Assert.Equal(fromA, fromB);
            Assert.Matches("^🎲 [1-6]$", fromA ?? "");
        }
    }

    [Fact]
    public async Task SeededRandom_DrivesTheDraws_MatchingAnOracle()
    {
        const int seed = 777;
        var service = Service(seed: seed);
        var oracle = new Random(seed); // same seed, drawn the way the service draws

        for (var i = 0; i < 6; i++)
        {
            var expected = oracle.Next(1, 7); // HandleDice rolls Next(1, 7)
            var reply = await Run(service, "!dice", username: $"oracle{i}");
            Assert.Equal($"🎲 {expected}", reply);
        }

        // The coin comes from the same stream: continue the oracle, don't reset it.
        for (var i = 0; i < 4; i++)
        {
            var heads = oracle.Next(2) == 0; // HandleCoinFlip rolls Next(2)
            var reply = await Run(service, "!flip", username: $"coin{i}");
            Assert.Equal(heads ? "🪙 Heads!" : "🪙 Tails!", reply);
        }
    }

    // ── spam guard edges ───────────────────────────────────────────────

    [Fact]
    public async Task SpamWindow_ExpiresAtExactly5Seconds()
    {
        var clock = new Wave17bClock();
        var service = Service(clock);

        Assert.Equal("🎯 7", await Run(service, "!rng 7 7")); // t0 — takes the slot

        clock.UtcNow = clock.UtcNow.Add(TimeSpan.FromMilliseconds(4999));
        Assert.Null(await Run(service, "!dice")); // 4.999 s — still inside

        clock.UtcNow = clock.UtcNow.Add(TimeSpan.FromMilliseconds(1)); // exactly 5.000 s
        Assert.NotNull(await Run(service, "!dice")); // strict < → free at the boundary
    }

    [Fact]
    public async Task NullOrBlankUsername_SharesTheAnonymousSlot()
    {
        var clock = new Wave17bClock();
        var service = Service(clock);

        Assert.Equal("🎯 7", await Run(service, "!rng 7 7", username: null));

        // Different spellings of "no user" are one key: the second fun command
        // inside the window is dropped whoever (not) sent it.
        Assert.Null(await Run(service, "!dice", username: null));
        Assert.Null(await Run(service, "!flip", username: "   "));

        clock.UtcNow = clock.UtcNow.AddSeconds(5.1);
        Assert.NotNull(await Run(service, "!dice", username: null)); // slot re-armed
    }

    [Fact]
    public async Task NullUsername_LurkAndHug_SaySomeone()
    {
        var clock = new Wave17bClock();
        var service = Service(clock);

        Assert.Equal("Someone slips into the shadows… 👋 (welcome back anytime)",
            await Run(service, "!lurk", username: null));

        clock.UtcNow = clock.UtcNow.AddSeconds(5.1); // same anonymous slot — re-arm
        Assert.Equal("Someone gives Alice a big hug! 🤗",
            await Run(service, "!hug @Alice", username: null));
    }

    [Fact]
    public async Task SpamGuard_DroppedAttempts_NeverExtendTheWindow()
    {
        var clock = new Wave17bClock();
        var service = Service(clock);

        await Run(service, "!lurk");                        // t0 — slot acquired by a VALID command
        clock.UtcNow = clock.UtcNow.AddSeconds(4);
        Assert.Null(await Run(service, "!dice"));           // 4 s — dropped, registry NOT rewritten
        clock.UtcNow = clock.UtcNow.AddSeconds(2);          // 6 s after t0, 2 s after the drop
        Assert.NotNull(await Run(service, "!flip"));        // free — drops never re-arm the window
    }

    // ── !dice sum format at the 10-dice cap ───────────────────────────

    [Fact]
    public async Task Dice_AtTheTenCap_ListsAllRollsAndTotal()
    {
        var service = Service(seed: 5);
        var reply = await Run(service, "!roll 10");

        Assert.Matches("^🎲 \\d+(, \\d+){9} = \\d+$", reply ?? "");
        var rolls = reply!["🎲 ".Length..].Split(" = ")[0].Split(", ").Select(int.Parse).ToArray();
        Assert.Equal(10, rolls.Length);
        Assert.All(rolls, r => Assert.InRange(r, 1, 6));
        Assert.Equal(rolls.Sum(), int.Parse(reply["🎲 ".Length..].Split(" = ")[1]));
    }

    // ── !rng span-cap boundary ────────────────────────────────────────

    [Fact]
    public async Task Rng_SpanOfExactlyOneBillion_IsAllowed()
    {
        var service = Service(seed: 11);
        for (var i = 0; i < 20; i++)
        {
            // 999_999_999 - 0 + 1 == the cap exactly — must NOT be rejected.
            var reply = await Run(service, "!rng 0 999999999", username: $"span{i}");
            Assert.InRange(int.Parse(reply!["🎯 ".Length..]), 0, 999999999);
        }
    }

    [Fact]
    public async Task Rng_SpanOfOneBillionAndOne_IsRejected()
    {
        var service = Service();
        Assert.Equal("Keep the range under a billion.", await Run(service, "!rng -1 999999999"));
    }

    [Fact]
    public async Task Rng_BothInclusiveEndpoints_AreReachable()
    {
        var service = Service(seed: 99);
        var seen = new HashSet<int>();
        for (var i = 0; i < 60; i++)
        {
            var reply = await Run(service, "!rng 5 6", username: $"endpoint{i}");
            seen.Add(int.Parse(reply!["🎯 ".Length..]));
        }

        Assert.Superset(new HashSet<int> { 5, 6 }, seen); // span 2 → both values drawn
    }

    // ── capacity pruning, verified against the real registry ──────────

    [Fact]
    public async Task CapacityPruning_RegistryNeverExceeds_Its256Cap()
    {
        var clock = new Wave17bClock();
        var service = Service(clock);

        // 300 distinct users, 10 ms apart — a 3 s span, so nothing expires
        // (window 5 s) but every timestamp is distinct: "oldest" is exact.
        for (var i = 0; i < 300; i++)
        {
            Assert.Equal("🎯 7", await Run(service, "!rng 7 7", username: $"viewer{i:D3}"));
            Assert.InRange(SpamRegistry(service).Count, 1, 256);
            clock.UtcNow = clock.UtcNow.AddMilliseconds(10);
        }

        // Nothing expired → eviction removed the OLDEST 44; the registry sits
        // exactly at the cap holding the newest 256 users.
        var registry = SpamRegistry(service);
        Assert.Equal(256, registry.Count);
        Assert.False(registry.ContainsKey("viewer000"));   // oldest — evicted
        Assert.True(registry.ContainsKey("viewer299"));    // newest — kept

        // The evicted user is spam-free again; the survivor is still guarded.
        Assert.Equal("🎯 7", await Run(service, "!rng 7 7", username: "viewer000"));
        Assert.Null(await Run(service, "!dice", username: "viewer299"));
    }

    [Fact]
    public async Task CapacityPruning_ExpiredEntriesAreDroppedFirst()
    {
        var clock = new Wave17bClock();
        var service = Service(clock);

        for (var i = 0; i < 300; i++)
            await Run(service, "!rng 7 7", username: $"viewer{i:D3}");
        Assert.Equal(256, SpamRegistry(service).Count);

        // Everything is stale now — the next cap overflow must shrink the
        // registry to just the fresh user, not keep evicting one-by-one.
        clock.UtcNow = clock.UtcNow.AddSeconds(6);
        await Run(service, "!rng 7 7", username: "newcomer");

        var registry = SpamRegistry(service);
        Assert.Single(registry);
        Assert.True(registry.ContainsKey("newcomer"));
    }
}
