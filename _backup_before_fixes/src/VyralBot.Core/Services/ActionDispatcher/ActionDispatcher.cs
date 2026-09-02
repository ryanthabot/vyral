using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using VyralBot.Core.Models;

namespace VyralBot.Core.Services.ActionDispatcher;

/// <summary>
/// Executes actions with timeout, cancellation, and error handling.
/// </summary>
public class ActionDispatcher : IActionDispatcher
{
    private readonly ILogger<ActionDispatcher> _logger;
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _runningActions = new();
    private const int DefaultTimeoutSeconds = 30;

    public int ActiveActionCount => _runningActions.Count;

    public ActionDispatcher(ILogger<ActionDispatcher> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Executes a single action with timeout.
    /// </summary>
    public async Task<bool> ExecuteActionAsync(Guid actionId, ActionContext context)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(DefaultTimeoutSeconds));

        _runningActions.TryAdd(actionId, cts);

        try
        {
            _logger.LogDebug("Executing action {ActionId} for trigger {TriggerId}",
                actionId, context.TriggerId);

            var startTime = DateTime.UtcNow;

            // TODO: Load action from database and execute
            // For now, simulate action execution
            await Task.Delay(100, cts.Token);

            var duration = DateTime.UtcNow - startTime;
            _logger.LogInformation("Action {ActionId} completed in {Duration}ms",
                actionId, duration.TotalMilliseconds);

            return true;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Action {ActionId} was cancelled", actionId);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Action {ActionId} failed", actionId);
            return false;
        }
        finally
        {
            _runningActions.TryRemove(actionId, out _);
            cts.Dispose();
        }
    }

    /// <summary>
    /// Executes a sequence of actions in order.
    /// Stops on first failure.
    /// </summary>
    public async Task<bool> ExecuteSequenceAsync(IEnumerable<Guid> actionIds, ActionContext context)
    {
        var actionList = actionIds.ToList();

        _logger.LogDebug("Executing sequence of {Count} actions for trigger {TriggerId}",
            actionList.Count, context.TriggerId);

        foreach (var actionId in actionList)
        {
            if (context.CancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning("Action sequence cancelled for trigger {TriggerId}", context.TriggerId);
                return false;
            }

            var success = await ExecuteActionAsync(actionId, context);
            if (!success)
            {
                _logger.LogError("Action {ActionId} failed, stopping sequence for trigger {TriggerId}",
                    actionId, context.TriggerId);
                return false;
            }
        }

        _logger.LogInformation("Action sequence completed successfully for trigger {TriggerId}", context.TriggerId);
        return true;
    }

    /// <summary>
    /// Cancels all running actions for a specific trigger.
    /// </summary>
    public void CancelTriggerActions(Guid triggerId)
    {
        var cancelledCount = 0;

        foreach (var (actionId, cts) in _runningActions)
        {
            // Note: In production, we'd need to track trigger->action mapping
            // For now, cancel all matching actions
            cts.Cancel();
            cancelledCount++;
        }

        if (cancelledCount > 0)
        {
            _logger.LogInformation("Cancelled {Count} actions for trigger {TriggerId}",
                cancelledCount, triggerId);
        }
    }
}
