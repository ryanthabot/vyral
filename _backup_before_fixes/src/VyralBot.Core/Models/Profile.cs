namespace VyralBot.Core.Models;

/// <summary>
/// Represents a user profile with isolated settings and connections.
/// </summary>
public class Profile
{
    public required string Name { get; set; }
    public string? DisplayName { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastUsedAt { get; set; } = DateTime.UtcNow;
    public string? Description { get; set; }
    
    /// <summary>
    /// Optional PIN code for profile protection (4-6 digits).
    /// </summary>
    public string? PinCode { get; set; }
    
    /// <summary>
    /// Profile-specific settings (JSON serialized).
    /// </summary>
    public Dictionary<string, string> Settings { get; set; } = new();
}
