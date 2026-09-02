namespace VyralBot.Core.Services.Chat;

/// <summary>
/// Interface for auto-reply service.
/// </summary>
public interface IAutoReplyService
{
    /// <summary>
    /// Processes an auto-reply message template by replacing variables.
    /// </summary>
    string ProcessTemplate(string template, Dictionary<string, string> variables);

    /// <summary>
    /// Checks if an auto-reply should be sent based on rate limiting.
    /// </summary>
    bool ShouldSendReply(string username, string platform, string eventType);

    /// <summary>
    /// Gets an auto-reply message based on conditional rules and rate limiting.
    /// </summary>
    /// <param name="username">Username</param>
    /// <param name="platform">Platform (Kick/Twitch)</param>
    /// <param name="eventType">Event type</param>
    /// <param name="context">Additional context for rule evaluation</param>
    /// <returns>Reply message if conditions are met, null if rate limited or conditions not met</returns>
    Task<string?> GetAutoReplyAsync(string username, string platform, string eventType, Dictionary<string, object>? context = null);

    /// <summary>
    /// Cleans up expired rate limiter entries.
    /// </summary>
    void CleanupExpiredEntries();

    /// <summary>
    /// Clears all rate limiting entries (for testing).
    /// </summary>
    void ClearRateLimits();
}
