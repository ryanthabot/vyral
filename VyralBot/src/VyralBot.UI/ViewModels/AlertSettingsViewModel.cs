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

    /// <summary>
    /// Feedback line under the Alert Events card: set when a per-event test
    /// preview was suppressed by a toggle on this page (so the user connects
    /// the toggle to the missing popup), or after a preview fires.
    /// </summary>
    [ObservableProperty]
    private string _statusMessage = string.Empty;

    /// <summary>
    /// Per-event preview payloads. The titles are the EXACT titles ChatViewModel
    /// passes to <see cref="AlertService.ShowAlertAsync"/> for real events, so
    /// AlertService.TitleToEventMap routes a preview the same way it routes a
    /// live event — the popup text users see here is what they see on stream.
    /// </summary>
    private static readonly Dictionary<string, (string Title, string Message, string DisplayName)> EventTestPayloads =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["follow"] = ("New Follower!", "Thanks for the follow!", "New Followers"),
            ["subscribe"] = ("New Subscriber!", "Thanks for subscribing!", "New Subscribers"),
            ["giftsub"] = ("Gift Subs!", "5 subscription(s) gifted!", "Gifted Subscriptions"),
            ["raid"] = ("Raid!", "200 viewers!", "Raids"),
            ["host"] = ("New Host!", "150 viewers!", "Hosts"),
            ["cheer"] = ("Bits!", "100 bits!", "Bits/Cheers")
        };

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

            StatusMessage = string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error testing alert");
        }
    }

    /// <summary>
    /// Per-event TEST buttons: fires the alert with the real pipeline title for
    /// that event (same string ChatViewModel passes live, so AlertService's
    /// TitleToEventMap gating and popup text match production). Gated by the
    /// CURRENT page toggles — flipping a toggle off and pressing TEST shows the
    /// suppression message instead of the popup, demonstrating exactly what the
    /// toggle controls, without requiring a save first.
    /// </summary>
    [RelayCommand]
    private async Task TestEventAlertAsync(string? eventType)
    {
        if (string.IsNullOrWhiteSpace(eventType) ||
            !EventTestPayloads.TryGetValue(eventType, out var payload))
        {
            _logger.LogWarning("Unknown event type for per-event alert test: {EventType}", eventType);
            return;
        }

        try
        {
            if (!AlertsEnabled)
            {
                StatusMessage = "Preview suppressed — visual alerts are disabled (master toggle above).";
                return;
            }

            if (!IsEventEnabledOnPage(eventType))
            {
                StatusMessage = $"Preview suppressed — {payload.DisplayName} alerts are toggled off.";
                return;
            }

            if (!Enum.TryParse<AlertAnimation>(SelectedAnimation, true, out var animation) ||
                !Enum.TryParse<AlertPosition>(SelectedPosition, true, out var position))
            {
                _logger.LogWarning("Invalid animation/position selection: {Animation} / {Position}",
                    SelectedAnimation, SelectedPosition);
                return;
            }

            _logger.LogInformation("Testing {EventType} alert with pipeline title '{Title}'",
                eventType, payload.Title);

            await _alertService.ShowAlertAsync(
                title: payload.Title,
                username: "TestUser123",
                message: payload.Message,
                imageUrl: null,
                animation: animation,
                position: position,
                durationSeconds: AlertDuration,
                applySettings: false);

            StatusMessage = $"{payload.DisplayName} preview shown — on stream this appears with the title \"{payload.Title}\".";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error testing {EventType} alert", eventType);
        }
    }

    /// <summary>Page-state mirror of AlertService.IsEventAlertEnabled for the six toggled event types.</summary>
    private bool IsEventEnabledOnPage(string eventType) => eventType switch
    {
        "follow" => ShowFollowAlerts,
        "subscribe" => ShowSubscribeAlerts,
        "giftsub" => ShowGiftSubAlerts,
        "raid" => ShowRaidAlerts,
        "host" => ShowHostAlerts,
        "cheer" => ShowCheerAlerts,
        _ => true
    };

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
