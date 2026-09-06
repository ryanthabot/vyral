namespace VyralBot.Data.Entities;

/// <summary>
/// A broadcaster-defined social goal ("reach N linked platforms", "N followers", ...).
/// Progress is tracked via ViewerGoalCompletions.
/// </summary>
public class SocialGoal
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = "";

    public string Description { get; set; } = "";

    /// <summary>
    /// Goal kind: "LinkedPlatforms" | "FollowPlatforms" | "SubAnyPlatform" | "SubTier".
    /// </summary>
    public string GoalType { get; set; } = "";

    /// <summary>Number of platforms/subs required to complete the goal.</summary>
    public int TargetCount { get; set; } = 1;

    /// <summary>Comma-separated platform list the goal counts, or "All".</summary>
    public string RequiredPlatforms { get; set; } = "All";

    /// <summary>Reward kind: "Currency" | "Multiplier" | "Badge".</summary>
    public string RewardType { get; set; } = "Currency";

    /// <summary>Points amount, multiplier factor, or unused depending on RewardType.</summary>
    public double RewardValue { get; set; }

    public bool IsEnabled { get; set; } = true;

    public int SortOrder { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
