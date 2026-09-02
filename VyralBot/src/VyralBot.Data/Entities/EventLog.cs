namespace VyralBot.Data.Entities;

/// <summary>
/// Log of events received by the system.
/// </summary>
public class EventLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    
    public required string Platform { get; set; }
    
    public required string EventType { get; set; }
    
    public string? Username { get; set; }
    
    public string? UserId { get; set; }
    
    /// <summary>
    /// JSON payload of the event.
    /// </summary>
    public string? Data { get; set; }
    
    /// <summary>
    /// Trigger ID that matched (if any).
    /// </summary>
    public Guid? TriggerId { get; set; }
    
    /// <summary>
    /// Whether a trigger was fired.
    /// </summary>
    public bool TriggerFired { get; set; }
    
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
