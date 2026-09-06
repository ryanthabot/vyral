namespace VyralBot.Data.Entities;

/// <summary>
/// Cached per-platform status for a viewer (subscriber tier, follower state).
/// One row per (Username, Platform) pair.
/// </summary>
public class ViewerPlatformStatus
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    public string Username { get; set; } = "";

    /// <summary>Platform identity string ("Twitch", "Kick", "YouTube", ...).</summary>
    public string Platform { get; set; } = "";

    /// <summary>Unified viewer identity, once linked (null while unlinked).</summary>
    public string? ViewerId { get; set; }

    public bool IsSubscriber { get; set; }

    /// <summary>Subscription tier: 0 none, 1..3.</summary>
    public int SubTier { get; set; }

    public int SubscriberMonths { get; set; }

    public bool IsFollower { get; set; }

    public DateTime LastUpdatedAt { get; set; } = DateTime.UtcNow;
}
