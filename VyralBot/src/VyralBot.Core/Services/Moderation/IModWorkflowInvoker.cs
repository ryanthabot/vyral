namespace VyralBot.Core.Services.Moderation;

/// <summary>
/// Invokes a moderator-command workflow for verbs the built-in engine does
/// not know ("!mod &lt;custom-verb&gt;"). Implemented over the workflow system
/// in the UI layer (which owns WorkflowTriggerService); Core only sees this
/// seam, mirroring the <see cref="IModTierResolver"/> placement.
/// </summary>
public interface IModWorkflowInvoker
{
    /// <summary>
    /// Offers an unknown mod verb to the workflow system. The built-in verbs
    /// never reach this (they keep precedence). The caller resolves the
    /// caller's tier and the workflow's own minimum-tier gate upstream here.
    /// </summary>
    /// <param name="verb">The lowercased "!mod" verb the engine did not recognize.</param>
    /// <param name="args">Everything after the verb, whitespace-joined.</param>
    /// <param name="botId">Id of the bot instance that received the command.</param>
    /// <param name="guildId">Discord guild snowflake id, when known.</param>
    /// <param name="channel">Channel the command was posted in (the mod channel).</param>
    /// <param name="username">Display name of the calling moderator.</param>
    /// <param name="userId">Discord snowflake id of the calling moderator.</param>
    /// <returns>Reply text when a workflow handled the verb, or null when no workflow matches.</returns>
    Task<string?> TryInvokeAsync(string verb, string args, string botId, string? guildId, string channel, string username, string userId);
}
