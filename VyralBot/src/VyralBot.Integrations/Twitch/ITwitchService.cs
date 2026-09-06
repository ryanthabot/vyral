namespace VyralBot.Integrations.Twitch;

/// <summary>
/// Outcome of a Twitch Helix management action, with a user-presentable message.
/// </summary>
/// <param name="Success">True when Twitch accepted the action.</param>
/// <param name="Message">Text safe to show in the UI (includes a re-auth hint on permission failures).</param>
public record TwitchActionResponse(bool Success, string Message);

/// <summary>Current Twitch chat moderation modes as reported by the Helix API.</summary>
/// <param name="EmoteOnly">Emote-only chat mode is enabled.</param>
/// <param name="FollowerOnly">Follower-only chat mode is enabled.</param>
/// <param name="ShieldMode">Shield mode is enabled.</param>
public record TwitchChatModeState(bool EmoteOnly, bool FollowerOnly, bool ShieldMode);

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

    // ── Stream-management quick actions (Helix API) ───────────────────
    // All methods return a TwitchActionResponse instead of throwing so UI
    // callers can surface failures directly; the message is user-presentable.

    /// <summary>
    /// Updates the broadcaster's channel title and/or category via
    /// PATCH /helix/channels. The category is resolved by name through
    /// GET /helix/search/categories. Pass null to leave a field unchanged.
    /// Requires the channel:manage:broadcast scope.
    /// </summary>
    Task<TwitchActionResponse> EditChannelInfoAsync(string? title, string? category);

    /// <summary>
    /// Creates a stream marker via POST /helix/streams/markers
    /// (the channel must be live). Null description = untitled marker.
    /// </summary>
    Task<TwitchActionResponse> CreateStreamMarkerAsync(string? description);

    /// <summary>
    /// Creates a clip via POST /helix/clips. The message includes the clip URL.
    /// </summary>
    Task<TwitchActionResponse> CreateClipAsync();

    /// <summary>
    /// Starts a raid to the given channel (login name, without '#') via
    /// POST /helix/raids after resolving the target's user id.
    /// </summary>
    Task<TwitchActionResponse> RaidChannelAsync(string targetChannel);

    /// <summary>
    /// Cancels an outgoing raid via DELETE /helix/raids.
    /// </summary>
    Task<TwitchActionResponse> StopRaidAsync();

    /// <summary>
    /// Clears all chat messages via DELETE /helix/moderation/chat.
    /// Requires the token to hold moderator:manage:chat.
    /// </summary>
    Task<TwitchActionResponse> ClearChatAsync();

    /// <summary>
    /// Updates chat moderation modes via PUT /helix/chat/settings. Only the
    /// provided fields are changed; pass null to leave a mode untouched.
    /// </summary>
    Task<TwitchActionResponse> SetChatModeAsync(bool? emoteOnly, bool? followerOnly);

    /// <summary>
    /// Enables/disables Shield Mode via PUT /helix/moderation/shield_mode.
    /// </summary>
    Task<TwitchActionResponse> SetShieldModeAsync(bool enabled);

    /// <summary>
    /// Reads the current chat moderation modes (GET /helix/chat/settings +
    /// GET /helix/moderation/shield_mode); null when unavailable.
    /// </summary>
    Task<TwitchChatModeState?> GetChatModeStateAsync();
}
