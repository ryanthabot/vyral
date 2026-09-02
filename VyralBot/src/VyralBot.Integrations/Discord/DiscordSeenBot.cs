using System.Text.RegularExpressions;

namespace VyralBot.Integrations.Discord;

/// <summary>
/// One distinct external bot (BotGhost, MEE6, Dyno, ...) observed in one of
/// our bot's servers. Entries come primarily from live gateway messages (any
/// bot-authored message refreshes the entry) and are seeded best-effort from
/// the guild member cache — partial by design, because the gateway client
/// runs without the GuildMembers intent. Our own account is never included.
/// </summary>
/// <param name="BotId">Snowflake ID of the bot account (string form).</param>
/// <param name="Username">Discord username of the bot account ("Unknown" when unavailable).</param>
/// <param name="DisplayName">Guild display name (nickname) when known, else null.</param>
/// <param name="LastSeenUtc">UTC time of the most recent observation (message or cache seed).</param>
/// <param name="GuildName">Name of the guild where last observed; null for direct messages/unknown.</param>
public record DiscordSeenBot(
    string BotId,
    string Username,
    string? DisplayName,
    DateTime LastSeenUtc,
    string? GuildName);

/// <summary>
/// Extracts command names from another bot's reply (typically its help
/// output) so Vyral can mirror external bots' command surfaces. Recognizes:
/// <list type="bullet">
/// <item>Discord slash-command mentions: <c>&lt;/help:123&gt;</c> → "help",
/// <c>&lt;/config set prefix:123&gt;</c> → "config set prefix"</item>
/// <item>Classic prefix tokens: <c>!help</c>, <c>.music</c>, <c>?rank</c>, <c>/ping</c></item>
/// <item>Backticked names: <c>`ban`</c> → "ban", <c>`!warn`</c> → "warn",
/// <c>`.music play`</c> → "music" (first word when prefixed)</item>
/// </list>
/// URLs and pure numbers are ignored; results are distinct (case-insensitive)
/// and ordered by first appearance in the reply. Purely syntactic — this
/// never queries Discord.
/// </summary>
public static class DiscordCommandNameParser
{
    /// <summary>
    /// Anything URL-shaped. Removed before scanning so paths and queries
    /// ("/docs", "?page=2") can never masquerade as commands.
    /// </summary>
    private static readonly Regex UrlRegex = new(
        @"(?:https?://|www\.)[^\s<>`]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    /// <summary>
    /// One left-to-right pass with three alternatives, so results keep their
    /// first-appearance order and consumed spans (slash mentions, backtick
    /// spans) are never double-scanned by the prefix alternative. The
    /// backtick alternative requires a single backtick on each side (not
    /// adjacent to another backtick) so triple/double code fences are never
    /// mistaken for an inline code span.
    /// </summary>
    private static readonly Regex TokenRegex = new(
        @"(?<slash></(?<sname>[A-Za-z0-9_-]{1,32}(?:\s+[A-Za-z0-9_-]{1,32})*):\d{5,25}>)"
        + @"|(?<prefix>(?<![A-Za-z0-9_-])[!?./](?<pname>[A-Za-z0-9_-]{2,32})(?![A-Za-z0-9_-]))"
        + @"|(?<tick>(?<!`)`(?<tname>[^`\r\n]{2,64})`(?!`))",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Parse the command names out of a bot reply. Returns distinct names in
    /// order of first appearance; empty/whitespace input yields an empty list.
    /// Never throws.
    /// </summary>
    public static IReadOnlyList<string> ParseCommandNames(string botReply)
    {
        var names = new List<string>();
        if (string.IsNullOrWhiteSpace(botReply))
        {
            return names;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var text = UrlRegex.Replace(botReply, " ");

        foreach (Match match in TokenRegex.Matches(text))
        {
            string candidate;
            bool singleTokenOnly;
            if (match.Groups["slash"].Success)
            {
                // </help:123> -> "help"; </config set prefix:123> -> "config set prefix"
                candidate = match.Groups["sname"].Value.Trim();
                singleTokenOnly = false;
            }
            else if (match.Groups["prefix"].Success)
            {
                // !help .music ?rank /ping — the regex already guarantees the
                // 2-32 charset and standalone-token boundaries.
                candidate = match.Groups["pname"].Value;
                singleTokenOnly = true;
            }
            else
            {
                // `ban` -> "ban"; `!warn` -> "warn"; `.music play` -> "music".
                // Multi-word spans WITHOUT a leading prefix char are skipped —
                // they are usually prose ("`click here`"), not a command.
                candidate = match.Groups["tname"].Value.Trim();
                var firstWord = candidate.Split(' ')[0];
                if (firstWord.Length > 0 && firstWord[0] is '!' or '.' or '?' or '/')
                {
                    candidate = firstWord[1..];
                }

                singleTokenOnly = true;
            }

            var isValid = singleTokenOnly
                ? IsValidCommandToken(candidate)
                : IsValidCommandName(candidate);
            if (isValid && seen.Add(candidate))
            {
                names.Add(candidate);
            }
        }

        return names;
    }

    /// <summary>
    /// A slash-mention candidate may span up to three whitespace-separated
    /// tokens (command / group / subcommand); every part must be a valid
    /// single command token.
    /// </summary>
    private static bool IsValidCommandName(string candidate)
    {
        if (candidate.Length == 0)
        {
            return false;
        }

        var parts = candidate.Split(' ');
        if (parts.Length > 3)
        {
            return false;
        }

        foreach (var part in parts)
        {
            if (!IsValidCommandToken(part))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// A single command token: 2-32 chars of ASCII letters/digits/-/_
    /// containing at least one letter (rejects pure numbers like "123" and
    /// punctuation spans like "--").
    /// </summary>
    private static bool IsValidCommandToken(string token)
    {
        if (token.Length is < 2 or > 32)
        {
            return false;
        }

        var hasLetter = false;
        foreach (var c in token)
        {
            if (c is >= 'a' and <= 'z' or >= 'A' and <= 'Z')
            {
                hasLetter = true;
            }
            else if (!char.IsAsciiDigit(c) && c != '-' && c != '_')
            {
                return false;
            }
        }

        return hasLetter;
    }
}
