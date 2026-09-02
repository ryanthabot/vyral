namespace VyralBot.Data.Entities;

/// <summary>
/// Settings for individual chat event types (follows, subs, raids, etc.)
/// Controls visibility and auto-reply behavior per platform and event type.
/// </summary>
public class ChatEventSetting
{
    public Guid Id { get; set; } = Guid.NewGuid();
    
    /// <summary>
    /// Platform name: "Kick" or "Twitch"
    /// </summary>
    public required string Platform { get; set; }
    
    /// <summary>
    /// Event type: "Follow", "Subscribe", "GiftSub", "Raid", "Host", "KicksGifted", "RewardRedeemed", "Cheer", etc.
    /// </summary>
    public required string EventType { get; set; }
    
    /// <summary>
    /// Show event notification in chat window
    /// </summary>
    public bool ShowInChat { get; set; } = true;
    
    /// <summary>
    /// Enable automatic reply to chat when event occurs
    /// </summary>
    public bool AutoReplyEnabled { get; set; } = false;
    
    /// <summary>
    /// Template message for auto-reply. Supports variables like {username}, {count}, {tier}, etc.
    /// </summary>
    public string? AutoReplyMessage { get; set; }
    
    /// <summary>
    /// Enable sound notification for this event
    /// </summary>
    public bool SoundEnabled { get; set; } = true;
    
    /// <summary>
    /// Sound file name to play (e.g., "follow.wav")
    /// </summary>
    public string? SoundFile { get; set; }
    
    /// <summary>
    /// Volume for this event's sound (0-100)
    /// </summary>
    public int SoundVolume { get; set; } = 100;
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
