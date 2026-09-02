namespace VyralBot.Integrations.Twitch;

/// <summary>
/// Service for Twitch IRC chat and API integration.
/// </summary>
public interface ITwitchService
{
    /// <summary>
    /// Connects to Twitch chat with provided credentials.
    /// </summary>
    /// <param name="username">Bot username.</param>
    /// <param name="accessToken">OAuth access token (without 'oauth:' prefix).</param>
    /// <param name="channel">Channel to join.</param>
    Task ConnectAsync(string username, string accessToken, string channel);

    /// <summary>
    /// Connects to Twitch chat with provided credentials and refresh token.
    /// </summary>
    /// <param name="username">Bot username.</param>
    /// <param name="accessToken">OAuth access token (without 'oauth:' prefix).</param>
    /// <param name="refreshToken">OAuth refresh token for automatic token renewal.</param>
    /// <param name="channel">Channel to join.</param>
    Task ConnectAsync(string username, string accessToken, string? refreshToken, string channel);

    /// <summary>
    /// Disconnects from Twitch chat.
    /// </summary>
    Task DisconnectAsync();

    /// <summary>
    /// Sends a message to the connected channel.
    /// </summary>
    Task SendMessageAsync(string message);

    /// <summary>
    /// Gets whether the service is connected.
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// Gets the connected channel name.
    /// </summary>
    string? Channel { get; }

    /// <summary>
    /// Gets the bot username.
    /// </summary>
    string? BotUsername { get; }
}
