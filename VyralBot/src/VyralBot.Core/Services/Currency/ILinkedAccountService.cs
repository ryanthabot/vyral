using VyralBot.Data.Entities;

namespace VyralBot.Core.Services.Currency;

/// <summary>
/// Service for managing cross-platform account linking and unified viewer identity.
/// </summary>
public interface ILinkedAccountService
{
    /// <summary>
    /// Get the unified ViewerId for a username+platform pair, or null if not linked.
    /// </summary>
    Task<string?> GetViewerIdAsync(string username, string platform);

    /// <summary>
    /// Get all linked accounts for a ViewerId.
    /// </summary>
    Task<IEnumerable<LinkedAccount>> GetLinkedAccountsAsync(string viewerId);

    /// <summary>
    /// Get or create a ViewerId for a username+platform. Auto-creates if not linked.
    /// </summary>
    Task<string> GetOrCreateViewerIdAsync(string username, string platform);

    /// <summary>
    /// Initiate a link request between two platform accounts.
    /// </summary>
    Task<bool> InitiateLinkAsync(string fromUsername, string fromPlatform, string targetUsername, string targetPlatform);

    /// <summary>
    /// Verify and complete a link (called when the target account confirms).
    /// </summary>
    Task<bool> VerifyLinkAsync(string username, string platform, string verificationCode);

    /// <summary>
    /// Unlink an account from its ViewerId.
    /// </summary>
    Task<bool> UnlinkAccountAsync(string username, string platform);

    /// <summary>
    /// Resolve a username+platform to the primary identity for unified balance operations.
    /// Returns all linked (Username, Platform) pairs if unified mode is enabled.
    /// </summary>
    Task<IEnumerable<(string Username, string Platform)>> ResolveIdentitiesAsync(string username, string platform);
}
