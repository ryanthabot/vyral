namespace VyralBot.UI.Models;

/// <summary>
/// A single entry in a platform dashboard's Activity Feed.
/// </summary>
/// <param name="Timestamp">UTC time the activity happened (rendered in local time).</param>
/// <param name="Text">Human-readable activity description.</param>
public record ActivityItem(DateTime Timestamp, string Text);
