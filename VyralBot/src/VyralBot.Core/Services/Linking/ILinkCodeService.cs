using VyralBot.Data.Entities;

namespace VyralBot.Core.Services.Linking;

/// <summary>
/// Outcome of redeeming a link code.
/// </summary>
/// <param name="Success">Whether the code was redeemed and accounts merged.</param>
/// <param name="Message">Ready-to-send chat reply for the redeemer.</param>
/// <param name="ViewerId">Unified viewer identity after the merge (null on failure).</param>
/// <param name="LinkedPlatforms">Distinct platform names linked under the viewer identity.</param>
public record LinkRedeemResult(bool Success, string Message, string? ViewerId, IReadOnlyList<string> LinkedPlatforms);

/// <summary>
/// Outcome of redeeming a delink code.
/// </summary>
/// <param name="Success">Whether the code was redeemed and the account removed.</param>
/// <param name="Message">Ready-to-send chat reply for the redeemer.</param>
public record DelinkResult(bool Success, string Message);

/// <summary>
/// Service for generating and redeeming one-time codes that merge platform
/// accounts into a single unified viewer identity.
/// </summary>
public interface ILinkCodeService
{
    /// <summary>
    /// Generates a fresh 6-digit code for the viewer behind username+platform.
    /// Any previous unredeemed code for that viewer is invalidated.
    /// Throws <see cref="InvalidOperationException"/> when linking is disabled for the platform.
    /// </summary>
    Task<(string Code, DateTime ExpiresAt)> GenerateCodeAsync(string username, string platform);

    /// <summary>
    /// Redeems a code, merging the redeemer's identity into the code's viewer identity.
    /// </summary>
    Task<LinkRedeemResult> RedeemCodeAsync(string code, string username, string platform);

    /// <summary>
    /// Generates a one-time delink code: the caller authorizes removal of their
    /// account on <paramref name="targetPlatform"/>; whoever types the code from
    /// that platform removes it from the unified viewer identity.
    /// Throws <see cref="InvalidOperationException"/> with a chat-ready message
    /// when the caller has no linked accounts, the target platform is unknown,
    /// or the removal is not possible as requested.
    /// </summary>
    Task<(string Code, DateTime ExpiresAt)> GenerateDelinkCodeAsync(string username, string platform, string targetPlatform);

    /// <summary>
    /// Redeems a delink code: removes the redeeming account on
    /// <paramref name="platform"/> from its unified viewer identity.
    /// </summary>
    Task<DelinkResult> RedeemDelinkCodeAsync(string code, string username, string platform);

    /// <summary>
    /// Gets all linked accounts for the unified viewer behind username+platform.
    /// </summary>
    Task<IEnumerable<LinkedAccount>> GetLinkedAccountsForAsync(string username, string platform);

    /// <summary>
    /// Unlinks username+platform from its unified viewer identity.
    /// </summary>
    Task<bool> UnlinkAsync(string username, string platform);

    /// <summary>
    /// Deletes link codes that expired more than 24 hours ago.
    /// </summary>
    Task CleanupExpiredAsync();
}
