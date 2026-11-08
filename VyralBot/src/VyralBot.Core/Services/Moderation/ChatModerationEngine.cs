using System.Text.RegularExpressions;

namespace VyralBot.Core.Services.Moderation;

/// <summary>
/// Synchronous chat-content moderation evaluator: pure CPU work, no I/O, no
/// async — designed to be called for EVERY incoming chat message on every
/// platform. Returns null for clean messages. Action execution (and its
/// graceful per-platform degradation) lives in <see cref="ModerationActionService"/>.
/// </summary>
public interface IChatModerationEngine
{
    /// <summary>Currently applied (frozen) rule snapshot.</summary>
    ModerationRuleSet Rules { get; }

    /// <summary>
    /// Atomically swaps the rule snapshot. The set is normalized, cached
    /// (banned/allowlist lookups precomputed) and treated as frozen afterwards.
    /// </summary>
    void ApplyRules(ModerationRuleSet rules);

    /// <summary>
    /// Evaluates one message. Null = clean. On a hit the viewer's strike count
    /// (escalation state, per viewer per platform) is incremented and the
    /// returned action already reflects the escalation ladder.
    /// </summary>
    ModerationVerdict? Evaluate(ModerationIncomingMessage message);

    /// <summary>Number of viewers currently tracked (repeat/escalation state).</summary>
    int TrackedViewerCount { get; }

    /// <summary>Clears one viewer's repeat + escalation state (e.g. after an unban).</summary>
    void ResetViewer(string platform, string username);

    /// <summary>Clears ALL viewer state (dashboard "reset escalation" button).</summary>
    void ResetAll();

    /// <summary>
    /// Records a manual moderator warning (!warn) for the viewer and advances
    /// the SAME escalation ladder as rule violations — the same strike store,
    /// the same decay window, the same timeout/ban rungs. Returns the ladder's
    /// answer for the new strike count: a Timeout/Ban action means the manual
    /// warn tipped the viewer onto that rung and the caller should fire it
    /// through the normal action pipeline immediately; a Warn action means the
    /// next rule violation (or warn) escalates naturally. Works even while
    /// <see cref="Evaluate"/> is master-switched off — a manual warn is a
    /// moderator decision, not a detection. The engine stays I/O-free, so the
    /// <paramref name="reason"/> is carried for the caller's audit log only.
    /// </summary>
    LadderAction RegisterManualWarning(string username, string platform, string reason);

    /// <summary>
    /// The viewer's live strike count: 0 for unknown viewers and for counts
    /// fully decayed past the escalation window. Read-only (no mutation).
    /// </summary>
    int GetStrikeCount(string platform, string username);

    /// <summary>
    /// The action the escalation ladder prescribes when a viewer reaches
    /// <paramref name="strikes"/> strikes. Manual warns enter the ladder with
    /// a Warn base; rule violations with their rule's action — a rule action
    /// already harsher than the rung is never downgraded by
    /// <see cref="Evaluate"/>.
    /// </summary>
    LadderAction ResolveLadderAction(int strikes);
}

/// <summary>
/// Default <see cref="IChatModerationEngine"/>. Performance contract for the
/// per-message hot path:
///  - the ONLY regex is the link detector, a static compiled matcher;
///  - banned words / allowlists are precomputed HashSet/List caches rebuilt
///    only when <see cref="ApplyRules"/> swaps the snapshot (never per message);
///  - caps / emoji / ASCII-art checks are single linear char scans with no
///    allocations beyond small reason strings (built only on a hit);
///  - per-viewer state (repeat tracking + strikes) is a bounded dictionary
///    (see <see cref="MaxTrackedViewers"/>) guarded by one lock; the lock is
///    held for O(1) work only.
/// </summary>
public sealed class ChatModerationEngine : IChatModerationEngine
{
    /// <summary>Hard cap on tracked viewers; beyond it stale entries are swept.</summary>
    public const int MaxTrackedViewers = 5000;

    private const int MaxTimeoutSeconds = 604800; // 7 days, Twitch/Kick ceiling
    private const int MaxTimeoutDoublings = 6;

    private static readonly Regex LinkPattern = new(
        @"(?:https?://|www\.)[^\s]+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// Process-wide default engine so the chat pipelines (subscription handlers
    /// in later waves), the Moderator dashboard and tests all see ONE rule set
    /// and ONE escalation state without new DI wiring. DI may still construct
    /// dedicated instances for tests.
    /// </summary>
    public static ChatModerationEngine Shared { get; } = new();

    /// <summary>Immutable per-snapshot lookup cache; swapped atomically on ApplyRules.</summary>
    private sealed class RuleCache
    {
        public required ModerationRuleSet Rules { get; init; }
        public required HashSet<string> BannedWords { get; init; }
        public required List<string> BannedPhrases { get; init; }
        public required HashSet<string> WordAllowlist { get; init; }
        public required HashSet<string> ExemptUsers { get; init; }
        public required List<string> LinkDomains { get; init; }

        public HashSet<string> Platforms { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Per-viewer repeat + escalation state.</summary>
    private sealed class ViewerState
    {
        public string LastMessage = "";
        public int RepeatCount;
        public DateTime LastSeenUtc;
        public int Strikes;
        public DateTime LastStrikeUtc;
    }

    private readonly object _stateGate = new();
    private readonly Dictionary<string, ViewerState> _viewers =
        new(StringComparer.OrdinalIgnoreCase); // key = platform + '\u001F' + username

    private volatile RuleCache _cache = BuildCache(ModerationRuleSet.CreateDefault());

    public ModerationRuleSet Rules => _cache.Rules;

    public int TrackedViewerCount
    {
        get
        {
            lock (_stateGate)
            {
                return _viewers.Count;
            }
        }
    }

    public void ApplyRules(ModerationRuleSet rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        var normalized = rules.Clone();
        normalized.Normalize();
        _cache = BuildCache(normalized);
    }

    public ModerationVerdict? Evaluate(ModerationIncomingMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        var cache = _cache;
        var ruleSet = cache.Rules;
        if (!ruleSet.MasterEnabled)
        {
            return null;
        }

        if (cache.Platforms.Count > 0 && !cache.Platforms.Contains(message.Platform))
        {
            return null;
        }

        var text = message.Message?.Trim() ?? "";
        if (text.Length == 0)
        {
            return null;
        }

        // The broadcaster is never auto-moderated; explicit user exemptions next.
        if (message.IsBroadcaster || cache.ExemptUsers.Contains(message.Username))
        {
            return null;
        }

        // Repeat tracking runs for every message (clean ones too) so the
        // repeated-message rule sees the true streak.
        var repeatStreak = TrackRepeat(message.Platform, message.Username, text, ruleSet.RepeatedMessage);

        // Priority: banned words → links → caps → emojis → repeats → ASCII art.
        if (ruleSet.BannedWords.Enabled && !IsExempt(message, ruleSet.BannedWords) &&
            TryMatchBannedWord(cache, text, out var bannedHit))
        {
            return Strike(message, ModerationRuleType.BannedWords, ruleSet.BannedWords,
                $"banned word '{Truncate(bannedHit, 40)}'");
        }

        if (ruleSet.Links.Enabled && !IsExempt(message, ruleSet.Links) &&
            TryMatchDisallowedLink(cache, text, ruleSet.Links.Threshold, out var badHost))
        {
            return Strike(message, ModerationRuleType.Links, ruleSet.Links,
                $"link not allowlisted: {badHost}");
        }

        if (ruleSet.Caps.Enabled && !IsExempt(message, ruleSet.Caps) &&
            MatchesCaps(text, ruleSet.Caps, out var capsRatio))
        {
            return Strike(message, ModerationRuleType.Caps, ruleSet.Caps,
                $"excessive caps ({capsRatio}% ≥ {ruleSet.Caps.Threshold}%)");
        }

        if (ruleSet.Emojis.Enabled && !IsExempt(message, ruleSet.Emojis))
        {
            var emojiCount = CountEmojis(text);
            if (emojiCount > ruleSet.Emojis.Threshold)
            {
                return Strike(message, ModerationRuleType.Emojis, ruleSet.Emojis,
                    $"{emojiCount} emojis (limit {ruleSet.Emojis.Threshold})");
            }
        }

        if (ruleSet.RepeatedMessage.Enabled && !IsExempt(message, ruleSet.RepeatedMessage) &&
            repeatStreak >= ruleSet.RepeatedMessage.Threshold)
        {
            return Strike(message, ModerationRuleType.RepeatedMessage, ruleSet.RepeatedMessage,
                $"message repeated {repeatStreak}× within {ruleSet.RepeatedMessage.MinLength}s");
        }

        if (ruleSet.AsciiArt.Enabled && !IsExempt(message, ruleSet.AsciiArt) &&
            MatchesAsciiArt(text, ruleSet.AsciiArt, out var artRatio))
        {
            return Strike(message, ModerationRuleType.AsciiArt, ruleSet.AsciiArt,
                $"ASCII art ({artRatio}% ≥ {ruleSet.AsciiArt.Threshold}%)");
        }

        return null;
    }

    public void ResetViewer(string platform, string username)
    {
        lock (_stateGate)
        {
            _viewers.Remove(BuildKey(platform, username));
        }
    }

    public void ResetAll()
    {
        lock (_stateGate)
        {
            _viewers.Clear();
        }
    }

    // ------------------------------------------------------------------
    // manual warnings (the !warn command surface — shares the ladder above)
    // ------------------------------------------------------------------

    public LadderAction RegisterManualWarning(string username, string platform, string reason)
    {
        // The engine is I/O-free by contract — the reason never touches state;
        // the caller (WarningCommandService) logs + audits it.
        _ = reason ?? "";

        var escalation = _cache.Rules.Escalation;
        int strikes;

        lock (_stateGate)
        {
            var key = BuildKey(platform, username);
            if (!_viewers.TryGetValue(key, out var state))
            {
                if (_viewers.Count >= MaxTrackedViewers)
                {
                    SweepStaleViewers(nowUtc: DateTime.UtcNow);
                }

                state = new ViewerState { LastSeenUtc = DateTime.UtcNow };
                _viewers[key] = state;
            }

            // Identical decay + increment path as a rule-violation strike, so
            // manual warns and auto-mod hits are indistinguishable downstream.
            var now = DateTime.UtcNow;
            var window = TimeSpan.FromMinutes(Math.Max(1, escalation.StrikeWindowMinutes));
            if (state.Strikes > 0 && now - state.LastStrikeUtc > window)
            {
                state.Strikes = 0;
            }

            state.Strikes++;
            state.LastStrikeUtc = now;
            strikes = state.Strikes;
        }

        return ResolveLadder(escalation, strikes);
    }

    public int GetStrikeCount(string platform, string username)
    {
        var escalation = _cache.Rules.Escalation;
        lock (_stateGate)
        {
            if (!_viewers.TryGetValue(BuildKey(platform, username), out var state))
            {
                return 0;
            }

            // Read-only decay view: expired counts read as 0 without mutating
            // the stored state (the next strike decays it for real).
            var window = TimeSpan.FromMinutes(Math.Max(1, escalation.StrikeWindowMinutes));
            return state.Strikes > 0 && DateTime.UtcNow - state.LastStrikeUtc > window
                ? 0
                : state.Strikes;
        }
    }

    public LadderAction ResolveLadderAction(int strikes)
        => ResolveLadder(_cache.Rules.Escalation, Math.Max(0, strikes));

    /// <summary>
    /// The single ladder resolver shared by rule violations (<see cref="Strike"/>)
    /// and manual warns (<see cref="RegisterManualWarning"/>): Warn base,
    /// Timeout at <c>StrikesToTimeout</c> (base seconds doubling per extra
    /// strike), Ban at <c>StrikesToBan</c>. Callers keep actions already
    /// harsher than the rung.
    /// </summary>
    private static LadderAction ResolveLadder(ModerationEscalationConfig escalation, int strikes)
    {
        var action = ModerationActionKind.Warn;
        var timeoutSeconds = Math.Clamp(escalation.BaseTimeoutSeconds, 1, MaxTimeoutSeconds);

        if (escalation.Enabled)
        {
            if (strikes >= escalation.StrikesToBan)
            {
                action = ModerationActionKind.Ban;
            }
            else if (strikes >= escalation.StrikesToTimeout)
            {
                var doublings = Math.Min(strikes - escalation.StrikesToTimeout, MaxTimeoutDoublings);
                timeoutSeconds = Math.Clamp(
                    escalation.BaseTimeoutSeconds * (1 << doublings),
                    1,
                    MaxTimeoutSeconds);
                action = ModerationActionKind.Timeout;
            }
        }

        return new LadderAction(strikes, action, timeoutSeconds);
    }

    // ------------------------------------------------------------------
    // rule checks (all allocation-light; strings only built on a hit)
    // ------------------------------------------------------------------

    private static bool IsExempt(ModerationIncomingMessage message, ModerationRuleConfig rule)
        => (message.IsModerator && rule.ExemptModerators)
           || (message.IsVip && rule.ExemptVips)
           || (message.IsSubscriber && rule.ExemptSubscribers);

    private static bool TryMatchBannedWord(RuleCache cache, string text, out string hit)
    {
        // Phrases (multi-word) use ordinal-ignore-case substring matching;
        // single words must appear as a whole word — cheap boundary scan.
        foreach (var phrase in cache.BannedPhrases)
        {
            if (text.Contains(phrase, StringComparison.OrdinalIgnoreCase))
            {
                hit = phrase;
                return true;
            }
        }

        if (cache.BannedWords.Count > 0)
        {
            var start = 0;
            while (start < text.Length)
            {
                // skip separators
                while (start < text.Length && IsSeparator(text[start]))
                {
                    start++;
                }

                var end = start;
                while (end < text.Length && !IsSeparator(text[end]))
                {
                    end++;
                }

                if (end > start)
                {
                    var word = text.Substring(start, end - start);
                    if (cache.BannedWords.Contains(word))
                    {
                        // Word allowlist overrides the ban list.
                        if (!cache.WordAllowlist.Contains(word))
                        {
                            hit = word;
                            return true;
                        }
                    }
                }

                start = end + 1;
            }
        }

        hit = "";
        return false;
    }

    private static bool IsSeparator(char c)
        => char.IsWhiteSpace(c) || c is ',' or '.' or '!' or '?' or ';' or ':' or '\'' or '"'
            or '(' or ')' or '[' or ']' or '{' or '}' or '-' or '_' or '/' or '\\' or '*' or '|' or '<' or '>';

    private static bool TryMatchDisallowedLink(RuleCache cache, string text, int allowedCount, out string badHost)
    {
        var disallowed = 0;
        badHost = "";
        var matches = LinkPattern.Matches(text);
        if (matches.Count == 0)
        {
            return false;
        }

        foreach (Match match in matches)
        {
            var host = ExtractHost(match.Value);
            if (host.Length == 0)
            {
                continue;
            }

            if (IsAllowlistedHost(host, cache.LinkDomains))
            {
                continue;
            }

            disallowed++;
            badHost = host;
            if (disallowed > allowedCount)
            {
                return true;
            }
        }

        return false;
    }

    private static string ExtractHost(string link)
    {
        var rest = link;
        var schemeIndex = rest.IndexOf("://", StringComparison.Ordinal);
        if (schemeIndex >= 0)
        {
            rest = rest.Substring(schemeIndex + 3);
        }
        else if (rest.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
        {
            rest = rest.Substring(4);
        }

        var cut = rest.IndexOfAny(new[] { '/', '?', '#', ':' });
        if (cut >= 0)
        {
            rest = rest.Substring(0, cut);
        }

        return rest.ToLowerInvariant().TrimEnd('.');
    }

    private static bool IsAllowlistedHost(string host, List<string> domains)
    {
        foreach (var domain in domains)
        {
            if (host.Equals(domain, StringComparison.OrdinalIgnoreCase) ||
                host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool MatchesCaps(string text, ModerationRuleConfig rule, out int capsPercent)
    {
        capsPercent = 0;
        if (text.Length < rule.MinLength)
        {
            return false;
        }

        var letters = 0;
        var upper = 0;
        foreach (var c in text)
        {
            if (!char.IsLetter(c))
            {
                continue;
            }

            letters++;
            if (char.IsUpper(c))
            {
                upper++;
            }
        }

        if (letters < rule.MinLength)
        {
            return false;
        }

        capsPercent = upper * 100 / letters;
        return capsPercent >= rule.Threshold;
    }

    private static int CountEmojis(string text)
    {
        var count = 0;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (char.IsHighSurrogate(c))
            {
                if (i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                {
                    // Emoji planes: emoticons onward (U+1F000+). Skip CJK ext etc.
                    // by only counting the commonly spammed U+1F000–U+1FBFF range.
                    var code = char.ConvertToUtf32(c, text[i + 1]);
                    if (code is >= 0x1F000 and <= 0x1FBFF)
                    {
                        count++;
                    }

                    i++; // consume the low surrogate
                }
            }
            else if (IsBasicEmoji(c))
            {
                count++;
            }
        }

        return count;
    }

    private static bool IsBasicEmoji(char c)
        => c is >= '\u2600' and <= '\u27BF'    // misc symbols + dingbats
            or >= '\u2B00' and <= '\u2BFF'     // stars, misc symbols-arrows
            or >= '\u2190' and <= '\u21FF';    // arrows (decorative spam)

    private static bool MatchesAsciiArt(string text, ModerationRuleConfig rule, out int artPercent)
    {
        artPercent = 0;
        if (text.Length < rule.MinLength)
        {
            return false;
        }

        var artChars = 0;
        foreach (var c in text)
        {
            // Box drawing (U+2500–257F), block elements (2580–259F),
            // geometric shapes (25A0–25FF) — the classic ASCII-art palette.
            if (c is >= '\u2500' and <= '\u25FF')
            {
                artChars++;
            }
        }

        artPercent = artChars * 100 / text.Length;
        return artPercent >= rule.Threshold;
    }

    // ------------------------------------------------------------------
    // per-viewer state (repeat streaks + escalation strikes)
    // ------------------------------------------------------------------

    private int TrackRepeat(string platform, string username, string text, ModerationRuleConfig rule)
    {
        var windowSeconds = Math.Max(5, rule.MinLength);
        var key = BuildKey(platform, username);
        var now = DateTime.UtcNow;

        lock (_stateGate)
        {
            if (!_viewers.TryGetValue(key, out var state))
            {
                if (_viewers.Count >= MaxTrackedViewers)
                {
                    SweepStaleViewers(nowUtc: now);
                }

                state = new ViewerState();
                _viewers[key] = state;
            }

            // Capture the PREVIOUS message time before overwriting it — the
            // repeat window is measured between consecutive identical messages.
            var previousSeenUtc = state.LastSeenUtc;
            state.LastSeenUtc = now;
            if (previousSeenUtc != default &&
                string.Equals(state.LastMessage, text, StringComparison.OrdinalIgnoreCase) &&
                (now - previousSeenUtc).TotalSeconds <= windowSeconds)
            {
                state.RepeatCount++;
            }
            else
            {
                state.RepeatCount = 1;
            }

            state.LastMessage = text;
            return state.RepeatCount;
        }
    }

    private ModerationVerdict Strike(
        ModerationIncomingMessage message,
        ModerationRuleType ruleType,
        ModerationRuleConfig rule,
        string reason)
    {
        var escalation = _cache.Rules.Escalation;
        int strikes;
        var action = rule.Action;
        var timeoutSeconds = Math.Clamp(rule.TimeoutSeconds, 1, MaxTimeoutSeconds);

        lock (_stateGate)
        {
            var state = _viewers.TryGetValue(BuildKey(message.Platform, message.Username), out var found)
                ? found
                : null;
            var now = DateTime.UtcNow;
            if (state is null)
            {
                state = new ViewerState { LastSeenUtc = now };
                _viewers[BuildKey(message.Platform, message.Username)] = state;
            }

            // Strike decay: strikes older than the window stop counting.
            var window = TimeSpan.FromMinutes(Math.Max(1, escalation.StrikeWindowMinutes));
            if (state.Strikes > 0 && now - state.LastStrikeUtc > window)
            {
                state.Strikes = 0;
            }

            state.Strikes++;
            state.LastStrikeUtc = now;
            strikes = state.Strikes;
        }

        if (escalation.Enabled)
        {
            // Same resolver the manual-warn path uses — one ladder, no drift.
            // A rule action already harsher than the rung is kept (Ban stays
            // Ban; a rule Timeout keeps Timeout, adopting the rung duration).
            var ladder = ResolveLadder(escalation, strikes);
            if (ladder.Action == ModerationActionKind.Ban)
            {
                action = ModerationActionKind.Ban;
            }
            else if (ladder.Action == ModerationActionKind.Timeout &&
                     action is not ModerationActionKind.Ban)
            {
                timeoutSeconds = ladder.TimeoutSeconds;
                if (action is not ModerationActionKind.Timeout)
                {
                    action = ModerationActionKind.Timeout;
                }
            }
        }

        if (action == ModerationActionKind.Timeout)
        {
            timeoutSeconds = Math.Clamp(timeoutSeconds, 1, MaxTimeoutSeconds);
        }

        return new ModerationVerdict(ruleType, action, timeoutSeconds, reason, strikes);
    }

    /// <summary>
    /// Bounded-memory sweep: drops viewers idle for over an hour; if the cap is
    /// still reached ( pathological raid) the table is cleared wholesale — the
    /// cost is only repeat-streak/escalation history, never correctness.
    /// </summary>
    private void SweepStaleViewers(DateTime nowUtc)
    {
        var stale = new List<string>();
        foreach (var pair in _viewers)
        {
            if (nowUtc - pair.Value.LastSeenUtc > TimeSpan.FromHours(1))
            {
                stale.Add(pair.Key);
            }
        }

        foreach (var key in stale)
        {
            _viewers.Remove(key);
        }

        if (_viewers.Count >= MaxTrackedViewers)
        {
            _viewers.Clear();
        }
    }

    private static string BuildKey(string platform, string username)
        => (platform ?? "").Trim() + '\u001F' + (username ?? "").Trim();

    private static string Truncate(string value, int maxLength)
        => value.Length <= maxLength ? value : value.Substring(0, maxLength) + "…";

    private static RuleCache BuildCache(ModerationRuleSet rules)
    {
        var bannedWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var bannedPhrases = new List<string>();
        foreach (var entry in rules.BannedWordList)
        {
            var trimmed = entry.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            if (trimmed.IndexOf(' ') >= 0)
            {
                bannedPhrases.Add(trimmed);
            }
            else
            {
                bannedWords.Add(trimmed);
            }
        }

        return new RuleCache
        {
            Rules = rules,
            BannedWords = bannedWords,
            BannedPhrases = bannedPhrases,
            WordAllowlist = new HashSet<string>(rules.WordAllowlist, StringComparer.OrdinalIgnoreCase),
            ExemptUsers = new HashSet<string>(rules.ExemptUsers, StringComparer.OrdinalIgnoreCase),
            LinkDomains = rules.LinkAllowlist,
            Platforms = new HashSet<string>(rules.Platforms, StringComparer.OrdinalIgnoreCase)
        };
    }
}
