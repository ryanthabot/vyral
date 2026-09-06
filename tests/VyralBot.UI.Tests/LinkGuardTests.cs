using VyralBot.Core.Services.Linking;
using Xunit;

namespace VyralBot.UI.Tests;

/// <summary>
/// BINDING TESTS against the shipped user-facing message constants for the v2
/// linking guards (one-account-per-platform + two-sided !delink).
///
/// The constants live in <see cref="LinkMessages"/> (VyralBot.Core/Services/Linking)
/// and are rendered at runtime by <see cref="LinkCodeService"/> and the
/// !link/!delink/!unlink handlers in CurrencyCommandService. These tests pin the
/// contract the chat copy must keep: every constant is non-empty, carries its
/// expected {placeholder} slots, keeps its ⚠️/✅ prefix where one is shipped, and
/// redirects only to !delink (never the retired !unlink). Rewording a shipped
/// message means updating the constant AND these tests together — a change that
/// drops a placeholder or breaks the prefix fails here.
///
/// Behavioral guard logic (the merge decisions themselves) is database-coupled
/// (IDataService + ILinkedAccountService + IEventBus) and covered by the service
/// layer; this file intentionally binds the message surface only.
/// </summary>
public class LinkGuardTests
{
    // ------------------------------------------------------------------
    // RedeemCodeAsync (link codes): guards + early no-op + wrong-purpose code
    // ------------------------------------------------------------------

    [Fact]
    public void OnePerPlatform_Rejection_IsShipped_WithPlatformAndNameSlots()
    {
        Assert.False(string.IsNullOrWhiteSpace(LinkMessages.AlreadyHasPlatformAccount));
        Assert.StartsWith("Your link group already has a {platform} account ({name})", LinkMessages.AlreadyHasPlatformAccount);
        Assert.Contains("{platform}", LinkMessages.AlreadyHasPlatformAccount);
        Assert.Contains("{name}", LinkMessages.AlreadyHasPlatformAccount);
    }

    [Fact]
    public void OnePerPlatform_Rejection_PointsAtDelinkOnly()
    {
        Assert.Contains("!delink", LinkMessages.AlreadyHasPlatformAccount);
        Assert.DoesNotContain("!unlink", LinkMessages.AlreadyHasPlatformAccount);
    }

    [Fact]
    public void MergeConflict_Rejection_IsShipped_WithPlatformSlot()
    {
        Assert.False(string.IsNullOrWhiteSpace(LinkMessages.MergeConflict));
        Assert.StartsWith("Both link groups have a {platform} account", LinkMessages.MergeConflict);
        Assert.Contains("{platform}", LinkMessages.MergeConflict);
        Assert.Contains("!delink", LinkMessages.MergeConflict);
    }

    [Fact]
    public void AlreadyInGroup_NoOp_IsShipped_AsPlainSuccess()
    {
        Assert.False(string.IsNullOrWhiteSpace(LinkMessages.AlreadyInGroup));
        Assert.Equal("You're already part of this link group.", LinkMessages.AlreadyInGroup);
    }

    [Fact]
    public void DelinkCodeUsedAsLink_Warning_IsShipped_WithCodeSlotAndPrefix()
    {
        Assert.False(string.IsNullOrWhiteSpace(LinkMessages.DelinkCodeUsedAsLink));
        Assert.StartsWith("⚠️", LinkMessages.DelinkCodeUsedAsLink);
        Assert.Contains("{code}", LinkMessages.DelinkCodeUsedAsLink);
        Assert.Contains("'!delink {code}'", LinkMessages.DelinkCodeUsedAsLink);
    }

    // ------------------------------------------------------------------
    // GenerateDelinkCodeAsync (authorizer-side guards)
    // ------------------------------------------------------------------

    [Fact]
    public void Delink_NoLinkedAccounts_IsShipped()
    {
        Assert.False(string.IsNullOrWhiteSpace(LinkMessages.NoLinkedAccounts));
        Assert.Equal("You have no linked accounts", LinkMessages.NoLinkedAccounts);
    }

    [Fact]
    public void Delink_SelfRemovalGuard_IsShipped()
    {
        // The two-sided rule: the authorizing account cannot be the removed one.
        Assert.False(string.IsNullOrWhiteSpace(LinkMessages.CannotRemoveAccountYouAreTypingFrom));
        Assert.Equal("You can't authorize removal of the account you're typing from", LinkMessages.CannotRemoveAccountYouAreTypingFrom);
    }

    [Fact]
    public void Delink_TargetNotLinked_IsShipped_WithPlatformSlot()
    {
        Assert.False(string.IsNullOrWhiteSpace(LinkMessages.NoAccountOnTargetPlatform));
        Assert.Equal("No {platform} account is linked to your group", LinkMessages.NoAccountOnTargetPlatform);
    }

    [Fact]
    public void Delink_SingleAccountGuard_IsShipped()
    {
        Assert.False(string.IsNullOrWhiteSpace(LinkMessages.OnlyOneLinkedAccount));
        Assert.Equal("This is your only linked account — ask the streamer to remove it", LinkMessages.OnlyOneLinkedAccount);
    }

    // ------------------------------------------------------------------
    // RedeemDelinkCodeAsync (confirmation-side guards + success)
    // ------------------------------------------------------------------

    [Fact]
    public void DelinkCode_ForeignGroupGuard_IsShipped()
    {
        Assert.False(string.IsNullOrWhiteSpace(LinkMessages.DelinkCodeNotForThisGroup));
        Assert.Equal("That code wasn't issued for this account group", LinkMessages.DelinkCodeNotForThisGroup);
    }

    [Fact]
    public void DelinkCode_WrongPlatformGuard_IsShipped_WithPlatformSlot()
    {
        Assert.False(string.IsNullOrWhiteSpace(LinkMessages.DelinkCodeWrongPlatform));
        Assert.StartsWith("Type !delink <code> from", LinkMessages.DelinkCodeWrongPlatform);
        Assert.Contains("{platform}", LinkMessages.DelinkCodeWrongPlatform);
    }

    [Fact]
    public void DelinkSuccess_PlatformsRemain_IsShipped_WithCheckmarkAndAllSlots()
    {
        Assert.False(string.IsNullOrWhiteSpace(LinkMessages.DelinkSuccessPlatformsRemain));
        Assert.StartsWith("✅", LinkMessages.DelinkSuccessPlatformsRemain);
        Assert.Contains("{platform}", LinkMessages.DelinkSuccessPlatformsRemain);
        Assert.Contains("{N}", LinkMessages.DelinkSuccessPlatformsRemain);
        Assert.Contains("{names}", LinkMessages.DelinkSuccessPlatformsRemain);
    }

    [Fact]
    public void DelinkSuccess_NothingRemains_IsShipped()
    {
        Assert.False(string.IsNullOrWhiteSpace(LinkMessages.DelinkSuccessNothingRemains));
        Assert.Equal("Your accounts are no longer linked together.", LinkMessages.DelinkSuccessNothingRemains);
    }

    // ------------------------------------------------------------------
    // !delink / !unlink command layer
    // ------------------------------------------------------------------

    [Fact]
    public void Delink_UsageHelp_IsShipped_WithTwoStepFlow()
    {
        Assert.False(string.IsNullOrWhiteSpace(LinkMessages.DelinkUsageHelp));
        Assert.Contains("!delink <platform>", LinkMessages.DelinkUsageHelp);
        Assert.Contains("!delink <code>", LinkMessages.DelinkUsageHelp);
    }

    [Fact]
    public void Delink_CodeDelivery_Instruction_IsShipped_WithCodePlatformExpirySlots()
    {
        Assert.False(string.IsNullOrWhiteSpace(LinkMessages.DelinkCodeDeliveryInstruction));
        Assert.Contains("'!delink {code}'", LinkMessages.DelinkCodeDeliveryInstruction);
        Assert.Contains("{platform}", LinkMessages.DelinkCodeDeliveryInstruction);
        Assert.Contains("{n}", LinkMessages.DelinkCodeDeliveryInstruction);
    }

    [Fact]
    public void Unlink_Retired_PointerReply_IsShipped_WarningPointingAtDelinkFlow()
    {
        Assert.False(string.IsNullOrWhiteSpace(LinkMessages.UnlinkReplacedByDelink));
        Assert.StartsWith("⚠️", LinkMessages.UnlinkReplacedByDelink);
        Assert.Contains("!unlink was replaced by !delink", LinkMessages.UnlinkReplacedByDelink);
        Assert.Contains("!delink <platform>", LinkMessages.UnlinkReplacedByDelink);
        Assert.Contains("!delink <code>", LinkMessages.UnlinkReplacedByDelink);
    }

    /// <summary>
    /// Every redirecting guard message points at the <c>!delink</c> command —
    /// never the retired <c>!unlink</c> — so docs and chat copy agree on the verb.
    /// </summary>
    [Fact]
    public void RedirectingGuardMessages_AllPointAtDelinkCommand()
    {
        var redirecting = new[]
        {
            LinkMessages.AlreadyHasPlatformAccount,
            LinkMessages.MergeConflict,
            LinkMessages.DelinkCodeUsedAsLink,
            LinkMessages.DelinkCodeWrongPlatform,
            LinkMessages.DelinkUsageHelp,
            LinkMessages.DelinkCodeDeliveryInstruction
        };

        Assert.All(redirecting, m => Assert.Contains("!delink", m));
        Assert.All(redirecting, m => Assert.DoesNotContain("!unlink", m));

        // The retired-command pointer is the one place "!unlink" may appear —
        // and only to announce that it was replaced.
        Assert.StartsWith("⚠️ !unlink was replaced by !delink.", LinkMessages.UnlinkReplacedByDelink);
    }
}
