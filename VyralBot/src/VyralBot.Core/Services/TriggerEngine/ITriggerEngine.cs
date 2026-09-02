using VyralBot.Core.Services.EventBus;

namespace VyralBot.Core.Services.TriggerEngine;

/// <summary>
/// Matches events to triggers and executes associated actions.
/// </summary>
public interface ITriggerEngine
{
    /// <summary>
    /// Starts the trigger engine and begins monitoring events.
    /// </summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops the trigger engine.
    /// </summary>
    Task StopAsync();

    /// <summary>
    /// Manually fires a trigger by ID.
    /// </summary>
    /// <param name="triggerId">The trigger ID to fire.</param>
    /// <param name="variables">Optional variables to pass to actions.</param>
    Task FireTriggerAsync(Guid triggerId, Dictionary<string, object>? variables = null);

    /// <summary>
    /// Reloads triggers from the database.
    /// </summary>
    Task ReloadTriggersAsync();

    /// <summary>
    /// Gets the count of active triggers.
    /// </summary>
    int ActiveTriggerCount { get; }
}
