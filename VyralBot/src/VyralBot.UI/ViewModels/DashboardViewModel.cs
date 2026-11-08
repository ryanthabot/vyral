using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using VyralBot.Core.Services.Audio;
using VyralBot.Core.Services.Profiles;
using VyralBot.Data;
using VyralBot.Integrations.Obs.Engine;
using VyralBot.Integrations.Restream;
using VyralBot.Integrations.Twitch;
using VyralBot.UI.Services;

namespace VyralBot.UI.ViewModels;

/// <summary>
/// Dashboard ViewModel showing overview and statistics. Also owns the
/// first-run Home "Get started" strip state (dismiss persists per app,
/// visibility additionally collapses once any platform connects —
/// the connection flags live on MainViewModel and are bound in XAML)
/// and the Home Quick Actions bar: one-click clip / replay save / ad
/// break / restream toggle / TTS mute over the singleton service seams
/// (ITwitchService, ObsOutputService, RestreamEngine, ITtsService).
/// </summary>
/// <remarks>
/// Quick-action service dependencies are optional (nullable): a missing
/// registration disables the matching button instead of breaking Home.
/// The engine event subscriptions use named handlers; this VM is an
/// app-lifetime singleton (never disposed by MainViewModel), so its
/// subscriptions intentionally live as long as the engine singletons —
/// no orphaned-handler buildup across navigations.
/// </remarks>
public partial class DashboardViewModel : ViewModelBase
{
    // Same persisted restream artifacts the Restream page reads (kept in
    // sync with RestreamViewModel's private constants so a Home start uses
    // exactly the saved session configuration).
    private const string RestreamSettingsConfigKey = "Restream.Settings";
    private const string RestreamDestinationsFileName = "restream_destinations.json";

    // Matches RestreamViewModel.PersistenceJson (string enums) so the
    // destination file written by the Restream page round-trips here.
    private static readonly JsonSerializerOptions RestreamPersistenceJson = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private static readonly TimeSpan QuickStatusDuration = TimeSpan.FromSeconds(2);

    private readonly UiPreferencesService _uiPreferences;
    private readonly ITwitchService? _twitch;
    private readonly ObsOutputService? _obsOutputs;
    private readonly ObsEngine? _obsEngine;
    private readonly RestreamEngine? _restreamEngine;
    private readonly RestreamFfmpegLocator? _ffmpegLocator;
    private readonly IDataService? _dataService;
    private readonly IProfileService? _profileService;
    private readonly ITtsService? _tts;
    private readonly ILogger<DashboardViewModel>? _logger;
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _quickStatusHideTimer;

    [ObservableProperty]
    private int _activeTriggerCount;

    [ObservableProperty]
    private int _totalActionCount;

    [ObservableProperty]
    private int _connectedPlatformCount;

    [ObservableProperty]
    private string _status = "Ready";

    /// <summary>True once the user dismissed the Home "Get started" strip.</summary>
    [ObservableProperty]
    private bool _isGettingStartedDismissed;

    // ── Quick Actions availability (service presence → button disabled) ──

    /// <summary>Clip + ad-break buttons work only with a Twitch service.</summary>
    [ObservableProperty]
    private bool _canQuickTwitch;

    /// <summary>Replay-buffer save works only with the embedded OBS outputs.</summary>
    [ObservableProperty]
    private bool _canQuickReplay;

    /// <summary>Restream toggle needs the engine + the config seam services.</summary>
    [ObservableProperty]
    private bool _canQuickRestream;

    /// <summary>TTS mute toggle needs the TTS service.</summary>
    [ObservableProperty]
    private bool _canQuickTts;

    // ── Quick Actions live state ──

    /// <summary>Embedded OBS engine reached Ready (event-fed via ObsEngine.StatusChanged).</summary>
    [ObservableProperty]
    private bool _isObsEngineReady;

    /// <summary>A restream session is running (event-fed via RestreamEngine.StatusChanged).</summary>
    [ObservableProperty]
    private bool _isRestreamRunning;

    /// <summary>Label for the restream toggle button — swaps Start/Stop with engine state.</summary>
    [ObservableProperty]
    private string _restreamActionLabel = "📡 Start restream";

    /// <summary>Persisted Tts.Enabled state (loaded async, refreshed on toggle).</summary>
    [ObservableProperty]
    private bool _isTtsEnabled;

    /// <summary>Label for the TTS toggle button — shows the current mute state.</summary>
    [ObservableProperty]
    private string _ttsActionLabel = "🔊 TTS: …";

    /// <summary>Last quick-action result shown under the buttons (fades out).</summary>
    [ObservableProperty]
    private string _quickActionStatus = "";

    /// <summary>Drives the fade-in/fade-out of <see cref="QuickActionStatus"/>.</summary>
    [ObservableProperty]
    private bool _isQuickActionStatusVisible;

    /// <summary>True when the last quick-action result was a failure (red text).</summary>
    [ObservableProperty]
    private bool _isQuickActionStatusError;

    public DashboardViewModel(
        UiPreferencesService uiPreferences,
        ITwitchService? twitch = null,
        ObsOutputService? obsOutputs = null,
        ObsEngine? obsEngine = null,
        RestreamEngine? restreamEngine = null,
        RestreamFfmpegLocator? ffmpegLocator = null,
        IDataService? dataService = null,
        IProfileService? profileService = null,
        ITtsService? tts = null,
        ILogger<DashboardViewModel>? logger = null)
    {
        _uiPreferences = uiPreferences;
        _twitch = twitch;
        _obsOutputs = obsOutputs;
        _obsEngine = obsEngine;
        _restreamEngine = restreamEngine;
        _ffmpegLocator = ffmpegLocator;
        _dataService = dataService;
        _profileService = profileService;
        _tts = tts;
        _logger = logger;
        _dispatcher = Dispatcher.CurrentDispatcher;

        IsGettingStartedDismissed = uiPreferences.Load().HomeGettingStartedDismissed;

        // Null service → permanently disabled button (CanExecute-bound).
        CanQuickTwitch = _twitch is not null;
        CanQuickReplay = _obsOutputs is not null && _obsEngine is not null;
        CanQuickRestream = _restreamEngine is not null
            && _ffmpegLocator is not null
            && _dataService is not null
            && _profileService is not null;
        CanQuickTts = _tts is not null;

        // Live engine state — both raise from background threads, so every
        // hop back to the captured UI dispatcher before touching observables.
        IsObsEngineReady = _obsOutputs?.IsConfigured ?? false;
        IsRestreamRunning = _restreamEngine?.IsRunning ?? false;
        if (_restreamEngine is not null)
        {
            IsRestreamRunning = _restreamEngine.Status.State
                is RestreamSessionState.WaitingForSource or RestreamSessionState.Streaming;
            _restreamEngine.StatusChanged += OnRestreamEngineStatusChanged;
        }

        if (_obsEngine is not null)
        {
            _obsEngine.StatusChanged += OnObsEngineStatusChanged;
        }

        _quickStatusHideTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = QuickStatusDuration,
        };
        _quickStatusHideTimer.Tick += (_, _) =>
        {
            _quickStatusHideTimer.Stop();
            IsQuickActionStatusVisible = false;
        };

        _ = LoadTtsStateAsync();

        LoadDashboard();
    }

    [RelayCommand]
    private void DismissGettingStarted()
    {
        IsGettingStartedDismissed = true;
        try
        {
            // Load fresh so a dismiss never clobbers theme/shade prefs
            // saved elsewhere during this session.
            var preferences = _uiPreferences.Load();
            preferences.HomeGettingStartedDismissed = true;
            _uiPreferences.Save(preferences);
        }
        catch (Exception)
        {
            // Best-effort persistence; the strip stays hidden for this session.
        }
    }

    // ------------------------------------------------------------------
    // Quick Actions — one-click streamer power tools
    // ------------------------------------------------------------------

    /// <summary>Creates a Twitch clip and copies its URL to the clipboard.</summary>
    [RelayCommand(CanExecute = nameof(CanQuickTwitch))]
    private async Task ClipAsync()
    {
        if (_twitch is null)
        {
            return;
        }

        if (!_twitch.IsConnected)
        {
            ShowQuickStatus("Clip unavailable — Twitch is not connected (link it on the Platforms page).", isError: true);
            return;
        }

        string? url = null;
        try
        {
            url = await _twitch.CreateClipUrlAsync();
        }
        catch (Exception ex)
        {
            _logger?.LogInformation("Quick action clip failed: {Message}", ex.Message);
        }

        if (url is null)
        {
            ShowQuickStatus("Clip failed — the channel may be offline or the token lacks the clips:edit scope.", isError: true);
            return;
        }

        try
        {
            Clipboard.SetText(url);
            ShowQuickStatus("Clip created — URL copied to the clipboard.");
        }
        catch (Exception)
        {
            // Clipboard locked by another process — surface the URL instead.
            ShowQuickStatus($"Clip created — {url}");
        }
    }

    /// <summary>Saves the embedded OBS replay buffer (engine must be Ready, buffer active).</summary>
    [RelayCommand(CanExecute = nameof(CanQuickReplay))]
    private async Task SaveReplayAsync()
    {
        if (_obsOutputs is null)
        {
            return;
        }

        if (!_obsOutputs.IsConfigured)
        {
            ShowQuickStatus("Save Replay unavailable — the OBS engine is not running (start it on the OBS page).", isError: true);
            return;
        }

        try
        {
            // Non-throwing by contract: engine-not-ready and buffer-inactive
            // come back as Success=false with a user-presentable message.
            var result = await _obsOutputs.SaveReplayAsync();
            ShowQuickStatus(result.Message, !result.Success);
        }
        catch (Exception ex)
        {
            _logger?.LogInformation("Quick action replay save failed: {Message}", ex.Message);
            ShowQuickStatus("Replay save failed — the OBS engine stopped while saving.", isError: true);
        }
    }

    /// <summary>Runs a 30-second Twitch commercial break.</summary>
    [RelayCommand(CanExecute = nameof(CanQuickTwitch))]
    private async Task StartAdBreakAsync()
    {
        if (_twitch is null)
        {
            return;
        }

        if (!_twitch.IsConnected)
        {
            ShowQuickStatus("Ad break unavailable — Twitch is not connected (link it on the Platforms page).", isError: true);
            return;
        }

        bool started = false;
        try
        {
            started = await _twitch.StartCommercialAsync(30);
        }
        catch (Exception ex)
        {
            _logger?.LogInformation("Quick action ad break failed: {Message}", ex.Message);
        }

        ShowQuickStatus(
            started ? "Ad break running (30 s)." : "Ad break failed — the channel must be live with commercial permission.",
            !started);
    }

    /// <summary>Starts or stops the FFmpeg restream session from the saved configuration.</summary>
    [RelayCommand(CanExecute = nameof(CanQuickRestream))]
    private async Task ToggleRestreamAsync()
    {
        if (_restreamEngine is null)
        {
            return;
        }

        if (IsRestreamRunning || _restreamEngine.IsRunning)
        {
            await _restreamEngine.StopAsync();
            ShowQuickStatus("Restream session stopped.");
            return;
        }

        await StartRestreamFromSavedConfigAsync();
    }

    /// <summary>Mutes/unmutes TTS (flips persisted Tts.Enabled; muting cancels speech).</summary>
    [RelayCommand(CanExecute = nameof(CanQuickTts))]
    private async Task ToggleTtsMuteAsync()
    {
        if (_tts is null)
        {
            return;
        }

        try
        {
            var settings = await _tts.GetSettingsAsync();
            settings.Enabled = !settings.Enabled;
            await _tts.SaveSettingsAsync(settings);
            if (!settings.Enabled)
            {
                _tts.StopSpeaking();
            }

            IsTtsEnabled = settings.Enabled;
            ShowQuickStatus(settings.Enabled
                ? "TTS unmuted — !tts and alert speech are active."
                : "TTS muted — current speech cancelled.");
        }
        catch (Exception ex)
        {
            _logger?.LogInformation("Quick action TTS toggle failed: {Message}", ex.Message);
            ShowQuickStatus("TTS mute state could not be saved.", isError: true);
        }
    }

    /// <summary>
    /// Rebuilds the session the Restream page would start: FFmpeg from the
    /// locator, destinations from the per-profile credentials file, source
    /// settings from the Restream.Settings config row. Start path only —
    /// stop goes straight to <see cref="RestreamEngine.StopAsync"/>.
    /// </summary>
    private async Task StartRestreamFromSavedConfigAsync()
    {
        var install = await _ffmpegLocator!.LocateAsync();
        if (install is null)
        {
            ShowQuickStatus("FFmpeg was not found — open the Restream page to download it.", isError: true);
            return;
        }

        var destinations = await LoadRestreamDestinationsAsync();
        if (destinations is null || destinations.Count == 0)
        {
            ShowQuickStatus("No saved restream destinations — add them on the Restream page.", isError: true);
            return;
        }

        if (destinations.All(d => !d.Enabled))
        {
            ShowQuickStatus("Every restream destination is disabled — enable one on the Restream page.", isError: true);
            return;
        }

        foreach (var destination in destinations.Where(d => d.Enabled))
        {
            if (RestreamValidation.Validate(destination) is { } problem)
            {
                ShowQuickStatus($"Destination '{destination.Name}': {problem}", isError: true);
                return;
            }
        }

        var source = await LoadRestreamSourceAsync();
        try
        {
            await _restreamEngine!.StartAsync(new RestreamSessionConfig(install.FfmpegPath, source, destinations));
            ShowQuickStatus("Restream session starting — waiting for OBS to publish.");
        }
        catch (Exception ex)
        {
            _logger?.LogInformation("Quick action restream start failed: {Message}", ex.Message);
            ShowQuickStatus("Restream could not start — check the Restream page for details.", isError: true);
        }
    }

    /// <summary>Reads the destination list the Restream page persists per profile. Null when unreadable.</summary>
    private async Task<List<RestreamDestination>?> LoadRestreamDestinationsAsync()
    {
        try
        {
            if (_profileService is null)
            {
                return null;
            }

            var profile = await _profileService.GetCurrentProfileAsync();
            if (profile is null)
            {
                return null;
            }

            var path = Path.Combine(
                _profileService.GetProfileCredentialsPath(profile.Name),
                RestreamDestinationsFileName);
            if (!File.Exists(path))
            {
                return null;
            }

            var json = await File.ReadAllTextAsync(path);
            return JsonSerializer.Deserialize<List<RestreamDestination>>(json, RestreamPersistenceJson);
        }
        catch (Exception ex)
        {
            _logger?.LogInformation("Quick actions: restream destinations could not be read ({Message})", ex.Message);
            return null;
        }
    }

    /// <summary>Source-side settings (mode/port/key) from the Restream.Settings row, defaults on any miss.</summary>
    private async Task<RestreamSourceConfig> LoadRestreamSourceAsync()
    {
        var mode = RestreamOperationMode.SinglePC;
        var port = RestreamSettingsDto.DefaultPort;
        var key = RestreamSettingsDto.DefaultStreamKey;

        try
        {
            var json = await _dataService!.GetConfigValueAsync(RestreamSettingsConfigKey);
            if (!string.IsNullOrEmpty(json)
                && JsonSerializer.Deserialize<RestreamSettingsDto>(json, RestreamPersistenceJson) is { } settings)
            {
                mode = settings.Mode;
                port = settings.IngestPort > 0 ? settings.IngestPort : port;
                key = string.IsNullOrWhiteSpace(settings.IngestStreamKey) ? key : settings.IngestStreamKey.Trim();
            }
        }
        catch (Exception ex)
        {
            _logger?.LogInformation("Quick actions: restream settings could not be read ({Message})", ex.Message);
        }

        return new RestreamSourceConfig(mode, port, key);
    }

    private async Task LoadTtsStateAsync()
    {
        try
        {
            if (_tts is not null)
            {
                IsTtsEnabled = (await _tts.GetSettingsAsync()).Enabled;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogInformation("Quick actions: TTS state could not be read ({Message})", ex.Message);
        }
    }

    /// <summary>Shows a quick-action result under the buttons; auto-hides after 2 s.</summary>
    private void ShowQuickStatus(string message, bool isError = false)
    {
        QuickActionStatus = message;
        IsQuickActionStatusError = isError;
        IsQuickActionStatusVisible = true;
        _quickStatusHideTimer.Stop();
        _quickStatusHideTimer.Start();
    }

    // Engine → UI marshaling (events raise on background threads).

    private void OnRestreamEngineStatusChanged(object? sender, RestreamEngineStatus status) =>
        _dispatcher.InvokeAsync(() => IsRestreamRunning = status.State
            is RestreamSessionState.WaitingForSource or RestreamSessionState.Streaming);

    private void OnObsEngineStatusChanged(ObsEngineStatus status) =>
        _dispatcher.InvokeAsync(() => IsObsEngineReady = _obsOutputs?.IsConfigured ?? false);

    partial void OnIsRestreamRunningChanged(bool value) =>
        RestreamActionLabel = value ? "📡 Stop restream" : "📡 Start restream";

    partial void OnIsTtsEnabledChanged(bool value) =>
        TtsActionLabel = value ? "🔊 TTS: on" : "🔇 TTS: muted";

    private void LoadDashboard()
    {
        // TODO: Load actual data from services
        ActiveTriggerCount = 0;
        TotalActionCount = 0;
        ConnectedPlatformCount = 0;
    }
}
