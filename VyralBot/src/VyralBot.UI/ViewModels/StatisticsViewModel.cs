using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using VyralBot.Core.Services.Statistics;

namespace VyralBot.UI.ViewModels;

/// <summary>
/// ViewModel for event statistics and analytics.
/// </summary>
public partial class StatisticsViewModel : ViewModelBase
{
    private readonly ILogger<StatisticsViewModel> _logger;
    private readonly IStatisticsService _statisticsService;

    [ObservableProperty]
    private ObservableCollection<EventCountSummary> _eventCounts = new();

    [ObservableProperty]
    private ObservableCollection<UserStatistic> _topContributors = new();

    [ObservableProperty]
    private ObservableCollection<UserStatistic> _topUsers = new();

    [ObservableProperty]
    private string _selectedEventType = "All";

    [ObservableProperty]
    private string? _selectedPlatform;

    [ObservableProperty]
    private DateTime? _startDate;

    [ObservableProperty]
    private DateTime? _endDate;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private int _totalEvents;

    [ObservableProperty]
    private ObservableCollection<string> _availableEventTypes = new() { "All", "follow", "subscribe", "giftsub", "raid", "cheer", "host" };

    [ObservableProperty]
    private ObservableCollection<string?> _availablePlatforms = new() { null, "Kick", "Twitch" };

    public StatisticsViewModel(
        ILogger<StatisticsViewModel> logger,
        IStatisticsService statisticsService)
    {
        _logger = logger;
        _statisticsService = statisticsService;
        
        // Default to last 30 days
        EndDate = DateTime.Today;
        StartDate = DateTime.Today.AddDays(-30);
    }

    /// <summary>
    /// Initialize the ViewModel by loading statistics.
    /// </summary>
    public async Task InitializeAsync()
    {
        await LoadStatisticsAsync();
    }

    /// <summary>
    /// Load all statistics data.
    /// </summary>
    [RelayCommand]
    private async Task LoadStatisticsAsync()
    {
        IsLoading = true;
        try
        {
            // Load event counts
            var counts = await _statisticsService.GetEventCountsByTypeAsync(StartDate, EndDate);
            EventCounts.Clear();
            TotalEvents = 0;

            foreach (var platform in counts)
            {
                // Apply the platform filter to the event-count grid too
                // (GetEventCountsByTypeAsync has no platform argument).
                if (SelectedPlatform != null &&
                    !platform.Key.Equals(SelectedPlatform, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                foreach (var eventType in platform.Value)
                {
                    EventCounts.Add(new EventCountSummary
                    {
                        Platform = platform.Key,
                        EventType = eventType.Key,
                        Count = eventType.Value
                    });
                    TotalEvents += eventType.Value;
                }
            }

            // Load top contributors for selected event type
            await LoadTopContributorsAsync();

            // Load top users
            // GetTopUsersAsync has no platform argument, so the filter is applied
            // client-side. Extra rows are fetched so the list still fills up to 20
            // after filtering.
            var topUsers = await _statisticsService.GetTopUsersAsync(limit: 100);
            TopUsers.Clear();
            foreach (var user in topUsers)
            {
                if (SelectedPlatform != null &&
                    !user.Platform.Equals(SelectedPlatform, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (TopUsers.Count >= 20)
                {
                    break;
                }

                TopUsers.Add(user);
            }

            _logger.LogInformation("Statistics loaded successfully: {TotalEvents} events", TotalEvents);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading statistics");
            MessageBox.Show($"Error loading statistics: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Load top contributors for the selected event type.
    /// </summary>
    private async Task LoadTopContributorsAsync()
    {
        if (SelectedEventType == "All")
        {
            TopContributors.Clear();
            return;
        }

        var contributors = await _statisticsService.GetTopContributorsAsync(SelectedEventType, SelectedPlatform, limit: 20);
        TopContributors.Clear();
        foreach (var contributor in contributors)
        {
            TopContributors.Add(contributor);
        }
    }

    /// <summary>
    /// Handle event type selection change.
    /// </summary>
    partial void OnSelectedEventTypeChanged(string value)
    {
        _ = LoadTopContributorsAsync();
    }

    /// <summary>
    /// Handle platform selection change.
    /// </summary>
    partial void OnSelectedPlatformChanged(string? value)
    {
        _ = LoadStatisticsAsync();
    }

    /// <summary>
    /// Export statistics to CSV file.
    /// </summary>
    [RelayCommand]
    private async Task ExportToCsvAsync()
    {
        try
        {
            var saveDialog = new SaveFileDialog
            {
                Title = "Export Statistics to CSV",
                Filter = "CSV Files (*.csv)|*.csv|All Files (*.*)|*.*",
                DefaultExt = ".csv",
                FileName = $"EventStatistics_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
            };

            if (saveDialog.ShowDialog() == true)
            {
                IsLoading = true;
                var csv = await _statisticsService.ExportToCsvAsync(StartDate, EndDate);
                await File.WriteAllTextAsync(saveDialog.FileName, csv);

                MessageBox.Show($"Statistics exported successfully to:\n{saveDialog.FileName}", 
                    "Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);

                _logger.LogInformation("Statistics exported to {FileName}", saveDialog.FileName);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exporting statistics");
            MessageBox.Show($"Error exporting statistics: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Refresh statistics with current filters.
    /// </summary>
    [RelayCommand]
    private async Task RefreshAsync()
    {
        await LoadStatisticsAsync();
    }
}

/// <summary>
/// Summary of event counts by type and platform.
/// </summary>
public class EventCountSummary
{
    public string Platform { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public int Count { get; set; }
}
