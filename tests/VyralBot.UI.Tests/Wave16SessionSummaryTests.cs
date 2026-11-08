using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using VyralBot.Core.Models;
using VyralBot.Core.Services.EventBus.Events;
using VyralBot.Core.Services.Statistics;
using VyralBot.Data;
using VyralBot.Data.Entities;
using Xunit;

namespace VyralBot.UI.Tests;

/// <summary>
/// Wave-16 stream session summaries: <see cref="SessionSummaryCompiler"/>
/// compiles the post-stream report from the session window's event rows
/// (breakdowns, value sums, top chatters, duration math) and
/// <see cref="SessionSummaryService"/> compiles → persists (capped JSON list
/// in the Configs store) → publishes <see cref="SessionEndedEvent"/> when the
/// last live platform goes offline, with honest flapping/multi-platform rules.
/// </summary>
public class Wave16SessionSummaryTests
{
    private static readonly DateTime Start = new(2026, 11, 6, 19, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime End = new(2026, 11, 6, 21, 30, 0, DateTimeKind.Utc);

    private static EventStatistic Row(
        string eventType,
        string username = "viewer",
        string platform = "Twitch",
        string? eventData = null,
        DateTime? at = null) => new()
    {
        Platform = platform,
        EventType = eventType,
        Username = username,
        EventData = eventData,
        Timestamp = at ?? Start.AddMinutes(30)
    };

    // ======================================================================
    // Compilation (SessionSummaryCompiler — pure, fake stats rows)
    // ======================================================================

    [Fact]
    public void Compile_BreakdownFromFakeStatsRows()
    {
        var rows = new[]
        {
            Row("Follow", "a"), Row("Follow", "b"), Row("follow", "c"), // case-insensitive match
            Row("Subscribe", "d"), Row("Resub", "e"), Row("GiftSub", "f", eventData: """{"count":5}"""),
            Row("Raid", "g", eventData: """{"viewers":10}"""),
            Row("Cheer", "h", eventData: """{"bits":100}"""), Row("Cheer", "i", eventData: """{"bits":500,"message":"hi"}"""),
            Row("Host", "j")
        };

        var summary = SessionSummaryCompiler.Compile(Start, End, "Twitch", rows, currencyEarned: 1234, chatCounts: null);

        Assert.Equal(3, summary.NewFollowers);
        Assert.Equal(3, summary.NewSubs); // Subscribe + Resub + GiftSub
        Assert.Equal(1, summary.Raids);
        Assert.Equal(10, summary.RaidViewers);
        Assert.Equal(2, summary.Cheers);
        Assert.Equal(600, summary.Bits);
        Assert.Equal(1234L, summary.CurrencyAwarded);
        Assert.Equal(0, summary.ChatMessages);
        Assert.Empty(summary.TopChatters);

        // Full breakdown keeps the canonical writer casing ("Follow", not "follow").
        Assert.Equal(3, summary.EventCounts["Follow"]);
        Assert.DoesNotContain("follow", summary.EventCounts.Keys); // folded into "Follow"
        Assert.Equal(1, summary.EventCounts["Host"]);
        Assert.Equal(7, summary.EventCounts.Count); // Follow, Subscribe, Resub, GiftSub, Raid, Cheer, Host
    }

    [Fact]
    public void Compile_TopChatters_OrderedDescending_WithOrdinalTieBreak()
    {
        var chat = new Dictionary<string, int>
        {
            ["alice"] = 5,
            ["bob"] = 12,
            ["Carol"] = 9,
            ["dave"] = 9,   // ties with Carol → ordinal "Carol" first
            ["silent"] = 0  // zero-count rows dropped
        };

        var summary = SessionSummaryCompiler.Compile(Start, End, "Kick", Array.Empty<EventStatistic>(), 0, chat);

        Assert.Equal(35, summary.ChatMessages);
        Assert.Equal(new[] { "bob", "Carol", "dave" }, summary.TopChatters.Select(c => c.Username));
        Assert.Equal(12, summary.TopChatters[0].Messages);
    }

    [Fact]
    public void Compile_DurationMath_RoundsToWholeMinutes_AndClampsNegative()
    {
        // 2h 30m → 150 minutes
        var s = SessionSummaryCompiler.Compile(Start, End, "Twitch", Array.Empty<EventStatistic>(), 0);
        Assert.Equal(150, s.DurationMinutes);

        // 44.6 min rounds to 45
        s = SessionSummaryCompiler.Compile(Start, Start.AddSeconds(44.6 * 60), "Twitch", Array.Empty<EventStatistic>(), 0);
        Assert.Equal(45, s.DurationMinutes);

        // end before start clamps to 0 (never negative)
        s = SessionSummaryCompiler.Compile(Start, Start.AddMinutes(-5), "Twitch", Array.Empty<EventStatistic>(), 0);
        Assert.Equal(0, s.DurationMinutes);
    }

    [Fact]
    public void Compile_MalformedEventData_CountsEvent_ButSumsZero()
    {
        var rows = new[] { Row("Cheer", "x", eventData: """{"bits":"not-a-number"}"""), Row("Cheer", "y", eventData: "not json at all") };

        var summary = SessionSummaryCompiler.Compile(Start, End, "Twitch", rows, 0);

        Assert.Equal(2, summary.Cheers);
        Assert.Equal(0, summary.Bits);
    }

    // ======================================================================
    // Service (registry transitions → compile → persist → publish)
    // ======================================================================

    private sealed class Harness
    {
        public Wave15DispatchingEventBus Bus { get; } = new();
        public Wave16SessionDataService Data { get; } = new();
        public Wave14LiveRegistry Registry { get; } = new();
        public MutableClock Clock { get; } = new() { Now = new DateTime(2026, 11, 6, 19, 0, 0, DateTimeKind.Utc) };
        public SessionSummaryService Service { get; }

        public Harness() => Service = new SessionSummaryService(
            NullLogger<SessionSummaryService>.Instance, Bus, Data, Registry, Clock.GetNow);

        public async Task StartAsync() => await Service.StartAsync();

        /// <summary>Publishes a chat message through the bus (runs handlers inline).</summary>
        public async Task ChatAsync(string username, string platform = "Twitch")
            => await Bus.PublishAsync(new ChatMessageReceivedEvent
            {
                Source = platform,
                Platform = platform,
                Username = username,
                Message = "hi"
            });
    }

    private sealed class MutableClock
    {
        public DateTime Now { get; set; }
        public DateTime GetNow() => Now;
    }

    [Fact]
    public async Task SessionLifecycle_PersistsSummary_AndPublishesSessionEndedEvent()
    {
        var h = new Harness();
        await h.StartAsync();

        h.Data.StatRows.AddRange(new[]
        {
            Row("Follow", "a", at: h.Clock.Now.AddMinutes(10)),
            Row("Follow", "b", at: h.Clock.Now.AddMinutes(11)),
            Row("Subscribe", "c", at: h.Clock.Now.AddMinutes(12)),
            Row("Cheer", "d", eventData: """{"bits":250}""", at: h.Clock.Now.AddMinutes(13))
        });
        h.Data.EarnedSince = 987;

        h.Registry.SetLive("Twitch", true);              // session opens at 19:00
        await h.ChatAsync("alice");
        await h.ChatAsync("alice");
        await h.ChatAsync("bob", platform: "Kick");      // any platform's chat counts
        h.Clock.Now = h.Clock.Now.AddHours(2).AddMinutes(5);
        h.Registry.SetLive("Twitch", false);             // session closes at 21:05

        var published = h.Bus.Published.OfType<SessionEndedEvent>().ToList();
        var evt = Assert.Single(published);
        Assert.Equal("Twitch", evt.Summary.Platforms);
        Assert.Equal(125, evt.Summary.DurationMinutes);
        Assert.Equal(2, evt.Summary.NewFollowers);
        Assert.Equal(1, evt.Summary.NewSubs);
        Assert.Equal(250, evt.Summary.Bits);
        Assert.Equal(3, evt.Summary.ChatMessages);
        Assert.Equal(987L, evt.Summary.CurrencyAwarded);
        Assert.Equal("alice", evt.Summary.TopChatters[0].Username);

        // The summary window is passed to the data layer.
        var queriedSince = Assert.Single(h.Data.TransactionTotalsSinceCalls);
        Assert.Equal(new DateTime(2026, 11, 6, 19, 0, 0, DateTimeKind.Utc), queriedSince);

        // Persistence round-trip: the JSON list landed in the Configs store and
        // reads back through the public API (newest first).
        Assert.True(h.Data.ConfigValues.ContainsKey("Statistics.SessionSummaries"));
        var stored = JsonSerializer.Deserialize<List<StreamSessionSummary>>(h.Data.ConfigValues["Statistics.SessionSummaries"]);
        Assert.NotNull(stored);
        var roundTripped = Assert.Single(stored);
        Assert.Equal(evt.Summary.Id, roundTripped.Id);
        Assert.Equal(125, roundTripped.DurationMinutes);
        Assert.Equal(250, roundTripped.Bits);

        var recent = await h.Service.GetRecentSummariesAsync();
        var read = Assert.Single(recent);
        Assert.Equal(evt.Summary.Id, read.Id);
        Assert.Equal("alice", read.TopChatters[0].Username); // nested list survived the round-trip
    }

    [Fact]
    public async Task MultiPlatform_SessionEndsOnlyWhenLastPlatformGoesOffline()
    {
        var h = new Harness();
        await h.StartAsync();

        h.Registry.SetLive("Twitch", true);
        h.Registry.SetLive("Kick", true);
        await h.ChatAsync("viewer");
        h.Clock.Now = h.Clock.Now.AddHours(1);
        h.Registry.SetLive("Twitch", false); // Kick still live → no summary yet
        Assert.Empty(h.Bus.Published.OfType<SessionEndedEvent>());

        h.Clock.Now = h.Clock.Now.AddHours(1);
        h.Registry.SetLive("Kick", false);   // last one out → summary

        var evt = Assert.Single(h.Bus.Published.OfType<SessionEndedEvent>());
        Assert.Equal("Kick, Twitch", evt.Summary.Platforms); // ordinal, both platforms recorded
        Assert.Equal(120, evt.Summary.DurationMinutes);      // start → last offline
        Assert.Equal(1, evt.Summary.ChatMessages);
    }

    [Fact]
    public async Task SubMinuteSession_IsDiscarded_NoSummaryNoEvent()
    {
        var h = new Harness();
        await h.StartAsync();

        h.Registry.SetLive("Twitch", true);
        h.Clock.Now = h.Clock.Now.AddSeconds(30); // YouTube live-chat state flapping window
        h.Registry.SetLive("Twitch", false);

        Assert.Empty(h.Bus.Published.OfType<SessionEndedEvent>());
        Assert.Empty(await h.Service.GetRecentSummariesAsync());
        Assert.False(h.Data.ConfigValues.ContainsKey("Statistics.SessionSummaries"));
    }

    [Fact]
    public async Task ChatMessages_OutsideASession_AreNotCounted()
    {
        var h = new Harness();
        await h.StartAsync();

        await h.ChatAsync("before");                 // before start → ignored
        h.Registry.SetLive("Twitch", true);
        h.Clock.Now = h.Clock.Now.AddHours(1);
        h.Registry.SetLive("Twitch", false);         // closes (empty counters)
        await h.ChatAsync("after");                  // after end → ignored

        var evt = Assert.Single(h.Bus.Published.OfType<SessionEndedEvent>());
        Assert.Equal(0, evt.Summary.ChatMessages);
    }

    [Fact]
    public async Task ChatCounts_AreCaseInsensitivePerChatter()
    {
        var h = new Harness();
        await h.StartAsync();

        h.Registry.SetLive("Twitch", true);
        await h.ChatAsync("Chatter");
        await h.ChatAsync("chatter");
        await h.ChatAsync("CHATTER");
        h.Clock.Now = h.Clock.Now.AddHours(1);
        h.Registry.SetLive("Twitch", false);

        var evt = Assert.Single(h.Bus.Published.OfType<SessionEndedEvent>());
        Assert.Equal(3, evt.Summary.ChatMessages);
        var top = Assert.Single(evt.Summary.TopChatters);
        Assert.Equal("Chatter", top.Username); // first-seen casing kept
        Assert.Equal(3, top.Messages);
    }

    [Fact]
    public async Task Persistence_CapsStoredSummariesAtTwenty_NewestFirst()
    {
        var h = new Harness();
        await h.StartAsync();

        // 20 already-persisted summaries.
        var seed = Enumerable.Range(1, 20)
            .Select(i => new StreamSessionSummary { StartedAtUtc = Start.AddHours(-i), EndedAtUtc = Start.AddHours(-i).AddMinutes(60) })
            .ToList();
        await h.Data.SetConfigValueAsync("Statistics.SessionSummaries", JsonSerializer.Serialize(seed), "Statistics");

        h.Registry.SetLive("Twitch", true);
        await h.ChatAsync("fresh");
        h.Clock.Now = h.Clock.Now.AddHours(1);
        h.Registry.SetLive("Twitch", false);

        var recent = await h.Service.GetRecentSummariesAsync();
        Assert.Equal(20, recent.Count);
        Assert.Equal(1, recent[0].ChatMessages); // the new session is first
        Assert.Equal(1, recent.Count(s => s.ChatMessages == 1));
    }

    [Fact]
    public async Task CorruptStoredJson_StartsAFreshList_InsteadOfThrowing()
    {
        var h = new Harness();
        await h.Data.SetConfigValueAsync("Statistics.SessionSummaries", "{{{ not json", "Statistics");

        var recent = await h.Service.GetRecentSummariesAsync();
        Assert.Empty(recent);
    }

    [Fact]
    public async Task StopAsync_WithOpenSession_CompilesTheSummaryBestEffort()
    {
        var h = new Harness();
        await h.StartAsync();

        h.Data.StatRows.Add(Row("Follow", "a", at: h.Clock.Now.AddMinutes(5)));
        h.Registry.SetLive("Twitch", true);
        await h.ChatAsync("nightowl");
        h.Clock.Now = h.Clock.Now.AddHours(3);

        await h.Service.StopAsync(); // app shutdown while live → still a report

        var evt = Assert.Single(h.Bus.Published.OfType<SessionEndedEvent>());
        Assert.Equal(180, evt.Summary.DurationMinutes);
        Assert.Equal("Twitch", evt.Summary.Platforms);
        Assert.Equal(1, evt.Summary.ChatMessages);
    }

    [Fact]
    public async Task Lifecycle_IsIdempotentAndUnsubscribes()
    {
        var h = new Harness();
        await h.StartAsync();
        Assert.Equal(1, h.Bus.SubscriptionCount);

        await h.Service.StartAsync(); // second call is a no-op
        Assert.Equal(1, h.Bus.SubscriptionCount);

        await h.Service.StopAsync();
        Assert.Equal(0, h.Bus.SubscriptionCount);

        // Registry hook removed too: transitions after stop produce nothing.
        h.Registry.SetLive("Twitch", true);
        h.Clock.Now = h.Clock.Now.AddHours(1);
        h.Registry.SetLive("Twitch", false);
        Assert.Empty(h.Bus.Published.OfType<SessionEndedEvent>());

        // StopAsync is safe to call twice.
        await h.Service.StopAsync();
    }
}

/// <summary>
/// IDataService fake for the wave-16 session summary tests: layers the two
/// compile-time reads (<see cref="IDataService.GetEventStatisticsAsync"/> with
/// faithful platform/type/date filtering, and
/// <see cref="IDataService.GetCurrencyTransactionTotalsSinceAsync"/>) over the
/// wave-8 in-memory config fake, which provides the real
/// Get/SetConfigValueAsync behavior the persistence round-trip exercises.
/// Everything else inherits the throwing fake — the service is read/write on
/// configs only, so accidental writes elsewhere fail loudly.
/// </summary>
public sealed class Wave16SessionDataService : IntegrationWave8DataService, IDataService
{
    /// <summary>Event statistic rows the compile step will read.</summary>
    public List<EventStatistic> StatRows { get; } = new();

    /// <summary>Earned total returned by GetCurrencyTransactionTotalsSinceAsync.</summary>
    public long EarnedSince { get; set; }

    /// <summary>Every "since" argument handed to GetCurrencyTransactionTotalsSinceAsync.</summary>
    public List<DateTime> TransactionTotalsSinceCalls { get; } = new();

    public new Task<IEnumerable<EventStatistic>> GetEventStatisticsAsync(
        string? platform = null, string? eventType = null, int limit = 100, DateTime? startDate = null, DateTime? endDate = null)
    {
        IEnumerable<EventStatistic> Filter()
        {
            foreach (var row in StatRows)
            {
                if (platform is not null && !string.Equals(row.Platform, platform, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (eventType is not null && !string.Equals(row.EventType, eventType, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (startDate is { } start && row.Timestamp < start)
                {
                    continue;
                }

                if (endDate is { } end && row.Timestamp > end)
                {
                    continue;
                }

                yield return row;
            }
        }

        return Task.FromResult(Filter().Take(limit));
    }

    public new Task<(long Earned, long Spent)> GetCurrencyTransactionTotalsSinceAsync(DateTime since)
    {
        TransactionTotalsSinceCalls.Add(since);
        return Task.FromResult((EarnedSince, 0L));
    }
}
