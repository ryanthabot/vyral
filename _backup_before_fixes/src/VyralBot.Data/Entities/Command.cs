namespace VyralBot.Data.Entities;

public class Command
{
    public required string Id { get; set; }
    public required string Name { get; set; }
    public required string Response { get; set; }
    public required string Permission { get; set; } = "Everyone";
    public int GlobalCooldown { get; set; } = 0;
    public int UserCooldown { get; set; } = 0;
    public bool IsEnabled { get; set; } = true;
    public bool IsBuiltIn { get; set; } = false;
    public bool EnabledOnKick { get; set; } = true;
    public bool EnabledOnTwitch { get; set; } = true;
    public int UsageCount { get; set; } = 0;
    public DateTime? LastUsedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
