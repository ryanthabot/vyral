namespace VyralBot.Core.Services.Moderation;

/// <summary>
/// Resolves a Discord user's effective mod tier: the explicit assignment OR the
/// highest Discord-role→tier mapping, whichever ranks higher.
/// </summary>
public interface IModTierResolver
{
    /// <returns>Canonical tier name ("TrialMod"…"Admin") or null when the user has none.</returns>
    Task<string?> ResolveTierAsync(string botId, string? guildId, string userId, string username);
}
