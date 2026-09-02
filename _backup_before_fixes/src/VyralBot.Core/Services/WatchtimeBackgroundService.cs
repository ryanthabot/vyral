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

        var lastCleanup = DateTime.UtcNow;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Increment watchtime for all active viewers
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
                // Continue running even after errors
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
        }

        _logger.LogInformation("🛑 Watchtime Background Service stopped");
    }
}
