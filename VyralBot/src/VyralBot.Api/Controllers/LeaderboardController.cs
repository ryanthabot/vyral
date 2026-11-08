using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using VyralBot.Core.Services.Currency;

namespace VyralBot.Api.Controllers;

/// <summary>
/// Loopback JSON surface for the points-leaderboard browser source
/// (wwwroot/leaderboard.html, added to OBS). Same trust model as the /songs
/// GET route: not part of the token-gated control API — the embedded server
/// binds loopback only, the page is served from this same origin, and this
/// controller is read-only, so no POST-style Origin gate is needed.
///
/// Routes:
/// - GET /leaderboard/data?count=10&amp;platform= — top N viewers by currency
///   balance via <see cref="ICurrencyService.GetLeaderboardAsync"/> (per-profile
///   data, deterministic tiebreakers). count is size-capped to 1..25; platform
///   is optional and must match a known platform (case-insensitive).
///
/// Balances live in the per-profile UserCurrency table via the shared
/// <see cref="ICurrencyService"/> (the same query the Currency page and
/// !leaderboard chat command use); this controller only reads it.
/// </summary>
[ApiController]
[Route("leaderboard")]
public class LeaderboardController : ControllerBase
{
    /// <summary>Upper bound for the count param so a hand-edited OBS URL cannot request an unbounded dump.</summary>
    private const int MaxCount = 25;

    /// <summary>
    /// Canonical platform names the widget can filter on (the
    /// PlatformToBrushConverter set) — normalizes case-insensitive URL input
    /// ("twitch") to the casing stored in UserCurrency rows ("Twitch").
    /// </summary>
    private static readonly Dictionary<string, string> KnownPlatforms = new(StringComparer.OrdinalIgnoreCase)
    {
        ["twitch"] = "Twitch",
        ["kick"] = "Kick",
        ["youtube"] = "YouTube",
        ["tiktok"] = "TikTok",
        ["discord"] = "Discord",
        ["facebook"] = "Facebook",
        ["vyral"] = "Vyral",
    };

    private readonly ILogger<LeaderboardController> _logger;
    private readonly ICurrencyService? _currency;

    public LeaderboardController(
        ILogger<LeaderboardController> logger,
        ICurrencyService? currency = null)
    {
        _logger = logger;
        _currency = currency;
    }

    /// <summary>Top N by balance (the leaderboard overlay polls this every 10s).</summary>
    [HttpGet("data")]
    public async Task<IActionResult> Data([FromQuery] int count = 10, [FromQuery] string? platform = null)
    {
        if (_currency == null)
        {
            _logger.LogInformation("/leaderboard request while the currency service is not registered - returning 503");
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "leaderboard_disabled" });
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

        var entries = await _currency.GetLeaderboardAsync(effectiveCount, canonicalPlatform);
        var settings = await _currency.GetSettingsAsync();

        Response.Headers.CacheControl = "no-store"; // overlay polls every 10s

        // Anonymous objects serialize camelCase by default (SongsController
        // convention); the query is already ordered by balance DESC.
        var rank = 0;
        return Ok(new
        {
            currencyName = settings?.CurrencyName ?? "Points",
            currencySymbol = settings?.CurrencySymbol,
            count = effectiveCount,
            platform = canonicalPlatform,
            entries = entries.Select(entry => new
            {
                rank = ++rank,
                username = entry.Username,
                platform = entry.Platform,
                balance = entry.Balance
            }).ToList()
        });
    }
}
