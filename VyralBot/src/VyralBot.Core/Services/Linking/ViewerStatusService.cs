using Microsoft.Extensions.Logging;
using VyralBot.Core.Services.Currency;
using VyralBot.Core.Services.EventBus;
using VyralBot.Core.Services.EventBus.Events;
using VyralBot.Data;
using VyralBot.Data.Entities;

namespace VyralBot.Core.Services.Linking;

/// <summary>
/// Static parser for platform-specific raw subscription tier labels.
/// Maps Twitch-style tier names onto the 0..3 tier scale stored in
/// <see cref="ViewerPlatformStatus.SubTier"/> (public test target).
/// </summary>
public static class SubTierParser
{
    /// <summary>
    /// "1000" → 1, "2000" → 2, "3000" → 3, "Prime" → 1 (case-insensitive);
    /// anything else (including null/empty) → 0.
    /// </summary>
    public static int Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return 0;
        }

        var value = raw.Trim();
        return value switch
        {
            "1000" => 1,
            "2000" => 2,
            "3000" => 3,
            _ when value.Equals("Prime", StringComparison.OrdinalIgnoreCase) => 1,
            _ => 0
        };
    }
}

/// <summary>
/// Default IViewerStatusService: upserts per-platform viewer status rows and
/// answers aggregate questions ("best tier anywhere", "follower anywhere",
/// "linked platform count") across a unified viewer identity.
/// </summary>
public class ViewerStatusService : IViewerStatusService
{
    private readonly ILogger<ViewerStatusService> _logger;
    private readonly IDataService _dataService;
    private readonly ILinkedAccountService _linkedAccounts;
    private readonly IEventBus _eventBus;

    public ViewerStatusService(
        ILogger<ViewerStatusService> logger,
        IDataService dataService,
        ILinkedAccountService linkedAccounts,
        IEventBus eventBus)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
        _linkedAccounts = linkedAccounts ?? throw new ArgumentNullException(nameof(linkedAccounts));
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
    }

    public async Task RecordSubscriptionAsync(string username, string platform, int tier, int months)
    {
        var status = await GetOrCreateAsync(username, platform);

        // Keep the highest tier/month count ever recorded for this account.
        status.SubTier = Math.Max(status.SubTier, tier);
        status.SubscriberMonths = Math.Max(status.SubscriberMonths, Math.Max(0, months));
        status.IsSubscriber = true;

        await SaveAndPublishAsync(status);
    }

    public async Task RecordFollowAsync(string username, string platform)
    {
        var status = await GetOrCreateAsync(username, platform);

        status.IsFollower = true;

        await SaveAndPublishAsync(status);
    }

    public Task<ViewerPlatformStatus?> GetStatusAsync(string username, string platform)
    {
        return _dataService.GetViewerPlatformStatusAsync(username, platform);
    }

    public async Task<int> GetBestSubTierAcrossLinkedAsync(string viewerId)
    {
        if (string.IsNullOrEmpty(viewerId))
        {
            return 0;
        }

        var statuses = await _dataService.GetViewerStatusesByViewerIdAsync(viewerId);
        return statuses
            .Where(HasViewerId)
            .Select(s => s.SubTier)
            .DefaultIfEmpty(0)
            .Max();
    }

    public async Task<bool> IsFollowerAnywhereAsync(string viewerId)
    {
        if (string.IsNullOrEmpty(viewerId))
        {
            return false;
        }

        var statuses = await _dataService.GetViewerStatusesByViewerIdAsync(viewerId);
        return statuses.Any(s => HasViewerId(s) && s.IsFollower);
    }

    public async Task<int> GetLinkedPlatformCountAsync(string viewerId)
    {
        if (string.IsNullOrEmpty(viewerId))
        {
            return 0;
        }

        var linkedAccounts = await _dataService.GetLinkedAccountsByViewerIdAsync(viewerId);
        return linkedAccounts
            .Select(a => a.Platform)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
    }

    private async Task<ViewerPlatformStatus> GetOrCreateAsync(string username, string platform)
    {
        var status = await _dataService.GetViewerPlatformStatusAsync(username, platform);
        if (status != null)
        {
            return status;
        }

        return new ViewerPlatformStatus
        {
            Username = username,
            Platform = platform
        };
    }

    private async Task SaveAndPublishAsync(ViewerPlatformStatus status)
    {
        // Backfill the unified viewer id once the account is linked.
        status.ViewerId ??= await _linkedAccounts.GetViewerIdAsync(status.Username, status.Platform);
        status.LastUpdatedAt = DateTime.UtcNow;

        await _dataService.SaveViewerPlatformStatusAsync(status);
        _logger.LogInformation("Recorded status for {Username} ({Platform}): subscriber={IsSubscriber}, tier={SubTier}, follower={IsFollower}",
            status.Username, status.Platform, status.IsSubscriber, status.SubTier, status.IsFollower);

        try
        {
            await _eventBus.PublishAsync(new ViewerStatusUpdatedEvent
            {
                Source = status.Platform,
                Username = status.Username,
                Platform = status.Platform,
                SubTier = status.SubTier,
                IsSubscriber = status.IsSubscriber,
                IsFollower = status.IsFollower
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish ViewerStatusUpdatedEvent for {Username} ({Platform})",
                status.Username, status.Platform);
        }
    }

    /// <summary>Aggregate queries only consider statuses already attached to a viewer identity.</summary>
    private static bool HasViewerId(ViewerPlatformStatus status)
    {
        return !string.IsNullOrEmpty(status.ViewerId);
    }
}
