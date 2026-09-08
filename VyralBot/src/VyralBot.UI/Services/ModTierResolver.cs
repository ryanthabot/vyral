using VyralBot.Core.Services.Moderation;

namespace VyralBot.UI.Services;

/// <summary>
/// Effective tier = max(DB assignment, Discord-role mapping). Role lookups are
/// best-effort: no bot wired, a non-snowflake sender id, an unknown guild or
/// any Discord failure all degrade to the stored assignment.
/// </summary>
public sealed class ModTierResolver : IModTierResolver
{
    private readonly IModRoleService _modRoles;
    private readonly IModDiscordBot? _bot;

    public ModTierResolver(IModRoleService modRoles, IModDiscordBot? bot)
    {
        _modRoles = modRoles ?? throw new ArgumentNullException(nameof(modRoles));
        _bot = bot;
    }

    /// <inheritdoc />
    public async Task<string?> ResolveTierAsync(string botId, string? guildId, string userId, string username)
    {
        var assigned = await _modRoles.GetTierAsync(userId).ConfigureAwait(false);
        if (_bot == null || string.IsNullOrWhiteSpace(guildId) || !ulong.TryParse(userId, out _))
        {
            return assigned;
        }

        try
        {
            var roleIds = await _bot.GetMemberRoleIdsAsync(guildId, userId).ConfigureAwait(false);
            if (roleIds.Count == 0)
            {
                return assigned;
            }

            var viaRole = await _modRoles.GetTierFromRolesAsync(botId, roleIds).ConfigureAwait(false);
            return ModTiers.Rank(viaRole) > ModTiers.Rank(assigned) ? viaRole : assigned;
        }
        catch
        {
            // Role lookups are best-effort — the stored assignment always answers.
            return assigned;
        }
    }
}
