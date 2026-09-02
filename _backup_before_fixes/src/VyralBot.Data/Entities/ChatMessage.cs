namespace VyralBot.Data.Entities;

public class ChatMessage
{
    public Guid Id { get; set; }
    public string Platform { get; set; } = "";
    public string Channel { get; set; } = "";
    public string? MessageId { get; set; }
    public string? UserId { get; set; }
    public string? Username { get; set; }
    public string? DisplayName { get; set; }
    public string? Content { get; set; }
    public DateTime Timestamp { get; set; }
    public string? Color { get; set; }
    public int IsModerator { get; set; }
    public int IsSubscriber { get; set; }
    public int IsOwner { get; set; }
    public string? BadgesJson { get; set; }
    
    /// <summary>
    /// Indicates if this is a system-generated event message (follow, sub, raid, etc.)
    /// rather than a user chat message
    /// </summary>
    public int IsSystemMessage { get; set; } = 0;
    
    public DateTime CreatedAt { get; set; }
}
