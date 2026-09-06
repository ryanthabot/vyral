namespace VyralBot.Core.Services.Linking;

/// <summary>
/// The shipped user-facing copy for the link/delink guards and replies.
///
/// Single source of truth: <see cref="LinkCodeService"/> and the
/// <c>!link</c>/<c>!delink</c>/<c>!unlink</c> command handlers in
/// <c>CurrencyCommandService</c> render their chat replies from these
/// constants. {curly} tokens are placeholders the caller fills in at
/// runtime via <see cref="string.Replace(string, string?)"/>:
/// <list type="bullet">
/// <item><c>{platform}</c> — a canonical platform name (e.g. "Twitch")</item>
/// <item><c>{name}</c> — the conflicting account's username</item>
/// <item><c>{code}</c> — the 6-digit code</item>
/// <item><c>{n}</c>/<c>{N}</c> — minute / platform counts</item>
/// <item><c>{names}</c> — comma-joined linked platform names</item>
/// </list>
/// Rejections that reach chat as warnings carry the ⚠️ prefix; success
/// replies carry ✅ — do not change the prefixes without updating the
/// binding tests in <c>LinkGuardTests</c>.
/// </summary>
public static class LinkMessages
{
    // ------------------------------------------------------------------
    // RedeemCodeAsync (link codes)
    // ------------------------------------------------------------------

    /// <summary>Guard 1: the target identity already holds an account on the redeemer's platform.</summary>
    public const string AlreadyHasPlatformAccount =
        "Your link group already has a {platform} account ({name}). Remove it first with !delink.";

    /// <summary>Guard 2: merging would put both groups' accounts on the same platform in one identity.</summary>
    public const string MergeConflict =
        "Both link groups have a {platform} account — remove one with !delink first.";

    /// <summary>Early no-op: the redeemer's account is already inside the code's link group.</summary>
    public const string AlreadyInGroup =
        "You're already part of this link group.";

    /// <summary>A delink (removal) code was typed into the !link redemption path.</summary>
    public const string DelinkCodeUsedAsLink =
        "⚠️ That code is a removal code — use '!delink {code}' from the platform you want to remove.";

    // ------------------------------------------------------------------
    // GenerateDelinkCodeAsync (authorizer-side guards)
    // ------------------------------------------------------------------

    public const string NoLinkedAccounts =
        "You have no linked accounts";

    /// <summary>The two-sided rule: the authorizing account cannot be the removed account.</summary>
    public const string CannotRemoveAccountYouAreTypingFrom =
        "You can't authorize removal of the account you're typing from";

    public const string NoAccountOnTargetPlatform =
        "No {platform} account is linked to your group";

    public const string OnlyOneLinkedAccount =
        "This is your only linked account — ask the streamer to remove it";

    // ------------------------------------------------------------------
    // RedeemDelinkCodeAsync (confirmation-side guards + success)
    // ------------------------------------------------------------------

    public const string DelinkCodeNotForThisGroup =
        "That code wasn't issued for this account group";

    public const string DelinkCodeWrongPlatform =
        "Type !delink <code> from {platform}";

    public const string DelinkSuccessPlatformsRemain =
        "✅ {platform} account removed from your link group. {N} platform(s) remain: {names}";

    public const string DelinkSuccessNothingRemains =
        "Your accounts are no longer linked together.";

    // ------------------------------------------------------------------
    // !delink / !unlink command layer (CurrencyCommandService)
    // ------------------------------------------------------------------

    public const string DelinkUsageHelp =
        "From one linked account: !delink <platform> — then on the account to remove: !delink <code>";

    public const string DelinkCodeDeliveryInstruction =
        "Now type '!delink {code}' on {platform} within {n} minutes.";

    /// <summary>The retired !unlink command now only points at the !delink flow.</summary>
    public const string UnlinkReplacedByDelink =
        "⚠️ !unlink was replaced by !delink. From a linked account: !delink <platform>, then on the account to remove: !delink <code>.";
}
