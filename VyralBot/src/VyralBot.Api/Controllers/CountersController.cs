using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using VyralBot.Core.Services.Counters;

namespace VyralBot.Api.Controllers;

/// <summary>
/// Loopback JSON surface for the death-counter OBS browser source
/// (wwwroot/deathcounter.html, added to OBS). Not part of the token-gated
/// control API — same reasoning as /songs: the embedded server binds loopback
/// only, the page is served from this same origin, and the route is a bare
/// GET with no side effects, so no Origin check is needed.
///
/// Routes:
/// - GET /counters/death — { count } snapshot (the widget polls every 3s)
///
/// Counter state lives in the per-profile config rows written by
/// <see cref="IDeathCounterService"/> (the !death chat command family and the
/// Tools page card mutate the same values); this controller only reads.
/// </summary>
[ApiController]
[Route("counters")]
public class CountersController : ControllerBase
{
    private readonly ILogger<CountersController> _logger;
    private readonly IDeathCounterService? _counters;

    public CountersController(ILogger<CountersController> logger, IDeathCounterService? counters = null)
    {
        _logger = logger;
        _counters = counters;
    }

    /// <summary>Current death count (the widget polls this every 3s).</summary>
    [HttpGet("death")]
    public async Task<IActionResult> Death()
    {
        if (_counters is null)
        {
            _logger.LogInformation("/counters/death request while the counter service is not registered - returning 503");
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "counters_disabled" });
        }

        var count = await _counters.GetCountAsync();
        Response.Headers.CacheControl = "no-store"; // widget polls every 3s
        return Ok(new { count });
    }
}
