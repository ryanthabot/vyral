namespace VyralBot.Data.Entities;

/// <summary>
/// Per-viewer opt-in preferences (one row per unified viewer identity).
/// </summary>
public class ViewerPreferences
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>Unified viewer identity.</summary>
    public string ViewerId { get; set; } = "";

    /// <summary>Whether the viewer opted in to the multi-platform watch bonus.</summary>
    public bool MultiPlatformBonusOptIn { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
