using Microsoft.Extensions.Logging.Abstractions;
using VyralBot.Core.Services.Moderation;
using Xunit;

namespace VyralBot.UI.Tests;

/// <summary>
/// WAVE-8 INTEGRATION: the auto-mod pipeline end to end —
/// <see cref="ChatModerationEngine"/> verdict (real rule evaluation) →
/// <see cref="ModerationActionService"/> execution (real degradation ladder)
/// → platform adapter (recording fake) → mod-action audit log (in-memory
/// IDataService fake).
///
/// The two halves are wired exactly like the chat pipeline does it: evaluate
/// every message, execute only non-null verdicts. Covers:
///   • banned word → Timeout verdict → adapter.TimeoutAsync with the rule's
///     duration + engine's reason, audit row written by AutoMod;
///   • banned word under a Delete rule → adapter.DeleteMessageAsync with the
///     exact incoming message (message id / author survive the handoff);
///   • clean message → null verdict → no adapter call, no audit row;
///   • platform without an adapter (TikTok) → graceful degradation to a
///     logged-only Warn, nothing thrown.
///
/// Fresh engine/service instances per test (NOT the .Shared singletons) so
/// rule snapshots and escalation state never leak between tests.
/// </summary>
public class IntegrationWave8AutoModerationTests
{
    [Fact]
    public async Task BannedWord_TimeoutRule_Executes_AdapterTimeout_And_Audits()
    {
        var t = MakeHarness(ModerationActionKind.Timeout, 600);
        var adapter = t.Adapter;

        var bad = Msg("troll", "you are such a forbiddenword indeed", userId: "u-1");
        var verdict = t.Engine.Evaluate(bad);

        // Verdict: the rule's action + duration, un-escalated (escalation off).
        Assert.NotNull(verdict);
        Assert.Equal(ModerationRuleType.BannedWords, verdict!.Rule);
        Assert.Equal(ModerationActionKind.Timeout, verdict.Action);
        Assert.Equal(600, verdict.TimeoutSeconds);
        Assert.Contains("forbiddenword", verdict.Reason);

        // Execution: adapter timeout with the exact parameters, nothing else.
        var executed = await t.Actions.ExecuteAsync(bad, verdict);
        Assert.Equal(ModerationActionKind.Timeout, executed);
        var timeout = Assert.Single(adapter.Timeouts);
        Assert.Same(bad, timeout.Message);
        Assert.Equal(600, timeout.Seconds);
        Assert.Equal(verdict.Reason, timeout.Reason);
        Assert.Empty(adapter.DeletedMessages);
        Assert.Empty(adapter.Bans);
        Assert.Empty(adapter.Notices);

        // Audit: one ModAction row by AutoMod naming the target.
        var audit = Assert.Single(t.Data.ModActions);
        Assert.Equal("AutoMod", audit.ModUsername);
        Assert.Equal("troll", audit.TargetUsername);
        Assert.Equal("u-1", audit.TargetUserId);
        Assert.Equal("Timeout", audit.Action);
        Assert.Contains("Twitch", audit.Details);
        Assert.Contains("600s", audit.Details);
    }

    [Fact]
    public async Task BannedWord_DeleteRule_Executes_AdapterDelete_With_MessageIdentity()
    {
        var t = MakeHarness(ModerationActionKind.Delete, seconds: 60);
        var adapter = t.Adapter;

        var bad = Msg("spammer", "hello forbiddenword friends", userId: "u-2", messageId: "msg-42");
        var verdict = t.Engine.Evaluate(bad);
        Assert.NotNull(verdict);
        Assert.Equal(ModerationActionKind.Delete, verdict!.Action);

        var executed = await t.Actions.ExecuteAsync(bad, verdict);
        Assert.Equal(ModerationActionKind.Delete, executed);

        // The message handed to the platform delete API keeps its identity
        // (delete-by-id APIs depend on it).
        var deleted = Assert.Single(adapter.DeletedMessages);
        Assert.Same(bad, deleted);
        Assert.Equal("msg-42", deleted.MessageId);
        Assert.Equal("spammer", deleted.Username);
        Assert.Equal("Twitch", deleted.Platform);
        Assert.Empty(adapter.Timeouts);
        Assert.Empty(adapter.Bans);

        Assert.Equal("Delete", Assert.Single(t.Data.ModActions).Action);
    }

    [Fact]
    public async Task Clean_Message_Returns_Null_Verdict_And_Never_Reaches_The_Action_Service()
    {
        var t = MakeHarness(ModerationActionKind.Timeout, 600);

        // Pipeline contract: null verdict → the caller never executes.
        var clean = Msg("nicefan", "what a great stream today!");
        Assert.Null(t.Engine.Evaluate(clean));

        // (ChatViewModel wiring: only non-null verdicts are executed.)
        var verdict = t.Engine.Evaluate(clean);
        if (verdict != null)
        {
            await t.Actions.ExecuteAsync(clean, verdict);
        }

        Assert.Empty(t.Adapter.DeletedMessages);
        Assert.Empty(t.Adapter.Timeouts);
        Assert.Empty(t.Adapter.Bans);
        Assert.Empty(t.Adapter.Notices);
        Assert.Empty(t.Data.ModActions);
    }

    [Fact]
    public async Task Platform_Without_Adapter_Degrades_To_LoggedOnly_Warn()
    {
        // Only a Twitch adapter is registered; a TikTok hit must degrade
        // gracefully (logged-only Warn) instead of throwing — the parity table
        // documents TikTok as having no enforcement surface at all.
        var t = MakeHarness(ModerationActionKind.Ban, seconds: 60);

        var tiktokMessage = new ModerationIncomingMessage
        {
            Platform = "TikTok",
            Username = "trolltok",
            Message = "forbiddenword on tiktok"
        };
        var verdict = t.Engine.Evaluate(tiktokMessage);
        Assert.NotNull(verdict);
        Assert.Equal(ModerationActionKind.Ban, verdict!.Action);

        var executed = await t.Actions.ExecuteAsync(tiktokMessage, verdict);
        Assert.Equal(ModerationActionKind.Warn, executed);

        // The Twitch adapter was never touched; the audit row records the
        // degradation explicitly.
        Assert.Empty(t.Adapter.Bans);
        Assert.Empty(t.Adapter.Timeouts);
        Assert.Empty(t.Adapter.DeletedMessages);
        var audit = Assert.Single(t.Data.ModActions);
        Assert.Equal("Warn", audit.Action);
        Assert.Equal("trolltok", audit.TargetUsername);
        Assert.Contains("no platform adapter", audit.Details);
    }

    // ------------------------------------------------------------------
    // helpers
    // ------------------------------------------------------------------

    private sealed class Harness
    {
        public IntegrationWave8DataService Data { get; } = new();
        public ChatModerationEngine Engine { get; }
        public ModerationActionService Actions { get; }
        public RecordingModerationAdapter Adapter { get; } = new("Twitch");

        public Harness(ModerationActionKind bannedWordAction, int seconds)
        {
            // Banned-words only, everything else off, escalation off — the
            // executed action is exactly the authored rule action.
            Engine = new ChatModerationEngine();
            Engine.ApplyRules(new ModerationRuleSet
            {
                MasterEnabled = true,
                BannedWords = new ModerationRuleConfig
                {
                    Enabled = true,
                    Action = bannedWordAction,
                    TimeoutSeconds = seconds
                },
                Links = new ModerationRuleConfig { Enabled = false },
                Caps = new ModerationRuleConfig { Enabled = false },
                Emojis = new ModerationRuleConfig { Enabled = false },
                RepeatedMessage = new ModerationRuleConfig { Enabled = false },
                AsciiArt = new ModerationRuleConfig { Enabled = false },
                Escalation = new ModerationEscalationConfig { Enabled = false },
                BannedWordList = new List<string> { "forbiddenword" }
            });

            Actions = new ModerationActionService(Data, NullLogger<ModerationActionService>.Instance);
            Actions.RegisterAdapter(Adapter);
        }
    }

    private static Harness MakeHarness(ModerationActionKind action, int seconds) => new(action, seconds);

    private static ModerationIncomingMessage Msg(
        string username, string message, string userId = "", string messageId = "")
        => new()
        {
            Platform = "Twitch",
            Username = username,
            UserId = userId,
            MessageId = messageId,
            Message = message
        };

    /// <summary>Records every enforcement call; returns success for each.</summary>
    private sealed class RecordingModerationAdapter : IModerationPlatformAdapter
    {
        public RecordingModerationAdapter(string platform)
        {
            Platform = platform;
        }

        public string Platform { get; }

        public bool CanDeleteMessage => true;
        public bool CanTimeout => true;
        public bool CanBan => true;

        public List<ModerationIncomingMessage> DeletedMessages { get; } = new();
        public List<(ModerationIncomingMessage Message, int Seconds, string Reason)> Timeouts { get; } = new();
        public List<(ModerationIncomingMessage Message, string Reason)> Bans { get; } = new();
        public List<string> Notices { get; } = new();

        public Task<bool> DeleteMessageAsync(ModerationIncomingMessage message)
        {
            DeletedMessages.Add(message);
            return Task.FromResult(true);
        }

        public Task<bool> TimeoutAsync(ModerationIncomingMessage message, int seconds, string reason)
        {
            Timeouts.Add((message, seconds, reason));
            return Task.FromResult(true);
        }

        public Task<bool> BanAsync(ModerationIncomingMessage message, string reason)
        {
            Bans.Add((message, reason));
            return Task.FromResult(true);
        }

        public Task<bool> SendNoticeAsync(string message)
        {
            Notices.Add(message);
            return Task.FromResult(true);
        }
    }
}
