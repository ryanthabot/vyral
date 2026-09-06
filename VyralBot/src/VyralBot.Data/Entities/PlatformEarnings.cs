namespace VyralBot.Data.Entities;

/// <summary>
/// Per-platform points ledger for a unified viewer identity: running totals of
/// points earned and spent on each platform (one row per (ViewerId, Platform)).
/// </summary>
public class PlatformEarnings
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>Unified viewer identity.</summary>
    public string ViewerId { get; set; } = "";

    /// <summary>Platform identity string ("Twitch", "Kick", "YouTube", ...).</summary>
    public string Platform { get; set; } = "";

    /// <summary>Total points earned on this platform.</summary>
    public long Earned { get; set; }

    /// <summary>Total points spent on this platform.</summary>
    public long Spent { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
