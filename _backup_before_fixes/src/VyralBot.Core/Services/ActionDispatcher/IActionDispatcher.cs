using VyralBot.Core.Models;

namespace VyralBot.Core.Services.ActionDispatcher;

/// <summary>
/// Dispatches and executes actions with timeout and cancellation support.
/// </summary>
public interface IActionDispatcher
{
    /// <summary>
    /// Executes a single action.
    /// </summary>
    /// <param name="actionId">The action ID to execute.</param>
    /// <param name="context">Execution context.</param>
    /// <returns>True if successful, false otherwise.</returns>
    Task<bool> ExecuteActionAsync(Guid actionId, ActionContext context);

    /// <summary>
    /// Executes a sequence of actions in order.
    /// </summary>
    /// <param name="actionIds">List of action IDs to execute.</param>
    /// <param name="context">Execution context.</param>
    /// <returns>True if all actions succeeded, false if any failed.</returns>
    Task<bool> ExecuteSequenceAsync(IEnumerable<Guid> actionIds, ActionContext context);

    /// <summary>
    /// Cancels all running actions for a specific trigger.
    /// </summary>
    /// <param name="triggerId">The trigger ID.</param>
    void CancelTriggerActions(Guid triggerId);

    /// <summary>
    /// Gets the count of currently executing actions.
    /// </summary>
    int ActiveActionCount { get; }
}
