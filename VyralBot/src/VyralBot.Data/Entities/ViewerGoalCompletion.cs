namespace VyralBot.Data.Entities;

/// <summary>
/// Records that a single viewer completed a SocialGoal (one row per GoalId + ViewerId).
/// </summary>
public class ViewerGoalCompletion
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>The SocialGoal that was completed.</summary>
    public Guid GoalId { get; set; }

    /// <summary>Unified viewer identity of the completer.</summary>
    public string ViewerId { get; set; } = "";

    public string Username { get; set; } = "";

    /// <summary>Platform identity string ("Twitch", "Kick", "YouTube", ...).</summary>
    public string Platform { get; set; } = "";

    public DateTime CompletedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Whether the goal reward has already been granted to this viewer.</summary>
    public bool RewardApplied { get; set; }
}
