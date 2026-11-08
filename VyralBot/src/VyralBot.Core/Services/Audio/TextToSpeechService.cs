using System.Globalization;
using System.Speech.Synthesis;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using VyralBot.Data;

namespace VyralBot.Core.Services.Audio;

/// <summary>
/// Text-to-speech synthesis using the in-box Windows SAPI voices
/// (System.Speech). SAPI's <see cref="SpeechSynthesizer.Speak(string)"/> is a
/// BLOCKING call, so all speech flows through a single-consumer bounded
/// channel (max 10 queued, DropOldest): overlapping <see cref="SpeakAsync"/>
/// callers only enqueue, at most one utterance plays at a time, and no call
/// ever spawns an extra thread. The synthesizer is created lazily on the
/// worker loop and confined to it; cancellation drains the queue and calls
/// <see cref="SpeechSynthesizer.Stop"/> on the in-flight prompt.
/// </summary>
public class TextToSpeechService : ITtsService, IDisposable
{
    private const string EnabledKey = "Tts.Enabled";
    private const string VoiceKey = "Tts.Voice";
    private const string RateKey = "Tts.RatePercent";
    private const string VolumeKey = "Tts.VolumePercent";
    private const string AllowViewersKey = "Tts.AllowViewers";
    private const string MaxLengthKey = "Tts.MaxLength";
    private const string SettingsCategory = "Tts";

    // Matches the do-not-disturb key owned by Settings → Notifications and
    // consulted by SoundService / AlertService.
    private const string DoNotDisturbKey = "Notifications.DoNotDisturb";

    private const int MaxQueueLength = 10;
    private const int MinRatePercent = -50;
    private const int MaxRatePercent = 50;
    private const int MaxVolumePercent = 100;
    private const int MinMaxLength = 10;
    private const int MaxMaxLength = 1000;
    private const int RateLimitCooldownSeconds = 30;

    private readonly ILogger<TextToSpeechService> _logger;
    private readonly IDataService _dataService;

    /// <summary>Single-consumer speech queue: bounded, drop-oldest backpressure.</summary>
    private readonly Channel<TtsRequest> _speakQueue =
        Channel.CreateBounded<TtsRequest>(new BoundedChannelOptions(MaxQueueLength)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });

    private readonly CancellationTokenSource _shutdownCts = new();
    private readonly Task _workerTask;

    /// <summary>Owned by the worker loop; volatile so StopSpeaking can cancel cross-thread.</summary>
    private volatile SpeechSynthesizer? _synthesizer;

    /// <summary>Cached installed voice names (immutable snapshot, swapped on first enumeration).</summary>
    private volatile IReadOnlyList<string> _voices = Array.Empty<string>();

    /// <summary>Set when SAPI is unavailable (no voices / COM failure) so we stop retrying.</summary>
    private volatile bool _engineUnavailable;

    /// <summary>Per-user !tts cooldown (UTC), case-insensitive; mods bypass.</summary>
    private readonly Dictionary<string, DateTime> _lastTtsUtc = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _rateLimitLock = new();

    private bool _disposed;

    /// <summary>A single queued utterance with fully resolved voice/rate/volume.</summary>
    private sealed record TtsRequest(string Text, string? Voice, int RatePercent, int VolumePercent);

    public TextToSpeechService(ILogger<TextToSpeechService> logger, IDataService dataService)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));

        _workerTask = Task.Run(() => WorkerLoopAsync());
    }

    public async Task<string?> ProcessCommandAsync(string username, string platform, bool isModerator, string message)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(message))
            {
                return null;
            }

            var trimmed = message.Trim();
            if (!trimmed.StartsWith("!tts", StringComparison.OrdinalIgnoreCase))
            {
                return null; // not a !tts command
            }

            // "!ttsxyz" is a different command; only "!tts" or "!tts <text>" is ours.
            var argument = trimmed.Length == 4 ? string.Empty : trimmed[4..];
            if (argument.Length > 0 && argument[0] != ' ')
            {
                return null;
            }

            var text = argument.Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                return "Usage: !tts <message>";
            }

            var settings = await GetSettingsAsync();
            if (!settings.Enabled)
            {
                return null; // silently ignored, like other disabled commands
            }

            if (!settings.AllowViewers && !isModerator)
            {
                return null;
            }

            // DND mutes every intrusive notification surface (same key as
            // SoundService/AlertService) — TTS included.
            if (await IsDoNotDisturbAsync())
            {
                return null;
            }

            if (!isModerator && !TryAcquireCooldown(username, out var remainingSeconds))
            {
                return $"⏳ {username}, you can use !tts again in {remainingSeconds}s.";
            }

            if (text.Length > settings.MaxLength)
            {
                text = text[..settings.MaxLength].TrimEnd();
            }

            // Uses the configured voice/rate/volume; no-ops internally when disabled/DND.
            await SpeakAsync(text);

            _logger.LogInformation("TTS command accepted from {Username} on {Platform} ({Length} chars)",
                username, platform, text.Length);

            return $"🔊 {username} says: {text}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing !tts command from {Username} on {Platform}", username, platform);
            return null;
        }
    }

    public async Task SpeakAsync(string text, string? voice = null, int ratePercent = 0, int volumePercent = 100)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            var settings = await GetSettingsAsync();
            if (!settings.Enabled)
            {
                return;
            }

            if (await IsDoNotDisturbAsync())
            {
                return;
            }

            // Default arguments mean "use the configured setting"; explicit
            // values override for this utterance only.
            var effectiveVoice = voice ?? settings.Voice;
            var effectiveRate = ratePercent == 0 ? settings.RatePercent : ratePercent;
            var effectiveVolume = volumePercent == MaxVolumePercent ? settings.VolumePercent : volumePercent;

            var effectiveText = text.Trim();
            if (effectiveText.Length > settings.MaxLength)
            {
                effectiveText = effectiveText[..settings.MaxLength].TrimEnd();
            }

            if (effectiveText.Length == 0)
            {
                return;
            }

            var request = new TtsRequest(
                effectiveText,
                effectiveVoice,
                Math.Clamp(effectiveRate, MinRatePercent, MaxRatePercent),
                Math.Clamp(effectiveVolume, 0, MaxVolumePercent));

            // DropOldest keeps the newest request; surface the drop at info so
            // users understand why an utterance never played.
            if (_speakQueue.Reader.Count >= MaxQueueLength)
            {
                _logger.LogInformation("TTS queue full ({Capacity}) - dropping the oldest utterance", MaxQueueLength);
            }

            if (!_speakQueue.Writer.TryWrite(request))
            {
                return; // queue completed during shutdown
            }

            _logger.LogInformation("TTS speech queued ({Length} chars, voice: {Voice}, queue: {Queued})",
                effectiveText.Length, effectiveVoice ?? "default", _speakQueue.Reader.Count);
        }
        catch (Exception ex)
        {
            // Contract: never throws.
            _logger.LogError(ex, "Error queueing TTS speech");
        }
    }

    public IReadOnlyList<string> GetVoices()
    {
        var cached = _voices;
        if (cached.Count > 0)
        {
            return cached;
        }

        try
        {
            using var explorer = new SpeechSynthesizer();
            var voices = explorer.GetInstalledVoices()
                .Where(v => v?.VoiceInfo?.Name is not null && v.Enabled)
                .Select(v => v.VoiceInfo.Name)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (voices.Count > 0)
            {
                _voices = voices; // immutable snapshot swap
            }

            return voices;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unable to enumerate installed TTS voices");
            return Array.Empty<string>();
        }
    }

    public async Task<TtsSettings> GetSettingsAsync()
    {
        var settings = new TtsSettings();
        try
        {
            if (bool.TryParse(await _dataService.GetConfigValueAsync(EnabledKey), out var enabled))
            {
                settings.Enabled = enabled;
            }

            var voice = await _dataService.GetConfigValueAsync(VoiceKey);
            if (!string.IsNullOrWhiteSpace(voice))
            {
                settings.Voice = voice;
            }

            if (TryParseInt(await _dataService.GetConfigValueAsync(RateKey), out var rate))
            {
                settings.RatePercent = Math.Clamp(rate, MinRatePercent, MaxRatePercent);
            }

            if (TryParseInt(await _dataService.GetConfigValueAsync(VolumeKey), out var volume))
            {
                settings.VolumePercent = Math.Clamp(volume, 0, MaxVolumePercent);
            }

            if (bool.TryParse(await _dataService.GetConfigValueAsync(AllowViewersKey), out var allowViewers))
            {
                settings.AllowViewers = allowViewers;
            }

            if (TryParseInt(await _dataService.GetConfigValueAsync(MaxLengthKey), out var maxLength))
            {
                settings.MaxLength = Math.Clamp(maxLength, MinMaxLength, MaxMaxLength);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading TTS settings - using defaults");
        }

        return settings;
    }

    public async Task SaveSettingsAsync(TtsSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        settings.RatePercent = Math.Clamp(settings.RatePercent, MinRatePercent, MaxRatePercent);
        settings.VolumePercent = Math.Clamp(settings.VolumePercent, 0, MaxVolumePercent);
        settings.MaxLength = Math.Clamp(settings.MaxLength, MinMaxLength, MaxMaxLength);

        await _dataService.SetConfigValueAsync(EnabledKey, settings.Enabled.ToString(), SettingsCategory);
        await _dataService.SetConfigValueAsync(VoiceKey, settings.Voice ?? string.Empty, SettingsCategory);
        await _dataService.SetConfigValueAsync(RateKey, settings.RatePercent.ToString(CultureInfo.InvariantCulture), SettingsCategory);
        await _dataService.SetConfigValueAsync(VolumeKey, settings.VolumePercent.ToString(CultureInfo.InvariantCulture), SettingsCategory);
        await _dataService.SetConfigValueAsync(AllowViewersKey, settings.AllowViewers.ToString(), SettingsCategory);
        await _dataService.SetConfigValueAsync(MaxLengthKey, settings.MaxLength.ToString(CultureInfo.InvariantCulture), SettingsCategory);

        _logger.LogInformation(
            "TTS settings saved - Enabled: {Enabled}, Voice: {Voice}, Rate: {Rate}%, Volume: {Volume}%, AllowViewers: {AllowViewers}, MaxLength: {MaxLength}",
            settings.Enabled, settings.Voice ?? "default", settings.RatePercent, settings.VolumePercent,
            settings.AllowViewers, settings.MaxLength);
    }

    public void StopSpeaking()
    {
        try
        {
            var dropped = 0;
            while (_speakQueue.Reader.TryRead(out _))
            {
                dropped++;
            }

            // Cancels the prompt the worker is currently speaking.
            _synthesizer?.SpeakAsyncCancelAll();

            if (dropped > 0)
            {
                _logger.LogInformation("TTS stopped - dropped {Count} queued utterance(s)", dropped);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error stopping TTS playback");
        }
    }

    /// <summary>
    /// Single-consumer loop: dequeues one request at a time and blocks inside
    /// SAPI <see cref="SpeechSynthesizer.Speak(string)"/>. Because the loop is
    /// serial, only one worker is ever inside SAPI, regardless of how many
    /// concurrent <see cref="SpeakAsync"/> calls arrive.
    /// </summary>
    private async Task WorkerLoopAsync()
    {
        try
        {
            await foreach (var request in _speakQueue.Reader.ReadAllAsync(_shutdownCts.Token))
            {
                try
                {
                    SpeakCore(request);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error speaking TTS request ({Length} chars)", request.Text.Length);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Shutdown - fall through to dispose the engine.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TTS worker loop terminated unexpectedly");
        }
        finally
        {
            try
            {
                _synthesizer?.Dispose();
            }
            catch
            {
                // best-effort cleanup during shutdown
            }

            _synthesizer = null;
        }
    }

    /// <summary>Speaks one request on the worker loop; the synthesizer is lazily created here.</summary>
    private void SpeakCore(TtsRequest request)
    {
        if (_engineUnavailable)
        {
            return;
        }

        var synthesizer = _synthesizer;
        if (synthesizer == null)
        {
            try
            {
                synthesizer = new SpeechSynthesizer();
                synthesizer.SetOutputToDefaultAudioDevice();
                _synthesizer = synthesizer;
            }
            catch (Exception ex)
            {
                _engineUnavailable = true;
                _logger.LogWarning(ex, "SAPI speech engine unavailable - TTS disabled until restart");
                return;
            }
        }

        if (!string.IsNullOrEmpty(request.Voice))
        {
            try
            {
                synthesizer.SelectVoice(request.Voice);
            }
            catch (Exception ex)
            {
                // Unknown/uninstalled voice name: keep the OS default voice.
                _logger.LogWarning(ex, "TTS voice '{Voice}' not available - using the default voice", request.Voice);
            }
        }

        // SAPI rate is -10..10; the -50..50 percent range maps by dividing by 5.
        synthesizer.Rate = Math.Clamp(request.RatePercent / 5, -10, 10);
        synthesizer.Volume = Math.Clamp(request.VolumePercent, 0, 100);

        synthesizer.Speak(request.Text); // blocking - fine on the dedicated consumer loop

        _logger.LogInformation("TTS spoke {Length} chars (voice: {Voice})",
            request.Text.Length, request.Voice ?? "default");
    }

    /// <summary>
    /// Records a !tts use for <paramref name="username"/> and reports whether
    /// the per-user cooldown (30s) allows it. Moderators never call this.
    /// </summary>
    private bool TryAcquireCooldown(string username, out int remainingSeconds)
    {
        lock (_rateLimitLock)
        {
            var nowUtc = DateTime.UtcNow;

            // Keep the map bounded: prune expired entries once it grows large.
            if (_lastTtsUtc.Count > 256)
            {
                var expired = _lastTtsUtc
                    .Where(kv => nowUtc - kv.Value >= TimeSpan.FromSeconds(RateLimitCooldownSeconds))
                    .Select(kv => kv.Key)
                    .ToList();
                foreach (var key in expired)
                {
                    _lastTtsUtc.Remove(key);
                }
            }

            if (_lastTtsUtc.TryGetValue(username, out var lastUse) &&
                nowUtc - lastUse < TimeSpan.FromSeconds(RateLimitCooldownSeconds))
            {
                remainingSeconds = (int)Math.Ceiling(
                    (TimeSpan.FromSeconds(RateLimitCooldownSeconds) - (nowUtc - lastUse)).TotalSeconds);
                return false;
            }

            _lastTtsUtc[username] = nowUtc;
            remainingSeconds = 0;
            return true;
        }
    }

    private async Task<bool> IsDoNotDisturbAsync()
    {
        var value = await _dataService.GetConfigValueAsync(DoNotDisturbKey);
        return bool.TryParse(value, out var dnd) && dnd;
    }

    private static bool TryParseInt(string? value, out int result) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            _synthesizer?.SpeakAsyncCancelAll(); // cut off any in-flight utterance at shutdown
        }
        catch
        {
            // best-effort
        }

        _speakQueue.Writer.TryComplete();
        _shutdownCts.Cancel();
        // The worker loop disposes the synthesizer in its finally block. The
        // worker task itself is intentionally NOT awaited here (no blocking on
        // shutdown); disposing it is only safe once completed.
        if (_workerTask.IsCompleted)
        {
            try
            {
                _workerTask.Dispose();
            }
            catch
            {
                // best-effort
            }
        }
    }
}
