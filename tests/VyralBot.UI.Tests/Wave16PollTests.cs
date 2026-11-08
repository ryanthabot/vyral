using Xunit;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using VyralBot.Api.Controllers;
using VyralBot.Core.Services;
using VyralBot.Data;
using VyralBot.Data.Entities;

namespace VyralBot.UI.Tests;

/// <summary>
/// Wave-16 coverage for the chat-driven poll surface and its overlay feed:
/// <see cref="PollCommandService"/> (ad-hoc "!poll start Q | A | B" with the
/// optional duration token and the everyone "!vote n" alias — all sharing the
/// real <see cref="PollService"/> pipeline against an in-memory
/// <see cref="IDataService"/>) and <see cref="PollController"/> (the
/// /poll/data snapshot the polloverlay.html browser source polls: active
/// shape, lingering last results, winner/tie detection). Controller actions
/// are invoked directly with a DefaultHttpContext, same as the wave-14
/// controller tests — no Kestrel host needed.
/// </summary>
public class Wave16PollTests
{
    private static object? Prop(object source, string name)
        => source.GetType().GetProperty(name)?.GetValue(source);

    // ── Shared fakes ──────────────────────────────────────────────────

    /// <summary>
    /// In-memory poll store layered over the wave-8 IDataService skeleton
    /// (re-declares IDataService so the `new` poll members re-implement the
    /// interface, the Wave14TtsDataService trick — PollService calls through
    /// IDataService, so shadowing without re-implementation would hit the
    /// throwing base members).
    /// </summary>
    public sealed class Wave16PollDataService : IntegrationWave8DataService, IDataService
    {
        public List<Poll> PollRows { get; } = new();
        public List<PollVote> VoteRows { get; } = new();

        public new Task<Poll?> GetPollAsync(Guid id)
            => Task.FromResult(PollRows.FirstOrDefault(p => p.Id == id));

        public new Task<Poll?> GetActivePollAsync()
            => Task.FromResult(PollRows
                .Where(p => p.Status == PollStatus.Active)
                .OrderByDescending(p => p.StartedAt)
                .FirstOrDefault());

        public new Task SavePollAsync(Poll poll)
        {
            var existing = PollRows.FirstOrDefault(p => p.Id == poll.Id);
            if (existing != null)
            {
                PollRows.Remove(existing);
            }
            PollRows.Add(poll);
            return Task.CompletedTask;
        }

        public new Task<IEnumerable<Poll>> GetPollsAsync()
            => Task.FromResult<IEnumerable<Poll>>(PollRows.OrderByDescending(p => p.CreatedAt).ToList());

        public new Task<PollVote?> GetPollVoteAsync(Guid pollId, string username, string platform)
            => Task.FromResult(VoteRows.FirstOrDefault(v =>
                v.PollId == pollId
                && v.Username.Equals(username, StringComparison.OrdinalIgnoreCase)
                && v.Platform.Equals(platform, StringComparison.OrdinalIgnoreCase)));

        public new Task SavePollVoteAsync(PollVote vote)
        {
            var existing = VoteRows.FirstOrDefault(v => v.Id == vote.Id);
            if (existing != null)
            {
                VoteRows.Remove(existing);
            }
            VoteRows.Add(vote);
            return Task.CompletedTask;
        }

        public new Task<IEnumerable<PollVote>> GetPollVotesAsync(Guid pollId)
            => Task.FromResult<IEnumerable<PollVote>>(VoteRows.Where(v => v.PollId == pollId).ToList());

        public new Task<int> GetPollVoteCountAsync(Guid pollId)
            => Task.FromResult(VoteRows.Count(v => v.PollId == pollId));

        public new Task<Dictionary<int, int>> GetPollVoteResultsAsync(Guid pollId)
            => Task.FromResult(VoteRows
                .Where(v => v.PollId == pollId)
                .GroupBy(v => v.OptionIndex)
                .ToDictionary(g => g.Key, g => g.Count()));
    }

    private static (PollService Service, Wave16PollDataService Data) PollHarness()
    {
        var data = new Wave16PollDataService();
        var service = new PollService(data, NullLogger<PollService>.Instance);
        return (service, data);
    }

    private static PollCommandService Commands(PollService service)
        => new(NullLogger<PollCommandService>.Instance, service);

    private static PollController Controller(PollService? service)
        => new(NullLogger<PollController>.Instance, service)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

    /// <summary>Creates + starts a poll through the real service pipeline.</summary>
    private static async Task<Poll> ActivePollAsync(PollService service, string question = "Pick one", params string[] options)
    {
        var poll = await service.CreatePollAsync(question, options, "broadcaster");
        var started = await service.StartPollAsync(poll.Id);
        Assert.True(started);
        return poll;
    }

    // ── PollCommandService: !vote alias ───────────────────────────────

    [Fact]
    public async Task VoteAlias_CastsAndDedupsThroughTheRealPipeline()
    {
        var (service, _) = PollHarness();
        var poll = await ActivePollAsync(service, "Pick one", "Red", "Blue");
        var commands = Commands(service);

        var first = await commands.ProcessCommandAsync("!vote 2", "alice", "Twitch", isModerator: false);
        Assert.Contains("Vote recorded for option 2: Blue", first);

        var second = await commands.ProcessCommandAsync("!VOTE 1", "alice", "Twitch", isModerator: false);
        Assert.Contains("already voted", second);

        var results = await service.GetCurrentResultsAsync(poll.Id);
        Assert.Equal(1, results!["Blue"]);
        Assert.Equal(0, results!["Red"]);
    }

    [Fact]
    public async Task VoteAlias_RejectsMissingOrInvalidNumber()
    {
        var (service, _) = PollHarness();
        await ActivePollAsync(service, "Pick one", "Red", "Blue");
        var commands = Commands(service);

        Assert.Contains("valid option number", await commands.ProcessCommandAsync("!vote abc", "alice", "Twitch", false));
        Assert.Contains("specify your vote", await commands.ProcessCommandAsync("!vote", "alice", "Twitch", false));
    }

    [Fact]
    public async Task VoteAlias_DoesNotSwallowUnrelatedCommands()
    {
        var (service, _) = PollHarness();
        var commands = Commands(service);

        Assert.Null(await commands.ProcessCommandAsync("!voters assemble", "alice", "Twitch", false));
        Assert.Null(await commands.ProcessCommandAsync("!hello", "alice", "Twitch", false));
    }

    // ── PollCommandService: ad-hoc !poll start ────────────────────────

    [Fact]
    public async Task StartAdHoc_CreatesAndStartsPollWithPipeOptions()
    {
        var (service, data) = PollHarness();
        var commands = Commands(service);

        var response = await commands.ProcessCommandAsync(
            "!poll start Best game? | Zelda | Elden Ring | Minecraft", "mod1", "Twitch", isModerator: true);

        Assert.Contains("Poll started", response);
        Assert.Contains("Best game?", response);
        Assert.Contains("(2) Elden Ring", response);

        var active = await service.GetActivePollAsync();
        Assert.NotNull(active);
        Assert.Equal("Best game?", active!.Question);
        Assert.Equal("Best game?", data.PollRows.Single(p => p.Status == PollStatus.Active).Question);
        var options = JsonSerializer.Deserialize<string[]>(active.Options)!;
        Assert.Equal(new[] { "Zelda", "Elden Ring", "Minecraft" }, options);

        // The poll is immediately votable via the alias.
        Assert.Contains("Vote recorded", await commands.ProcessCommandAsync("!vote 1", "viewer", "Twitch", false));
    }

    [Theory]
    [InlineData("!poll start Q | OnlyOne")]
    [InlineData("!poll start Q | 1 | 2 | 3 | 4 | 5 | 6 | 7")]
    [InlineData("!poll start Q | A |  | B")]           // empty option segment
    [InlineData("!poll start | A | B")]                 // missing question
    public async Task StartAdHoc_RejectsMalformedInput(string command)
    {
        var (service, _) = PollHarness();
        var commands = Commands(service);

        var response = await commands.ProcessCommandAsync(command, "mod1", "Twitch", isModerator: true);

        Assert.StartsWith("⚠️", response);
        Assert.Null(await service.GetActivePollAsync());
    }

    [Fact]
    public async Task StartAdHoc_DurationTokenSetsScheduledEnd()
    {
        var (service, _) = PollHarness();
        var commands = Commands(service);
        var before = DateTime.UtcNow;

        var response = await commands.ProcessCommandAsync(
            "!poll start 90s Map pick? | Dust | Mirage", "mod1", "Twitch", isModerator: true);

        Assert.Contains("Poll started", response);
        var active = await service.GetActivePollAsync();
        Assert.NotNull(active!.ScheduledEndTime);
        Assert.InRange(active.ScheduledEndTime!.Value, before.AddSeconds(85), before.AddSeconds(95));
    }

    [Fact]
    public async Task StartAdHoc_DurationOutOfRangeIsRejected()
    {
        var (service, _) = PollHarness();
        var commands = Commands(service);

        var response = await commands.ProcessCommandAsync(
            "!poll start 30h Q | A | B", "mod1", "Twitch", isModerator: true);

        Assert.Contains("duration must be between", response);
        Assert.Null(await service.GetActivePollAsync());
    }

    [Fact]
    public async Task StartAdHoc_IsModGated()
    {
        var (service, _) = PollHarness();
        var commands = Commands(service);

        var response = await commands.ProcessCommandAsync(
            "!poll start Q | A | B", "viewer", "Twitch", isModerator: false);

        Assert.Contains("Only moderators", response);
        Assert.Null(await service.GetActivePollAsync());
    }

    [Fact]
    public async Task Start_BareStillStartsPendingUiPoll()
    {
        var (service, _) = PollHarness();
        // Legacy path: a poll created in the UI sits Pending until bare "!poll start".
        var pending = await service.CreatePollAsync("UI question", new[] { "A", "B" }, "streamer");
        var commands = Commands(service);

        var response = await commands.ProcessCommandAsync("!poll start", "mod1", "Twitch", isModerator: true);

        Assert.Contains("Poll started: UI question", response);
        var active = await service.GetActivePollAsync();
        Assert.Equal(pending.Id, active!.Id);
    }

    // ── PollCommandService: !poll end ─────────────────────────────────

    [Fact]
    public async Task End_ReturnsResultsStringForTheChatRouter()
    {
        var (service, data) = PollHarness();
        var poll = await ActivePollAsync(service, "Pick one", "Red", "Blue");
        await service.VoteAsync(poll.Id, "a", "Twitch", 0);
        await service.VoteAsync(poll.Id, "b", "Twitch", 0);
        await service.VoteAsync(poll.Id, "c", "Kick", 1);
        var commands = Commands(service);

        var response = await commands.ProcessCommandAsync("!poll end", "mod1", "Twitch", isModerator: true);

        Assert.Contains("Poll ended", response);
        Assert.Contains("Red wins with 2 votes", response);
        Assert.Contains("Total votes: 3", response);
        Assert.Equal(PollStatus.Completed, data.PollRows.Single(p => p.Id == poll.Id).Status);
    }

    // ── PollController: /poll/data ────────────────────────────────────

    [Fact]
    public async Task Poll_WithoutPollService_Returns503()
    {
        var result = await Controller(null).Data();

        var status = Assert.IsType<ObjectResult>(result);
        Assert.Equal(503, status.StatusCode);
    }

    [Fact]
    public async Task Poll_ActiveSnapshotHasOrderedOptionsPercentAndTotal()
    {
        var (service, _) = PollHarness();
        var poll = await ActivePollAsync(service, "Pick one", "Red", "Blue");
        await service.VoteAsync(poll.Id, "a", "Twitch", 0);
        await service.VoteAsync(poll.Id, "b", "Twitch", 0);
        await service.VoteAsync(poll.Id, "c", "Kick", 1);

        var payload = Assert.IsType<OkObjectResult>(await Controller(service).Data()).Value!;

        var active = Prop(payload, "active")!;
        Assert.Equal("Pick one", Prop(active, "question"));
        Assert.Equal(3, Prop(active, "totalVotes"));
        Assert.Null(Prop(active, "endsAt"));
        var options = (System.Collections.IEnumerable)Prop(active, "options")!;
        var list = options.Cast<object>().ToList();
        Assert.Equal(2, list.Count);
        Assert.Equal(1, Prop(list[0], "index"));
        Assert.Equal("Red", Prop(list[0], "text"));
        Assert.Equal(2, Prop(list[0], "votes"));
        Assert.Equal(66.7, Prop(list[0], "percent"));
        Assert.Equal(1, Prop(list[1], "votes"));
        Assert.Null(Prop(payload, "lastResults"));
    }

    [Fact]
    public async Task Poll_ActiveEndsAtIsUtcIso()
    {
        var (service, _) = PollHarness();
        var endsAt = DateTime.UtcNow.AddMinutes(5);
        var poll = await service.CreatePollAsync("Timed?", new[] { "A", "B" }, "streamer", endsAt);
        await service.StartPollAsync(poll.Id);

        var payload = Assert.IsType<OkObjectResult>(await Controller(service).Data()).Value!;

        var endsAtJson = (string?)Prop(Prop(payload, "active")!, "endsAt");
        Assert.NotNull(endsAtJson);
        // Round-trips back to the same instant regardless of stored Kind.
        var parsed = DateTime.Parse(endsAtJson!, null, System.Globalization.DateTimeStyles.RoundtripKind);
        Assert.Equal(endsAt.Ticks, parsed.ToUniversalTime().Ticks);
    }

    [Fact]
    public async Task Poll_LastResultsLingerFiveMinutesThenDisappear()
    {
        var (service, data) = PollHarness();
        var poll = await ActivePollAsync(service, "Pick one", "Red", "Blue");
        await service.VoteAsync(poll.Id, "a", "Twitch", 0);
        await service.VoteAsync(poll.Id, "c", "Twitch", 0);
        await service.VoteAsync(poll.Id, "b", "Kick", 1);
        await service.EndPollAsync(poll.Id);

        var fresh = Assert.IsType<OkObjectResult>(await Controller(service).Data()).Value!;
        var last = Prop(fresh, "lastResults")!;
        Assert.Null(Prop(fresh, "active"));
        Assert.Equal("Red", Prop(last, "winner"));
        Assert.False((bool)Prop(last, "isTie")!);
        Assert.Equal(3, Prop(last, "totalVotes"));

        // Six minutes old: past the linger window, the overlay hides itself.
        var ended = data.PollRows.Single(p => p.Id == poll.Id);
        ended.EndedAt = DateTime.UtcNow.AddMinutes(-6);
        await data.SavePollAsync(ended);

        var stale = Assert.IsType<OkObjectResult>(await Controller(service).Data()).Value!;
        Assert.Null(Prop(stale, "active"));
        Assert.Null(Prop(stale, "lastResults"));
    }

    [Fact]
    public async Task Poll_TieHasWinnerNull()
    {
        var (service, _) = PollHarness();
        var poll = await ActivePollAsync(service, "Pick one", "Red", "Blue");
        await service.VoteAsync(poll.Id, "a", "Twitch", 0);
        await service.VoteAsync(poll.Id, "b", "Kick", 1);
        await service.EndPollAsync(poll.Id);

        var payload = Assert.IsType<OkObjectResult>(await Controller(service).Data()).Value!;

        var last = Prop(payload, "lastResults")!;
        Assert.Null(Prop(last, "winner"));
        Assert.True((bool)Prop(last, "isTie")!);
    }
}
