using Microsoft.Extensions.Logging;
using VyralBot.Data;
using VyralBot.Data.Entities;

namespace VyralBot.Core.Services.Currency;

/// <summary>
/// Service for managing temporary currency earning boosts and multiplier events.
/// </summary>
public class CurrencyBoostService : ICurrencyBoostService
{
    private readonly ILogger<CurrencyBoostService> _logger;
    private readonly IDataService _dataService;

    public CurrencyBoostService(ILogger<CurrencyBoostService> logger, IDataService dataService)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
    }

    public async Task<CurrencyBoost> StartBoostAsync(double multiplier, int durationMinutes, string reason, string source = "Manual", string? platform = null)
    {
        // Expire any stale boosts first
        await ExpireBoostsAsync();

        var boost = new CurrencyBoost
        {
            Multiplier = Math.Max(1.0, multiplier),
            DurationMinutes = Math.Max(1, durationMinutes),
            StartedAt = DateTime.UtcNow,
            EndsAt = DateTime.UtcNow.AddMinutes(durationMinutes),
            Reason = reason,
            Source = source,
            IsActive = true,
            Platform = platform
        };

        await _dataService.SaveCurrencyBoostAsync(boost);

        _logger.LogInformation("Started {Multiplier}x boost for {Duration}m: {Reason} (Platform: {Platform})",
            boost.Multiplier, boost.DurationMinutes, reason, platform ?? "All");

        return boost;
    }

    public async Task<bool> EndBoostAsync(string boostId)
    {
        var boost = await _dataService.GetCurrencyBoostAsync(boostId);
        if (boost == null || !boost.IsActive)
            return false;

        boost.IsActive = false;
        boost.EndsAt = DateTime.UtcNow;
        await _dataService.SaveCurrencyBoostAsync(boost);

        _logger.LogInformation("Ended boost {BoostId} ({Multiplier}x) early", boostId, boost.Multiplier);
        return true;
    }

    public async Task<IEnumerable<CurrencyBoost>> GetActiveBoostsAsync(string? platform = null)
    {
        await ExpireBoostsAsync();
        return await _dataService.GetActiveBoostsAsync(platform);
    }

    public async Task<double> GetActiveMultiplierAsync(string? platform = null)
    {
        var boosts = await GetActiveBoostsAsync(platform);
        var boostList = boosts.ToList();

        if (boostList.Count == 0)
            return 1.0;

        // Use highest-wins strategy
        return boostList.Max(b => b.Multiplier);
    }

    public async Task<IEnumerable<CurrencyBoost>> GetBoostHistoryAsync(int limit = 50)
    {
        return await _dataService.GetBoostHistoryAsync(limit);
    }

    public async Task ExpireBoostsAsync()
    {
        await _dataService.ExpireBoostsAsync();
    }

    public async Task<CurrencyBoost?> TriggerHypeTrainBoostAsync(string platform, int level)
    {
        // Escalation: level 1 = 1.5x, level 2 = 2x, level 3+ = 3x
        var multiplier = level switch
        {
            1 => 1.5,
            2 => 2.0,
            _ => 3.0
        };

        var durationMinutes = 10 + (level * 5); // 15m, 20m, 25m+

        // End any existing hype train boost on this platform
        var existingBoosts = await _dataService.GetActiveBoostsAsync(platform);
        foreach (var existing in existingBoosts.Where(b => b.Source == "HypeTrain"))
        {
            await EndBoostAsync(existing.Id);
        }

        return await StartBoostAsync(multiplier, durationMinutes,
            $"Hype Train Level {level}!", "HypeTrain", platform);
    }
}
