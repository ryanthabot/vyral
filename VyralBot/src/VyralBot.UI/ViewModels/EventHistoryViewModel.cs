using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using VyralBot.Data;
using VyralBot.Data.Entities;
using System.Text.Json;

namespace VyralBot.UI.ViewModels;

public partial class EventHistoryViewModel : ViewModelBase
{
    private readonly ILogger<EventHistoryViewModel> _logger;
    private readonly IDataService _dataService;

    [ObservableProperty]
    private ObservableCollection<EventStatistic> _events = new();

    [ObservableProperty]
    private EventStatistic? _selectedEvent;

    [ObservableProperty]
    private DateTime _startDate = DateTime.Now.AddDays(-7);

    [ObservableProperty]
    private DateTime _endDate = DateTime.Now;

    [ObservableProperty]
    private string? _selectedPlatform;

    [ObservableProperty]
    private string? _selectedEventType;

    [ObservableProperty]
    private string _usernameFilter = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private int _currentPage = 1;

    [ObservableProperty]
    private int _pageSize = 50;

    [ObservableProperty]
    private int _totalEvents;

    [ObservableProperty]
    private int _totalPages;

    public ObservableCollection<string> Platforms { get; } = new() { "All", "Kick", "Twitch" };
    public ObservableCollection<string> EventTypes { get; } = new()
    {
        "All",
        "Follow",
        "Subscribe",
        "Resub",
        "GiftSub",
        "Raid",
        "Host",
        "Cheer",
        "KicksGifted",
        "RewardRedeemed",
        "StreamStatus"
    };

    public EventHistoryViewModel(
        ILogger<EventHistoryViewModel> logger,
        IDataService dataService)
    {
        _logger = logger;
        _dataService = dataService;

        SelectedPlatform = "All";
        SelectedEventType = "All";
    }

    public async Task InitializeAsync()
    {
        _logger.LogInformation("Initializing EventHistoryViewModel");
        await LoadEventsAsync();
    }

    [RelayCommand]
    private async Task LoadEventsAsync()
    {
        try
        {
            IsLoading = true;
            _logger.LogInformation("Loading events - Page: {Page}, PageSize: {PageSize}", CurrentPage, PageSize);

            // Push every filter the SQL supports down to the query (platform,
            // event type, date range) and fetch a large window — previously the
            // call defaulted to limit=100, permanently capping history at the
            // newest 100 rows regardless of filters.
            var platformFilter = !string.IsNullOrEmpty(SelectedPlatform) && SelectedPlatform != "All"
                ? SelectedPlatform
                : null;
            var eventTypeFilter = !string.IsNullOrEmpty(SelectedEventType) && SelectedEventType != "All"
                ? SelectedEventType
                : null;

            var allEvents = await _dataService.GetEventStatisticsAsync(
                platformFilter, eventTypeFilter, limit: 10000,
                startDate: StartDate, endDate: EndDate.AddDays(1));

            // Only the "contains" username filter cannot be expressed by the SQL
            // signature, so it stays in-memory.
            var filtered = allEvents.AsEnumerable();

            // Username filter
            if (!string.IsNullOrEmpty(UsernameFilter))
            {
                filtered = filtered.Where(e => e.Username.Contains(UsernameFilter, StringComparison.OrdinalIgnoreCase));
            }

            // Calculate totals
            var filteredList = filtered.OrderByDescending(e => e.Timestamp).ToList();
            TotalEvents = filteredList.Count;
            TotalPages = (int)Math.Ceiling((double)TotalEvents / PageSize);

            // Apply pagination
            var page = filteredList
                .Skip((CurrentPage - 1) * PageSize)
                .Take(PageSize)
                .ToList();

            Events.Clear();
            foreach (var evt in page)
            {
                Events.Add(evt);
            }

            _logger.LogInformation("Loaded {Count} events (Total: {Total}, Pages: {Pages})", page.Count, TotalEvents, TotalPages);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading events");
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        CurrentPage = 1; // Reset to first page on search
        await LoadEventsAsync();
    }

    [RelayCommand]
    private async Task ClearFiltersAsync()
    {
        _logger.LogInformation("Clearing all filters");
        StartDate = DateTime.Now.AddDays(-7);
        EndDate = DateTime.Now;
        SelectedPlatform = "All";
        SelectedEventType = "All";
        UsernameFilter = string.Empty;
        CurrentPage = 1;
        await LoadEventsAsync();
    }

    [RelayCommand]
    private async Task ClearHistoryAsync()
    {
        if (!Confirm("Delete ALL event history for this profile? Every stored event will be removed. This cannot be undone."))
        {
            return;
        }

        try
        {
            IsLoading = true;
            _logger.LogInformation("Clearing all event history");

            var deleted = await _dataService.DeleteEventStatisticsAsync();
            StatusMessage = $"Cleared {deleted} events";

            // Reset to the first page and reload so the grid reflects the empty table
            CurrentPage = 1;
            await LoadEventsAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error clearing event history");
            StatusMessage = $"Error clearing event history: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Modal OK/cancel confirmation (same pattern as CommandsViewModel and
    /// SettingsViewModel's data-management confirmations). True only when the user confirmed.
    /// </summary>
    private static bool Confirm(string message)
    {
        var dialog = new Views.ConfirmDialog
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };
        dialog.SetMessage(message);
        return dialog.ShowDialog() == true;
    }

    [RelayCommand]
    private async Task NextPageAsync()
    {
        if (CurrentPage < TotalPages)
        {
            CurrentPage++;
            await LoadEventsAsync();
        }
    }

    [RelayCommand]
    private async Task PreviousPageAsync()
    {
        if (CurrentPage > 1)
        {
            CurrentPage--;
            await LoadEventsAsync();
        }
    }

    [RelayCommand]
    private void ViewDetails(EventStatistic? evt)
    {
        if (evt == null) return;

        _logger.LogInformation("Viewing details for event: {EventType} by {Username}", evt.EventType, evt.Username);
        
        // Format the event data as readable JSON
        var formattedData = FormatEventData(evt);
        
        System.Windows.MessageBox.Show(
            $"Event Details\n\n" +
            $"Platform: {evt.Platform}\n" +
            $"Event Type: {evt.EventType}\n" +
            $"Username: {evt.Username}\n" +
            $"Timestamp: {evt.Timestamp:yyyy-MM-dd HH:mm:ss}\n" +
            $"Auto-Replied: {(evt.WasAutoReplied ? "Yes" : "No")}\n\n" +
            $"Event Data:\n{formattedData}",
            "Event Details",
            System.Windows.MessageBoxButton.OK,
            System.Windows.MessageBoxImage.Information);
    }

    [RelayCommand]
    private async Task ReplayAutoReplyAsync(EventStatistic? evt)
    {
        if (evt == null) return;

        try
        {
            _logger.LogInformation("Viewing auto-reply for event: {EventType} by {Username}", evt.EventType, evt.Username);

            // Find the chat event setting for this event type
            var setting = await _dataService.GetChatEventSettingAsync(evt.Platform, evt.EventType);

            if (setting == null || !setting.AutoReplyEnabled || string.IsNullOrEmpty(setting.AutoReplyMessage))
            {
                System.Windows.MessageBox.Show(
                    "No auto-reply message configured for this event type.",
                    "Cannot View Reply",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
                return;
            }

            // Replace placeholders in the message
            var message = setting.AutoReplyMessage
                .Replace("{username}", evt.Username)
                .Replace("{platform}", evt.Platform)
                .Replace("{eventtype}", evt.EventType);

            // Parse EventData to replace additional placeholders
            if (!string.IsNullOrEmpty(evt.EventData))
            {
                try
                {
                    using var doc = JsonDocument.Parse(evt.EventData);
                    var root = doc.RootElement;

                    if (root.TryGetProperty("count", out var count))
                        message = message.Replace("{count}", count.GetInt32().ToString());
                    if (root.TryGetProperty("amount", out var amount))
                        message = message.Replace("{amount}", amount.GetDecimal().ToString());
                    if (root.TryGetProperty("viewers", out var viewers))
                        message = message.Replace("{viewers}", viewers.GetInt32().ToString());
                    if (root.TryGetProperty("bits", out var bits))
                        message = message.Replace("{bits}", bits.GetInt32().ToString());
                    if (root.TryGetProperty("months", out var months))
                        message = message.Replace("{months}", months.GetInt32().ToString());
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error parsing EventData for placeholder replacement");
                }
            }

            _logger.LogInformation("Auto-reply message: {Message}", message);
            System.Windows.MessageBox.Show(
                $"Auto-reply that was sent:\n\n{message}",
                "Auto-Reply Message",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error replaying auto-reply");
            System.Windows.MessageBox.Show(
                $"Error replaying auto-reply: {ex.Message}",
                "Error",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
        }
    }

    private string FormatEventData(EventStatistic evt)
    {
        if (string.IsNullOrEmpty(evt.EventData))
            return "(No data)";

        try
        {
            // Pretty-print JSON
            using var doc = JsonDocument.Parse(evt.EventData);
            return JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true });
        }
        catch
        {
            return evt.EventData; // Return raw if parsing fails
        }
    }

    partial void OnSelectedPlatformChanged(string? value)
    {
        _ = LoadEventsAsync();
    }

    partial void OnSelectedEventTypeChanged(string? value)
    {
        _ = LoadEventsAsync();
    }
}
