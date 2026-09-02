namespace VyralBot.Core.Services.Audio;

/// <summary>
/// Manages sound files and playback for event alerts.
/// </summary>
public interface ISoundService
{
    /// <summary>
    /// Gets all available sound files.
    /// </summary>
    Task<List<string>> GetSoundFilesAsync();
    
    /// <summary>
    /// Uploads a sound file to the Sounds directory.
    /// </summary>
    /// <param name="sourceFilePath">Source path of the file to upload</param>
    /// <param name="fileName">Destination file name (optional, uses source name if null)</param>
    Task<string> UploadSoundAsync(string sourceFilePath, string? fileName = null);
    
    /// <summary>
    /// Deletes a sound file.
    /// </summary>
    Task<bool> DeleteSoundAsync(string fileName);
    
    /// <summary>
    /// Plays a sound for a specific event.
    /// Respects streamer/stream sound settings.
    /// </summary>
    /// <param name="eventType">Event type (follow, subscribe, raid, etc.)</param>
    /// <param name="platform">Platform (Kick or Twitch)</param>
    Task PlayEventSoundAsync(string eventType, string platform);
    
    /// <summary>
    /// Plays a specific sound file directly (for testing).
    /// </summary>
    Task PlaySoundFileAsync(string soundFile, float volume = 1.0f);
    
    /// <summary>
    /// Checks if a sound file exists.
    /// </summary>
    bool SoundFileExists(string fileName);
    
    /// <summary>
    /// Gets the full path to the sounds directory.
    /// </summary>
    string GetSoundsDirectory();
    
    /// <summary>
    /// Initializes default sounds (copies from embedded resources if missing).
    /// </summary>
    Task InitializeDefaultSoundsAsync();
}
