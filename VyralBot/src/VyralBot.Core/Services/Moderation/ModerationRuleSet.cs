using System.Text.Json;

namespace VyralBot.Core.Services.Moderation;

/// <summary>Action the auto-moderator takes when a rule triggers.</summary>
public enum ModerationActionKind
{
    /// <summary>Delete the offending message (platform permitting).</summary>
    Delete = 0,

    /// <summary>Post a chat notice / warn the viewer; never removes anything.</summary>
    Warn = 1,

    /// <summary>Time the viewer out for the rule's <see cref="ModerationRuleConfig.TimeoutSeconds"/>.</summary>
    Timeout = 2,

    /// <summary>Ban the viewer (platform permitting).</summary>
    Ban = 3
}

/// <summary>The six built-in chat-content detection rules.</summary>
public enum ModerationRuleType
{
    BannedWords = 0,
    Links = 1,
    Caps = 2,
    Emojis = 3,
    RepeatedMessage = 4,
    AsciiArt = 5,

    /// <summary>
    /// Not a detection rule: marks verdicts born from a manual !warn that
    /// tipped the viewer onto a Timeout/Ban rung (see
    /// <see cref="IChatModerationEngine.RegisterManualWarning"/>).
    /// </summary>
    ManualWarning = 6
}

/// <summary>
/// Per-rule auto-moderation config: enable switch, action, thresholds and the
/// per-rule viewer-class exemptions (mods / VIPs / subscribers). The broadcaster
/// is ALWAYS exempt and needs no flag. All thresholds are clamped by
/// <see cref="ModerationRuleSet.Normalize"/> before an engine applies them.
/// </summary>
public sealed class ModerationRuleConfig
{
    /// <summary>Rule switch. Disabled rules are skipped without cost.</summary>
    public bool Enabled { get; set; }

    /// <summary>Action executed when the rule triggers (before escalation).</summary>
    public ModerationActionKind Action { get; set; } = ModerationActionKind.Delete;

    /// <summary>
    /// Rule-specific threshold:
    /// Caps = max upper-case % of letters; Links = allowed non-allowlisted links;
    /// Emojis = max emojis per message; BannedWords = unused (0);
    /// RepeatedMessage = repeats within the window; AsciiArt = max art-char %.
    /// </summary>
    public int Threshold { get; set; }

    /// <summary>
    /// Minimum message length before the rule applies (Caps, AsciiArt);
    /// for RepeatedMessage it is the repeat window in seconds.
    /// </summary>
    public int MinLength { get; set; }

    /// <summary>Timeout length used when <see cref="Action"/> is Timeout.</summary>
    public int TimeoutSeconds { get; set; } = 60;

    /// <summary>Exempt the channel's moderators.</summary>
    public bool ExemptModerators { get; set; } = true;

    /// <summary>Exempt VIPs (Twitch/Kick badges).</summary>
    public bool ExemptVips { get; set; }

    /// <summary>Exempt subscribers / members.</summary>
    public bool ExemptSubscribers { get; set; }
}

/// <summary>
/// Optional warning → timeout → ban escalation ladder shared by every rule:
/// strike counts are per viewer per platform with a decay window, and once a
/// viewer's live strike count reaches <see cref="StrikesToTimeout"/> /
/// <see cref="StrikesToBan"/> the executed action is upgraded (a rule action
/// already harsher than the ladder rung is kept).
/// </summary>
public sealed class ModerationEscalationConfig
{
    public bool Enabled { get; set; } = true;

    /// <summary>Strikes at which Delete/Warn actions are upgraded to Timeout.</summary>
    public int StrikesToTimeout { get; set; } = 2;

    /// <summary>Strikes at which the action is upgraded to Ban.</summary>
    public int StrikesToBan { get; set; } = 4;

    /// <summary>Strikes older than this many minutes no longer count.</summary>
    public int StrikeWindowMinutes { get; set; } = 15;

    /// <summary>Timeout seconds for the first escalated timeout; doubles per additional strike.</summary>
    public int BaseTimeoutSeconds { get; set; } = 120;
}

/// <summary>
/// The complete serializable auto-moderation config persisted as JSON under the
/// <c>Moderation.AutoModRules</c> config key. Instances handed to
/// <see cref="ChatModerationEngine"/> are treated as frozen snapshots; edit a
/// clone, then re-apply.
/// </summary>
public sealed class ModerationRuleSet
{
    /// <summary>Master switch — when false the engine passes every message.</summary>
    public bool MasterEnabled { get; set; } = true;

    /// <summary>
    /// Platforms auto-mod runs on (case-insensitive; empty list = every platform,
    /// including future ones). TikTok has no enforcement surface today — see
    /// <see cref="ModerationPlatformParity"/>.
    /// </summary>
    public List<string> Platforms { get; set; } = new();

    public ModerationRuleConfig BannedWords { get; set; } = new();
    public ModerationRuleConfig Links { get; set; } = new();
    public ModerationRuleConfig Caps { get; set; } = new();
    public ModerationRuleConfig Emojis { get; set; } = new();
    public ModerationRuleConfig RepeatedMessage { get; set; } = new();
    public ModerationRuleConfig AsciiArt { get; set; } = new();

    public ModerationEscalationConfig Escalation { get; set; } = new();

    /// <summary>Usernames never auto-moderated (case-insensitive).</summary>
    public List<string> ExemptUsers { get; set; } = new();

    /// <summary>
    /// Link domains that never count as violations (host or suffix match,
    /// e.g. "twitch.tv" also allows "clips.twitch.tv").
    /// </summary>
    public List<string> LinkAllowlist { get; set; } = new();

    /// <summary>Words that never count as banned (overrides the banned list).</summary>
    public List<string> WordAllowlist { get; set; } = new();

    /// <summary>Banned words and phrases (case-insensitive substring match).</summary>
    public List<string> BannedWordList { get; set; } = new();

    /// <summary>Factory with safe defaults (banned-word list empty, rule set curated).</summary>
    public static ModerationRuleSet CreateDefault() => new()
    {
        MasterEnabled = false, // opt-in: chat keeps flowing until the streamer enables it
        Platforms = new List<string>(),
        BannedWords = new ModerationRuleConfig
        {
            Enabled = false, // the default banned list is empty — enable after populating it
            Action = ModerationActionKind.Delete,
            Threshold = 0
        },
        Links = new ModerationRuleConfig
        {
            Enabled = true,
            Action = ModerationActionKind.Delete,
            Threshold = 0 // no non-allowlisted links allowed
        },
        Caps = new ModerationRuleConfig
        {
            Enabled = true,
            Action = ModerationActionKind.Delete,
            Threshold = 70, // % upper-case
            MinLength = 12 // ignore short shouts like "LOL"
        },
        Emojis = new ModerationRuleConfig
        {
            Enabled = true,
            Action = ModerationActionKind.Delete,
            Threshold = 12
        },
        RepeatedMessage = new ModerationRuleConfig
        {
            Enabled = true,
            Action = ModerationActionKind.Delete,
            Threshold = 3, // 3rd identical message within the window trips
            MinLength = 60 // window seconds (reused field — see ModerationRuleConfig.MinLength)
        },
        AsciiArt = new ModerationRuleConfig
        {
            Enabled = true,
            Action = ModerationActionKind.Delete,
            Threshold = 30, // % art chars
            MinLength = 20
        },
        Escalation = new ModerationEscalationConfig
        {
            Enabled = true,
            StrikesToTimeout = 2,
            StrikesToBan = 4,
            StrikeWindowMinutes = 15,
            BaseTimeoutSeconds = 120
        }
    };

    /// <summary>Deep clone via JSON round-trip (config-sized payloads only).</summary>
    public ModerationRuleSet Clone()
        => JsonSerializer.Deserialize<ModerationRuleSet>(JsonSerializer.Serialize(this)) ?? CreateDefault();

    /// <summary>Clamps every threshold into a sane range; drops empty list entries.</summary>
    public void Normalize()
    {
        // Backfill: a partial/corrupt JSON payload may deserialize slots as
        // null — Normalize is the single place that guarantees a complete set.
        BannedWords ??= new ModerationRuleConfig();
        Links ??= new ModerationRuleConfig();
        Caps ??= new ModerationRuleConfig();
        Emojis ??= new ModerationRuleConfig();
        RepeatedMessage ??= new ModerationRuleConfig();
        AsciiArt ??= new ModerationRuleConfig();
        Escalation ??= new ModerationEscalationConfig();
        Platforms ??= new List<string>();
        ExemptUsers ??= new List<string>();
        LinkAllowlist ??= new List<string>();
        WordAllowlist ??= new List<string>();
        BannedWordList ??= new List<string>();

        Platforms = NormalizeList(Platforms);
        ExemptUsers = NormalizeList(ExemptUsers);
        LinkAllowlist = NormalizeList(LinkAllowlist);
        WordAllowlist = NormalizeList(WordAllowlist);
        BannedWordList = NormalizeList(BannedWordList);

        NormalizeRule(BannedWords, minThreshold: 0, maxThreshold: 0);
        NormalizeRule(Links, minThreshold: 0, maxThreshold: 10);
        NormalizeRule(Caps, minThreshold: 10, maxThreshold: 100, defaultThreshold: 70, defaultMinLength: 12);
        NormalizeRule(Emojis, minThreshold: 1, maxThreshold: 200, defaultThreshold: 12);
        NormalizeRule(RepeatedMessage, minThreshold: 2, maxThreshold: 20, defaultThreshold: 3, defaultMinLength: 60);
        NormalizeRule(AsciiArt, minThreshold: 5, maxThreshold: 100, defaultThreshold: 30, defaultMinLength: 20);

        Escalation ??= new ModerationEscalationConfig();
        Escalation.StrikesToTimeout = Math.Clamp(Escalation.StrikesToTimeout, 1, 50);
        Escalation.StrikesToBan = Math.Max(Escalation.StrikesToBan, Escalation.StrikesToTimeout + 1);
        Escalation.StrikeWindowMinutes = Math.Clamp(Escalation.StrikeWindowMinutes, 1, 1440);
        Escalation.BaseTimeoutSeconds = Math.Clamp(Escalation.BaseTimeoutSeconds, 1, 604800);
    }

    private static void NormalizeRule(
        ModerationRuleConfig? rule,
        int minThreshold,
        int maxThreshold,
        int defaultThreshold = 0,
        int defaultMinLength = 0)
    {
        if (rule is null)
        {
            return;
        }

        if (maxThreshold > minThreshold)
        {
            rule.Threshold = Math.Clamp(rule.Threshold, minThreshold, maxThreshold);
            if (rule.Threshold == 0 && defaultThreshold > 0)
            {
                rule.Threshold = defaultThreshold;
            }
        }
        else
        {
            rule.Threshold = minThreshold;
        }

        rule.MinLength = Math.Clamp(rule.MinLength, 0, 3600);
        if (rule.MinLength == 0 && defaultMinLength > 0)
        {
            rule.MinLength = defaultMinLength;
        }

        rule.TimeoutSeconds = Math.Clamp(rule.TimeoutSeconds, 1, 604800);
    }

    private static List<string> NormalizeList(List<string>? list)
    {
        if (list is null || list.Count == 0)
        {
            return new List<string>();
        }

        var result = new List<string>(list.Count);
        foreach (var entry in list)
        {
            var trimmed = (entry ?? "").Trim();
            if (trimmed.Length > 0 && !result.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
            {
                result.Add(trimmed);
            }
        }

        return result;
    }
}
