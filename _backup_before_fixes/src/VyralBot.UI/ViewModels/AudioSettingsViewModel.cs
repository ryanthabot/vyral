using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using VyralBot.Core.Services.Audio;
using VyralBot.Data;

namespace VyralBot.UI.ViewModels;

public partial class AudioSettingsViewModel : ViewModelBase
{
    private readonly ILogger<AudioSettingsViewModel> _logger;
    private readonly IDataService _dataService;
    // private readonly IAudioWebSocketServer _audioWebSocketServer; // No longer needed
    private readonly ISoundService _soundService;

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
        ISoundService soundService)
    {
        _logger = logger;
        _dataService = dataService;
        _soundService = soundService;
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
            WebSocketPort = 5000;
            BrowserSourceUrl = "http://localhost:5000/audio.html";
            IsServerRunning = true;
            ConnectedClients = 0;
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

            // Play the first available sound as test
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
        // No longer needed; server is always running on port 5000
        System.Windows.MessageBox.Show(
            "Audio server is now always running on port 5000.",
            "Info",
            System.Windows.MessageBoxButton.OK,
            System.Windows.MessageBoxImage.Information);
        await Task.CompletedTask;
    }

    private void UpdateServerStatus()
    {
        // No longer tracked; always running
        IsServerRunning = true;
        ConnectedClients = 0;
    }

    partial void OnMasterVolumeChanged(int value)
    {
        // Clamp volume between 0 and 100
        if (value < 0) MasterVolume = 0;
        if (value > 100) MasterVolume = 100;
    }

    partial void OnWebSocketPortChanged(int value)
    {
        // Port is now fixed at 5000
        if (value != 5000)
        {
            _logger.LogWarning("Audio browser source now always uses port 5000. Ignoring user port change.");
            WebSocketPort = 5000;
        }
        BrowserSourceUrl = "http://localhost:5000/audio.html";
    }
}
