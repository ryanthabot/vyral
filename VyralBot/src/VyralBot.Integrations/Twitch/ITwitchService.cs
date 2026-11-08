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
    /// Times a viewer out via POST /helix/moderation/bans (duration in seconds,
    /// clamped to Twitch's 1s–14d window). The username is resolved to a user
    /// id through GET /helix/users. Requires moderator:manage:banned_users.
    /// Returns false (never throws) when the viewer can't be resolved or
    /// Twitch rejects the call.
    /// </summary>
    Task<bool> TimeoutUserAsync(string username, TimeSpan duration, string reason);

    /// <summary>
    /// Bans a viewer via POST /helix/moderation/bans (no duration). The
    /// username is resolved to a user id through GET /helix/users. Requires
    /// moderator:manage:banned_users. Returns false (never throws) on failure.
    /// </summary>
    Task<bool> BanUserAsync(string username, string reason);

    /// <summary>
    /// Deletes a single chat message via DELETE /helix/moderation/chat with a
    /// message_id. Requires moderator:manage:chat_messages. Returns false
    /// (never throws) when no id is given or Twitch rejects the call.
    /// </summary>
    Task<bool> DeleteMessageAsync(string messageId);

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

    // ── Helix engine-facing actions & lookups ─────────────────────────
    // Bool/string-returning variants of the quick actions above for engine
    // callers (chat commands, workflows): never throw, log at info level on
    // failure (same contract as TimeoutUserAsync/BanUserAsync). Named
    // *UrlAsync/*CancelRaidAsync where CreateClipAsync/StopRaidAsync already
    // exist above with TwitchActionResponse returns for the UI.

    /// <summary>
    /// Creates a clip via POST /helix/clips and returns its URL
    /// (https://clips.twitch.tv/{id}), or null on failure. Clips finalize
    /// asynchronously on Twitch's side, so the URL is returned immediately
    /// after creation. Requires the clips:edit scope.
    /// </summary>
    Task<string?> CreateClipUrlAsync();

    /// <summary>
    /// Starts a raid via POST /helix/raids after resolving the target login
    /// to a user id. Requires channel:manage:raids. Returns false (never
    /// throws) when the target can't be resolved or Twitch rejects the raid.
    /// </summary>
    Task<bool> StartRaidAsync(string targetLogin);

    /// <summary>
    /// Cancels the outgoing raid via DELETE /helix/raids. Requires
    /// channel:manage:raids. Returns false (never throws) on failure.
    /// </summary>
    Task<bool> CancelRaidAsync();

    /// <summary>
    /// Updates title and/or category via PATCH /helix/channels; only the
    /// provided fields are sent (null = unchanged). Unlike
    /// <see cref="EditChannelInfoAsync"/> this takes a raw Twitch game id
    /// (resolve names via <see cref="GetTopGamesAsync"/>). Requires the
    /// channel:manage:broadcast scope.
    /// </summary>
    Task<bool> UpdateChannelInfoAsync(string? title, string? gameId);

    /// <summary>
    /// Reads the broadcaster's current title and game name via
    /// GET /helix/channels; null when unavailable.
    /// </summary>
    Task<(string Title, string GameName)?> GetChannelInfoAsync();

    /// <summary>
    /// Looks up games as a "id:name;id:name;..." string for !game-style
    /// commands. With a query: GET /helix/games?name= (exact match only)
    /// first, then GET /helix/search/categories as a partial-match fallback
    /// (limitation: Helix has no fuzzy game search, so misspelled names only
    /// match via the search endpoint's own matching rules). Without a query:
    /// the current top games. Null when nothing matches or the call fails.
    /// </summary>
    Task<string?> GetTopGamesAsync(string? query = null, int limit = 20);

    /// <summary>
    /// Runs a commercial via POST /helix/channels/commercial. The length is
    /// clamped to Twitch's allowed values (30/60/90/120/150/180 seconds).
    /// Requires the channel:edit:commercial scope on the broadcaster's own
    /// token; returns false (never throws) otherwise.
    /// </summary>
    Task<bool> StartCommercialAsync(int seconds);

    /// <summary>
    /// Live stream uptime from GET /helix/streams started_at (now-UTC
    /// delta); null when the channel is offline or the call fails.
    /// </summary>
    Task<TimeSpan?> GetStreamUptimeAsync();

    /// <summary>
    /// How long the given user has followed the broadcaster, from
    /// GET /helix/channels/followers followed_at (now-UTC delta); null when
    /// they don't follow, the login can't be resolved, or the call fails.
    /// Requires the moderator:read:followers scope.
    /// </summary>
    Task<TimeSpan?> GetFollowAgeAsync(string login);
}
