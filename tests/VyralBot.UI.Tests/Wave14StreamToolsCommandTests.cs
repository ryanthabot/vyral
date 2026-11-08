using Xunit;
using Microsoft.Extensions.Logging.Abstractions;
using VyralBot.Core.Services.StreamStatus;
using VyralBot.Integrations.StreamTools;

namespace VyralBot.UI.Tests;

/// <summary>
/// Wave-14 coverage for <see cref="StreamToolsCommandService"/> — the classic
/// mod/stream-tool chat commands. Drives the full parse matrix with the
/// Wave-14 Twitch fake (connected/disconnected/null) and the live-registry
/// fake for the non-Twitch !uptime degradation replies:
/// !so (with/without target, mod-gated, per-platform URLs), !raid +
/// !raid cancel, !title read/write, !game read/write, !commercial clamp,
/// !uptime on every platform shape, !followage target defaulting and the
/// two-largest-units humanization.
/// </summary>
public class Wave14StreamToolsCommandTests
{
    private const string Viewer = "randomviewer";
    private const string Mod = "moddude";

    private static StreamToolsCommandService Service(
        Wave14TwitchService? twitch = null,
        IStreamLiveRegistry? registry = null)
        => new(twitch, registry, NullLogger<StreamToolsCommandService>.Instance);

    private static Task<string?> Run(
        StreamToolsCommandService service,
        string message,
        bool isModerator = true,
        string username = Mod,
        string platform = "Twitch")
        => service.ProcessCommandAsync(username, platform, isModerator, message);

    // ── fallback / routing matrix ──────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("hello there")]
    public async Task NonCommandMessages_FallThrough_Null(string? message)
    {
        var reply = await Run(Service(), message!);
        Assert.Null(reply);
    }

    [Fact]
    public async Task UnknownCommand_FallsThrough_Null()
    {
        var reply = await Run(Service(), "!definitelynotastreamtool");
        Assert.Null(reply);
    }

    // ── !so ────────────────────────────────────────────────────────────

    [Fact]
    public async Task So_WithoutTarget_IsModeratorGated_ThenUsage()
    {
        var service = Service();

        // Non-mods never reach the usage check — the gate replies first.
        Assert.Equal("That's a moderator-only command.", await Run(service, "!so", isModerator: false));
        // Mods with no target get usage (the next arg would be the target).
        Assert.Equal("Usage: !so <user>", await Run(service, "!so"));
    }

    [Theory]
    [InlineData("Twitch", "CoolStreamer", "Check out CoolStreamer at twitch.tv/coolstreamer — go show them some love!")]
    [InlineData("Kick", "CoolStreamer", "Check out CoolStreamer at kick.com/coolstreamer — go show them some love!")]
    [InlineData("YouTube", "CoolStreamer", "Check out CoolStreamer at youtube.com/@CoolStreamer — go show them some love!")]
    [InlineData("TikTok", "CoolStreamer", "Check out CoolStreamer at tiktok.com/@CoolStreamer — go show them some love!")]
    [InlineData("Discord", "CoolStreamer", "Go check out CoolStreamer — they're awesome!")]
    public async Task So_WithTarget_BuildsPerPlatformReply(string platform, string target, string expected)
    {
        var reply = await Run(Service(), $"!so {target}", platform: platform);
        Assert.Equal(expected, reply);
    }

    [Fact]
    public async Task So_MentionDecoration_IsStripped()
    {
        var reply = await Run(Service(), "!so @SomeoneNice");
        Assert.Equal("Check out SomeoneNice at twitch.tv/someonenice — go show them some love!", reply);
    }

    [Fact]
    public async Task So_DoesNotNeedTwitchConnection()
    {
        // !so is pure text building — a null Twitch service must not matter.
        var service = Service(twitch: null);
        var reply = await Run(service, "!so anyone");
        Assert.NotNull(reply);
        Assert.Contains("twitch.tv/anyone", reply);
    }

    // ── !raid ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Raid_Target_RaidsSanitizedTarget()
    {
        var twitch = new Wave14TwitchService();
        var service = Service(twitch);

        Assert.Equal("Raiding FriendChannel!", await Run(service, "!raid @FriendChannel"));
        Assert.Equal(new[] { "FriendChannel" }, twitch.RaidTargets);
    }

    [Fact]
    public async Task Raid_Cancel_CancelsOutgoingRaid()
    {
        var twitch = new Wave14TwitchService();
        var service = Service(twitch);

        Assert.Equal("Raid cancelled.", await Run(service, "!raid CANCEL"));
        Assert.Equal(1, twitch.CancelRaidCalls);
        Assert.Empty(twitch.RaidTargets);
    }

    [Fact]
    public async Task Raid_WithoutTarget_IsUsage()
    {
        var service = Service(new Wave14TwitchService()); // IsConnected defaults true
        Assert.Equal("Usage: !raid <channel> (or !raid cancel)", await Run(service, "!raid"));
    }

    [Fact]
    public async Task Raid_RejectedByTwitch_ReportsFailure()
    {
        var twitch = new Wave14TwitchService { StartRaidResult = false };
        Assert.Equal("Raid failed — check the channel name.", await Run(Service(twitch), "!raid ghostchannel"));
    }

    [Fact]
    public async Task Raid_CancelRejectedByTwitch_ReportsFailure()
    {
        var twitch = new Wave14TwitchService { CancelRaidResult = false };
        Assert.Equal("Raid cancel failed.", await Run(Service(twitch), "!raid cancel"));
    }

    [Fact]
    public async Task Raid_NonTwitchPlatform_IsTwitchOnly()
    {
        Assert.Equal("!raid is Twitch-only.", await Run(Service(), "!raid someone", platform: "Kick"));
    }

    // ── !title ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Title_Read_IncludesGameWhenPresent()
    {
        var twitch = new Wave14TwitchService
        {
            ChannelInfoResult = ("Building the death counter", "Software & Game Development")
        };

        Assert.Equal(
            "Current title: Building the death counter (playing Software & Game Development)",
            await Run(Service(twitch), "!title"));
    }

    [Fact]
    public async Task Title_Read_WithoutGame_IsTitleOnly()
    {
        var twitch = new Wave14TwitchService { ChannelInfoResult = ("Just chatting", "") };
        Assert.Equal("Current title: Just chatting", await Run(Service(twitch), "!title"));
    }

    [Fact]
    public async Task Title_Read_Unavailable_ReportsFailure()
    {
        var twitch = new Wave14TwitchService { ChannelInfoResult = null };
        Assert.Equal("Couldn't read the current title.", await Run(Service(twitch), "!title"));
    }

    [Fact]
    public async Task Title_Write_JoinsRemainingWords()
    {
        var twitch = new Wave14TwitchService();

        Assert.Equal("Title updated.", await Run(Service(twitch), "!title New Title With Spaces"));
        Assert.Single(twitch.ChannelUpdates);
        Assert.Equal(("New Title With Spaces", null), twitch.ChannelUpdates[0]);
    }

    [Fact]
    public async Task Title_WriteRejectedByTwitch_ReportsFailure()
    {
        var twitch = new Wave14TwitchService { UpdateChannelInfoResult = false };
        Assert.Equal("Title update failed.", await Run(Service(twitch), "!title whatever"));
    }

    [Fact]
    public async Task Title_NonTwitchPlatform_IsTwitchOnly()
    {
        Assert.Equal("!title is Twitch-only.", await Run(Service(), "!title anything", platform: "Discord"));
    }

    // ── !game ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Game_Read_ReportsCurrentGame()
    {
        var twitch = new Wave14TwitchService { ChannelInfoResult = ("Title", "Minecraft") };
        Assert.Equal("Current game: Minecraft", await Run(Service(twitch), "!game"));
    }

    [Fact]
    public async Task Game_Read_UnavailableOrEmpty_ReportsFailure()
    {
        var offline = new Wave14TwitchService { ChannelInfoResult = null };
        var untitled = new Wave14TwitchService { ChannelInfoResult = ("Title", "") };

        Assert.Equal("Couldn't read the current game.", await Run(Service(offline), "!game"));
        Assert.Equal("Couldn't read the current game.", await Run(Service(untitled), "!game"));
    }

    [Fact]
    public async Task Game_Write_ResolvesNameToIdAndPatches()
    {
        var twitch = new Wave14TwitchService
        {
            // GetTopGamesAsync "id:name;..." format — only the first pair is used.
            TopGamesResult = "21779:League of Legends;509658:Just Chatting"
        };

        Assert.Equal("Game updated to League of Legends.", await Run(Service(twitch), "!game league of legends"));
        Assert.Equal(new[] { "league of legends" }, twitch.TopGamesQueries);
        Assert.Single(twitch.ChannelUpdates);
        Assert.Equal((null, "21779"), twitch.ChannelUpdates[0]);
    }

    [Fact]
    public async Task Game_Write_UnresolvedName_ReportsNotFound()
    {
        var twitch = new Wave14TwitchService { TopGamesResult = null };

        Assert.Equal("Game \"Fortcraft\" not found on Twitch.", await Run(Service(twitch), "!game Fortcraft"));
        Assert.Empty(twitch.ChannelUpdates);
    }

    [Fact]
    public async Task Game_NonTwitchPlatform_IsTwitchOnly()
    {
        Assert.Equal("!game is Twitch-only.", await Run(Service(), "!game Minecraft", platform: "TikTok"));
    }

    // ── !commercial ────────────────────────────────────────────────────

    [Theory]
    [InlineData("!commercial")]
    [InlineData("!commercial 45")]
    [InlineData("!commercial 30 60")]
    [InlineData("!commercial abc")]
    [InlineData("!commercial 0")]
    [InlineData("!commercial 210")]
    public async Task Commercial_NonAllowedLength_IsUsage(string message)
    {
        var twitch = new Wave14TwitchService();

        Assert.Equal("Usage: !commercial <30|60|90|120|150|180>", await Run(Service(twitch), message));
        Assert.Empty(twitch.CommercialLengths); // never reaches Twitch
    }

    [Theory]
    [InlineData(30)]
    [InlineData(60)]
    [InlineData(90)]
    [InlineData(120)]
    [InlineData(150)]
    [InlineData(180)]
    public async Task Commercial_AllowedLength_StartsCommercial(int seconds)
    {
        var twitch = new Wave14TwitchService();

        Assert.Equal($"Commercial started ({seconds}s).", await Run(Service(twitch), $"!commercial {seconds}"));
        Assert.Equal(new[] { seconds }, twitch.CommercialLengths);
    }

    [Fact]
    public async Task Commercial_RejectedByTwitch_ReportsFailure()
    {
        var twitch = new Wave14TwitchService { StartCommercialResult = false };
        Assert.Equal("Commercial failed.", await Run(Service(twitch), "!commercial 30"));
    }

    [Fact]
    public async Task Commercial_NonTwitchPlatform_IsTwitchOnly()
    {
        Assert.Equal("!commercial is Twitch-only.", await Run(Service(), "!commercial 30", platform: "YouTube"));
    }

    // ── !uptime ────────────────────────────────────────────────────────

    [Fact]
    public async Task Uptime_TwitchLive_FormatsHoursAndMinutes()
    {
        var twitch = new Wave14TwitchService
        {
            StreamUptimeResult = TimeSpan.FromHours(2) + TimeSpan.FromMinutes(5)
        };

        Assert.Equal("Stream uptime: 2h 5m", await Run(Service(twitch), "!uptime"));
    }

    [Fact]
    public async Task Uptime_TwitchOffline_ReportsOffline()
    {
        var twitch = new Wave14TwitchService { StreamUptimeResult = null };
        Assert.Equal("Stream is offline.", await Run(Service(twitch), "!uptime"));
    }

    [Fact]
    public async Task Uptime_TwitchDisconnected_ReportsNotConnected()
    {
        var twitch = new Wave14TwitchService { IsConnected = false };
        Assert.Equal("Twitch is not connected.", await Run(Service(twitch), "!uptime"));
    }

    [Fact]
    public async Task Uptime_IsAvailableToViewers()
    {
        // !uptime is the one stream-tools command that is NOT mod-gated.
        var twitch = new Wave14TwitchService { StreamUptimeResult = TimeSpan.FromMinutes(90) };
        Assert.Equal("Stream uptime: 1h 30m", await Run(Service(twitch), "!uptime", isModerator: false, username: Viewer));
    }

    [Theory]
    [InlineData("Kick", true, "Live now — duration unknown (no start time available).")]
    [InlineData("YouTube", true, "Live now — duration unknown (no start time available).")]
    [InlineData("TikTok", true, "Live now — duration unknown (no start time available).")]
    [InlineData("Kick", false, "Stream is offline.")]
    [InlineData("YouTube", false, "Stream is offline.")]
    public async Task Uptime_NonTwitch_UsesLiveRegistry(string platform, bool isLive, string expected)
    {
        var registry = new Wave14LiveRegistry();
        registry.SetLive(platform, isLive);

        Assert.Equal(
            expected,
            await Run(Service(registry: registry), "!uptime", platform: platform, isModerator: false));
    }

    [Fact]
    public async Task Uptime_NonTwitchNeverTracked_IsOffline()
    {
        // Discord (and anything never SetLive'd) reports offline, not "live".
        var service = Service(registry: new Wave14LiveRegistry());
        Assert.Equal("Stream is offline.", await Run(service, "!uptime", platform: "Discord"));
    }

    [Fact]
    public async Task Uptime_NonTwitchWithoutRegistry_IsUntracked()
    {
        var service = Service(twitch: null, registry: null);
        Assert.Equal("Stream status isn't tracked.", await Run(service, "!uptime", platform: "Kick"));
    }

    // ── !followage ─────────────────────────────────────────────────────

    [Fact]
    public async Task FollowAge_WithTarget_LooksUpSanitizedLowercaseTarget()
    {
        var twitch = new Wave14TwitchService { FollowAgeResult = TimeSpan.FromDays(65) };

        Assert.Equal("Bob has been following for 2 months, 5 days.", await Run(Service(twitch), "!followage @Bob"));
        Assert.Equal(new[] { "bob" }, twitch.FollowAgeLookups);
    }

    [Fact]
    public async Task FollowAge_WithoutTarget_DefaultsToInvoker()
    {
        var twitch = new Wave14TwitchService { FollowAgeResult = TimeSpan.FromSeconds(90) };

        Assert.Equal(
            $"{Viewer} has been following for 1 minute.",
            await Run(Service(twitch), "!followage", isModerator: false, username: Viewer));
        Assert.Equal(new[] { Viewer.ToLowerInvariant() }, twitch.FollowAgeLookups);
    }

    [Fact]
    public async Task FollowAge_NotFollowing_ReportsNotFollowing()
    {
        var twitch = new Wave14TwitchService { FollowAgeResult = null };
        Assert.Equal("stranger isn't following the channel.", await Run(Service(twitch), "!followage stranger"));
    }

    [Theory]
    // Two largest non-zero units (coarse-to-fine).
    [InlineData(65, 0, 0, 0, "2 months, 5 days")]
    [InlineData(400, 0, 0, 0, "1 year, 1 month")]
    [InlineData(0, 3, 25, 0, "3 hours, 25 minutes")]
    [InlineData(3, 2, 30, 0, "3 days, 2 hours")]
    // A single non-zero unit renders alone; zero renders the sub-minute text.
    [InlineData(0, 0, 1, 0, "1 minute")]
    [InlineData(0, 0, 0, 45, "less than a minute")]
    public async Task FollowAge_HumanizesToTwoLargestUnits(int days, int hours, int minutes, int seconds, string expected)
    {
        var twitch = new Wave14TwitchService
        {
            FollowAgeResult = new TimeSpan(days, hours, minutes, seconds)
        };

        var reply = await Run(Service(twitch), "!followage someone");
        Assert.Equal($"someone has been following for {expected}.", reply);
    }

    [Fact]
    public async Task FollowAge_NonTwitchPlatform_IsTwitchOnly()
    {
        Assert.Equal("Follow-age lookup is Twitch-only.", await Run(Service(), "!followage anyone", platform: "Kick"));
    }

    // ── disconnected Twitch (null service or IsConnected false) ────────

    [Fact]
    public async Task NullTwitchService_TwitchOnlyCommands_ReportNotConnected()
    {
        var service = Service(twitch: null);

        Assert.Equal("Twitch is not connected.", await Run(service, "!raid someone"));
        Assert.Equal("Twitch is not connected.", await Run(service, "!title"));
        Assert.Equal("Twitch is not connected.", await Run(service, "!game"));
        Assert.Equal("Twitch is not connected.", await Run(service, "!uptime"));
        Assert.Equal("Twitch is not connected.", await Run(service, "!followage"));
        // !commercial validates its argument BEFORE the connection check.
        Assert.Equal("Usage: !commercial <30|60|90|120|150|180>", await Run(service, "!commercial"));
        Assert.Equal("Twitch is not connected.", await Run(service, "!commercial 60"));
    }

    [Fact]
    public async Task DisconnectedTwitchService_IsTreatedAsNotConnected()
    {
        var twitch = new Wave14TwitchService { IsConnected = false };
        var service = Service(twitch);

        Assert.Equal("Twitch is not connected.", await Run(service, "!raid someone"));
        Assert.Equal("Twitch is not connected.", await Run(service, "!title"));
        Assert.Empty(twitch.RaidTargets); // the guard fired before any Helix call
    }

    // ── mod gating across the Twitch-only command family ───────────────

    [Theory]
    [InlineData("!so target")]
    [InlineData("!raid target")]
    [InlineData("!title")]
    [InlineData("!game")]
    [InlineData("!commercial 30")]
    public async Task TwitchOnlyCommands_AreModeratorGated(string message)
    {
        var service = Service();

        Assert.Equal("That's a moderator-only command.", await Run(service, message, isModerator: false, username: Viewer));
    }
}
