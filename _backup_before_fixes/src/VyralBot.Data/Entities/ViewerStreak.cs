namespace VyralBot.Data.Entities;

/// <summary>
/// Tracks consecutive stream attendance for streak bonuses.
/// </summary>
public class ViewerStreak
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public required string Username { get; set; }
    public required string Platform { get; set; }
    public int CurrentStreak { get; set; }
    public int LongestStreak { get; set; }
    public DateTime? LastStreamDate { get; set; }
    public DateTime? LastDailyClaimDate { get; set; }
    public bool HasChattedThisStream { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
