namespace VyralBot.Core.Services.Commands;

/// <summary>
/// The chat moderation modes the <c>!emoteonly/!subonly/!followers/!slow</c>
/// command family can toggle on Twitch (Helix chat settings).
/// </summary>
public enum TwitchChatMode
{
    /// <summary>Only Twitch emotes may be sent (<c>emote_mode</c>).</summary>
    EmoteOnly,

    /// <summary>Only subscribers may chat (<c>subscriber_mode</c>).</summary>
    SubscriberOnly,

    /// <summary>Only followers may chat (<c>follower_mode</c>).</summary>
    FollowerOnly,

    /// <summary>Rate-limited chat (<c>slow_mode</c>).</summary>
    Slow
}

/// <summary>
/// Twitch chat-mode seam for <see cref="ChatModeCommandService"/>: turns a
/// single <see cref="TwitchChatMode"/> on or off. Implemented by the
/// Integrations <c>TwitchService</c> (via PUT /helix/chat/settings) and
/// registered in App.xaml.cs as a cast of the <c>ITwitchService</c> singleton,
/// so Core's command service can drive Twitch without referencing the
/// Integrations project (Integrations → Core only, never the reverse).
/// </summary>
public interface ITwitchChatModeApplier
{
    /// <summary>Whether the underlying Twitch connection is live.</summary>
    bool IsConnected { get; }

    /// <summary>
    /// Applies one chat moderation mode via PUT /helix/chat/settings.
    /// </summary>
    /// <param name="mode">The mode to change; other modes are left untouched.</param>
    /// <param name="enabled">True to enable, false to disable the mode.</param>
    /// <param name="duration">
    /// Mode-dependent, only used when enabling: required follow age in MINUTES
    /// for <see cref="TwitchChatMode.FollowerOnly"/> (0/omitted = any follower)
    /// and the slow-mode wait in SECONDS for <see cref="TwitchChatMode.Slow"/>.
    /// Clamped to the Helix bounds by the implementation; ignored when
    /// disabling and by the simple boolean modes.
    /// </param>
    /// <returns>
    /// True when Twitch accepted the change; false (never throws) on any
    /// failure — the implementation info-logs the reason.
    /// </returns>
    Task<bool> ApplyChatModeAsync(TwitchChatMode mode, bool enabled, int? duration = null);
}
