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
/// ViewModel for viewing watchtime statistics and leaderboards
/// </summary>
public partial class WatchtimeViewModel : ViewModelBase
{
    private readonly WatchtimeService _watchtimeService;
    private readonly IDataService _dataService;
    private readonly ILogger<WatchtimeViewModel> _logger;

    [ObservableProperty]
    private ObservableCollection<UserWatchtime> _allViewers = new();

    [ObservableProperty]
    private ObservableCollection<UserWatchtime> _topViewers = new();

    [ObservableProperty]
    private ObservableCollection<UserWatchtime> _activeViewers = new();

    [ObservableProperty]
    private UserWatchtime? _selectedViewer;

    [ObservableProperty]
    private int _totalActiveViewers;

    [ObservableProperty]
    private int _topViewersLimit = 10;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    public WatchtimeViewModel(
        WatchtimeService watchtimeService,
        IDataService dataService,
        ILogger<WatchtimeViewModel> logger)
    {
        _watchtimeService = watchtimeService;
        _dataService = dataService;
        _logger = logger;
    }

    public async Task InitializeAsync()
    {
        try
        {
            IsLoading = true;
            await LoadDataAsync();
            _logger.LogInformation("WatchtimeViewModel initialized");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initializing WatchtimeViewModel");
            StatusMessage = $"Error loading watchtime data: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task LoadDataAsync()
    {
        try
        {
            var allViewers = await _dataService.GetAllUserWatchtimeAsync();
            var topViewers = await _watchtimeService.GetTopWatchtimeUsersAsync(TopViewersLimit);
            var activeViewers = await _watchtimeService.GetActiveViewersAsync();
            var activeCount = await _watchtimeService.GetActiveViewerCountAsync();

            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                AllViewers.Clear();
                foreach (var viewer in allViewers.OrderByDescending(v => v.TotalMinutes))
                {
                    AllViewers.Add(viewer);
                }

                TopViewers.Clear();
                foreach (var viewer in topViewers)
                {
                    TopViewers.Add(viewer);
                }

                ActiveViewers.Clear();
                foreach (var viewer in activeViewers)
                {
                    ActiveViewers.Add(viewer);
                }

                TotalActiveViewers = activeCount;
            });

            _logger.LogInformation("Loaded watchtime data: {AllCount} total, {TopCount} top, {ActiveCount} active", 
                allViewers.Count(), topViewers.Count(), activeCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading watchtime data");
            throw;
        }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        try
        {
            IsLoading = true;
            StatusMessage = "Refreshing...";
            await LoadDataAsync();
            StatusMessage = "✅ Refreshed";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error refreshing watchtime data");
            StatusMessage = $"❌ Error refreshing: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task ResetStreamSessionAsync()
    {
        try
        {
            IsLoading = true;
            StatusMessage = "Resetting stream session...";
            await _watchtimeService.ResetStreamSessionAsync();
            await LoadDataAsync();
            StatusMessage = "✅ Stream session reset for all viewers";
            _logger.LogInformation("Reset stream session for all viewers");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resetting stream session");
            StatusMessage = $"❌ Error resetting session: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    partial void OnTopViewersLimitChanged(int value)
    {
        if (value > 0 && value <= 100)
        {
            _ = LoadTopViewersAsync();
        }
    }

    private async Task LoadTopViewersAsync()
    {
        try
        {
            var topViewers = await _watchtimeService.GetTopWatchtimeUsersAsync(TopViewersLimit);

            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                TopViewers.Clear();
                foreach (var viewer in topViewers)
                {
                    TopViewers.Add(viewer);
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading top viewers");
        }
    }

    public static string FormatWatchtime(int totalMinutes)
    {
        return WatchtimeService.FormatWatchtime(totalMinutes);
    }
}
