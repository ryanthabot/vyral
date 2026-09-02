namespace VyralBot.Data.Entities;

/// <summary>
/// Currency transaction audit log.
/// </summary>
public class CurrencyTransaction
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public required string Username { get; set; }
    public required string Platform { get; set; }
    public int Amount { get; set; }
    public required string TransactionType { get; set; } // "Earn", "Spend", "Transfer", "Admin"
    public string? Reason { get; set; }
    public int BalanceBefore { get; set; }
    public int BalanceAfter { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
