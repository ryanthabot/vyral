namespace VyralBot.Integrations.YouTube;

/// <summary>
/// Outcome of a YouTube management action, with a user-presentable message.
/// </summary>
/// <param name="Success">True when Google accepted the action.</param>
/// <param name="Message">Text safe to show in the UI (includes a re-auth hint on permission failures).</param>
public record YouTubeActionResponse(bool Success, string Message);

/// <summary>
/// Service for YouTube Live Chat integration via YouTube Data API v3.
/// </summary>
public interface IYouTubeService
{
    /// <summary>
    /// Connects to YouTube Live Chat using stored credentials.
    /// Starts polling for live chat messages when a broadcast is active.
    /// </summary>
    /// <param name="username">The channel display name.</param>
    /// <param name="accessToken">OAuth access token.</param>
    /// <param name="channel">Channel name/ID.</param>
    Task ConnectAsync(string username, string accessToken, string channel);

    /// <summary>
    /// Connects to YouTube Live Chat with a refresh token for automatic renewal.
    /// </summary>
    Task ConnectAsync(string username, string accessToken, string? refreshToken, string channel);

    /// <summary>
    /// Disconnects from YouTube Live Chat and stops polling.
    /// </summary>
    Task DisconnectAsync();

    /// <summary>
    /// Sends a message to the active YouTube Live Chat.
    /// </summary>
    Task SendMessageAsync(string message);

    /// <summary>
    /// Gets whether the service is connected and polling.
    /// </summary>
    bool IsConnected { get; }

    /// <summary>True when the last token refresh failed and Google sign-in is required again (Platforms page surfaces this).</summary>
    bool RequiresReauth { get; }

    /// <summary>Google's error reason for <see cref="RequiresReauth"/> (e.g. "invalid_grant"); null when not applicable.</summary>
    string? ReauthReason { get; }

    /// <summary>
    /// Live-chat state: "NotConnected", "Connecting", "WaitingForBroadcast"
    /// (connected but the channel isn't live), "Listening" (active live chat)
    /// or "ReauthRequired".
    /// </summary>
    string LiveChatState { get; }

    /// <summary>
    /// Gets the connected channel name.
    /// </summary>
    string? Channel { get; }

    /// <summary>
    /// Gets the bot/channel username.
    /// </summary>
    string? BotUsername { get; }

    /// <summary>
    /// Updates the active broadcast's title and/or description via the
    /// liveBroadcasts API. Pass null to leave a field unchanged. Fails with a
    /// friendly message when the channel has no active broadcast or the token
    /// lacks the required scope (never throws to the caller).
    /// </summary>
    Task<YouTubeActionResponse> UpdateStreamInfoAsync(string? title, string? description);
}
