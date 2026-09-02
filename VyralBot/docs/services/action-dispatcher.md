# Action Dispatcher

## Overview
Executes bot actions (individual or sequences) with timeout and cancellation support. Used by the TriggerEngine to execute action sequences when triggers fire.

## Source Files

### IActionDispatcher Interface
- **File:** `src/VyralBot.Core/Services/ActionDispatcher/IActionDispatcher.cs`
- **Namespace:** `VyralBot.Core.Services.ActionDispatcher`
- **Methods:**
  - `Task<bool> ExecuteActionAsync(Guid actionId, ActionContext context)` — executes a single action
  - `Task<bool> ExecuteSequenceAsync(IEnumerable<Guid> actionIds, ActionContext context)` — executes ordered sequence
  - `void CancelTriggerActions(Guid triggerId)` — cancels all running actions for a trigger
- **Properties:**
  - `int ActiveActionCount { get; }` — number of currently running actions

### ActionDispatcher Implementation
- **File:** `src/VyralBot.Core/Services/ActionDispatcher/ActionDispatcher.cs`
- **Namespace:** `VyralBot.Core.Services.ActionDispatcher`
- **Class:** `ActionDispatcher : IActionDispatcher`
- **Constructor:** `ILogger<ActionDispatcher>`
- **Private fields:**
  - `ConcurrentDictionary<Guid, CancellationTokenSource> _runningActions` — tracks active actions for cancellation
  - `const int DefaultTimeoutSeconds = 30`
- **Execution:** Actions are tracked by Guid in the concurrent dictionary with individual CancellationTokenSources

### ActionContext Model
- **File:** `src/VyralBot.Core/Models/ActionContext.cs`
- **Namespace:** `VyralBot.Core.Models`
- **Properties:**
  - `Guid ContextId` (auto-generated)
  - `required Guid TriggerId`
  - `Dictionary<string, object> Variables`
  - `CancellationToken CancellationToken`
  - `string? Platform`
  - `string? Username`

## Architecture
```
TriggerEngine
  → Detects matching trigger
  → Creates ActionContext
  → IActionDispatcher.ExecuteSequenceAsync(trigger.ActionSequence, context)
    → For each action ID in sequence:
      → ExecuteActionAsync(actionId, context)
        → Track in _runningActions with CTS
        → Execute with timeout (30s default)
        → Remove from _runningActions on completion
```

## All Files
| File | Type | Description |
|------|------|-------------|
| `src/VyralBot.Core/Services/ActionDispatcher/IActionDispatcher.cs` | Interface | Action execution contract |
| `src/VyralBot.Core/Services/ActionDispatcher/ActionDispatcher.cs` | Class | Implementation with timeout/cancellation |
| `src/VyralBot.Core/Models/ActionContext.cs` | Model | Context passed to action execution |
