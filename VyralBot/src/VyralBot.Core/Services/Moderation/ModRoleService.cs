using Microsoft.Extensions.Logging;
using VyralBot.Data;
using VyralBot.Data.Entities;

namespace VyralBot.Core.Services.Moderation;

/// <summary>
/// Default <see cref="IModRoleService"/>: upserts the ModRoleAssignment row via
/// <see cref="IDataService"/> and grants the backing Discord role (role name =
/// tier name) best-effort through <see cref="IModDiscordBot"/> — the Core-side
/// mirror of the per-bot Discord surface. The Discord dependency is optional:
/// null (or a Discord failure) never blocks the persisted assignment.
/// </summary>
public class ModRoleService : IModRoleService
{
    private readonly IDataService _dataService;
    private readonly IModDiscordBot? _discord;
    private readonly ILogger<ModRoleService> _logger;

    public ModRoleService(IDataService dataService, IModDiscordBot? discord, ILogger<ModRoleService> logger)
    {
        _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
        _discord = discord;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task AssignAsync(string discordUserId, string username, string tier, string assignedBy)
    {
        if (string.IsNullOrWhiteSpace(discordUserId))
        {
            throw new ArgumentException("Discord user id is required.", nameof(discordUserId));
        }

        var canonicalTier = ModTiers.NormalizeTier(tier)
            ?? throw new ArgumentException(
                $"Unknown mod tier '{tier}'. Valid tiers: {string.Join(", ", ModTiers.TierNames)}.",
                nameof(tier));

        var existing = await _dataService.GetModRoleAssignmentAsync(discordUserId).ConfigureAwait(false);
        if (existing != null)
        {
            existing.Username = username ?? "";
            existing.Tier = canonicalTier;
            existing.AssignedBy = assignedBy ?? "";
            existing.AssignedAt = DateTime.UtcNow;
            await _dataService.SaveModRoleAssignmentAsync(existing).ConfigureAwait(false);
        }
        else
        {
            await _dataService.SaveModRoleAssignmentAsync(new ModRoleAssignment
            {
                DiscordUserId = discordUserId,
                Username = username ?? "",
                Tier = canonicalTier,
                AssignedBy = assignedBy ?? ""
            }).ConfigureAwait(false);
        }

        _logger.LogInformation(
            "Mod tier '{Tier}' assigned to Discord user {DiscordUserId} ({Username}) by {AssignedBy}",
            canonicalTier, discordUserId, username, assignedBy);

        if (_discord == null)
        {
            return;
        }

        try
        {
            var granted = await _discord.AddRoleToUserAsync(
                username ?? discordUserId,
                canonicalTier,
                $"VyralBot mod tier '{canonicalTier}' assigned by {assignedBy}").ConfigureAwait(false);
            if (!granted)
            {
                _logger.LogWarning(
                    "Discord role grant for mod tier '{Tier}' failed for {Username} — assignment is still persisted",
                    canonicalTier, username);
            }
        }
        catch (Exception ex)
        {
            // Best-effort: the persisted assignment outranks the Discord grant.
            _logger.LogWarning(ex,
                "Discord role grant threw for mod tier '{Tier}' on {Username} — assignment is still persisted",
                canonicalTier, username);
        }
    }

    /// <inheritdoc />
    public async Task UnassignAsync(string discordUserId)
    {
        await _dataService.DeleteModRoleAssignmentAsync(discordUserId).ConfigureAwait(false);
        _logger.LogInformation("Mod assignment removed for Discord user {DiscordUserId}", discordUserId);
    }

    /// <inheritdoc />
    public async Task<string?> GetTierAsync(string discordUserId)
    {
        var assignment = await _dataService.GetModRoleAssignmentAsync(discordUserId).ConfigureAwait(false);
        return assignment?.Tier;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ModRoleAssignment>> GetAllAsync()
    {
        var assignments = await _dataService.GetModRoleAssignmentsAsync().ConfigureAwait(false);
        return assignments.ToList();
    }

    /// <inheritdoc />
    public async Task<string?> GetTierFromRolesAsync(string botId, IReadOnlyList<string> discordRoleIds)
    {
        var mappings = await _dataService.GetRoleTierAssignmentsAsync(botId).ConfigureAwait(false);

        string? best = null;
        var bestRank = -1;
        foreach (var mapping in mappings)
        {
            if (!discordRoleIds.Contains(mapping.DiscordRoleId))
            {
                continue;
            }

            var rank = ModTiers.Rank(mapping.Tier);
            if (rank > bestRank)
            {
                bestRank = rank;
                best = ModTiers.NormalizeTier(mapping.Tier);
            }
        }

        return best;
    }
}
