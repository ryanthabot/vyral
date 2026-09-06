namespace VyralBot.Data.Entities;

/// <summary>
/// One mod-role assignment per Discord user (UNIQUE(DiscordUserId)):
/// which mod tier the user holds, who assigned it and the backing Discord role.
/// </summary>
public class ModRoleAssignment
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>Discord user snowflake ID of the mod.</summary>
    public string DiscordUserId { get; set; } = "";

    /// <summary>Discord username of the mod (display/cache snapshot).</summary>
    public string Username { get; set; } = "";

    /// <summary>Mod tier: "TrialMod" | "Mod" | "SeniorMod" | "Admin".</summary>
    public string Tier { get; set; } = "TrialMod";

    /// <summary>Username/ID of whoever granted the assignment.</summary>
    public string AssignedBy { get; set; } = "";

    public DateTime AssignedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Discord role snowflake ID backing this assignment (resolved at assign time).</summary>
    public string? DiscordRoleId { get; set; }
}
