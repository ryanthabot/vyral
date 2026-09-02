namespace VyralBot.Data.Entities;

/// <summary>
/// Bid in a currency auction.
/// </summary>
public class CurrencyAuctionBid
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public required string AuctionId { get; set; }
    public required string Username { get; set; }
    public required string Platform { get; set; }
    public int Amount { get; set; }
    public DateTime BidAt { get; set; } = DateTime.UtcNow;
}
