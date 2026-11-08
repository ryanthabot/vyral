using System.Collections.Concurrent;

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

    // Cross-transport idempotency: the same subscription/gift can be delivered by
    // BOTH the Pusher websocket (KickLibChatService) and a webhook when a matching
    // subscription for that event type exists in the Kick dev portal (e.g. leftover
    // or manually registered channel.subscription.* subs). Both transports claim
    // the event under the same key; the loser skips publishing, preventing double
    // alerts and double currency awards. Webhook retries alone are already deduped
    // by message id in KickWebhookValidationMiddleware.
    private static readonly ConcurrentDictionary<string, DateTime> RecentEventKeys = new();
    private const int MaxTrackedKeys = 2000;

    /// <summary>
    /// Cross-transport duplicates of the SAME event arrive within seconds (the
    /// webhook retry path is additionally deduped by message id upstream), so a
    /// short window suppresses them without swallowing DISTINCT events by the
    /// same actor — a gifter's second batch 20s later must still count.
    /// </summary>
    private static readonly TimeSpan DedupWindow = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Attempts to claim an event key atomically: the first caller within the
    /// dedup window wins (publish); later callers lose (skip duplicate). Uses
    /// remove-then-TryAdd so webhook/Pusher threads racing one key can never
    /// both claim it.
    /// </summary>
    public static bool TryBeginEvent(string dedupKey)
    {
        var nowUtc = DateTime.UtcNow;

        if (RecentEventKeys.TryGetValue(dedupKey, out var seenAt))
        {
            if (nowUtc - seenAt < DedupWindow)
            {
                return false;
            }
            RecentEventKeys.TryRemove(new KeyValuePair<string, DateTime>(dedupKey, seenAt));
        }

        var claimed = RecentEventKeys.TryAdd(dedupKey, nowUtc);
        if (claimed)
        {
            PruneExpired(nowUtc);
        }
        return claimed;
    }

    /// <summary>
    /// Bounds the dedup cache: once over capacity, entries older than the dedup
    /// window are dropped. Best-effort and cheap — runs only on cap overflow.
    /// </summary>
    private static void PruneExpired(DateTime nowUtc)
    {
        if (RecentEventKeys.Count <= MaxTrackedKeys)
        {
            return;
        }

        foreach (var (key, seenAt) in RecentEventKeys)
        {
            if (nowUtc - seenAt > DedupWindow)
            {
                RecentEventKeys.TryRemove(key, out _);
            }
        }
    }
}
