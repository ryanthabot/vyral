using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using VyralBot.Core.Services.EventBus;
using VyralBot.Data;
using VyralBot.UI.Services;
using VyralBot.Workflows.Models;
using VyralBot.Workflows.Nodes;
using VyralBot.Workflows.Persistence;
using VyralBot.Workflows.Runtime;
using Xunit;

namespace VyralBot.UI.Tests;

/// <summary>
/// WAVE-8 INTEGRATION: the chat → workflow command pipeline end to end, in
/// process and without any UI window:
///
///   ChatMessageReceived(!cmd, platform) → ChatViewModel's pipeline hook
///   (<see cref="WorkflowTriggerService.TryHandleCommand"/>) → command-index
///   match → platform filters (Trigger.Platforms) → options/permission gates →
///   <see cref="WorkflowRuntime"/> (real node registry + send_message node) →
///   <see cref="WorkflowMessageSender"/> → ITwitchService/IKickService.
///
/// The workflow is authored as a <see cref="Workflow"/> object, persisted via
/// <see cref="WorkflowJson.ToRow"/> into the fake Workflows table and loaded
/// back by <see cref="WorkflowTriggerService.StartAsync"/> — so the trigger's
/// Platforms filter is proven across the real JSON persistence layer, not just
/// on an in-memory object.
///
/// ENTRY-POINT NOTE: command triggers enter through TryHandleCommand (the hook
/// ChatViewModel calls for every chat message), NOT through an EventBus
/// subscription — WorkflowTriggerService's SubscribeToEventsAsync covers
/// follow/sub/raid-style events only. The chat hook is therefore the highest
/// seam available without a dispatcher.
/// </summary>
public class IntegrationWave8CommandPipelineTests
{
    private const string TwitchOnlyCommand = "wave8twitch";
    private const string AnyPlatformCommand = "wave8any";

    [Fact]
    public async Task Twitch_Command_Workflow_Fires_Sends_And_Records_History()
    {
        await using var h = new PipelineHarness();
        h.Data.WorkflowRows.Add(CommandWorkflowRow(
            TwitchOnlyCommand,
            "Hey {username}, welcome to {channel} on {platform}!",
            platforms: new List<string> { "Twitch" }));
        await h.Triggers.StartAsync();

        // The Twitch message is claimed by the workflow pipeline...
        var claimed = h.Triggers.TryHandleCommand("Twitch", "viewer_rita", "!" + TwitchOnlyCommand, "vyralhq", null);
        Assert.True(claimed);

        // ...the send_message node ran through the real runtime + message
        // sender, with {username}/{channel}/{platform} substituted.
        var sent = await h.Twitch.NextSend.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal("Hey viewer_rita, welcome to vyralhq on Twitch!", sent);
        Assert.Single(h.Twitch.SentMessages);
        Assert.Empty(h.Kick.SentMessages);

        // The runtime persisted a Completed execution-history row through the
        // IDataService reachable from the execution context.
        var row = await WaitForAsync(
            () => h.Data.WorkflowExecutions.FirstOrDefault(e => e.Status == "Completed"),
            TimeSpan.FromSeconds(10));
        Assert.Equal(h.Data.WorkflowRows[0].Id, row.WorkflowId);
        Assert.Equal("Wave8 " + TwitchOnlyCommand, row.WorkflowName);
        Assert.Contains("send_message", row.NodePath);
    }

    [Fact]
    public async Task Kick_Message_Is_Filtered_By_TwitchOnly_Platforms_List()
    {
        await using var h = new PipelineHarness();
        h.Data.WorkflowRows.Add(CommandWorkflowRow(
            TwitchOnlyCommand, "twitch-only reply", platforms: new List<string> { "Twitch" }));
        h.Data.WorkflowRows.Add(CommandWorkflowRow(
            AnyPlatformCommand, "any-platform reply", platforms: null));
        await h.Triggers.StartAsync();

        // The Twitch-only workflow does NOT claim the Kick message (platform
        // filter E2E) and nothing is executed on either platform.
        var claimedTwitchOnly = h.Triggers.TryHandleCommand("Kick", "viewer_rita", "!" + TwitchOnlyCommand, "vyralhq", null);
        Assert.False(claimedTwitchOnly);

        // An unknown command name is equally not claimed (fall-through to the
        // classic command pipeline).
        Assert.False(h.Triggers.TryHandleCommand("Kick", "viewer_rita", "!wave8unknown", "vyralhq", null));

        // Negative control: the platform-ANY twin command (Platforms unset =
        // all platforms) DOES fire from Kick and routes to the Kick sender.
        var claimedAny = h.Triggers.TryHandleCommand("Kick", "viewer_rita", "!" + AnyPlatformCommand, "vyralhq", null);
        Assert.True(claimedAny);
        var sent = await h.Kick.NextSend.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal("any-platform reply", sent);

        // The filtered workflow never executed: exactly one completed history
        // row (the any-platform twin), no Twitch send ever happened.
        await WaitUntilAsync(() => h.Data.WorkflowExecutions.Count == 1, TimeSpan.FromSeconds(10));
        Assert.Single(h.Data.WorkflowExecutions);
        Assert.Empty(h.Twitch.SentMessages);
    }

    [Fact]
    public async Task WorkflowJson_RoundTrip_Preserves_Platforms_Filter_For_Command_Triggers()
    {
        // The persistence slice of the pipeline: what the flow editor saves
        // (Workflow → row) is what the trigger service loads (row → Workflow).
        var row = CommandWorkflowRow(TwitchOnlyCommand, "hi", new List<string> { "Twitch", "  " });

        var reloaded = WorkflowJson.FromRow(row);

        Assert.Equal(WorkflowTriggerTypes.Command, reloaded.Trigger.Type);
        Assert.Equal(TwitchOnlyCommand, reloaded.Trigger.CommandName);
        Assert.NotNull(reloaded.Trigger.Platforms);
        Assert.Equal(new[] { "Twitch", "  " }, reloaded.Trigger.Platforms);
        Assert.Equal("send_message", Assert.Single(reloaded.Nodes).Type);
        Assert.Equal("hi", Assert.Single(reloaded.Nodes).Config["text"]);
    }

    // ------------------------------------------------------------------
    // helpers
    // ------------------------------------------------------------------

    /// <summary>
    /// Builds the full pipeline with real engine pieces (EventBus,
    /// WorkflowRuntime + built-in node registry, WorkflowMessageSender,
    /// WorkflowTriggerService) over the wave-8 fakes. StartAsync is NOT
    /// called by the constructor — tests seed WorkflowRows first.
    /// </summary>
    private sealed class PipelineHarness : IAsyncDisposable
    {
        public IntegrationWave8DataService Data { get; } = new();
        public IntegrationWave8TwitchService Twitch { get; } = new();
        public IntegrationWave8KickService Kick { get; } = new();
        public EventBus Bus { get; } = new(NullLogger<EventBus>.Instance);
        public ServiceProvider Provider { get; }
        public WorkflowTriggerService Triggers { get; }

        public PipelineHarness()
        {
            // Real engine: full built-in node catalog + runtime, resolvable
            // from the service provider the way WorkflowTriggerService expects
            // (SendMessageNode looks the runtime up via ctx.Services).
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

    private static WorkflowEntity CommandWorkflowRow(string commandName, string text, List<string>? platforms)
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
            Name = "Wave8 " + commandName,
            Trigger = new WorkflowTrigger
            {
                Type = WorkflowTriggerTypes.Command,
                CommandName = commandName,
                CommandPrefix = "!",
                Platforms = platforms
            },
            Nodes = { send },
            EntryNodeId = send.Id
        };
        return WorkflowJson.ToRow(workflow);
    }

    /// <summary>Polls until the condition yields a non-null value (bounded async handoff — no test-logic sleeps).</summary>
    private static async Task<T> WaitForAsync<T>(Func<T?> condition, TimeSpan deadline) where T : class
    {
        var deadlineAt = DateTime.UtcNow + deadline;
        while (true)
        {
            var value = condition();
            if (value != null)
            {
                return value;
            }

            if (DateTime.UtcNow > deadlineAt)
            {
                throw new TimeoutException($"Wave-8 pipeline condition not met within {deadline.TotalSeconds}s");
            }

            await Task.Delay(20);
        }
    }

    /// <summary>Polls until the boolean condition holds (bounded async handoff).</summary>
    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan deadline)
    {
        var deadlineAt = DateTime.UtcNow + deadline;
        while (!condition())
        {
            if (DateTime.UtcNow > deadlineAt)
            {
                throw new TimeoutException($"Wave-8 pipeline condition not met within {deadline.TotalSeconds}s");
            }

            await Task.Delay(20);
        }
    }
}
