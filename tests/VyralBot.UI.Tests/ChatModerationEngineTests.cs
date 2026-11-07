using VyralBot.Core.Services.Moderation;
using Xunit;

namespace VyralBot.UI.Tests;

/// <summary>
/// Marathon wave-1 fix: the cross-platform <see cref="ChatModerationEngine"/>
/// (rules + exemptions + escalation + bounded viewer state). These tests pin
/// the public contract on a dedicated engine instance (never
/// <see cref="ChatModerationEngine.Shared"/>, which other tests and the app
/// share):
///
///   1. THRESHOLDS: each of the six rules fires at (or beyond) its threshold
///      and stays silent below it.
///   2. EXEMPTIONS: the broadcaster is always exempt; per-rule mod/VIP/sub
///      flags and the global ExemptUsers list skip the rule.
///   3. ESCALATION: strikes climb the warn/delete → timeout (doubling) → ban
///      ladder; a rule action already harsher than the rung is kept;
///      ResetViewer/ResetAll clear the ladder.
///   4. BOUNDED STATE: the tracked-viewer table never exceeds
///      <see cref="ChatModerationEngine.MaxTrackedViewers"/>.
///
/// Strike DECAY (strike window) is wall-clock based (DateTime.UtcNow, window
/// clamped to >= 1 minute) so a fast unit test cannot cross it; the decay
/// branch itself is therefore covered only through the reset APIs below.
/// </summary>
public class ChatModerationEngineTests
{
    private static ModerationIncomingMessage Msg(
        string message,
        string username = "viewer",
        string platform = "Twitch",
        bool isModerator = false,
        bool isVip = false,
        bool isSubscriber = false,
        bool isBroadcaster = false) => new()
    {
        Platform = platform,
        Username = username,
        Message = message,
        IsModerator = isModerator,
        IsVip = isVip,
        IsSubscriber = isSubscriber,
        IsBroadcaster = isBroadcaster
    };

    /// <summary>A curated rule set with the master switch ON and sane thresholds.</summary>
    private static ModerationRuleSet Rules() => new()
    {
        MasterEnabled = true,
        BannedWords = new ModerationRuleConfig { Enabled = true, Action = ModerationActionKind.Delete },
        Links = new ModerationRuleConfig { Enabled = true, Action = ModerationActionKind.Delete, Threshold = 0 },
        Caps = new ModerationRuleConfig { Enabled = true, Action = ModerationActionKind.Delete, Threshold = 70, MinLength = 12 },
        Emojis = new ModerationRuleConfig { Enabled = true, Action = ModerationActionKind.Delete, Threshold = 2 },
        RepeatedMessage = new ModerationRuleConfig { Enabled = true, Action = ModerationActionKind.Delete, Threshold = 3, MinLength = 60 },
        AsciiArt = new ModerationRuleConfig { Enabled = true, Action = ModerationActionKind.Delete, Threshold = 30, MinLength = 20 },
        Escalation = new ModerationEscalationConfig
        {
            Enabled = true,
            StrikesToTimeout = 2,
            StrikesToBan = 3,
            StrikeWindowMinutes = 15,
            BaseTimeoutSeconds = 120
        }
    };

    private static ChatModerationEngine MakeEngine(ModerationRuleSet? rules = null)
    {
        var engine = new ChatModerationEngine();
        engine.ApplyRules(rules ?? Rules());
        return engine;
    }

    // ------------------------------------------------------------------
    // Rule 1: banned words (fires on a listed word; misses below/around it)
    // ------------------------------------------------------------------

    [Fact]
    public void BannedWords_Fires_OnListedWord()
    {
        var rules = Rules();
        rules.BannedWordList.Add("badword");

        var verdict = MakeEngine(rules).Evaluate(Msg("say badword now"));

        Assert.NotNull(verdict);
        Assert.Equal(ModerationRuleType.BannedWords, verdict!.Rule);
        Assert.Equal(ModerationActionKind.Delete, verdict.Action);
        Assert.Contains("badword", verdict.Reason);
    }

    [Fact]
    public void BannedWords_Silent_WhenWordNotListed()
    {
        var rules = Rules();
        rules.BannedWordList.Add("badword");

        Assert.Null(MakeEngine(rules).Evaluate(Msg("what a nice day")));
    }

    [Fact]
    public void BannedWords_WholeWordOnly_SubstringDoesNotFire()
    {
        var rules = Rules();
        rules.BannedWordList.Add("ass");

        // "class" CONTAINS the banned substring but is not the whole word.
        Assert.Null(MakeEngine(rules).Evaluate(Msg("great class today")));
    }

    [Fact]
    public void BannedWords_WordAllowlist_OverridesTheBan()
    {
        var rules = Rules();
        rules.BannedWordList.Add("badword");
        rules.WordAllowlist.Add("badword");

        Assert.Null(MakeEngine(rules).Evaluate(Msg("say badword now")));
    }

    // ------------------------------------------------------------------
    // Rule 2: links (threshold = allowed non-allowlisted links)
    // ------------------------------------------------------------------

    [Fact]
    public void Links_AllowedCount_OneLinkTolerated_TwoLinksFire()
    {
        var rules = Rules();
        rules.Links.Threshold = 1; // one non-allowlisted link tolerated

        var engine = MakeEngine(rules);
        Assert.Null(engine.Evaluate(Msg("see https://spam.example/a", username: "user1")));
        Assert.NotNull(engine.Evaluate(Msg("see https://spam.example/a and https://spam2.example/b", username: "user2")));
    }

    [Fact]
    public void Links_ThresholdZero_AnyNonAllowlistedLinkFires()
    {
        var engine = MakeEngine(Rules());

        var verdict = engine.Evaluate(Msg("see https://spam.example/a"));

        Assert.NotNull(verdict);
        Assert.Equal(ModerationRuleType.Links, verdict!.Rule);
    }

    [Fact]
    public void Links_AllowlistedDomain_DoesNotCount()
    {
        var rules = Rules();
        rules.Links.Threshold = 0;
        rules.LinkAllowlist.Add("twitch.tv");

        // Suffix match too: clips.twitch.tv is covered by "twitch.tv".
        Assert.Null(MakeEngine(rules).Evaluate(Msg("clip https://clips.twitch.tv/x here")));
    }

    // ------------------------------------------------------------------
    // Rule 3: caps (threshold = max upper-case % of letters)
    // ------------------------------------------------------------------

    [Fact]
    public void Caps_Fires_AtOrAboveThreshold()
    {
        // 20 letters, 100% upper-case.
        var verdict = MakeEngine().Evaluate(Msg("HELLO WORLD HELLO WORLD"));

        Assert.NotNull(verdict);
        Assert.Equal(ModerationRuleType.Caps, verdict!.Rule);
        Assert.Contains("100%", verdict.Reason);
    }

    [Fact]
    public void Caps_Silent_BelowThreshold()
    {
        // 32 letters, 11 upper-case = 34% < 70%.
        Assert.Null(MakeEngine().Evaluate(Msg("Hello WORLD world hello WORLD hi there")));
    }

    [Fact]
    public void Caps_Silent_ForShortShouts()
    {
        // "LOL" is 100% caps but below the 12-letter minimum length.
        Assert.Null(MakeEngine().Evaluate(Msg("LOL")));
    }

    // ------------------------------------------------------------------
    // Rule 4: emojis (threshold = max emoji count; fires above it)
    // ------------------------------------------------------------------

    [Fact]
    public void Emojis_AtThreshold_Silent_AboveThreshold_Fires()
    {
        var engine = MakeEngine(Rules()); // threshold = 2

        Assert.Null(engine.Evaluate(Msg("\U0001F600\U0001F600 nice", username: "user1")));       // exactly 2: allowed
        var verdict = engine.Evaluate(Msg("\U0001F600\U0001F600\U0001F600 spam", username: "user2")); // 3: strike
        Assert.NotNull(verdict);
        Assert.Equal(ModerationRuleType.Emojis, verdict!.Rule);
    }

    // ------------------------------------------------------------------
    // Rule 5: repeated message (threshold = repeats within the window)
    // ------------------------------------------------------------------

    [Fact]
    public void RepeatedMessage_BelowThreshold_Silent_AtThreshold_Fires()
    {
        var engine = MakeEngine(Rules()); // threshold = 3 identical messages

        Assert.Null(engine.Evaluate(Msg("spam me please", username: "repeater")));
        Assert.Null(engine.Evaluate(Msg("spam me please", username: "repeater")));
        var verdict = engine.Evaluate(Msg("spam me please", username: "repeater"));

        Assert.NotNull(verdict);
        Assert.Equal(ModerationRuleType.RepeatedMessage, verdict!.Rule);
        Assert.Contains("repeated 3", verdict.Reason);
    }

    // ------------------------------------------------------------------
    // Rule 6: ASCII art (threshold = max art-char % of the message)
    // ------------------------------------------------------------------

    [Fact]
    public void AsciiArt_Fires_AtOrAboveThreshold_SilentBelow()
    {
        var rules = Rules(); // threshold 30%, min length 20
        var engine = MakeEngine(rules);

        // 10 art chars / 22 chars = 45%.
        var verdict = engine.Evaluate(Msg("────────── hello world", username: "user1"));
        Assert.NotNull(verdict);
        Assert.Equal(ModerationRuleType.AsciiArt, verdict!.Rule);

        // 2 art chars / 22 chars = 9%.
        Assert.Null(engine.Evaluate(Msg("── hello world friend", username: "user2")));
    }

    // ------------------------------------------------------------------
    // Master switch + platform scoping
    // ------------------------------------------------------------------

    [Fact]
    public void MasterSwitch_Off_PassesEverything()
    {
        var rules = Rules();
        rules.MasterEnabled = false;
        rules.BannedWordList.Add("badword");

        Assert.Null(MakeEngine(rules).Evaluate(Msg("say badword now")));
    }

    [Fact]
    public void PlatformFilter_OffPlatformMessages_Pass()
    {
        var rules = Rules();
        rules.Platforms.Add("Twitch");
        rules.BannedWordList.Add("badword");

        var engine = MakeEngine(rules);

        Assert.Null(engine.Evaluate(Msg("say badword now", platform: "Kick")));
        // Case-insensitive platform match still moderates.
        Assert.NotNull(engine.Evaluate(Msg("say badword now", platform: "twitch")));
    }

    // ------------------------------------------------------------------
    // Exemptions: broadcaster always; per-rule mod/VIP/sub; ExemptUsers
    // ------------------------------------------------------------------

    [Fact]
    public void Broadcaster_IsAlwaysExempt()
    {
        var rules = Rules();
        rules.BannedWordList.Add("badword");

        Assert.Null(MakeEngine(rules).Evaluate(Msg("say badword now", isBroadcaster: true)));
    }

    [Fact]
    public void Moderator_IsExempt_WhenRuleAllows()
    {
        var rules = Rules();
        rules.BannedWordList.Add("badword");
        rules.BannedWords.ExemptModerators = true; // the shipped default

        Assert.Null(MakeEngine(rules).Evaluate(Msg("say badword now", isModerator: true)));
    }

    [Fact]
    public void Vip_IsExempt_Only_WhenRuleOptsIn()
    {
        var rules = Rules();
        rules.BannedWordList.Add("badword");
        rules.BannedWords.ExemptModerators = false;

        rules.BannedWords.ExemptVips = false;
        Assert.NotNull(MakeEngine(rules).Evaluate(Msg("say badword now", isVip: true)));

        rules.BannedWords.ExemptVips = true;
        Assert.Null(MakeEngine(rules).Evaluate(Msg("say badword now", isVip: true)));
    }

    [Fact]
    public void Subscriber_IsExempt_WhenRuleOptsIn()
    {
        var rules = Rules();
        rules.BannedWordList.Add("badword");
        rules.BannedWords.ExemptModerators = false;
        rules.BannedWords.ExemptSubscribers = true;

        Assert.Null(MakeEngine(rules).Evaluate(Msg("say badword now", isSubscriber: true)));
    }

    [Fact]
    public void ExemptUsersList_SkipsTheViewer()
    {
        var rules = Rules();
        rules.BannedWordList.Add("badword");
        rules.ExemptUsers.Add("trusted");

        Assert.Null(MakeEngine(rules).Evaluate(Msg("say badword now", username: "Trusted"))); // case-insensitive
    }

    // ------------------------------------------------------------------
    // Escalation ladder: strike 1 rule action → timeout (doubling) → ban
    // ------------------------------------------------------------------

    [Fact]
    public void Escalation_Strike1_KeepsRuleAction()
    {
        var rules = Rules();
        rules.BannedWordList.Add("badword");

        var verdict = MakeEngine(rules).Evaluate(Msg("hit badword one", username: "ladder"));

        Assert.NotNull(verdict);
        Assert.Equal(ModerationActionKind.Delete, verdict!.Action);
        Assert.Equal(1, verdict.Strikes);
    }

    [Fact]
    public void Escalation_Strike2_UpgradesToTimeout_AtBaseSeconds()
    {
        var rules = Rules();
        rules.BannedWordList.Add("badword");
        var engine = MakeEngine(rules);

        engine.Evaluate(Msg("hit badword one", username: "ladder"));
        var verdict = engine.Evaluate(Msg("hit badword two", username: "ladder"));

        Assert.Equal(ModerationActionKind.Timeout, verdict!.Action);
        Assert.Equal(120, verdict.TimeoutSeconds); // BaseTimeoutSeconds, no doubling yet
        Assert.Equal(2, verdict.Strikes);
    }

    [Fact]
    public void Escalation_Strike3_UpgradesToBan()
    {
        var rules = Rules();
        rules.BannedWordList.Add("badword");
        var engine = MakeEngine(rules);

        engine.Evaluate(Msg("hit badword one", username: "ladder"));
        engine.Evaluate(Msg("hit badword two", username: "ladder"));
        var verdict = engine.Evaluate(Msg("hit badword three", username: "ladder"));

        Assert.Equal(ModerationActionKind.Ban, verdict!.Action);
        Assert.Equal(3, verdict.Strikes);
    }

    [Fact]
    public void Escalation_TimeoutSeconds_Double_PerAdditionalStrike()
    {
        var rules = Rules();
        rules.BannedWordList.Add("badword");
        rules.Escalation.StrikesToTimeout = 2;
        rules.Escalation.StrikesToBan = 5; // leave room for two doublings
        rules.Escalation.BaseTimeoutSeconds = 60;
        var engine = MakeEngine(rules);

        Assert.Equal(ModerationActionKind.Delete,
            engine.Evaluate(Msg("hit badword one", username: "ladder"))!.Action);

        var strike2 = engine.Evaluate(Msg("hit badword two", username: "ladder"));
        Assert.Equal(ModerationActionKind.Timeout, strike2!.Action);
        Assert.Equal(60, strike2.TimeoutSeconds); // 60 × 2^0

        var strike3 = engine.Evaluate(Msg("hit badword three", username: "ladder"));
        Assert.Equal(60 * 2, strike3!.TimeoutSeconds); // 60 × 2^1

        var strike4 = engine.Evaluate(Msg("hit badword four", username: "ladder"));
        Assert.Equal(60 * 4, strike4!.TimeoutSeconds); // 60 × 2^2
    }

    [Fact]
    public void Escalation_RuleActionBan_IsNeverDowngraded()
    {
        var rules = Rules();
        rules.BannedWordList.Add("badword");
        rules.BannedWords.Action = ModerationActionKind.Ban;
        var engine = MakeEngine(rules);

        var strike1 = engine.Evaluate(Msg("hit badword one", username: "ladder"));
        var strike2 = engine.Evaluate(Msg("hit badword two", username: "ladder"));

        Assert.Equal(ModerationActionKind.Ban, strike1!.Action);
        // The ladder's timeout rung must not soften an explicit ban action.
        Assert.Equal(ModerationActionKind.Ban, strike2!.Action);
    }

    [Fact]
    public void Escalation_Disabled_StrikesNeverUpgradeTheAction()
    {
        var rules = Rules();
        rules.BannedWordList.Add("badword");
        rules.Escalation.Enabled = false;
        var engine = MakeEngine(rules);

        engine.Evaluate(Msg("hit badword one", username: "ladder"));
        engine.Evaluate(Msg("hit badword two", username: "ladder"));
        var strike3 = engine.Evaluate(Msg("hit badword three", username: "ladder"));

        Assert.Equal(ModerationActionKind.Delete, strike3!.Action);
        Assert.Equal(3, strike3.Strikes); // strikes still counted (for the mod log)
    }

    [Fact]
    public void ResetViewer_RestartsTheLadder()
    {
        var rules = Rules();
        rules.BannedWordList.Add("badword");
        var engine = MakeEngine(rules);

        engine.Evaluate(Msg("hit badword one", username: "ladder"));
        engine.Evaluate(Msg("hit badword two", username: "ladder")); // timeout rung reached
        engine.ResetViewer("Twitch", "ladder");
        var verdict = engine.Evaluate(Msg("hit badword three", username: "ladder"));

        Assert.NotNull(verdict);
        Assert.Equal(1, verdict!.Strikes);
        Assert.Equal(ModerationActionKind.Delete, verdict.Action);
    }

    [Fact]
    public void ResetAll_ClearsEveryLadder()
    {
        var rules = Rules();
        rules.BannedWordList.Add("badword");
        var engine = MakeEngine(rules);

        engine.Evaluate(Msg("hit badword one", username: "ladder"));
        engine.Evaluate(Msg("hit badword two", username: "ladder"));
        engine.ResetAll();

        Assert.Equal(0, engine.TrackedViewerCount);
        Assert.Equal(1, engine.Evaluate(Msg("hit badword three", username: "ladder"))!.Strikes);
    }

    // ------------------------------------------------------------------
    // Bounded viewer state
    // ------------------------------------------------------------------

    [Fact]
    public void TrackedViewers_NeverExceedTheHardCap()
    {
        var engine = MakeEngine(Rules());

        // 5001 distinct viewers: the 5001st must trigger the sweep, never grow
        // the table past ChatModerationEngine.MaxTrackedViewers. Fresh rows
        // are not stale, so the sweep falls through to the wholesale clear.
        for (var i = 0; i < ChatModerationEngine.MaxTrackedViewers + 1; i++)
        {
            engine.Evaluate(Msg($"hello {i}", username: $"viewer{i}"));
        }

        Assert.InRange(engine.TrackedViewerCount, 0, ChatModerationEngine.MaxTrackedViewers);
    }
}
