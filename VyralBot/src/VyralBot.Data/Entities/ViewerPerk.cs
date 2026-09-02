namespace VyralBot.Data.Entities;

/// <summary>
/// Temporary purchased perk with expiry (VIP status, highlighted messages, etc.).
/// </summary>
public class ViewerPerk
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public required string Username { get; set; }
    public required string Platform { get; set; }
    public required string PerkType { get; set; } // "VIP", "HighlightedMessage", "CustomColor"
    public string? PerkData { get; set; } // JSON for perk-specific data
    public DateTime PurchasedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }
    public bool IsActive { get; set; } = true;
    public int Cost { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
