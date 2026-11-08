using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using VyralBot.Core.Services.EventBus;
using VyralBot.Core.Services.EventBus.Events;
using VyralBot.Data;
using VyralBot.Integrations.Kick.Events;
using VyralBot.Integrations.StreamStatus;
using VyralBot.Integrations.Testing;
using VyralBot.Integrations.Twitch.Events;
using VyralBot.Integrations.YouTube.Events;
using VyralBot.UI.Services;
using VyralBot.Workflows.Models;
using VyralBot.Workflows.Nodes;
using VyralBot.Workflows.Persistence;
using VyralBot.Workflows.Runtime;
using Xunit;

namespace VyralBot.UI.Tests;

/// <summary>
/// WAVE-17B close-out for stream live/offline workflow triggers — the seams the
/// wave-17 pass left open, rebuilt after a scripting accident truncated the
/// original file. Three areas:
///
///  • REGISTRY FEEDS — the <see cref="StreamLiveRegistry"/> DI ctor wiring per
///    platform: Twitch/Kick stream-status events map IsLive straight through,
///    YouTube is live ONLY in the "Listening" chat state, and the registry
///    publishes <see cref="StreamLiveChangedEvent"/> on ACTUAL transitions only
///    (repeat pings with the same value, and a first-ever not-live report, stay
///    silent). Uses <see cref="Wave15DispatchingEventBus"/> so handler dispatch
///    is inline and observable instead of background-fanned.
///
///  • DISPATCH — <see cref="WorkflowTriggerService"/>'s StreamLiveChangedEvent
///    subscription: IsLive selects the "streamlive"/"streamoffline" key, aliases
///    fire end to end (reflection on NormalizeEventType plus an aliased
///    workflow executing for real), variables platform/username/isLive/eventType
///    are seeded, and the username is the platform's CHANNEL name with a
///    platform-name fallback. ROUTING LESSON (the wave-17b bug this file
///    exists to pin): a send_message from a KICK-context workflow goes to the
///    KICK sender — Kick-platform assertions must wait on the Kick fake's
///    SentMessages, Twitch-platform ones on the Twitch fake.
///
///  • TEST EVENTS — the <see cref="TestEventPublisher"/> ids
///    "streamlive.fire"/"streamoffline.fire" are registered test events that
///    publish StreamLiveChangedEvent (Twitch-shaped payload, IsLive per
///    direction) and drive the whole trigger pipeline end to end.
/// </summary>
public class Wave17bStreamLiveTriggerTests
{
    // ==================================================================
    // Alias mapping (NormalizeEventType via reflection — the same stable
    // private-static seam the earlier waves pinned)
    // ==================================================================

    private static readonly MethodInfo? NormalizeMethod = typeof(WorkflowTriggerService).GetMethod(
        "NormalizeEventType", BindingFlags.NonPublic | BindingFlags.Static);

    private static string Normalize(string? eventType)
    {
        Assert.NotNull(NormalizeMethod);
        return (string)NormalizeMethod!.Invoke(null, new object?[] { eventType })!;
    }

    [Theory]
    [InlineData("streamlive", "streamlive")]        // canonical live key passes through
    [InlineData("Stream Live", "streamlive")]       // human spelling
    [InlineData("stream_live", "streamlive")]       // snake_case
    [InlineData("STREAMLIVE", "streamlive")]        // shouting
    [InlineData("streamstart", "streamlive")]       // alias
    [InlineData("online", "streamlive")]            // alias
    [InlineData("golive", "streamlive")]            // alias
    [InlineData("Go-Live", "streamlive")]           // alias, kebab spelling
    [InlineData("streamoffline", "streamoffline")]  // canonical offline key
    [InlineData("Stream Offline", "streamoffline")] // human spelling
    [InlineData("streamend", "streamoffline")]      // alias
    [InlineData("offline", "streamoffline")]        // alias
    [InlineData("OFFLINE", "streamoffline")]        // alias, shouting
    public void StreamSpellings_AndAliases_NormalizeOntoDirectionKeys(string authored, string canonical)
    {
        Assert.Equal(canonical, Normalize(authored));
    }

    [Fact]
    public void StreamAliases_StaySeparate_FromNeighboringCanonicalKeys()
    {
        // The live-stream aliases must not collide with existing keys, and the
        // two directions must never fold into one another.
        Assert.NotEqual(Normalize("streamlive"), Normalize("streamoffline"));
        Assert.NotEqual(Normalize("host"), Normalize("online"));
        Assert.NotEqual(Normalize("follow"), Normalize("golive"));
        Assert.NotEqual(Normalize("raid"), Normalize("streamend"));

        // Neighboring aliases keep their own canonical targets.
        Assert.Equal("host", Normalize("hosted"));
        Assert.Equal("follow", Normalize("followed"));
        Assert.Equal("firstmessage", Normalize("firsttimechatter"));
    }

    // ==================================================================
    // Registry feeds: per-platform live semantics, transitions only
    // ==================================================================

    [Fact]
    public async Task TwitchStreamStatus_Feed_FiresTransitionsOnly_RepeatPingsStaySilent()
    {
        var bus = new Wave15DispatchingEventBus();
        var registry = new StreamLiveRegistry(bus);
        try
        {
            await bus.PublishAsync(new TwitchStreamStatusEvent { Source = "Twitch", IsLive = true });
            await bus.PublishAsync(new TwitchStreamStatusEvent { Source = "Twitch", IsLive = true }); // repeat ping
            await bus.PublishAsync(new TwitchStreamStatusEvent { Source = "Twitch", IsLive = true }); // repeat ping
            await bus.PublishAsync(new TwitchStreamStatusEvent { Source = "Twitch", IsLive = false });

            await WaitUntilAsync(
                () => Transitions(bus).Count == 2,
                TimeSpan.FromSeconds(10),
                "exactly two Twitch transition events (live + offline; repeat pings silent)",
                () => $"{Transitions(bus).Count} transitions, IsLive(Twitch)={registry.IsLive("Twitch")}");

            Assert.False(registry.IsLive("Twitch")); // final state is offline
            Assert.Equal("Twitch", Transitions(bus)[0].Platform);
            Assert.True(Transitions(bus)[0].IsLive);
            Assert.Equal("Twitch", Transitions(bus)[1].Platform);
            Assert.False(Transitions(bus)[1].IsLive);
        }
        finally
        {
            registry.UnsubscribeAll();
        }
    }

    [Fact]
    public async Task KickStreamStatus_Feed_LiveThenOffline_ExactlyTwoTransitionEvents()
    {
        var bus = new Wave15DispatchingEventBus();
        var registry = new StreamLiveRegistry(bus);
        try
        {
            await bus.PublishAsync(new KickStreamStatusEvent
            {
                Source = "Kick", IsLive = true, Title = "Wave 17b test live"
            });
            await bus.PublishAsync(new KickStreamStatusEvent
            {
                Source = "Kick", IsLive = false, Title = "Wave 17b test over"
            });
            await bus.PublishAsync(new KickStreamStatusEvent
            {
                Source = "Kick", IsLive = false, Title = "Wave 17b test over (repeat)"
            });

            await WaitUntilAsync(
                () => Transitions(bus).Count == 2,
                TimeSpan.FromSeconds(10),
                "exactly two Kick transition events (live + offline; offline repeat silent)",
                () => $"{Transitions(bus).Count} transitions, IsLive(Kick)={registry.IsLive("Kick")}");

            Assert.False(registry.IsLive("Kick"));
            Assert.All(Transitions(bus), e => Assert.Equal("Kick", e.Platform));
            Assert.True(Transitions(bus)[0].IsLive);
            Assert.False(Transitions(bus)[1].IsLive);
        }
        finally
        {
            registry.UnsubscribeAll();
        }
    }

    [Fact]
    public async Task YouTubeFeed_ListeningIsTheOnlyLiveState_NonListeningReportsStartSilent()
    {
        var bus = new Wave15DispatchingEventBus();
        var registry = new StreamLiveRegistry(bus);
        try
        {
            // Platforms start NOT live: a first-ever "Connecting" report is a
            // no-change and must publish nothing.
            await bus.PublishAsync(new YouTubeLiveChatStateEvent
            {
                Source = "YouTube", State = "Connecting", Message = "signing in"
            });
            Assert.False(registry.IsLive("YouTube"));
            Assert.Empty(Transitions(bus));

            // "Listening" is the only YouTube state polling an ACTIVE live chat.
            await bus.PublishAsync(new YouTubeLiveChatStateEvent
            {
                Source = "YouTube", State = "Listening", Message = "polling"
            });
            await bus.PublishAsync(new YouTubeLiveChatStateEvent
            {
                Source = "YouTube", State = "Listening", Message = "still polling"
            });
            await WaitUntilAsync(
                () => Transitions(bus).Count == 1,
                TimeSpan.FromSeconds(10),
                "one YouTube transition (Listening => live); the repeat Listening ping stays silent",
                () => $"{Transitions(bus).Count} transitions, IsLive(YouTube)={registry.IsLive("YouTube")}");
            Assert.True(registry.IsLive("YouTube"));

            // Every other state ("WaitingForBroadcast", "ReauthRequired") is NOT
            // live: the first one transitions offline, repeats stay silent.
            await bus.PublishAsync(new YouTubeLiveChatStateEvent
            {
                Source = "YouTube", State = "WaitingForBroadcast", Message = "waiting"
            });
            await bus.PublishAsync(new YouTubeLiveChatStateEvent
            {
                Source = "YouTube", State = "ReauthRequired", Message = "sign-in needed"
            });
            await WaitUntilAsync(
                () => Transitions(bus).Count == 2,
                TimeSpan.FromSeconds(10),
                "a second YouTube transition (non-Listening => offline); ReauthRequired repeat stays silent",
                () => $"{Transitions(bus).Count} transitions, IsLive(YouTube)={registry.IsLive("YouTube")}");

            Assert.False(registry.IsLive("YouTube"));
            Assert.All(Transitions(bus), e => Assert.Equal("YouTube", e.Platform));
            Assert.True(Transitions(bus)[0].IsLive);
            Assert.False(Transitions(bus)[1].IsLive);
        }
        finally
        {
            registry.UnsubscribeAll();
        }
    }

    // ==================================================================
    // Dispatch: routing, direction keys, aliases, seeded variables
    // ==================================================================

    [Fact]
    public async Task KickLiveTransition_SendsOnTheKickSender_TwitchStaysSilent()
    {
        // ROUTING LESSON: the workflow's context platform is "Kick", so the
        // send_message node routes to the KICK sender — waiting on the Twitch
        // fake for a Kick-platform event was the wave-17b bug.
        await using var h = new Wave17bTriggerHarness();
        h.Data.WorkflowRows.Add(EventWorkflowRow(
            "streamlive", "KICK LIVE: {username} on {platform} [{isLive}]"));
        await h.Triggers.StartAsync();

        await h.Bus.PublishAsync(new StreamLiveChangedEvent
        {
            Source = "Kick",
            Platform = "Kick",
            IsLive = true
        });

        // The Kick fake's Channel default ("vyralhq") seeds {username}; the
        // platform/isLive variables prove the trigger's seeding.
        var sent = await h.Kick.NextSend.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal("KICK LIVE: vyralhq on Kick [True]", sent);

        // Bounded settle: exactly one completed execution and NOTHING on the
        // Twitch sender (per-platform routing, no cross-bleed).
        await WaitUntilAsync(
            () => h.Data.WorkflowExecutions.Count == 1,
            TimeSpan.FromSeconds(10),
            "one workflow execution for the Kick go-live",
            () => $"executions={h.Data.WorkflowExecutions.Count}, kick sends={h.Kick.SentMessages.Count}, twitch sends={h.Twitch.SentMessages.Count}");
        Assert.Empty(h.Twitch.SentMessages);
        Assert.Single(h.Kick.SentMessages);
    }

    [Fact]
    public async Task KickOffline_ChannelNull_UsernameFallsBackToThePlatformName()
    {
        await using var h = new Wave17bTriggerHarness();
        h.Kick.Channel = null; // no connected channel name to seed from
        h.Data.WorkflowRows.Add(EventWorkflowRow(
            "streamoffline", "OFF: {username}/{platform}/{isLive}"));
        await h.Triggers.StartAsync();

        await h.Bus.PublishAsync(new StreamLiveChangedEvent
        {
            Source = "Kick",
            Platform = "Kick",
            IsLive = false
        });

        var sent = await h.Kick.NextSend.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal("OFF: Kick/Kick/False", sent); // {username} == platform-name fallback
    }

    [Fact]
    public async Task DirectionKey_SelectedByIsLive_AliasedWorkflowsFireEndToEnd()
    {
        // Authored ALIASES ("golive"/"streamend") must survive the whole
        // pipeline, and IsLive must pick the right direction's key — a
        // go-live transition can never fire the stream-end twin (they index
        // under different normalized keys).
        await using var h = new Wave17bTriggerHarness();
        h.Data.WorkflowRows.Add(EventWorkflowRow("golive", "live: {eventType}/{isLive}"));     // alias -> streamlive
        h.Data.WorkflowRows.Add(EventWorkflowRow("streamend", "done: {eventType}/{isLive}")); // alias -> streamoffline
        await h.Triggers.StartAsync();

        // Twitch-context transitions land on the TWITCH sender (routing lesson,
        // mirrored direction); the template reads the trigger-seeded
        // eventType/isLive variables.
        await h.Bus.PublishAsync(new StreamLiveChangedEvent
        {
            Source = "Twitch",
            Platform = "Twitch",
            IsLive = true
        });
        var first = await h.Twitch.NextSend.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal("live: streamlive/True", first);

        await h.Bus.PublishAsync(new StreamLiveChangedEvent
        {
            Source = "Twitch",
            Platform = "Twitch",
            IsLive = false
        });
        await WaitUntilAsync(
            () => h.Twitch.SentMessages.Count == 2,
            TimeSpan.FromSeconds(10),
            "the streamend-aliased workflow to fire after the offline transition",
            () => $"twitch sends={h.Twitch.SentMessages.Count}, executions={h.Data.WorkflowExecutions.Count}");
        Assert.Equal("done: streamoffline/False", h.Twitch.SentMessages[1]);

        // Only the two Twitch sends happened — the Kick sender never saw a
        // Twitch-context workflow, and each direction fired exactly once.
        Assert.Empty(h.Kick.SentMessages);
        Assert.Equal(2, h.Data.WorkflowExecutions.Count);
    }

    // ==================================================================
    // Test events: streamlive.fire / streamoffline.fire
    // ==================================================================

    [Fact]
    public void StreamFireIds_AreRegisteredTestEvents()
    {
        Assert.Equal("streamlive.fire", TestEventPublisher.StreamLive);
        Assert.Equal("streamoffline.fire", TestEventPublisher.StreamOffline);

        Assert.Contains(TestEventPublisher.StreamLive, TestEventPublisher.SupportedEventTypes);
        Assert.Contains(TestEventPublisher.StreamOffline, TestEventPublisher.SupportedEventTypes);

        // Registry invariant: ids stay unique as the catalog grows.
        Assert.Equal(
            TestEventPublisher.SupportedEventTypes.Count,
            TestEventPublisher.SupportedEventTypes.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task Publish_StreamLiveFire_MapsToStreamLiveChangedEvent_IsLiveTrue()
    {
        var bus = new Wave15DispatchingEventBus();
        var publisher = new TestEventPublisher(bus, NullLogger<TestEventPublisher>.Instance);

        await publisher.PublishTestEventAsync(TestEventPublisher.StreamLive);

        var fired = Assert.IsType<StreamLiveChangedEvent>(Assert.Single(bus.Published));
        Assert.Equal("Twitch", fired.Source);
        Assert.Equal("Twitch", fired.Platform);
        Assert.True(fired.IsLive);
    }

    [Fact]
    public async Task Publish_StreamOfflineFire_MapsToStreamLiveChangedEvent_IsLiveFalse()
    {
        var bus = new Wave15DispatchingEventBus();
        var publisher = new TestEventPublisher(bus, NullLogger<TestEventPublisher>.Instance);

        await publisher.PublishTestEventAsync(TestEventPublisher.StreamOffline);

        var fired = Assert.IsType<StreamLiveChangedEvent>(Assert.Single(bus.Published));
        Assert.Equal("Twitch", fired.Source);
        Assert.Equal("Twitch", fired.Platform);
        Assert.False(fired.IsLive);
    }

    [Fact]
    public async Task StreamLiveFire_EndToEnd_FiresStreamLiveWorkflow_ThroughTheRealBus()
    {
        // The Event Testing page path: test event -> real EventBus background
        // dispatch -> WorkflowTriggerService "streamlive" key -> runtime ->
        // send_message on the Twitch fake (Twitch-shaped payload).
        await using var h = new Wave17bTriggerHarness();
        h.Data.WorkflowRows.Add(EventWorkflowRow(
            "streamlive", "test: {eventType}/{isLive} on {platform}"));
        await h.Triggers.StartAsync();

        await h.Publisher.PublishTestEventAsync(TestEventPublisher.StreamLive);

        var sent = await h.Twitch.NextSend.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal("test: streamlive/True on Twitch", sent);
        Assert.Empty(h.Kick.SentMessages);
    }

    // ------------------------------------------------------------------
    // helpers
    // ------------------------------------------------------------------

    /// <summary>
    /// Real engine pieces (EventBus, WorkflowRuntime + built-in node catalog,
    /// WorkflowMessageSender, WorkflowTriggerService, TestEventPublisher) over
    /// the wave-8 fakes — the same harness shape as the wave-17 trigger tests.
    /// The KICK fake is a first-class member: Kick-platform transitions assert
    /// against ITS SentMessages gate (per-platform routing).
    /// </summary>
    private sealed class Wave17bTriggerHarness : IAsyncDisposable
    {
        public IntegrationWave8DataService Data { get; } = new();
        public IntegrationWave8TwitchService Twitch { get; } = new();
        public IntegrationWave8KickService Kick { get; } = new(); // Channel defaults "vyralhq"
        public EventBus Bus { get; } = new(NullLogger<EventBus>.Instance);
        public TestEventPublisher Publisher { get; }
        public ServiceProvider Provider { get; }
        public WorkflowTriggerService Triggers { get; }

        public Wave17bTriggerHarness()
        {
            var registry = new NodeRegistry();
            BuiltInNodes.RegisterAll(registry);
            var runtime = new WorkflowRuntime(registry);

            var services = new ServiceCollection();
            services.AddSingleton(runtime);
            services.AddSingleton<IDataService>(Data);
            Provider = services.BuildServiceProvider();

            var sender = new WorkflowMessageSender(
                NullLogger<WorkflowMessageSender>.Instance,
                Twitch,
                Kick,
                new IntegrationWave8DiscordBotManager(),
                new IntegrationWave8DiscordService());

            Triggers = new WorkflowTriggerService(
                NullLogger<WorkflowTriggerService>.Instance,
                Data,
                Bus,
                Provider,
                sender,
                Twitch,
                Kick);
            Publisher = new TestEventPublisher(Bus, NullLogger<TestEventPublisher>.Instance);
        }

        public async ValueTask DisposeAsync()
        {
            await Triggers.StopAsync();
            Provider.Dispose();
            Bus.Dispose();
        }
    }

    private static WorkflowEntity EventWorkflowRow(string eventType, string text)
    {
        var send = new WorkflowNode
        {
            Type = "send_message",
            Config = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["text"] = text
            }
        };
        var workflow = new Workflow
        {
            Name = "Wave17b " + eventType,
            Trigger = new WorkflowTrigger
            {
                Type = WorkflowTriggerTypes.Event,
                EventType = eventType
            },
            Nodes = { send },
            EntryNodeId = send.Id
        };
        return WorkflowJson.ToRow(workflow);
    }

    /// <summary>The StreamLiveChangedEvents a dispatching bus saw, in publish order.</summary>
    private static List<StreamLiveChangedEvent> Transitions(Wave15DispatchingEventBus bus) =>
        bus.Published.OfType<StreamLiveChangedEvent>().ToList();

    /// <summary>
    /// Polls until the condition holds (bounded async handoff — deadline +
    /// diagnostics in the failure, never a blind sleep).
    /// </summary>
    private static async Task WaitUntilAsync(
        Func<bool> condition,
        TimeSpan deadline,
        string what,
        Func<string>? diagnostics = null)
    {
        var deadlineAt = DateTime.UtcNow + deadline;
        while (!condition())
        {
            if (DateTime.UtcNow > deadlineAt)
            {
                throw new TimeoutException(
                    $"Wave-17b condition not met within {deadline.TotalSeconds}s: {what}"
                    + (diagnostics == null ? string.Empty : " — " + diagnostics()));
            }

            await Task.Delay(20);
        }
    }
}
