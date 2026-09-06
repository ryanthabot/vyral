using VyralBot.Data.Entities;

namespace VyralBot.Core.Services.Moderation;

/// <summary>
/// Persists one mod tier per Discord user (<see cref="ModRoleAssignment"/>,
/// unique per DiscordUserId) and best-effort grants the backing Discord role.
/// </summary>
public interface IModRoleService
{
    /// <summary>
    /// Assigns <paramref name="tier"/> to <paramref name="discordUserId"/>,
    /// upserting the existing row when one is present. The tier is normalized
    /// to its canonical spelling ("TrialMod"/"Mod"/"SeniorMod"/"Admin") and an
    /// unknown tier throws <see cref="ArgumentException"/>. The backing Discord
    /// role grant (role name = tier name) is best-effort: a Discord failure is
    /// logged and swallowed so the persisted assignment always lands.
    /// </summary>
    Task AssignAsync(string discordUserId, string username, string tier, string assignedBy);

    /// <summary>Removes the mod assignment for a Discord user (if any).</summary>
    Task UnassignAsync(string discordUserId);

    /// <summary>
    /// The stored (canonical) tier of a Discord user, or null when the user
    /// holds no mod assignment.
    /// </summary>
    Task<string?> GetTierAsync(string discordUserId);

    /// <summary>Every current mod assignment.</summary>
    Task<IReadOnlyList<ModRoleAssignment>> GetAllAsync();
}
