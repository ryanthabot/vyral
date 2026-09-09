using VyralBot.Core.Services.Moderation;

namespace VyralBot.UI.Services;

/// <summary>
/// Core-side <see cref="IModWorkflowInvoker"/> adapter over
/// <see cref="WorkflowTriggerService"/>: lets the Core !mod engine run custom
/// moderator-command workflows (verbs the built-in engine does not know)
/// without a Core→UI reference. The service performs the tier gating and the
/// awaited execution; the returned confirmation is what the engine audits.
/// </summary>
public sealed class ModWorkflowInvoker(WorkflowTriggerService triggerService) : IModWorkflowInvoker
{
    public Task<string?> TryInvokeAsync(string verb, string args, string botId, string? guildId, string channel, string username, string userId)
        => triggerService.TryHandleModCommandAsync(botId, guildId, channel, username, userId, verb, args);
}
