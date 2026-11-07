using System.Text;
using System.Text.RegularExpressions;

namespace VyralBot.UI.Services;

/// <summary>
/// Crash-report formatting with secret scrubbing (crash pipeline).
///
/// Exception messages frequently embed credentials: OAuth redirect/callback
/// URLs carry <c>access_token=…</c>, RTMP URLs end in the stream key, IRC/chat
/// libraries leak <c>oauth:…</c> bot tokens, and webhook URLs carry
/// <c>token=…</c> query parameters. Every exception surfaced to the UI
/// (<see cref="Views.ErrorDialog"/>) or logged by the global handlers in
/// <c>App.xaml.cs</c> goes through <see cref="Format"/> first so a user-submitted
/// crash report never contains a live credential (golden rule 4: never log
/// secrets). Scrubbing is deliberately over-broad — a redacted report is
/// still diagnosable; a leaked stream key is not recoverable.
/// </summary>
public static partial class CrashReport
{
    /// <summary>Replacement token written in place of every matched secret.</summary>
    private const string Redacted = "***REDACTED***";

    /// <summary>
    /// <c>Authorization: …</c> headers — the whole credential rest-of-line is
    /// secret, whatever the scheme (Bearer, Basic, OAuth…). Runs BEFORE the
    /// named-assignment pattern, which would otherwise consume only the
    /// scheme word and leave the token itself exposed.
    /// </summary>
    [GeneratedRegex(@"(?i)(authorization\s*[:=]\s*)[^\r\n]+", RegexOptions.CultureInvariant)]
    private static partial Regex AuthorizationHeaderRegex();

    /// <summary><c>Bearer …</c> values appearing outside a header line.</summary>
    [GeneratedRegex(@"(?i)bearer\s+[A-Za-z0-9\-._~+/]+=*", RegexOptions.CultureInvariant)]
    private static partial Regex BearerRegex();

    /// <summary>Twitch IRC password form <c>oauth:xxxxxxxxxx</c>.</summary>
    [GeneratedRegex(@"(?i)oauth:[A-Za-z0-9]+", RegexOptions.CultureInvariant)]
    private static partial Regex OauthPrefixRegex();

    /// <summary>
    /// RTMP(S) ingest URLs — everything after <c>rtmp://host/app/</c> is the
    /// stream key. The scheme+host+app prefix is kept (useful for diagnosis,
    /// contains no secret); the key segment is redacted. Requires the
    /// trailing slash so a bare <c>rtmp://host/app</c> (no key) is untouched.
    /// </summary>
    [GeneratedRegex(@"(?i)(rtmps?://[^\s/&?]+/[^\s/&?]+/)[^\s""']+", RegexOptions.CultureInvariant)]
    private static partial Regex RtmpKeyRegex();

    /// <summary>
    /// Named credential assignments: <c>access_token=…</c> (inline, with an
    /// optional quoting character before the value) and
    /// <c>?access_token=…</c>/<c>&amp;token=…</c> (query string — the
    /// separator and name are preserved, only the value is redacted).
    /// Longest names first so <c>access_token</c> wins over generic
    /// <c>token</c>.
    /// </summary>
    [GeneratedRegex(
        @"(?i)(?:(access_token|refresh_token|client_secret|bot[_-]?token|api[_-]?key|stream[_-]?key|auth[_-]?token|webhook[_-]?token|password|secret|token|authorization)\s*[=:]\s*[""']?|([?&])(access_token|refresh_token|client_secret|api[_-]?key|stream[_-]?key|secret|token|key)=)([^\s&""',;)]+)",
        RegexOptions.CultureInvariant)]
    private static partial Regex NamedSecretRegex();

    /// <summary>
    /// Removes known secret patterns from arbitrary text (exception message,
    /// URL, header dump). Null-safe; returns the input unchanged when empty.
    /// Order matters: the Authorization-header pass must run before the named
    /// assignments (see <see cref="AuthorizationHeaderRegex"/>).
    /// </summary>
    public static string Scrub(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var result = AuthorizationHeaderRegex().Replace(text, $"$1{Redacted}");
        result = BearerRegex().Replace(result, $"Bearer {Redacted}");
        result = OauthPrefixRegex().Replace(result, $"oauth:{Redacted}");
        result = RtmpKeyRegex().Replace(result, $"$1{Redacted}");
        result = NamedSecretRegex().Replace(result, m =>
        {
            // Keep the name (and its query-string separator), redact the
            // value. Groups: 1 = inline "name = value" name, 2+3 = the
            // "?"/"&" and name of a query-string parameter, 4 = the value.
            var prefix = m.Groups[1].Success
                ? $"{m.Groups[1].Value}="
                : $"{m.Groups[2].Value}{m.Groups[3].Value}=";
            return $"{prefix}{Redacted}";
        });
        return result;
    }

    /// <summary>
    /// Formats an exception (type, message, stack, inner-exception chain)
    /// fully scrubbed — safe for both the ErrorDialog display and Serilog
    /// output. Aggregates unwrap their inner exceptions.
    /// </summary>
    public static string Format(Exception? exception)
    {
        if (exception is null)
        {
            return "(no exception)";
        }

        var sb = new StringBuilder();
        AppendChain(sb, exception, includeStack: true);
        return sb.ToString();
    }

    private static void AppendChain(StringBuilder sb, Exception ex, bool includeStack)
    {
        var depth = 0;
        for (var current = ex; current is not null; current = current.InnerException)
        {
            if (current is AggregateException aggregate)
            {
                // Unwrap: the aggregate's own message is just "One or more
                // errors occurred" — the inner exceptions carry the detail.
                sb.AppendLine(depth == 0
                    ? $"{aggregate.GetType().Name}: {Scrub(aggregate.Message)}"
                    : $"--- {aggregate.GetType().Name} (depth {depth}) ---")
                  .AppendLine($"  ({aggregate.InnerExceptions.Count} inner exception(s))");
                for (var i = 0; i < aggregate.InnerExceptions.Count && i < 5; i++)
                {
                    sb.AppendLine($"  --- inner #{i + 1} ---");
                    AppendChain(sb, aggregate.InnerExceptions[i], includeStack: depth == 0);
                }

                return;
            }

            if (depth > 0)
            {
                sb.AppendLine($"--- Inner exception (depth {depth}) ---");
            }

            sb.AppendLine($"{current.GetType().FullName}: {Scrub(current.Message)}");
            if (includeStack && !string.IsNullOrWhiteSpace(current.StackTrace))
            {
                sb.AppendLine(Scrub(current.StackTrace));
            }

            depth++;
        }
    }
}
