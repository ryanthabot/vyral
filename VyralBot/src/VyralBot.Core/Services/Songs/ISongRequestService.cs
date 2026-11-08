using VyralBot.Data.Entities;

namespace VyralBot.Core.Services.Songs;

/// <summary>
/// Resolves a YouTube video's title/duration for a song request. Without an
/// API key VyralBot cannot look titles up at enqueue time, so the default
/// <see cref="NullTitleResolver"/> returns null and requests are stored as
/// "Video &lt;id&gt;" until the player page reports the real title through
/// <see cref="ISongRequestService.UpdateTitleAsync"/>.
/// </summary>
public interface ITitleResolver
{
    /// <summary>Title/duration for a video id, or null when unresolvable.</summary>
    Task<SongTitleInfo?> ResolveAsync(string youtubeId);
}

/// <summary>Resolved title metadata for one YouTube video.</summary>
public sealed record SongTitleInfo(string Title, int? DurationSeconds);

/// <summary>
/// Default no-op resolver: no API key, no metadata API — titles arrive later
/// from the player via <see cref="ISongRequestService.UpdateTitleAsync"/>.
/// </summary>
public sealed class NullTitleResolver : ITitleResolver
{
    public Task<SongTitleInfo?> ResolveAsync(string youtubeId) => Task.FromResult<SongTitleInfo?>(null);
}

/// <summary>Outcome of <see cref="ISongRequestService.EnqueueAsync"/> — Success or a chat-ready Error.</summary>
public sealed class SongRequestResult
{
    public bool Success { get; init; }
    public string Error { get; init; } = "";
    public SongRequest? Request { get; init; }

    public static SongRequestResult Ok(SongRequest request) => new() { Success = true, Request = request };
    public static SongRequestResult Fail(string error) => new() { Success = false, Error = error };
}

/// <summary>
/// Chat-driven song request queue (streamer.bot staple): viewers enqueue
/// YouTube videos with "!sr &lt;link&gt;", the queue drains through
/// Playing → Played/Skipped. Mirrors the QuoteService chat-pipeline contract —
/// <see cref="ProcessCommandAsync"/> returns the ready-to-send reply, or null
/// when the message is not a song request command (ChatViewModel falls through).
/// </summary>
public interface ISongRequestService
{
    /// <summary>
    /// Processes a song request command (!sr, !srqueue, !srskip, !srclear) and
    /// returns the chat reply, or null when the message is not a song command.
    /// </summary>
    /// <param name="command">Raw chat message text.</param>
    /// <param name="username">Sender's username (stored as RequestedBy on !sr).</param>
    /// <param name="platform">Originating platform (stored as the request's Platform source).</param>
    /// <param name="isModerator">Sender's moderator status — gates !srskip/!srclear.</param>
    Task<string?> ProcessCommandAsync(string command, string username, string platform, bool isModerator);

    // ===== Queue surface (player / UI) =====

    /// <summary>
    /// Validates and enqueues one request. Input must be a YouTube URL
    /// (watch/youtu.be/live/shorts/embed) or a bare 11-character video id —
    /// search text is NOT supported. Enforces the per-user cap
    /// (config "Songs.MaxPerUser", default 2) and the total cap
    /// (config "Songs.MaxQueue", default 30).
    /// </summary>
    Task<SongRequestResult> EnqueueAsync(string youtubeUrlOrId, string requestedBy, string platform);

    /// <summary>Queued requests in queue order (oldest first).</summary>
    Task<IEnumerable<SongRequest>> GetQueueAsync();

    /// <summary>The currently-playing request, if any.</summary>
    Task<SongRequest?> GetCurrentSongRequestAsync();

    /// <summary>Promotes the first Queued request to Playing (returns it, or null when the queue is empty).</summary>
    Task<SongRequest?> MarkPlayingAsync();

    /// <summary>Marks the current Playing request as Played (returns it, or null when nothing is playing).</summary>
    Task<SongRequest?> MarkPlayedAsync();

    /// <summary>Marks the current Playing request as Skipped (mod skip — returns it, or null when nothing is playing).</summary>
    Task<SongRequest?> SkipCurrentAsync();

    /// <summary>Clears every song request row (mod "!srclear"); returns rows removed.</summary>
    Task<int> ClearAsync();

    /// <summary>
    /// Updates Title/DurationSeconds on every queued/playing row with this
    /// video id — the seam the song player uses once the player page knows the
    /// real title (no API key needed at enqueue time).
    /// </summary>
    Task<int> UpdateTitleAsync(string youtubeId, string title, int? durationSeconds = null);
}
