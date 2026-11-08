using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using VyralBot.Core.Services.Counters;
using VyralBot.Data;

namespace VyralBot.UI.ViewModels;

/// <summary>
/// Tools hub page. Owns the death-counter card state: big count display,
/// +/−/set/reset buttons, the OBS browser-source URL (with COPY) and the
/// recent-change history list. The count itself lives in
/// <see cref="IDeathCounterService"/> (per-profile config rows) so the !death
/// chat command family, the Tools card, the /counters/death widget endpoint
/// and the VyralDeck state all share one number.
///
/// Registered as a singleton and navigated to without an InitializeAsync call
/// (see MainViewModel.NavigateToToolsAsync), so the card hydrates itself from
/// the constructor with a fire-and-forget load.
/// </summary>
public partial class ToolsViewModel : ViewModelBase
{
    private readonly ILogger<ToolsViewModel> _logger;
    private readonly IDeathCounterService _deathCounter;
    private readonly IDataService _dataService;

    private int _widgetPort = 5000;
    private bool _suppressLabelPersist;
    private CancellationTokenSource? _labelPersistCts;

    /// <summary>Last counter changes, newest first (card shows the first 10).</summary>
    public ObservableCollection<DeathHistoryRow> DeathHistory { get; } = new();

    [ObservableProperty]
    private int _deathCount;

    [ObservableProperty]
    private string _deathLabel = "Deaths";

    [ObservableProperty]
    private string _deathWidgetUrl = string.Empty;

    /// <summary>Value typed into the SET box; parsed on demand, invalid text is ignored.</summary>
    [ObservableProperty]
    private string _deathSetValueText = string.Empty;

    [ObservableProperty]
    private bool _hasDeathHistory;

    public ToolsViewModel(
        ILogger<ToolsViewModel> logger,
        IDeathCounterService deathCounter,
        IDataService dataService)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _deathCounter = deathCounter ?? throw new ArgumentNullException(nameof(deathCounter));
        _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));

        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        try
        {
            // Browser source is served by the embedded Kestrel server - build
            // the URL from the persisted web-server port (5000 fallback), same
            // as the audio.html / songplayer.html URLs.
            try
            {
                var webSettings = await _dataService.GetWebServerSettingsAsync();
                if (webSettings is not null && webSettings.Port > 0)
                {
                    _widgetPort = webSettings.Port;
                }
            }
            catch (Exception portEx)
            {
                _logger.LogWarning(portEx, "Could not load web-server settings - using default port 5000 for the death counter URL");
            }

            var savedLabel = await _dataService.GetConfigValueAsync("DeathCounter.Label");
            _suppressLabelPersist = true;
            if (!string.IsNullOrWhiteSpace(savedLabel))
            {
                DeathLabel = savedLabel.Trim();
            }

            RebuildDeathWidgetUrl();
            _suppressLabelPersist = false;

            await ReloadDeathCardAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initializing the death counter card");
        }
    }

    // ------------------------------------------------------------------
    // Card actions (+ / − / set / reset / copy)
    // ------------------------------------------------------------------

    [RelayCommand]
    private async Task DeathIncrementAsync()
    {
        try
        {
            DeathCount = await _deathCounter.AddAsync(1, "Tools page");
            await ReloadHistoryAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error incrementing the death counter");
        }
    }

    [RelayCommand]
    private async Task DeathDecrementAsync()
    {
        try
        {
            DeathCount = await _deathCounter.SubtractAsync(1, "Tools page");
            await ReloadHistoryAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error decrementing the death counter");
        }
    }

    [RelayCommand]
    private async Task DeathSetAsync()
    {
        var text = DeathSetValueText?.Trim();
        if (int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value >= 0)
        {
            try
            {
                DeathCount = await _deathCounter.SetAsync(value, "Tools page");
                DeathSetValueText = string.Empty;
                await ReloadHistoryAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error setting the death counter");
            }
        }
    }

    [RelayCommand]
    private async Task DeathResetAsync()
    {
        try
        {
            DeathCount = await _deathCounter.ResetAsync("Tools page");
            await ReloadHistoryAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resetting the death counter");
        }
    }

    [RelayCommand]
    private void CopyDeathWidgetUrl()
    {
        try
        {
            if (!string.IsNullOrEmpty(DeathWidgetUrl))
            {
                System.Windows.Clipboard.SetText(DeathWidgetUrl);
                _logger.LogInformation("Copied the death counter widget URL to the clipboard");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not copy the death counter widget URL");
        }
    }

    partial void OnDeathLabelChanged(string value)
    {
        RebuildDeathWidgetUrl();
        if (!_suppressLabelPersist)
        {
            ScheduleDeathLabelPersist();
        }
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    /// <summary>Refreshes count + history in one pass (initial load; buttons reload history only).</summary>
    private async Task ReloadDeathCardAsync()
    {
        DeathCount = await _deathCounter.GetCountAsync();
        await ReloadHistoryAsync();
    }

    private async Task ReloadHistoryAsync()
    {
        try
        {
            var events = await _deathCounter.GetHistoryAsync(10);

            DeathHistory.Clear();
            foreach (var item in events)
            {
                DeathHistory.Add(DeathHistoryRow.From(item));
            }

            HasDeathHistory = DeathHistory.Count > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading the death counter history");
        }
    }

    private void RebuildDeathWidgetUrl()
    {
        var label = string.IsNullOrWhiteSpace(DeathLabel) ? "Deaths" : DeathLabel.Trim();
        DeathWidgetUrl =
            $"http://localhost:{_widgetPort}/deathcounter.html?label={Uri.EscapeDataString(label)}";
    }

    /// <summary>Debounced persist of the label into the DeathCounter.Label config key.</summary>
    private void ScheduleDeathLabelPersist()
    {
        _labelPersistCts?.Cancel();
        _labelPersistCts?.Dispose();
        _labelPersistCts = new CancellationTokenSource();
        _ = PersistDeathLabelAsync(_labelPersistCts.Token);
    }

    private async Task PersistDeathLabelAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(600, ct);
            var label = string.IsNullOrWhiteSpace(DeathLabel) ? "Deaths" : DeathLabel.Trim();
            await _dataService.SetConfigValueAsync("DeathCounter.Label", label, "DeathCounter");
        }
        catch (OperationCanceledException)
        {
            // a newer keystroke won - its own persist is already scheduled
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not persist the death counter label");
        }
    }

    /// <summary>One row of the card's history list.</summary>
    public sealed record DeathHistoryRow
    {
        public string Time { get; init; } = string.Empty;
        public string Change { get; init; } = string.Empty;
        public string By { get; init; } = string.Empty;

        public static DeathHistoryRow From(DeathCounterEvent item) => new()
        {
            Time = item.At.ToLocalTime().ToString("MMM d, HH:mm", CultureInfo.CurrentCulture),
            Change = item.Delta switch
            {
                > 0 => "+" + item.Delta.ToString(CultureInfo.InvariantCulture),
                < 0 => item.Delta.ToString(CultureInfo.InvariantCulture),
                _ => "0",
            },
            By = item.By,
        };
    }
}
