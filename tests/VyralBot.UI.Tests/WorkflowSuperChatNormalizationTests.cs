using System.Reflection;
using VyralBot.UI.Services;
using Xunit;

namespace VyralBot.UI.Tests;

/// <summary>
/// Wave-6 regression coverage for YouTube Super Chat trigger normalization
/// (wave 5): <c>WorkflowTriggerService.NormalizeEventType</c> folds author
/// spellings onto canonical, lowercase, separator-free event keys —
/// "super_chat" / "Super Chat" / "SuperChat" must all land on the dedicated
/// <c>superchat</c> key (YouTube Super Chats deliberately do NOT fold into
/// Twitch's <c>cheer</c> key, so platform-Any workflows stay precise).
///
/// The normalizer is <c>private static</c> (no public seam), so it is invoked
/// via reflection — a pure string→string function with no state, which keeps
/// the reflection stable. If the method is renamed/made public, these tests
/// fail loudly at the seam lookup rather than silently skipping.
/// </summary>
public class WorkflowSuperChatNormalizationTests
{
    private static readonly MethodInfo? NormalizeMethod = typeof(WorkflowTriggerService).GetMethod(
        "NormalizeEventType", BindingFlags.NonPublic | BindingFlags.Static);

    private static string Normalize(string? eventType)
    {
        Assert.NotNull(NormalizeMethod);
        return (string)NormalizeMethod!.Invoke(null, new object?[] { eventType })!;
    }

    // ==================================================================
    // Super Chat spellings → the dedicated "superchat" key
    // ==================================================================

    [Theory]
    [InlineData("superchat")]        // canonical
    [InlineData("SuperChat")]        // Pascal spelling
    [InlineData("super_chat")]       // snake_case
    [InlineData("Super Chat")]       // human spelling (workflow author)
    [InlineData("SUPER CHAT")]       // shouting
    [InlineData("super-chat")]       // kebab-case
    [InlineData("  Super Chat  ")]   // stray whitespace
    public void SuperChatSpellings_NormalizeOntoSuperchatKey(string authored)
    {
        Assert.Equal("superchat", Normalize(authored));
    }

    [Fact]
    public void SuperChat_KeepsItsOwnKey_DoesNotFoldIntoCheer()
    {
        // Wave-5 key choice: "superchat" is its own canonical key so an
        // Any-platform cheer workflow cannot accidentally fire for it.
        var normalized = Normalize("Super Chat");

        Assert.Equal("superchat", normalized);
        Assert.NotEqual("cheer", normalized);
    }

    // ==================================================================
    // Neighbor aliases (guard the switch the superchat case lives in)
    // ==================================================================

    [Theory]
    [InlineData("bits", "cheer")]
    [InlineData("cheer", "cheer")]
    [InlineData("sub", "subscribe")]
    [InlineData("subscription", "subscribe")]
    [InlineData("gifted", "giftsub")]
    [InlineData("raided", "raid")]
    [InlineData("join", "discordmemberjoined")]
    [InlineData("discord member joined", "discordmemberjoined")]
    [InlineData("boost", "discordboost")]
    [InlineData("Discord Boost", "discordboost")]
    public void NeighboringAliases_NormalizeOntoCanonicalKeys(string authored, string canonical)
    {
        Assert.Equal(canonical, Normalize(authored));
    }

    [Fact]
    public void UnknownEventType_KeepsLowercaseSeparatorFreeForm()
    {
        Assert.Equal("customevent", Normalize("Custom_Event"));
        Assert.Equal("hypetrain", Normalize("HYPE-TRAIN"));
    }

    [Fact]
    public void NullOrEmpty_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, Normalize(null));
        Assert.Equal(string.Empty, Normalize(""));
        Assert.Equal(string.Empty, Normalize("   "));
    }
}
