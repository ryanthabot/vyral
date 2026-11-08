namespace VyralBot.Core.Services.Audio;

/// <summary>
/// Text-to-speech synthesis for chat commands (!tts) and event alerts, using
/// the in-box Windows SAPI voices via System.Speech. All speech is serialized
/// through a single background worker so overlapping requests never spawn
/// unbounded threads.
/// </summary>
public interface ITtsService
{
    /// <summary>
    /// Handles the <c>!tts &lt;text&gt;</c> chat command: validates the message
    /// against the persisted TTS settings (enabled, viewer permission, max
    /// length, per-user cooldown), speaks it and returns the chat reply.
    /// Returns null when <paramref name="message"/> is not a !tts command or
    /// the message is silently ignored (TTS disabled, viewers not allowed).
    /// Moderators bypass the per-user cooldown.
    /// </summary>
    Task<string?> ProcessCommandAsync(string username, string platform, bool isModerator, string message);

    /// <summary>
    /// Speaks text aloud. Never throws; no-op when TTS is disabled or
    /// do-not-disturb (Notifications.DoNotDisturb) is active. Arguments left at
    /// their defaults mean "use the persisted Tts.* settings"; explicit values
    /// override them for this utterance.
    /// </summary>
    /// <param name="text">Text to speak (trimmed, clamped to Tts.MaxLength).</param>
    /// <param name="voice">Installed voice name, or null for the configured voice.</param>
    /// <param name="ratePercent">Speaking rate offset, -50..50 (0 = configured rate).</param>
    /// <param name="volumePercent">Volume, 0..100 (100 = configured volume).</param>
    Task SpeakAsync(string text, string? voice = null, int ratePercent = 0, int volumePercent = 100);

    /// <summary>
    /// Installed voice names (cached after the first enumeration; empty when
    /// no SAPI voices are installed).
    /// </summary>
    IReadOnlyList<string> GetVoices();

    /// <summary>
    /// Loads the persisted Tts.* settings; returns defaults when nothing valid
    /// is stored.
    /// </summary>
    Task<TtsSettings> GetSettingsAsync();

    /// <summary>
    /// Persists the given settings under the Tts.* Configs keys.
    /// </summary>
    Task SaveSettingsAsync(TtsSettings settings);

    /// <summary>
    /// Cancels the utterance currently being spoken and drops everything still
    /// queued.
    /// </summary>
    void StopSpeaking();
}

/// <summary>
/// Text-to-speech settings persisted as individual Configs rows under the
/// Tts.* keys in the per-profile database (plain invariant strings, matching
/// the Notifications.* key convention).
/// </summary>
public class TtsSettings
{
    /// <summary>Master toggle. Off by default — a voice speaking over the
    /// stream is intrusive, so TTS is opt-in.</summary>
    public bool Enabled { get; set; } = false;

    /// <summary>Voice name, or null for the OS default (first installed voice).</summary>
    public string? Voice { get; set; }

    /// <summary>Speaking rate offset, clamped to -50..50.</summary>
    public int RatePercent { get; set; } = 0;

    /// <summary>Volume, clamped to 0..100.</summary>
    public int VolumePercent { get; set; } = 100;

    /// <summary>Whether viewers (non-moderators) may use !tts.</summary>
    public bool AllowViewers { get; set; } = true;

    /// <summary>Maximum !tts message length in characters, clamped to 10..1000.</summary>
    public int MaxLength { get; set; } = 300;
}
