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
    // Matches the 1..15 range of the duration slider in AlertSettingsView.xaml.
    private const int MinDurationSeconds = 1;
    private const int MaxDurationSeconds = 15;

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

        try
        {
            var settings = await _alertService.GetAlertSettingsAsync();
            ApplySettingsToProperties(settings);

            _logger.LogInformation(
                "Alert settings loaded - Enabled: {Enabled}, Duration: {Duration}s, Animation: {Animation}, Position: {Position}",
                AlertsEnabled, AlertDuration, SelectedAnimation, SelectedPosition);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading alert settings - using defaults");
            ApplySettingsToProperties(new AlertSettings());
        }
    }

    [RelayCommand]
    private async Task TestAlertAsync()
    {
        try
        {
            if (!Enum.TryParse<AlertAnimation>(SelectedAnimation, true, out var animation) ||
                !Enum.TryParse<AlertPosition>(SelectedPosition, true, out var position))
            {
                _logger.LogWarning("Invalid animation/position selection: {Animation} / {Position}",
                    SelectedAnimation, SelectedPosition);
                return;
            }

            _logger.LogInformation("Testing alert with animation: {Animation}, position: {Position}",
                SelectedAnimation, SelectedPosition);

            // applySettings: false → the test previews exactly what is selected on this
            // page, even before saving. Real event alerts (ChatViewModel call sites)
            // run with the default and are styled/gated by the persisted settings.
            await _alertService.ShowAlertAsync(
                title: "Test Alert!",
                username: "TestUser123",
                message: "This is a test alert",
                imageUrl: null,
                animation: animation,
                position: position,
                durationSeconds: AlertDuration,
                applySettings: false);
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

            var settings = BuildSettingsFromProperties();
            await _alertService.SaveAlertSettingsAsync(settings);

            _logger.LogInformation(
                "✅ Alert settings saved - Enabled: {Enabled}, Duration: {Duration}s, Animation: {Animation}, Position: {Position}",
                settings.AlertsEnabled, settings.DurationSeconds, settings.Animation, settings.Position);

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
    }

    [RelayCommand]
    private async Task ResetToDefaultsAsync()
    {
        var defaults = new AlertSettings();
        ApplySettingsToProperties(defaults);

        _logger.LogInformation("Alert settings reset to defaults");

        try
        {
            await _alertService.SaveAlertSettingsAsync(defaults);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error persisting default alert settings after reset");
            System.Windows.MessageBox.Show(
                "Settings were reset on this page, but saving the defaults failed.",
                "Error",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
        }
    }

    private AlertSettings BuildSettingsFromProperties()
    {
        return new AlertSettings
        {
            AlertsEnabled = AlertsEnabled,
            DurationSeconds = Math.Clamp(AlertDuration, MinDurationSeconds, MaxDurationSeconds),
            Animation = Enum.TryParse<AlertAnimation>(SelectedAnimation, true, out var animation)
                ? animation.ToString()
                : AlertAnimation.SlideInFromTop.ToString(),
            Position = Enum.TryParse<AlertPosition>(SelectedPosition, true, out var position)
                ? position.ToString()
                : AlertPosition.TopCenter.ToString(),
            FollowAlertsEnabled = ShowFollowAlerts,
            SubscribeAlertsEnabled = ShowSubscribeAlerts,
            GiftSubAlertsEnabled = ShowGiftSubAlerts,
            RaidAlertsEnabled = ShowRaidAlerts,
            HostAlertsEnabled = ShowHostAlerts,
            CheerAlertsEnabled = ShowCheerAlerts
        };
    }

    private void ApplySettingsToProperties(AlertSettings settings)
    {
        AlertsEnabled = settings.AlertsEnabled;
        AlertDuration = Math.Clamp(settings.DurationSeconds, MinDurationSeconds, MaxDurationSeconds);
        SelectedAnimation = Enum.TryParse<AlertAnimation>(settings.Animation, true, out var animation)
            ? animation.ToString()
            : AlertAnimation.SlideInFromTop.ToString();
        SelectedPosition = Enum.TryParse<AlertPosition>(settings.Position, true, out var position)
            ? position.ToString()
            : AlertPosition.TopCenter.ToString();
        ShowFollowAlerts = settings.FollowAlertsEnabled;
        ShowSubscribeAlerts = settings.SubscribeAlertsEnabled;
        ShowGiftSubAlerts = settings.GiftSubAlertsEnabled;
        ShowRaidAlerts = settings.RaidAlertsEnabled;
        ShowHostAlerts = settings.HostAlertsEnabled;
        ShowCheerAlerts = settings.CheerAlertsEnabled;
    }
}
