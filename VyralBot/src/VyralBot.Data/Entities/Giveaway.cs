namespace VyralBot.Data.Entities;

/// <summary>
/// Represents a giveaway event with entries and winner selection.
/// Supports cross-platform participation from Kick, Twitch, and Discord.
/// </summary>
public class Giveaway
{
    public Guid Id { get; set; } = Guid.NewGuid();
    
    /// <summary>
    /// Display name for the giveaway (e.g., "Stream Anniversary Giveaway")
    /// </summary>
    public required string Name { get; set; }
    
    /// <summary>
    /// Detailed description of what's being given away
    /// </summary>
    public string? Description { get; set; }
    
    /// <summary>
    /// Keyword users must type to enter (e.g., "!enter", "!giveaway")
    /// </summary>
    public required string EntryKeyword { get; set; }
    
    /// <summary>
    /// Current status of the giveaway
    /// </summary>
    public GiveawayStatus Status { get; set; } = GiveawayStatus.Pending;
    
    /// <summary>
    /// When the giveaway was created
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// When the giveaway started accepting entries
    /// </summary>
    public DateTime? StartedAt { get; set; }
    
    /// <summary>
    /// When the giveaway ended and winner was drawn
    /// </summary>
    public DateTime? EndedAt { get; set; }
    
    /// <summary>
    /// Optional: Automatic end time for the giveaway
    /// </summary>
    public DateTime? ScheduledEndTime { get; set; }
    
    /// <summary>
    /// Maximum number of entries allowed (null = unlimited)
    /// </summary>
    public int? MaxEntries { get; set; }
    
    /// <summary>
    /// Number of winners to draw (default 1)
    /// </summary>
    public int WinnerCount { get; set; } = 1;
    
    /// <summary>
    /// Whether subscribers get multiple entries
    /// </summary>
    public bool SubscriberBonusEnabled { get; set; } = false;
    
    /// <summary>
    /// Number of bonus entries for subscribers
    /// </summary>
    public int SubscriberBonusEntries { get; set; } = 1;
    
    /// <summary>
    /// Minimum watchtime required to enter (in minutes, 0 = no requirement)
    /// </summary>
    public int MinWatchtimeMinutes { get; set; } = 0;
    
    /// <summary>
    /// Platforms allowed to participate (comma-separated: "Kick,Twitch,Discord" or "All")
    /// </summary>
    public string AllowedPlatforms { get; set; } = "All";
    
    /// <summary>
    /// Whether linked-viewer odds boost is enabled for this giveaway
    /// </summary>
    public bool OddsBoostEnabled { get; set; } = false;

    /// <summary>
    /// Whether followers-only mode is enabled
    /// </summary>
    public bool FollowersOnly { get; set; } = false;
    
    /// <summary>
    /// Whether subscribers-only mode is enabled
    /// </summary>
    public bool SubscribersOnly { get; set; } = false;
    
    /// <summary>
    /// Comma-separated list of winner usernames (after drawing)
    /// </summary>
    public string? Winners { get; set; }
    
    /// <summary>
    /// Total number of entries received
    /// </summary>
    public int TotalEntries { get; set; }
    
    /// <summary>
    /// Total number of unique participants
    /// </summary>
    public int UniqueParticipants { get; set; }
    
    /// <summary>
    /// JSON metadata for custom settings
    /// </summary>
    public string? Metadata { get; set; }
    
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Status of a giveaway lifecycle
/// </summary>
public enum GiveawayStatus
{
    /// <summary>
    /// Giveaway created but not yet started
    /// </summary>
    Pending = 0,
    
    /// <summary>
    /// Giveaway is active and accepting entries
    /// </summary>
    Active = 1,
    
    /// <summary>
    /// Giveaway has ended and winner(s) drawn
    /// </summary>
    Completed = 2,
    
    /// <summary>
    /// Giveaway was cancelled before completion
    /// </summary>
    Cancelled = 3
}
