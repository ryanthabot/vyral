namespace VyralBot.Core.Services.Audio;

/// <summary>
/// WebSocket server for sending audio events to browser sources.
/// </summary>
public interface IAudioWebSocketServer
{
    /// <summary>
    /// Indicates whether the server is running.
    /// </summary>
    bool IsRunning { get; }
    
    /// <summary>
    /// Number of connected browser source clients.
    /// </summary>
    int ConnectedClients { get; }
    
    /// <summary>
    /// Starts the WebSocket server on the specified port.
    /// </summary>
    Task StartAsync(int port = 8080);
    
    /// <summary>
    /// Stops the WebSocket server.
    /// </summary>
    Task StopAsync();
    
    /// <summary>
    /// Sends an audio event to all connected browser sources.
    /// </summary>
    Task SendAudioEventAsync(AudioEvent audioEvent);
}

/// <summary>
/// Represents an audio event sent to browser sources.
/// </summary>
public class AudioEvent
{
    /// <summary>
    /// Event type (follow, subscribe, raid, etc.)
    /// </summary>
    public required string EventType { get; set; }
    
    /// <summary>
    /// Platform (Kick or Twitch)
    /// </summary>
    public required string Platform { get; set; }
    
    /// <summary>
    /// Sound file name (e.g., "follow.wav")
    /// </summary>
    public required string SoundFile { get; set; }
    
    /// <summary>
    /// Volume (0.0 to 1.0)
    /// </summary>
    public float Volume { get; set; } = 1.0f;
    
    /// <summary>
    /// Additional event data for visual alerts (optional)
    /// </summary>
    public Dictionary<string, string>? Data { get; set; }
}
