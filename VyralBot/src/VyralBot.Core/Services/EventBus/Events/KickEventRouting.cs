namespace VyralBot.Core.Services.EventBus.Events;

/// <summary>
/// Coordinates which transport owns Kick follow events. KickLib's Pusher feed only
/// reports follower-count changes (no username), while Kick webhooks deliver the
/// follower's real username. When webhook subscriptions for <c>channel.followed</c>
/// are confirmed active, the Pusher-side count-only follow event is suppressed so
/// consumers never see duplicate alerts or "Unknown" usernames.
/// </summary>
public static class KickEventRouting
{
    /// <summary>
    /// True while Kick webhook subscriptions for channel.followed are active.
    /// Set by the WebhookCoordinatorService; read by the KickLib Pusher chat service.
    /// </summary>
    public static volatile bool WebhookFollowsActive;
}
