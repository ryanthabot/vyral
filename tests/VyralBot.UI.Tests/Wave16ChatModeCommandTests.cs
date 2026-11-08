using Microsoft.Extensions.Logging.Abstractions;
using VyralBot.Core.Services.Commands;
using Xunit;

namespace VyralBot.UI.Tests;

/// <summary>
/// Wave-16 coverage for <see cref="ChatModeCommandService"/> — the Twitch
/// chat-mode protection commands with auto-revert (!emoteonly / !subonly /
/// !followers / !slow). Drives the full parse matrix against a recording
/// applier fake plus a controllable clock: mod/platform/connection gates,
/// default + explicit revert windows, follow-age suffix parsing ("2h"),
/// Helix slow-second bounds, "!&lt;mode&gt; off" cancellation, and the
/// auto-revert registry itself — one pending entry per mode, re-arm on
/// re-issue, single-attempt revert, graceful failure/disconnect/dispose.
/// The 30-second timer is never waited on: <c>ProcessDueRevertsAsync</c> is
/// the deterministic seam.
/// </summary>
public class Wave16ChatModeCommandTests
{
    private const string Viewer = "randomviewer";
    private const string Mod = "moddude";
    private const string ModOnly = "That's a moderator-only command.";
    private const string NotConnected = "Twitch is not connected.";

    /// <summary>Recording ITwitchChatModeApplier — every call appended, result settable.</summary>
    private sealed class Wave16ChatModeApplier : ITwitchChatModeApplier
    {
        public bool IsConnected { get; set; } = true;
        public bool ApplyResult { get; set; } = true;
        public List<(TwitchChatMode Mode, bool Enabled, int? Duration)> Calls { get; } = new(8);

        public Task<bool> ApplyChatModeAsync(TwitchChatMode mode, bool enabled, int? duration = null)
        {
            Calls.Add((mode, enabled, duration));
            return Task.FromResult(ApplyResult);
        }
    }

    /// <summary>Controllable clock so due-revert tests advance minutes, never sleep.</summary>
    private sealed class Wave16Clock
    {
        public DateTime UtcNow { get; set; } = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
    }

    private static ChatModeCommandService Service(
        Wave16ChatModeApplier? applier,
        Wave16Clock clock) =>
        new(applier, NullLogger<ChatModeCommandService>.Instance, () => clock.UtcNow);

    private static Task<string?> Run(
        ChatModeCommandService service,
        string message,
        bool isModerator = true,
        string platform = "Twitch",
        string username = Mod) =>
        service.ProcessCommandAsync(username, platform, isModerator, message);

    // ── routing matrix ─────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("hello there")]
    public async Task NonCommandMessages_FallThrough_Null(string? message)
    {
        using var service = Service(new Wave16ChatModeApplier(), new Wave16Clock());
        Assert.Null(await Run(service, message!));
    }

    [Fact]
    public async Task UnknownCommand_FallsThrough_Null()
    {
        using var service = Service(new Wave16ChatModeApplier(), new Wave16Clock());
        Assert.Null(await Run(service, "!definitelynotachatmode 10"));
    }

    [Theory]
    [InlineData("!emoteonly")]
    [InlineData("!emoteonly 5")]
    [InlineData("!emoteonly off")]
    [InlineData("!subonly")]
    [InlineData("!followers")]
    [InlineData("!followers 30")]
    [InlineData("!slow 10")]
    [InlineData("!slow off")]
    public async Task ChatModeCommands_AreModeratorGated(string message)
    {
        var applier = new Wave16ChatModeApplier();
        using var service = Service(applier, new Wave16Clock());

        Assert.Equal(ModOnly, await Run(service, message, isModerator: false, username: Viewer));
        Assert.Empty(applier.Calls); // the gate fired before any Twitch call
    }

    [Theory]
    [InlineData("Kick", "!emoteonly", "!emoteonly is Twitch-only.")]
    [InlineData("Discord", "!subonly", "!subonly is Twitch-only.")]
    [InlineData("YouTube", "!followers", "!followers is Twitch-only.")]
    [InlineData("TikTok", "!slow 10", "!slow is Twitch-only.")]
    public async Task ChatModeCommands_AreTwitchOnly(string platform, string message, string expected)
    {
        using var service = Service(new Wave16ChatModeApplier(), new Wave16Clock());
        Assert.Equal(expected, await Run(service, message, platform: platform));
    }

    [Fact]
    public async Task DisconnectedApplier_IsTreatedAsNotConnected()
    {
        var applier = new Wave16ChatModeApplier { IsConnected = false };
        using var service = Service(applier, new Wave16Clock());

        Assert.Equal(NotConnected, await Run(service, "!emoteonly"));
        Assert.Equal(NotConnected, await Run(service, "!followers 30"));
        Assert.Empty(applier.Calls);
    }

    [Fact]
    public async Task NullApplier_ReportsNotConnected()
    {
        using var service = Service(null, new Wave16Clock());
        Assert.Equal(NotConnected, await Run(service, "!slow 10"));
    }

    // ── !emoteonly ─────────────────────────────────────────────────────

    [Fact]
    public async Task EmoteOnly_Default_Is10MinuteAutoRevert()
    {
        var applier = new Wave16ChatModeApplier();
        using var service = Service(applier, new Wave16Clock());

        Assert.Equal("Emote-only enabled for 10 minutes.", await Run(service, "!emoteonly"));
        Assert.Equal((TwitchChatMode.EmoteOnly, true, (int?)null), applier.Calls.Single());
        Assert.Equal(1, service.PendingRevertCount);
    }

    [Fact]
    public async Task EmoteOnly_ExplicitWindow_IsEchoed()
    {
        var applier = new Wave16ChatModeApplier();
        using var service = Service(applier, new Wave16Clock());

        Assert.Equal("Emote-only enabled for 5 minutes.", await Run(service, "!emoteonly 5"));
        Assert.Equal("Emote-only enabled for 1 minute.", await Run(service, "!emoteonly 1"));
    }

    [Fact]
    public async Task EmoteOnly_Window_IsClampedTo120Minutes()
    {
        var applier = new Wave16ChatModeApplier();
        using var service = Service(applier, new Wave16Clock());

        Assert.Equal("Emote-only enabled for 120 minutes.", await Run(service, "!emoteonly 500"));
    }

    [Theory]
    [InlineData("!emoteonly abc")]
    [InlineData("!emoteonly 0")]
    [InlineData("!emoteonly -3")]
    [InlineData("!emoteonly 1.5")]
    public async Task EmoteOnly_BadArguments_IsUsage(string message)
    {
        var applier = new Wave16ChatModeApplier();
        using var service = Service(applier, new Wave16Clock());

        Assert.Equal("Usage: !emoteonly [minutes] (or !emoteonly off)", await Run(service, message));
        Assert.Empty(applier.Calls);
        Assert.Equal(0, service.PendingRevertCount);
    }

    [Fact]
    public async Task EmoteOnly_Off_DisablesNowAndCancelsRevert()
    {
        var applier = new Wave16ChatModeApplier();
        var clock = new Wave16Clock();
        using var service = Service(applier, clock);

        await Run(service, "!emoteonly");
        Assert.Equal("Emote-only disabled.", await Run(service, "!emoteonly OFF"));
        Assert.Equal((TwitchChatMode.EmoteOnly, false, (int?)null), applier.Calls[^1]);
        Assert.Equal(0, service.PendingRevertCount);

        // The cancelled revert must never fire later.
        clock.UtcNow = clock.UtcNow.AddMinutes(11);
        Assert.Equal(0, await service.ProcessDueRevertsAsync());
        Assert.Equal(2, applier.Calls.Count); // enable + explicit off, no auto-revert
    }

    [Fact]
    public async Task EmoteOnly_RejectedByTwitch_ReportsFailure_NoRevertArmed()
    {
        var applier = new Wave16ChatModeApplier { ApplyResult = false };
        using var service = Service(applier, new Wave16Clock());

        Assert.Equal("Emote-only update failed.", await Run(service, "!emoteonly"));
        Assert.Equal(0, service.PendingRevertCount);
    }

    // ── !subonly ───────────────────────────────────────────────────────

    [Fact]
    public async Task SubOnly_DefaultAndOff()
    {
        var applier = new Wave16ChatModeApplier();
        using var service = Service(applier, new Wave16Clock());

        Assert.Equal("Subscribers-only enabled for 10 minutes.", await Run(service, "!subonly"));
        Assert.Equal((TwitchChatMode.SubscriberOnly, true, (int?)null), applier.Calls.Single());
        Assert.Equal(1, service.PendingRevertCount);

        Assert.Equal("Subscribers-only disabled.", await Run(service, "!subonly off"));
        Assert.Equal((TwitchChatMode.SubscriberOnly, false, (int?)null), applier.Calls[^1]);
        Assert.Equal(0, service.PendingRevertCount);
    }

    // ── !followers ─────────────────────────────────────────────────────

    [Fact]
    public async Task Followers_Default_AnyFollower_10MinuteRevert()
    {
        var applier = new Wave16ChatModeApplier();
        using var service = Service(applier, new Wave16Clock());

        Assert.Equal("Followers-only enabled for 10 minutes.", await Run(service, "!followers"));
        Assert.Equal((TwitchChatMode.FollowerOnly, true, (int?)0), applier.Calls.Single());
        Assert.Equal(1, service.PendingRevertCount);
    }

    [Theory]
    [InlineData("30", 30, "30 minutes")]
    [InlineData("30m", 30, "30 minutes")]
    [InlineData("2h", 120, "2 hours")]
    [InlineData("60", 60, "1 hour")]
    [InlineData("1440", 1440, "1 day")]
    [InlineData("43200", 43200, "1 month")]
    public async Task Followers_FollowAge_IsParsedAndHumanized(string arg, int expectedMinutes, string expectedText)
    {
        var applier = new Wave16ChatModeApplier();
        using var service = Service(applier, new Wave16Clock());

        Assert.Equal(
            $"Followers-only enabled (must have followed for {expectedText}) for 10 minutes.",
            await Run(service, $"!followers {arg}"));
        Assert.Equal((TwitchChatMode.FollowerOnly, true, (int?)expectedMinutes), applier.Calls.Single());
    }

    [Fact]
    public async Task Followers_SecondArgument_IsTheRevertWindow()
    {
        var applier = new Wave16ChatModeApplier();
        using var service = Service(applier, new Wave16Clock());

        Assert.Equal(
            "Followers-only enabled (must have followed for 30 minutes) for 45 minutes.",
            await Run(service, "!followers 30 45"));
        Assert.Equal((TwitchChatMode.FollowerOnly, true, (int?)30), applier.Calls.Single());
    }

    [Theory]
    [InlineData("!followers abc")]
    [InlineData("!followers -1")]
    [InlineData("!followers 1.5h")]
    [InlineData("!followers 10 0")]
    [InlineData("!followers 10 x")]
    public async Task Followers_BadArguments_IsUsage(string message)
    {
        var applier = new Wave16ChatModeApplier();
        using var service = Service(applier, new Wave16Clock());

        Assert.Equal(
            "Usage: !followers [follow minutes|hours, e.g. 30 or 2h] [revert minutes] (or !followers off)",
            await Run(service, message));
        Assert.Empty(applier.Calls);
    }

    [Fact]
    public async Task Followers_Off_Disables()
    {
        var applier = new Wave16ChatModeApplier();
        using var service = Service(applier, new Wave16Clock());

        Assert.Equal("Followers-only disabled.", await Run(service, "!followers off"));
        Assert.Equal((TwitchChatMode.FollowerOnly, false, (int?)null), applier.Calls.Single());
    }

    // ── !slow ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Slow_SecondsAndWindow_AreEchoed()
    {
        var applier = new Wave16ChatModeApplier();
        using var service = Service(applier, new Wave16Clock());

        Assert.Equal("Slow mode enabled (10s) for 10 minutes.", await Run(service, "!slow 10"));
        Assert.Equal((TwitchChatMode.Slow, true, (int?)10), applier.Calls.Single());

        Assert.Equal("Slow mode enabled (5s) for 20 minutes.", await Run(service, "!slow 5 20"));
        Assert.Equal((TwitchChatMode.Slow, true, (int?)5), applier.Calls[^1]);
    }

    [Theory]
    [InlineData("!slow")]
    [InlineData("!slow abc")]
    [InlineData("!slow 0")]
    [InlineData("!slow 2")]
    [InlineData("!slow 121")]
    [InlineData("!slow 10 0")]
    [InlineData("!slow 10 abc")]
    public async Task Slow_OutOfRangeOrBadArguments_IsUsage(string message)
    {
        var applier = new Wave16ChatModeApplier();
        using var service = Service(applier, new Wave16Clock());

        Assert.Equal("Usage: !slow <3-120 seconds> [minutes] (or !slow off)", await Run(service, message));
        Assert.Empty(applier.Calls);
    }

    [Fact]
    public async Task Slow_Off_Disables()
    {
        var applier = new Wave16ChatModeApplier();
        using var service = Service(applier, new Wave16Clock());

        Assert.Equal("Slow mode disabled.", await Run(service, "!slow off"));
        Assert.Equal((TwitchChatMode.Slow, false, (int?)null), applier.Calls.Single());
    }

    // ── auto-revert registry ───────────────────────────────────────────

    [Fact]
    public async Task Revert_FiresAtDueTime_AndClearsPending()
    {
        var applier = new Wave16ChatModeApplier();
        var clock = new Wave16Clock();
        using var service = Service(applier, clock);

        await Run(service, "!emoteonly"); // due at +10:00
        clock.UtcNow = clock.UtcNow.Add(TimeSpan.FromMinutes(9)).Add(TimeSpan.FromSeconds(59));
        Assert.Equal(0, await service.ProcessDueRevertsAsync());
        Assert.Single(applier.Calls); // not due yet

        clock.UtcNow = clock.UtcNow.Add(TimeSpan.FromSeconds(1)); // exactly +10:00
        Assert.Equal(1, await service.ProcessDueRevertsAsync());
        Assert.Equal((TwitchChatMode.EmoteOnly, false, (int?)null), applier.Calls[^1]);
        Assert.Equal(0, service.PendingRevertCount);
    }

    [Fact]
    public async Task Revert_Reissue_ReArmsTheSinglePendingEntry()
    {
        var applier = new Wave16ChatModeApplier();
        var clock = new Wave16Clock();
        using var service = Service(applier, clock);

        await Run(service, "!emoteonly 30"); // due at +30
        clock.UtcNow = clock.UtcNow.AddMinutes(20);
        Assert.Equal(0, await service.ProcessDueRevertsAsync()); // old window not due yet

        await Run(service, "!emoteonly 5"); // re-armed: now due at +25 (not +35)
        Assert.Equal(1, service.PendingRevertCount); // still exactly ONE entry

        clock.UtcNow = clock.UtcNow.AddMinutes(4).Add(TimeSpan.FromSeconds(59));
        Assert.Equal(0, await service.ProcessDueRevertsAsync());

        clock.UtcNow = clock.UtcNow.AddSeconds(1);
        Assert.Equal(1, await service.ProcessDueRevertsAsync());
        Assert.Equal(3, applier.Calls.Count); // enable + enable + ONE auto-disable
        Assert.Single(applier.Calls, c => c is { Mode: TwitchChatMode.EmoteOnly, Enabled: false });
    }

    [Fact]
    public async Task Revert_OnePendingPerMode_BothModesRevert()
    {
        var applier = new Wave16ChatModeApplier();
        var clock = new Wave16Clock();
        using var service = Service(applier, clock);

        await Run(service, "!emoteonly 5");
        await Run(service, "!slow 10 5");
        Assert.Equal(2, service.PendingRevertCount);

        clock.UtcNow = clock.UtcNow.AddMinutes(6);
        Assert.Equal(2, await service.ProcessDueRevertsAsync());
        Assert.Contains(applier.Calls, c => c is { Mode: TwitchChatMode.EmoteOnly, Enabled: false });
        Assert.Contains(applier.Calls, c => c is { Mode: TwitchChatMode.Slow, Enabled: false, Duration: null });
        Assert.Equal(0, service.PendingRevertCount);
    }

    [Fact]
    public async Task Revert_FailedTwitchCall_IsSingleAttempt_AndDropsEntry()
    {
        var applier = new Wave16ChatModeApplier();
        var clock = new Wave16Clock();
        using var service = Service(applier, clock);

        await Run(service, "!subonly");
        applier.ApplyResult = false; // Twitch rejects the auto-revert

        clock.UtcNow = clock.UtcNow.AddMinutes(11);
        Assert.Equal(0, await service.ProcessDueRevertsAsync());
        Assert.Equal(0, service.PendingRevertCount); // dropped, not retried forever

        Assert.Equal(0, await service.ProcessDueRevertsAsync());
        Assert.Equal(2, applier.Calls.Count); // enable + exactly one revert attempt
    }

    [Fact]
    public async Task Revert_DisconnectedTwitch_DropsEntryWithoutCalling()
    {
        var applier = new Wave16ChatModeApplier();
        var clock = new Wave16Clock();
        using var service = Service(applier, clock);

        await Run(service, "!emoteonly");
        applier.IsConnected = false;

        clock.UtcNow = clock.UtcNow.AddMinutes(11);
        Assert.Equal(0, await service.ProcessDueRevertsAsync());
        Assert.Single(applier.Calls); // enable only — the guard skipped the Helix call
        Assert.Equal(0, service.PendingRevertCount);
    }

    [Fact]
    public async Task Revert_AfterDispose_MakesNoCalls()
    {
        var applier = new Wave16ChatModeApplier();
        var clock = new Wave16Clock();
        var service = Service(applier, clock);

        await Run(service, "!emoteonly");
        service.Dispose();
        service.Dispose(); // double dispose is safe

        clock.UtcNow = clock.UtcNow.AddMinutes(11);
        Assert.Equal(0, await service.ProcessDueRevertsAsync());
        Assert.Single(applier.Calls); // the enable only
    }
}
