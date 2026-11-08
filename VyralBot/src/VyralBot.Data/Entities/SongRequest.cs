namespace VyralBot.Data.Entities;

/// <summary>
/// One row of the SongRequests table: a chat-driven song request
/// (streamer.bot staple) enqueued with "!sr &lt;YouTube link or id&gt;" on any
/// platform. Mirrors the Quotes pattern: per-profile SQLite, small stable
/// autoincrement Id, status lifecycle Queued → Playing → Played/Skipped.
/// </summary>
public class SongRequest
{
    /// <summary>Stable small per-profile number (SQLite autoincrement PK), queue order = Id ASC.</summary>
    public int Id { get; set; }

    /// <summary>Chat username that requested the song.</summary>
    public string RequestedBy { get; set; } = "";

    /// <summary>Platform the request came from (Twitch/Kick/YouTube/Discord).</summary>
    public string Platform { get; set; } = "";

    /// <summary>11-character YouTube video id (URLs are parsed down to this).</summary>
    public string YoutubeId { get; set; } = "";

    /// <summary>Video title. "Video &lt;id&gt;" until the player reports the real one (UpdateTitleAsync).</summary>
    public string Title { get; set; } = "";

    public DateTime RequestedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Lifecycle status: Queued, Playing, Played or Skipped (see SongRequestStatuses).</summary>
    public string Status { get; set; } = "Queued";

    /// <summary>Video duration in seconds, when known (filled by the player via UpdateTitleAsync).</summary>
    public int? DurationSeconds { get; set; }
}

/// <summary>Status values for <see cref="SongRequest.Status"/>.</summary>
public static class SongRequestStatuses
{
    public const string Queued = "Queued";
    public const string Playing = "Playing";
    public const string Played = "Played";
    public const string Skipped = "Skipped";
}
