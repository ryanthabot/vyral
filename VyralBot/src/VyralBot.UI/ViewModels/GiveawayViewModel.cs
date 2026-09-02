using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using VyralBot.Core.Services;
using VyralBot.Data;
using VyralBot.Data.Entities;

namespace VyralBot.UI.ViewModels;

/// <summary>
/// ViewModel for managing giveaways with weighted entry system
/// </summary>
public partial class GiveawayViewModel : ViewModelBase
{
    private readonly GiveawayService _giveawayService;
    private readonly IDataService _dataService;
    private readonly ILogger<GiveawayViewModel> _logger;

    [ObservableProperty]
    private ObservableCollection<Giveaway> _giveaways = new();

    [ObservableProperty]
    private Giveaway? _selectedGiveaway;

    [ObservableProperty]
    private ObservableCollection<GiveawayEntry> _entries = new();

    // Create giveaway form fields
    [ObservableProperty]
    private string _newGiveawayName = string.Empty;

    [ObservableProperty]
    private string _newGiveawayDescription = string.Empty;

    [ObservableProperty]
    private string _newGiveawayKeyword = "enter";

    [ObservableProperty]
    private int _newGiveawayWinnerCount = 1;

    // 0 means no cap — GiveawayService treats a null MaxEntries as unlimited,
    // so anything <= 0 is passed as null when the giveaway is created.
    [ObservableProperty]
    private int _newGiveawayMaxEntries = 0;

    [ObservableProperty]
    private bool _newGiveawaySubscriberBonusEnabled = false;

    [ObservableProperty]
    private int _newGiveawaySubscriberBonusEntries = 5;

    [ObservableProperty]
    private int _newGiveawayMinWatchtime = 0;

    [ObservableProperty]
    private bool _newGiveawayFollowersOnly = false;

    [ObservableProperty]
    private bool _newGiveawaySubscribersOnly = false;

    [ObservableProperty]
    private string _newGiveawayAllowedPlatforms = "Twitch,Kick";

    [ObservableProperty]
    private DateTime? _newGiveawayScheduledEnd;

    // Statistics
    [ObservableProperty]
    private int _totalEntries;

    [ObservableProperty]
    private int _uniqueParticipants;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    public GiveawayViewModel(
        GiveawayService giveawayService,
        IDataService dataService,
        ILogger<GiveawayViewModel> logger)
    {
        _giveawayService = giveawayService;
        _dataService = dataService;
        _logger = logger;
    }

    public async Task InitializeAsync()
    {
        try
        {
            IsLoading = true;
            await LoadGiveawaysAsync();
            _logger.LogInformation("GiveawayViewModel initialized");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initializing GiveawayViewModel");
            StatusMessage = $"Error loading giveaways: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task LoadGiveawaysAsync()
    {
        try
        {
            var giveaways = await _dataService.GetGiveawaysAsync();
            
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                Giveaways.Clear();
                foreach (var giveaway in giveaways.OrderByDescending(g => g.CreatedAt))
                {
                    Giveaways.Add(giveaway);
                }
            });

            _logger.LogInformation("Loaded {Count} giveaways", giveaways.Count());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading giveaways");
            throw;
        }
    }

    partial void OnSelectedGiveawayChanged(Giveaway? value)
    {
        if (value != null)
        {
            _ = LoadEntriesForSelectedGiveawayAsync();
        }
    }

    private async Task LoadEntriesForSelectedGiveawayAsync()
    {
        if (SelectedGiveaway == null) return;

        try
        {
            var entries = await _dataService.GetGiveawayEntriesAsync(SelectedGiveaway.Id);
            var totalEntries = await _dataService.GetGiveawayEntryCountAsync(SelectedGiveaway.Id);
            var uniqueParticipants = await _dataService.GetGiveawayUniqueParticipantsAsync(SelectedGiveaway.Id);

            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                Entries.Clear();
                foreach (var entry in entries.OrderByDescending(e => e.EnteredAt))
                {
                    Entries.Add(entry);
                }

                TotalEntries = totalEntries;
                UniqueParticipants = uniqueParticipants;
            });

            _logger.LogInformation("Loaded {Count} entries for giveaway {GiveawayId}", entries.Count(), SelectedGiveaway.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading entries for giveaway {GiveawayId}", SelectedGiveaway?.Id.ToString() ?? "unknown");
            StatusMessage = $"Error loading entries: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task CreateGiveawayAsync()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(NewGiveawayName))
            {
                StatusMessage = "❌ Giveaway name is required";
                return;
            }

            if (string.IsNullOrWhiteSpace(NewGiveawayKeyword))
            {
                StatusMessage = "❌ Entry keyword is required";
                return;
            }

            if (NewGiveawayWinnerCount < 1)
            {
                StatusMessage = "❌ Winner count must be at least 1";
                return;
            }

            IsLoading = true;
            StatusMessage = "Creating giveaway...";

            // 0 (or negative) = unlimited entries; GiveawayService only caps when MaxEntries has a value.
            int? maxEntries = NewGiveawayMaxEntries > 0 ? NewGiveawayMaxEntries : null;

            var giveaway = await _giveawayService.CreateGiveawayAsync(
                NewGiveawayName,
                NewGiveawayDescription,
                NewGiveawayKeyword,
                NewGiveawayWinnerCount,
                maxEntries,
                NewGiveawayScheduledEnd,
                NewGiveawaySubscriberBonusEnabled,
                NewGiveawaySubscriberBonusEntries,
                NewGiveawayMinWatchtime,
                NewGiveawayAllowedPlatforms,
                NewGiveawayFollowersOnly,
                NewGiveawaySubscribersOnly
            );

            StatusMessage = $"✅ Created giveaway: {giveaway.Name}";
            await LoadGiveawaysAsync();
            ClearForm();
            _logger.LogInformation("Created giveaway: {Name}", NewGiveawayName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating giveaway");
            StatusMessage = $"❌ Error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task StartGiveawayAsync()
    {
        if (SelectedGiveaway == null)
        {
            StatusMessage = "❌ No giveaway selected";
            return;
        }

        try
        {
            IsLoading = true;
            StatusMessage = "Starting giveaway...";

            var success = await _giveawayService.StartGiveawayAsync(SelectedGiveaway.Id);

            if (success)
            {
                StatusMessage = $"✅ Giveaway started";
                await LoadGiveawaysAsync();
                
                // Re-select the giveaway to refresh its status
                var updated = Giveaways.FirstOrDefault(g => g.Id == SelectedGiveaway.Id);
                if (updated != null)
                {
                    SelectedGiveaway = updated;
                }

                _logger.LogInformation("Started giveaway: {Name}", SelectedGiveaway.Name);
            }
            else
            {
                StatusMessage = $"❌ Failed to start giveaway";
                _logger.LogWarning("Failed to start giveaway");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting giveaway");
            StatusMessage = $"❌ Error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task DrawWinnersAsync()
    {
        if (SelectedGiveaway == null)
        {
            StatusMessage = "❌ No giveaway selected";
            return;
        }

        try
        {
            IsLoading = true;
            StatusMessage = "Drawing winners...";

            var (success, winners, message) = await _giveawayService.DrawWinnersAsync(SelectedGiveaway.Id);

            if (success && winners != null)
            {
                var winnerNames = string.Join(", ", winners);
                StatusMessage = $"🎉 Winners: {winnerNames}";
                
                await LoadGiveawaysAsync();
                await LoadEntriesForSelectedGiveawayAsync();

                _logger.LogInformation("Drew {Count} winners for giveaway {Name}: {Winners}", 
                    winners.Count, SelectedGiveaway.Name, winnerNames);
            }
            else
            {
                StatusMessage = $"❌ {message}";
                _logger.LogWarning("Failed to draw winners: {Message}", message);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error drawing winners");
            StatusMessage = $"❌ Error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task CancelGiveawayAsync()
    {
        if (SelectedGiveaway == null)
        {
            StatusMessage = "❌ No giveaway selected";
            return;
        }

        try
        {
            IsLoading = true;
            StatusMessage = "Cancelling giveaway...";

            var success = await _giveawayService.CancelGiveawayAsync(SelectedGiveaway.Id);

            if (success)
            {
                StatusMessage = $"✅ Giveaway cancelled";
                await LoadGiveawaysAsync();
                
                // Re-select the giveaway to refresh its status
                var updated = Giveaways.FirstOrDefault(g => g.Id == SelectedGiveaway.Id);
                if (updated != null)
                {
                    SelectedGiveaway = updated;
                }

                _logger.LogInformation("Cancelled giveaway: {Name}", SelectedGiveaway.Name);
            }
            else
            {
                StatusMessage = $"❌ Failed to cancel giveaway";
                _logger.LogWarning("Failed to cancel giveaway");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cancelling giveaway");
            StatusMessage = $"❌ Error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        try
        {
            IsLoading = true;
            StatusMessage = "Refreshing...";

            await LoadGiveawaysAsync();
            
            if (SelectedGiveaway != null)
            {
                await LoadEntriesForSelectedGiveawayAsync();
            }

            StatusMessage = "✅ Refreshed";
            _logger.LogInformation("Refreshed giveaways list");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error refreshing giveaways");
            StatusMessage = $"❌ Error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void ClearForm()
    {
        NewGiveawayName = string.Empty;
        NewGiveawayDescription = string.Empty;
        NewGiveawayKeyword = "enter";
        NewGiveawayWinnerCount = 1;
        NewGiveawayMaxEntries = 0; // 0 = unlimited entries
        NewGiveawaySubscriberBonusEnabled = false;
        NewGiveawaySubscriberBonusEntries = 5;
        NewGiveawayMinWatchtime = 0;
        NewGiveawayFollowersOnly = false;
        NewGiveawaySubscribersOnly = false;
        NewGiveawayAllowedPlatforms = "Twitch,Kick";
        NewGiveawayScheduledEnd = null;
        StatusMessage = string.Empty;
    }
}
