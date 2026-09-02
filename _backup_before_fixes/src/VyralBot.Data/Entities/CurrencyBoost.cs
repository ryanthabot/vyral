namespace VyralBot.Data.Entities;

/// <summary>
/// Active or historical currency earning boost/multiplier event.
/// </summary>
public class CurrencyBoost
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public double Multiplier { get; set; } = 2.0;
    public int DurationMinutes { get; set; } = 30;
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime EndsAt { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string Source { get; set; } = "Manual"; // Manual, HypeTrain, Scheduled
    public bool IsActive { get; set; } = true;
    public string? Platform { get; set; } // null = all platforms
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
