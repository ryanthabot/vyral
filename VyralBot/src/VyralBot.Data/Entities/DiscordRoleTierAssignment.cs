namespace VyralBot.Data.Entities;

/// <summary>Maps a Discord role to a mod tier ("TrialMod"|"Mod"|"SeniorMod"|"Admin"). Members holding the role gain the tier.</summary>
public class DiscordRoleTierAssignment
{
    public int Id { get; set; }
    public string BotId { get; set; } = "";
    public string GuildId { get; set; } = "";
    public string DiscordRoleId { get; set; } = "";
    public string RoleName { get; set; } = "";
    public string Tier { get; set; } = "";
    public DateTime AssignedAtUtc { get; set; } = DateTime.UtcNow;
}
