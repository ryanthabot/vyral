namespace VyralBot.Data.Entities;

/// <summary>
/// Represents a single vote in a poll by a user.
/// Tracks platform, vote time, and the chosen option.
/// </summary>
public class PollVote
{
    public Guid Id { get; set; } = Guid.NewGuid();
    
    /// <summary>
    /// Foreign key to the poll this vote belongs to
    /// </summary>
    public Guid PollId { get; set; }
    
    /// <summary>
    /// Username of the voter
    /// </summary>
    public required string Username { get; set; }
    
    /// <summary>
    /// Platform the user voted from ("Kick", "Twitch", "Discord")
    /// </summary>
    public required string Platform { get; set; }
    
    /// <summary>
    /// Platform-specific user ID (for verification/bans)
    /// </summary>
    public string? PlatformUserId { get; set; }
    
    /// <summary>
    /// Index of the option voted for (0-based)
    /// </summary>
    public int OptionIndex { get; set; }
    
    /// <summary>
    /// When the vote was cast
    /// </summary>
    public DateTime VotedAt { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// Whether the user was a subscriber at vote time
    /// </summary>
    public bool IsSubscriber { get; set; }
    
    /// <summary>
    /// Whether the user was a follower at vote time
    /// </summary>
    public bool IsFollower { get; set; }
    
    /// <summary>
    /// User's watchtime in minutes at vote time (for minimum requirements)
    /// </summary>
    public int WatchtimeMinutes { get; set; }
}
