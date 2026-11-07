namespace VyralBot.Data.Entities;

/// <summary>
/// Tracks individual event occurrences for statistics and history
/// </summary>
public class EventStatistic
{
    public Guid Id { get; set; } = Guid.NewGuid();
    
    /// <summary>
    /// Platform: "Kick", "Twitch" or "YouTube"
    /// </summary>
    public required string Platform { get; set; }
    
    /// <summary>
    /// Event type: "Follow", "Subscribe", "Raid", etc.
    /// </summary>
    public required string EventType { get; set; }
    
    /// <summary>
    /// Username of user who triggered the event
    /// </summary>
    public required string Username { get; set; }
    
    /// <summary>
    /// JSON data specific to event type (tier, amount, count, etc.)
    /// </summary>
    public string? EventData { get; set; }
    
    /// <summary>
    /// When the event occurred
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// Whether an auto-reply was sent
    /// </summary>
    public bool WasAutoReplied { get; set; } = false;
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
