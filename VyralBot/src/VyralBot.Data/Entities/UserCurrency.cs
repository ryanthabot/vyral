namespace VyralBot.Data.Entities;

/// <summary>
/// User currency balance (cross-platform by username).
/// </summary>
public class UserCurrency
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public required string Username { get; set; }
    public required string Platform { get; set; }
    public int Balance { get; set; }
    public int TotalEarned { get; set; }
    public int TotalSpent { get; set; }
    public DateTime? LastWatchTimeUpdate { get; set; }
    public DateTime? LastChatActivityUpdate { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
