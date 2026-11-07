using System.Collections.Concurrent;

namespace VyralBot.Core.Services.Chat;

/// <summary>
/// Rate limiter for auto-reply system to prevent spam.
/// Tracks per-user cooldowns per event type.
/// </summary>
public class AutoReplyRateLimiter
{
    private readonly ConcurrentDictionary<string, DateTime> _lastReplyPerUser = new();
    private readonly TimeSpan _minDelay;
    private DateTime _lastCleanupUtc = DateTime.UtcNow;

    // The 10-minute cutoff is far above the default 30s min delay, so pruning
    // can never cut an active cooldown short.
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromMinutes(10);

    public AutoReplyRateLimiter(TimeSpan? minDelay = null)
    {
        _minDelay = minDelay ?? TimeSpan.FromSeconds(30);
    }

    /// <summary>
    /// Checks if an auto-reply can be sent for a user/event combination.
    /// </summary>
    /// <param name="username">Username (case-insensitive)</param>
    /// <param name="platform">Platform (Kick/Twitch)</param>
    /// <param name="eventType">Event type (Follow, Subscribe, etc.)</param>
    /// <returns>True if reply is allowed, false if still in cooldown</returns>
    public bool CanReply(string username, string platform, string eventType)
    {
        var key = $"{platform}:{eventType}:{username.ToLowerInvariant()}";
        var now = DateTime.UtcNow;

        if (_lastReplyPerUser.TryGetValue(key, out var lastReply))
        {
            if (now - lastReply < _minDelay)
            {
                return false; // Still in cooldown
            }
        }

        _lastReplyPerUser[key] = now;

        // Opportunistic prune: the limiter lives on a singleton service, so
        // without this the per-user map grows unbounded for the process
        // lifetime (unique viewers x platforms x event types).
        if (now - _lastCleanupUtc >= CleanupInterval)
        {
            _lastCleanupUtc = now;
            CleanupExpired();
        }

        return true;
    }

    /// <summary>
    /// Clears all cooldown entries (for testing).
    /// </summary>
    public void Clear()
    {
        _lastReplyPerUser.Clear();
    }

    /// <summary>
    /// Cleans up expired entries older than 10 minutes.
    /// </summary>
    public void CleanupExpired()
    {
        var cutoff = DateTime.UtcNow - TimeSpan.FromMinutes(10);
        var expiredKeys = _lastReplyPerUser
            .Where(kvp => kvp.Value < cutoff)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in expiredKeys)
        {
            _lastReplyPerUser.TryRemove(key, out _);
        }
    }
}
