using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using VyralBot.Api.Services;
using VyralBot.Core.Services.Statistics;
using VyralBot.Integrations.Testing;

namespace VyralBot.Api.Controllers;

/// <summary>
/// Minimal, read-mostly control API for community tools. Every route sits under
/// <c>/api</c> and is gated by <see cref="Middleware.ControlApiAuthMiddleware"/>
/// (Bearer token = the existing webhook auth token; fixed-time compare; 401 JSON
/// on failure). No secrets, file paths or destination URLs appear in responses.
///
/// Routes:
/// - GET  /api/status       — uptime, platform connections, OBS/restream state, viewer counts
/// - GET  /api/stats/today  — event counts by type+platform since local midnight
/// - POST /api/events/test  — fire a canned test event (same payloads as the Event Testing page)
/// - GET  /api/version      — app version
/// </summary>
[ApiController]
[Route("api")]
public class ApiController : ControllerBase
{
    private readonly ILogger<ApiController> _logger;
    private readonly ControlApiStatusService _statusService;
    private readonly IStatisticsService _statisticsService;
    private readonly TestEventPublisher _testEventPublisher;

    public ApiController(
        ILogger<ApiController> logger,
        ControlApiStatusService statusService,
        IStatisticsService statisticsService,
        TestEventPublisher testEventPublisher)
    {
        _logger = logger;
        _statusService = statusService;
        _statisticsService = statisticsService;
        _testEventPublisher = testEventPublisher;
    }

    /// <summary>Runtime status: uptime, platforms, OBS, restream, viewers.</summary>
    [HttpGet("status")]
    public IActionResult Status()
    {
        return Ok(_statusService.BuildStatus());
    }

    /// <summary>Event counts by type+platform recorded since local midnight.</summary>
    [HttpGet("stats/today")]
    public async Task<IActionResult> StatsToday()
    {
        var localMidnight = DateTime.Today;
        var counts = await _statisticsService.GetEventCountsByTypeAsync(startDate: localMidnight);
        return Ok(new
        {
            date = localMidnight.ToString("yyyy-MM-dd"),
            sinceLocalMidnight = localMidnight,
            counts
        });
    }

    /// <summary>
    /// Fires a canned test event on the EventBus — the same payloads as the
    /// Event Testing page (<see cref="TestEventPublisher"/>). Body (≤16 KB, enforced
    /// by the auth middleware): <c>{ "type": "twitch.follow", "username": "...", "message": "..." }</c>.
    /// </summary>
    [HttpPost("events/test")]
    public async Task<IActionResult> TestEvent()
    {
        // Middleware already capped the body; read defensively with a bounded reader.
        using var reader = new StreamReader(Request.Body);
        var body = await reader.ReadToEndAsync(Request.HttpContext.RequestAborted);

        string? type = null;
        string? username = null;
        string? message = null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object)
            {
                if (root.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String) type = t.GetString();
                if (root.TryGetProperty("username", out var u) && u.ValueKind == JsonValueKind.String) username = u.GetString();
                if (root.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String) message = m.GetString();
            }
        }
        catch (JsonException)
        {
            return BadRequest(new { error = "invalid_json" });
        }

        if (string.IsNullOrWhiteSpace(type))
        {
            return BadRequest(new { error = "missing_type", supported = TestEventPublisher.SupportedEventTypes });
        }

        try
        {
            await _testEventPublisher.PublishTestEventAsync(type, username, message);
        }
        catch (ArgumentException)
        {
            return BadRequest(new { error = "unknown_event_type", supported = TestEventPublisher.SupportedEventTypes });
        }

        _logger.LogInformation("Control API fired test event {EventType}", type);
        return Ok(new { published = type });
    }

    /// <summary>App version (entry assembly informational + file version, .NET runtime).</summary>
    [HttpGet("version")]
    public IActionResult Version()
    {
        var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var fileVersion = assembly.GetName().Version?.ToString(3);

        return Ok(new
        {
            version = fileVersion,
            informationalVersion = informational,
            dotnet = Environment.Version.ToString()
        });
    }
}
