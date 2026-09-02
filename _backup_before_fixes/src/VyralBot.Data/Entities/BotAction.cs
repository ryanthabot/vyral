namespace VyralBot.Data.Entities;

/// <summary>
/// Represents an action that can be executed.
/// Named "BotAction" to avoid conflict with System.Action.
/// </summary>
public class BotAction
{
    public Guid Id { get; set; } = Guid.NewGuid();
    
    public required string Name { get; set; }
    
    public string? Description { get; set; }
    
    /// <summary>
    /// Action type (e.g., "SendMessage", "Delay", "HttpRequest", "RunScript").
    /// </summary>
    public required string ActionType { get; set; }
    
    /// <summary>
    /// JSON configuration for the action.
    /// </summary>
    public required string Configuration { get; set; } = "{}";
    
    /// <summary>
    /// Timeout in seconds (0 = use default).
    /// </summary>
    public int TimeoutSeconds { get; set; } = 30;
    
    /// <summary>
    /// Whether to retry on failure.
    /// </summary>
    public bool RetryOnFailure { get; set; }
    
    /// <summary>
    /// Max retry attempts.
    /// </summary>
    public int MaxRetries { get; set; } = 3;
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
