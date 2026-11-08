using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using VyralBot.Core.Services.Linking;

namespace VyralBot.Api.Controllers;

/// <summary>
/// Loopback JSON surface for the goals-progress browser source
/// (wwwroot/goals.html, added to OBS). Same trust model as the /leaderboard
/// GET route: not part of the token-gated control API — the embedded server
/// binds loopback only, the page is served from this same origin, and this
/// controller is read-only, so no POST-style Origin gate is needed.
///
/// Routes:
/// - GET /goals/data — every enabled Vyral goal from
///   <see cref="ISocialGoalService.GetGoalsAsync"/> ordered by SortOrder,
///   mapped to display fields for the overlay (title, kind, completion count,
///   target). The completion count is an aggregate number only — no usernames,
///   viewer IDs or any other PII leave the process.
///
/// Mapping note: a Vyral goal's TargetCount is the per-viewer threshold
/// ("link 2 platforms"); the aggregate progress the service exposes is
/// GetCompletionCountAsync (how many viewers completed it). The overlay shows
/// "completions / target" with the bar capped at 100%.
/// </summary>
[ApiController]
[Route("goals")]
public class GoalsController : ControllerBase
{
    /// <summary>
    /// Display kind for each GoalType (GoalEvaluator's supported set). Unknown
    /// types degrade to the raw string so a future goal type still renders.
    /// </summary>
    private static readonly Dictionary<string, string> KindLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["LinkedPlatforms"] = "linked",
        ["FollowPlatforms"] = "follows",
        ["SubAnyPlatform"] = "subs",
        ["SubTier"] = "sub tier",
    };

    private readonly ILogger<GoalsController> _logger;
    private readonly ISocialGoalService? _goals;

    public GoalsController(
        ILogger<GoalsController> logger,
        ISocialGoalService? goals = null)
    {
        _logger = logger;
        _goals = goals;
    }

    /// <summary>Enabled goals as overlay rows (the widget polls this every 15s).</summary>
    [HttpGet("data")]
    public async Task<IActionResult> Data()
    {
        if (_goals == null)
        {
            _logger.LogInformation("/goals request while the social goal service is not registered - returning 503");
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "goals_disabled" });
        }

        var goals = (await _goals.GetGoalsAsync())
            .Where(g => g.IsEnabled)
            .OrderBy(g => g.SortOrder)
            .ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var rows = new List<object>(goals.Count);
        foreach (var goal in goals)
        {
            var current = await _goals.GetCompletionCountAsync(goal.Id);
            rows.Add(new
            {
                id = goal.Id.ToString(),
                kind = KindLabels.TryGetValue(goal.GoalType, out var label) ? label : goal.GoalType,
                title = goal.Name,
                subtitle = goal.Description,
                current,
                target = goal.TargetCount
            });
        }

        Response.Headers.CacheControl = "no-store"; // overlay polls every 15s

        // Anonymous objects serialize camelCase by default (LeaderboardController
        // convention).
        return Ok(new { goals = rows });
    }
}
