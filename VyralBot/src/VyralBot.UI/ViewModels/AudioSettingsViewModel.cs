using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System;
using System.Text.Json;
using System.Windows.Threading;
using VyralBot.Core.Services.Audio;
using VyralBot.Data;

namespace VyralBot.UI.ViewModels;

public partial class AudioSettingsViewModel : ViewModelBase, IDisposable
{
    // How often the connected browser-source count is refreshed while the page is open.
    private const int ClientCountPollIntervalMs = 2000;

    private readonly ILogger<AudioSettingsViewModel> _logger;
    private readonly IDataService _dataService;
    // private readonly IAudioWebSocketServer _audioWebSocketServer; // No longer needed
    private readonly ISoundService _soundService;
    private readonly VyralBot.Api.Services.WebhookCoordinatorService? _webhookCoordinator;
    private DispatcherTimer? _clientCountTimer;

    // Port of the embedded web server (source of the browser-source URL); 5000 fallback
    private int _serverPort = 5000;

    [ObservableProperty]
    private bool _streamerSoundsEnabled = true;

    [ObservableProperty]
    private bool _streamSoundsEnabled = true;

    [ObservableProperty]
    private int _masterVolume = 100;

    [ObservableProperty]
    private int _webSocketPort = 5000;

    [ObservableProperty]
    private int _connectedClients;

    [ObservableProperty]
    private bool _isServerRunning;

    [ObservableProperty]
    private string _browserSourceUrl = "http://localhost:5000/audio.html";

    public AudioSettingsViewModel(
        ILogger<AudioSettingsViewModel> logger,
        IDataService dataService,
        ISoundService soundService,
        VyralBot.Api.Services.WebhookCoordinatorService? webhookCoordinator = null)
    {
        _logger = logger;
        _dataService = dataService;
        _soundService = soundService;
        _webhookCoordinator = webhookCoordinator;

        // Reflect the real web-server state instead of a hardcoded true
        if (_webhookCoordinator != null)
        {
            _webhookCoordinator.StatusChanged += OnWebhookStatusChanged;
            IsServerRunning = _webhookCoordinator.IsRunning;
        }
    }

    private void OnWebhookStatusChanged(object? sender, string status)
    {
        // The coordinator raises StatusChanged from background threads - marshal to the UI thread
        System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            IsServerRunning = _webhookCoordinator?.IsRunning ?? false;
        });
    }

    public async Task InitializeAsync()
    {
        try
        {
            // Load settings from database
            var settingsJson = await _dataService.GetConfigValueAsync("AudioSettings");
            if (!string.IsNullOrEmpty(settingsJson))
            {
                var settings = JsonSerializer.Deserialize<AudioSettings>(settingsJson);
                if (settings != null)
                {
                    StreamerSoundsEnabled = settings.StreamerSoundsEnabled;
                    StreamSoundsEnabled = settings.StreamSoundsEnabled;
                    MasterVolume = settings.MasterVolume;
                }
            }
            // The browser source is served by the embedded Kestrel web server; build the
            // OBS URL from the port persisted in the web-server settings (5000 fallback).
            var port = 5000;
            try
            {
                var webSettings = await _dataService.GetWebServerSettingsAsync();
                if (webSettings is not null && webSettings.Port > 0)
                {
                    port = webSettings.Port;
                }
            }
            catch (Exception portEx)
            {
                _logger.LogWarning(portEx, "Could not load web-server settings - using default port 5000 for the browser source URL");
            }

            _serverPort = port;
            WebSocketPort = port;
            BrowserSourceUrl = $"http://localhost:{port}/audio.html";
            IsServerRunning = _webhookCoordinator?.IsRunning ?? false;

            // Poll the live browser-source client count while the page is open.
            StartClientCountPolling();

            _logger.LogInformation("AudioSettingsViewModel initialized - Streamer: {Streamer}, Stream: {Stream}, Volume: {Volume}%",
                StreamerSoundsEnabled, StreamSoundsEnabled, MasterVolume);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initializing AudioSettingsViewModel");
        }
    }

    [RelayCommand]
    private async Task SaveSettingsAsync()
    {
        try
        {
            var settings = new AudioSettings
            {
                StreamerSoundsEnabled = StreamerSoundsEnabled,
                StreamSoundsEnabled = StreamSoundsEnabled,
                MasterVolume = MasterVolume,
                WebSocketPort = WebSocketPort
            };

            var settingsJson = JsonSerializer.Serialize(settings);
            await _dataService.SetConfigValueAsync("AudioSettings", settingsJson);

            _logger.LogInformation("✅ Audio settings saved - Streamer: {Streamer}, Stream: {Stream}, Volume: {Volume}%",
                StreamerSoundsEnabled, StreamSoundsEnabled, MasterVolume);

            System.Windows.MessageBox.Show(
                "Audio settings saved successfully!",
                "Settings Saved",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving audio settings");
            System.Windows.MessageBox.Show(
                $"Failed to save audio settings: {ex.Message}",
                "Error",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private async Task TestSoundAsync()
    {
        try
        {
            _logger.LogInformation("Testing audio with current settings...");

            // Try to find a test sound file
            var soundFiles = await _soundService.GetSoundFilesAsync();
            if (!soundFiles.Any())
            {
                System.Windows.MessageBox.Show(
                    "No sound files found in Sounds/ folder. Please upload some sounds first.",
                    "No Sounds",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
                return;
            }

            // Play the first available sound as test. This uses the same pipeline as
            // real events: SoundService.PlaySoundFileAsync re-reads the persisted
            // AudioSettings blob on every play, so the Streamer/Stream output toggles
            // gate the test exactly like real events, at the current master volume.
            var testSound = soundFiles.First();
            await _soundService.PlaySoundFileAsync(testSound, MasterVolume / 100f);

            _logger.LogInformation("✅ Test sound played: {SoundFile}", testSound);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error testing sound");
            System.Windows.MessageBox.Show(
                $"Failed to test sound: {ex.Message}",
                "Error",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void CopyBrowserSourceUrl()
    {
        try
        {
            System.Windows.Clipboard.SetText(BrowserSourceUrl);
            _logger.LogInformation("Browser source URL copied to clipboard: {Url}", BrowserSourceUrl);

            System.Windows.MessageBox.Show(
                $"Browser source URL copied to clipboard:\n\n{BrowserSourceUrl}\n\nAdd this as a Browser Source in OBS.",
                "URL Copied",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error copying URL to clipboard");
        }
    }

    [RelayCommand]
    private async Task RestartServerAsync()
    {
        if (_webhookCoordinator == null)
        {
            System.Windows.MessageBox.Show(
                "The web server coordinator service is not available, so the server cannot be restarted.",
                "Restart Server",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
            return;
        }

        try
        {
            IsServerRunning = false;
            await _webhookCoordinator.StopAsync();
            var started = await _webhookCoordinator.StartAsync();
            IsServerRunning = _webhookCoordinator.IsRunning;

            if (started)
            {
                // Refresh the browser-source URL in case the configured port changed
                if (_webhookCoordinator.Port > 0)
                {
                    _serverPort = _webhookCoordinator.Port;
                    WebSocketPort = _serverPort;
                    BrowserSourceUrl = $"http://localhost:{_serverPort}/audio.html";
                }

                _logger.LogInformation("Web server restarted successfully on port {Port}", _serverPort);
                System.Windows.MessageBox.Show(
                    $"Web server restarted successfully on port {_serverPort}.",
                    "Restart Server",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information);
            }
            else
            {
                _logger.LogWarning("Web server restart failed - port may be in use");
                System.Windows.MessageBox.Show(
                    $"Failed to restart the web server. Port {_serverPort} may be in use - check the logs for details.",
                    "Restart Server",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            IsServerRunning = _webhookCoordinator.IsRunning;
            _logger.LogError(ex, "Error restarting web server");
            System.Windows.MessageBox.Show(
                $"Failed to restart the web server: {ex.Message}",
                "Restart Server",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
        }
    }

    private void UpdateServerStatus()
    {
        // The audio browser source is served by the embedded Kestrel web server,
        // which is managed from the Web Server settings; reflect its real state.
        IsServerRunning = _webhookCoordinator?.IsRunning ?? false;
        UpdateConnectedClientCount();
    }

    /// <summary>
    /// Starts (or restarts) the DispatcherTimer that refreshes the connected
    /// browser-source count. Idempotent — InitializeAsync can be called more
    /// than once per navigation (view Loaded + MainViewModel).
    /// </summary>
    private void StartClientCountPolling()
    {
        if (_clientCountTimer == null)
        {
            _clientCountTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(ClientCountPollIntervalMs)
            };
            _clientCountTimer.Tick += OnClientCountTimerTick;
        }

        UpdateConnectedClientCount();
        _clientCountTimer.Start();
    }

    private void OnClientCountTimerTick(object? sender, EventArgs e) => UpdateConnectedClientCount();

    private void UpdateConnectedClientCount()
    {
        // Live count of browser-source pages attached to the /audio-ws endpoint,
        // maintained by the web server through AudioWebSocketBroadcaster
        // (incremented on connect, decremented on disconnect, reset on server stop).
        ConnectedClients = AudioWebSocketBroadcaster.ConnectedClientCount;
    }

    /// <summary>
    /// Stops the client-count polling timer and unsubscribes its handler.
    /// </summary>
    public void Dispose()
    {
        if (_clientCountTimer != null)
        {
            _clientCountTimer.Stop();
            _clientCountTimer.Tick -= OnClientCountTimerTick;
            _clientCountTimer = null;
        }

        if (_webhookCoordinator != null)
        {
            _webhookCoordinator.StatusChanged -= OnWebhookStatusChanged;
        }
    }

    partial void OnMasterVolumeChanged(int value)
    {
        // Clamp volume between 0 and 100
        if (value < 0) MasterVolume = 0;
        if (value > 100) MasterVolume = 100;
    }

    partial void OnWebSocketPortChanged(int value)
    {
        // The port is owned by the Web Server settings (embedded Kestrel). The
        // browser-source page derives its WebSocket and sound URLs from
        // window.location, so user edits here are reverted to the configured port.
        if (value != _serverPort)
        {
            _logger.LogWarning("Audio browser source port is managed by Web Server settings ({Port}). Reverting change ({Value}).", _serverPort, value);
            WebSocketPort = _serverPort;
        }
        BrowserSourceUrl = $"http://localhost:{_serverPort}/audio.html";
    }
}
