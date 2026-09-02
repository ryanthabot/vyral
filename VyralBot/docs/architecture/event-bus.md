# Event Bus Architecture

## Overview

The Event Bus provides a decoupled, asynchronous publish/subscribe messaging system used throughout VyralBot. It is built on `System.Threading.Channels` for high-throughput, bounded event processing with controlled concurrency.

### Source Files

| File | Path |
|------|------|
| `IEvent` | `src/VyralBot.Core/Services/EventBus/IEvent.cs` |
| `IEventBus` | `src/VyralBot.Core/Services/EventBus/IEventBus.cs` |
| `EventBus` | `src/VyralBot.Core/Services/EventBus/EventBus.cs` |
| `ProfileSwitchedEvent` | `src/VyralBot.Core/Services/EventBus/ProfileSwitchedEvent.cs` |

---

## IEvent Interface & EventBase

All events implement `IEvent`. The `EventBase` abstract record provides default implementations for all interface members.

```csharp
namespace VyralBot.Core.Services.EventBus;

public interface IEvent
{
    Guid EventId { get; }
    DateTime Timestamp { get; }
    string Source { get; }
    Dictionary<string, object>? Metadata { get; }
}

public abstract record EventBase : IEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public required string Source { get; init; }
    public Dictionary<string, object>? Metadata { get; init; }
}
```

### Event Properties

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `EventId` | `Guid` | `Guid.NewGuid()` | Unique identifier for each event instance |
| `Timestamp` | `DateTime` | `DateTime.UtcNow` | UTC time the event was created |
| `Source` | `string` | *(required)* | Identifies who/what produced the event |
| `Metadata` | `Dictionary<string, object>?` | `null` | Optional key-value metadata bag |

---

## IEventBus Interface

```csharp
namespace VyralBot.Core.Services.EventBus;

public interface IEventBus
{
    Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default) where TEvent : IEvent;
    Task<Guid> SubscribeAsync<TEvent>(Func<TEvent, Task> handler, CancellationToken cancellationToken = default) where TEvent : IEvent;
    void Unsubscribe(Guid subscriptionId);
    int SubscriptionCount { get; }
}
```

| Member | Description |
|--------|-------------|
| `PublishAsync<TEvent>` | Writes an event into the internal channel for asynchronous dispatch |
| `SubscribeAsync<TEvent>` | Registers a handler for `TEvent`, returns a `Guid` subscription ID |
| `Unsubscribe(Guid)` | Removes a subscription by its ID |
| `SubscriptionCount` | Total number of active subscriptions across all event types |

---

## EventBus Implementation

The concrete `EventBus` class (205 lines) implements `IEventBus` and `IDisposable`.

### Internal Architecture

```
PublishAsync ──► BoundedChannel<(IEvent, Type)> ──► ProcessEventsAsync loop ──► Handler dispatch
                  (capacity: 500, DropOldest)          (single reader)         (max 4 concurrent)
```

### Key Fields

```csharp
private readonly ConcurrentDictionary<Type, ConcurrentDictionary<Guid, Delegate>> _subscriptions = new();
private readonly Channel<(IEvent Event, Type EventType)> _eventChannel;
private readonly SemaphoreSlim _handlerSemaphore;
private readonly CancellationTokenSource _cts = new();
private readonly Task _processingTask;
```

### Channel Configuration

| Setting | Value | Description |
|---------|-------|-------------|
| `ChannelCapacity` | `500` | Maximum events buffered in the channel |
| `FullMode` | `BoundedChannelFullMode.DropOldest` | Oldest events are dropped when the channel is full |
| `SingleReader` | `true` | Only the processing loop reads from the channel |
| `SingleWriter` | `false` | Multiple producers can write concurrently |

### Concurrency Control

| Setting | Value | Description |
|---------|-------|-------------|
| `MaxConcurrentHandlers` | `4` | Maximum handlers executing in parallel via `SemaphoreSlim` |

The background `ProcessEventsAsync` loop reads events from the channel, resolves matching subscriptions by event type, and dispatches handlers in parallel using `Task.WhenAll`. Each handler execution is gated by the `SemaphoreSlim` to cap concurrency at 4.

### Subscription Storage

Subscriptions are stored in a two-level concurrent dictionary:

```
ConcurrentDictionary<Type, ConcurrentDictionary<Guid, Delegate>>
         ▲                         ▲           ▲
     Event type          Subscription ID    Handler delegate
```

- `SubscribeAsync` generates a new `Guid`, stores the handler delegate keyed by event type and subscription ID.
- `Unsubscribe` iterates all type dictionaries to find and remove the matching subscription ID.
- `SubscriptionCount` sums the count of all inner dictionaries.

### Disposal

`EventBus` implements `IDisposable`:
- Cancels the internal `CancellationTokenSource`
- Completes the channel writer
- Disposes the `SemaphoreSlim`

---

## System Events

All system events extend `EventBase`.

### ProfileSwitchedEvent

Raised when the active profile changes.

```csharp
public record ProfileSwitchedEvent : EventBase
{
    public required string OldProfileName { get; init; }
    public required string NewProfileName { get; init; }
    public ProfileSwitchedEvent() { Source = "System"; }
}
```

### PlatformChatRoutingChangedEvent

Raised when chat routing is enabled or disabled for a platform.

```csharp
public record PlatformChatRoutingChangedEvent : EventBase
{
    public string Platform { get; init; } = "";
    public bool Enabled { get; init; }
}
```

### PlatformConnectionChangedEvent

Raised when a platform connection state changes.

```csharp
public record PlatformConnectionChangedEvent : EventBase
{
    public string Platform { get; init; } = "";
    public bool Connected { get; init; }
    public string? Channel { get; init; }
    public string? BotUsername { get; init; }
}
```

---

## Usage Patterns

### Publishing an Event

```csharp
await _eventBus.PublishAsync(new ProfileSwitchedEvent
{
    OldProfileName = "Default",
    NewProfileName = "Stream"
});
```

### Subscribing to an Event

`SubscribeAsync` returns a `Guid` that identifies the subscription for later removal.

```csharp
Guid subId = await _eventBus.SubscribeAsync<ProfileSwitchedEvent>(async evt =>
{
    // Handle profile switch
    _logger.LogInformation("Profile changed from {Old} to {New}", evt.OldProfileName, evt.NewProfileName);
});
```

### Unsubscribing

Pass the `Guid` returned from `SubscribeAsync` to remove the subscription.

```csharp
_eventBus.Unsubscribe(subId);
```

### Subscribing to Platform Events

```csharp
await _eventBus.SubscribeAsync<PlatformConnectionChangedEvent>(async evt =>
{
    _logger.LogInformation("{Platform} connection: {Connected}, channel: {Channel}",
        evt.Platform, evt.Connected, evt.Channel);
});

await _eventBus.SubscribeAsync<PlatformChatRoutingChangedEvent>(async evt =>
{
    _logger.LogInformation("{Platform} chat routing: {Enabled}", evt.Platform, evt.Enabled);
});
```

---

## Dependency Injection Registration

The `EventBus` is registered as a singleton so all consumers share the same instance and channel:

```csharp
services.AddSingleton<IEventBus, EventBus>();
```

Consumers inject `IEventBus`:

```csharp
public class MyService
{
    private readonly IEventBus _eventBus;

    public MyService(IEventBus eventBus)
    {
        _eventBus = eventBus;
    }
}
```
