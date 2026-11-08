using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using VyralBot.Core.Services.Chat;
using VyralBot.Core.Services.EventBus;
using VyralBot.Integrations.Discord.Events;
using VyralBot.Integrations.Kick.Events;
using VyralBot.Integrations.Twitch.Events;
using VyralBot.Integrations.YouTube.Events;

namespace VyralBot.Api.Controllers;

/// <summary>
/// Loopback JSON surface for the chat-feed OBS browser source
/// (wwwroot/chatfeed.html — face-cam captions showing the last few chat
/// messages). Same trust model as the /leaderboard GET route: not part of the
/// token-gated control API — the embedded server binds loopback only, the page
/// is served from this same origin, and this controller is read-only, so no
/// POST-style Origin gate is needed.
///
/// Routes:
/// - GET /chatfeed/data?count=5&amp;platform=&amp;channel= — the last N chat
///   messages as { username, platform, message, at }, NEWEST FIRST (the widget
///   re-orders them newest-bottom for the caption stack)
///
/// Message history lives in the per-profile ChatMessages table via the shared
/// <see cref="IChatMessageStore"/> (the platform chat services write the same
/// rows through <c>AddAsync</c>); this controller only reads. The store keys
/// rows per platform+channel — an empty <c>channel</c> param selects the most
/// recent messages across all of the platform's channels so the overlay URL
/// works without knowing channel keys.
/// </summary>
[ApiController]
[Route("chatfeed")]
public class ChatFeedController : ControllerBase
{
    /// <summary>Upper bound for the count param so a hand-edited OBS URL cannot request an unbounded dump.</summary>
    private const int MaxCount = 15;

    /// <summary>
    /// Chat platforms the feed can read (the four platforms whose chat
    /// services persist messages) — normalizes case-insensitive URL input
    /// ("twitch") to the casing stored in ChatMessages rows ("Twitch").
    /// </summary>
    private static readonly Dictionary<string, string> KnownPlatforms = new(StringComparer.OrdinalIgnoreCase)
    {
        ["twitch"] = "Twitch",
        ["kick"] = "Kick",
        ["youtube"] = "YouTube",
        ["discord"] = "Discord",
    };

    private readonly ILogger<ChatFeedController> _logger;
    private readonly IChatMessageStore? _store;

    public ChatFeedController(
        ILogger<ChatFeedController> logger,
        IChatMessageStore? store = null)
    {
        _logger = logger;
        _store = store;
    }

    /// <summary>Last N chat messages, newest first (the widget polls this every 2s).</summary>
    [HttpGet("data")]
    public async Task<IActionResult> Data([FromQuery] int count = 5, [FromQuery] string? platform = null, [FromQuery] string? channel = null)
    {
        if (_store is null)
        {
            _logger.LogInformation("/chatfeed request while the chat message store is not registered - returning 503");
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "chatfeed_disabled" });
        }

        // Size cap, not an error: a browser source with a stale/hand-edited URL
        // keeps working (clamped) instead of rendering a dead overlay.
        var effectiveCount = Math.Clamp(count, 1, MaxCount);

        string? canonicalPlatform = null;
        if (!string.IsNullOrWhiteSpace(platform))
        {
            if (!KnownPlatforms.TryGetValue(platform.Trim(), out canonicalPlatform))
            {
                return BadRequest(new { error = "invalid_platform" });
            }
        }

        // Empty channel = wildcard across the platform's channels (see the
        // DapperDataService GetChatMessagesAsync contract).
        var channelKey = string.IsNullOrWhiteSpace(channel) ? string.Empty : channel.Trim();

        var platformsToRead = canonicalPlatform is null
            ? KnownPlatforms.Values.ToArray()
            : new[] { canonicalPlatform };

        var rows = new List<(string Username, string Platform, string Message, DateTime AtUtc)>();
        foreach (var platformName in platformsToRead)
        {
            // GetRecentAsync returns the platform's most recent rows in
            // oldest->newest order; each platform contributes up to count rows
            // and the merged take below keeps the globally newest N.
            var stored = await _store.GetRecentAsync(platformName, channelKey, effectiveCount);
            if (stored is null)
            {
                continue;
            }

            foreach (var evt in stored)
            {
                var row = Map(platformName, evt);
                if (row is not null)
                {
                    rows.Add(row.Value);
                }
            }
        }

        // Anonymous objects serialize camelCase by default (LeaderboardController
        // convention); the query is ordered newest first for the widget.
        var messages = rows
            .OrderByDescending(r => r.AtUtc)
            .ThenBy(r => r.Username, StringComparer.OrdinalIgnoreCase)
            .Take(effectiveCount)
            .Select(r => new { username = r.Username, platform = r.Platform, message = r.Message, at = r.AtUtc })
            .ToList();

        Response.Headers.CacheControl = "no-store"; // widget polls every 2s
        return Ok(new
        {
            count = messages.Count,
            platform = canonicalPlatform,
            channel = string.IsNullOrEmpty(channelKey) ? null : channelKey,
            messages
        });
    }

    /// <summary>
    /// Maps a platform-specific chat event (the store round-trips rows as the
    /// concrete event type per platform) to the feed DTO; non-chat events and
    /// empty rows are skipped.
    /// </summary>
    private static (string Username, string Platform, string Message, DateTime AtUtc)? Map(string platform, IEvent evt)
    {
        string? username = null;
        string? message = null;
        DateTime? at = null;

        switch (evt)
        {
            case TwitchChatMessageEvent t:
                username = string.IsNullOrWhiteSpace(t.DisplayName) ? t.Username : t.DisplayName;
                message = t.Message;
                at = t.Timestamp;
                break;
            case KickChatMessageEvent k:
                username = string.IsNullOrWhiteSpace(k.DisplayName) ? k.Username : k.DisplayName;
                message = k.Message;
                at = k.CreatedAt ?? k.Timestamp;
                break;
            case YouTubeChatMessageEvent y:
                username = string.IsNullOrWhiteSpace(y.DisplayName) ? y.Username : y.DisplayName;
                message = y.Message;
                at = y.PublishedAt ?? y.Timestamp;
                break;
            case DiscordChatMessageEvent d:
                username = d.Username;
                message = d.Message;
                at = d.Timestamp;
                break;
            default:
                return null;
        }

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(message))
        {
            return null;
        }

        return (username, platform, message, NormalizeUtc(at ?? evt.Timestamp));
    }

    /// <summary>
    /// Chat rows are written with UTC clocks but SQLite hands them back with
    /// <see cref="DateTimeKind.Unspecified"/> — pin the kind so the JSON
    /// serializer emits a round-trippable UTC instant.
    /// </summary>
    private static DateTime NormalizeUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };
}
