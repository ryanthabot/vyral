namespace VyralBot.Data.Entities;

/// <summary>
/// Currency redemption record (queue management).
/// </summary>
public class CurrencyRedemption
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public required string RewardId { get; set; }
    public required string RewardName { get; set; }
    public required string Username { get; set; }
    public required string Platform { get; set; }
    public int Cost { get; set; }
    public string? UserInput { get; set; }
    public string Status { get; set; } = "Pending"; // "Pending", "Approved", "Rejected", "Completed"
    public DateTime RedeemedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ProcessedAt { get; set; }
    public string? ProcessedBy { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
