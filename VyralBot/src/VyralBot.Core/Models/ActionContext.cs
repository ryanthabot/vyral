namespace VyralBot.Core.Models;

/// <summary>
/// Context provided to actions during execution.
/// Contains state, variables, and helper methods.
/// </summary>
public class ActionContext
{
    /// <summary>
    /// Unique identifier for this execution context.
    /// </summary>
    public Guid ContextId { get; init; } = Guid.NewGuid();

    /// <summary>
    /// The trigger that initiated this action sequence.
    /// </summary>
    public required Guid TriggerId { get; init; }

    /// <summary>
    /// Variables available in this context.
    /// </summary>
    public Dictionary<string, object> Variables { get; } = new();

    /// <summary>
    /// Cancellation token for the action execution.
    /// </summary>
    public CancellationToken CancellationToken { get; init; }

    /// <summary>
    /// Platform that triggered the action (e.g., "Twitch", "OBS").
    /// </summary>
    public string? Platform { get; init; }

    /// <summary>
    /// User who triggered the action (if applicable).
    /// </summary>
    public string? Username { get; init; }

    /// <summary>
    /// Gets a variable value.
    /// </summary>
    public T? GetVariable<T>(string key)
    {
        return Variables.TryGetValue(key, out var value) ? (T?)value : default;
    }

    /// <summary>
    /// Sets a variable value.
    /// </summary>
    public void SetVariable(string key, object value)
    {
        Variables[key] = value;
    }
}
