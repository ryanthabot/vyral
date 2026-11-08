using Microsoft.Extensions.Logging.Abstractions;
using VyralBot.Core.Services.Commands;
using Xunit;

namespace VyralBot.UI.Tests;

/// <summary>
/// Wave-17 coverage for <see cref="FunCommandService"/> — the community fun
/// pack (!8ball, !dice/!roll, !coinflip/!flip, !lurk, !hug, !rng). Drives the
/// full command matrix with a SEEDED Random and a controllable clock: every
/// command is viewer-accessible and platform-agnostic, replies stay inside
/// their response sets/bounds, argument validation returns usage text, the
/// !hug target is sanitized like !so, and the per-user 5-second spam guard
/// drops silently without refreshing on rejected attempts. Determinism
/// tricks: repeat draws vary the USERNAME (the guard is per-user) and
/// <c>!rng N N</c> has a span of one, so the reply is known without knowing
/// the seed's sequence.
/// </summary>
public class Wave17FunCommandTests
{
    private const string Viewer = "randomviewer";

    /// <summary>The 20 classic 8-ball answers the service must draw from.</summary>
    private static readonly HashSet<string> EightBallAnswers = new(StringComparer.Ordinal)
    {
        "It is certain.", "It is decidedly so.", "Without a doubt.", "Yes, definitely.",
        "You may rely on it.", "As I see it, yes.", "Most likely.", "Outlook good.",
        "Yes.", "Signs point to yes.", "Reply hazy, try again.", "Ask again later.",
        "Better not tell you now.", "Cannot predict now.", "Concentrate and ask again.",
        "Don't count on it.", "My reply is no.", "My sources say no.",
        "Outlook not so good.", "Very doubtful."
    };

    /// <summary>Controllable clock so spam-window tests advance seconds, never sleep.</summary>
    private sealed class Wave17Clock
    {
        public DateTime UtcNow { get; set; } = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
    }

    private static FunCommandService Service(Wave17Clock? clock = null, int seed = 17) =>
        new(NullLogger<FunCommandService>.Instance, new Random(seed), () => (clock ?? new Wave17Clock()).UtcNow);

    private static Task<string?> Run(
        FunCommandService service,
        string message,
        string username = Viewer,
        string platform = "Twitch",
        bool isModerator = false) =>
        service.ProcessCommandAsync(username, platform, isModerator, message);

    // ── routing matrix ─────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("hello there")]
    public async Task NonCommandMessages_FallThrough_Null(string? message)
    {
        var service = Service();
        Assert.Null(await Run(service, message!));
    }

    [Fact]
    public async Task UnknownCommand_FallsThrough_Null()
    {
        var service = Service();
        Assert.Null(await Run(service, "!definitelynotfun"));
    }

    [Theory]
    [InlineData("!8ball will tests pass?")]
    [InlineData("!dice")]
    [InlineData("!roll 3")]
    [InlineData("!coinflip")]
    [InlineData("!flip")]
    [InlineData("!lurk")]
    [InlineData("!hug @friend")]
    [InlineData("!rng 1 10")]
    public async Task EveryCommand_WorksForViewers_OnAllPlatforms(string message)
    {
        foreach (var platform in new[] { "Twitch", "Kick", "YouTube", "Discord", "TikTok" })
        {
            var service = Service();
            // Fresh service per platform: the spam guard would otherwise eat
            // the repeat. isModerator stays false — fun is for everyone.
            Assert.NotNull(await Run(service, message, platform: platform));
        }
    }

    [Fact]
    public async Task Commands_AreCaseInsensitive()
    {
        var service = Service();
        Assert.Matches("^🔮 ", await Run(service, "!8BALL why not?") ?? "");
        Assert.NotNull(await Run(service, "!ROLL 2", username: "case2"));
        Assert.NotNull(await Run(service, "!FLIP", username: "case3"));
    }

    // ── !8ball ─────────────────────────────────────────────────────────

    [Fact]
    public async Task EightBall_NoQuestion_IsUsage()
    {
        var service = Service();
        Assert.Equal("Usage: !8ball <question>", await Run(service, "!8ball"));
        Assert.Equal("Usage: !8ball <question>", await Run(service, "!8ball   ", username: "silent2"));
    }

    [Fact]
    public async Task EightBall_AlwaysAnswersFromThe20Classics()
    {
        var service = Service(seed: 42);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < 60; i++)
        {
            var reply = await Run(service, $"!8ball try {i}?", username: $"asker{i}");
            Assert.StartsWith("🔮 ", reply);
            Assert.Contains(reply!["🔮 ".Length..], EightBallAnswers);
            seen.Add(reply!);
        }

        // With a fixed seed and 60 draws the oracle must not be stuck.
        Assert.True(seen.Count >= 2, $"seed 42 produced a single answer: {seen.First()}");
    }

    // ── !dice / !roll ──────────────────────────────────────────────────

    [Fact]
    public async Task Dice_Default_IsSingleD6()
    {
        var service = Service();
        Assert.Matches("^🎲 [1-6]$", await Run(service, "!dice") ?? "");
        Assert.Matches("^🎲 [1-6]$", await Run(service, "!roll", username: "roller2") ?? "");
    }

    [Fact]
    public async Task Dice_MultipleDice_ListsRollsAndTotal()
    {
        var service = Service();
        var reply = await Run(service, "!dice 3");
        Assert.Matches("^🎲 [1-6], [1-6], [1-6] = \\d+$", reply ?? "");

        var rolls = reply!["🎲 ".Length..].Split(" = ")[0].Split(", ").Select(int.Parse).ToArray();
        Assert.Equal(3, rolls.Length);
        Assert.All(rolls, r => Assert.InRange(r, 1, 6));
        Assert.Equal(rolls.Sum(), int.Parse(reply["🎲 ".Length..].Split(" = ")[1]));
    }

    [Theory]
    [InlineData("!dice 0")]
    [InlineData("!dice 11")]
    [InlineData("!dice abc")]
    [InlineData("!dice -2")]
    [InlineData("!roll 99")]
    public async Task Dice_CountOutOfRange_IsUsage(string message)
    {
        var service = Service();
        Assert.Equal("Usage: !dice [1-10]", await Run(service, message));
    }

    // ── !coinflip / !flip ──────────────────────────────────────────────

    [Fact]
    public async Task CoinFlip_IsHeadsOrTails()
    {
        var service = Service(seed: 7);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < 40; i++)
        {
            var reply = await Run(service, i % 2 == 0 ? "!coinflip" : "!flip", username: $"flipper{i}");
            Assert.Contains(reply, new[] { "🪙 Heads!", "🪙 Tails!" });
            seen.Add(reply!);
        }

        Assert.Equal(2, seen.Count); // both faces appear under a fixed seed
    }

    // ── !lurk ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Lurk_AnnouncesTheShadowExit()
    {
        var service = Service();
        Assert.Equal(
            "randomviewer slips into the shadows… 👋 (welcome back anytime)",
            await Run(service, "!lurk"));
    }

    // ── !hug ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Hug_Target_IsSanitizedLikeShoutout()
    {
        var service = Service();
        Assert.Equal("randomviewer gives Alice a big hug! 🤗", await Run(service, "!hug @Alice"));
        Assert.Equal("hug2 gives Bob a big hug! 🤗", await Run(service, "!hug #Bob", username: "hug2"));
        // Path/query junk is stripped — only legal username characters remain.
        Assert.Equal("hug3 gives ..etcpasswd a big hug! 🤗",
            await Run(service, "!hug ../etc/passwd", username: "hug3"));
    }

    [Fact]
    public async Task Hug_MissingTarget_IsUsage()
    {
        var service = Service();
        Assert.Equal("Usage: !hug <user>", await Run(service, "!hug"));
        Assert.Equal("Usage: !hug <user>", await Run(service, "!hug @", username: "hugless2"));
    }

    // ── !rng ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData("!rng")]
    [InlineData("!rng 5")]
    [InlineData("!rng 5 6 7")]
    [InlineData("!rng a b")]
    [InlineData("!rng 99999999999999999999 5")]
    public async Task Rng_WrongArgumentShape_IsUsage(string message)
    {
        var service = Service();
        Assert.Equal("Usage: !rng <min> <max>", await Run(service, message));
    }

    [Fact]
    public async Task Rng_MinGreaterThanMax_IsRejected()
    {
        var service = Service();
        Assert.Equal("Min can't be greater than max.", await Run(service, "!rng 5 3"));
    }

    [Fact]
    public async Task Rng_SpanOverABillion_IsRejected()
    {
        var service = Service();
        Assert.Equal("Keep the range under a billion.", await Run(service, "!rng -1000000000 1000000000"));
    }

    [Fact]
    public async Task Rng_SingleValueRange_IsDeterministic()
    {
        var service = Service();
        // Span of one: any seed must answer with the only inclusive value.
        Assert.Equal("🎯 7", await Run(service, "!rng 7 7"));
        Assert.Equal("🎯 0", await Run(service, "!rng 0 0", username: "rng2"));
        Assert.Equal("🎯 -3", await Run(service, "!rng -3 -3", username: "rng3"));
    }

    [Fact]
    public async Task Rng_ValueStaysInsideTheInclusiveRange()
    {
        var service = Service(seed: 99);
        for (var i = 0; i < 40; i++)
        {
            var reply = await Run(service, "!rng -5 5", username: $"roller{i}");
            Assert.Matches("^🎯 -?\\d+$", reply ?? "");
            Assert.InRange(int.Parse(reply!["🎯 ".Length..]), -5, 5);
        }
    }

    [Fact]
    public async Task Rng_UpperIntEdge_IncludesMaxWhenSpanAllows()
    {
        var service = Service();
        // max = int.MaxValue with a small span exercises the +1-overflow guard.
        for (var i = 0; i < 40; i++)
        {
            var reply = await Run(service, "!rng 2147483640 2147483647", username: $"edger{i}");
            Assert.InRange(int.Parse(reply!["🎯 ".Length..]), 2147483640, 2147483647);
        }
    }

    // ── spam guard ─────────────────────────────────────────────────────

    [Fact]
    public async Task SpamGuard_SecondCommandWithin5Seconds_IsDroppedSilently()
    {
        var clock = new Wave17Clock();
        var service = Service(clock);

        Assert.Equal("🎯 7", await Run(service, "!rng 7 7")); // t0 — takes the slot
        Assert.Null(await Run(service, "!dice"));            // t0 — dropped
        Assert.Null(await Run(service, "!flip"));            // t0 — dropped

        clock.UtcNow = clock.UtcNow.Add(TimeSpan.FromSeconds(4.9));
        Assert.Null(await Run(service, "!dice"));            // 4.9s — still inside

        clock.UtcNow = clock.UtcNow.Add(TimeSpan.FromSeconds(0.2));
        Assert.NotNull(await Run(service, "!dice"));         // 5.1s — free again
    }

    [Fact]
    public async Task SpamGuard_RejectedAttempt_DoesNotRefreshTheWindow()
    {
        var clock = new Wave17Clock();
        var service = Service(clock);

        await Run(service, "!rng 7 7");                       // t0 — last USE
        clock.UtcNow = clock.UtcNow.AddSeconds(3);
        Assert.Null(await Run(service, "!dice 99"));          // usage error, dropped
        clock.UtcNow = clock.UtcNow.AddSeconds(2.5);          // 5.5s after the last USE
        Assert.NotNull(await Run(service, "!dice"));          // window measured from t0
    }

    [Fact]
    public async Task SpamGuard_UnmatchedCommands_DoNotConsumeTheSlot()
    {
        var service = Service();
        Assert.Null(await Run(service, "!hello"));            // not a fun command
        Assert.Equal("🎯 7", await Run(service, "!rng 7 7")); // immediately fine
    }

    [Fact]
    public async Task SpamGuard_UsageErrors_ConsumeTheSlot()
    {
        var service = Service();
        Assert.Equal("Usage: !8ball <question>", await Run(service, "!8ball"));
        // The typo took the slot; the corrected retry inside 5s is dropped.
        Assert.Null(await Run(service, "!8ball is anyone there?"));
    }

    [Fact]
    public async Task SpamGuard_IsPerUser_AndCaseInsensitive()
    {
        var service = Service();
        Assert.Equal("🎯 7", await Run(service, "!rng 7 7", username: "alice"));
        Assert.Equal("🎯 7", await Run(service, "!rng 7 7", username: "bob"));   // other users unaffected
        Assert.Null(await Run(service, "!dice", username: "ALICE"));             // same user, case-insensitive
        Assert.Null(await Run(service, "!dice", username: "alice"));
    }

    [Fact]
    public async Task SpamGuard_RegistryStaysFunctional_PastItsCapacity()
    {
        var clock = new Wave17Clock();
        var service = Service(clock);

        // 300 distinct users — past the 256 cap; pruning must keep it working.
        for (var i = 0; i < 300; i++)
            Assert.Equal("🎯 7", await Run(service, "!rng 7 7", username: $"viewer{i}"));

        clock.UtcNow = clock.UtcNow.AddSeconds(6); // every entry expired
        Assert.Equal("🎯 7", await Run(service, "!rng 7 7", username: "viewer0"));
        Assert.Null(await Run(service, "!dice", username: "viewer0")); // re-armed after expiry
    }
}
