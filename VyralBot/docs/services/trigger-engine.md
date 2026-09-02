# Trigger Engine

## Overview
Event-driven trigger system that subscribes to EventBus events, matches them against configured triggers, and dispatches action sequences via the ActionDispatcher.

## Source Files

### ITriggerEngine Interface
- **File:** `src/VyralBot.Core/Services/TriggerEngine/ITriggerEngine.cs`
- **Namespace:** `VyralBot.Core.Services.TriggerEngine`
- **Methods:**
  - `Task StartAsync(CancellationToken cancellationToken)` — starts listening for events
  - `Task StopAsync()` — stops the engine
  - `Task FireTriggerAsync(Guid triggerId, Dictionary<string, object>? variables)` — manually fire a trigger
  - `Task ReloadTriggersAsync()` — reload trigger configurations from database
- **Properties:**
  - `int ActiveTriggerCount { get; }` — number of loaded triggers

### TriggerEngine Implementation
- **File:** `src/VyralBot.Core/Services/TriggerEngine/TriggerEngine.cs`
- **Namespace:** `VyralBot.Core.Services.TriggerEngine`
- **Class:** `TriggerEngine : ITriggerEngine`
- **Constructor:** `ILogger<TriggerEngine>`, `IEventBus`, `IActionDispatcher`
- **Private fields:**
  - `List<Guid> _subscriptionIds` — EventBus subscription IDs for cleanup
  - `bool _isRunning`
- **Startup:** Subscribes to EventBus events, loads triggers from database
- **Event processing:** `OnEventReceivedAsync(IEvent)` — matches events against trigger conditions, creates ActionContext, dispatches via IActionDispatcher

### Trigger Entity
- **File:** `src/VyralBot.Data/Entities/Trigger.cs`
- **Namespace:** `VyralBot.Data.Entities`
- **Properties:**
  - `Guid Id`
  - `string Name`
  - `string EventType` — which event type this trigger responds to
  - `string? Platform` — optional platform filter
  - `string? Condition` — JSON condition for filtering
  - `int CooldownSeconds`
  - `string Permission` — required permission level
  - `string ActionSequence` — JSON array of BotAction Guid IDs

### BotAction Entity
- **File:** `src/VyralBot.Data/Entities/BotAction.cs`
- **Namespace:** `VyralBot.Data.Entities`
- **Properties:**
  - `Guid Id`
  - `string Name`
  - `string ActionType`
  - `string Configuration` — JSON configuration
  - `int TimeoutSeconds`
  - `bool RetryOnFailure`
  - `int MaxRetries`

## Trigger Flow
```
EventBus publishes event
  → TriggerEngine.OnEventReceivedAsync(event)
    → Match event type against loaded triggers
    → Check platform filter, conditions, cooldown, permissions
    → Create ActionContext with event variables
    → IActionDispatcher.ExecuteSequenceAsync(trigger.ActionSequence, context)
      → Execute each BotAction in sequence
```

## Lifecycle
```
App.OnStartup()
  → ITriggerEngine.StartAsync()
    → Subscribe to EventBus events
    → Load triggers from database
  (running...)
  → ITriggerEngine.StopAsync()
    → Unsubscribe from EventBus
```

## All Files
| File | Type | Description |
|------|------|-------------|
| `src/VyralBot.Core/Services/TriggerEngine/ITriggerEngine.cs` | Interface | Trigger engine contract |
| `src/VyralBot.Core/Services/TriggerEngine/TriggerEngine.cs` | Class | Event matching and dispatch |
| `src/VyralBot.Data/Entities/Trigger.cs` | Entity | Trigger configuration |
| `src/VyralBot.Data/Entities/BotAction.cs` | Entity | Action configuration |
