namespace VyralBot.Data.Entities;

/// <summary>
/// A one-time code that a viewer redeems to link a platform account to their unified viewer identity.
/// </summary>
public class LinkCode
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>6-digit string shown to the viewer for redemption.</summary>
    public string Code { get; set; } = "";

    /// <summary>Unified viewer identity the code belongs to.</summary>
    public string ViewerId { get; set; } = "";

    /// <summary>Username of the broadcaster account that created the code.</summary>
    public string CreatedByUsername { get; set; } = "";

    /// <summary>Platform of the broadcaster account that created the code.</summary>
    public string CreatedByPlatform { get; set; } = "";

    /// <summary>What the code does: "Link" (default) or "Delink" (removes a platform from the viewer).</summary>
    public string Purpose { get; set; } = "Link";

    /// <summary>Delink only: platform being removed from the viewer identity (null for link codes).</summary>
    public string? TargetPlatform { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime ExpiresAt { get; set; }

    /// <summary>Username the code was redeemed for (null while unredeemed).</summary>
    public string? RedeemedByUsername { get; set; }

    /// <summary>Platform the code was redeemed on (null while unredeemed).</summary>
    public string? RedeemedByPlatform { get; set; }

    public DateTime? RedeemedAt { get; set; }
}
