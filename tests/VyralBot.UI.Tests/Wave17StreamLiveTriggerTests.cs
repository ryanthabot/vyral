using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using VyralBot.Core.Services.EventBus;
using VyralBot.Core.Services.EventBus.Events;
using VyralBot.Data;
using VyralBot.Integrations.StreamStatus;
using VyralBot.UI.Services;
using VyralBot.Workflows.Models;
using VyralBot.Workflows.Nodes;
using VyralBot.Workflows.Persistence;
using VyralBot.Workflows.Runtime;
using Xunit;

namespace VyralBot.UI.Tests;

/// <summary>
/// WAVE-17: stream live/offline workflow triggers end to end.
///
///   platform stream-status events → <see cref="StreamLiveRegistry"/> →
///   <see cref="StreamLiveChangedEvent"/> on the EventBus →
///   <see cref="WorkflowTriggerService"/> dispatch key
///   "streamlive" (IsLive=true) / "streamoffline" (IsLive=false) →
///   <see cref="WorkflowRuntime"/> send_message.
///
/// Covers all three seams:
///  • ALIAS MAPPING — <c>WorkflowTriggerService.NormalizeEventType</c> folds
///    authored spellings onto the direction keys (reflection on the private
///    static normalizer, the same stable seam as the Wave-7
///    membership/superchat tests).
///  • REGISTRY PUBLISHING — the registry broadcasts the transition event on
///    the bus exactly once per actual transition.
///  • DISPATCH — a faked StreamLiveChangedEvent published on a real EventBus
///    fires the workflow authored with the canonical key OR an alias, seeds
///    username from the platform's CHANNEL name (platform fallback) and the
///    isLive/eventType/platform variables, and never fires the opposite
///    direction's workflow.
/// </summary>
public class Wave17StreamLiveTriggerTests
{
    // ==================================================================
    // Alias mapping (NormalizeEventType via reflection — see class doc)
    // ==================================================================

    private static readonly MethodInfo? NormalizeMethod = typeof(WorkflowTriggerService).GetMethod(
        "NormalizeEventType", BindingFlags.NonPublic | BindingFlags.Static);

    private static string Normalize(string? eventType)
    {
        Assert.NotNull(NormalizeMethod);
        return (string)NormalizeMethod!.Invoke(null, new object?[] { eventType })!;
    }

    [Theory]
    [InlineData("streamlive", "streamlive")]        // canonical live key
    [InlineData("Stream Live", "streamlive")]       // human spelling
    [InlineData("stream-live", "streamlive")]       // kebab-case
    [InlineData("stream_live", "streamlive")]       // snake_case
    [InlineData("STREAMLIVE", "streamlive")]        // shouting
    [InlineData("streamstart", "streamlive")]       // alias
    [InlineData("online", "streamlive")]            // alias
    [InlineData("golive", "streamlive")]            // alias
    [InlineData("Go Live", "streamlive")]           // alias, human spelling
    [InlineData("streamoffline", "streamoffline")]  // canonical offline key
    [InlineData("Stream Offline", "streamoffline")] // human spelling
    [InlineData("stream-offline", "streamoffline")] // kebab-case
    [InlineData("streamend", "streamoffline")]      // alias
    [InlineData("offline", "streamoffline")]        // alias
    public void StreamSpellings_NormalizeOntoDirectionKeys(string authored, string canonical)
    {
        Assert.Equal(canonical, Normalize(authored));
    }

    [Fact]
    public void StreamKeys_StaySeparate_FromExistingEventKeys()
    {
        // "online" must NOT collide with an existing key ("host" etc.), and
        // the two directions must not fold into one another.
        Assert.NotEqual(Normalize("streamlive"), Normalize("streamoffline"));
        Assert.NotEqual(Normalize("host"), Normalize("online"));
        Assert.NotEqual(Normalize("follow"), Normalize("golive"));

        // Neighboring aliases still normalize onto their canonical keys.
        Assert.Equal("host", Normalize("hosted"));
        Assert.Equal("firstmessage", Normalize("firsttimechatter"));
    }

    // ==================================================================
    // Registry publishing (transitions only, once each)
    // ==================================================================

    [Fact]
    public async Task Registry_PublishesStreamLiveChangedEvent_OnTransitionsOnly()
    {
        var bus = new EventBus(NullLogger<EventBus>.Instance);
        try
        {
            var registry = new StreamLiveRegistry(bus);
            var received = new List<StreamLiveChangedEvent>();
            await bus.SubscribeAsync<StreamLiveChangedEvent>(e =>
            {
                lock (received) received.Add(e);
                return Task.CompletedTask;
            });

            registry.SetLive("Twitch", true);   // transition → live
            registry.SetLive("Twitch", true);   // repeat ping — NOT a transition
            registry.SetLive("Twitch", false);  // transition → offline
            registry.SetLive("Kick", false);    // unseen platform starts not-live — no event

            await WaitUntilAsync(
                () => { lock (received) return received.Count == 2; },
                TimeSpan.FromSeconds(10),
                "exactly two transition events (live + offline)");

            Assert.Equal("Twitch", received[0].Platform);
            Assert.True(received[0].IsLive);
            Assert.Equal("Twitch", received[1].Platform);
            Assert.False(received[1].IsLive);
        }
        finally
        {
            bus.Dispose();
        }
    }

    // ==================================================================
    // Dispatch: fake StreamLiveChangedEvent fires the right key
    // ==================================================================

    [Fact]
    public async Task StreamLiveEvent_FiresStreamLiveWorkflow_WithChannelAsUsername()
    {
        await using var h = new Wave17TriggerHarness();
        h.Data.WorkflowRows.Add(EventWorkflowRow(
            "streamlive", "{username} went LIVE on {platform} ({eventType}/{isLive})"));
        await h.Triggers.StartAsync();

        await h.Bus.PublishAsync(new StreamLiveChangedEvent
        {
            Source = "Twitch",
            Platform = "Twitch",
            IsLive = true
        });

        // Username is the Twitch CHANNEL name (the fake is connected to
        // "vyralhq"); platform/eventType/isLive prove the seeded variables.
        var sent = await h.Twitch.NextSend.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal("vyralhq went LIVE on Twitch (streamlive/True)", sent);
    }

    [Fact]
    public async Task OfflineEvent_FiresStreamEndAliasedWorkflow_NotTheLiveOne()
    {
        await using var h = new Wave17TriggerHarness();
        h.Data.WorkflowRows.Add(EventWorkflowRow("streamend", "wrapping up: {eventType}")); // alias → streamoffline
        h.Data.WorkflowRows.Add(EventWorkflowRow("streamlive", "we are live: {eventType}"));
        await h.Triggers.StartAsync();

        await h.Bus.PublishAsync(new StreamLiveChangedEvent
        {
            Source = "Twitch",
            Platform = "Twitch",
            IsLive = false
        });

        var sent = await h.Twitch.NextSend.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal("wrapping up: streamoffline", sent);

        // Bounded negative window: the live-direction twin must stay silent —
        // an offline transition must never fire a "streamlive" workflow.
        await Task.Delay(300);
        Assert.Single(h.Twitch.SentMessages);
        Assert.Single(h.Data.WorkflowExecutions);
    }

    // ------------------------------------------------------------------
    // helpers
    // ------------------------------------------------------------------

    /// <summary>
    /// Real engine pieces (EventBus, WorkflowRuntime + built-in node catalog,
    /// WorkflowMessageSender, WorkflowTriggerService) over the wave-8 fakes —
    /// the same harness shape as <c>IntegrationWave8CommandPipelineTests</c>,
    /// reused because the wave-17 seams are identical (only the trigger
    /// source differs: an EventBus event instead of the chat hook).
    /// </summary>
    private sealed class Wave17TriggerHarness : IAsyncDisposable
    {
        public IntegrationWave8DataService Data { get; } = new();
        public IntegrationWave8TwitchService Twitch { get; } = new();
        public IntegrationWave8KickService Kick { get; } = new();
        public EventBus Bus { get; } = new(NullLogger<EventBus>.Instance);
        public ServiceProvider Provider { get; }
        public WorkflowTriggerService Triggers { get; }

        public Wave17TriggerHarness()
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
            Name = "Wave17 " + eventType,
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

    /// <summary>Polls until the boolean condition holds (bounded async handoff).</summary>
    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan deadline, string what)
    {
        var deadlineAt = DateTime.UtcNow + deadline;
        while (!condition())
        {
            if (DateTime.UtcNow > deadlineAt)
            {
                throw new TimeoutException(
                    $"Wave-17 condition not met within {deadline.TotalSeconds}s: {what}");
            }

            await Task.Delay(20);
        }
    }
}
