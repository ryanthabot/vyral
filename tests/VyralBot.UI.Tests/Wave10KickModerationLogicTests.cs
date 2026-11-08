using System.Reflection;
using Xunit;

namespace VyralBot.UI.Tests;

/// <summary>
/// Wave-10 coverage for the KICK MODERATION APIS added in wave 9
/// (<c>KickLibChatService.TimeoutUserAsync/BanUserAsync/DeleteMessageAsync</c>,
/// the enforcement path of the auto-moderator's KickModerationAdapter).
///
/// Seam note (deliberate scope): KickLibChatService hard-constructs its
/// <c>KickLib.IKickClient</c>/<c>KickLib.IKickApi</c> in the constructor —
/// there is no injection seam, so the network-touching branches cannot be
/// exercised without a live Kick session. What IS pure is the static
/// moderation-logic helper <c>TruncateReason</c> (Kick's 100-char reason cap)
/// plus the two documented Kick API bounds, pinned here via reflection (the
/// wave-8 reflection pattern). The timeout minutes clamp
/// (<c>Math.Clamp((int)Math.Ceiling(duration.TotalMinutes), 1, 10080)</c>)
/// is INLINE in TimeoutUserAsync with no extracted helper — untestable
/// without a refactor; see the marathon notes.
/// </summary>
public class Wave10KickModerationLogicTests
{
    private static MethodInfo TruncateReasonMethod()
        => typeof(VyralBot.Integrations.Kick.KickLibChatService)
            .GetMethod("TruncateReason", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("KickLibChatService.TruncateReason moved — update the wave-10 tests");

    private static string TruncateReason(string reason)
        => (string)TruncateReasonMethod().Invoke(null, new object?[] { reason })!;

    private static int GetPrivateConst(string name)
        => (int)(typeof(VyralBot.Integrations.Kick.KickLibChatService)
            .GetField(name, BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException($"KickLibChatService.{name} moved — update the wave-10 tests"))
            .GetValue(null)!;

    // ------------------------------------------------------------------
    // Documented Kick API bounds (pin the constants the logic relies on)
    // ------------------------------------------------------------------

    [Fact]
    public void KickCaps_BanReason100Chars_TimeoutMax10080Minutes()
    {
        // Kick: ban/timeout reasons cap at 100 characters; timeouts are
        // minute-granular, 1 minute .. 7 days (10080 minutes).
        Assert.Equal(100, GetPrivateConst("MaxModerationReasonLength"));
        Assert.Equal(10080, GetPrivateConst("MaxTimeoutMinutes"));
    }

    // ------------------------------------------------------------------
    // TruncateReason: the pure pre-send helper for bans + timeouts
    // ------------------------------------------------------------------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void EmptyOrNullReason_FallsBackToAutoMod(string? reason)
    {
        // A blank caller reason must still satisfy Kick's required reason
        // field — "auto-mod" names the enforcing subsystem instead of
        // sending an empty string the API would reject.
        Assert.Equal("auto-mod", TruncateReason(reason!));
    }

    [Fact]
    public void ShortReason_PassesThroughUnchanged()
    {
        Assert.Equal("spamming emotes", TruncateReason("spamming emotes"));
    }

    [Fact]
    public void ReasonAtExactly100Chars_IsKeptWhole()
    {
        var reason = new string('x', 100);
        Assert.Equal(100, TruncateReason(reason).Length);
        Assert.Equal(reason, TruncateReason(reason));
    }

    [Fact]
    public void ReasonOver100Chars_IsCutToExactly100()
    {
        var truncated = TruncateReason(new string('x', 250));
        Assert.Equal(100, truncated.Length);
        Assert.Equal(new string('x', 100), truncated);
    }

    [Fact]
    public void Boundary_99And101Chars()
    {
        Assert.Equal(99, TruncateReason(new string('y', 99)).Length);
        Assert.Equal(100, TruncateReason(new string('y', 101)).Length);
    }
}
