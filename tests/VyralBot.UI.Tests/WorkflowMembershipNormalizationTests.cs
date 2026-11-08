using System.Reflection;
using VyralBot.UI.Services;
using Xunit;

namespace VyralBot.UI.Tests;

/// <summary>
/// Wave-7 regression coverage for the YouTube membership trigger key:
/// <c>WorkflowTriggerService.NormalizeEventType</c> folds author spellings
/// onto the dedicated <c>membership</c> key — which deliberately does NOT
/// fold into Twitch/Kick's <c>subscribe</c> key (the "sub" alias), so
/// platform-Any subscribe workflows cannot fire for YouTube members and the
/// two remain separately targetable.
///
/// Mirrors <see cref="WorkflowSuperChatNormalizationTests"/> (the normalizer
/// is <c>private static</c>, so it is invoked via reflection — a pure
/// string→string function with no state, which keeps the reflection stable;
/// a rename fails loudly at the seam lookup rather than silently skipping).
/// </summary>
public class WorkflowMembershipNormalizationTests
{
    private static readonly MethodInfo? NormalizeMethod = typeof(WorkflowTriggerService).GetMethod(
        "NormalizeEventType", BindingFlags.NonPublic | BindingFlags.Static);

    private static string Normalize(string? eventType)
    {
        Assert.NotNull(NormalizeMethod);
        return (string)NormalizeMethod!.Invoke(null, new object?[] { eventType })!;
    }

    // ==================================================================
    // Membership spellings → the dedicated "membership" key
    // ==================================================================

    [Theory]
    [InlineData("membership")]              // canonical
    [InlineData("Membership")]              // Pascal spelling
    [InlineData("MEMBERSHIP")]              // shouting
    [InlineData("YouTube Membership")]      // human spelling (workflow author)
    [InlineData("youtube-membership")]      // kebab-case with platform prefix
    [InlineData("yt_membership")]           // snake_case with platform prefix
    [InlineData("sponsor")]                 // YouTube API shorthand
    [InlineData("newSponsor")]              // YouTube API join event (short)
    [InlineData("newSponsorEvent")]         // YouTube API join event (verbatim)
    [InlineData("member milestone")]        // milestone chat event (short)
    [InlineData("memberMilestoneChatEvent")] // milestone chat event (verbatim)
    [InlineData("  Membership  ")]          // stray whitespace
    public void MembershipSpellings_NormalizeOntoMembershipKey(string authored)
    {
        Assert.Equal("membership", Normalize(authored));
    }

    [Fact]
    public void Membership_KeepsItsOwnKey_DoesNotFoldIntoSubscribe()
    {
        // Wave-7 key choice: "membership" is its own canonical key so an
        // Any-platform subscribe workflow cannot accidentally fire for it.
        var normalized = Normalize("YouTube Membership");

        Assert.Equal("membership", normalized);
        Assert.NotEqual("subscribe", normalized);
    }

    // ==================================================================
    // Twitch sub aliases survive next to the new membership arm
    // ==================================================================

    [Theory]
    [InlineData("sub", "subscribe")]
    [InlineData("subscription", "subscribe")]
    [InlineData("Sub", "subscribe")]
    [InlineData("resub", "resub")]
    [InlineData("memberjoined", "discordmemberjoined")]
    [InlineData("discord member joined", "discordmemberjoined")]
    public void NeighboringAliases_StillNormalizeOntoTheirCanonicalKeys(string authored, string canonical)
    {
        Assert.Equal(canonical, Normalize(authored));
    }
}
