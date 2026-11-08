using Microsoft.Extensions.Logging.Abstractions;
using System.Reflection;
using VyralBot.Core.Models;
using VyralBot.Core.Services.EventBus;
using VyralBot.Data.Entities;
using VyralBot.UI.Services;
using Xunit;

namespace VyralBot.UI.Tests;

/// <summary>
/// WAVE-8 INTEGRATION: CommandTimerService's streamer.bot-style chat-activity
/// gate (MinimumMessages) end to end — real EventBus delivery of
/// <see cref="ChatMessageReceivedEvent"/> into the per-platform counters, the
/// gated fire, the Twitch send and the LastRunAt stamp.
///
/// SEAM NOTE (why reflection): CommandTimerService ticks on an INTERNAL
/// hardcoded 30s <c>System.Threading.Timer</c>; <c>TickAsync</c> is private
/// and there is no injectable clock or public tick. Rather than sleeping 30+
/// seconds per tick, the test drives the REAL private TickAsync via reflection
/// — the production code path — while the 30s timer never gets a chance to
/// fire (its due time exceeds the test duration and StopAsync disposes it).
/// The EventBus handoff (publish → background dispatcher → subscription
/// handler) is awaited deterministically by polling the service's counter
/// table, bounded and short — an async handoff wait, not a timing assumption.
/// If TickAsync is ever renamed, these tests fail loudly and should be
/// replaced by a public tick seam, not by sleeps.
/// </summary>
public class IntegrationWave8TimerGatingTests
{
    private static readonly MethodInfo TickMethod =
        typeof(CommandTimerService).GetMethod("TickAsync", BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("CommandTimerService.TickAsync seam not found — replace these tests with a public tick seam");

    [Fact]
    public async Task MinimumMessages_Two_OneMessage_NoFire_TwoMessages_Fire()
    {
        var h = new TimerHarness(minimumMessages: 2);
        await h.Service.StartAsync();
        try
        {
            // 1st chat message arrives → gate still closed on the first tick.
            await h.PublishChatAsync("viewer_one", "hello");
            await TickAsync(h.Service);
            Assert.Empty(h.Twitch.SentMessages);
            Assert.Null(h.Timer.LastRunAt); // gate does NOT consume the slot

            // 2nd chat message → gate open → timer fires, sends the command's
            // response to Twitch and stamps LastRunAt (and zeroes the counter,
            // re-arming the chat gate).
            await h.PublishChatAsync("viewer_two", "world");
            await TickAsync(h.Service);
            Assert.Equal("Follow us everywhere!", Assert.Single(h.Twitch.SentMessages));
            Assert.NotNull(h.Timer.LastRunAt);

            // Re-arm the chat gate with two fresh messages, then tick: the
            // gate is open but the INTERVAL guard (LastRunAt just stamped,
            // IntervalMinutes not elapsed) still holds — no second send.
            await h.PublishChatAsync("viewer_three", "chat");
            await h.PublishChatAsync("viewer_four", "chat chat");
            await TickAsync(h.Service);
            Assert.Single(h.Twitch.SentMessages);
        }
        finally
        {
            await h.Service.StopAsync();
        }
    }

    [Fact]
    public async Task Chat_Counter_Is_PerPlatform_Kick_Chatter_Never_Arms_A_Twitch_Timer()
    {
        var h = new TimerHarness(minimumMessages: 2);
        await h.Service.StartAsync();
        try
        {
            // Plenty of KICK activity — the Twitch-targeted timer stays gated.
            await h.PublishChatAsync("kick_viewer_1", "hey", platform: "Kick");
            await h.PublishChatAsync("kick_viewer_2", "ho", platform: "Kick");
            await TickAsync(h.Service);
            Assert.Empty(h.Twitch.SentMessages);
            Assert.Null(h.Timer.LastRunAt);

            // One Twitch message is still below the threshold of two.
            await h.PublishChatAsync("twitch_viewer_1", "hi");
            await TickAsync(h.Service);
            Assert.Empty(h.Twitch.SentMessages);
            Assert.Null(h.Timer.LastRunAt);

            // Reaching two TWITCH messages opens the gate; the send goes to
            // the timer's target platform (Twitch), never Kick.
            await h.PublishChatAsync("twitch_viewer_2", "hi again");
            await TickAsync(h.Service);
            Assert.Equal("Follow us everywhere!", Assert.Single(h.Twitch.SentMessages));
            Assert.Empty(h.Kick.SentMessages);
        }
        finally
        {
            await h.Service.StopAsync();
        }
    }

    [Fact]
    public async Task Start_Subscribes_To_Chat_Stop_Unsubscribes_And_Clears_Counters()
    {
        var h = new TimerHarness(minimumMessages: 1);
        var baseline = h.Bus.SubscriptionCount;

        await h.Service.StartAsync();
        Assert.True(h.Service.IsRunning);
        Assert.Equal(baseline + 1, h.Bus.SubscriptionCount); // exactly one chat subscription

        // Arm the gate with one message, then stop: the counters and the
        // subscription are dropped, so a post-stop message re-subscribes to
        // nothing and a (reflection-driven) tick stays quiet even though the
        // gate had been armed before the stop.
        await h.PublishChatAsync("viewer_one", "hello");
        await h.Service.StopAsync();

        Assert.False(h.Service.IsRunning);
        Assert.Equal(baseline, h.Bus.SubscriptionCount);

        // Post-stop chat reaches no subscriber (nothing to wait for — publish
        // directly, do NOT use the counting helper) and a driven tick is a
        // no-op because the scheduler is stopped.
        await h.Bus.PublishAsync(new ChatMessageReceivedEvent
        {
            Source = "Wave8Test",
            Platform = "Twitch",
            Channel = "vyralhq",
            Username = "viewer_two",
            Message = "ghost message"
        });
        await TickAsync(h.Service); // _tickTimer == null → immediate return
        Assert.Empty(h.Twitch.SentMessages);
        Assert.Null(h.Timer.LastRunAt);
    }

    // ------------------------------------------------------------------
    // helpers
    // ------------------------------------------------------------------

    private sealed class TimerHarness
    {
        public IntegrationWave8DataService Data { get; } = new();
        public IntegrationWave8TwitchService Twitch { get; } = new();
        public IntegrationWave8KickService Kick { get; } = new();
        public EventBus Bus { get; } = new(NullLogger<EventBus>.Instance);
        public CommandTimerService Service { get; }
        public CommandTimer Timer { get; }

        public TimerHarness(int minimumMessages)
        {
            Data.CommandsByName["!socials"] = new Command
            {
                Id = "cmd-socials",
                Name = "!socials",
                Response = "Follow us everywhere!",
                Permission = "Everyone"
            };
            Timer = new CommandTimer
            {
                Id = "timer-socials",
                Name = "Socials",
                CommandName = "!socials",
                IntervalMinutes = 30,
                MinimumMessages = minimumMessages,
                TargetPlatform = "Twitch",
                IsEnabled = true,
                LastRunAt = null
            };
            Data.CommandTimers[Timer.Id] = Timer;

            Service = new CommandTimerService(
                NullLogger<CommandTimerService>.Instance,
                Data,
                Twitch,
                Kick,
                new IntegrationWave8DiscordBotManager(),
                new IntegrationWave8YouTubeService(),
                new IntegrationWave8VariableReplacement(),
                Bus);
        }

        /// <summary>
        /// Publishes a chat message through the REAL EventBus and waits until
        /// the timer service's subscription has counted it (bounded handoff
        /// wait — the bus dispatches on a background task).
        /// </summary>
        public async Task PublishChatAsync(string username, string message, string platform = "Twitch")
        {
            await Bus.PublishAsync(new ChatMessageReceivedEvent
            {
                Source = "Wave8Test",
                Platform = platform,
                Channel = "vyralhq",
                Username = username,
                Message = message
            });

            var counters = MessageCounters(Service);
            var target = (counters.TryGetValue(platform, out var current) ? current : 0) + 1;
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            while (GetCount(counters, platform) < target)
            {
                if (DateTime.UtcNow > deadline)
                {
                    var observed = counters.Count == 0
                        ? "(no counters at all)"
                        : string.Join(", ", counters.Select(kv => $"{kv.Key}={kv.Value}"));
                    throw new TimeoutException(
                        $"CommandTimerService did not count the {platform} chat message within 5s "
                        + $"(expected {platform} >= {target}; observed counters: {observed}) — "
                        + "the EventBus background dispatch never reached the subscription handler");
                }

                await Task.Delay(20);
            }
        }
    }

    /// <summary>Drives the service's private tick (see the class doc for why).</summary>
    private static Task TickAsync(CommandTimerService service)
        => (Task)(TickMethod.Invoke(service, null) ?? throw new InvalidOperationException("TickAsync returned null"));

    private static Dictionary<string, int> MessageCounters(CommandTimerService service)
        => (Dictionary<string, int>)(typeof(CommandTimerService)
               .GetField("_messagesSinceFire", BindingFlags.NonPublic | BindingFlags.Instance)
               ?.GetValue(service)
           ?? throw new InvalidOperationException("_messagesSinceFire field not found"));

    private static int GetCount(Dictionary<string, int> counters, string platform)
        => counters.TryGetValue(platform, out var count) ? count : 0;
}
