using Microsoft.Extensions.Logging;
using VyralBot.Data;
using System.Globalization;
using System.Media;
using VyralBot.Core.Services.Audio;

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
        IDataService dataService)
    {
        _logger = logger;
        _dataService = dataService;
        // Use wwwroot/sounds for browser source compatibility
        _soundsDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "VyralBot.Api", "wwwroot", "sounds");
        Directory.CreateDirectory(_soundsDirectory);
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

    private async Task PlayLocalSoundAsync(string filePath, float volume)
    {
        try
        {
            // For .wav files, use SoundPlayer (built-in Windows API)
            if (filePath.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
            {
                using var player = new SoundPlayer(filePath);
                player.PlaySync(); // Play synchronously on background thread
            }
            else
            {
                // For MP3/OGG/M4A, we'd need additional dependencies (NAudio, etc.)
                // For now, log that we'd need those for non-WAV
                _logger.LogWarning("Local playback only supports .wav files. Use browser source for {File}", Path.GetFileName(filePath));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error playing local sound: {FilePath}", filePath);
        }

        await Task.CompletedTask;
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
