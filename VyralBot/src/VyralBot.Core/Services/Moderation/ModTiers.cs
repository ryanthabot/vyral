namespace VyralBot.Core.Services.Moderation;

/// <summary>
/// W5B mod tier ladder: the static, single source of truth for which commands
/// each mod tier may run.
///
///   TrialMod  (rank 0): kick, timeout, purge
///   Mod       (rank 1): + unban, mute, addcurrency, removecurrency
///   SeniorMod (rank 2): + ban, startgiveaway, cancelgiveaway, draw, startpoll, endpoll
///   Admin     (rank 3): + setbalance, assignmod (the ceiling — Admin only)
///
/// Every tier inherits every lower tier's commands, so each tier array below is
/// CUMULATIVE. Command names are matched case-insensitively and lowercased;
/// tier names are the canonical strings stored on
/// <see cref="VyralBot.Data.Entities.ModRoleAssignment.Tier"/>
/// ("TrialMod" | "Mod" | "SeniorMod" | "Admin"), compared case-insensitively.
/// </summary>
public static class ModTiers
{
    /// <summary>Canonical tier names, lowest rank first.</summary>
    public static readonly string[] TierNames = { "TrialMod", "Mod", "SeniorMod", "Admin" };

    /// <summary>Commands a TrialMod may run.</summary>
    public static readonly string[] TrialMod = { "kick", "timeout", "purge", "slowmode", "unmute" };

    /// <summary>Commands a Mod may run (includes every TrialMod command).</summary>
    public static readonly string[] Mod =
        TrialMod.Concat(new[] { "unban", "mute", "addcurrency", "removecurrency" }).ToArray();

    /// <summary>Commands a SeniorMod may run (includes every Mod command).</summary>
    public static readonly string[] SeniorMod =
        Mod.Concat(new[] { "ban", "startgiveaway", "cancelgiveaway", "draw", "startpoll", "endpoll" }).ToArray();

    /// <summary>Commands an Admin may run — the full set (Admin is the highest tier).</summary>
    public static readonly string[] Admin =
        SeniorMod.Concat(new[] { "setbalance", "assignmod" }).ToArray();

    /// <summary>command (lowercase) -> minimum tier rank required to run it.</summary>
    private static readonly Dictionary<string, int> RequiredRank = BuildRequiredRank();

    private static Dictionary<string, int> BuildRequiredRank()
    {
        var map = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var rank = 0; rank < TierNames.Length; rank++)
        {
            var commands = rank switch
            {
                0 => TrialMod,
                1 => Mod,
                2 => SeniorMod,
                _ => Admin
            };
            foreach (var command in commands)
            {
                // First writer wins: the lowest tier that lists a command sets its minimum.
                if (!map.ContainsKey(command))
                {
                    map[command] = rank;
                }
            }
        }

        return map;
    }

    /// <summary>
    /// Rank of a tier name (case-insensitive): TrialMod=0, Mod=1, SeniorMod=2,
    /// Admin=3. Unknown tiers return -1.
    /// </summary>
    public static int Rank(string? tier)
    {
        if (string.IsNullOrWhiteSpace(tier))
        {
            return -1;
        }

        for (var i = 0; i < TierNames.Length; i++)
        {
            if (string.Equals(TierNames[i], tier.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>True when <paramref name="tier"/> is one of the four known tiers.</summary>
    public static bool IsValidTier(string? tier) => Rank(tier) >= 0;

    /// <summary>
    /// Canonical ("TrialMod"/"Mod"/"SeniorMod"/"Admin") spelling of a tier name,
    /// or null when the tier is unknown.
    /// </summary>
    public static string? NormalizeTier(string? tier)
    {
        var rank = Rank(tier);
        return rank < 0 ? null : TierNames[rank];
    }

    /// <summary>
    /// Whether <paramref name="tier"/> may run <paramref name="command"/>:
    /// the command must be known and the tier's rank must be at least the
    /// command's minimum tier rank. Both arguments are case-insensitive.
    /// </summary>
    public static bool CanRun(string? tier, string? command)
    {
        if (command == null || !RequiredRank.TryGetValue(command.Trim().ToLowerInvariant(), out var required))
        {
            return false;
        }

        var tierRank = Rank(tier);
        return tierRank >= 0 && tierRank >= required;
    }

    /// <summary>
    /// Name of the LOWEST tier that may run <paramref name="command"/> (for
    /// "requires X or higher" replies), or null for unknown commands.
    /// </summary>
    public static string? MinimumTier(string? command)
    {
        if (command == null || !RequiredRank.TryGetValue(command.Trim().ToLowerInvariant(), out var required))
        {
            return null;
        }

        return TierNames[required];
    }
}
