namespace VyralBot.Data.Entities;

/// <summary>
/// Currency auction — viewers bid on items using their currency.
/// </summary>
public class CurrencyAuction
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public required string ItemName { get; set; }
    public string? Description { get; set; }
    public int StartingBid { get; set; } = 100;
    public int MinBidIncrement { get; set; } = 10;
    public int CurrentBid { get; set; }
    public string? CurrentBidder { get; set; }
    public string? CurrentBidderPlatform { get; set; }
    public int DurationMinutes { get; set; } = 5;
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime EndsAt { get; set; }
    public string Status { get; set; } = "Active"; // Active, Completed, Cancelled
    public bool AntiSnipeEnabled { get; set; } = true;
    public int AntiSnipeSeconds { get; set; } = 30;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
