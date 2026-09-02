using Microsoft.Extensions.Logging;
using VyralBot.Data;
using VyralBot.Data.Entities;

namespace VyralBot.Core.Services.Currency;

/// <summary>
/// Service for managing currency auctions where viewers bid on items.
/// </summary>
public class CurrencyAuctionService : ICurrencyAuctionService
{
    private readonly ILogger<CurrencyAuctionService> _logger;
    private readonly IDataService _dataService;
    private readonly ICurrencyService _currencyService;

    public CurrencyAuctionService(
        ILogger<CurrencyAuctionService> logger,
        IDataService dataService,
        ICurrencyService currencyService)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
        _currencyService = currencyService ?? throw new ArgumentNullException(nameof(currencyService));
    }

    public async Task<CurrencyAuction> CreateAuctionAsync(string itemName, string? description, int startingBid, int durationMinutes, int minBidIncrement = 10)
    {
        // Check for existing active auction
        var existing = await GetActiveAuctionAsync();
        if (existing != null)
        {
            throw new InvalidOperationException("An auction is already active. End it before starting a new one.");
        }

        var auction = new CurrencyAuction
        {
            ItemName = itemName,
            Description = description,
            StartingBid = Math.Max(1, startingBid),
            MinBidIncrement = Math.Max(1, minBidIncrement),
            CurrentBid = 0,
            DurationMinutes = Math.Max(1, durationMinutes),
            StartedAt = DateTime.UtcNow,
            EndsAt = DateTime.UtcNow.AddMinutes(durationMinutes),
            Status = "Active"
        };

        await _dataService.SaveCurrencyAuctionAsync(auction);

        _logger.LogInformation("Auction started: {ItemName} (starting bid: {StartingBid}, duration: {Duration}m)",
            itemName, startingBid, durationMinutes);

        return auction;
    }

    public async Task<(bool Success, string Message)> PlaceBidAsync(string username, string platform, int amount)
    {
        var auction = await GetActiveAuctionAsync();
        if (auction == null)
            return (false, "No active auction right now.");

        if (DateTime.UtcNow > auction.EndsAt)
        {
            await ResolveAuctionAsync(auction.Id);
            return (false, "The auction has ended!");
        }

        // Validate bid amount
        var minRequired = auction.CurrentBid > 0
            ? auction.CurrentBid + auction.MinBidIncrement
            : auction.StartingBid;

        if (amount < minRequired)
            return (false, $"Minimum bid is {minRequired:N0}. Current highest: {auction.CurrentBid:N0}");

        // Check bidder balance
        var balance = await _currencyService.GetBalanceAsync(username, platform);
        if (balance < amount)
            return (false, $"Insufficient balance (have {balance:N0}, need {amount:N0}).");

        // Refund the previous bidder
        if (!string.IsNullOrEmpty(auction.CurrentBidder) && auction.CurrentBid > 0)
        {
            await _currencyService.AddCurrencyAsync(
                auction.CurrentBidder, auction.CurrentBidderPlatform ?? platform,
                auction.CurrentBid, $"Auction outbid refund: {auction.ItemName}", "Refund");
        }

        // Deduct from new bidder
        var removed = await _currencyService.RemoveCurrencyAsync(
            username, platform, amount, $"Auction bid: {auction.ItemName}", "Spend");

        if (!removed)
            return (false, "Failed to place bid.");

        // Update auction
        auction.CurrentBid = amount;
        auction.CurrentBidder = username;
        auction.CurrentBidderPlatform = platform;
        auction.UpdatedAt = DateTime.UtcNow;

        // Anti-snipe: extend if bid placed near end
        if (auction.AntiSnipeEnabled)
        {
            var timeRemaining = auction.EndsAt - DateTime.UtcNow;
            if (timeRemaining.TotalSeconds < 60)
            {
                auction.EndsAt = DateTime.UtcNow.AddSeconds(auction.AntiSnipeSeconds);
            }
        }

        await _dataService.SaveCurrencyAuctionAsync(auction);

        // Log bid
        var bid = new CurrencyAuctionBid
        {
            AuctionId = auction.Id,
            Username = username,
            Platform = platform,
            Amount = amount
        };
        await _dataService.SaveCurrencyAuctionBidAsync(bid);

        var settings = await _currencyService.GetSettingsAsync();
        var currencyName = settings?.CurrencyName ?? "Points";
        var remaining = (int)(auction.EndsAt - DateTime.UtcNow).TotalSeconds;

        return (true, $"🔨 @{username} bids {amount:N0} {currencyName} on '{auction.ItemName}'! ({remaining}s remaining)");
    }

    public async Task<CurrencyAuction?> GetActiveAuctionAsync()
    {
        return await _dataService.GetActiveAuctionAsync();
    }

    public async Task<(bool Resolved, string Message)> ResolveAuctionAsync(string auctionId)
    {
        var auction = await _dataService.GetCurrencyAuctionAsync(auctionId);
        if (auction == null || auction.Status != "Active")
            return (false, "Auction not found or already resolved.");

        auction.Status = "Completed";
        auction.UpdatedAt = DateTime.UtcNow;
        await _dataService.SaveCurrencyAuctionAsync(auction);

        if (string.IsNullOrEmpty(auction.CurrentBidder) || auction.CurrentBid == 0)
        {
            _logger.LogInformation("Auction {AuctionId} ended with no bids", auctionId);
            return (true, $"🔨 Auction for '{auction.ItemName}' ended with no bids.");
        }

        var settings = await _currencyService.GetSettingsAsync();
        var currencyName = settings?.CurrencyName ?? "Points";

        _logger.LogInformation("Auction {AuctionId} won by {Winner} for {Amount}",
            auctionId, auction.CurrentBidder, auction.CurrentBid);

        return (true, $"🔨 @{auction.CurrentBidder} won '{auction.ItemName}' for {auction.CurrentBid:N0} {currencyName}!");
    }

    public async Task<bool> CancelAuctionAsync(string auctionId)
    {
        var auction = await _dataService.GetCurrencyAuctionAsync(auctionId);
        if (auction == null || auction.Status != "Active")
            return false;

        // Refund current bidder
        if (!string.IsNullOrEmpty(auction.CurrentBidder) && auction.CurrentBid > 0)
        {
            await _currencyService.AddCurrencyAsync(
                auction.CurrentBidder, auction.CurrentBidderPlatform ?? "Unknown",
                auction.CurrentBid, $"Auction cancelled refund: {auction.ItemName}", "Refund");
        }

        auction.Status = "Cancelled";
        auction.UpdatedAt = DateTime.UtcNow;
        await _dataService.SaveCurrencyAuctionAsync(auction);

        _logger.LogInformation("Auction {AuctionId} cancelled, bidder refunded", auctionId);
        return true;
    }

    public async Task<IEnumerable<CurrencyAuction>> GetAuctionHistoryAsync(int limit = 20)
    {
        return await _dataService.GetAuctionHistoryAsync(limit);
    }

    public async Task<IEnumerable<CurrencyAuctionBid>> GetAuctionBidsAsync(string auctionId)
    {
        return await _dataService.GetAuctionBidsAsync(auctionId);
    }
}
