namespace VyralBot.Data.Entities;

/// <summary>
/// User bank account for long-term savings with interest.
/// Bank balance is separate from wallet balance and excluded from games.
/// </summary>
public class UserBankAccount
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public required string Username { get; set; }
    public required string Platform { get; set; }
    public int BankBalance { get; set; }
    public int TotalDeposited { get; set; }
    public int TotalWithdrawn { get; set; }
    public int TotalInterestEarned { get; set; }
    public DateTime? LastInterestPayout { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
