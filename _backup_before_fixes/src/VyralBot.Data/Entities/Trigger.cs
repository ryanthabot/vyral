namespace VyralBot.Data.Entities;

/// <summary>
/// Represents a trigger that fires when events match conditions.
/// </summary>
public class Trigger
{
    public Guid Id { get; set; } = Guid.NewGuid();
    
    public required string Name { get; set; }
    
    public string? Description { get; set; }
    
    public bool Enabled { get; set; } = true;
    
    /// <summary>
    /// Event type to match (e.g., "ChatMessage", "Follow", "Sub").
    /// </summary>
    public required string EventType { get; set; }
    
    /// <summary>
    /// Platform filter (e.g., "Twitch", "YouTube", null for any).
    /// </summary>
    public string? Platform { get; set; }
    
    /// <summary>
    /// Condition expression (JSON, regex, or simple comparison).
    /// </summary>
    public string? Condition { get; set; }
    
    /// <summary>
    /// Cooldown in seconds (0 = no cooldown).
    /// </summary>
    public int CooldownSeconds { get; set; }
    
    /// <summary>
    /// Last time this trigger fired.
    /// </summary>
    public DateTime? LastFiredAt { get; set; }
    
    /// <summary>
    /// Total times this trigger has fired.
    /// </summary>
    public int FireCount { get; set; }
    
    /// <summary>
    /// Permission level required (Everyone, Subscriber, Moderator, Broadcaster).
    /// </summary>
    public string Permission { get; set; } = "Everyone";
    
    /// <summary>
    /// JSON array of action IDs to execute.
    /// </summary>
    public required string ActionSequence { get; set; } = "[]";
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
