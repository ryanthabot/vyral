namespace VyralBot.Data.Entities;

/// <summary>
/// Represents a variable that can be used in commands, messages, and templates.
/// Variables are referenced using {variablename} syntax.
/// </summary>
public class Variable
{
    /// <summary>
    /// Unique identifier for the variable (GUID string).
    /// </summary>
    public required string Id { get; set; }
    
    /// <summary>
    /// Variable name (lowercase, kebab-case). E.g., "discord", "kick-followercount".
    /// This is what users type in braces: {name}
    /// </summary>
    public required string Name { get; set; }
    
    /// <summary>
    /// Human-readable display name. E.g., "Discord", "Kick Follower Count".
    /// </summary>
    public required string DisplayName { get; set; }
    
    /// <summary>
    /// Category for organizing variables: Global, Twitch, Kick, Social, Custom.
    /// </summary>
    public required string Category { get; set; }
    
    /// <summary>
    /// Platform prefix for platform-specific variables. E.g., "kick-", "twitch-".
    /// </summary>
    public string? Prefix { get; set; }
    
    /// <summary>
    /// The current value of the variable. Null for system-generated runtime variables.
    /// </summary>
    public string? Value { get; set; }
    
    /// <summary>
    /// Maximum character length for variable values.
    /// </summary>
    public int MaxLength { get; set; } = 2000;
    
    /// <summary>
    /// Whether users can edit this variable's value in the UI.
    /// </summary>
    public bool IsEditable { get; set; }
    
    /// <summary>
    /// Whether this is a system-generated variable (runtime context only).
    /// </summary>
    public bool IsSystem { get; set; }
    
    /// <summary>
    /// Whether this variable auto-updates from webhooks/API.
    /// </summary>
    public bool IsAutoUpdating { get; set; }
    
    /// <summary>
    /// Whether this variable's value contains nested variable references.
    /// </summary>
    public bool ContainsNestedVariables { get; set; }
    
    /// <summary>
    /// Comma-separated list of platforms this variable is available on.
    /// Null means available on all platforms.
    /// </summary>
    public string? Platforms { get; set; }
    
    /// <summary>
    /// Help text describing the variable's purpose.
    /// </summary>
    public string? Description { get; set; }
    
    /// <summary>
    /// When the variable was created.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// When the variable was last updated.
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// When auto-updating variables were last synced from API/webhooks.
    /// </summary>
    public DateTime? LastSyncedAt { get; set; }
}
