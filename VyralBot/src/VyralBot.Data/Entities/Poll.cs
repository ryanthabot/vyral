namespace VyralBot.Data.Entities;

/// <summary>
/// Represents a poll with multiple choice options for viewer voting.
/// Supports cross-platform voting from Kick, Twitch, and Discord.
/// </summary>
public class Poll
{
    public Guid Id { get; set; } = Guid.NewGuid();
    
    /// <summary>
    /// The poll question (e.g., "What game should I play next?")
    /// </summary>
    public required string Question { get; set; }
    
    /// <summary>
    /// JSON array of poll options (e.g., ["Option A", "Option B", "Option C"])
    /// </summary>
    public required string Options { get; set; }
    
    /// <summary>
    /// Current status of the poll
    /// </summary>
    public PollStatus Status { get; set; } = PollStatus.Pending;
    
    /// <summary>
    /// When the poll was created
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// When the poll started accepting votes
    /// </summary>
    public DateTime? StartedAt { get; set; }
    
    /// <summary>
    /// When the poll ended
    /// </summary>
    public DateTime? EndedAt { get; set; }
    
    /// <summary>
    /// Optional: Automatic end time for the poll
    /// </summary>
    public DateTime? ScheduledEndTime { get; set; }
    
    /// <summary>
    /// Username of the user who created the poll
    /// </summary>
    public required string CreatorUsername { get; set; }
    
    /// <summary>
    /// Total number of votes cast
    /// </summary>
    public int TotalVotes { get; set; }
    
    /// <summary>
    /// Whether users can change their vote
    /// </summary>
    public bool AllowMultipleVotes { get; set; } = false;
    
    /// <summary>
    /// Platforms allowed to vote (comma-separated: "Kick,Twitch,Discord" or "All")
    /// </summary>
    public string AllowedPlatforms { get; set; } = "All";
    
    /// <summary>
    /// Whether followers-only mode is enabled
    /// </summary>
    public bool FollowersOnly { get; set; } = false;
    
    /// <summary>
    /// Whether subscribers-only mode is enabled
    /// </summary>
    public bool SubscribersOnly { get; set; } = false;
    
    /// <summary>
    /// Minimum watchtime required to vote (in minutes, 0 = no requirement)
    /// </summary>
    public int MinWatchtimeMinutes { get; set; } = 0;
    
    /// <summary>
    /// JSON object storing vote counts per option index (after poll ends)
    /// </summary>
    public string? VoteResults { get; set; }
    
    /// <summary>
    /// JSON metadata for custom settings
    /// </summary>
    public string? Metadata { get; set; }
    
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Status of a poll lifecycle
/// </summary>
public enum PollStatus
{
    /// <summary>
    /// Poll created but not yet started
    /// </summary>
    Pending = 0,
    
    /// <summary>
    /// Poll is active and accepting votes
    /// </summary>
    Active = 1,
    
    /// <summary>
    /// Poll has ended
    /// </summary>
    Completed = 2,
    
    /// <summary>
    /// Poll was cancelled before completion
    /// </summary>
    Cancelled = 3
}
