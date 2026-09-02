namespace VyralBot.Core.Services.EventBus;

/// <summary>
/// Base interface for all events in the system.
/// </summary>
public interface IEvent
{
    /// <summary>
    /// Unique identifier for this event instance.
    /// </summary>
    Guid EventId { get; }

    /// <summary>
    /// When the event was created.
    /// </summary>
    DateTime Timestamp { get; }

    /// <summary>
    /// Source of the event (e.g., "Twitch", "OBS", "Manual", "System").
    /// </summary>
    string Source { get; }

    /// <summary>
    /// Optional metadata for the event.
    /// </summary>
    Dictionary<string, object>? Metadata { get; }
}

/// <summary>
/// Base class for events providing common implementation.
/// </summary>
public abstract record EventBase : IEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public required string Source { get; init; }
    public Dictionary<string, object>? Metadata { get; init; }
}
