using Microsoft.Extensions.Logging;
using VyralBot.Data;
using VyralBot.Data.Entities;
using VyralBot.Core.Services.Currency;
using VyralBot.Core.Services.StreamStatus;

namespace VyralBot.Core.Services;

/// <summary>
/// Service for tracking user watchtime and managing viewer activity.
/// Handles periodic watchtime updates and currency rewards for watching.
/// </summary>
public class WatchtimeService
{
    private readonly IDataService _dataService;
    private readonly ILogger<WatchtimeService> _logger;
    private readonly ICurrencyService? _currencyService;
    private readonly IStreamLiveRegistry? _streamLiveRegistry;
    private readonly TimeSpan _inactiveThreshold = TimeSpan.FromMinutes(5);
    private const int MinutesPerUpdate = 1; // Track every minute

    public WatchtimeService(
        IDataService dataService,
        ILogger<WatchtimeService> logger,
        ICurrencyService? currencyService = null,
        IStreamLiveRegistry? streamLiveRegistry = null)
    {
        _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _currencyService = currencyService;
        _streamLiveRegistry = streamLiveRegistry;
    }

    /// <summary>
    /// W1T4 gating decision (pure, unit-tested in WatchtimeGatingTests):
    /// whether a viewer on <paramref name="platform"/> should accrue watch-time
    /// points this tick. Discord NEVER accrues (it has no live concept);
    /// every other platform accrues only while it is live. When the live
    /// status is unknown (<paramref name="isLive"/> is null — e.g. no
    /// IStreamLiveRegistry is wired) stream platforms accrue as before so
    /// existing callers stay back-compat.
    /// </summary>
    /// <param name="platform">Viewer platform ("Twitch", "Kick", "YouTube", "Discord", ...).</param>
    /// <param name="isLive">Live status of the platform; null when unknown / no registry.</param>
    public static bool ShouldAccrueWatchtime(string? platform, bool? isLive)
    {
        // Discord has no live concept: its viewers never earn watch-time points.
        if (string.Equals(platform, "Discord", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Unknown live status (no registry wired) → behave as before the gating.
        return isLive ?? true;
    }

    /// <summary>
    /// Records user activity (message sent, active in chat)
    /// </summary>
    public async Task RecordUserActivityAsync(string username, string platform, string? streamId = null)
    {
        var watchtime = await _dataService.GetUserWatchtimeAsync(username, platform);

        if (watchtime == null)
        {
            // Create new watchtime record
            watchtime = new UserWatchtime
            {
                Username = username,
                Platform = platform,
                TotalMinutes = 0,
                LastSeenAt = DateTime.UtcNow,
                FirstSeenAt = DateTime.UtcNow,
                StreamSessionMinutes = 0,
                LastStreamId = streamId,
                IsActive = true
            };

            await _dataService.SaveUserWatchtimeAsync(watchtime);
            _logger.LogInformation("Created new watchtime record for {Username} ({Platform})", username, platform);
        }
        else
        {
            // Update last seen time and mark as active
            watchtime.LastSeenAt = DateTime.UtcNow;
            watchtime.IsActive = true;

            // If stream ID changed, reset session minutes (new stream)
            if (!string.IsNullOrEmpty(streamId) && streamId != watchtime.LastStreamId)
            {
                _logger.LogInformation("Stream ID changed for {Username} - resetting session", username);
                watchtime.StreamSessionMinutes = 0;
                watchtime.LastStreamId = streamId;
            }

            await _dataService.SaveUserWatchtimeAsync(watchtime);
        }
    }

    /// <summary>
    /// Increments watchtime for all active users (called periodically)
    /// </summary>
    public async Task IncrementActiveWatchtimeAsync(int minutes = 1)
    {
        var activeViewers = await _dataService.GetActiveViewersAsync();
        var count = 0;

        foreach (var viewer in activeViewers)
        {
            // W1T4 gating: Discord viewers never accrue watchtime, and stream
            // platforms accrue only while that platform is live. When no live
            // registry is wired the live check is skipped (back-compat).
            var isLive = _streamLiveRegistry is null
                ? (bool?)null
                : _streamLiveRegistry.IsLive(viewer.Platform);
            if (!ShouldAccrueWatchtime(viewer.Platform, isLive))
            {
                continue;
            }

            viewer.TotalMinutes += minutes;
            viewer.StreamSessionMinutes += minutes;
            await _dataService.SaveUserWatchtimeAsync(viewer);

            // Award currency if currency service is available
            if (_currencyService != null)
            {
                try
                {
                    // Route through the configurable watch-time earning path so the
                    // CurrencySetting toggles (WatchTimeEnabled / WatchTimeAmount /
                    // WatchTimeInterval) and earn multipliers are honored, instead of
                    // a hard-coded 1-per-minute award.
                    await _currencyService.AwardWatchTimeAsync(viewer.Username, viewer.Platform);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to award currency for watchtime: {Username}", viewer.Username);
                }
            }

            count++;
        }

        if (count > 0)
        {
            _logger.LogInformation("Incremented watchtime for {Count} active viewers by {Minutes} min", count, minutes);
        }
    }

    /// <summary>
    /// Marks users as inactive if they haven't been seen recently
    /// </summary>
    public async Task DeactivateInactiveViewersAsync()
    {
        var inactiveSince = DateTime.UtcNow - _inactiveThreshold;
        await _dataService.DeactivateInactiveViewersAsync(inactiveSince);
        _logger.LogDebug("Deactivated viewers not seen since {Time}", inactiveSince);
    }

    /// <summary>
    /// Gets a user's watchtime information
    /// </summary>
    public async Task<UserWatchtime?> GetUserWatchtimeAsync(string username, string platform)
    {
        return await _dataService.GetUserWatchtimeAsync(username, platform);
    }

    /// <summary>
    /// Gets a user's total tracked watchtime in minutes.
    /// Returns 0 when the user has never been tracked.
    /// </summary>
    public async Task<int> GetTotalMinutesAsync(string username, string platform)
    {
        var watchtime = await _dataService.GetUserWatchtimeAsync(username, platform);
        return watchtime?.TotalMinutes ?? 0;
    }

    /// <summary>
    /// Gets the top users by watchtime
    /// </summary>
    public async Task<IEnumerable<UserWatchtime>> GetTopWatchtimeUsersAsync(int limit = 10)
    {
        return await _dataService.GetTopWatchtimeUsersAsync(limit);
    }

    /// <summary>
    /// Gets all active viewers
    /// </summary>
    public async Task<IEnumerable<UserWatchtime>> GetActiveViewersAsync()
    {
        return await _dataService.GetActiveViewersAsync();
    }

    /// <summary>
    /// Gets the current count of active viewers
    /// </summary>
    public async Task<int> GetActiveViewerCountAsync()
    {
        return await _dataService.GetTotalActiveViewersCountAsync();
    }

    /// <summary>
    /// Resets stream session minutes for all users (call when stream starts)
    /// </summary>
    public async Task ResetStreamSessionAsync()
    {
        await _dataService.ResetStreamSessionMinutesAsync();
        _logger.LogInformation("Reset stream session minutes for all users");
    }

    /// <summary>
    /// Formats watchtime duration for display
    /// </summary>
    public static string FormatWatchtime(int totalMinutes)
    {
        if (totalMinutes < 60)
            return $"{totalMinutes} minutes";

        var hours = totalMinutes / 60;
        var minutes = totalMinutes % 60;

        if (hours < 24)
            return minutes > 0 ? $"{hours}h {minutes}m" : $"{hours}h";

        var days = hours / 24;
        hours = hours % 24;

        if (hours > 0)
            return $"{days}d {hours}h";

        return $"{days} days";
    }
}
