using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.RegularExpressions;
using VyralBot.Core.Services.Songs;
using VyralBot.Data;
using VyralBot.Data.Entities;

namespace VyralBot.Api.Controllers;

/// <summary>
/// Loopback JSON surface for the song-request player browser source
/// (wwwroot/songplayer.html, added to OBS). Not part of the token-gated
/// control API — like /audio-ws these routes are safe because the embedded
/// server binds loopback only and the page is served from this same origin.
///
/// Routes:
/// - GET  /songs/queue  — { current, upcoming } snapshot; lazily promotes the
///   first Queued row to Playing (the player's poll is what starts playback)
/// - POST /songs/played — player reports the current video finished/errored
///   (marks Played, promotes the next row, returns the post-advance snapshot)
/// - POST /songs/title  — player reports a resolved title/duration
///   (<see cref="ISongRequestService.UpdateTitleAsync"/>, no API key needed)
///
/// Hardening (wave-2 style): bodies capped at 4 KB (413), strict field
/// validation (YouTube id charset, bounded title/duration), and a loopback
/// Origin check on POSTs mirroring /audio-ws — browsers do not apply CORS to
/// no-cors POSTs, so a random web page must not be able to advance the queue.
///
/// Queue state lives in the per-profile SongRequests table via the shared
/// <see cref="ISongRequestService"/> (the !sr chat command family writes the
/// same rows); this controller only reads/advances it.
/// </summary>
[ApiController]
[Route("songs")]
public class SongsController : ControllerBase
{
    private const int MaxBodyBytes = 4_096; // payloads are ~100 bytes

    /// <summary>YouTube video ids are exactly 11 chars of [A-Za-z0-9_-].</summary>
    private static readonly Regex VideoIdPattern = new("^[A-Za-z0-9_-]{11}$", RegexOptions.Compiled);

    /// <summary>
    /// Serializes the promote/mark path so two browser sources polling at once
    /// cannot both promote a row and end up with two "Playing" songs.
    /// </summary>
    private static readonly SemaphoreSlim QueueGate = new(1, 1);

    private readonly ILogger<SongsController> _logger;
    private readonly ISongRequestService? _songs;
    private readonly IDataService? _data;

    public SongsController(
        ILogger<SongsController> logger,
        ISongRequestService? songs = null,
        IDataService? data = null)
    {
        _logger = logger;
        _songs = songs;
        _data = data;
    }

    /// <summary>Current + upcoming songs (the player polls this every 5s).</summary>
    [HttpGet("queue")]
    public async Task<IActionResult> Queue()
    {
        if (SongsUnavailable())
        {
            return ServiceUnavailable();
        }

        await QueueGate.WaitAsync(HttpContext.RequestAborted);
        try
        {
            var snapshot = await BuildSnapshotAsync();
            Response.Headers.CacheControl = "no-store"; // player polls every 5s
            return Ok(snapshot);
        }
        finally
        {
            QueueGate.Release();
        }
    }

    /// <summary>
    /// Player reports the current video finished (or is unplayable). Body:
    /// <c>{ "videoId": "..." }</c>. Idempotent — a stale report (second browser
    /// source) that does not match the current row is a no-op. Marks the row
    /// Played, promotes the next row and returns the post-advance snapshot.
    /// </summary>
    [HttpPost("played")]
    public async Task<IActionResult> Played()
    {
        if (SongsUnavailable())
        {
            return ServiceUnavailable();
        }

        var (okBody, body) = await TryPrepareBodyAsync();
        if (!okBody)
        {
            if (Response.HasStarted)
            {
                return new EmptyResult(); // 403/413 rejection already written
            }

            return BadRequest(new { error = "invalid_json" });
        }

        string? videoId = null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty("videoId", out var idEl) &&
                idEl.ValueKind == JsonValueKind.String)
            {
                videoId = idEl.GetString();
            }
        }
        catch (JsonException)
        {
            return BadRequest(new { error = "invalid_json" });
        }

        if (videoId != null && !IsValidVideoId(videoId))
        {
            return BadRequest(new { error = "invalid_video_id" });
        }

        await QueueGate.WaitAsync(HttpContext.RequestAborted);
        try
        {
            var current = await _data!.GetCurrentSongRequestAsync();
            if (current != null &&
                (string.IsNullOrEmpty(videoId) || string.Equals(current.YoutubeId, videoId, StringComparison.Ordinal)))
            {
                await _songs!.MarkPlayedAsync();
                await _songs.MarkPlayingAsync(); // promote the next row for the response
                _logger.LogInformation("Song marked played via player: {YoutubeId}", current.YoutubeId);
            }
            else if (current != null)
            {
                _logger.LogInformation("Ignoring stale played report for {Reported} (current is {Current})",
                    videoId, current.YoutubeId);
            }

            return Ok(await BuildSnapshotAsync());
        }
        finally
        {
            QueueGate.Release();
        }
    }

    /// <summary>
    /// Player reports a title/duration it resolved via the YouTube IFrame API
    /// for a queued video still showing the "Video &lt;id&gt;" placeholder. Body:
    /// <c>{ "id": "...", "title": "...", "duration": 123 }</c>.
    /// </summary>
    [HttpPost("title")]
    public async Task<IActionResult> Title()
    {
        if (SongsUnavailable())
        {
            return ServiceUnavailable();
        }

        var (okBody, body) = await TryPrepareBodyAsync();
        if (!okBody)
        {
            if (Response.HasStarted)
            {
                return new EmptyResult(); // 403/413 rejection already written
            }

            return BadRequest(new { error = "invalid_json" });
        }

        string? id = null;
        string? title = null;
        int? duration = null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                if (doc.RootElement.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String)
                {
                    id = idEl.GetString();
                }

                if (doc.RootElement.TryGetProperty("title", out var titleEl) && titleEl.ValueKind == JsonValueKind.String)
                {
                    title = titleEl.GetString();
                }

                if (doc.RootElement.TryGetProperty("duration", out var durationEl))
                {
                    if (durationEl.ValueKind == JsonValueKind.Number && durationEl.TryGetInt32(out var seconds))
                    {
                        duration = seconds;
                    }
                    else if (durationEl.ValueKind == JsonValueKind.Null)
                    {
                        duration = null;
                    }
                    else
                    {
                        return BadRequest(new { error = "invalid_duration" });
                    }
                }
            }
        }
        catch (JsonException)
        {
            return BadRequest(new { error = "invalid_json" });
        }

        if (!IsValidVideoId(id))
        {
            return BadRequest(new { error = "invalid_video_id" });
        }

        title = title?.Trim();
        if (string.IsNullOrEmpty(title))
        {
            return Ok(new { stored = false, updatedRows = 0 }); // nothing usable to store
        }

        if (title.Length > 200)
        {
            return BadRequest(new { error = "title_too_long" });
        }

        if (duration is < 0 or > 86_400)
        {
            duration = null; // live streams report 0 - treat as unknown
        }

        var updated = await _songs!.UpdateTitleAsync(id, title, duration);
        return Ok(new { stored = updated > 0, updatedRows = updated });
    }

    // ------------------------------------------------------------------
    // Snapshot mapping (anonymous objects serialize camelCase by default)
    // ------------------------------------------------------------------

    private async Task<object> BuildSnapshotAsync()
    {
        // Lazy start: the queue head only becomes "Playing" once a player page
        // asks for it — no browser source open, no playback state churn.
        var current = await _data!.GetCurrentSongRequestAsync()
            ?? await _songs!.MarkPlayingAsync();
        var upcoming = await _songs!.GetQueueAsync();

        return new
        {
            current = current == null ? null : Map(current),
            upcoming = upcoming.Select(Map).ToList()
        };
    }

    private static object Map(SongRequest request) => new
    {
        id = request.Id,
        videoId = request.YoutubeId,
        title = request.Title,
        durationSeconds = request.DurationSeconds,
        requester = request.RequestedBy,
        platform = request.Platform,
        requestedAtUtc = request.RequestedAt,
        status = request.Status
    };

    private bool SongsUnavailable() => _songs == null || _data == null;

    private static bool IsValidVideoId(string? videoId) =>
        !string.IsNullOrEmpty(videoId) && VideoIdPattern.IsMatch(videoId);

    // ------------------------------------------------------------------
    // Hardening helpers
    // ------------------------------------------------------------------

    /// <summary>
    /// Shared pre-parse gate for POST bodies: loopback Origin enforcement
    /// (mirrors the /audio-ws check — absent Origin means a non-browser client
    /// such as curl and is allowed), the 4 KB size cap, and JSON parsing.
    /// </summary>
    private async Task<(bool Ok, string Body)> TryPrepareBodyAsync()
    {
        var body = string.Empty;

        if (!IsLoopbackOrigin())
        {
            _logger.LogWarning("Rejected /songs POST with non-loopback Origin: {Origin}",
                Request.Headers["Origin"].ToString());
            await WriteErrorAsync(StatusCodes.Status403Forbidden, new { error = "forbidden_origin" });
            return (false, body);
        }

        if (Request.ContentLength is > MaxBodyBytes)
        {
            _logger.LogWarning("Rejected oversized /songs body ({Length} bytes)", Request.ContentLength);
            await WriteErrorAsync(StatusCodes.Status413PayloadTooLarge, new { error = "payload_too_large" });
            return (false, body);
        }

        if (HttpContext.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>() is { } sizeFeature)
        {
            sizeFeature.MaxRequestBodySize = MaxBodyBytes; // bounds chunked bodies too
        }

        using var reader = new StreamReader(Request.Body);
        body = await reader.ReadToEndAsync(Request.HttpContext.RequestAborted);
        return (body.Length > 0, body);
    }

    /// <summary>
    /// Same rule as the audio WebSocket: a presented Origin must be loopback;
    /// no Origin header (non-browser client) passes.
    /// </summary>
    private bool IsLoopbackOrigin()
    {
        var origin = Request.Headers["Origin"].ToString();
        if (string.IsNullOrEmpty(origin))
        {
            return true;
        }

        return Uri.TryCreate(origin, UriKind.Absolute, out var originUri)
            && (originUri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                || originUri.Host.Equals("127.0.0.1", StringComparison.Ordinal)
                || originUri.Host.Equals("::1", StringComparison.Ordinal));
    }

    private async Task WriteErrorAsync(int statusCode, object payload)
    {
        Response.StatusCode = statusCode;
        Response.ContentType = "application/json";
        Response.Headers.CacheControl = "no-store";
        await Response.WriteAsync(JsonSerializer.Serialize(payload), HttpContext.RequestAborted);
    }

    private ObjectResult ServiceUnavailable()
    {
        _logger.LogInformation("/songs request while the song services are not registered - returning 503");
        return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "songs_disabled" });
    }
}
