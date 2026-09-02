namespace VyralBot.Data.Entities;

/// <summary>
/// Represents a single entry in a giveaway by a user.
/// Tracks platform, entry time, and bonus entry multipliers.
/// </summary>
public class GiveawayEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    
    /// <summary>
    /// Foreign key to the giveaway this entry belongs to
    /// </summary>
    public Guid GiveawayId { get; set; }
    
    /// <summary>
    /// Username of the participant
    /// </summary>
    public required string Username { get; set; }
    
    /// <summary>
    /// Platform the user entered from ("Kick", "Twitch", "Discord")
    /// </summary>
    public required string Platform { get; set; }
    
    /// <summary>
    /// Platform-specific user ID (for verification/bans)
    /// </summary>
    public string? PlatformUserId { get; set; }
    
    /// <summary>
    /// When the entry was submitted
    /// </summary>
    public DateTime EnteredAt { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// Number of entries this user has (1 for base, +bonus for subscribers)
    /// </summary>
    public int EntryCount { get; set; } = 1;
    
    /// <summary>
    /// Whether the user was a subscriber at entry time
    /// </summary>
    public bool IsSubscriber { get; set; }
    
    /// <summary>
    /// Whether the user was a follower at entry time
    /// </summary>
    public bool IsFollower { get; set; }
    
    /// <summary>
    /// User's watchtime in minutes at entry time (for minimum requirements)
    /// </summary>
    public int WatchtimeMinutes { get; set; }
    
    /// <summary>
    /// Optional message the user included with their entry
    /// </summary>
    public string? Message { get; set; }
    
    /// <summary>
    /// Whether this entry was selected as a winner
    /// </summary>
    public bool IsWinner { get; set; }
    
    /// <summary>
    /// When the winner was announced (if applicable)
    /// </summary>
    public DateTime? WonAt { get; set; }
}
