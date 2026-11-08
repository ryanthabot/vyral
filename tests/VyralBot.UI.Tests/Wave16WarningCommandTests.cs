using VyralBot.Core.Services.Moderation;
using VyralBot.Data.Entities;
using Xunit;

namespace VyralBot.UI.Tests;

/// <summary>
/// Wave-16 manual warnings (!warn / !warnings / !clearwarn). These tests pin
/// <see cref="WarningCommandService"/> on a DEDICATED engine + action service
/// (never the Shared instances other tests and the app share):
///
///   1. GATING: only the three exact command words are claimed (null for
///      everything else, including "!warned"/"!warning" prefixes); every
///      command is moderator-only — denials change no state and audit nothing.
///   2. SHARED LADDER: a manual warn advances the SAME strike store as rule
///      violations — in both orders (warn → next rule violation escalates;
///      rule strike → next warn escalates) the strike count is continuous.
///   3. ESCALATION: a warning-level strike only records + audits; a manual
///      warn that tips onto a Timeout/Ban rung fires through the NORMAL
///      action pipeline (adapter enforcement + AutoMod audit row) exactly
///      once — never double-punished; durations double per extra strike.
///   4. AUDIT: warning-level strikes and clearwarn persist through
///      SaveModActionAsync with the issuing mod as actor; escalated strikes
///      are audited by ModerationActionService with actor AutoMod.
///
/// Strike DECAY is wall-clock based (window clamped >= 1 minute) so a fast
/// unit test cannot cross it — same note as ChatModerationEngineTests. The
/// counts are in-memory engine state: restarting the app clears them (the
/// audit rows survive); that trade-off is documented in the service docs.
/// </summary>
public class Wave16WarningCommandTests
{
    private static (WarningCommandService Service, ChatModerationEngine Engine, FakeDataService Data, RecordingAdapter Adapter) Make()
    {
        // Fresh engine = CreateDefault rules: master switch OFF (manual warns
        // are moderator decisions and work regardless), escalation ladder ON
        // with timeout at strike 2, ban at strike 4, base timeout 120s.
        var engine = new ChatModerationEngine();
        var data = new FakeDataService();
        var actions = new ModerationActionService(data);
        var adapter = new RecordingAdapter();
        actions.RegisterAdapter(adapter);
        return (new WarningCommandService(engine, data, actions), engine, data, adapter);
    }

    private static ModerationIncomingMessage Msg(string text, string username = "viewer") => new()
    {
        Platform = "Twitch",
        Username = username,
        Message = text
    };

    /// <summary>Master switch ON with one banned word and the classic 2→3 ladder (mirrors ChatModerationEngineTests).</summary>
    private static ModerationRuleSet AutoModRules() => new()
    {
        MasterEnabled = true,
        BannedWords = new ModerationRuleConfig { Enabled = true, Action = ModerationActionKind.Delete },
        BannedWordList = new List<string> { "badword" },
        Escalation = new ModerationEscalationConfig
        {
            Enabled = true,
            StrikesToTimeout = 2,
            StrikesToBan = 3,
            StrikeWindowMinutes = 15,
            BaseTimeoutSeconds = 120
        }
    };

    // ------------------------------------------------------------------
    // gating
    // ------------------------------------------------------------------

    [Fact]
    public async Task NotAWarningCommand_ReturnsNull_AndTouchesNothing()
    {
        var (service, engine, data, adapter) = Make();

        Assert.Null(await service.ProcessCommandAsync("ModGuy", "Twitch", true, "!hello world"));
        Assert.Null(await service.ProcessCommandAsync("ModGuy", "Twitch", true, "warn spammer please"));
        Assert.Null(await service.ProcessCommandAsync("ModGuy", "Twitch", true, "!warned spammer")); // prefix, not ours
        Assert.Null(await service.ProcessCommandAsync("ModGuy", "Twitch", true, "!warning spammer"));
        Assert.Null(await service.ProcessCommandAsync("ModGuy", "Twitch", true, "!clearwarnings spammer"));

        Assert.Equal(0, engine.TrackedViewerCount);
        Assert.Empty(data.ModActions);
        Assert.Empty(adapter.Timeouts);
    }

    [Fact]
    public async Task EveryCommand_IsModeratorOnly()
    {
        var (service, engine, data, adapter) = Make();

        Assert.Equal("❌ Only moderators can manage warnings.", await service.ProcessCommandAsync("viewer", "Twitch", false, "!warn spammer rude"));
        Assert.Equal("❌ Only moderators can manage warnings.", await service.ProcessCommandAsync("viewer", "Twitch", false, "!warnings spammer"));
        Assert.Equal("❌ Only moderators can manage warnings.", await service.ProcessCommandAsync("viewer", "Twitch", false, "!clearwarn spammer"));

        Assert.Equal(0, engine.GetStrikeCount("Twitch", "spammer"));
        Assert.Empty(data.ModActions);
        Assert.Empty(adapter.Timeouts);
    }

    [Fact]
    public async Task MissingTarget_AnswersUsage_WithoutSideEffects()
    {
        var (service, engine, data, _) = Make();

        Assert.Equal("❌ Usage: !warn <user> [reason]", await service.ProcessCommandAsync("ModGuy", "Twitch", true, "!warn"));
        Assert.Equal("❌ Usage: !warnings <user>", await service.ProcessCommandAsync("ModGuy", "Twitch", true, "!warnings"));
        Assert.Equal("❌ Usage: !clearwarn <user>", await service.ProcessCommandAsync("ModGuy", "Twitch", true, "!clearwarn"));

        Assert.Equal(0, engine.TrackedViewerCount);
        Assert.Empty(data.ModActions);
    }

    // ------------------------------------------------------------------
    // !warn — recording + audit
    // ------------------------------------------------------------------

    [Fact]
    public async Task Warn_FirstStrike_RecordsIntoSharedLadder_AndAudits()
    {
        var (service, engine, data, adapter) = Make();

        var reply = await service.ProcessCommandAsync("ModGuy", "Twitch", true, "!warn @spammer posting links");

        // Strike 1 is warning-level on the default ladder (timeout at 2):
        // record + audit only — no platform punishment, no pipeline run.
        Assert.Equal("⚠ spammer warned (1 total) — next: timeout 120s", reply);
        Assert.Equal(1, engine.GetStrikeCount("Twitch", "spammer"));
        Assert.Empty(adapter.Timeouts);
        Assert.Empty(adapter.Bans);
        Assert.Empty(adapter.Notices);

        var audit = Assert.Single(data.ModActions);
        Assert.Equal("ModGuy", audit.ModUsername);
        Assert.Equal("spammer", audit.TargetUsername);
        Assert.Equal("Warn", audit.Action);
        Assert.Equal("Twitch: manual warning (strike 1) — posting links", audit.Details);
    }

    [Fact]
    public async Task Warn_WithoutReason_RecordsNoReasonGiven()
    {
        var (service, _, data, _) = Make();

        await service.ProcessCommandAsync("ModGuy", "Twitch", true, "!warn spammer");

        var audit = Assert.Single(data.ModActions);
        Assert.Equal("Twitch: manual warning (strike 1) — no reason given", audit.Details);
    }

    [Fact]
    public async Task Warn_StateIsPerPlatform()
    {
        var (service, engine, _, _) = Make();

        await service.ProcessCommandAsync("ModGuy", "Twitch", true, "!warn spammer");
        await service.ProcessCommandAsync("ModGuy", "Kick", true, "!warn spammer");

        Assert.Equal(1, engine.GetStrikeCount("Twitch", "spammer"));
        Assert.Equal(1, engine.GetStrikeCount("Kick", "spammer"));
    }

    // ------------------------------------------------------------------
    // shared ladder — manual warns and rule violations are ONE strike store
    // ------------------------------------------------------------------

    [Fact]
    public async Task ManualWarn_ThenRuleViolation_EscalatesOnTheNextHit()
    {
        var (service, engine, _, _) = Make();
        engine.ApplyRules(AutoModRules());

        await service.ProcessCommandAsync("ModGuy", "Twitch", true, "!warn spammer first");

        var verdict = engine.Evaluate(Msg("you badword guy", "spammer"));

        // The manual warn fed the SAME strike store: the next rule violation
        // is strike 2 and escalates Delete → Timeout through the normal
        // pipeline (the caller executes the verdict, as for every violation).
        Assert.NotNull(verdict);
        Assert.Equal(2, verdict!.Strikes);
        Assert.Equal(ModerationActionKind.Timeout, verdict.Action);
        Assert.Equal(120, verdict.TimeoutSeconds);
    }

    [Fact]
    public async Task RuleStrike_ThenManualWarn_EscalatesImmediatelyThroughThePipeline()
    {
        var (service, engine, data, adapter) = Make();
        engine.ApplyRules(AutoModRules());

        var verdict = engine.Evaluate(Msg("you badword guy", "spammer"));
        Assert.NotNull(verdict);
        Assert.Equal(1, verdict!.Strikes); // rule strike 1 keeps the rule action
        Assert.Equal(ModerationActionKind.Delete, verdict.Action);

        var reply = await service.ProcessCommandAsync("ModGuy", "Twitch", true, "!warn spammer kept going");

        // The manual warn is strike 2 on the SAME ladder → the ladder escalates
        // NOW, fired through the normal action pipeline exactly once:
        // adapter timeout + AutoMod audit row (never double-punished).
        Assert.Contains("⚠ spammer warned (2 total)", reply);
        Assert.Contains("escalated: timeout 120s", reply);
        Assert.Contains("next: ban", reply);
        var timeout = Assert.Single(adapter.Timeouts);
        Assert.Equal(("spammer", 120, "manual warning by ModGuy: kept going"), timeout);
        Assert.Contains(data.ModActions,
            a => a.ModUsername == "AutoMod" && a.Action == "Timeout" && a.TargetUsername == "spammer");
    }

    [Fact]
    public async Task ManualWarns_ClaimEscalatedLadderRungs_ThenBan()
    {
        var (service, engine, data, adapter) = Make();

        var first = await service.ProcessCommandAsync("ModGuy", "Twitch", true, "!warn spammer one");
        var second = await service.ProcessCommandAsync("ModGuy", "Twitch", true, "!warn spammer two");
        var third = await service.ProcessCommandAsync("ModGuy", "Twitch", true, "!warn spammer three");
        var fourth = await service.ProcessCommandAsync("ModGuy", "Twitch", true, "!warn spammer four");

        // Default ladder: warn → 120s timeout → 240s timeout (doubling) → ban.
        Assert.Equal("⚠ spammer warned (1 total) — next: timeout 120s", first);
        Assert.Contains("escalated: timeout 120s", second);
        Assert.Contains("next: timeout 240s", second);
        Assert.Contains("escalated: timeout 240s", third);
        Assert.Contains("next: ban", third);
        Assert.Contains("escalated: ban", fourth);

        Assert.Equal(new[] { 120, 240 }, adapter.Timeouts.Select(t => t.Seconds).ToArray());
        Assert.Equal(new[] { "spammer" }, adapter.Bans);
        Assert.Equal(4, engine.GetStrikeCount("Twitch", "spammer"));
        Assert.Contains(data.ModActions, a => a.Action == "Ban" && a.ModUsername == "AutoMod");
    }

    [Fact]
    public async Task ManualWarns_NeverEscalate_WhenLadderDisabled()
    {
        var (service, engine, data, adapter) = Make();
        var rules = ModerationRuleSet.CreateDefault();
        rules.Escalation = new ModerationEscalationConfig
        {
            Enabled = false,
            StrikesToTimeout = 2,
            StrikesToBan = 3,
            StrikeWindowMinutes = 15,
            BaseTimeoutSeconds = 120
        };
        engine.ApplyRules(rules);

        for (var strike = 1; strike <= 4; strike++)
        {
            var reply = await service.ProcessCommandAsync("ModGuy", "Twitch", true, "!warn spammer");
            Assert.Equal($"⚠ spammer warned ({strike} total) — next: warning", reply);
        }

        Assert.Empty(adapter.Timeouts);
        Assert.Empty(adapter.Bans);
        Assert.Equal(4, engine.GetStrikeCount("Twitch", "spammer"));

        var query = await service.ProcessCommandAsync("ModGuy", "Twitch", true, "!warnings spammer");
        Assert.Equal("⚠ spammer: 4 warning(s) — next: warning (escalation off)", query);
    }

    // ------------------------------------------------------------------
    // !warnings — read-only ladder position
    // ------------------------------------------------------------------

    [Fact]
    public async Task Warnings_ReportsCountAndLadderPosition()
    {
        var (service, _, _, _) = Make();
        await service.ProcessCommandAsync("ModGuy", "Twitch", true, "!warn spammer rude");

        var reply = await service.ProcessCommandAsync("ModGuy", "Twitch", true, "!warnings @spammer");

        Assert.Equal("⚠ spammer: 1 warning(s) — next: timeout 120s (timeout at 2, ban at 4, 15m decay window)", reply);
    }

    [Fact]
    public async Task Warnings_UnknownViewer_HasNoActiveWarnings()
    {
        var (service, engine, _, _) = Make();

        var reply = await service.ProcessCommandAsync("ModGuy", "Twitch", true, "!warnings ghost");

        Assert.Equal("✅ ghost has no active warnings.", reply);
        Assert.Equal(0, engine.TrackedViewerCount); // read-only — no state created
    }

    // ------------------------------------------------------------------
    // !clearwarn — reset via the engine's existing ResetViewer
    // ------------------------------------------------------------------

    [Fact]
    public async Task ClearWarn_ResetsTheViewer_LadderRestarts()
    {
        var (service, engine, data, _) = Make();
        await service.ProcessCommandAsync("ModGuy", "Twitch", true, "!warn spammer rude");

        var reply = await service.ProcessCommandAsync("ModGuy", "Twitch", true, "!clearwarn @spammer");

        Assert.Equal("✅ Warnings cleared for spammer (had 1).", reply);
        Assert.Equal(0, engine.GetStrikeCount("Twitch", "spammer"));
        Assert.Equal(2, data.ModActions.Count); // warn + clearwarn, both audited
        var audit = data.ModActions[1];
        Assert.Equal("ModGuy", audit.ModUsername);
        Assert.Equal("ClearWarn", audit.Action);
        Assert.Equal("Twitch: cleared 1 warning(s)", audit.Details);

        // The ladder genuinely restarted: the next warn is strike 1 again.
        var next = await service.ProcessCommandAsync("ModGuy", "Twitch", true, "!warn spammer again");
        Assert.Equal("⚠ spammer warned (1 total) — next: timeout 120s", next);
    }

    // ==================================================================
    // test double: an enforcement-capable platform adapter that records
    // ==================================================================
    private sealed class RecordingAdapter : IModerationPlatformAdapter
    {
        public string Platform => "Twitch";
        public bool CanDeleteMessage => true;
        public bool CanTimeout => true;
        public bool CanBan => true;

        public List<(string User, int Seconds, string Reason)> Timeouts { get; } = new(capacity: 8);
        public List<string> Bans { get; } = new(capacity: 8);
        public List<string> Notices { get; } = new(capacity: 8);

        public Task<bool> DeleteMessageAsync(ModerationIncomingMessage message) => Task.FromResult(true);

        public Task<bool> TimeoutAsync(ModerationIncomingMessage message, int seconds, string reason)
        {
            Timeouts.Add((message.Username, seconds, reason));
            return Task.FromResult(true);
        }

        public Task<bool> BanAsync(ModerationIncomingMessage message, string reason)
        {
            Bans.Add(message.Username);
            return Task.FromResult(true);
        }

        public Task<bool> SendNoticeAsync(string message)
        {
            Notices.Add(message);
            return Task.FromResult(true);
        }
    }
}
