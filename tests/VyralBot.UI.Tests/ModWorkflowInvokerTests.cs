using Microsoft.Extensions.Logging.Abstractions;
using VyralBot.Core.Services;
using VyralBot.Core.Services.Currency;
using VyralBot.Core.Services.EventBus;
using VyralBot.Core.Services.Linking;
using VyralBot.Core.Services.Moderation;
using VyralBot.UI.Services;
using Xunit;

namespace VyralBot.UI.Tests;

/// <summary>
/// The unknown-verb fallback seam between the built-in !mod engine and the
/// workflow system (Task 2 of "mod commands as workflows"):
///
///   1. FALLBACK: a verb the engine does not know is offered to
///      IModWorkflowInvoker first; a non-null reply is posted into the mod
///      channel AND audited exactly like every other executed action.
///   2. NO MATCH: when the invoker answers null (no workflow registered the
///      verb) the engine keeps its classic "Unknown mod action" reply, never
///      audited.
///   3. BUILT-IN PRECEDENCE: known engine verbs (e.g. addcurrency) never
///      reach the invoker — the built-ins keep precedence.
///   4. OPTIONAL SEAM: without an invoker (null ctor default) behavior is
///      exactly as before, so older construction sites keep working.
///
/// The real invoker wraps the heavy WorkflowTriggerService, so the mapping is
/// exercised at the engine level with a recording fake; the UI-side wiring
/// (ModWorkflowInvoker → TryHandleModCommandAsync) needs a live run to verify.
/// </summary>
public class ModWorkflowInvokerTests
{
    private const string ModChannelId = "111222333";
    private const string BotId = "bot-1";
    private const string SenderId = "999000";
    private const string SenderName = "boss";

    [Fact]
    public async Task Unknown_Verb_With_Workflow_Reply_Posts_And_Audits_It()
    {
        var invoker = new FakeWorkflowInvoker { Reply = "✨ 'hype' triggered by boss." };
        var t = MakeEngine(invoker);
        Promote(t, SenderId);

        await t.Engine.HandleMessageAsync(Msg("!mod hype train now"));

        var call = Assert.Single(invoker.Calls);
        Assert.Equal(("hype", "train now", BotId, null, ModChannelId, SenderName, SenderId),
            (call.Verb, call.Args, call.BotId, call.GuildId, call.Channel, call.Username, call.UserId));

        // The workflow's reply is delivered into the mod channel...
        var reply = Assert.Single(t.Bot.SentMessages);
        Assert.Equal(ModChannelId, reply.ChannelId);
        Assert.Contains("'hype'", reply.Content);

        // ...and audited like every other executed action.
        var audit = Assert.Single(t.Data.ModActions);
        Assert.Equal("hype", audit.Action);
        Assert.Equal(SenderName, audit.ModUsername);
        Assert.Equal(SenderId, audit.ModUserId);
        Assert.Equal("✨ 'hype' triggered by boss.", audit.Details);
    }

    [Fact]
    public async Task Unknown_Verb_Without_Workflow_Match_Keeps_Unknown_Action_Reply()
    {
        var invoker = new FakeWorkflowInvoker { Reply = null };
        var t = MakeEngine(invoker);
        Promote(t, SenderId);

        await t.Engine.HandleMessageAsync(Msg("!mod nuke everything"));

        Assert.Equal(1, invoker.Calls.Count); // asked, but had no workflow
        var reply = Assert.Single(t.Bot.SentMessages);
        Assert.Contains("Unknown mod action 'nuke'", reply.Content);
        Assert.Empty(t.Data.ModActions); // never audited
    }

    [Fact]
    public async Task BuiltIn_Verb_Never_Reaches_The_Invoker()
    {
        var invoker = new FakeWorkflowInvoker { Reply = "should never be returned" };
        var t = MakeEngine(invoker);
        Promote(t, SenderId, "Mod");
        t.Currency.Balances["bob|Discord"] = 100;

        await t.Engine.HandleMessageAsync(Msg("!mod addcurrency @bob 50"));

        Assert.Empty(invoker.Calls); // built-ins keep precedence
        Assert.Contains("✅", Assert.Single(t.Bot.SentMessages).Content);
        Assert.Equal("addcurrency", Assert.Single(t.Data.ModActions).Action);
    }

    [Fact]
    public async Task Without_Invoker_Wired_Behavior_Is_Unchanged()
    {
        var t = MakeEngine(invoker: null);
        Promote(t, SenderId);

        await t.Engine.HandleMessageAsync(Msg("!mod nuke everything"));

        Assert.Contains("Unknown mod action 'nuke'", Assert.Single(t.Bot.SentMessages).Content);
        Assert.Empty(t.Data.ModActions);
    }

    // ------------------------------------------------------------------
    // helpers
    // ------------------------------------------------------------------

    private sealed class Harness
    {
        public FakeDataService Data { get; } = new();
        public FakeCurrencyService Currency { get; } = new();
        public FakeModRoleService Roles { get; } = new();
        public FakeDiscordBot Bot { get; } = new();

        public ModCommandEngine Engine { get; }

        public Harness(FakeWorkflowInvoker? invoker)
        {
            var bus = new EventBus(NullLogger<EventBus>.Instance);
            Engine = new ModCommandEngine(
                Roles, new ModTierResolver(Roles, Bot), Data, Currency,
                new GiveawayService(Data, bus, new FakeLinkedAccounts(), new FakeViewerStatus(),
                    NullLogger<GiveawayService>.Instance),
                new PollService(Data, NullLogger<PollService>.Instance),
                bus, NullLogger<ModCommandEngine>.Instance, invoker)
            {
                ModChannelIdProvider = () => ModChannelId,
                BotResolver = _ => Bot,
            };
        }
    }

    private static Harness MakeEngine(FakeWorkflowInvoker? invoker) => new(invoker);

    private static void Promote(Harness t, string senderId, string tier = "Admin")
        => t.Roles.Store[senderId] = tier;

    private static ModChannelMessage Msg(string message)
        => new(ModChannelId, SenderId, SenderName, message, BotId);

    /// <summary>Records every fallback attempt; Reply simulates the workflow layer.</summary>
    private sealed class FakeWorkflowInvoker : IModWorkflowInvoker
    {
        public string? Reply { get; set; }

        public List<(string Verb, string Args, string BotId, string? GuildId, string Channel, string Username, string UserId)> Calls { get; } = new();

        public Task<string?> TryInvokeAsync(string verb, string args, string botId, string? guildId, string channel, string username, string userId)
        {
            Calls.Add((verb, args, botId, guildId, channel, username, userId));
            return Task.FromResult(Reply);
        }
    }
}
