using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using VyralBot.Core.Services;
using VyralBot.Data.Entities;

namespace VyralBot.Api.Controllers;

/// <summary>
/// Loopback JSON surface for the poll overlay browser source
/// (wwwroot/polloverlay.html, added to OBS). Same trust model as the
/// /leaderboard and /songs GET routes: not part of the token-gated control
/// API — the embedded server binds loopback only, the page is served from
/// this same origin, and this controller is read-only.
///
/// Routes:
/// - GET /poll/data — { active, lastResults } snapshot. <c>active</c> is the
///   currently running poll with live per-option votes and the optional
///   scheduled end (set by "!poll start 5m …"); <c>lastResults</c> is the
///   most recently completed poll and lingers for 5 minutes so the overlay
///   can show the winner bars after the poll ends. Both are null when not
///   applicable — the widget hides itself.
///
/// Vote counts come from the per-profile PollVotes table via the shared
/// <see cref="PollService"/> (the same rows "!vote"/"!poll vote" write);
/// this controller only reads them.
/// </summary>
[ApiController]
[Route("poll")]
public class PollController : ControllerBase
{
    /// <summary>How long a completed poll keeps showing on the overlay.</summary>
    private static readonly TimeSpan ResultsLinger = TimeSpan.FromMinutes(5);

    private readonly ILogger<PollController> _logger;
    private readonly PollService? _pollService;

    public PollController(
        ILogger<PollController> logger,
        PollService? pollService = null)
    {
        _logger = logger;
        _pollService = pollService;
    }

    /// <summary>Poll snapshot (the overlay polls this every 3s).</summary>
    [HttpGet("data")]
    public async Task<IActionResult> Data()
    {
        if (_pollService == null)
        {
            _logger.LogInformation("/poll request while the poll service is not registered - returning 503");
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "poll_disabled" });
        }

        var active = await _pollService.GetActivePollAsync();

        // Most recent completed poll, but only within the linger window.
        var cutoff = DateTime.UtcNow - ResultsLinger;
        var completed = (await _pollService.GetAllPollsAsync())
            .Where(p => p.Status == PollStatus.Completed && p.EndedAt != null)
            .Select(p => (Poll: p, EndedUtc: NormalizeUtc(p.EndedAt!.Value)))
            .Where(x => x.EndedUtc >= cutoff)
            .OrderByDescending(x => x.EndedUtc)
            .Select(x => x.Poll)
            .FirstOrDefault();

        Response.Headers.CacheControl = "no-store"; // overlay polls every 3s

        // Anonymous objects serialize camelCase by default (SongsController
        // convention); option order is the poll's stored option order.
        return Ok(new
        {
            active = active != null && active.Status == PollStatus.Active
                ? BuildSnapshot(active, await _pollService.GetPollVotesAsync(active.Id), includeEndsAt: true)
                : null,
            lastResults = completed != null
                ? BuildSnapshot(completed, await _pollService.GetPollVotesAsync(completed.Id), includeEndsAt: false)
                : null
        });
    }

    /// <summary>
    /// Shapes one poll into the overlay payload: ordered options with votes +
    /// percent of total, and — for finished polls — the winner label (null on
    /// ties and zero-vote polls, mirroring PollService.FormatResultsMessage).
    /// </summary>
    private static object BuildSnapshot(Poll poll, IEnumerable<PollVote> votes, bool includeEndsAt)
    {
        var options = JsonSerializer.Deserialize<string[]>(poll.Options) ?? Array.Empty<string>();

        // Index-keyed (NOT option-text-keyed) so duplicate option labels can
        // never merge two bars.
        var counts = votes
            .GroupBy(v => v.OptionIndex)
            .ToDictionary(g => g.Key, g => g.Count());

        var total = counts.Values.Sum();
        var snapshotOptions = new List<object>(options.Length);
        for (var i = 0; i < options.Length; i++)
        {
            var optionVotes = counts.TryGetValue(i, out var c) ? c : 0;
            snapshotOptions.Add(new
            {
                index = i + 1,
                text = options[i],
                votes = optionVotes,
                percent = total > 0 ? Math.Round(optionVotes * 100.0 / total, 1) : 0.0
            });
        }

        string? winner = null;
        var isTie = false;
        if (!includeEndsAt && total > 0)
        {
            var top = counts.Where(kvp => kvp.Key >= 0 && kvp.Key < options.Length)
                .Select(kvp => kvp.Value)
                .DefaultIfEmpty(0)
                .Max();
            var winners = Enumerable.Range(0, options.Length).Where(i => counts.TryGetValue(i, out var c) && c == top).ToList();
            if (winners.Count == 1)
            {
                winner = options[winners[0]];
            }
            else
            {
                isTie = true;
            }
        }

        return new
        {
            question = poll.Question,
            options = snapshotOptions,
            totalVotes = total,
            endsAt = includeEndsAt ? NormalizeUtc(poll.ScheduledEndTime)?.ToString("o") : null,
            endedAt = includeEndsAt ? null : NormalizeUtc(poll.EndedAt)?.ToString("o"),
            winner,
            isTie
        };
    }

    /// <summary>
    /// Poll timestamps are persisted as "o"-format TEXT and the SQLite/Dapper
    /// read path may hand them back with Kind Local or Unspecified; every
    /// stored poll time is UTC by convention (PollService/VM write UTC), so
    /// normalize the kind before serializing/ comparing.
    /// </summary>
    private static DateTime? NormalizeUtc(DateTime? value)
        => value == null
            ? null
            : NormalizeUtc(value.Value);

    private static DateTime NormalizeUtc(DateTime value)
        => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
}
