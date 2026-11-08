using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using VyralBot.Core.Services.Audio;
using VyralBot.UI.Services;
using VyralBot.UI.Views;

namespace VyralBot.UI.ViewModels;

public partial class AlertSettingsViewModel : ViewModelBase
{
    // Matches the 1..15 range of the duration slider in AlertSettingsView.xaml.
    private const int MinDurationSeconds = 1;
    private const int MaxDurationSeconds = 15;

    // Matches the clamps enforced by TextToSpeechService for the Tts.* keys.
    private const int MinTtsRatePercent = -50;
    private const int MaxTtsRatePercent = 50;
    private const int MinTtsVolumePercent = 0;
    private const int MaxTtsVolumePercent = 100;
    private const int MinTtsMaxLength = 10;
    private const int MaxTtsMaxLength = 1000;

    private readonly ILogger<AlertSettingsViewModel> _logger;
    private readonly IAlertService _alertService;
    private readonly ITtsService? _ttsService;

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

    /// <summary>AlertSettings.TtsAlertsEnabled — speak alerts via TTS after the popup shows.</summary>
    [ObservableProperty]
    private bool _speakAlertTts = true;

    // --- Text-to-Speech (TTS) card: persisted as individual Tts.* keys ---

    [ObservableProperty]
    private bool _ttsEnabled;

    [ObservableProperty]
    private string _selectedTtsVoice = string.Empty;

    [ObservableProperty]
    private int _ttsRatePercent;

    [ObservableProperty]
    private int _ttsVolumePercent = 100;

    [ObservableProperty]
    private bool _ttsAllowViewers = true;

    [ObservableProperty]
    private int _ttsMaxLength = 300;

    /// <summary>Feedback line under the Text-to-Speech card (test outcome / suppression).</summary>
    [ObservableProperty]
    private string _ttsStatusMessage = string.Empty;

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

    /// <summary>Installed SAPI voice names (from ITtsService.GetVoices, cached there).</summary>
    public ObservableCollection<string> TtsVoices { get; } = new();

    public AlertSettingsViewModel(
        ILogger<AlertSettingsViewModel> logger,
        IAlertService alertService,
        ITtsService? ttsService = null)
    {
        _logger = logger;
        _alertService = alertService;
        _ttsService = ttsService;
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

        try
        {
            // Voice enumeration touches SAPI once (cached inside the service);
            // kept off the UI thread for the first call.
            var voices = await Task.Run(() => _ttsService.GetVoices());
            TtsVoices.Clear();
            foreach (var voice in voices)
            {
                TtsVoices.Add(voice);
            }

            ApplyTtsSettingsToProperties(await _ttsService.GetSettingsAsync());

            _logger.LogInformation(
                "TTS settings loaded - Enabled: {Enabled}, Voice: {Voice}, Rate: {Rate}%, Volume: {Volume}%, Voices: {VoiceCount}",
                TtsEnabled, SelectedTtsVoice, TtsRatePercent, TtsVolumePercent, TtsVoices.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading TTS settings - using defaults");
            ApplyTtsSettingsToProperties(new TtsSettings());
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

    /// <summary>
    /// Persists the current page state as Tts.* keys first (SpeakAsync gates on
    /// the stored enabled flag, so the test must run against what the page
    /// shows), then speaks a sample sentence with the configured voice.
    /// </summary>
    [RelayCommand]
    private async Task TestTtsAsync()
    {
        try
        {
            var ttsSettings = BuildTtsSettingsFromProperties();
            await _ttsService.SaveSettingsAsync(ttsSettings);

            if (!ttsSettings.Enabled)
            {
                TtsStatusMessage = "Preview suppressed — text-to-speech is disabled (toggle above).";
                return;
            }

            // Default arguments = "use the persisted settings" (voice/rate/volume).
            await _ttsService.SpeakAsync("Text to speech is working. This is how I sound on your stream.");

            TtsStatusMessage = TtsVoices.Count == 0
                ? "No SAPI voices are installed — add a Windows voice pack (Settings → Time & language → Speech)."
                : string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error testing TTS");
            TtsStatusMessage = "TTS test failed — check the log for details.";
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
            await _ttsService.SaveSettingsAsync(BuildTtsSettingsFromProperties());

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

        var ttsDefaults = new TtsSettings();
        ApplyTtsSettingsToProperties(ttsDefaults);

        _logger.LogInformation("Alert settings reset to defaults");

        try
        {
            await _alertService.SaveAlertSettingsAsync(defaults);
            await _ttsService.SaveSettingsAsync(ttsDefaults);
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
            CheerAlertsEnabled = ShowCheerAlerts,
            TtsAlertsEnabled = SpeakAlertTts
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
        SpeakAlertTts = settings.TtsAlertsEnabled;
    }

    /// <summary>Builds the Tts.* settings blob from the page state (clamps match the service).</summary>
    private TtsSettings BuildTtsSettingsFromProperties() => new()
    {
        Enabled = TtsEnabled,
        Voice = string.IsNullOrWhiteSpace(SelectedTtsVoice) ? null : SelectedTtsVoice,
        RatePercent = Math.Clamp(TtsRatePercent, MinTtsRatePercent, MaxTtsRatePercent),
        VolumePercent = Math.Clamp(TtsVolumePercent, MinTtsVolumePercent, MaxTtsVolumePercent),
        AllowViewers = TtsAllowViewers,
        MaxLength = Math.Clamp(TtsMaxLength, MinTtsMaxLength, MaxTtsMaxLength)
    };

    private void ApplyTtsSettingsToProperties(TtsSettings settings)
    {
        TtsEnabled = settings.Enabled;
        TtsRatePercent = Math.Clamp(settings.RatePercent, MinTtsRatePercent, MaxTtsRatePercent);
        TtsVolumePercent = Math.Clamp(settings.VolumePercent, MinTtsVolumePercent, MaxTtsVolumePercent);
        TtsAllowViewers = settings.AllowViewers;
        TtsMaxLength = Math.Clamp(settings.MaxLength, MinTtsMaxLength, MaxTtsMaxLength);

        // Voice default is the first installed voice; an unknown stored name
        // falls back to it too (the service also falls back at speak time).
        SelectedTtsVoice = !string.IsNullOrWhiteSpace(settings.Voice) && TtsVoices.Contains(settings.Voice)
            ? settings.Voice
            : TtsVoices.FirstOrDefault() ?? string.Empty;
    }
}
