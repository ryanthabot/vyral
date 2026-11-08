using VyralBot.UI.Services;
using Xunit;

namespace VyralBot.UI.Tests;

/// <summary>
/// Wave-6 regression coverage for the crash-report scrubber
/// (<see cref="CrashReport"/>). A user-submitted crash report must never
/// contain a live credential (golden rule 4): OAuth callback URLs, RTMP
/// ingest URLs, IRC oauth: passwords, Authorization/Bearer headers and named
/// credential assignments are all redacted before display/logging — while
/// diagnostic plain text (hosts, paths, ordinary words) survives untouched.
/// </summary>
public class CrashReportScrubberTests
{
    private const string Redacted = "***REDACTED***";

    // ==================================================================
    // Scrub — named credential assignments
    // ==================================================================

    [Theory]
    [InlineData("access_token=abc123XYZ", "access_token", "abc123XYZ")]
    [InlineData("refresh_token=rt_987", "refresh_token", "rt_987")]
    [InlineData("client_secret=shh", "client_secret", "shh")]
    [InlineData("stream_key=live_4444", "stream_key", "live_4444")]
    [InlineData("stream-key=live_4444", "stream-key", "live_4444")]
    [InlineData("password=hunter2", "password", "hunter2")]
    [InlineData("secret=topsecret", "secret", "topsecret")]
    [InlineData("token=tok123", "token", "tok123")]
    public void Scrub_RedactsNamedAssignments(string input, string name, string secret)
    {
        var result = CrashReport.Scrub(input);

        Assert.DoesNotContain(secret, result, StringComparison.Ordinal);
        Assert.Contains($"{name}={Redacted}", result, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ACCESS_TOKEN=abc123")]
    [InlineData("Password=hunter2")]
    [InlineData("STREAM_KEY=live_1")]
    public void Scrub_NamedAssignmentMatch_IsCaseInsensitive(string input)
    {
        var result = CrashReport.Scrub(input);

        Assert.DoesNotContain("abc123", result, StringComparison.Ordinal);
        Assert.DoesNotContain("hunter2", result, StringComparison.Ordinal);
        Assert.DoesNotContain("live_1", result, StringComparison.Ordinal);
        Assert.Contains(Redacted, result, StringComparison.Ordinal);
    }

    [Fact]
    public void Scrub_NamedAssignment_ToleratesSpacesAndQuotes()
    {
        // OAuth-style dump: "stream_key" and quoted value.
        var result = CrashReport.Scrub("stream_key = \"live_9182\"");

        Assert.DoesNotContain("live_9182", result, StringComparison.Ordinal);
        Assert.Contains("stream_key=", result, StringComparison.Ordinal);
        Assert.Contains(Redacted, result, StringComparison.Ordinal);
    }

    [Fact]
    public void Scrub_RedactsQueryStringTokens_PreservingOtherParameters()
    {
        var result = CrashReport.Scrub("https://cb.example.com/oauth?access_token=xyz789&state=abc&foo=bar");

        Assert.DoesNotContain("xyz789", result, StringComparison.Ordinal);
        Assert.Contains($"access_token={Redacted}", result, StringComparison.Ordinal);
        // Non-secret parameters survive — the report stays diagnosable.
        Assert.Contains("state=abc", result, StringComparison.Ordinal);
        Assert.Contains("foo=bar", result, StringComparison.Ordinal);
    }

    [Fact]
    public void Scrub_RedactsAmpersandSeparatedTokenParameter()
    {
        var result = CrashReport.Scrub("https://hooks.example.com/x?a=1&token=webhooksecret9");

        Assert.DoesNotContain("webhooksecret9", result, StringComparison.Ordinal);
        Assert.Contains($"token={Redacted}", result, StringComparison.Ordinal);
        Assert.Contains("a=1", result, StringComparison.Ordinal);
    }

    // ==================================================================
    // Scrub — Authorization headers + Bearer tokens
    // ==================================================================

    [Theory]
    [InlineData("Authorization: Bearer eyJhbGciOiJIUzI1NiJ9.sig.part")]
    [InlineData("Authorization: Basic dXNlcjpwYXNzd29yZA==")]
    [InlineData("authorization: OAuth abcdef123456")]
    public void Scrub_RedactsEntireAuthorizationHeader(string input)
    {
        var result = CrashReport.Scrub(input);

        // The header pass consumes the whole credential rest-of-line; the later
        // named-assignment pass then normalizes the separator, so the final
        // form is "Authorization=***REDACTED***" — assert no-leak + the header
        // name survives rather than the exact separator.
        Assert.StartsWith("Authorization", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(Redacted, result, StringComparison.Ordinal);
        Assert.DoesNotContain("eyJhbGciOiJIUzI1NiJ9", result, StringComparison.Ordinal);
        Assert.DoesNotContain("dXNlcjpwYXNzd29yZA", result, StringComparison.Ordinal);
        Assert.DoesNotContain("abcdef123456", result, StringComparison.Ordinal);
    }

    [Fact]
    public void Scrub_RedactsBearerToken_OutsideHeaderLine()
    {
        var result = CrashReport.Scrub("request failed, bearer aBc123-XyZ.456 attached");

        Assert.DoesNotContain("aBc123-XyZ.456", result, StringComparison.Ordinal);
        Assert.Contains($"Bearer {Redacted}", result, StringComparison.Ordinal);
    }

    // ==================================================================
    // Scrub — oauth: prefix + RTMP stream keys
    // ==================================================================

    [Fact]
    public void Scrub_RedactsTwitchIrcOauthPassword()
    {
        var result = CrashReport.Scrub("PASS oauth:abcdef0123456789");

        Assert.DoesNotContain("abcdef0123456789", result, StringComparison.Ordinal);
        Assert.Contains($"oauth:{Redacted}", result, StringComparison.Ordinal);
        Assert.StartsWith("PASS ", result, StringComparison.Ordinal);
    }

    [Fact]
    public void Scrub_RedactsRtmpStreamKey_KeepingIngestPrefix()
    {
        var result = CrashReport.Scrub("rtmp://ingest.example.net/live/STREAMKEY7788");

        // Host + app stay diagnosable; only the key segment goes.
        Assert.Equal($"rtmp://ingest.example.net/live/{Redacted}", result);
    }

    [Fact]
    public void Scrub_RedactsRtmpsKeySegment()
    {
        var result = CrashReport.Scrub("publish to rtmps://live-kick.example.com/app/k1ck-s3cret");

        Assert.DoesNotContain("k1ck-s3cret", result, StringComparison.Ordinal);
        Assert.StartsWith("publish to rtmps://live-kick.example.com/app/", result, StringComparison.Ordinal);
    }

    [Fact]
    public void Scrub_LeavesKeylessRtmpUrl_Unchanged()
    {
        // No trailing key segment -> nothing secret -> nothing touched.
        const string input = "rtmp://ingest.example.net/live";
        Assert.Equal(input, CrashReport.Scrub(input));
    }

    // ==================================================================
    // Scrub — plain text must survive (over-redaction guard)
    // ==================================================================

    [Theory]
    [InlineData("Failed to start FFmpeg after 3 attempts (exit code 1)")]
    [InlineData("Could not load scene collection 'Streaming' from disk")]
    [InlineData("https://api.example.com/v1/streams?user_login=someone&limit=50")]
    public void Scrub_LeavesDiagnosticPlainText_Unchanged(string input)
    {
        Assert.Equal(input, CrashReport.Scrub(input));
    }

    [Fact]
    public void Scrub_MixedLine_RedactsSecret_KeepsContext()
    {
        var result = CrashReport.Scrub("connect to rtmp://a.b.c/live/KEYXYZ failed: timeout after 10s");

        Assert.DoesNotContain("KEYXYZ", result, StringComparison.Ordinal);
        Assert.Contains("rtmp://a.b.c/live/", result, StringComparison.Ordinal);
        Assert.Contains("failed: timeout after 10s", result, StringComparison.Ordinal);
    }

    [Fact]
    public void Scrub_NullOrEmpty_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, CrashReport.Scrub(null));
        Assert.Equal(string.Empty, CrashReport.Scrub(string.Empty));
    }

    // ==================================================================
    // Format — exception chains, aggregates, null
    // ==================================================================

    [Fact]
    public void Format_Null_ReturnsPlaceholder()
    {
        Assert.Equal("(no exception)", CrashReport.Format(null));
    }

    [Fact]
    public void Format_SimpleException_IncludesTypeAndScrubbedMessage()
    {
        var ex = new InvalidOperationException("connect failed with password=supersecret5");

        var result = CrashReport.Format(ex);

        Assert.Contains("System.InvalidOperationException:", result, StringComparison.Ordinal);
        Assert.DoesNotContain("supersecret5", result, StringComparison.Ordinal);
        Assert.Contains($"password={Redacted}", result, StringComparison.Ordinal);
    }

    [Fact]
    public void Format_InnerExceptionChain_BothLevelsScrubbed()
    {
        var inner = new ArgumentException("callback https://x.example.org/cb?token=innerTok99 rejected");
        var outer = new Exception("oauth handshake failed at rtmp://ing.example.net/app/outKey42", inner);

        var result = CrashReport.Format(outer);

        Assert.Contains("--- Inner exception (depth 1) ---", result, StringComparison.Ordinal);
        Assert.Contains("System.ArgumentException:", result, StringComparison.Ordinal);
        Assert.DoesNotContain("innerTok99", result, StringComparison.Ordinal);
        Assert.DoesNotContain("outKey42", result, StringComparison.Ordinal);
        Assert.Contains($"token={Redacted}", result, StringComparison.Ordinal);
        Assert.Contains($"ing.example.net/app/{Redacted}", result, StringComparison.Ordinal);
    }

    [Fact]
    public void Format_AggregateException_UnwrapsAndScrubsEachInner()
    {
        var first = new InvalidOperationException("irc auth used oauth:aggSecret1");
        var second = new TimeoutException("stream_key=aggKey2 timed out");
        var aggregate = new AggregateException(first, second);

        var result = CrashReport.Format(aggregate);

        Assert.Contains("AggregateException:", result, StringComparison.Ordinal);
        Assert.Contains("(2 inner exception(s))", result, StringComparison.Ordinal);
        Assert.Contains("--- inner #1 ---", result, StringComparison.Ordinal);
        Assert.Contains("--- inner #2 ---", result, StringComparison.Ordinal);
        Assert.Contains("System.InvalidOperationException:", result, StringComparison.Ordinal);
        Assert.Contains("System.TimeoutException:", result, StringComparison.Ordinal);
        Assert.DoesNotContain("aggSecret1", result, StringComparison.Ordinal);
        Assert.DoesNotContain("aggKey2", result, StringComparison.Ordinal);
        Assert.Contains($"oauth:{Redacted}", result, StringComparison.Ordinal);
        Assert.Contains($"stream_key={Redacted}", result, StringComparison.Ordinal);
    }

    [Fact]
    public void Format_AggregateMessage_ItselfIsScrubbed()
    {
        // AggregateException.Message embeds the inner messages in parentheses.
        var aggregate = new AggregateException(new Exception("token=aggMsgTok7"));

        var result = CrashReport.Format(aggregate);

        Assert.DoesNotContain("aggMsgTok7", result, StringComparison.Ordinal);
        Assert.Contains($"token={Redacted}", result, StringComparison.Ordinal);
    }
}
