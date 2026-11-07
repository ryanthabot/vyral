using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace VyralBot.Core.Services;

/// <summary>
/// Background service that periodically increments watchtime for active viewers
/// and manages viewer activity status.
/// </summary>
public class WatchtimeBackgroundService : BackgroundService
{
    private readonly ILogger<WatchtimeBackgroundService> _logger;
    private readonly WatchtimeService _watchtimeService;
    private readonly TimeSpan _incrementInterval = TimeSpan.FromMinutes(1); // Increment every minute
    private readonly TimeSpan _cleanupInterval = TimeSpan.FromMinutes(2); // Clean up inactive viewers every 2 minutes

    public WatchtimeBackgroundService(
        ILogger<WatchtimeBackgroundService> logger,
        WatchtimeService watchtimeService)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _watchtimeService = watchtimeService ?? throw new ArgumentNullException(nameof(watchtimeService));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("🕐 Watchtime Background Service started - will increment active viewers every {Interval}", _incrementInterval);

        // Startup deactivation: IsActive persists in the database, so viewers that
        // were chatting before an app restart would otherwise keep accruing
        // watchtime/points for up to ~5-7 minutes (the inactive threshold plus tick
        // cadence) without being in chat. Clear the flag up front; chatters
        // re-activate on their next message via RecordUserActivityAsync.
        try
        {
            await _watchtimeService.DeactivateAllViewersAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to deactivate stale viewers at startup - continuing");
        }

        var lastCleanup = DateTime.UtcNow;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Increment watchtime for all active viewers. Each tick awards a
                // fixed 1 minute and the loop waits a full interval AFTER the work,
                // so the real cadence is interval + processing time (a slight
                // under-award, never an over-award).
                await _watchtimeService.IncrementActiveWatchtimeAsync(1);

                // Periodically deactivate inactive viewers
                if (DateTime.UtcNow - lastCleanup >= _cleanupInterval)
                {
                    await _watchtimeService.DeactivateInactiveViewersAsync();
                    lastCleanup = DateTime.UtcNow;
                }

                // Wait for next increment
                await Task.Delay(_incrementInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Expected when cancellation is requested
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in watchtime background service");
                // Retry after a FULL increment interval, not a few seconds: if the
                // increment partially applied before throwing, a quick retry would
                // re-award viewers that were already saved this pass.
                await Task.Delay(_incrementInterval, stoppingToken);
            }
        }

        _logger.LogInformation("🛑 Watchtime Background Service stopped");
    }
}
