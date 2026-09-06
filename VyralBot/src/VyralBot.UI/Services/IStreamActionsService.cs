using VyralBot.Core.Services.EventBus.Events;

namespace VyralBot.UI.Services;

/// <summary>Result of a stream action, with a user-presentable message.</summary>
/// <param name="Success">True when the platform accepted the action.</param>
/// <param name="Message">Text safe to show in chat/status UI (includes a re-auth hint on permission failures).</param>
public record StreamActionResult(bool Success, string Message);

/// <summary>Current Twitch chat moderation modes (from GET /helix/chat/settings + shield status).</summary>
public record TwitchChatSettingsState(bool EmoteOnly, bool FollowerOnly, bool ShieldMode);

/// <summary>
/// Executes stream-management quick actions against the connected platforms.
/// Twitch actions use the Helix API; Kick actions use documented mod chat commands
/// sent as the bot account; YouTube uses the LiveBroadcasts API.
/// </summary>
public interface IStreamActionsService
{
    // ── Twitch (Helix) ────────────────────────────────────────────────
    /// <summary>Update channel title and/or category (category resolved by name via /helix/search/categories). Pass null to leave unchanged.</summary>
    Task<StreamActionResult> TwitchEditStreamInfoAsync(string? title, string? category);

    /// <summary>Create a stream marker (stream must be live). Null description = untitled marker.</summary>
    Task<StreamActionResult> TwitchCreateMarkerAsync(string? description);

    /// <summary>Create a clip and return its URL in the message.</summary>
    Task<StreamActionResult> TwitchCreateClipAsync();

    /// <summary>Raid the given channel (username, without #).</summary>
    Task<StreamActionResult> TwitchRaidAsync(string targetChannel);

    /// <summary>Cancel an outgoing raid (Helix DELETE /helix/raids).</summary>
    Task<StreamActionResult> TwitchStopRaidAsync();

    /// <summary>Clear all chat messages in the channel.</summary>
    Task<StreamActionResult> TwitchClearChatAsync();

    Task<StreamActionResult> TwitchSetEmoteOnlyAsync(bool enabled);
    Task<StreamActionResult> TwitchSetFollowerOnlyAsync(bool enabled);

    /// <summary>Toggle Shield Mode on the channel.</summary>
    Task<StreamActionResult> TwitchSetShieldModeAsync(bool enabled);

    /// <summary>Fetch current moderation mode state (null when offline of info unavailable).</summary>
    Task<TwitchChatSettingsState?> TwitchGetChatSettingsAsync();

    // ── Kick (mod chat commands sent as the bot account) ──────────────
    Task<StreamActionResult> KickClearChatAsync();
    Task<StreamActionResult> KickSetEmoteOnlyAsync(bool enabled);
    Task<StreamActionResult> KickSetFollowerOnlyAsync(bool enabled);
    Task<StreamActionResult> KickSetSlowModeAsync(bool enabled);

    // ── YouTube (LiveBroadcasts API) ──────────────────────────────────
    /// <summary>Update the active broadcast's title and/or description. Pass null to leave unchanged.</summary>
    Task<StreamActionResult> YouTubeUpdateStreamInfoAsync(string? title, string? description);
}
