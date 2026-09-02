namespace VyralBot.Data.Entities;

/// <summary>
/// Redeemable currency reward.
/// </summary>
public class CurrencyReward
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public required string Name { get; set; }
    public string? Description { get; set; }
    public int Cost { get; set; }
    public bool IsEnabled { get; set; } = true;
    public bool RequiresApproval { get; set; }
    public int Cooldown { get; set; } // seconds
    public int MaxRedemptionsPerUser { get; set; } // 0 = unlimited
    public int MaxRedemptionsTotal { get; set; } // 0 = unlimited
    public int CurrentRedemptions { get; set; }
    public string? Category { get; set; }
    public string? Prompt { get; set; } // User input prompt

    // Phase 5: Advanced Rewards
    public string? ActionSequenceId { get; set; } // FK to action sequences for auto-execute on redeem
    public DateTime? AvailableFrom { get; set; }
    public DateTime? AvailableUntil { get; set; }
    public string? RewardTier { get; set; } // "Common", "Rare", "Epic", "Legendary"
    public bool RequiresSubscription { get; set; }
    public string? RequiredLoyaltyTier { get; set; } // Minimum loyalty tier name to unlock

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
