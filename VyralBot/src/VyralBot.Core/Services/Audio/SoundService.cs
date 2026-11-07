using Microsoft.Extensions.Logging;
using VyralBot.Data;
using System.Globalization;
using System.Windows.Media;
using System.Windows.Threading;
using VyralBot.Core.Services.Audio;
using VyralBot.Core.Services.Profiles;

namespace VyralBot.Core.Services.Audio;

/// <summary>
/// Manages sound files and playback for event alerts.
/// </summary>
public class SoundService : ISoundService
{
    private readonly ILogger<SoundService> _logger;
    private readonly IDataService _dataService;
    private readonly string _soundsDirectory;

    public SoundService(
        ILogger<SoundService> logger,
        IDataService dataService,
        IProfileService? profileService = null)
    {
        _logger = logger;
        _dataService = dataService;

        // Legacy app-global location: wwwroot/sounds under the app output. Still used
        // verbatim on first run (before any profile exists) and kept as the static-root
        // fallback for browser sources.
        var globalDirectory = Path.Combine(AppContext.BaseDirectory, "wwwroot", "sounds");

        // Sounds are user data, so the default location is the current profile's data
        // folder (profiles are the app's user-data boundary). Profile switching requires
        // an app restart, so resolving the directory once at construction is safe.
        var profileName = profileService?.GetCurrentProfileName();
        _soundsDirectory = string.IsNullOrWhiteSpace(profileName)
            ? globalDirectory
            : Path.Combine(profileService!.GetProfileDataPath(profileName), "sounds");

        Directory.CreateDirectory(_soundsDirectory);
        MigrateLegacySounds(globalDirectory);
    }

    /// <summary>
    /// One-time best-effort migration into the per-profile sounds folder. Sources are
    /// the app-global wwwroot/sounds under the app output and (older still) the
    /// VyralBot.Api/wwwroot/sounds folder next to it. Copies files over only while the
    /// profile folder is still empty (no overwrite, never deletes the old location,
    /// never throws).
    /// </summary>
    private void MigrateLegacySounds(string globalDirectory)
    {
        try
        {
            var legacyDirectories = new[]
            {
                globalDirectory,
                Path.Combine(AppContext.BaseDirectory, "..", "VyralBot.Api", "wwwroot", "sounds")
            };

            // Running against the app-global location (first run, no profile yet):
            // that IS the legacy location — nothing to migrate.
            var destinationRoot = Path.GetFullPath(_soundsDirectory);
            if (legacyDirectories.Any(d =>
                    string.Equals(Path.GetFullPath(d), destinationRoot, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            // Only migrate the first time: once the profile has any sound file, the
            // profile folder is authoritative and the old location is left untouched.
            if (Directory.GetFiles(_soundsDirectory).Any(IsSupportedAudioFile))
            {
                return;
            }

            var migrated = 0;
            foreach (var legacyDirectory in legacyDirectories)
            {
                if (!Directory.Exists(legacyDirectory))
                {
                    continue;
                }

                foreach (var file in Directory.GetFiles(legacyDirectory))
                {
                    var destination = Path.Combine(_soundsDirectory, Path.GetFileName(file));
                    if (!File.Exists(destination))
                    {
                        File.Copy(file, destination);
                        migrated++;
                    }
                }
            }

            if (migrated > 0)
            {
                _logger.LogInformation(
                    "Migrated {Count} sound file(s) from the app-global sounds folder into {Directory} (originals kept)",
                    migrated, _soundsDirectory);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to migrate legacy sound files into {Directory}", _soundsDirectory);
        }
    }

    public Task<List<string>> GetSoundFilesAsync()
    {
        try
        {
            var files = Directory.GetFiles(_soundsDirectory, "*.*")
                .Where(f => IsSupportedAudioFile(f))
                .Select(Path.GetFileName)
                .Where(f => f != null)
                .Cast<string>()
                .OrderBy(f => f)
                .ToList();

            return Task.FromResult(files);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting sound files");
            return Task.FromResult(new List<string>());
        }
    }

    public async Task<string> UploadSoundAsync(string sourceFilePath, string? fileName = null)
    {
        if (!File.Exists(sourceFilePath))
        {
            throw new FileNotFoundException("Source sound file not found", sourceFilePath);
        }

        if (!IsSupportedAudioFile(sourceFilePath))
        {
            throw new NotSupportedException("Unsupported audio format. Supported: .wav, .mp3, .ogg, .m4a");
        }

        fileName ??= Path.GetFileName(sourceFilePath);
        var destPath = Path.Combine(_soundsDirectory, fileName);

        // If file exists, append number to avoid overwrite
        if (File.Exists(destPath))
        {
            var nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
            var extension = Path.GetExtension(fileName);
            var counter = 1;

            do
            {
                fileName = $"{nameWithoutExt}_{counter}{extension}";
                destPath = Path.Combine(_soundsDirectory, fileName);
                counter++;
            } while (File.Exists(destPath));
        }

        await Task.Run(() => File.Copy(sourceFilePath, destPath));
        _logger.LogInformation("✅ Uploaded sound file: {FileName}", fileName);

        return fileName;
    }

    public Task<bool> DeleteSoundAsync(string fileName)
    {
        try
        {
            var filePath = Path.Combine(_soundsDirectory, fileName);

            if (!File.Exists(filePath))
            {
                _logger.LogWarning("Sound file not found for deletion: {FileName}", fileName);
                return Task.FromResult(false);
            }

            File.Delete(filePath);
            _logger.LogInformation("🗑️ Deleted sound file: {FileName}", fileName);
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting sound file: {FileName}", fileName);
            return Task.FromResult(false);
        }
    }

    public async Task PlayEventSoundAsync(string eventType, string platform)
    {
        try
        {
            // Master toggle (Settings → Notifications)
            var eventSoundsEnabled = await _dataService.GetConfigValueAsync("Notifications.EventSoundsEnabled");
            if (bool.TryParse(eventSoundsEnabled, out var enabled) && !enabled)
            {
                _logger.LogDebug("Event sounds disabled (Settings → Notifications) - skipping playback");
                return;
            }

            // Get audio settings
            var settings = await GetAudioSettingsAsync();
            if (!settings.StreamerSoundsEnabled && !settings.StreamSoundsEnabled)
            {
                _logger.LogDebug("All sounds disabled - skipping playback");
                return;
            }

            // Get sound file mapped to this event
            var soundFile = await GetEventSoundFileAsync(eventType, platform);
            if (string.IsNullOrEmpty(soundFile))
            {
                _logger.LogDebug("No sound mapped to event {EventType} on {Platform}", eventType, platform);
                return;
            }

            // Get volume (master * event-specific)
            var volume = settings.MasterVolume / 100f;
            var eventVolume = await GetEventVolumeAsync(eventType, platform);
            volume *= eventVolume;

            await PlaySoundFileAsync(soundFile, volume);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error playing event sound for {EventType} on {Platform}", eventType, platform);
        }
    }

    public async Task PlaySoundFileAsync(string soundFile, float volume = 1.0f)
    {
        try
        {
            var filePath = Path.Combine(_soundsDirectory, soundFile);

            if (!File.Exists(filePath))
            {
                _logger.LogWarning("Sound file not found: {SoundFile}", soundFile);
                return;
            }

            var settings = await GetAudioSettingsAsync();

            // Play locally for streamer
            if (settings.StreamerSoundsEnabled)
            {
                _ = Task.Run(() => PlayLocalSoundAsync(filePath, volume));
            }

            // Send to browser source for stream
            if (settings.StreamSoundsEnabled)
            {
                // Browser-source page contract (wwwroot/audio.html):
                //   {"type":"play","sound":"<file>","volume":<0..1>}
                // Property names are camelCase by construction (anonymous type),
                // and volume is the final loudness (master × per-event), clamped to 0..1.
                var broadcastVolume = Math.Clamp(float.IsNaN(volume) ? 1f : volume, 0f, 1f);
                var payload = new
                {
                    type = "play",
                    sound = soundFile,
                    volume = broadcastVolume
                };
                var json = System.Text.Json.JsonSerializer.Serialize(payload);
                if (AudioWebSocketBroadcaster.BroadcastAsync != null)
                {
                    await AudioWebSocketBroadcaster.BroadcastAsync(json);
                }
                else
                {
                    _logger.LogWarning("No AudioWebSocketBroadcaster available to broadcast audio event");
                }
            }

            _logger.LogDebug("🔊 Played sound: {SoundFile} (Volume: {Volume}, Streamer: {Streamer}, Stream: {Stream})",
                soundFile, volume, settings.StreamerSoundsEnabled, settings.StreamSoundsEnabled);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error playing sound file: {SoundFile}", soundFile);
        }
    }

    public bool SoundFileExists(string fileName)
    {
        var filePath = Path.Combine(_soundsDirectory, fileName);
        return File.Exists(filePath);
    }

    public string GetSoundsDirectory()
    {
        return _soundsDirectory;
    }

    public async Task InitializeDefaultSoundsAsync()
    {
        _logger.LogInformation("Checking for default sound files...");

        var defaultSounds = new Dictionary<string, string>
        {
            { "follow.wav", "Default follow sound" },
            { "subscribe.wav", "Default subscribe sound" },
            { "raid.wav", "Default raid sound" },
            { "cheer.wav", "Default cheer sound" },
            { "giftsub.wav", "Default gift sub sound" }
        };

        foreach (var sound in defaultSounds)
        {
            var filePath = Path.Combine(_soundsDirectory, sound.Key);
            if (!File.Exists(filePath))
            {
                _logger.LogInformation("⚠️ Default sound missing: {FileName} - You'll need to provide custom sounds", sound.Key);
                // Note: We can't generate actual audio files programmatically
                // Users will need to provide their own or download sound packs
            }
        }

        await Task.CompletedTask;
    }

    // Helper methods

    private bool IsSupportedAudioFile(string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        return extension is ".wav" or ".mp3" or ".ogg" or ".m4a";
    }

    private async Task<AudioSettings> GetAudioSettingsAsync()
    {
        var settingsJson = await _dataService.GetConfigValueAsync("AudioSettings");
        if (string.IsNullOrEmpty(settingsJson))
        {
            return new AudioSettings
            {
                StreamerSoundsEnabled = true,
                StreamSoundsEnabled = true,
                MasterVolume = 100,
                WebSocketPort = 8080
            };
        }

        try
        {
            var settings = System.Text.Json.JsonSerializer.Deserialize<AudioSettings>(settingsJson);
            return settings ?? new AudioSettings
            {
                StreamerSoundsEnabled = true,
                StreamSoundsEnabled = true,
                MasterVolume = 100,
                WebSocketPort = 8080
            };
        }
        catch
        {
            return new AudioSettings
            {
                StreamerSoundsEnabled = true,
                StreamSoundsEnabled = true,
                MasterVolume = 100,
                WebSocketPort = 8080
            };
        }
    }

    private async Task<string?> GetEventSoundFileAsync(string eventType, string platform)
    {
        var key = $"EventSound_{platform}_{eventType}";
        var soundFile = await _dataService.GetConfigValueAsync(key);
        return soundFile;
    }

    private async Task<float> GetEventVolumeAsync(string eventType, string platform)
    {
        var key = $"EventVolume_{platform}_{eventType}";
        var volumeStr = await _dataService.GetConfigValueAsync(key);
        if (string.IsNullOrWhiteSpace(volumeStr))
        {
            return 1.0f;
        }

        // Values are persisted as invariant "F2" strings in the 0..1 range
        // ("0.00".."1.00") by SoundsViewModel. Legacy rows may hold whole-percent
        // integers (e.g. "70") — normalize those to 0..1 as well, then clamp.
        if (double.TryParse(volumeStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var volume))
        {
            if (volume > 1.0)
            {
                volume /= 100.0;
            }

            return (float)Math.Clamp(volume, 0.0, 1.0);
        }

        return 1.0f;
    }

    private Task PlayLocalSoundAsync(string filePath, float volume)
    {
        // All supported formats (including .wav) play through WPF MediaPlayer so the
        // volume setting is honored — System.Media.SoundPlayer has no volume API,
        // which made .wav event sounds ignore master/per-event volume. Each playback
        // runs on its own short-lived STA thread with its own MediaPlayer instance,
        // so simultaneous sounds still overlap.
        PlayWithMediaPlayer(filePath, volume);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Plays all supported formats (.wav/.mp3/.ogg/.m4a) via WPF <see cref="MediaPlayer"/>
    /// on a dedicated STA background thread. MediaPlayer's media clock needs a Dispatcher
    /// pump, so the thread runs a Dispatcher that is shut down on MediaEnded/MediaFailed.
    /// </summary>
    private void PlayWithMediaPlayer(string filePath, float volume)
    {
        var thread = new Thread(() =>
        {
            MediaPlayer? player = null;
            try
            {
                player = new MediaPlayer();

                void Shutdown()
                {
                    try
                    {
                        player?.Close();
                        player?.Dispatcher.InvokeShutdown();
                    }
                    catch
                    {
                        // best-effort cleanup
                    }
                }

                player.MediaEnded += (s, e) => Shutdown();
                player.MediaFailed += (s, e) =>
                {
                    _logger.LogError(e.ErrorException, "Local playback failed for {FilePath}", filePath);
                    Shutdown();
                };

                player.Open(new Uri(filePath));
                player.Volume = Math.Clamp(volume, 0.0, 1.0);
                player.Play();

                Dispatcher.Run(); // Pump until MediaEnded/MediaFailed shuts the dispatcher down
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error playing local sound: {FilePath}", filePath);
                try
                {
                    player?.Close();
                    player?.Dispatcher.InvokeShutdown();
                }
                catch
                {
                    // best-effort cleanup
                }
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
    }
}

/// <summary>
/// Audio settings stored in database.
/// </summary>
public class AudioSettings
{
    public bool StreamerSoundsEnabled { get; set; } = true;
    public bool StreamSoundsEnabled { get; set; } = true;
    public int MasterVolume { get; set; } = 100; // 0-100
    public int WebSocketPort { get; set; } = 8080;
}
