using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using VyralBot.Data;
using VyralBot.Data.Entities;

namespace VyralBot.Core.Services.Songs;

/// <summary>
/// Handles the !sr command family (streamer.bot staple) on top of the
/// per-profile SongRequests table. Invoked from the ChatViewModel chat
/// pipeline exactly like QuoteService: the returned string is sent on the
/// ORIGINATING platform (Twitch/Kick/YouTube/Discord) by the caller;
/// returning null lets other handlers process the message.
///
/// Commands:
///   !sr &lt;url|id&gt; — request a song (everyone; YouTube URL or 11-char id only,
///                  search text is not supported)
///   !srqueue     — next 3 queued songs (everyone)
///   !srskip      — skip the current song (moderator+)
///   !srclear     — clear the whole queue (moderator+)
///
/// Titles are resolved without an API key: requests are stored as
/// "Video &lt;id&gt;" and the player updates them later via
/// <see cref="UpdateTitleAsync"/> (the <see cref="ITitleResolver"/> seam
/// returns null by default).
/// </summary>
public partial class SongRequestService : ISongRequestService
{
    /// <summary>Config key: max queued requests per user (default 2).</summary>
    private const string ConfigMaxPerUser = "Songs.MaxPerUser";

    /// <summary>Config key: max total queued requests (default 30).</summary>
    private const string ConfigMaxQueue = "Songs.MaxQueue";

    /// <summary>Default per-user queue cap.</summary>
    private const int DefaultMaxPerUser = 2;

    /// <summary>Default total queue cap.</summary>
    private const int DefaultMaxQueue = 30;

    /// <summary>Queued songs shown per !srqueue reply, to keep chat replies short.</summary>
    private const int QueueReplyLimit = 3;

    /// <summary>Character cap per song title in chat replies.</summary>
    private const int TitleExcerptLength = 40;

    private readonly ILogger<SongRequestService> _logger;
    private readonly IDataService _dataService;
    private readonly ITitleResolver _titleResolver;

    public SongRequestService(
        ILogger<SongRequestService> logger,
        IDataService dataService,
        ITitleResolver? titleResolver = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
        _titleResolver = titleResolver ?? new NullTitleResolver();
    }

    /// <inheritdoc />
    public async Task<string?> ProcessCommandAsync(string command, string username, string platform, bool isModerator)
    {
        if (string.IsNullOrWhiteSpace(command) || !command.StartsWith("!"))
            return null;

        var parts = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var cmd = parts[0].ToLowerInvariant();

        if (cmd != "!sr" && cmd != "!srqueue" && cmd != "!srskip" && cmd != "!srclear")
            return null; // Not a song request command — let the pipeline continue

        try
        {
            switch (cmd)
            {
                case "!srqueue":
                    return await HandleQueueAsync();
                case "!srskip":
                    return isModerator
                        ? await HandleSkipAsync()
                        : "You must be a moderator to skip songs";
                case "!srclear":
                    return isModerator
                        ? await HandleClearAsync()
                        : "You must be a moderator to clear the song queue";
                default:
                    return await HandleRequestAsync(parts, username, platform);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing song request command: {Command}", command);
            return "An error occurred while processing the song request.";
        }
    }

    private async Task<string> HandleRequestAsync(string[] parts, string username, string platform)
    {
        if (parts.Length < 2)
        {
            return "Usage: !sr <YouTube link or 11-character video ID>";
        }

        var result = await EnqueueAsync(parts[1], username, platform);
        if (!result.Success)
        {
            return result.Error;
        }

        var queue = (await _dataService.GetSongQueueAsync()).ToList();
        var position = queue.FindIndex(r => r.Id == result.Request!.Id) + 1;
        return $"Added \"{Excerpt(result.Request.Title)}\" to the song queue (position #{position})";
    }

    private async Task<string> HandleQueueAsync()
    {
        var queue = (await _dataService.GetSongQueueAsync()).ToList();
        if (queue.Count == 0)
        {
            return "The song queue is empty — request one with !sr <YouTube link>";
        }

        var entries = queue.Take(QueueReplyLimit)
            .Select((r, i) => $"{i + 1}. {Excerpt(r.Title)} ({r.RequestedBy})");
        return $"Song queue ({queue.Count}): {string.Join(" | ", entries)}";
    }

    private async Task<string> HandleSkipAsync()
    {
        var skipped = await SkipCurrentAsync();
        return skipped == null
            ? "Nothing is currently playing."
            : $"Skipped \"{Excerpt(skipped.Title)}\".";
    }

    private async Task<string> HandleClearAsync()
    {
        var removed = await ClearAsync();
        return removed == 1
            ? "Song queue cleared (1 request removed)."
            : $"Song queue cleared ({removed} requests removed).";
    }

    /// <summary>Chat-ready truncation of a song title.</summary>
    private static string Excerpt(string title)
    {
        return string.IsNullOrWhiteSpace(title)
            ? "unknown"
            : title.Length <= TitleExcerptLength ? title : title[..(TitleExcerptLength - 1)] + "…";
    }

    // ===== Queue surface (player / UI) =====

    /// <inheritdoc />
    public async Task<SongRequestResult> EnqueueAsync(string youtubeUrlOrId, string requestedBy, string platform)
    {
        var input = (youtubeUrlOrId ?? string.Empty).Trim();
        var youtubeId = TryParseYoutubeId(input);
        if (youtubeId == null)
        {
            return SongRequestResult.Fail(
                "Please provide a YouTube link or 11-character video ID — searching by name is not supported.");
        }

        var maxPerUser = ParseInt(await _dataService.GetConfigValueAsync(ConfigMaxPerUser), DefaultMaxPerUser);
        var maxQueue = ParseInt(await _dataService.GetConfigValueAsync(ConfigMaxQueue), DefaultMaxQueue);

        var userCount = await _dataService.GetQueuedSongRequestCountByUserAsync(requestedBy);
        if (userCount >= maxPerUser)
        {
            return SongRequestResult.Fail(
                $"You already have {userCount} song(s) in the queue (max {maxPerUser} per viewer).");
        }

        var totalCount = await _dataService.GetQueuedSongRequestCountAsync();
        if (totalCount >= maxQueue)
        {
            return SongRequestResult.Fail($"The song queue is full ({totalCount}/{maxQueue}).");
        }

        // No API key: the resolver returns null by default and the player page
        // reports the real title later via UpdateTitleAsync.
        var resolved = await _titleResolver.ResolveAsync(youtubeId);
        var title = resolved?.Title ?? $"Video {youtubeId}";

        var request = await _dataService.AddSongRequestAsync(new SongRequest
        {
            RequestedBy = requestedBy,
            Platform = platform,
            YoutubeId = youtubeId,
            Title = title,
            Status = SongRequestStatuses.Queued,
            DurationSeconds = resolved?.DurationSeconds
        });

        _logger.LogInformation(
            "Song request queued: {YoutubeId} by {Username} on {Platform} (queue {Total}/{MaxQueue})",
            youtubeId, requestedBy, platform, totalCount + 1, maxQueue);
        return SongRequestResult.Ok(request);
    }

    /// <inheritdoc />
    public Task<IEnumerable<SongRequest>> GetQueueAsync() => _dataService.GetSongQueueAsync();

    /// <summary>The currently-playing request, if any (the player page's "now playing").</summary>
    public Task<SongRequest?> GetCurrentSongRequestAsync() => _dataService.GetCurrentSongRequestAsync();

    /// <inheritdoc />
    public async Task<SongRequest?> MarkPlayingAsync()
    {
        var next = (await _dataService.GetSongQueueAsync()).FirstOrDefault();
        if (next == null)
        {
            return null;
        }

        if (!await _dataService.SetSongRequestStatusAsync(next.Id, SongRequestStatuses.Playing))
        {
            return null;
        }

        next.Status = SongRequestStatuses.Playing;
        _logger.LogInformation("Song request now playing: {YoutubeId} (id {Id})", next.YoutubeId, next.Id);
        return next;
    }

    /// <inheritdoc />
    public async Task<SongRequest?> MarkPlayedAsync()
    {
        var current = await _dataService.GetCurrentSongRequestAsync();
        if (current == null || !await _dataService.SetSongRequestStatusAsync(current.Id, SongRequestStatuses.Played))
        {
            return null;
        }

        current.Status = SongRequestStatuses.Played;
        _logger.LogInformation("Song request finished: {YoutubeId} (id {Id})", current.YoutubeId, current.Id);
        return current;
    }

    /// <inheritdoc />
    public async Task<SongRequest?> SkipCurrentAsync()
    {
        var current = await _dataService.GetCurrentSongRequestAsync();
        if (current == null || !await _dataService.SetSongRequestStatusAsync(current.Id, SongRequestStatuses.Skipped))
        {
            return null;
        }

        current.Status = SongRequestStatuses.Skipped;
        _logger.LogInformation("Song request skipped: {YoutubeId} (id {Id})", current.YoutubeId, current.Id);
        return current;
    }

    /// <inheritdoc />
    public async Task<int> ClearAsync()
    {
        var removed = await _dataService.ClearSongRequestsAsync();
        _logger.LogInformation("Song queue cleared ({Removed} request(s) removed)", removed);
        return removed;
    }

    /// <inheritdoc />
    public async Task<int> UpdateTitleAsync(string youtubeId, string title, int? durationSeconds = null)
    {
        var updated = await _dataService.UpdateSongRequestTitleAsync(youtubeId, title, durationSeconds);
        if (updated > 0)
        {
            _logger.LogInformation(
                "Song request title resolved: {YoutubeId} -> \"{Title}\" ({Updated} row(s))",
                youtubeId, title, updated);
        }

        return updated;
    }

    /// <summary>
    /// Extracts an 11-character YouTube video id from a URL
    /// (watch?v=, youtu.be/, live/, shorts/, embed/, music.youtube.com) or
    /// accepts a bare 11-character id. Returns null for anything else
    /// (including "search text" — unsupported by design).
    /// </summary>
    internal static string? TryParseYoutubeId(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        var trimmed = input.Trim();
        if (BareIdRegex().IsMatch(trimmed))
        {
            return trimmed;
        }

        var match = UrlIdRegex().Match(trimmed);
        return match.Success ? match.Groups[1].Value : null;
    }

    /// <summary>Bare 11-character video id ([A-Za-z0-9_-]{11}).</summary>
    [GeneratedRegex(@"^[A-Za-z0-9_-]{11}$")]
    private static partial Regex BareIdRegex();

    /// <summary>Video id inside the supported YouTube URL shapes.</summary>
    [GeneratedRegex(
        @"(?:youtube\.com/(?:watch\?(?:[^#\s]*&)?v=|live/|shorts/|embed/)|youtu\.be/)([A-Za-z0-9_-]{11})",
        RegexOptions.IgnoreCase)]
    private static partial Regex UrlIdRegex();

    private static int ParseInt(string? raw, int fallback) =>
        int.TryParse(raw, out var value) && value > 0 ? value : fallback;
}
