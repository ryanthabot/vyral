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
    /// Stored as a hash (see ProfileService).
    /// </summary>
    public string? PinCode { get; set; }

    /// <summary>
    /// Whether this profile is protected by a PIN.
    /// Computed from <see cref="PinCode"/>; used by the profile selector padlock icon.
    /// </summary>
    public bool HasPin => !string.IsNullOrEmpty(PinCode);

    /// <summary>
    /// Profile-specific settings (JSON serialized).
    /// </summary>
    public Dictionary<string, string> Settings { get; set; } = new();
}
