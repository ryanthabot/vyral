namespace VyralBot.Data.Entities;

/// <summary>
/// Represents a user's watchtime tracking across platforms.
/// Tracks total watchtime and current stream session activity.
/// </summary>
public class UserWatchtime
{
    public Guid Id { get; set; } = Guid.NewGuid();
    
    /// <summary>
    /// Username of the viewer
    /// </summary>
    public required string Username { get; set; }
    
    /// <summary>
    /// Platform the user is viewing from ("Kick", "Twitch", "Discord")
    /// </summary>
    public required string Platform { get; set; }
    
    /// <summary>
    /// Total watchtime accumulated in minutes (all time)
    /// </summary>
    public int TotalMinutes { get; set; }
    
    /// <summary>
    /// When the user was last seen in chat or detected as active
    /// </summary>
    public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// When the user was first tracked
    /// </summary>
    public DateTime FirstSeenAt { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// Minutes watched in the current stream session (resets when stream ends)
    /// </summary>
    public int StreamSessionMinutes { get; set; }
    
    /// <summary>
    /// Current stream session ID (to detect when stream restarts)
    /// </summary>
    public string? LastStreamId { get; set; }
    
    /// <summary>
    /// Whether the user is currently considered active (seen recently)
    /// </summary>
    public bool IsActive { get; set; }
    
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
