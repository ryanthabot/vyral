using Microsoft.Extensions.Logging;
using VyralBot.Data;
using VyralBot.Data.Entities;

namespace VyralBot.Core.Services.Currency;

/// <summary>
/// Service for managing loyalty tiers and rank evaluation.
/// </summary>
public class LoyaltyTierService : ILoyaltyTierService
{
    private readonly ILogger<LoyaltyTierService> _logger;
    private readonly IDataService _dataService;
    private List<LoyaltyTier>? _cachedTiers;
    private DateTime _tiersCacheTime = DateTime.MinValue;
    private static readonly TimeSpan TiersCacheDuration = TimeSpan.FromMinutes(10);

    public LoyaltyTierService(ILogger<LoyaltyTierService> logger, IDataService dataService)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
    }

    public async Task<IEnumerable<LoyaltyTier>> GetTiersAsync()
    {
        if (_cachedTiers != null && DateTime.UtcNow - _tiersCacheTime < TiersCacheDuration)
        {
            return _cachedTiers;
        }

        var tiers = (await _dataService.GetLoyaltyTiersAsync()).ToList();
        _cachedTiers = tiers;
        _tiersCacheTime = DateTime.UtcNow;
        return tiers;
    }

    public async Task<LoyaltyTier?> GetUserTierAsync(string username, string platform)
    {
        var userCurrency = await _dataService.GetUserCurrencyAsync(username, platform);
        if (userCurrency == null)
        {
            return null;
        }

        return await EvaluateTierAsync(userCurrency.TotalEarned);
    }

    public async Task<LoyaltyTier?> GetNextTierAsync(string username, string platform)
    {
        var userCurrency = await _dataService.GetUserCurrencyAsync(username, platform);
        if (userCurrency == null)
        {
            return null;
        }

        var tiers = (await GetTiersAsync()).OrderBy(t => t.MinPoints).ToList();
        return tiers.FirstOrDefault(t => t.MinPoints > userCurrency.TotalEarned);
    }

    public async Task<LoyaltyTier?> EvaluateTierAsync(int totalEarned)
    {
        var tiers = (await GetTiersAsync()).OrderByDescending(t => t.MinPoints).ToList();
        return tiers.FirstOrDefault(t => totalEarned >= t.MinPoints);
    }

    public async Task<double> GetTierMultiplierAsync(string username, string platform)
    {
        var tier = await GetUserTierAsync(username, platform);
        return tier?.EarnMultiplier ?? 1.0;
    }

    public async Task SaveTierAsync(LoyaltyTier tier)
    {
        await _dataService.SaveLoyaltyTierAsync(tier);
        _cachedTiers = null; // Invalidate cache
        _logger.LogInformation("Saved loyalty tier: {TierName} (MinPoints: {MinPoints})", tier.Name, tier.MinPoints);
    }

    public async Task DeleteTierAsync(string id)
    {
        await _dataService.DeleteLoyaltyTierAsync(id);
        _cachedTiers = null; // Invalidate cache
        _logger.LogInformation("Deleted loyalty tier: {TierId}", id);
    }
}
