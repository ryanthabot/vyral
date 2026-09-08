using VyralBot.Core.Services.EventBus;
using VyralBot.Core.Services.StreamEffects;
using VyralBot.UI.Services;
using Xunit;

namespace VyralBot.UI.Tests;

/// <summary>
/// Stream-effect command router rules:
///
///   1. RECOGNITION: a message is a stream-effect command when its first token
///      (after trimming whitespace and an optional !/. prefix) is one of
///      flashlights, lightson, lightsoff, streamdeck — anything else (including
///      empty, whitespace, a bare "!" or other "!"-commands like !kick) returns
///      null so the message falls through to the other command handlers.
///   2. TIER GATING: the caller's effective tier (IModTierResolver) must satisfy
///      ModTiers for the verb (flashlights = Mod+, lightson/lightsoff =
///      SeniorMod+, streamdeck = Admin+). A denial replies with "requires
///      &lt;tier&gt; tier or higher", publishes NOTHING and is not audited.
///   3. EXECUTION: an allowed run publishes exactly one StreamEffectEvent
///      (EffectType + JSON payload + RequestedBy + ChannelId) and persists an
///      audit row with the Action "streamfx:&lt;verb&gt;" through
///      IDataService.SaveModActionAsync, mirroring ModCommandEngine's insert.
/// </summary>
public class StreamEffectCommandRouterTests
{
    private const string BotId = "bot-1";
    private const string ChannelId = "555666777";

    // ------------------------------------------------------------------
    // Rule 1+3: recognized command from an allowed tier
    // ------------------------------------------------------------------

    [Fact]
    public async Task Flashlights_By_Mod_Publishes_Event_Replies_And_Audits()
    {
        var t = MakeRouter();
        t.Roles.Store["222222"] = "Mod";

        var reply = await t.Router.TryHandleAsync(Ctx("!flashlights #00FFAA 7", senderId: "222222", username: "lamps"));

        Assert.NotNull(reply);
        Assert.Contains("✨", reply);

        var evt = Assert.Single(t.Bus.Published.OfType<StreamEffectEvent>());
        Assert.Equal("LightFlash", evt.EffectType);
        Assert.Contains("#00FFAA", evt.Payload);
        Assert.Contains("7", evt.Payload);
        Assert.Equal("lamps", evt.RequestedBy);
        Assert.Equal(ChannelId, evt.ChannelId);

        var audit = Assert.Single(t.Data.ModActions);
        Assert.Equal("streamfx:flashlights", audit.Action);
        Assert.Equal("lamps", audit.ModUsername);
        Assert.Equal("222222", audit.ModUserId);
    }

    // ------------------------------------------------------------------
    // Rule 2: tier gating
    // ------------------------------------------------------------------

    [Fact]
    public async Task Flashlights_By_TrialMod_Is_Denied_Without_Publishing()
    {
        var t = MakeRouter();
        t.Roles.Store["111111"] = "TrialMod";

        var reply = await t.Router.TryHandleAsync(Ctx("flashlights", senderId: "111111", username: "rookie"));

        Assert.NotNull(reply);
        Assert.Contains("requires Mod tier", reply);
        Assert.Empty(t.Bus.Published);
        Assert.Empty(t.Data.ModActions);
    }

    [Fact]
    public async Task StreamDeck_By_Admin_Publishes_The_Action_Payload()
    {
        var t = MakeRouter();
        t.Roles.Store["999000"] = "Admin";

        var reply = await t.Router.TryHandleAsync(Ctx("!streamdeck hype"));

        Assert.NotNull(reply);
        Assert.Contains("✨", reply);

        var evt = Assert.Single(t.Bus.Published.OfType<StreamEffectEvent>());
        Assert.Equal("StreamDeckAction", evt.EffectType);
        Assert.Contains("hype", evt.Payload);
        Assert.Equal("streamfx:streamdeck", Assert.Single(t.Data.ModActions).Action);
    }

    [Fact]
    public async Task LightsOff_By_SeniorMod_Publishes_The_Off_Scene()
    {
        var t = MakeRouter();
        t.Roles.Store["333333"] = "SeniorMod";

        await t.Router.TryHandleAsync(Ctx("lightsoff", senderId: "333333"));

        var evt = Assert.Single(t.Bus.Published.OfType<StreamEffectEvent>());
        Assert.Equal("LightScene", evt.EffectType);
        Assert.Contains("\"state\":\"off\"", evt.Payload);
        Assert.Equal("streamfx:lightsoff", Assert.Single(t.Data.ModActions).Action);
    }

    // ------------------------------------------------------------------
    // Rule 1: fall-through cases
    // ------------------------------------------------------------------

    [Fact]
    public async Task Non_StreamFx_Command_Falls_Through()
    {
        var t = MakeRouter();
        t.Roles.Store["999000"] = "Admin";

        var reply = await t.Router.TryHandleAsync(Ctx("!kick someone"));

        Assert.Null(reply);
        Assert.Empty(t.Bus.Published);
        Assert.Empty(t.Data.ModActions);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("!")]
    [InlineData(".")]
    public async Task Blank_Or_PrefixOnly_Messages_Fall_Through(string message)
    {
        var t = MakeRouter();
        t.Roles.Store["999000"] = "Admin";

        Assert.Null(await t.Router.TryHandleAsync(Ctx(message)));

        Assert.Empty(t.Bus.Published);
        Assert.Empty(t.Data.ModActions);
    }

    // ------------------------------------------------------------------
    // helpers
    // ------------------------------------------------------------------

    private static StreamEffectCommandContext Ctx(
        string message,
        string senderId = "999000",
        string username = "boss")
        // GuildId stays null: the test resolver (no Discord bot wired) answers
        // with the stored assignment alone, exactly like production DMs do.
        => new(BotId, GuildId: null, ChannelId, senderId, username, message);

    private sealed class Harness
    {
        public FakeDataService Data { get; } = new();
        public FakeModRoleService Roles { get; } = new();
        public RecordingEventBus Bus { get; } = new();
        public StreamEffectCommandRouter Router { get; }

        public Harness()
        {
            Router = new StreamEffectCommandRouter(
                new ModTierResolver(Roles, bot: null), Bus, Data);
        }
    }

    private static Harness MakeRouter() => new();

    /// <summary>
    /// Records every published event synchronously (the real EventBus fans out
    /// through a background channel, which would make "was it published?"
    /// assertions racy).
    /// </summary>
    private sealed class RecordingEventBus : IEventBus
    {
        public List<object> Published { get; } = new();

        public Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
            where TEvent : IEvent
        {
            Published.Add(@event!);
            return Task.CompletedTask;
        }

        public Task<Guid> SubscribeAsync<TEvent>(Func<TEvent, Task> handler, CancellationToken cancellationToken = default)
            where TEvent : IEvent
            => Task.FromResult(Guid.NewGuid());

        public void Unsubscribe(Guid subscriptionId) { }

        public int SubscriptionCount => 0;
    }
}
