using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using VyralBot.Data;

namespace VyralBot.Core.Services.Currency;

/// <summary>
/// Background service that processes currency decay for inactive users
/// and daily bank interest payouts.
/// </summary>
public class CurrencyDecayBackgroundService : BackgroundService
{
    private readonly ILogger<CurrencyDecayBackgroundService> _logger;
    private readonly IDataService _dataService;
    private readonly ICurrencyService _currencyService;
    private readonly ICurrencyBankService _bankService;
    private readonly ICurrencyBoostService _boostService;
    private readonly TimeSpan _checkInterval = TimeSpan.FromHours(1);

    public CurrencyDecayBackgroundService(
        ILogger<CurrencyDecayBackgroundService> logger,
        IDataService dataService,
        ICurrencyService currencyService,
        ICurrencyBankService bankService,
        ICurrencyBoostService boostService)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
        _currencyService = currencyService ?? throw new ArgumentNullException(nameof(currencyService));
        _bankService = bankService ?? throw new ArgumentNullException(nameof(bankService));
        _boostService = boostService ?? throw new ArgumentNullException(nameof(boostService));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("CurrencyDecayBackgroundService started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessDecayAsync(stoppingToken);
                await _bankService.ProcessInterestPayoutsAsync();
                await _boostService.ExpireBoostsAsync();
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in currency decay background service");
            }

            try
            {
                await Task.Delay(_checkInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.LogInformation("CurrencyDecayBackgroundService stopped");
    }

    private async Task ProcessDecayAsync(CancellationToken ct)
    {
        var settings = await _currencyService.GetSettingsAsync();
        if (settings == null || !settings.CurrencyDecayEnabled)
            return;

        var decayPercent = settings.CurrencyDecayPercent;
        var decayDays = settings.CurrencyDecayDays;

        if (decayPercent <= 0 || decayDays <= 0)
            return;

        var cutoffDate = DateTime.UtcNow.AddDays(-decayDays);
        var inactiveUsers = await _dataService.GetInactiveUsersForDecayAsync(cutoffDate);
        int decayed = 0;

        foreach (var user in inactiveUsers)
        {
            if (ct.IsCancellationRequested) break;
            if (user.Balance <= 0) continue;

            var decayAmount = (int)(user.Balance * decayPercent / 100.0);
            if (decayAmount <= 0) continue;

            await _currencyService.RemoveCurrencyAsync(
                user.Username, user.Platform, decayAmount,
                $"Inactivity decay ({decayDays} days)", "Decay");
            decayed++;
        }

        if (decayed > 0)
        {
            _logger.LogInformation("Applied currency decay to {Count} inactive users", decayed);
        }
    }
}
