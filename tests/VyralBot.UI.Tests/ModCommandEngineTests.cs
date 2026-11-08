using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using VyralBot.Core.Services;
using VyralBot.Core.Services.Currency;
using VyralBot.Core.Services.EventBus;
using VyralBot.Core.Services.Linking;
using VyralBot.Core.Services.Moderation;
using VyralBot.Core.Services.StreamEffects;
using VyralBot.Data;
using VyralBot.Data.Entities;
using VyralBot.UI.Services;
using Xunit;
// The real Integrations event, referenced the way App.xaml.cs will wire it
// (the test project reaches VyralBot.Integrations transitively via VyralBot.UI).
using DiscordChatMessageEvent = VyralBot.Integrations.Discord.Events.DiscordChatMessageEvent;

namespace VyralBot.UI.Tests;

/// <summary>
/// W5B mod command engine rules:
///
///   1. GATING: the engine only reacts inside the configured mod channel
///      (ModChannelIdProvider). A null provider, a null/empty configured id or
///      a message from any other channel is silently ignored — and so is any
///      text that is not a "!mod &lt;action&gt; [args...]" command.
///   2. TIER RESOLUTION: the caller's tier comes from ModRoleService keyed by
///      the Discord sender id. No assignment → "You are not a moderator."
///      A known assignment is checked against ModTiers: an under-ranked action
///      answers "'&lt;action&gt;' requires &lt;tier&gt; or higher." Neither reply is
///      audited and nothing executes.
///   3. EXECUTION: currency actions go through ICurrencyService (platform
///      "Discord"), giveaway/poll actions through the real GiveawayService /
///      PollService, kick/ban/mute/unban/timeout/unmute/purge/slowmode are
///      delegated to the underlying Discord command handler through the
///      ModerationInvoker seam ("timeout" is translated to the underlying
///      "mute" command), and assignmod persists via ModRoleService.
///   4. AUDIT: every EXECUTED action (including a service-level failure) is
///      persisted with SaveModActionAsync. Denials, usage errors and
///      "nothing to act on" results are never audited.
///   5. REPLIES: every reply goes back into the mod channel through the bot
///      resolved by BotResolver (keyed by the event's BotId).
///   6. LIFECYCLE: StartAsync subscribes through the DiscordChatSubscriber seam
///      exactly once and stores the subscription Guid; StopAsync unsubscribes
///      via the IEventBus.
///
/// Because VyralBot.Core cannot reference VyralBot.Integrations (the reference
/// direction is Integrations → Core), the engine consumes the Discord event
/// and the per-bot Discord service through the Core-side mirrors
/// <see cref="ModChannelMessage"/> and <see cref="IModDiscordBot"/>; the
/// lifecycle test below wires the subscriber with the REAL
/// VyralBot.Integrations.Discord.Events.DiscordChatMessageEvent exactly like
/// the App.xaml.cs composition will.
/// </summary>
public class ModCommandEngineTests
{
    private const string ModChannelId = "111222333";
    private const string BotId = "bot-1";
    private const string AdminSender = "999000";
    private const string AdminName = "boss";

    // ------------------------------------------------------------------
    // Rule 1: mod-channel gating
    // ------------------------------------------------------------------

    [Fact]
    public async Task Ignores_Messages_When_No_ModChannel_Configured()
    {
        var t = MakeEngine(modChannel: () => null);

        await t.Engine.HandleMessageAsync(Msg("!mod kick @spammer"));

        Assert.Empty(t.Bot.SentMessages);
        Assert.Empty(t.Data.ModActions);
    }

    [Fact]
    public async Task Ignores_Messages_When_ModChannel_Id_Is_Empty()
    {
        var t = MakeEngine(modChannel: () => "  ");

        await t.Engine.HandleMessageAsync(Msg("!mod kick @spammer"));

        Assert.Empty(t.Bot.SentMessages);
    }

    [Fact]
    public async Task Ignores_Messages_From_Other_Channels()
    {
        var t = MakeEngine();
        PromoteAdmin(t);

        await t.Engine.HandleMessageAsync(Msg("!mod kick @spammer", channel: "444555666"));

        Assert.Empty(t.Bot.SentMessages);
        Assert.Empty(t.Data.ModActions);
    }

    [Fact]
    public async Task Ignores_Messages_That_Are_Not_ModCommands()
    {
        var t = MakeEngine();
        PromoteAdmin(t);

        await t.Engine.HandleMessageAsync(Msg("!kick @spammer"));
        await t.Engine.HandleMessageAsync(Msg("hello everyone"));
        await t.Engine.HandleMessageAsync(Msg("!moderator something")); // near-match, not "!mod"

        Assert.Empty(t.Bot.SentMessages);
        Assert.Empty(t.Data.ModActions);
    }

    // ------------------------------------------------------------------
    // Rule 2: caller tier resolution and denials
    // ------------------------------------------------------------------

    [Fact]
    public async Task Caller_Without_Assignment_Is_Not_A_Moderator()
    {
        var t = MakeEngine();

        await t.Engine.HandleMessageAsync(Msg("!mod kick @spammer", senderId: "nobody"));

        var reply = Assert.Single(t.Bot.SentMessages);
        Assert.Equal(ModChannelId, reply.ChannelId);
        Assert.Contains("You are not a moderator", reply.Content);
        Assert.Empty(t.Data.ModActions);
    }

    [Fact]
    public async Task Bare_Mod_Command_Answers_Usage()
    {
        var t = MakeEngine();
        PromoteAdmin(t);

        await t.Engine.HandleMessageAsync(Msg("!mod"));

        var reply = Assert.Single(t.Bot.SentMessages);
        Assert.Contains("Usage", reply.Content);
        Assert.Empty(t.Data.ModActions);
    }

    [Fact]
    public async Task Unknown_Action_Is_Rejected()
    {
        var t = MakeEngine();
        PromoteAdmin(t);

        await t.Engine.HandleMessageAsync(Msg("!mod nuke everything"));

        var reply = Assert.Single(t.Bot.SentMessages);
        Assert.Contains("Unknown mod action 'nuke'", reply.Content);
        Assert.Empty(t.Data.ModActions);
    }

    [Fact]
    public async Task TrialMod_Cannot_Run_Mod_Commands()
    {
        var t = MakeEngine();
        t.Roles.Store[TrialSender] = "TrialMod";

        await t.Engine.HandleMessageAsync(Msg("!mod addcurrency @bob 50", senderId: TrialSender));

        var reply = Assert.Single(t.Bot.SentMessages);
        Assert.Contains("requires Mod or higher", reply.Content);
        Assert.Empty(t.Currency.Adds);
        Assert.Empty(t.Data.ModActions);
    }

    [Fact]
    public async Task SeniorMod_Cannot_Run_Admin_Commands()
    {
        var t = MakeEngine();
        t.Roles.Store[SeniorSender] = "SeniorMod";

        await t.Engine.HandleMessageAsync(Msg("!mod setbalance @bob 1", senderId: SeniorSender));

        Assert.Contains("requires Admin or higher", Assert.Single(t.Bot.SentMessages).Content);
        Assert.Empty(t.Data.ModActions);
    }

    // ------------------------------------------------------------------
    // Rule 3: currency actions (ICurrencyService, platform "Discord")
    // ------------------------------------------------------------------

    [Fact]
    public async Task AddCurrency_Executes_For_SeniorMod_And_Audits()
    {
        var t = MakeEngine();
        t.Roles.Store[SeniorSender] = "SeniorMod";
        t.Currency.Balances["bob|Discord"] = 200;

        await t.Engine.HandleMessageAsync(Msg("!mod addcurrency @bob 50", senderId: SeniorSender, username: "sen"));

        var add = Assert.Single(t.Currency.Adds);
        Assert.Equal("bob", add.Username);
        Assert.Equal("Discord", add.Platform);
        Assert.Equal(50, add.Amount);
        Assert.Equal("Admin", add.TransactionType);

        var reply = Assert.Single(t.Bot.SentMessages);
        Assert.Contains("✅", reply.Content);
        Assert.Contains("250", reply.Content);

        var audit = Assert.Single(t.Data.ModActions);
        Assert.Equal("addcurrency", audit.Action);
        Assert.Equal("sen", audit.ModUsername);
        Assert.Equal(SeniorSender, audit.ModUserId);
        Assert.Equal("bob", audit.TargetUsername);
    }

    [Fact]
    public async Task RemoveCurrency_Executes_For_Mod()
    {
        var t = MakeEngine();
        t.Roles.Store[ModSender] = "Mod";
        t.Currency.Balances["bob|Discord"] = 200;

        await t.Engine.HandleMessageAsync(Msg("!mod removecurrency @bob 30", senderId: ModSender));

        var remove = Assert.Single(t.Currency.Removes);
        Assert.Equal(("bob", "Discord", 30, "Admin"), (remove.Username, remove.Platform, remove.Amount, remove.TransactionType));
        Assert.Contains("170", Assert.Single(t.Bot.SentMessages).Content);
        Assert.Equal("removecurrency", Assert.Single(t.Data.ModActions).Action);
    }

    [Fact]
    public async Task RemoveCurrency_With_Insufficient_Balance_Fails_But_Audits()
    {
        var t = MakeEngine();
        t.Roles.Store[ModSender] = "Mod";
        t.Currency.Balances["bob|Discord"] = 200;

        await t.Engine.HandleMessageAsync(Msg("!mod removecurrency @bob 500", senderId: ModSender));

        Assert.Contains("❌", Assert.Single(t.Bot.SentMessages).Content);
        Assert.Equal(200, t.Currency.Balances["bob|Discord"]);
        Assert.Equal("removecurrency", Assert.Single(t.Data.ModActions).Action);
    }

    [Fact]
    public async Task AddCurrency_With_Missing_Args_Answers_Usage_And_Does_Not_Audit()
    {
        var t = MakeEngine();
        PromoteAdmin(t);

        await t.Engine.HandleMessageAsync(Msg("!mod addcurrency bob"));

        Assert.Contains("Usage", Assert.Single(t.Bot.SentMessages).Content);
        Assert.Empty(t.Currency.Adds);
        Assert.Empty(t.Data.ModActions);
    }

    [Fact]
    public async Task SetBalance_Adjusts_By_Delta_For_Admin()
    {
        var t = MakeEngine();
        PromoteAdmin(t);
        t.Currency.Balances["bob|Discord"] = 100;

        await t.Engine.HandleMessageAsync(Msg("!mod setbalance @bob 250"));

        var add = Assert.Single(t.Currency.Adds);
        Assert.Equal(150, add.Amount); // 100 -> 250 means +150
        Assert.Contains("250", Assert.Single(t.Bot.SentMessages).Content);
        Assert.Equal("setbalance", Assert.Single(t.Data.ModActions).Action);
    }

    [Fact]
    public async Task SetBalance_Can_Lower_Balance()
    {
        var t = MakeEngine();
        PromoteAdmin(t);
        t.Currency.Balances["bob|Discord"] = 100;

        await t.Engine.HandleMessageAsync(Msg("!mod setbalance @bob 40"));

        Assert.Equal(60, Assert.Single(t.Currency.Removes).Amount);
        Assert.Equal(40, t.Currency.Balances["bob|Discord"]);
    }

    // ------------------------------------------------------------------
    // Rule 3: giveaway actions (real GiveawayService over the fake data store)
    // ------------------------------------------------------------------

    [Fact]
    public async Task StartGiveaway_Creates_And_Starts()
    {
        var t = MakeEngine();
        t.Roles.Store[SeniorSender] = "SeniorMod";

        await t.Engine.HandleMessageAsync(Msg("!mod startgiveaway skins 2 wewin", senderId: SeniorSender));

        var active = await t.Giveaways.GetActiveGiveawayAsync();
        Assert.NotNull(active);
        Assert.Equal("skins", active!.Name);
        Assert.Equal(2, active.WinnerCount);
        Assert.Equal("wewin", active.EntryKeyword);

        Assert.Contains("✅", Assert.Single(t.Bot.SentMessages).Content);
        Assert.Equal("startgiveaway", Assert.Single(t.Data.ModActions).Action);
    }

    [Fact]
    public async Task StartGiveaway_With_Missing_Args_Answers_Usage()
    {
        var t = MakeEngine();
        t.Roles.Store[SeniorSender] = "SeniorMod";

        await t.Engine.HandleMessageAsync(Msg("!mod startgiveaway onlyname", senderId: SeniorSender));

        Assert.Contains("Usage", Assert.Single(t.Bot.SentMessages).Content);
        Assert.Empty(t.Data.Giveaways);
        Assert.Empty(t.Data.ModActions);
    }

    [Fact]
    public async Task CancelGiveaway_Cancels_The_Active_Giveaway()
    {
        var t = MakeEngine();
        t.Roles.Store[SeniorSender] = "SeniorMod";
        await StartActiveGiveawayAsync(t.Giveaways);

        await t.Engine.HandleMessageAsync(Msg("!mod cancelgiveaway", senderId: SeniorSender));

        Assert.Null(await t.Giveaways.GetActiveGiveawayAsync());
        Assert.Contains("✅", Assert.Single(t.Bot.SentMessages).Content);
        Assert.Equal("cancelgiveaway", Assert.Single(t.Data.ModActions).Action);
    }

    [Fact]
    public async Task CancelGiveaway_Without_Active_Giveaway_Fails_Without_Audit()
    {
        var t = MakeEngine();
        t.Roles.Store[SeniorSender] = "SeniorMod";

        await t.Engine.HandleMessageAsync(Msg("!mod cancelgiveaway", senderId: SeniorSender));

        Assert.Contains("No active giveaway", Assert.Single(t.Bot.SentMessages).Content);
        Assert.Empty(t.Data.ModActions);
    }

    [Fact]
    public async Task Draw_Without_Active_Giveaway_Fails_Without_Audit()
    {
        var t = MakeEngine();
        t.Roles.Store[SeniorSender] = "SeniorMod";

        await t.Engine.HandleMessageAsync(Msg("!mod draw", senderId: SeniorSender));

        Assert.Contains("No active giveaway", Assert.Single(t.Bot.SentMessages).Content);
        Assert.Empty(t.Data.ModActions);
    }

    // ------------------------------------------------------------------
    // Rule 3: poll actions (real PollService over the fake data store)
    // ------------------------------------------------------------------

    [Fact]
    public async Task StartPoll_Creates_And_Starts()
    {
        var t = MakeEngine();
        t.Roles.Store[SeniorSender] = "SeniorMod";

        await t.Engine.HandleMessageAsync(Msg("!mod startpoll Best game? | Minecraft | Fortnite", senderId: SeniorSender));

        var active = await t.Polls.GetActivePollAsync();
        Assert.NotNull(active);
        Assert.Equal("Best game?", active!.Question);

        Assert.Contains("✅", Assert.Single(t.Bot.SentMessages).Content);
        Assert.Equal("startpoll", Assert.Single(t.Data.ModActions).Action);
    }

    [Fact]
    public async Task StartPoll_With_Fewer_Than_Two_Options_Answers_Usage()
    {
        var t = MakeEngine();
        t.Roles.Store[SeniorSender] = "SeniorMod";

        await t.Engine.HandleMessageAsync(Msg("!mod startpoll Best game? | Minecraft", senderId: SeniorSender));

        Assert.Contains("Usage", Assert.Single(t.Bot.SentMessages).Content);
        Assert.Empty(t.Data.Polls);
        Assert.Empty(t.Data.ModActions);
    }

    [Fact]
    public async Task EndPoll_Ends_The_Active_Poll()
    {
        var t = MakeEngine();
        t.Roles.Store[SeniorSender] = "SeniorMod";
        await StartActivePollAsync(t.Polls);

        await t.Engine.HandleMessageAsync(Msg("!mod endpoll", senderId: SeniorSender));

        Assert.Null(await t.Polls.GetActivePollAsync());
        Assert.Contains("✅", Assert.Single(t.Bot.SentMessages).Content);
        Assert.Equal("endpoll", Assert.Single(t.Data.ModActions).Action);
    }

    [Fact]
    public async Task EndPoll_Without_Active_Poll_Fails_Without_Audit()
    {
        var t = MakeEngine();
        t.Roles.Store[SeniorSender] = "SeniorMod";

        await t.Engine.HandleMessageAsync(Msg("!mod endpoll", senderId: SeniorSender));

        Assert.Contains("❌", Assert.Single(t.Bot.SentMessages).Content);
        Assert.Empty(t.Data.ModActions);
    }

    // ------------------------------------------------------------------
    // Rule 3: delegation to the underlying Discord moderation handler
    // ------------------------------------------------------------------

    [Fact]
    public async Task Kick_Delegates_To_The_Moderation_Invoker()
    {
        var t = MakeEngine();
        t.Roles.Store[TrialSender] = "TrialMod";
        var invoked = new List<(string BotId, string CommandLine, string? ChannelId)>();
        t.Engine.ModerationInvoker = (botId, commandLine, channelId) =>
        {
            invoked.Add((botId, commandLine, channelId));
            return Task.FromResult<string?>("Kicked @spammer from Server");
        };

        await t.Engine.HandleMessageAsync(Msg("!mod kick @spammer griefing", senderId: TrialSender));

        var call = Assert.Single(invoked);
        Assert.Equal(BotId, call.BotId);
        Assert.Equal("!kick @spammer griefing", call.CommandLine);
        Assert.Equal(ModChannelId, call.ChannelId);

        // The underlying handler's response is relayed into the mod channel.
        Assert.Contains("Kicked @spammer", Assert.Single(t.Bot.SentMessages).Content);
        var audit = Assert.Single(t.Data.ModActions);
        Assert.Equal("kick", audit.Action);
        Assert.Equal("spammer", audit.TargetUsername);
    }

    [Fact]
    public async Task Timeout_Is_Translated_To_The_Underlying_Mute_Command()
    {
        var t = MakeEngine();
        t.Roles.Store[TrialSender] = "TrialMod";
        var invoked = new List<string>();
        t.Engine.ModerationInvoker = (_, commandLine, _) =>
        {
            invoked.Add(commandLine);
            return Task.FromResult<string?>("Muted @loud for 10 minutes");
        };

        await t.Engine.HandleMessageAsync(Msg("!mod timeout @loud 10", senderId: TrialSender));

        Assert.Equal("!mute @loud 10", Assert.Single(invoked));
    }

    [Fact]
    public async Task Delegated_Command_With_Null_Response_Reports_Failure()
    {
        var t = MakeEngine();
        t.Roles.Store[TrialSender] = "TrialMod";
        t.Engine.ModerationInvoker = (_, _, _) => Task.FromResult<string?>(null);

        await t.Engine.HandleMessageAsync(Msg("!mod kick @spammer", senderId: TrialSender));

        Assert.Contains("❌", Assert.Single(t.Bot.SentMessages).Content);
        Assert.Equal("kick", Assert.Single(t.Data.ModActions).Action);
    }

    [Fact]
    public async Task Delegated_Command_Without_Invoker_Reports_Not_Wired()
    {
        var t = MakeEngine();
        t.Roles.Store[TrialSender] = "TrialMod";

        await t.Engine.HandleMessageAsync(Msg("!mod kick @spammer", senderId: TrialSender));

        Assert.Contains("not wired", Assert.Single(t.Bot.SentMessages).Content);
        Assert.Empty(t.Data.ModActions);
    }

    // ------------------------------------------------------------------
    // Rule 3: assignmod (Admin only)
    // ------------------------------------------------------------------

    [Fact]
    public async Task AssignMod_By_Admin_Assigns_The_Tier()
    {
        var t = MakeEngine();
        PromoteAdmin(t);

        await t.Engine.HandleMessageAsync(Msg("!mod assignmod @newguy Mod"));

        Assert.Equal("Mod", t.Roles.Store["newguy"]);
        Assert.Contains("✅", Assert.Single(t.Bot.SentMessages).Content);
        var audit = Assert.Single(t.Data.ModActions);
        Assert.Equal("assignmod", audit.Action);
        Assert.Equal("newguy", audit.TargetUsername);
    }

    [Fact]
    public async Task AssignMod_With_Unknown_Tier_Fails()
    {
        var t = MakeEngine();
        PromoteAdmin(t);

        await t.Engine.HandleMessageAsync(Msg("!mod assignmod @newguy Owner"));

        Assert.Contains("Unknown tier", Assert.Single(t.Bot.SentMessages).Content);
        Assert.Empty(t.Roles.AssignCalls);
        Assert.Empty(t.Data.ModActions);
    }

    [Fact]
    public async Task AssignMod_With_Missing_Tier_Answers_Usage()
    {
        var t = MakeEngine();
        PromoteAdmin(t);

        await t.Engine.HandleMessageAsync(Msg("!mod assignmod @newguy"));

        Assert.Contains("Usage", Assert.Single(t.Bot.SentMessages).Content);
        Assert.Empty(t.Roles.AssignCalls);
    }

    // ------------------------------------------------------------------
    // Rule 3: stream-effect verbs (publish StreamEffectEvent + audit + reply)
    // ------------------------------------------------------------------

    [Fact]
    public async Task Flashlights_Mod_Command_Publishes_Stream_Effect_And_Audits()
    {
        var bus = new RecordingEventBus();
        var t = MakeEngine(bus: bus);
        t.Roles.Store[ModSender] = "Mod";

        await t.Engine.HandleMessageAsync(Msg("!mod flashlights #00FFAA 3", senderId: ModSender, username: "lamps"));

        var evt = Assert.Single(bus.Published.OfType<StreamEffectEvent>());
        Assert.Equal("LightFlash", evt.EffectType);
        Assert.Contains("#00FFAA", evt.Payload);
        Assert.Contains("3", evt.Payload);
        Assert.Equal("Discord", evt.Source);
        Assert.Equal("lamps", evt.RequestedBy);
        Assert.Equal(ModChannelId, evt.ChannelId);

        var reply = Assert.Single(t.Bot.SentMessages);
        Assert.Contains("✨", reply.Content);
        var audit = Assert.Single(t.Data.ModActions);
        Assert.Equal("flashlights", audit.Action);
        Assert.Equal("lamps", audit.ModUsername);
        Assert.Equal(ModSender, audit.ModUserId);
    }

    // ------------------------------------------------------------------
    // Rule 5: replies route through the resolved bot into the mod channel
    // ------------------------------------------------------------------

    [Fact]
    public async Task Replies_Use_The_Bot_Resolved_By_The_Event_BotId()
    {
        var t = MakeEngine();
        PromoteAdmin(t);
        var resolvedBotIds = new List<string>();
        var routedBot = new FakeDiscordBot();
        t.Engine.BotResolver = botId =>
        {
            resolvedBotIds.Add(botId);
            return routedBot;
        };

        await t.Engine.HandleMessageAsync(Msg("!mod kick @spammer"));

        Assert.Equal(BotId, Assert.Single(resolvedBotIds));
        // The default harness bot saw nothing — the routed bot did.
        Assert.Empty(t.Bot.SentMessages);
        Assert.Equal(ModChannelId, Assert.Single(routedBot.SentMessages).ChannelId);
    }

    // ------------------------------------------------------------------
    // Rule 6: lifecycle (subscribe once, unsubscribe on stop)
    // ------------------------------------------------------------------

    [Fact]
    public async Task StartAsync_Subscribes_And_StopAsync_Unsubscribes()
    {
        var t = MakeEngine();
        t.Engine.DiscordChatSubscriber = handler => t.Bus.SubscribeAsync<DiscordChatMessageEvent>(
            e => handler(new ModChannelMessage(e.ChannelId, e.SenderUserId, e.Username, e.Message, e.BotId)));

        await t.Engine.StartAsync();
        Assert.Equal(1, t.Bus.SubscriptionCount);

        await t.Engine.StopAsync();
        Assert.Equal(0, t.Bus.SubscriptionCount);
    }

    [Fact]
    public async Task StartAsync_Twice_Subscribes_Once()
    {
        var t = MakeEngine();
        t.Engine.DiscordChatSubscriber = handler => t.Bus.SubscribeAsync<DiscordChatMessageEvent>(
            e => handler(new ModChannelMessage(e.ChannelId, e.SenderUserId, e.Username, e.Message, e.BotId)));

        await t.Engine.StartAsync();
        await t.Engine.StartAsync();

        Assert.Equal(1, t.Bus.SubscriptionCount);
    }

    [Fact]
    public async Task StartAsync_Without_Subscriber_Is_A_Safe_NoOp()
    {
        var t = MakeEngine();

        await t.Engine.StartAsync();
        await t.Engine.StopAsync();
    }

    [Fact]
    public async Task StopAsync_Without_Start_Is_A_Safe_NoOp()
    {
        var t = MakeEngine();

        await t.Engine.StopAsync();
    }

    // ------------------------------------------------------------------
    // helpers
    // ------------------------------------------------------------------

    private const string TrialSender = "111111";
    private const string ModSender = "222222";
    private const string SeniorSender = "333333";

    private sealed class Harness
    {
        public Harness(Func<string?>? modChannel = null, IEventBus? bus = null)
        {
            Data = new FakeDataService();
            Currency = new FakeCurrencyService();
            Roles = new FakeModRoleService();
            Bot = new FakeDiscordBot();
            Bus = bus ?? new EventBus(NullLogger<EventBus>.Instance);
            Giveaways = new GiveawayService(
                Data, Bus, new FakeLinkedAccounts(), new FakeViewerStatus(),
                NullLogger<GiveawayService>.Instance);
            Polls = new PollService(Data, NullLogger<PollService>.Instance);
            Engine = new ModCommandEngine(
                Roles, new ModTierResolver(Roles, Bot), Data, Currency, Giveaways, Polls, Bus,
                NullLogger<ModCommandEngine>.Instance)
            {
                ModChannelIdProvider = modChannel ?? (() => ModChannelId),
                BotResolver = _ => Bot,
            };
        }

        public FakeDataService Data { get; }
        public FakeCurrencyService Currency { get; }
        public FakeModRoleService Roles { get; }
        public FakeDiscordBot Bot { get; }
        public IEventBus Bus { get; }
        public GiveawayService Giveaways { get; }
        public PollService Polls { get; }
        public ModCommandEngine Engine { get; }
    }

    private static Harness MakeEngine(Func<string?>? modChannel = null, IEventBus? bus = null)
        => new(modChannel, bus);

    private static void PromoteAdmin(Harness t) => t.Roles.Store[AdminSender] = "Admin";

    private static ModChannelMessage Msg(
        string message,
        string senderId = AdminSender,
        string username = AdminName,
        string? channel = ModChannelId)
        => new(channel, senderId, username, message, BotId);

    private static async Task<Giveaway> StartActiveGiveawayAsync(GiveawayService giveaways)
    {
        var giveaway = await giveaways.CreateGiveawayAsync("test", "desc", "enter", 1);
        Assert.True(await giveaways.StartGiveawayAsync(giveaway.Id));
        return giveaway;
    }

    private static async Task<Poll> StartActivePollAsync(PollService polls)
    {
        var poll = await polls.CreatePollAsync("Q?", new[] { "A", "B" }, "creator");
        Assert.True(await polls.StartPollAsync(poll.Id));
        return poll;
    }
}

// ======================================================================
// Shared test doubles (also used by ModRoleServiceTests in ModTierTests.cs)
// ======================================================================

/// <summary>
/// In-memory IDataService: real behavior ONLY for the mod, giveaway and poll
/// tables the moderation services touch; every other member throws (a test
/// should never depend on it).
/// </summary>
public class FakeDataService : IDataService
{
    public List<ModAction> ModActions { get; } = new();
    public Dictionary<string, ModRoleAssignment> ModRoleAssignments { get; } = new(StringComparer.Ordinal);
    public Dictionary<Guid, Giveaway> Giveaways { get; } = new();
    public Dictionary<Guid, Poll> Polls { get; } = new();

    private const string NotImplemented = "Not needed for mod engine tests.";

    // ----- Mod system (real behavior) -----

    public Task SaveModActionAsync(ModAction action)
    {
        ModActions.Add(action);
        return Task.CompletedTask;
    }

    public Task<IEnumerable<ModAction>> GetModActionsAsync(int limit = 200)
        => Task.FromResult<IEnumerable<ModAction>>(ModActions.Take(limit).ToList());

    public Task<IEnumerable<ModAction>> GetModActionsForViewerAsync(string username, string userId, int limit = 100)
        => Task.FromResult<IEnumerable<ModAction>>(ModActions.Take(limit).ToList());

    public Task SaveModRoleAssignmentAsync(ModRoleAssignment assignment)
    {
        ModRoleAssignments[assignment.DiscordUserId] = assignment;
        return Task.CompletedTask;
    }

    public Task DeleteModRoleAssignmentAsync(string discordUserId)
    {
        ModRoleAssignments.Remove(discordUserId);
        return Task.CompletedTask;
    }

    public Task<ModRoleAssignment?> GetModRoleAssignmentAsync(string discordUserId)
        => Task.FromResult(ModRoleAssignments.TryGetValue(discordUserId, out var a) ? a : null);

    public Task<IEnumerable<ModRoleAssignment>> GetModRoleAssignmentsAsync()
        => Task.FromResult<IEnumerable<ModRoleAssignment>>(ModRoleAssignments.Values.ToList());

    // ----- Connected channels / role tiers / OBS scene collections (not needed here) -----

    public Task<List<ConnectedChannel>> GetConnectedChannelsAsync(string botId) => throw new NotImplementedException(NotImplemented);
    public Task<ConnectedChannel> UpsertConnectedChannelAsync(ConnectedChannel channel) => throw new NotImplementedException(NotImplemented);
    public Task DeleteConnectedChannelAsync(string botId, string channelId) => throw new NotImplementedException(NotImplemented);
    public Task<int> CountConnectedChannelsAsync(string botId) => throw new NotImplementedException(NotImplemented);
    public Task<List<DiscordRoleTierAssignment>> GetRoleTierAssignmentsAsync(string botId) => throw new NotImplementedException(NotImplemented);
    public Task UpsertRoleTierAssignmentAsync(DiscordRoleTierAssignment assignment) => throw new NotImplementedException(NotImplemented);
    public Task DeleteRoleTierAssignmentAsync(string botId, string discordRoleId) => throw new NotImplementedException(NotImplemented);
    public Task<List<ObsSceneCollection>> GetObsSceneCollectionsAsync() => throw new NotImplementedException(NotImplemented);
    public Task<ObsSceneCollection?> GetObsSceneCollectionAsync(int id) => throw new NotImplementedException(NotImplemented);
    public Task<ObsSceneCollection> SaveObsSceneCollectionAsync(ObsSceneCollection collection) => throw new NotImplementedException(NotImplemented);
    public Task DeleteObsSceneCollectionAsync(int id) => throw new NotImplementedException(NotImplemented);

    // ----- Giveaways / polls (real behavior; real GiveawayService/PollService run on top) -----

    public Task<Giveaway?> GetGiveawayAsync(Guid id)
        => Task.FromResult(Giveaways.TryGetValue(id, out var g) ? g : null);

    public Task<Giveaway?> GetActiveGiveawayAsync()
        => Task.FromResult(Giveaways.Values.FirstOrDefault(g => g.Status == GiveawayStatus.Active));

    public Task SaveGiveawayAsync(Giveaway giveaway)
    {
        Giveaways[giveaway.Id] = giveaway;
        return Task.CompletedTask;
    }

    public Task<Poll?> GetPollAsync(Guid id)
        => Task.FromResult(Polls.TryGetValue(id, out var p) ? p : null);

    public Task<Poll?> GetActivePollAsync()
        => Task.FromResult(Polls.Values.FirstOrDefault(p => p.Status == PollStatus.Active));

    public Task SavePollAsync(Poll poll)
    {
        Polls[poll.Id] = poll;
        return Task.CompletedTask;
    }

    public Task<Dictionary<int, int>> GetPollVoteResultsAsync(Guid pollId)
        => Task.FromResult(new Dictionary<int, int>());

    // ----- Everything else (out of scope) -----

    public Task<IEnumerable<Platform>> GetPlatformsAsync() => throw new NotImplementedException(NotImplemented);
    public Task<Platform?> GetPlatformByNameAsync(string name) => throw new NotImplementedException(NotImplemented);
    public Task<Platform?> GetPlatformByIdAsync(Guid id) => throw new NotImplementedException(NotImplemented);
    public Task SavePlatformAsync(Platform platform) => throw new NotImplementedException(NotImplemented);
    public Task DeletePlatformAsync(Guid id) => throw new NotImplementedException(NotImplemented);
    public Task<IEnumerable<DiscordBot>> GetDiscordBotsAsync() => throw new NotImplementedException(NotImplemented);
    public Task SaveDiscordBotAsync(DiscordBot bot) => throw new NotImplementedException(NotImplemented);
    public Task DeleteDiscordBotAsync(string id) => throw new NotImplementedException(NotImplemented);
    public Task<string?> GetAssignedChannelIdsAsync(string botId) => throw new NotImplementedException(NotImplemented);
    public Task SetAssignedChannelIdsAsync(string botId, string channelIds) => throw new NotImplementedException(NotImplemented);
    public Task<IEnumerable<WorkflowEntity>> GetWorkflowsAsync() => throw new NotImplementedException(NotImplemented);
    public Task SaveWorkflowAsync(WorkflowEntity workflow) => throw new NotImplementedException(NotImplemented);
    public Task DeleteWorkflowAsync(string id) => throw new NotImplementedException(NotImplemented);
    public Task<IEnumerable<WorkflowExecution>> GetWorkflowExecutionsAsync(string? workflowId = null, int limit = 50) => throw new NotImplementedException(NotImplemented);
    public Task SaveWorkflowExecutionAsync(WorkflowExecution execution) => throw new NotImplementedException(NotImplemented);
    public Task<string?> GetConfigValueAsync(string key) => throw new NotImplementedException(NotImplemented);
    public Task SetConfigValueAsync(string key, string value, string? category = null) => throw new NotImplementedException(NotImplemented);
    public Task<bool> DeleteConfigValueAsync(string key, string? category = null) => throw new NotImplementedException(NotImplemented);
    public Task<IEnumerable<Config>> GetConfigsByCategoryAsync(string category) => throw new NotImplementedException(NotImplemented);
    public Task<IEnumerable<Trigger>> GetTriggersAsync(bool? enabledOnly = null) => throw new NotImplementedException(NotImplemented);
    public Task<Trigger?> GetTriggerByIdAsync(Guid id) => throw new NotImplementedException(NotImplemented);
    public Task SaveTriggerAsync(Trigger trigger) => throw new NotImplementedException(NotImplemented);
    public Task DeleteTriggerAsync(Guid id) => throw new NotImplementedException(NotImplemented);
    public Task<IEnumerable<BotAction>> GetActionsAsync() => throw new NotImplementedException(NotImplemented);
    public Task<BotAction?> GetActionByIdAsync(Guid id) => throw new NotImplementedException(NotImplemented);
    public Task SaveActionAsync(BotAction action) => throw new NotImplementedException(NotImplemented);
    public Task DeleteActionAsync(Guid id) => throw new NotImplementedException(NotImplemented);
    public Task<IEnumerable<EventLog>> GetEventLogsAsync(int limit = 100) => throw new NotImplementedException(NotImplemented);
    public Task LogEventAsync(EventLog log) => throw new NotImplementedException(NotImplemented);
    public Task<WebServerSettings> GetWebServerSettingsAsync() => throw new NotImplementedException(NotImplemented);
    public Task SaveWebServerSettingsAsync(WebServerSettings settings) => throw new NotImplementedException(NotImplemented);
    public Task<IEnumerable<WebhookSubscription>> GetWebhookSubscriptionsAsync(string? platform = null) => throw new NotImplementedException(NotImplemented);
    public Task SaveWebhookSubscriptionAsync(WebhookSubscription subscription) => throw new NotImplementedException(NotImplemented);
    public Task DeleteWebhookSubscriptionAsync(string id) => throw new NotImplementedException(NotImplemented);
    public Task DeleteWebhookSubscriptionsByPlatformAsync(string platform) => throw new NotImplementedException(NotImplemented);
    public Task EnsureDatabaseCreatedAsync() => throw new NotImplementedException(NotImplemented);
    public Task<IReadOnlyDictionary<string, int>> GetDataInventoryAsync() => throw new NotImplementedException(NotImplemented);
    public Task DeleteAllChatMessagesAsync() => throw new NotImplementedException(NotImplemented);
    public Task DeleteEventLogsAsync() => throw new NotImplementedException(NotImplemented);
    public Task DeleteWorkflowExecutionsAsync(string? workflowId = null, DateTime? olderThan = null) => throw new NotImplementedException(NotImplemented);
    public Task<bool> CompactDatabaseAsync() => throw new NotImplementedException(NotImplemented);
    public Task<bool> SaveChatMessageAsync(ChatMessage message) => throw new NotImplementedException(NotImplemented);
    public Task<IEnumerable<ChatMessage>> GetChatMessagesAsync(string platform, string channel, int limit = 200) => throw new NotImplementedException(NotImplemented);
    public Task DeleteChatMessagesAsync(string platform, string channel) => throw new NotImplementedException(NotImplemented);
    public Task<ChatEventSetting?> GetChatEventSettingAsync(string platform, string eventType) => throw new NotImplementedException(NotImplemented);
    public Task<IEnumerable<ChatEventSetting>> GetAllChatEventSettingsAsync(string? platform = null) => throw new NotImplementedException(NotImplemented);
    public Task SaveChatEventSettingAsync(ChatEventSetting setting) => throw new NotImplementedException(NotImplemented);
    public Task DeleteChatEventSettingAsync(Guid id) => throw new NotImplementedException(NotImplemented);
    public Task SaveEventStatisticAsync(EventStatistic statistic) => throw new NotImplementedException(NotImplemented);
    public Task<IEnumerable<EventStatistic>> GetEventStatisticsAsync(string? platform = null, string? eventType = null, int limit = 100, DateTime? startDate = null, DateTime? endDate = null) => throw new NotImplementedException(NotImplemented);
    public Task<IEnumerable<EventStatistic>> GetEventStatisticsByUsernameAsync(string username, string? platform = null) => throw new NotImplementedException(NotImplemented);
    public Task<int> DeleteEventStatisticsAsync() => throw new NotImplementedException(NotImplemented);
    public Task<CurrencySetting?> GetCurrencySettingsAsync() => throw new NotImplementedException(NotImplemented);
    public Task SaveCurrencySettingsAsync(CurrencySetting settings) => throw new NotImplementedException(NotImplemented);
    public Task<UserCurrency?> GetUserCurrencyAsync(string username, string platform) => throw new NotImplementedException(NotImplemented);
    public Task<IEnumerable<UserCurrency>> GetUserCurrenciesAsync(string username) => throw new NotImplementedException(NotImplemented);
    public Task<IEnumerable<UserCurrency>> GetCurrencyLeaderboardAsync(int limit = 10, string? platform = null) => throw new NotImplementedException(NotImplemented);
    public Task<IEnumerable<UserCurrency>> GetCurrencyUsersPagedAsync(int limit, int offset, string? platform = null) => throw new NotImplementedException(NotImplemented);
    public Task SaveUserCurrencyAsync(UserCurrency currency) => throw new NotImplementedException(NotImplemented);
    public Task SaveCurrencyTransactionAsync(CurrencyTransaction transaction) => throw new NotImplementedException(NotImplemented);
    public Task<IEnumerable<CurrencyTransaction>> GetCurrencyTransactionsAsync(string? username = null, string? platform = null, int limit = 100) => throw new NotImplementedException(NotImplemented);
    public Task<(long UserCount, long TotalBalance)> GetCurrencyEconomyTotalsAsync() => throw new NotImplementedException(NotImplemented);
    public Task<(long Earned, long Spent)> GetCurrencyTransactionTotalsSinceAsync(DateTime since) => throw new NotImplementedException(NotImplemented);
    // Wave-15 currency seasons (loyalty seasons) — not used by these tests.
    public Task<IEnumerable<CurrencySeason>> GetCurrencySeasonsAsync(int limit = 20) => throw new NotImplementedException(NotImplemented);
    public Task<CurrencySeason?> GetCurrentCurrencySeasonAsync() => throw new NotImplementedException(NotImplemented);
    public Task<(CurrencySeason NewSeason, int ResetBalances)> StartCurrencySeasonAsync(string name, string? topJson, bool resetBalances) => throw new NotImplementedException(NotImplemented);
    public Task<CurrencyReward?> GetCurrencyRewardAsync(string id) => throw new NotImplementedException(NotImplemented);
    public Task<IEnumerable<CurrencyReward>> GetCurrencyRewardsAsync(bool? enabledOnly = null) => throw new NotImplementedException(NotImplemented);
    public Task SaveCurrencyRewardAsync(CurrencyReward reward) => throw new NotImplementedException(NotImplemented);
    public Task DeleteCurrencyRewardAsync(string id) => throw new NotImplementedException(NotImplemented);
    public Task<CurrencyRedemption?> GetCurrencyRedemptionAsync(string id) => throw new NotImplementedException(NotImplemented);
    public Task<IEnumerable<CurrencyRedemption>> GetCurrencyRedemptionsAsync(string? status = null, string? username = null) => throw new NotImplementedException(NotImplemented);
    public Task SaveCurrencyRedemptionAsync(CurrencyRedemption redemption) => throw new NotImplementedException(NotImplemented);
    public Task DeleteCurrencyRedemptionAsync(string id) => throw new NotImplementedException(NotImplemented);
    public Task<LinkedAccount?> GetLinkedAccountAsync(string username, string platform) => throw new NotImplementedException(NotImplemented);
    public Task<IEnumerable<LinkedAccount>> GetLinkedAccountsByViewerIdAsync(string viewerId) => throw new NotImplementedException(NotImplemented);
    public Task<string?> GetViewerIdAsync(string username, string platform) => throw new NotImplementedException(NotImplemented);
    public Task SaveLinkedAccountAsync(LinkedAccount account) => throw new NotImplementedException(NotImplemented);
    public Task DeleteLinkedAccountAsync(string id) => throw new NotImplementedException(NotImplemented);
    public Task<LinkCode?> GetLinkCodeAsync(string code) => throw new NotImplementedException(NotImplemented);
    public Task SaveDiscordDmMessageAsync(DiscordDmMessage message) => throw new NotImplementedException(NotImplemented);
    public Task<IEnumerable<DiscordDmMessage>> GetDiscordDmHistoryAsync(string username) => throw new NotImplementedException(NotImplemented);
    public Task<IEnumerable<(string Username, string? BotUsername, DateTime LastAt, int Total)>> GetDiscordDmConversationsAsync() => throw new NotImplementedException(NotImplemented);
    public Task<IEnumerable<DiscordDmMessage>> SearchDiscordDmMessagesAsync(string query) => throw new NotImplementedException(NotImplemented);
    public Task ClearDiscordDmHistoryAsync(string? username) => throw new NotImplementedException(NotImplemented);
    public Task SaveLinkCodeAsync(LinkCode code) => throw new NotImplementedException(NotImplemented);
    public Task InvalidateLinkCodesForViewerAsync(string viewerId) => throw new NotImplementedException(NotImplemented);
    public Task<IEnumerable<LinkCode>> GetActiveLinkCodesAsync() => throw new NotImplementedException(NotImplemented);
    public Task<IEnumerable<LinkCode>> GetRecentLinkCodesAsync(int limit = 50) => throw new NotImplementedException(NotImplemented);
    public Task<IEnumerable<LinkCode>> GetUnredeemedLinkCodesByViewerAsync(string viewerId) => throw new NotImplementedException(NotImplemented);
    public Task DeleteLinkCodeAsync(string id) => throw new NotImplementedException(NotImplemented);
    public Task<IEnumerable<LinkedAccount>> GetAllLinkedAccountsAsync() => throw new NotImplementedException(NotImplemented);
    public Task UpdateLinkedAccountViewerAsync(string accountId, string newViewerId) => throw new NotImplementedException(NotImplemented);
    public Task DeleteLinkedAccountsByViewerIdAsync(string viewerId) => throw new NotImplementedException(NotImplemented);
    public Task<ViewerPlatformStatus?> GetViewerPlatformStatusAsync(string username, string platform) => throw new NotImplementedException(NotImplemented);
    public Task SaveViewerPlatformStatusAsync(ViewerPlatformStatus status) => throw new NotImplementedException(NotImplemented);
    public Task<IEnumerable<ViewerPlatformStatus>> GetViewerStatusesByViewerIdAsync(string viewerId) => throw new NotImplementedException(NotImplemented);
    public Task<ViewerPreferences?> GetViewerPreferencesAsync(string viewerId) => throw new NotImplementedException(NotImplemented);
    public Task SaveViewerPreferencesAsync(ViewerPreferences preferences) => throw new NotImplementedException(NotImplemented);
    public Task<IEnumerable<SocialGoal>> GetSocialGoalsAsync() => throw new NotImplementedException(NotImplemented);
    public Task<SocialGoal?> GetSocialGoalAsync(Guid id) => throw new NotImplementedException(NotImplemented);
    public Task SaveSocialGoalAsync(SocialGoal goal) => throw new NotImplementedException(NotImplemented);
    public Task DeleteSocialGoalAsync(Guid id) => throw new NotImplementedException(NotImplemented);
    public Task<ViewerGoalCompletion?> GetViewerGoalCompletionAsync(Guid goalId, string viewerId) => throw new NotImplementedException(NotImplemented);
    public Task SaveViewerGoalCompletionAsync(ViewerGoalCompletion completion) => throw new NotImplementedException(NotImplemented);
    public Task<int> GetGoalCompletionCountAsync(Guid goalId) => throw new NotImplementedException(NotImplemented);
    public Task<IEnumerable<ViewerGoalCompletion>> GetViewerGoalCompletionsByViewerIdAsync(string viewerId) => throw new NotImplementedException(NotImplemented);
    public Task<ViewerStreak?> GetViewerStreakAsync(string username, string platform) => throw new NotImplementedException(NotImplemented);
    public Task SaveViewerStreakAsync(ViewerStreak streak) => throw new NotImplementedException(NotImplemented);
    public Task<IEnumerable<LoyaltyTier>> GetLoyaltyTiersAsync() => throw new NotImplementedException(NotImplemented);
    public Task<LoyaltyTier?> GetLoyaltyTierAsync(string id) => throw new NotImplementedException(NotImplemented);
    public Task SaveLoyaltyTierAsync(LoyaltyTier tier) => throw new NotImplementedException(NotImplemented);
    public Task DeleteLoyaltyTierAsync(string id) => throw new NotImplementedException(NotImplemented);
    public Task<CurrencyGameSetting?> GetCurrencyGameSettingAsync(string gameType) => throw new NotImplementedException(NotImplemented);
    public Task<IEnumerable<CurrencyGameSetting>> GetCurrencyGameSettingsAsync() => throw new NotImplementedException(NotImplemented);
    public Task SaveCurrencyGameSettingAsync(CurrencyGameSetting setting) => throw new NotImplementedException(NotImplemented);
    public Task<IEnumerable<ViewerPerk>> GetActiveViewerPerksAsync(string username, string platform) => throw new NotImplementedException(NotImplemented);
    public Task<IEnumerable<ViewerPerk>> GetViewerPerksByTypeAsync(string username, string platform, string perkType) => throw new NotImplementedException(NotImplemented);
    public Task SaveViewerPerkAsync(ViewerPerk perk) => throw new NotImplementedException(NotImplemented);
    public Task ExpireViewerPerksAsync() => throw new NotImplementedException(NotImplemented);
    public Task<CurrencyBoost?> GetCurrencyBoostAsync(string id) => throw new NotImplementedException(NotImplemented);
    public Task<IEnumerable<CurrencyBoost>> GetActiveBoostsAsync(string? platform = null) => throw new NotImplementedException(NotImplemented);
    public Task<IEnumerable<CurrencyBoost>> GetBoostHistoryAsync(int limit = 50) => throw new NotImplementedException(NotImplemented);
    public Task SaveCurrencyBoostAsync(CurrencyBoost boost) => throw new NotImplementedException(NotImplemented);
    public Task ExpireBoostsAsync() => throw new NotImplementedException(NotImplemented);
    public Task<CurrencyAuction?> GetCurrencyAuctionAsync(string id) => throw new NotImplementedException(NotImplemented);
    public Task<CurrencyAuction?> GetActiveAuctionAsync() => throw new NotImplementedException(NotImplemented);
    public Task<IEnumerable<CurrencyAuction>> GetAuctionHistoryAsync(int limit = 20) => throw new NotImplementedException(NotImplemented);
    public Task SaveCurrencyAuctionAsync(CurrencyAuction auction) => throw new NotImplementedException(NotImplemented);
    public Task SaveCurrencyAuctionBidAsync(CurrencyAuctionBid bid) => throw new NotImplementedException(NotImplemented);
    public Task<IEnumerable<CurrencyAuctionBid>> GetAuctionBidsAsync(string auctionId) => throw new NotImplementedException(NotImplemented);
    public Task<UserBankAccount?> GetUserBankAccountAsync(string username, string platform) => throw new NotImplementedException(NotImplemented);
    public Task<IEnumerable<UserBankAccount>> GetAllBankAccountsAsync() => throw new NotImplementedException(NotImplemented);
    public Task SaveUserBankAccountAsync(UserBankAccount account) => throw new NotImplementedException(NotImplemented);
    public Task<IEnumerable<UserCurrency>> GetInactiveUsersForDecayAsync(DateTime cutoffDate) => throw new NotImplementedException(NotImplemented);
    public Task<Command?> GetCommandAsync(string id) => throw new NotImplementedException(NotImplemented);
    public Task<Command?> GetCommandByNameAsync(string name) => throw new NotImplementedException(NotImplemented);
    public Task<IEnumerable<Command>> GetCommandsAsync(bool? enabledOnly = null, bool? builtInOnly = null) => throw new NotImplementedException(NotImplemented);
    public Task<IEnumerable<Command>> GetCommandsByBotAsync(string botId) => throw new NotImplementedException(NotImplemented);
    public Task SaveCommandAsync(Command command) => throw new NotImplementedException(NotImplemented);
    public Task DeleteCommandAsync(string id) => throw new NotImplementedException(NotImplemented);
    public Task IncrementCommandUsageAsync(string id) => throw new NotImplementedException(NotImplemented);
    public Task<CommandCooldown?> GetCommandCooldownAsync(string commandId, string? username, string platform) => throw new NotImplementedException(NotImplemented);
    public Task SaveCommandCooldownAsync(CommandCooldown cooldown) => throw new NotImplementedException(NotImplemented);
    public Task DeleteExpiredCooldownsAsync() => throw new NotImplementedException(NotImplemented);
    public Task<IEnumerable<CommandTimer>> GetCommandTimersAsync() => throw new NotImplementedException(NotImplemented);
    public Task SaveCommandTimerAsync(CommandTimer timer) => throw new NotImplementedException(NotImplemented);
    public Task DeleteCommandTimerAsync(string id) => throw new NotImplementedException(NotImplemented);
    public Task<Quote> AddQuoteAsync(Quote quote) => throw new NotImplementedException(NotImplemented);
    public Task<Quote?> GetQuoteByNumberAsync(int number) => throw new NotImplementedException(NotImplemented);
    public Task<Quote?> GetRandomQuoteAsync() => throw new NotImplementedException(NotImplemented);
    public Task<IEnumerable<Quote>> SearchQuotesAsync(string searchText, int limit = 20) => throw new NotImplementedException(NotImplemented);
    public Task<IEnumerable<Quote>> GetQuotesAsync() => throw new NotImplementedException(NotImplemented);
    public Task<bool> DeleteQuoteAsync(int number) => throw new NotImplementedException(NotImplemented);
    public Task<int> GetQuoteCountAsync() => throw new NotImplementedException(NotImplemented);
    public Task<VyralBot.Data.Entities.SongRequest> AddSongRequestAsync(VyralBot.Data.Entities.SongRequest request) => throw new NotImplementedException(NotImplemented);
    public Task<IEnumerable<VyralBot.Data.Entities.SongRequest>> GetSongQueueAsync() => throw new NotImplementedException(NotImplemented);
    public Task<VyralBot.Data.Entities.SongRequest?> GetCurrentSongRequestAsync() => throw new NotImplementedException(NotImplemented);
    public Task<bool> SetSongRequestStatusAsync(int id, string status) => throw new NotImplementedException(NotImplemented);
    public Task<int> GetQueuedSongRequestCountAsync() => throw new NotImplementedException(NotImplemented);
    public Task<int> GetQueuedSongRequestCountByUserAsync(string requestedBy) => throw new NotImplementedException(NotImplemented);
    public Task<int> ClearSongRequestsAsync() => throw new NotImplementedException(NotImplemented);
    public Task<int> UpdateSongRequestTitleAsync(string youtubeId, string title, int? durationSeconds) => throw new NotImplementedException(NotImplemented);
    public Task<IEnumerable<AutoReplyRule>> GetAutoReplyRulesAsync() => throw new NotImplementedException(NotImplemented);
    public Task<IEnumerable<AutoReplyRule>> GetAutoReplyRulesByPlatformAndEventAsync(string platform, string eventType) => throw new NotImplementedException(NotImplemented);
    public Task<AutoReplyRule?> GetAutoReplyRuleAsync(int id) => throw new NotImplementedException(NotImplemented);
    public Task<int> SaveAutoReplyRuleAsync(AutoReplyRule rule) => throw new NotImplementedException(NotImplemented);
    public Task DeleteAutoReplyRuleAsync(int id) => throw new NotImplementedException(NotImplemented);
    public Task IncrementRuleTriggerCountAsync(int id) => throw new NotImplementedException(NotImplemented);
    public Task<IEnumerable<Giveaway>> GetGiveawaysAsync() => throw new NotImplementedException(NotImplemented);
    public Task DeleteGiveawayAsync(Guid id) => throw new NotImplementedException(NotImplemented);
    public Task<IEnumerable<GiveawayEntry>> GetGiveawayEntriesAsync(Guid giveawayId) => throw new NotImplementedException(NotImplemented);
    public Task<GiveawayEntry?> GetGiveawayEntryAsync(Guid giveawayId, string username, string platform) => throw new NotImplementedException(NotImplemented);
    public Task SaveGiveawayEntryAsync(GiveawayEntry entry) => throw new NotImplementedException(NotImplemented);
    public Task<int> GetGiveawayEntryCountAsync(Guid giveawayId) => throw new NotImplementedException(NotImplemented);
    public Task<int> GetGiveawayUniqueParticipantsAsync(Guid giveawayId) => throw new NotImplementedException(NotImplemented);
    public Task<IEnumerable<GiveawayEntry>> GetGiveawayEntriesForUserAsync(string username, string platform) => throw new NotImplementedException(NotImplemented);
    public Task<IEnumerable<Poll>> GetPollsAsync() => throw new NotImplementedException(NotImplemented);
    public Task DeletePollAsync(Guid id) => throw new NotImplementedException(NotImplemented);
    public Task<IEnumerable<PollVote>> GetPollVotesAsync(Guid pollId) => throw new NotImplementedException(NotImplemented);
    public Task<PollVote?> GetPollVoteAsync(Guid pollId, string username, string platform) => throw new NotImplementedException(NotImplemented);
    public Task SavePollVoteAsync(PollVote vote) => throw new NotImplementedException(NotImplemented);
    public Task<int> GetPollVoteCountAsync(Guid pollId) => throw new NotImplementedException(NotImplemented);
    public Task<UserWatchtime?> GetUserWatchtimeAsync(string username, string platform) => throw new NotImplementedException(NotImplemented);
    public Task<IEnumerable<UserWatchtime>> GetAllUserWatchtimeAsync() => throw new NotImplementedException(NotImplemented);
    public Task<IEnumerable<UserWatchtime>> GetTopWatchtimeUsersAsync(int limit = 10) => throw new NotImplementedException(NotImplemented);
    public Task<IEnumerable<UserWatchtime>> GetActiveViewersAsync() => throw new NotImplementedException(NotImplemented);
    public Task<int> GetTotalActiveViewersCountAsync() => throw new NotImplementedException(NotImplemented);
    public Task SaveUserWatchtimeAsync(UserWatchtime watchtime) => throw new NotImplementedException(NotImplemented);
    public Task DeactivateInactiveViewersAsync(DateTime inactiveSince) => throw new NotImplementedException(NotImplemented);
    public Task<int> DeactivateAllViewersAsync() => throw new NotImplementedException(NotImplemented);
    public Task ResetStreamSessionMinutesAsync() => throw new NotImplementedException(NotImplemented);
    public Task<bool> HasAnyActivityAsync(string username) => throw new NotImplementedException(NotImplemented);
    public Task SavePlatformEarningsAsync(PlatformEarnings earnings) => throw new NotImplementedException(NotImplemented);
    public Task<IEnumerable<PlatformEarnings>> GetPlatformEarningsAsync(string viewerId) => throw new NotImplementedException(NotImplemented);
    public Task AddPlatformEarningsAsync(string viewerId, string platform, long earned, long spent) => throw new NotImplementedException(NotImplemented);
    public Task<Variable?> GetVariableAsync(string id) => throw new NotImplementedException(NotImplemented);
    public Task<Variable?> GetVariableByNameAsync(string name) => throw new NotImplementedException(NotImplemented);
    public Task<IEnumerable<Variable>> GetVariablesAsync() => throw new NotImplementedException(NotImplemented);
    public Task<IEnumerable<Variable>> GetVariablesByCategoryAsync(string category) => throw new NotImplementedException(NotImplemented);
    public Task<IEnumerable<string>> GetVariableCategoriesAsync() => throw new NotImplementedException(NotImplemented);
    public Task SaveVariableAsync(Variable variable) => throw new NotImplementedException(NotImplemented);
    public Task DeleteVariableAsync(string id) => throw new NotImplementedException(NotImplemented);
    public Task UpdateVariableValueAsync(string name, string? value) => throw new NotImplementedException(NotImplemented);
}

/// <summary>Records currency operations against an in-memory balance table.</summary>
public class FakeCurrencyService : ICurrencyService
{
    public Dictionary<string, int> Balances { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<(string Username, string Platform, int Amount, string TransactionType)> Adds { get; } = new();
    public List<(string Username, string Platform, int Amount, string TransactionType)> Removes { get; } = new();

    private const string NotImplemented = "Not needed for mod engine tests.";

    private static string Key(string username, string platform) => $"{username}|{platform}";

    private int GetSync(string username, string platform)
        => Balances.TryGetValue(Key(username, platform), out var b) ? b : 0;

    public Task<int> GetBalanceAsync(string username, string platform)
        => Task.FromResult(GetSync(username, platform));

    public Task<bool> AddCurrencyAsync(string username, string platform, int amount, string reason, string transactionType = "Earn")
    {
        Adds.Add((username, platform, amount, transactionType));
        Balances[Key(username, platform)] = GetSync(username, platform) + amount;
        return Task.FromResult(true);
    }

    public Task<bool> RemoveCurrencyAsync(string username, string platform, int amount, string reason, string transactionType = "Spend")
    {
        if (GetSync(username, platform) < amount)
        {
            return Task.FromResult(false);
        }

        Removes.Add((username, platform, amount, transactionType));
        Balances[Key(username, platform)] = GetSync(username, platform) - amount;
        return Task.FromResult(true);
    }

    public Task<CurrencySetting?> GetSettingsAsync() => Task.FromResult<CurrencySetting?>(null);
    public Task SaveSettingsAsync(CurrencySetting settings) => throw new NotImplementedException(NotImplemented);
    public Task<IEnumerable<UserCurrency>> GetAllBalancesAsync(string username) => throw new NotImplementedException(NotImplemented);
    public Task<bool> TransferCurrencyAsync(string fromUsername, string fromPlatform, string toUsername, string toPlatform, int amount) => throw new NotImplementedException(NotImplemented);
    public Task<IEnumerable<UserCurrency>> GetLeaderboardAsync(int limit = 10, string? platform = null) => throw new NotImplementedException(NotImplemented);
    public Task<IEnumerable<CurrencyTransaction>> GetTransactionHistoryAsync(string username, string? platform = null, int limit = 50) => throw new NotImplementedException(NotImplemented);
    public Task AwardChatActivityAsync(string username, string platform, string? channelId = null) => throw new NotImplementedException(NotImplemented);
    public Task AwardWatchTimeAsync(string username, string platform) => throw new NotImplementedException(NotImplemented);
    public Task AwardEventBonusAsync(string username, string platform, string eventType, int multiplier = 1) => throw new NotImplementedException(NotImplemented);
    public Task<(bool Success, int Amount, int Streak, string Message)> ClaimDailyBonusAsync(string username, string platform) => throw new NotImplementedException(NotImplemented);
    public Task AwardFirstChatBonusAsync(string username, string platform) => throw new NotImplementedException(NotImplemented);
    public Task<double> GetEffectiveMultiplierAsync(string username, string platform, bool isSubscriber = false, bool isVip = false, bool isModerator = false) => throw new NotImplementedException(NotImplemented);
    public Task<int> GiveCurrencyToAllAsync(string platform, int amount, string reason) => throw new NotImplementedException(NotImplemented);
    public Task<EconomyStats> GetEconomyStatsAsync() => throw new NotImplementedException(NotImplemented);
    public Task ConsolidateViewerBalancesAsync(string viewerId) => throw new NotImplementedException(NotImplemented);
    public Task<IReadOnlyList<(string Platform, long Earned, long Spent)>> GetPerPlatformEarningsAsync(string viewerId) => throw new NotImplementedException(NotImplemented);
}

/// <summary>In-memory mod tier assignments (what the engine reads).</summary>
public class FakeModRoleService : IModRoleService
{
    /// <summary>Discord user id -> tier.</summary>
    public Dictionary<string, string> Store { get; } = new(StringComparer.Ordinal);

    public List<(string DiscordUserId, string Username, string Tier, string AssignedBy)> AssignCalls { get; } = new();

    public Task AssignAsync(string discordUserId, string username, string tier, string assignedBy)
    {
        AssignCalls.Add((discordUserId, username, tier, assignedBy));
        Store[discordUserId] = tier;
        return Task.CompletedTask;
    }

    public Task UnassignAsync(string discordUserId)
    {
        Store.Remove(discordUserId);
        return Task.CompletedTask;
    }

    public Task<string?> GetTierAsync(string discordUserId)
        => Task.FromResult(Store.TryGetValue(discordUserId, out var tier) ? tier : null);

    public Task<IReadOnlyList<ModRoleAssignment>> GetAllAsync()
        => Task.FromResult<IReadOnlyList<ModRoleAssignment>>(new List<ModRoleAssignment>());

    // Engine tests never carry a GuildId, so the role→tier mapping is unreachable.
    public Task<string?> GetTierFromRolesAsync(string botId, IReadOnlyList<string> discordRoleIds)
        => Task.FromResult<string?>(null);
}

/// <summary>Captures everything an <see cref="IModDiscordBot"/> would send to Discord.</summary>
public class FakeDiscordBot : IModDiscordBot
{
    public List<(string ChannelId, string Content)> SentMessages { get; } = new();
    public List<(string Username, string RoleIdOrName, string? Reason)> GrantedRoles { get; } = new();
    public List<(string Username, string Content)> SentDms { get; } = new();

    public Task<bool> SendToChannelAsync(string channelNameOrId, string content, string? embedJson, string? guildName = null)
    {
        SentMessages.Add((channelNameOrId, content));
        return Task.FromResult(true);
    }

    public Task<bool> SendDmAsync(string username, string content, string? embedJson, string? guildName = null)
    {
        SentDms.Add((username, content));
        return Task.FromResult(true);
    }

    public Task<bool> AddRoleToUserAsync(string username, string roleIdOrName, string? reason, string? guildName = null)
    {
        GrantedRoles.Add((username, roleIdOrName, reason));
        return Task.FromResult(true);
    }

    public Task<List<string>> GetMemberRoleIdsAsync(string guildId, string userId)
        => Task.FromResult(new List<string>());
}

/// <summary>A bot whose role grants always fail (Discord outage simulation).</summary>
public class ThrowingDiscordBot : IModDiscordBot
{
    public Task<bool> SendToChannelAsync(string channelNameOrId, string content, string? embedJson, string? guildName = null)
        => Task.FromResult(true);

    public Task<bool> SendDmAsync(string username, string content, string? embedJson, string? guildName = null)
        => Task.FromResult(true);

    public Task<bool> AddRoleToUserAsync(string username, string roleIdOrName, string? reason, string? guildName = null)
        => throw new InvalidOperationException("Discord is down");

    public Task<List<string>> GetMemberRoleIdsAsync(string guildId, string userId)
        => Task.FromResult(new List<string>());
}

/// <summary>
/// IEventBus fake that records published events synchronously (the real
/// EventBus fans out through a background channel, which would make "was it
/// published?" assertions racy).
/// </summary>
public sealed class RecordingEventBus : IEventBus
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

/// <summary>GiveawayService dependency — unused by the tested code paths.</summary>
public class FakeLinkedAccounts : ILinkedAccountService
{
    public Task<string?> GetViewerIdAsync(string username, string platform) => Task.FromResult<string?>(null);
    public Task<IEnumerable<LinkedAccount>> GetLinkedAccountsAsync(string viewerId) => Task.FromResult<IEnumerable<LinkedAccount>>(new List<LinkedAccount>());
    public Task<string> GetOrCreateViewerIdAsync(string username, string platform) => throw new NotImplementedException("Not needed for mod engine tests.");
    public Task<bool> InitiateLinkAsync(string fromUsername, string fromPlatform, string targetUsername, string targetPlatform) => throw new NotImplementedException("Not needed for mod engine tests.");
    public Task<bool> VerifyLinkAsync(string username, string platform, string verificationCode) => throw new NotImplementedException("Not needed for mod engine tests.");
    public Task<bool> UnlinkAccountAsync(string username, string platform) => throw new NotImplementedException("Not needed for mod engine tests.");
    public Task<IEnumerable<(string Username, string Platform)>> ResolveIdentitiesAsync(string username, string platform) => Task.FromResult<IEnumerable<(string Username, string Platform)>>(new List<(string Username, string Platform)>());
}

/// <summary>GiveawayService dependency — unused by the tested code paths.</summary>
public class FakeViewerStatus : IViewerStatusService
{
    public Task RecordSubscriptionAsync(string username, string platform, int tier, int months) => Task.CompletedTask;
    public Task RecordFollowAsync(string username, string platform) => Task.CompletedTask;
    public Task<ViewerPlatformStatus?> GetStatusAsync(string username, string platform) => Task.FromResult<ViewerPlatformStatus?>(null);
    public Task<int> GetBestSubTierAcrossLinkedAsync(string viewerId) => Task.FromResult(0);
    public Task<bool> IsFollowerAnywhereAsync(string viewerId) => Task.FromResult(false);
    public Task<int> GetLinkedPlatformCountAsync(string viewerId) => Task.FromResult(0);
}
