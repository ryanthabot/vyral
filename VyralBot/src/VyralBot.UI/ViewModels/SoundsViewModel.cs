using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using VyralBot.Core.Services.Audio;
using VyralBot.Data;

namespace VyralBot.UI.ViewModels;

/// <summary>
/// ViewModel for managing sound files and event sound assignments.
/// </summary>
public partial class SoundsViewModel : ViewModelBase
{
    private readonly ILogger<SoundsViewModel> _logger;
    private readonly ISoundService _soundService;
    private readonly IDataService _dataService;

    // Master volume (0..1) from the persisted AudioSettings blob; previews multiply
    // by it so they match live playback loudness (live: master * per-event volume).
    private double _masterVolume = 1.0;

    // Debounces the per-event volume slider: cancel the previous pending persist and
    // write only the latest value after the drag settles.
    private CancellationTokenSource? _volumePersistCts;

    [ObservableProperty]
    private ObservableCollection<string> _soundFiles = new();

    [ObservableProperty]
    private string? _selectedSoundFile;

    [ObservableProperty]
    private ObservableCollection<EventSoundMapping> _eventMappings = new();

    [ObservableProperty]
    private EventSoundMapping? _selectedEventMapping;

    [ObservableProperty]
    private bool _isLoading;

    public SoundsViewModel(
        ILogger<SoundsViewModel> logger,
        ISoundService soundService,
        IDataService dataService)
    {
        _logger = logger;
        _soundService = soundService;
        _dataService = dataService;
    }

    /// <summary>
    /// Initialize the ViewModel by loading sound files and event mappings.
    /// </summary>
    public async Task InitializeAsync()
    {
        IsLoading = true;
        try
        {
            await LoadMasterVolumeAsync();
            await LoadSoundFilesAsync();
            await LoadEventMappingsAsync();
            _logger.LogInformation("SoundsViewModel initialized successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initializing SoundsViewModel");
            MessageBox.Show($"Error loading sounds: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Loads the master volume from the persisted AudioSettings blob (same key SoundService
    /// reads) so Test/Play previews can apply it exactly like the live playback path.
    /// </summary>
    private async Task LoadMasterVolumeAsync()
    {
        try
        {
            var settingsJson = await _dataService.GetConfigValueAsync("AudioSettings");
            if (!string.IsNullOrEmpty(settingsJson))
            {
                var settings = System.Text.Json.JsonSerializer.Deserialize<AudioSettings>(settingsJson);
                if (settings != null)
                {
                    _masterVolume = System.Math.Clamp(settings.MasterVolume / 100.0, 0.0, 1.0);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading master volume for previews - defaulting to 100%");
            _masterVolume = 1.0;
        }
    }

    /// <summary>
    /// Load all sound files from the Sounds directory.
    /// </summary>
    private async Task LoadSoundFilesAsync()
    {
        SoundFiles.Clear();
        var files = await _soundService.GetSoundFilesAsync();
        foreach (var file in files.OrderBy(f => f))
        {
            SoundFiles.Add(file);
        }
        _logger.LogInformation("Loaded {Count} sound files", SoundFiles.Count);
    }

    /// <summary>
    /// Load all event sound mappings from the database.
    /// </summary>
    private async Task LoadEventMappingsAsync()
    {
        EventMappings.Clear();

        var eventTypes = new[]
        {
            ("Kick", "follow", "Follow"),
            ("Kick", "subscribe", "Subscribe"),
            ("Kick", "giftsub", "Gift Sub"),
            ("Kick", "host", "Host"),
            // Kick donation/redemption (KickKicksGiftedEvent / KickRewardRedeemedEvent);
            // shaped like the Twitch cheer row — playback wired in ChatViewModel's
            // OnKickKicksGiftedAsync / OnKickRewardRedeemedAsync handlers.
            ("Kick", "kicks_gifted", "Kicks Gifted"),
            ("Kick", "redemption", "Reward Redeemed"),
            ("Twitch", "follow", "Follow"),
            ("Twitch", "subscribe", "Subscribe"),
            ("Twitch", "resub", "Re-sub"),
            ("Twitch", "giftsub", "Gift Sub"),
            ("Twitch", "raid", "Raid"),
            ("Twitch", "cheer", "Cheer")
        };

        foreach (var (platform, eventType, displayName) in eventTypes)
        {
            var soundKey = $"EventSound_{platform}_{eventType}";
            var volumeKey = $"EventVolume_{platform}_{eventType}";

            var soundFile = await _dataService.GetConfigValueAsync(soundKey);
            var volumeStr = await _dataService.GetConfigValueAsync(volumeKey);
            // Volumes are persisted as invariant "F2" strings in the 0..1 range
            // ("0.00".."1.00"); SoundService.GetEventVolumeAsync parses the same format.
            var volume = ParseStoredVolume(volumeStr);

            EventMappings.Add(new EventSoundMapping
            {
                Platform = platform,
                EventType = eventType,
                DisplayName = $"{platform} - {displayName}",
                SoundFile = soundFile,
                Volume = volume
            });
        }

        _logger.LogInformation("Loaded {Count} event mappings", EventMappings.Count);
    }

    /// <summary>
    /// Upload a new sound file.
    /// </summary>
    [RelayCommand]
    private async Task UploadSoundAsync()
    {
        try
        {
            var dialog = new OpenFileDialog
            {
                Title = "Select Sound File",
                Filter = "Audio Files (*.wav;*.mp3;*.ogg;*.m4a)|*.wav;*.mp3;*.ogg;*.m4a|All Files (*.*)|*.*",
                Multiselect = false
            };

            if (dialog.ShowDialog() == true)
            {
                IsLoading = true;
                var sourceFile = dialog.FileName;
                var fileName = Path.GetFileName(sourceFile);

                _logger.LogInformation("Uploading sound file: {FileName}", fileName);
                var uploadedFileName = await _soundService.UploadSoundAsync(sourceFile);

                await LoadSoundFilesAsync();
                SelectedSoundFile = uploadedFileName;

                MessageBox.Show($"Sound file '{uploadedFileName}' uploaded successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading sound file");
            MessageBox.Show($"Error uploading sound: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Delete the selected sound file.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanDeleteSound))]
    private async Task DeleteSoundAsync()
    {
        if (SelectedSoundFile == null) return;

        try
        {
            var result = MessageBox.Show(
                $"Are you sure you want to delete '{SelectedSoundFile}'?\n\nThis will also remove all event assignments using this sound.",
                "Confirm Delete",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes) return;

            IsLoading = true;
            _logger.LogInformation("Deleting sound file: {FileName}", SelectedSoundFile);

            await _soundService.DeleteSoundAsync(SelectedSoundFile);

            // Remove assignments that used this sound
            foreach (var mapping in EventMappings.Where(m => m.SoundFile == SelectedSoundFile).ToList())
            {
                mapping.SoundFile = null;
                var soundKey = $"EventSound_{mapping.Platform}_{mapping.EventType}";
                await _dataService.SetConfigValueAsync(soundKey, string.Empty);
            }

            await LoadSoundFilesAsync();
            SelectedSoundFile = null;

            MessageBox.Show("Sound file deleted successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting sound file");
            MessageBox.Show($"Error deleting sound: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private bool CanDeleteSound() => SelectedSoundFile != null;

    /// <summary>
    /// Play the selected sound file for testing.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanPlaySound))]
    private async Task PlaySoundAsync()
    {
        if (SelectedSoundFile == null) return;

        try
        {
            _logger.LogInformation("Playing sound file: {FileName}", SelectedSoundFile);
            // Apply master volume like the live path so previews are not louder
            await _soundService.PlaySoundFileAsync(SelectedSoundFile, (float)_masterVolume);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error playing sound file");
            MessageBox.Show($"Error playing sound: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private bool CanPlaySound() => SelectedSoundFile != null;

    /// <summary>
    /// Assign a sound to an event.
    /// </summary>
    [RelayCommand]
    private async Task AssignSoundAsync(EventSoundMapping? mapping)
    {
        if (mapping == null || SelectedSoundFile == null) return;

        try
        {
            IsLoading = true;
            var soundKey = $"EventSound_{mapping.Platform}_{mapping.EventType}";
            await _dataService.SetConfigValueAsync(soundKey, SelectedSoundFile);

            mapping.SoundFile = SelectedSoundFile;

            _logger.LogInformation("Assigned sound '{Sound}' to {Platform} {Event}", SelectedSoundFile, mapping.Platform, mapping.EventType);
            MessageBox.Show($"Sound assigned to {mapping.DisplayName}!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error assigning sound");
            MessageBox.Show($"Error assigning sound: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Remove sound assignment from an event.
    /// </summary>
    [RelayCommand]
    private async Task UnassignSoundAsync(EventSoundMapping? mapping)
    {
        if (mapping == null) return;

        try
        {
            IsLoading = true;
            var soundKey = $"EventSound_{mapping.Platform}_{mapping.EventType}";
            await _dataService.SetConfigValueAsync(soundKey, string.Empty);

            mapping.SoundFile = null;

            _logger.LogInformation("Unassigned sound from {Platform} {Event}", mapping.Platform, mapping.EventType);
            MessageBox.Show($"Sound removed from {mapping.DisplayName}!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error unassigning sound");
            MessageBox.Show($"Error removing sound: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Test play an event sound with its volume.
    /// </summary>
    [RelayCommand]
    private async Task TestEventSoundAsync(EventSoundMapping? mapping)
    {
        if (mapping == null || string.IsNullOrEmpty(mapping.SoundFile)) return;

        try
        {
            _logger.LogInformation("Testing event sound: {Platform} {Event} - {Sound} @ {Volume}", 
                mapping.Platform, mapping.EventType, mapping.SoundFile, mapping.Volume);

            // Multiply by master volume like the live path (SoundService: master * per-event)
            await _soundService.PlaySoundFileAsync(mapping.SoundFile, (float)(mapping.Volume * _masterVolume));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error testing event sound");
            MessageBox.Show($"Error playing event sound: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// Update event volume when slider changes. The slider fires this on every drag tick,
    /// so the persist is debounced: only the latest value is written, after a 400ms pause.
    /// </summary>
    [RelayCommand]
    private async Task UpdateEventVolumeAsync(EventSoundMapping? mapping)
    {
        if (mapping == null) return;

        // Cancel the previous pending persist (if any) and arm a new one
        _volumePersistCts?.Cancel();
        _volumePersistCts?.Dispose();
        _volumePersistCts = new CancellationTokenSource();
        var cts = _volumePersistCts;

        try
        {
            await Task.Delay(400, cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer slider value - nothing to persist
            return;
        }

        try
        {
            var volumeKey = $"EventVolume_{mapping.Platform}_{mapping.EventType}";
            // Persist in the same invariant "F2" (0..1) format SoundService parses.
            var volume = System.Math.Clamp(mapping.Volume, 0.0, 1.0);
            await _dataService.SetConfigValueAsync(volumeKey, volume.ToString("F2", CultureInfo.InvariantCulture));
            _logger.LogInformation("Updated volume for {Platform} {Event} to {Volume}",
                mapping.Platform, mapping.EventType, volume.ToString("F2", CultureInfo.InvariantCulture));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating event volume");
        }
    }

    /// <summary>
    /// Parses a stored per-event volume. Accepts the invariant "F2" 0..1 format this
    /// page writes, plus legacy whole-percent values (e.g. "70"); defaults to full
    /// volume when nothing valid is stored.
    /// </summary>
    private static double ParseStoredVolume(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return 1.0;
        }

        if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var volume))
        {
            if (volume > 1.0)
            {
                volume /= 100.0;
            }

            return System.Math.Clamp(volume, 0.0, 1.0);
        }

        return 1.0;
    }
}

/// <summary>
/// Represents a mapping between an event type and a sound file.
/// </summary>
public partial class EventSoundMapping : ObservableObject
{
    [ObservableProperty]
    private string _platform = string.Empty;

    [ObservableProperty]
    private string _eventType = string.Empty;

    [ObservableProperty]
    private string _displayName = string.Empty;

    [ObservableProperty]
    private string? _soundFile;

    [ObservableProperty]
    private double _volume = 1.0;
}
