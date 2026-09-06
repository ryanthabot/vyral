using VyralBot.Data.Entities;

namespace VyralBot.Core.Services.Linking;

/// <summary>
/// Service recording and aggregating per-platform viewer status
/// (subscriber tier and follower state) under unified viewer identities.
/// </summary>
public interface IViewerStatusService
{
    /// <summary>
    /// Records a subscription, keeping the highest tier and month count seen.
    /// </summary>
    Task RecordSubscriptionAsync(string username, string platform, int tier, int months);

    /// <summary>
    /// Records a follow on the given platform.
    /// </summary>
    Task RecordFollowAsync(string username, string platform);

    /// <summary>
    /// Gets the cached status for a username+platform pair, or null.
    /// </summary>
    Task<ViewerPlatformStatus?> GetStatusAsync(string username, string platform);

    /// <summary>
    /// Best subscription tier among the viewer's statuses (0 when none).
    /// </summary>
    Task<int> GetBestSubTierAcrossLinkedAsync(string viewerId);

    /// <summary>
    /// Whether the viewer is a follower on any linked platform.
    /// </summary>
    Task<bool> IsFollowerAnywhereAsync(string viewerId);

    /// <summary>
    /// Number of distinct platforms the viewer has recorded statuses on.
    /// </summary>
    Task<int> GetLinkedPlatformCountAsync(string viewerId);
}
