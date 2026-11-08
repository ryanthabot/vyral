namespace VyralBot.Data.Entities;

/// <summary>
/// One scheduled command timer (Commands page → Timers tab): fires the command
/// named <see cref="CommandName"/> on the target platform every
/// <see cref="IntervalMinutes"/> minutes while the CommandTimerService runs.
/// </summary>
public class CommandTimer
{
    public required string Id { get; set; }

    /// <summary>Display name of the timer (free text, shown in the grid).</summary>
    public required string Name { get; set; }

    /// <summary>Trigger name of the command to fire, e.g. "!socials".</summary>
    public required string CommandName { get; set; }

    /// <summary>Minutes between fires (minimum enforced by the UI: 5).</summary>
    public int IntervalMinutes { get; set; } = 30;

    /// <summary>
    /// Minimum chat messages that must arrive on the target platform between
    /// fires before the timer may fire again (streamer.bot-style chat-activity
    /// gate). 0 = fire regardless of chat activity.
    /// </summary>
    public int MinimumMessages { get; set; } = 0;

    /// <summary>Destination platform: "Twitch", "Kick" or "Discord".</summary>
    public required string TargetPlatform { get; set; } = "Twitch";

    /// <summary>
    /// Discord bot scope (DiscordBots row id): the manager bot that sends the
    /// message. Null means the manager's Primary bot (first connected).
    /// Only meaningful when <see cref="TargetPlatform"/> is "Discord".
    /// </summary>
    public string? BotId { get; set; }

    public bool IsEnabled { get; set; } = true;

    /// <summary>Last time the timer fired (UTC). Null = never fired yet.</summary>
    public DateTime? LastRunAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
