namespace VyralBot.Data.Entities;

public class CommandCooldown
{
    public required string Id { get; set; }
    public required string CommandId { get; set; }
    public required string CommandName { get; set; }
    public string? Username { get; set; } // Null for global cooldowns
    public required string Platform { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
