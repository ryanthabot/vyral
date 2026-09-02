namespace VyralBot.Data.Entities;

/// <summary>
/// Configurable loyalty tier/rank definition.
/// </summary>
public class LoyaltyTier
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public required string Name { get; set; }
    public int MinPoints { get; set; }
    public double EarnMultiplier { get; set; } = 1.0;
    public string? Icon { get; set; }
    public string? Color { get; set; }
    public string? Perks { get; set; } // JSON
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
