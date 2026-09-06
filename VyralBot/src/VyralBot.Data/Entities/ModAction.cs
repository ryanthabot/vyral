namespace VyralBot.Data.Entities;

/// <summary>
/// Audit log entry for a moderation action performed by a mod
/// (e.g. "timeout", "ban", "delete", "warn"). One row per action.
/// </summary>
public class ModAction
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>Display name of the mod who performed the action.</summary>
    public string ModUsername { get; set; } = "";

    /// <summary>Platform/Discord user ID of the mod who performed the action.</summary>
    public string ModUserId { get; set; } = "";

    /// <summary>Display name of the viewer the action targeted.</summary>
    public string TargetUsername { get; set; } = "";

    /// <summary>Platform/Discord user ID of the viewer the action targeted.</summary>
    public string TargetUserId { get; set; } = "";

    /// <summary>Action kind: "Timeout", "Ban", "Unban", "Delete", "Warn", ...</summary>
    public string Action { get; set; } = "";

    /// <summary>Optional free-form details (reason, duration, source message).</summary>
    public string? Details { get; set; }

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
