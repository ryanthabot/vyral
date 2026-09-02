using VyralBot.Data.Entities;

namespace VyralBot.Core.Services.Currency;

/// <summary>
/// Service for managing currency auctions where viewers bid on items.
/// </summary>
public interface ICurrencyAuctionService
{
    /// <summary>
    /// Create and start a new auction.
    /// </summary>
    Task<CurrencyAuction> CreateAuctionAsync(string itemName, string? description, int startingBid, int durationMinutes, int minBidIncrement = 10);

    /// <summary>
    /// Place a bid in the active auction.
    /// </summary>
    Task<(bool Success, string Message)> PlaceBidAsync(string username, string platform, int amount);

    /// <summary>
    /// Get the currently active auction (if any).
    /// </summary>
    Task<CurrencyAuction?> GetActiveAuctionAsync();

    /// <summary>
    /// Resolve the auction — award item to highest bidder.
    /// </summary>
    Task<(bool Resolved, string Message)> ResolveAuctionAsync(string auctionId);

    /// <summary>
    /// Cancel an active auction and refund the current bidder.
    /// </summary>
    Task<bool> CancelAuctionAsync(string auctionId);

    /// <summary>
    /// Get auction history.
    /// </summary>
    Task<IEnumerable<CurrencyAuction>> GetAuctionHistoryAsync(int limit = 20);

    /// <summary>
    /// Get bids for a specific auction.
    /// </summary>
    Task<IEnumerable<CurrencyAuctionBid>> GetAuctionBidsAsync(string auctionId);
}
