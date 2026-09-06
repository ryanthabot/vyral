namespace VyralBot.Core.Services.StreamStatus;

/// <summary>
/// Tracks the live/offline status of streaming platforms.
/// Platform identities are strings ("Twitch", "Kick", "YouTube"), matched
/// OrdinalIgnoreCase. Platforms that were never reported (e.g. Discord) are
/// always NOT live; the watchtime gate excludes Discord separately.
/// </summary>
public interface IStreamLiveRegistry
{
    /// <summary>
    /// Returns whether the platform's stream is currently live.
    /// Unknown, null or empty platform names return <c>false</c>.
    /// </summary>
    bool IsLive(string platform);

    /// <summary>
    /// Updates the platform's live status.
    /// <see cref="StreamLiveChanged"/> fires only when the value actually changes.
    /// Null/empty/whitespace platform names are ignored.
    /// </summary>
    void SetLive(string platform, bool isLive);

    /// <summary>
    /// Raised when a platform transitions between live/offline.
    /// The event argument is the platform name as passed to <see cref="SetLive"/>.
    /// </summary>
    event EventHandler<string>? StreamLiveChanged;
}
