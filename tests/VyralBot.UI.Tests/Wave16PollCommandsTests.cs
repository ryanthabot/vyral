using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using VyralBot.Core.Services;
using VyralBot.Core.Services.EventBus.Events;
using VyralBot.Data.Entities;
using Xunit;

namespace VyralBot.UI.Tests;

/// <summary>
/// WAVE-16: the !poll command family through the real
/// <see cref="PollCommandService"/> + <see cref="PollService"/> over the
/// in-memory <see cref="Wave16PollDataService"/> (real polls + votes),
/// plus the auto-end math in <see cref="GiveawayPollSchedulerService"/>
/// (tick invoked through the repo's reflection seam, same as the wave-8
/// timer gating tests).
///
/// NOTE on the wave-16 spec: "!poll start" in this build does NOT parse a
/// question/options/duration from chat — it starts a poll configured in
/// the UI (first Pending row). The inline-parse variants and the bare
/// "!vote" compat alias are covered by skipped facts at the bottom of
/// this file pending that feature.
/// </summary>
public class Wave16PollCommandsTests
{
    private readonly Wave16PollDataService _data = new();
    private readonly PollService _polls;
    private readonly PollCommandService _svc;

    public Wave16PollCommandsTests()
    {
        _polls = new PollService(_data, NullLogger<PollService>.Instance);
        _svc = new PollCommandService(NullLogger<PollCommandService>.Instance, _polls);
    }

    private static Poll NewPoll(
        string question = "Best game?",
        string[]? options = null,
        PollStatus status = PollStatus.Pending,
        DateTime? scheduledEnd = null,
        bool allowChange = false,
        string allowedPlatforms = "All",
        bool followersOnly = false,
        bool subscribersOnly = false,
        int minWatchtimeMinutes = 0)
        => new()
        {
            Question = question,
            Options = System.Text.Json.JsonSerializer.Serialize(options ?? new[] { "Option A", "Option B" }),
            CreatorUsername = "streamer",
            Status = status,
            ScheduledEndTime = scheduledEnd,
            AllowMultipleVotes = allowChange,
            AllowedPlatforms = allowedPlatforms,
            FollowersOnly = followersOnly,
            SubscribersOnly = subscribersOnly,
            MinWatchtimeMinutes = minWatchtimeMinutes
        };

    private Poll StartActivePoll()
    {
        var poll = NewPoll(status: PollStatus.Active);
        _data.Polls[poll.Id] = poll;
        return poll;
    }

    // ----- Command routing / usage parsing -----

    [Fact]
    public async Task NonPollCommand_ReturnsNull()
    {
        Assert.Null(await _svc.ProcessCommandAsync("!dice", "viewer", "Twitch", isModerator: false));
        Assert.Null(await _svc.ProcessCommandAsync("", "viewer", "Twitch", isModerator: false));
    }

    [Fact]
    public async Task BarePoll_ShowsUsage()
    {
        var reply = await _svc.ProcessCommandAsync("!poll", "viewer", "Twitch", isModerator: false);
        Assert.NotNull(reply);
        Assert.Contains("Usage: !poll", reply);
        Assert.Contains("start", reply);
        Assert.Contains("vote", reply);
        Assert.Contains("end", reply);
    }

    [Fact]
    public async Task UnknownSubcommand_ShowsUsage()
    {
        var reply = await _svc.ProcessCommandAsync("!poll frobnicate", "viewer", "Twitch", isModerator: false);
        Assert.NotNull(reply);
        Assert.Contains("Usage: !poll", reply);
    }

    [Fact]
    public async Task BareVoteAlias_NoActivePoll_ReportsCleanly()
    {
        // The wave-16 !vote alias answers with a clean no-active-poll message
        // instead of silently ignoring the viewer.
        var reply = await _svc.ProcessCommandAsync("!vote 1", "viewer", "Twitch", isModerator: false);
        Assert.NotNull(reply);
        Assert.Contains("No active poll", reply);
    }

    // ----- !poll start: mod gating + pre-configured poll lifecycle -----

    [Fact]
    public async Task Start_NonModerator_Rejected()
    {
        var reply = await _svc.ProcessCommandAsync("!poll start", "viewer", "Twitch", isModerator: false);
        Assert.NotNull(reply);
        Assert.Contains("Only moderators can start polls", reply);
    }

    [Fact]
    public async Task Start_NoConfiguredPoll_AdvisesUi()
    {
        var reply = await _svc.ProcessCommandAsync("!poll start", "mod", "Twitch", isModerator: true);
        Assert.NotNull(reply);
        Assert.Contains("No poll configured", reply);
        Assert.Empty(_data.Polls);
    }

    [Fact]
    public async Task Start_Mod_StartsPendingPoll_AndAnnouncesNumberedOptions()
    {
        var pending = NewPoll();
        _data.Polls[pending.Id] = pending;

        var reply = await _svc.ProcessCommandAsync("!poll start", "mod", "Twitch", isModerator: true);

        Assert.NotNull(reply);
        Assert.Contains("Poll started", reply);
        Assert.Contains(pending.Question, reply);
        Assert.Contains("(1) Option A", reply);
        Assert.Contains("(2) Option B", reply);
        Assert.Contains("!poll vote", reply);
        Assert.Equal(PollStatus.Active, _data.Polls[pending.Id].Status);
        Assert.NotNull(_data.Polls[pending.Id].StartedAt);
    }

    [Fact]
    public async Task Start_WhilePollActive_ReportsActiveQuestion()
    {
        var active = StartActivePoll();

        var reply = await _svc.ProcessCommandAsync("!poll start", "mod", "Twitch", isModerator: true);

        Assert.NotNull(reply);
        Assert.Contains("already active", reply);
        Assert.Contains(active.Question, reply);
        Assert.Contains("!poll end", reply);
    }

    [Fact]
    public async Task Start_ModButNoPendingRow_DoesNotResurrectCompletedPoll()
    {
        var done = NewPoll(status: PollStatus.Completed);
        _data.Polls[done.Id] = done;

        var reply = await _svc.ProcessCommandAsync("!poll start", "mod", "Twitch", isModerator: true);

        Assert.NotNull(reply);
        Assert.Contains("No poll configured", reply);
        Assert.Equal(PollStatus.Completed, _data.Polls[done.Id].Status);
    }

    // ----- !poll vote: parse + PollService gates -----

    [Fact]
    public async Task Vote_NoActivePoll_Advises()
    {
        var reply = await _svc.ProcessCommandAsync("!poll vote 1", "viewer", "Twitch", isModerator: false);
        Assert.NotNull(reply);
        Assert.Contains("No active poll", reply);
    }

    [Fact]
    public async Task Vote_MissingNumber_ShowsVoteUsage()
    {
        StartActivePoll();
        var reply = await _svc.ProcessCommandAsync("!poll vote", "viewer", "Twitch", isModerator: false);
        Assert.NotNull(reply);
        Assert.Contains("specify your vote", reply);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("-1")]
    public async Task Vote_InvalidNumber_Rejected(string badNumber)
    {
        StartActivePoll();
        var reply = await _svc.ProcessCommandAsync($"!poll vote {badNumber}", "viewer", "Twitch", isModerator: false);
        Assert.NotNull(reply);
        Assert.Contains("valid option number", reply);
        Assert.Empty(_data.PollVotes);
    }

    [Fact]
    public async Task Vote_OutOfRange_RejectedWithRangeHint()
    {
        StartActivePoll();
        var reply = await _svc.ProcessCommandAsync("!poll vote 3", "viewer", "Twitch", isModerator: false);
        Assert.NotNull(reply);
        Assert.Contains("Invalid option", reply);
        Assert.Contains("choose 1-2", reply);
        Assert.Empty(_data.PollVotes);
    }

    [Fact]
    public async Task Vote_Valid_RecordsVote_AndUpdatesTotal()
    {
        var poll = StartActivePoll();

        var reply = await _svc.ProcessCommandAsync("!poll vote 1", "alice", "Twitch", isModerator: false);

        Assert.NotNull(reply);
        Assert.Contains("@alice", reply);
        Assert.Contains("Vote recorded for option 1", reply);
        Assert.Contains("Option A", reply);
        var stored = Assert.Single(_data.PollVotes);
        Assert.Equal(0, stored.OptionIndex); // 1-based chat number -> 0-based index
        Assert.Equal(1, _data.Polls[poll.Id].TotalVotes);
    }

    [Fact]
    public async Task Vote_SameViewerTwice_Rejected()
    {
        StartActivePoll();
        await _svc.ProcessCommandAsync("!poll vote 1", "alice", "Twitch", isModerator: false);

        var reply = await _svc.ProcessCommandAsync("!poll vote 2", "alice", "Twitch", isModerator: false);

        Assert.NotNull(reply);
        Assert.Contains("already voted", reply);
        Assert.Single(_data.PollVotes);
    }

    [Fact]
    public async Task Vote_ChangeAllowed_WhenPollAllowsVoteChange()
    {
        var poll = NewPoll(status: PollStatus.Active, allowChange: true);
        _data.Polls[poll.Id] = poll;
        await _svc.ProcessCommandAsync("!poll vote 1", "alice", "Twitch", isModerator: false);

        var reply = await _svc.ProcessCommandAsync("!poll vote 2", "alice", "Twitch", isModerator: false);

        Assert.NotNull(reply);
        Assert.Contains("Vote changed to option 2", reply);
        Assert.Single(_data.PollVotes);
        Assert.Equal(1, _data.PollVotes[0].OptionIndex);
        Assert.Equal(1, _data.Polls[poll.Id].TotalVotes);
    }

    [Fact]
    public async Task Vote_PlatformRestrictedPoll_RejectsOtherPlatform()
    {
        var poll = NewPoll(status: PollStatus.Active, allowedPlatforms: "Twitch");
        _data.Polls[poll.Id] = poll;

        var reply = await _svc.ProcessCommandAsync("!poll vote 1", "alice", "Kick", isModerator: false);

        Assert.NotNull(reply);
        Assert.Contains("not open to Kick", reply);
        Assert.Empty(_data.PollVotes);
    }

    [Fact]
    public async Task Vote_FollowersOnly_RejectsNonFollower()
    {
        var poll = NewPoll(status: PollStatus.Active, followersOnly: true);
        _data.Polls[poll.Id] = poll;

        var reply = await _svc.ProcessCommandAsync(
            "!poll vote 1", "alice", "Twitch", isModerator: false, isFollower: false);

        Assert.NotNull(reply);
        Assert.Contains("must be a follower", reply);
        Assert.Empty(_data.PollVotes);
    }

    [Fact]
    public async Task Vote_SubscribersOnly_RejectsNonSubscriber()
    {
        var poll = NewPoll(status: PollStatus.Active, subscribersOnly: true);
        _data.Polls[poll.Id] = poll;

        var reply = await _svc.ProcessCommandAsync(
            "!poll vote 1", "alice", "Twitch", isModerator: false, isSubscriber: false);

        Assert.NotNull(reply);
        Assert.Contains("must be a subscriber", reply);
        Assert.Empty(_data.PollVotes);
    }

    [Theory]
    [InlineData(4, false)]
    [InlineData(5, true)]
    public async Task Vote_WatchtimeGate_BoundaryIsInclusive(int watchtime, bool expectedSuccess)
    {
        var poll = NewPoll(status: PollStatus.Active, minWatchtimeMinutes: 5);
        _data.Polls[poll.Id] = poll;

        var reply = await _svc.ProcessCommandAsync(
            "!poll vote 1", "alice", "Twitch", isModerator: false, watchtimeMinutes: watchtime);

        Assert.NotNull(reply);
        if (expectedSuccess)
            Assert.Contains("Vote recorded", reply);
        else
            Assert.Contains("at least 5 minutes of watchtime", reply);
    }

    // ----- !poll end / cancel / status -----

    [Fact]
    public async Task End_NonModerator_Rejected()
    {
        var reply = await _svc.ProcessCommandAsync("!poll end", "viewer", "Twitch", isModerator: false);
        Assert.NotNull(reply);
        Assert.Contains("Only moderators can end polls", reply);
    }

    [Fact]
    public async Task End_NoActivePoll_Advises()
    {
        var reply = await _svc.ProcessCommandAsync("!poll end", "mod", "Twitch", isModerator: true);
        Assert.NotNull(reply);
        Assert.Contains("No active poll to end", reply);
    }

    [Fact]
    public async Task End_WithVotes_CompletesPoll_AndReportsTotals()
    {
        var poll = StartActivePoll();
        await _svc.ProcessCommandAsync("!poll vote 1", "alice", "Twitch", isModerator: false);
        await _svc.ProcessCommandAsync("!poll vote 1", "bob", "Twitch", isModerator: false);

        var reply = await _svc.ProcessCommandAsync("!poll end", "mod", "Twitch", isModerator: true);

        Assert.NotNull(reply);
        Assert.Contains("Poll ended", reply);
        Assert.Contains("Option A wins with 2 votes", reply);
        Assert.Contains("Total votes: 2", reply);
        Assert.Equal(PollStatus.Completed, _data.Polls[poll.Id].Status);
        Assert.NotNull(_data.Polls[poll.Id].EndedAt);
    }

    [Fact]
    public async Task End_ZeroVotes_SaysNoVotesCast()
    {
        var poll = StartActivePoll();

        var reply = await _svc.ProcessCommandAsync("!poll end", "mod", "Twitch", isModerator: true);

        Assert.NotNull(reply);
        Assert.Contains("no votes were cast", reply);
        Assert.Contains("Total votes: 0", reply);
        Assert.Equal(PollStatus.Completed, _data.Polls[poll.Id].Status);
    }

    [Fact]
    public async Task End_TiedVotes_AnnouncesTie()
    {
        var poll = StartActivePoll();
        await _svc.ProcessCommandAsync("!poll vote 1", "alice", "Twitch", isModerator: false);
        await _svc.ProcessCommandAsync("!poll vote 2", "bob", "Twitch", isModerator: false);

        var reply = await _svc.ProcessCommandAsync("!poll end", "mod", "Twitch", isModerator: true);

        Assert.NotNull(reply);
        Assert.Contains("it's a tie between Option A and Option B", reply);
        Assert.Equal(PollStatus.Completed, _data.Polls[poll.Id].Status);
    }

    [Fact]
    public async Task Cancel_NonModerator_Rejected()
    {
        var reply = await _svc.ProcessCommandAsync("!poll cancel", "viewer", "Twitch", isModerator: false);
        Assert.NotNull(reply);
        Assert.Contains("Only moderators can cancel polls", reply);
    }

    [Fact]
    public async Task Status_ActivePoll_ShowsQuestionOptionsAndTotals()
    {
        var poll = StartActivePoll();
        await _svc.ProcessCommandAsync("!poll vote 2", "alice", "Twitch", isModerator: false);

        var reply = await _svc.ProcessCommandAsync("!poll status", "viewer", "Twitch", isModerator: false);

        Assert.NotNull(reply);
        Assert.Contains("Active Poll", reply);
        Assert.Contains(poll.Question, reply);
        Assert.Contains("(1) Option A", reply);
        Assert.Contains("(2) Option B", reply);
        Assert.Contains("Total votes: 1", reply);
    }

    // ----- PollService creation guard -----

    [Fact]
    public async Task CreatePoll_FewerThanTwoOptions_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _polls.CreatePollAsync("Q?", new[] { "Only one" }, "streamer"));
        Assert.Empty(_data.Polls);
    }

    // ----- Auto-end math: GiveawayPollSchedulerService tick (reflection seam) -----

    private (GiveawayPollSchedulerService Scheduler, RecordingEventBus Bus) NewScheduler()
    {
        var bus = new RecordingEventBus();
        var giveaways = new GiveawayService(
            _data, bus, new FakeLinkedAccounts(), new FakeViewerStatus(),
            NullLogger<GiveawayService>.Instance);
        return (new GiveawayPollSchedulerService(
            NullLogger<GiveawayPollSchedulerService>.Instance,
            _data, giveaways, _polls, bus), bus);
    }

    private static async Task TickAsync(GiveawayPollSchedulerService scheduler)
    {
        // Same reflection seam the wave-8 timer-gating tests use for
        // private TickAsync members of BackgroundServices.
        var method = typeof(GiveawayPollSchedulerService).GetMethod(
            "ProcessScheduledEndsAsync", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);
        await (Task)(method!.Invoke(scheduler, new object?[] { CancellationToken.None })
            ?? throw new InvalidOperationException("ProcessScheduledEndsAsync returned null"));
    }

    [Fact]
    public async Task Scheduler_PastScheduledEnd_CompletesPoll_AndPublishesResults()
    {
        var poll = NewPoll(status: PollStatus.Active, scheduledEnd: DateTime.UtcNow.AddMinutes(-1));
        _data.Polls[poll.Id] = poll;
        await _polls.VoteAsync(poll.Id, "alice", "Twitch", 0);
        var (scheduler, bus) = NewScheduler();

        await TickAsync(scheduler);

        Assert.Equal(PollStatus.Completed, _data.Polls[poll.Id].Status);
        var evt = Assert.IsType<PollAutoEndedEvent>(Assert.Single(bus.Published));
        Assert.Equal("Scheduler", evt.Source);
        Assert.Equal(poll.Question, evt.Question);
        Assert.Contains("has ended", evt.SummaryText);
        Assert.Contains("Option A: 1", evt.SummaryText);
    }

    [Fact]
    public async Task Scheduler_FutureScheduledEnd_LeavesPollActive_NoEvent()
    {
        var poll = NewPoll(status: PollStatus.Active, scheduledEnd: DateTime.UtcNow.AddHours(1));
        _data.Polls[poll.Id] = poll;
        var (scheduler, bus) = NewScheduler();

        await TickAsync(scheduler);

        Assert.Equal(PollStatus.Active, _data.Polls[poll.Id].Status);
        Assert.Empty(bus.Published);
    }

    [Fact]
    public async Task Scheduler_NoScheduledEnd_LeavesPollActive_NoEvent()
    {
        var poll = StartActivePoll();
        var (scheduler, bus) = NewScheduler();

        await TickAsync(scheduler);

        Assert.Equal(PollStatus.Active, _data.Polls[poll.Id].Status);
        Assert.Empty(bus.Published);
    }

    // ----- Wave-16 spec features NOT in this build (skipped, see class docs) -----

    [Fact(Skip = "Feature absent in this build: !poll start does not parse a question + 2-6 options from chat; it starts a UI-configured pending poll. Wire this up when ChatViewModel/PollCommandService accept inline '!poll start <question> | opt1 | opt2 ...'.")]
    public async Task PollStartInline_ParsesQuestionAndTwoToSixOptions()
        => await Task.CompletedTask;

    [Fact(Skip = "Feature absent in this build: optional duration argument (e.g. '!poll start ... 60') is not parsed and auto-end is only driven by the UI-set ScheduledEndTime via GiveawayPollSchedulerService.")]
    public async Task PollStartInline_OptionalDurationMinutes_SetsScheduledEnd()
        => await Task.CompletedTask;

    [Fact(Skip = "Feature absent in this build: no bare '!vote <n>' compat alias is routed to the poll service (verified by BareVoteAlias_ReturnsNull_CompatAliasAbsent above); add the alias then flip this test on.")]
    public async Task VoteAlias_RoutesToActivePoll()
        => await Task.CompletedTask;
}
