namespace VyralBot.Data.Entities;

/// <summary>
/// Configuration key-value storage.
/// </summary>
public class Config
{
    public required string Key { get; set; }
    
    public required string Value { get; set; }
    
    /// <summary>
    /// Category for grouping configs.
    /// </summary>
    public string? Category { get; set; }
    
    /// <summary>
    /// Whether the value is encrypted.
    /// </summary>
    public bool IsEncrypted { get; set; }
    
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
