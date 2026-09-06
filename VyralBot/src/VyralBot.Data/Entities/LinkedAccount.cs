namespace VyralBot.Data.Entities;

/// <summary>
/// Maps a unified viewer identity to platform-specific accounts.
/// One ViewerId can have multiple (Username, Platform) pairs.
/// </summary>
public class LinkedAccount
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public required string ViewerId { get; set; }
    public required string Username { get; set; }
    public required string Platform { get; set; }
    public bool IsVerified { get; set; }
    public DateTime? VerifiedAt { get; set; }
    public DateTime CreatedAt { get; set; }

	public string? PlatformUserId { get; set; }
}
