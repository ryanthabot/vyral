using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using VyralBot.Core.Services.Audio;
using VyralBot.Data;
using VyralBot.UI.Services.Hotkey;

namespace VyralBot.UI.ViewModels;

/// <summary>
/// ViewModel for managing sound files and event sound assignments.
/// </summary>
public partial class SoundsViewModel : ViewModelBase, IDisposable
{
    private readonly ILogger<SoundsViewModel> _logger;
    private readonly ISoundService _soundService;
    private readonly IDataService _dataService;
    private readonly GlobalHotkeyService _hotkeyService;

    // Master volume multiplier (0..1) derived from MasterVolumePercent; previews
    // multiply by it so they match live playback loudness (live: master * per-event).
    private double _masterVolume = 1.0;

    // True while InitializeAsync assigns MasterVolumePercent from storage — the
    // OnChanged handler must not schedule a write-back of the value it just read.
    private bool _suppressMasterVolumePersist;

    // Debounces the per-event volume slider: cancel the previous pending persist and
    // write only the latest value after the drag settles.
    private CancellationTokenSource? _volumePersistCts;

    // Same debounce pattern for the master volume slider.
    private CancellationTokenSource? _masterVolumePersistCts;

    [ObservableProperty]
    private ObservableCollection<SoundFileItem> _soundFiles = new();

    [ObservableProperty]
    private SoundFileItem? _selectedSoundFile;

    [ObservableProperty]
    private ObservableCollection<EventSoundMapping> _eventMappings = new();

    [ObservableProperty]
    private EventSoundMapping? _selectedEventMapping;

    [ObservableProperty]
    private bool _isLoading;

    /// <summary>
    /// Global master volume (0-100). Persisted (debounced) into the same
    /// AudioSettings blob SoundService reads; SoundService multiplies it with the
    /// per-event volume on every live playback.
    /// </summary>
    [ObservableProperty]
    private int _masterVolumePercent = 100;

    /// <summary>
    /// Name of the sound whose hotkey is being captured right now (null = not
    /// capturing). Click-to-capture state for the library's Hotkey column.
    /// </summary>
    [ObservableProperty]
    private string? _capturingSoundName;

    /// <summary>Status line shown next to the capture banner (Esc cancels, etc.).</summary>
    [ObservableProperty]
    private string? _captureHint;

    /// <summary>True while a hotkey capture is in progress (banner visibility).</summary>
    public bool IsCapturing => CapturingSoundName != null;

    partial void OnCapturingSoundNameChanged(string? value)
    {
        OnPropertyChanged(nameof(IsCapturing));
    }

    public SoundsViewModel(
        ILogger<SoundsViewModel> logger,
        ISoundService soundService,
        IDataService dataService,
        GlobalHotkeyService hotkeyService)
    {
        _logger = logger;
        _soundService = soundService;
        _dataService = dataService;
        _hotkeyService = hotkeyService;
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
    /// Clamps the slider value and schedules a debounced persist into the
    /// AudioSettings blob (read-modify-write, other audio settings preserved).
    /// </summary>
    partial void OnMasterVolumePercentChanged(int value)
    {
        var clamped = System.Math.Clamp(value, 0, 100);
        if (clamped != value)
        {
            MasterVolumePercent = clamped; // re-enters once with the clamped value
            return;
        }

        _masterVolume = clamped / 100.0;

        if (_suppressMasterVolumePersist)
        {
            return;
        }

        _masterVolumePersistCts?.Cancel();
        _masterVolumePersistCts?.Dispose();
        _masterVolumePersistCts = new CancellationTokenSource();
        _ = PersistMasterVolumeAsync(_masterVolumePersistCts.Token);
    }

    /// <summary>
    /// Writes MasterVolumePercent into the persisted AudioSettings JSON after the
    /// slider settles (600ms). Read-modify-write so toggles/port set on the Audio
    /// Settings page are never clobbered.
    /// </summary>
    private async Task PersistMasterVolumeAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(600, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return; // superseded by a newer slider value
        }

        try
        {
            var settingsJson = await _dataService.GetConfigValueAsync("AudioSettings");
            AudioSettings settings;
            try
            {
                settings = System.Text.Json.JsonSerializer.Deserialize<AudioSettings>(settingsJson ?? string.Empty)
                    ?? new AudioSettings();
            }
            catch (System.Text.Json.JsonException)
            {
                settings = new AudioSettings();
            }

            settings.MasterVolume = System.Math.Clamp(MasterVolumePercent, 0, 100);
            var json = System.Text.Json.JsonSerializer.Serialize(settings);
            await _dataService.SetConfigValueAsync("AudioSettings", json);

            _logger.LogInformation("Master volume set to {Volume}% (AudioSettings)", settings.MasterVolume);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error persisting master volume");
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
            var percent = 100;
            var settingsJson = await _dataService.GetConfigValueAsync("AudioSettings");
            if (!string.IsNullOrEmpty(settingsJson))
            {
                var settings = System.Text.Json.JsonSerializer.Deserialize<AudioSettings>(settingsJson);
                if (settings != null)
                {
                    percent = System.Math.Clamp(settings.MasterVolume, 0, 100);
                }
            }

            _suppressMasterVolumePersist = true;
            MasterVolumePercent = percent; // updates _masterVolume via OnChanged
            _suppressMasterVolumePersist = false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading master volume for previews - defaulting to 100%");
            _suppressMasterVolumePersist = true;
            MasterVolumePercent = 100;
            _suppressMasterVolumePersist = false;
        }
    }

    /// <summary>
    /// Load all sound files from the Sounds directory, including cheap duration
    /// display (.wav header parse only — other formats need a decoder, so they
    /// simply show no duration).
    /// </summary>
    private async Task LoadSoundFilesAsync()
    {
        var files = await _soundService.GetSoundFilesAsync();
        var soundsDirectory = _soundService.GetSoundsDirectory();

        // Header parsing is I/O-bound — keep it off the UI thread.
        var durations = await Task.Run(() =>
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in files)
            {
                if (!file.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var duration = TryGetWavDuration(Path.Combine(soundsDirectory, file));
                if (duration != null)
                {
                    map[file] = duration;
                }
            }

            return map;
        });

        SoundFiles.Clear();
        foreach (var file in files.OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            SoundFiles.Add(new SoundFileItem
            {
                Name = file,
                Duration = durations.TryGetValue(file, out var duration) ? duration : null
            });
        }

        // Rows are rebuilt from scratch — re-apply the saved hotkey assignments.
        await ApplyHotkeysToItemsAsync();

        _logger.LogInformation("Loaded {Count} sound files", SoundFiles.Count);
    }

    /// <summary>
    /// Parses a .wav header (RIFF chunk walk: fmt byte-rate + data size) for the
    /// duration string. Returns null for anything that is not a well-formed wav —
    /// display is simply omitted rather than guessed.
    /// </summary>
    private static string? TryGetWavDuration(string filePath)
    {
        try
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var reader = new BinaryReader(stream);

            if (stream.Length < 44 ||
                reader.ReadUInt32() != 0x46464952U || // "RIFF"
                reader.ReadUInt32() != 0x45564157U)   // "WAVE"
            {
                return null;
            }

            var byteRate = 0;
            while (stream.Position + 8 <= stream.Length)
            {
                var chunkId = reader.ReadUInt32();
                var chunkSize = reader.ReadUInt32();

                if (chunkId == 0x20746D66U) // "fmt "
                {
                    if (chunkSize < 16)
                    {
                        return null;
                    }

                    reader.ReadUInt16(); // audio format
                    reader.ReadUInt16(); // channels
                    reader.ReadUInt32(); // sample rate
                    byteRate = (int)reader.ReadUInt32();
                    stream.Position += chunkSize - 12; // skip the rest of fmt (blockAlign, bits, extras)
                }
                else if (chunkId == 0x61746164U) // "data"
                {
                    if (byteRate <= 0)
                    {
                        return null;
                    }

                    var seconds = chunkSize / (double)byteRate;
                    return TimeSpan.FromSeconds(seconds).ToString(@"m\:ss", CultureInfo.InvariantCulture);
                }
                else
                {
                    // Skip unknown chunk (chunks are word-aligned)
                    stream.Position += chunkSize + (chunkSize & 1);
                }
            }

            return null;
        }
        catch
        {
            return null; // corrupt/locked file — just hide the duration
        }
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
            ("Twitch", "cheer", "Cheer"),
            // Twitch channel-points redemption — mirrors the Kick redemption
            // row; ChatViewModel's OnTwitchChannelPointsRedeemAsync already
            // plays it via PlayEventSoundAsync("redemption", "Twitch").
            ("Twitch", "redemption", "Reward Redeemed"),
            // YouTube Super Chat/Sticker — keyed "cheer" because ChatViewModel's
            // OnYouTubeSuperChatAsync plays PlayEventSoundAsync("cheer", "YouTube")
            // (SoundService resolves EventSound_YouTube_cheer). The WORKFLOW
            // trigger key is the separate "superchat" (WorkflowTriggerService)
            // so flows can target Twitch bits vs YouTube Super Chats precisely.
            ("YouTube", "cheer", "Super Chat"),
            // YouTube membership (new member / milestone) — row ready for
            // ChatViewModel's OnYouTubeMembershipStatusAsync to play
            // PlayEventSoundAsync("membership", "YouTube").
            ("YouTube", "membership", "Membership"),
            // Discord member events — playback wired in ChatViewModel's
            // OnDiscordMemberJoinedAsync ("follow") and OnDiscordBoostAsync
            // ("subscribe" — boosts are Discord's paid-support counterpart).
            ("Discord", "follow", "Member Joined"),
            ("Discord", "subscribe", "Server Boost")
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
    /// Upload new sound file(s) via the file dialog.
    /// </summary>
    [RelayCommand]
    private async Task UploadSoundAsync()
    {
        try
        {
            var dialog = new OpenFileDialog
            {
                Title = "Select Sound File(s)",
                Filter = "Audio Files (*.wav;*.mp3;*.ogg;*.m4a)|*.wav;*.mp3;*.ogg;*.m4a|All Files (*.*)|*.*",
                Multiselect = true
            };

            if (dialog.ShowDialog() == true)
            {
                IsLoading = true;
                var uploaded = new List<string>();
                foreach (var sourceFile in dialog.FileNames)
                {
                    _logger.LogInformation("Uploading sound file: {FileName}", Path.GetFileName(sourceFile));
                    uploaded.Add(await _soundService.UploadSoundAsync(sourceFile));
                }

                await LoadSoundFilesAsync();
                SelectedSoundFile = SoundFiles.FirstOrDefault(f =>
                    string.Equals(f.Name, uploaded.LastOrDefault(), StringComparison.OrdinalIgnoreCase));

                MessageBox.Show(
                    $"{uploaded.Count} sound file(s) uploaded successfully!",
                    "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading sound file");
            MessageBox.Show($"Error uploading sounds: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Import sound file(s) dragged onto the library list. Files are copied into
    /// the profile sounds folder by <see cref="ISoundService.UploadSoundAsync"/>.
    /// </summary>
    [RelayCommand]
    private async Task DropSoundFilesAsync(DragEventArgs? e)
    {
        if (e?.Data.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0)
        {
            return;
        }

        e.Handled = true;

        var supported = files.Where(IsSupportedAudioFile).ToList();
        if (supported.Count == 0)
        {
            MessageBox.Show(
                "No supported audio files in the drop. Supported formats: .wav, .mp3, .ogg, .m4a",
                "Sounds", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            IsLoading = true;
            var added = new List<string>();
            foreach (var file in supported)
            {
                _logger.LogInformation("Importing dropped sound file: {FileName}", Path.GetFileName(file));
                added.Add(await _soundService.UploadSoundAsync(file));
            }

            await LoadSoundFilesAsync();
            SelectedSoundFile = SoundFiles.FirstOrDefault(f =>
                string.Equals(f.Name, added.LastOrDefault(), StringComparison.OrdinalIgnoreCase));

            _logger.LogInformation("Imported {Count} sound file(s) via drag-drop", added.Count);
            MessageBox.Show(
                $"{added.Count} sound file(s) added to the library.",
                "Sounds", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error importing dropped sound files");
            MessageBox.Show($"Error importing sounds: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Shows the copy cursor while supported audio files are dragged over the list.
    /// </summary>
    [RelayCommand]
    private void DragOverSoundFiles(DragEventArgs? e)
    {
        if (e == null)
        {
            return;
        }

        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private static bool IsSupportedAudioFile(string path)
    {
        return Path.GetExtension(path).ToLowerInvariant() is ".wav" or ".mp3" or ".ogg" or ".m4a";
    }

    /// <summary>
    /// Delete the selected sound file.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanDeleteSound))]
    private async Task DeleteSoundAsync()
    {
        var fileToDelete = SelectedSoundFile?.Name;
        if (fileToDelete == null) return;

        try
        {
            var result = MessageBox.Show(
                $"Are you sure you want to delete '{fileToDelete}'?\n\nThis will also remove all event assignments using this sound.",
                "Confirm Delete",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes) return;

            IsLoading = true;
            _logger.LogInformation("Deleting sound file: {FileName}", fileToDelete);

            await _soundService.DeleteSoundAsync(fileToDelete);

            // Release the deleted sound's global hotkey slot too (zero-orphans).
            await _hotkeyService.ClearSoundHotkeyAsync(fileToDelete);

            // Remove assignments that used this sound
            foreach (var mapping in EventMappings.Where(m => m.SoundFile == fileToDelete).ToList())
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
        var fileName = SelectedSoundFile?.Name;
        if (fileName == null) return;

        await PlayFileInternalAsync(fileName);
    }

    private bool CanPlaySound() => SelectedSoundFile != null;

    /// <summary>
    /// Per-row test-play button on the library list: previews one file at the
    /// current master volume without needing to select it first.
    /// </summary>
    [RelayCommand]
    private async Task PlayFileAsync(string? fileName)
    {
        if (string.IsNullOrEmpty(fileName)) return;

        await PlayFileInternalAsync(fileName);
    }

    private async Task PlayFileInternalAsync(string fileName)
    {
        try
        {
            _logger.LogInformation("Playing sound file: {FileName}", fileName);
            // Apply master volume like the live path so previews are not louder
            await _soundService.PlaySoundFileAsync(fileName, (float)_masterVolume);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error playing sound file");
            MessageBox.Show($"Error playing sound: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ------------------------------------------------------------------
    // Global hotkeys (KI-55): the Hotkey column on each library row. The
    // GlobalHotkeyService singleton owns the actual RegisterHotKey slots and
    // playback; this VM only drives capture UX and reflects assignments.
    // ------------------------------------------------------------------

    /// <summary>
    /// Copies the persisted {sound: gesture} map onto the library rows. Called
    /// after every list rebuild (init, upload, drag-drop import, delete).
    /// </summary>
    private async Task ApplyHotkeysToItemsAsync()
    {
        try
        {
            var map = await _hotkeyService.GetSoundHotkeyMapAsync();
            foreach (var item in SoundFiles)
            {
                item.Hotkey = map.TryGetValue(item.Name, out var gesture) ? gesture : null;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error applying saved hotkeys to the sound list");
        }
    }

    /// <summary>
    /// Starts click-to-capture for a row: the next modifier+key press becomes
    /// that sound's global hotkey (registered and persisted immediately).
    /// </summary>
    [RelayCommand]
    private void BeginCaptureHotkey(SoundFileItem? item)
    {
        if (item == null) return;

        EndCapture(); // only one row captures at a time
        item.IsCapturingHotkey = true;
        CapturingSoundName = item.Name;
        CaptureHint = "Press a modifier + key combo (Esc cancels)";
        _logger.LogInformation("Hotkey capture started for '{Sound}'", item.Name);
    }

    /// <summary>Aborts the in-progress capture without changing anything.</summary>
    [RelayCommand]
    private void CancelHotkeyCapture()
    {
        EndCapture();
    }

    /// <summary>
    /// Key sink for capture mode — wired from SoundsView's PreviewKeyDown
    /// (Alt combos arrive as Key.System, the view resolves SystemKey first).
    /// Returns true when the key was consumed (capture active).
    /// </summary>
    public async Task<bool> HandleCaptureKeyDownAsync(Key key, ModifierKeys modifiers)
    {
        if (CapturingSoundName == null)
        {
            return false;
        }

        var item = SoundFiles.FirstOrDefault(f =>
            string.Equals(f.Name, CapturingSoundName, StringComparison.OrdinalIgnoreCase));
        if (item == null)
        {
            EndCapture();
            return false;
        }

        // Modifier-only presses keep the capture alive while the combo is built.
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
        {
            CaptureHint = "Now press a regular key (Esc cancels)";
            return true;
        }

        if (key == Key.Escape && modifiers == ModifierKeys.None)
        {
            EndCapture();
            return true;
        }

        if (!HotkeyGesture.FromWpfKey(key, modifiers, out var gesture))
        {
            CaptureHint = modifiers == ModifierKeys.None
                ? "Add Ctrl, Alt, Shift or Win to the combo (Esc cancels)"
                : "That key can't be used in a global hotkey (Esc cancels)";
            return true;
        }

        var display = HotkeyGesture.Format(gesture);
        var result = await _hotkeyService.SetSoundHotkeyAsync(item.Name, display);
        switch (result.Outcome)
        {
            case SoundHotkeyOutcome.Registered:
            case SoundHotkeyOutcome.ReplacedExisting:
                item.Hotkey = display;
                EndCapture();
                _logger.LogInformation("Hotkey {Gesture} assigned to '{Sound}'", display, item.Name);
                break;

            case SoundHotkeyOutcome.ConflictOtherSound:
                EndCapture();
                MessageBox.Show(
                    $"{display} is already assigned to '{result.ConflictSound}'. Pick a different combo.",
                    "Hotkey in use", MessageBoxButton.OK, MessageBoxImage.Warning);
                break;

            case SoundHotkeyOutcome.OsRejected:
                EndCapture();
                MessageBox.Show(
                    $"{display} is already claimed by another application (or the OS). Pick a different combo.",
                    "Hotkey unavailable", MessageBoxButton.OK, MessageBoxImage.Warning);
                break;

            case SoundHotkeyOutcome.LimitReached:
                EndCapture();
                MessageBox.Show(
                    $"All {GlobalHotkeyService.MaxSlots} hotkey slots are in use. Clear one first.",
                    "Hotkey limit", MessageBoxButton.OK, MessageBoxImage.Warning);
                break;

            case SoundHotkeyOutcome.NoWindowAttached:
                EndCapture();
                MessageBox.Show(
                    "Global hotkeys are unavailable (main window not ready).",
                    "Hotkeys unavailable", MessageBoxButton.OK, MessageBoxImage.Warning);
                break;

            default:
                EndCapture();
                MessageBox.Show(
                    "That combination is not valid for a global hotkey.",
                    "Invalid hotkey", MessageBoxButton.OK, MessageBoxImage.Warning);
                break;
        }

        return true;
    }

    /// <summary>Clears a row's hotkey: unregisters the OS slot and unpersists it.</summary>
    [RelayCommand]
    private async Task ClearHotkeyAsync(SoundFileItem? item)
    {
        if (item == null || string.IsNullOrEmpty(item.Hotkey)) return;

        try
        {
            await _hotkeyService.ClearSoundHotkeyAsync(item.Name);
            item.Hotkey = null;
            _logger.LogInformation("Cleared hotkey for '{Sound}'", item.Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error clearing hotkey for '{Sound}'", item.Name);
            MessageBox.Show($"Error clearing hotkey: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>Resets capture state (row flag + banner) without touching registrations.</summary>
    private void EndCapture()
    {
        var item = SoundFiles.FirstOrDefault(f =>
            string.Equals(f.Name, CapturingSoundName, StringComparison.OrdinalIgnoreCase));
        if (item != null)
        {
            item.IsCapturingHotkey = false;
        }

        CapturingSoundName = null;
        CaptureHint = null;
    }

    /// <summary>
    /// Assign a sound to an event.
    /// </summary>
    [RelayCommand]
    private async Task AssignSoundAsync(EventSoundMapping? mapping)
    {
        var fileName = SelectedSoundFile?.Name;
        if (mapping == null || fileName == null) return;

        try
        {
            IsLoading = true;
            var soundKey = $"EventSound_{mapping.Platform}_{mapping.EventType}";
            await _dataService.SetConfigValueAsync(soundKey, fileName);

            mapping.SoundFile = fileName;

            _logger.LogInformation("Assigned sound '{Sound}' to {Platform} {Event}", fileName, mapping.Platform, mapping.EventType);
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

    /// <summary>
    /// Cancels any pending debounced volume persists and hotkey capture when the
    /// page is navigated away. Live hotkey registrations are owned by the
    /// GlobalHotkeyService singleton and keep firing.
    /// </summary>
    public void Dispose()
    {
        EndCapture();

        _volumePersistCts?.Cancel();
        _volumePersistCts?.Dispose();
        _volumePersistCts = null;

        _masterVolumePersistCts?.Cancel();
        _masterVolumePersistCts?.Dispose();
        _masterVolumePersistCts = null;
    }
}

/// <summary>
/// One row of the sound library list: file name plus (for .wav, where the header
/// makes it cheap) the duration, and its global hotkey assignment.
/// </summary>
public partial class SoundFileItem : ObservableObject
{
    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string? _duration;

    /// <summary>Assigned global hotkey ("Ctrl+Alt+1") or null when unset.</summary>
    [ObservableProperty]
    private string? _hotkey;

    /// <summary>True while this row is waiting for a key combo (capture mode).</summary>
    [ObservableProperty]
    private bool _isCapturingHotkey;

    public bool HasDuration => !string.IsNullOrEmpty(Duration);

    public bool HasHotkey => !string.IsNullOrEmpty(Hotkey);

    partial void OnDurationChanged(string? value)
    {
        OnPropertyChanged(nameof(HasDuration));
    }

    partial void OnHotkeyChanged(string? value)
    {
        OnPropertyChanged(nameof(HasHotkey));
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
