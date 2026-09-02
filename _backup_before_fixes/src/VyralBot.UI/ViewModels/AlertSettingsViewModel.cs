using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using VyralBot.UI.Services;
using VyralBot.UI.Views;

namespace VyralBot.UI.ViewModels;

public partial class AlertSettingsViewModel : ViewModelBase
{
    private readonly ILogger<AlertSettingsViewModel> _logger;
    private readonly IAlertService _alertService;

    [ObservableProperty]
    private bool _alertsEnabled = true;

    [ObservableProperty]
    private int _alertDuration = 5;

    [ObservableProperty]
    private string _selectedAnimation = "SlideInFromTop";

    [ObservableProperty]
    private string _selectedPosition = "TopCenter";

    [ObservableProperty]
    private bool _showFollowAlerts = true;

    [ObservableProperty]
    private bool _showSubscribeAlerts = true;

    [ObservableProperty]
    private bool _showGiftSubAlerts = true;

    [ObservableProperty]
    private bool _showRaidAlerts = true;

    [ObservableProperty]
    private bool _showHostAlerts = true;

    [ObservableProperty]
    private bool _showCheerAlerts = true;

    public ObservableCollection<string> Animations { get; } = new()
    {
        "SlideInFromTop",
        "SlideInFromBottom",
        "FadeIn",
        "Bounce"
    };

    public ObservableCollection<string> Positions { get; } = new()
    {
        "TopLeft",
        "TopCenter",
        "TopRight",
        "MiddleLeft",
        "MiddleCenter",
        "MiddleRight",
        "BottomLeft",
        "BottomCenter",
        "BottomRight"
    };

    public AlertSettingsViewModel(
        ILogger<AlertSettingsViewModel> logger,
        IAlertService alertService)
    {
        _logger = logger;
        _alertService = alertService;
    }

    public async Task InitializeAsync()
    {
        _logger.LogInformation("Initializing AlertSettingsViewModel");
        
        // TODO: Load settings from database
        
        await Task.CompletedTask;
    }

    [RelayCommand]
    private async Task TestAlertAsync()
    {
        try
        {
            _logger.LogInformation("Testing alert with animation: {Animation}, position: {Position}", SelectedAnimation, SelectedPosition);

            var animation = Enum.Parse<AlertAnimation>(SelectedAnimation);
            var position = Enum.Parse<AlertPosition>(SelectedPosition);

            await _alertService.ShowAlertAsync(
                title: "Test Alert!",
                username: "TestUser123",
                message: "This is a test alert",
                imageUrl: null,
                animation: animation,
                position: position,
                durationSeconds: AlertDuration);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error testing alert");
        }
    }

    [RelayCommand]
    private async Task SaveSettingsAsync()
    {
        try
        {
            _logger.LogInformation("Saving alert settings");

            // TODO: Save settings to database

            System.Windows.MessageBox.Show(
                "Alert settings saved successfully!",
                "Settings Saved",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving alert settings");
            System.Windows.MessageBox.Show(
                "Failed to save alert settings.",
                "Error",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
        }

        await Task.CompletedTask;
    }

    [RelayCommand]
    private async Task ResetToDefaultsAsync()
    {
        AlertsEnabled = true;
        AlertDuration = 5;
        SelectedAnimation = "SlideInFromTop";
        SelectedPosition = "TopCenter";
        ShowFollowAlerts = true;
        ShowSubscribeAlerts = true;
        ShowGiftSubAlerts = true;
        ShowRaidAlerts = true;
        ShowHostAlerts = true;
        ShowCheerAlerts = true;

        _logger.LogInformation("Alert settings reset to defaults");

        await Task.CompletedTask;
    }
}
