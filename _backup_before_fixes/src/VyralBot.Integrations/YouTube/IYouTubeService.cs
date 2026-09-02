namespace VyralBot.Integrations.YouTube;

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

    /// <summary>
    /// Gets the connected channel name.
    /// </summary>
    string? Channel { get; }

    /// <summary>
    /// Gets the bot/channel username.
    /// </summary>
    string? BotUsername { get; }
}
