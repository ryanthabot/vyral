# Action Context Model

**Complete documentation of the action execution context system**

---

## Table of Contents

1. [Overview](#overview)
2. [Class Definition](#class-definition)
3. [Properties](#properties)
4. [Methods](#methods)
5. [Usage Examples](#usage-examples)
6. [Variable Substitution](#variable-substitution)
7. [Integration Points](#integration-points)
8. [Design Patterns](#design-patterns)
9. [Best Practices](#best-practices)

---

## Overview

### Purpose

**ActionContext** is a data class that provides execution context for trigger actions. It serves as a carrier for:

1. **Execution Identity**: Unique context ID and triggering event ID
2. **State Storage**: Dictionary for variables shared between actions
3. **Event Metadata**: Platform and username information
4. **Cancellation Support**: Token for graceful termination

### Key Responsibilities

```
ActionContext Responsibilities:
├─ Identify execution instance (ContextId)
├─ Link to triggering event (TriggerId)
├─ Store execution variables (Variables dictionary)
├─ Provide cancellation mechanism (CancellationToken)
└─ Carry event metadata (Platform, Username)
```

### File Location

- **File**: `src/VyralBot.Core/Models/ActionContext.cs`
- **Namespace**: `VyralBot.Core.Models`
- **Lines**: 60
- **Dependencies**: None (pure data class)

---

## Class Definition

```csharp
namespace VyralBot.Core.Models;

/// <summary>
/// Provides execution context for trigger actions.
/// Contains execution state, variables, and metadata for action execution.
/// </summary>
public class ActionContext
{
    /// <summary>
    /// Unique identifier for this execution context.
    /// Automatically generated for each context instance.
    /// </summary>
    public Guid ContextId { get; init; } = Guid.NewGuid();

    /// <summary>
    /// ID of the trigger that initiated this action execution.
    /// Required - links actions back to their triggering event.
    /// </summary>
    public required Guid TriggerId { get; init; }

    /// <summary>
    /// Dictionary of variables available during action execution.
    /// Used for variable substitution in action parameters.
    /// </summary>
    public Dictionary<string, object> Variables { get; } = new();

    /// <summary>
    /// Cancellation token for graceful termination of long-running actions.
    /// </summary>
    public CancellationToken CancellationToken { get; init; }

    /// <summary>
    /// Source platform of the triggering event (e.g., "Twitch", "Kick", "OBS").
    /// Optional - may be null for system-initiated events.
    /// </summary>
    public string? Platform { get; init; }

    /// <summary>
    /// Username of the user who triggered the event.
    /// Optional - may be null for system events or anonymous triggers.
    /// </summary>
    public string? Username { get; init; }

    /// <summary>
    /// Gets a variable from the context dictionary with type safety.
    /// Returns default(T) if the key doesn't exist.
    /// </summary>
    public T? GetVariable<T>(string key)
    {
        return Variables.TryGetValue(key, out var value) ? (T?)value : default;
    }

    /// <summary>
    /// Sets a variable in the context dictionary.
    /// Overwrites existing values with the same key.
    /// </summary>
    public void SetVariable(string key, object value)
    {
        Variables[key] = value;
    }
}
```

### Design Choices

**Why `init` setters?**
- Properties are immutable after construction
- Prevents accidental modification of identity/metadata
- Forces explicit construction with required values

**Why `required` on TriggerId?**
- Compiler enforces providing TriggerId at construction
- Makes it impossible to create ActionContext without linking to trigger
- Reduces runtime errors from missing trigger references

**Why public `Variables` dictionary?**
- Allows direct access for bulk operations
- Maintains encapsulation via property (not field)
- Supports advanced scenarios like enumeration/clearing

---

## Properties

### 1. ContextId

```csharp
public Guid ContextId { get; init; } = Guid.NewGuid();
```

**Purpose**: Unique identifier for this specific execution instance

**Characteristics:**
- **Type**: `Guid` (globally unique identifier)
- **Auto-generated**: Default initializer creates new GUID
- **Immutable**: `init` setter prevents modification after construction
- **Usage**: Logging, debugging, execution tracking

**Example Values:**
```
ContextId: 8a7f3e2c-1d4b-4a5c-9f3e-7b2a1c4d5e6f
ContextId: 3f4e5d6c-7a8b-9c0d-1e2f-3a4b5c6d7e8f
```

**Use Cases:**
- **Logging**: `[Context {ContextId}] Executing action...`
- **Debugging**: Trace execution flow across multiple actions
- **Audit Trail**: Link action executions to results
- **Error Reporting**: Include ContextId in exception messages

---

### 2. TriggerId

```csharp
public required Guid TriggerId { get; init; }
```

**Purpose**: Links actions back to the trigger that initiated them

**Characteristics:**
- **Type**: `Guid` (trigger's unique identifier)
- **Required**: Compiler error if not provided at construction
- **Immutable**: `init` setter prevents modification
- **Foreign Key**: References `Trigger.Id` from database

**Example Construction:**
```csharp
// ✅ Valid - TriggerId provided
var context = new ActionContext
{
    TriggerId = trigger.Id
};

// ❌ Compile error - TriggerId not provided
var context = new ActionContext { };
```

**Use Cases:**
- **Trigger Lookup**: Query trigger details during execution
- **Action History**: Store which trigger executed which actions
- **Performance Tracking**: Measure execution time per trigger
- **Debugging**: Identify which trigger is misbehaving

---

### 3. Variables

```csharp
public Dictionary<string, object> Variables { get; } = new();
```

**Purpose**: Storage for execution state and variable substitution

**Characteristics:**
- **Type**: `Dictionary<string, object>` (key-value pairs)
- **Initialized**: Empty dictionary created at construction
- **Mutable**: Contents can be modified (but not the dictionary reference)
- **Get-only property**: Cannot replace entire dictionary

**Common Variable Types:**

| Variable Name | Type | Example Value | Source |
|---------------|------|---------------|--------|
| `username` | string | "JohnDoe" | Event property |
| `message` | string | "Hello world!" | Chat message |
| `subscriber_months` | int | 12 | Subscription event |
| `bits_amount` | int | 500 | Cheer event |
| `raid_viewer_count` | int | 250 | Raid event |
| `timestamp` | DateTime | 2024-01-15 14:30:22 | System |
| `platform` | string | "Twitch" | Event source |
| `last_process_id` | int | 12345 | Action result |

**Variable Lifetime:**
```
Trigger fires
   ↓
TriggerEngine creates ActionContext
   ↓
Populate Variables from event
   ↓
Action 1 executes (reads/writes variables)
   ↓
Action 2 executes (reads variables from Action 1)
   ↓
Action N executes
   ↓
Context disposed (variables lost)
```

---

### 4. CancellationToken

```csharp
public CancellationToken CancellationToken { get; init; }
```

**Purpose**: Enables graceful cancellation of long-running actions

**Characteristics:**
- **Type**: `CancellationToken` (from System.Threading)
- **Optional**: Defaults to `CancellationToken.None` if not provided
- **Immutable**: `init` setter prevents replacement
- **Propagation**: Passed to async methods like ProcessManager.StartProcessAsync

**Use Cases:**

| Scenario | Cancellation Trigger |
|----------|----------------------|
| User stops action | UI button click → CancellationTokenSource.Cancel() |
| Profile switch | Profile change → Cancel all pending actions |
| App shutdown | Application.Exit → Cancel all actions |
| Timeout | Task.Delay(timeout) → Cancel after N seconds |

**Example Implementation:**
```csharp
public async Task ExecuteDelayActionAsync(ActionContext context, int delaySeconds)
{
    try
    {
        await Task.Delay(
            TimeSpan.FromSeconds(delaySeconds), 
            context.CancellationToken
        );
    }
    catch (OperationCanceledException)
    {
        _logger.LogInformation("Delay action cancelled for context {ContextId}", context.ContextId);
    }
}
```

---

### 5. Platform

```csharp
public string? Platform { get; init; }
```

**Purpose**: Identifies the source platform of the triggering event

**Characteristics:**
- **Type**: `string?` (nullable string)
- **Optional**: May be null for system events
- **Immutable**: `init` setter prevents modification
- **Values**: "Twitch", "Kick", "YouTube", "TikTok", "Discord", "OBS", null

**Platform Values by Event Type:**

| Event Type | Platform Value |
|------------|----------------|
| TwitchChatMessageEvent | "Twitch" |
| KickChatMessageEvent | "Kick" |
| YouTubeChatMessageEvent | "YouTube" |
| OBSSceneChangedEvent | "OBS" |
| TimerTriggeredEvent | null |
| SystemStartupEvent | null |

**Use Cases:**
- **Conditional Logic**: Only execute action if Platform == "Twitch"
- **Platform-Specific Actions**: Send message to correct platform
- **Analytics**: Track which platforms generate most triggers
- **Debugging**: Identify event source in logs

---

### 6. Username

```csharp
public string? Username { get; init; }
```

**Purpose**: Username of the person who triggered the event

**Characteristics:**
- **Type**: `string?` (nullable string)
- **Optional**: May be null for system/anonymous events
- **Immutable**: `init` setter prevents modification
- **Case**: Typically lowercase (matches platform conventions)

**When Username is Populated:**

| Event Type | Username Value |
|------------|----------------|
| Chat message | Message sender |
| Subscription | Subscriber username |
| Follow | New follower username |
| Raid | Raider username |
| Cheer | Cheerer username |
| Timer | null |
| System startup | null |

**Use Cases:**
- **Personalized Responses**: "Thanks for the sub, {username}!"
- **Moderation**: Check username against ban list
- **Permissions**: Only execute action if username is mod
- **Logging**: Track who triggered which actions

---

## Methods

### GetVariable<T>(string key)

**Signature:**
```csharp
public T? GetVariable<T>(string key)
```

**Purpose**: Type-safe retrieval of variables from the context dictionary

**Implementation:**
```csharp
public T? GetVariable<T>(string key)
{
    return Variables.TryGetValue(key, out var value) ? (T?)value : default;
}
```

**Workflow:**
1. **TryGetValue**: Attempts to retrieve value for key
2. **Cast**: Casts `object` value to `T?`
3. **Default**: Returns `default(T)` if key not found or cast fails

**Return Values:**

| Scenario | Return Value |
|----------|--------------|
| Key exists, correct type | Casted value |
| Key exists, wrong type | default(T) (0, null, false, etc.) |
| Key doesn't exist | default(T) |

**Type Examples:**

```csharp
// String variable
var username = context.GetVariable<string>("username");
// Returns: "JohnDoe" or null

// Int variable
var months = context.GetVariable<int>("subscriber_months");
// Returns: 12 or 0

// Bool variable
var isMod = context.GetVariable<bool>("is_moderator");
// Returns: true, false

// DateTime variable
var timestamp = context.GetVariable<DateTime>("timestamp");
// Returns: DateTime instance or DateTime.MinValue

// Complex type
var customData = context.GetVariable<MyCustomClass>("data");
// Returns: MyCustomClass instance or null
```

**Null-Coalescing Pattern:**
```csharp
var username = context.GetVariable<string>("username") ?? "Unknown";
var months = context.GetVariable<int>("subscriber_months");
if (months == 0) months = 1; // Default to 1 if not set
```

**Advantages:**
- **Type Safety**: Generic parameter ensures correct return type
- **No Exceptions**: Returns default instead of throwing
- **Null Safety**: Works with nullable and non-nullable types
- **Clean Syntax**: Single method call instead of multiple checks

---

### SetVariable(string key, object value)

**Signature:**
```csharp
public void SetVariable(string key, object value)
```

**Purpose**: Stores a variable in the context dictionary

**Implementation:**
```csharp
public void SetVariable(string key, object value)
{
    Variables[key] = value;
}
```

**Behavior:**
- **Overwrites**: If key exists, replaces old value
- **Creates**: If key doesn't exist, adds new entry
- **No Validation**: Accepts any object type
- **Case Sensitive**: "Username" and "username" are different keys

**Usage Examples:**

```csharp
// Basic types
context.SetVariable("username", "JohnDoe");
context.SetVariable("subscriber_months", 12);
context.SetVariable("is_moderator", true);
context.SetVariable("timestamp", DateTime.Now);

// Complex types
context.SetVariable("user_data", new UserProfile { ... });
context.SetVariable("event_metadata", new Dictionary<string, string> { ... });

// Overwriting
context.SetVariable("count", 1);
context.SetVariable("count", 2); // Overwrites 1
```

**Naming Conventions:**

| Convention | Example | Use Case |
|------------|---------|----------|
| snake_case | `subscriber_months` | Event data |
| camelCase | `messageText` | Action results |
| PascalCase | `UserProfile` | Complex objects |
| Prefixed | `twitch_username` | Platform-specific |

**When to Set Variables:**

1. **Trigger Initialization**: TriggerEngine populates from event
2. **Action Execution**: Actions store results for later actions
3. **Conditional Logic**: Store decision results
4. **Error Handling**: Store error messages/codes

---

## Usage Examples

### Example 1: Basic Context Creation

```csharp
public class TriggerEngine
{
    public async Task ExecuteTriggerAsync(Trigger trigger, TwitchChatMessageEvent evt)
    {
        // Create context with required TriggerId
        var context = new ActionContext
        {
            TriggerId = trigger.Id,
            Platform = "Twitch",
            Username = evt.Username,
            CancellationToken = CancellationToken.None
        };

        // Populate variables from event
        context.SetVariable("username", evt.Username);
        context.SetVariable("message", evt.Message);
        context.SetVariable("is_moderator", evt.IsModerator);
        context.SetVariable("is_subscriber", evt.IsSubscriber);
        context.SetVariable("timestamp", DateTime.Now);

        // Execute actions
        await _actionDispatcher.ExecuteActionsAsync(trigger.Actions, context);
    }
}
```

---

### Example 2: Variable Substitution in Actions

```csharp
public class SendMessageAction
{
    public async Task ExecuteAsync(string messageTemplate, ActionContext context)
    {
        // Original template: "Thanks for the message, {username}!"
        
        // Substitute variables
        var message = messageTemplate
            .Replace("{username}", context.GetVariable<string>("username") ?? "viewer")
            .Replace("{message}", context.GetVariable<string>("message") ?? "")
            .Replace("{platform}", context.Platform ?? "unknown");

        // Result: "Thanks for the message, JohnDoe!"
        
        await _chatService.SendMessageAsync(message);
    }
}
```

---

### Example 3: Chaining Actions with Context

```csharp
public async Task ExecuteActionChainAsync(List<IAction> actions, ActionContext context)
{
    foreach (var action in actions)
    {
        switch (action)
        {
            case RunProgramAction programAction:
                var process = await _processManager.StartProcessAsync(...);
                context.SetVariable("last_process_id", process.Id); // Store for later
                break;

            case DelayAction delayAction:
                await Task.Delay(delayAction.Milliseconds, context.CancellationToken);
                break;

            case KillProcessAction killAction:
                // Retrieve PID from earlier action
                var pid = context.GetVariable<int>("last_process_id");
                if (pid != 0)
                {
                    await _processManager.KillProcessAsync(pid);
                }
                break;

            case SendMessageAction messageAction:
                var username = context.GetVariable<string>("username");
                await SendMessageAsync($"Action completed for {username}");
                break;
        }
    }
}
```

---

### Example 4: Conditional Execution Based on Context

```csharp
public async Task ExecuteConditionalActionAsync(ConditionalAction action, ActionContext context)
{
    // Evaluate condition using context variables
    var shouldExecute = action.Condition switch
    {
        "is_subscriber" => context.GetVariable<bool>("is_subscriber"),
        "is_moderator" => context.GetVariable<bool>("is_moderator"),
        "months_greater_than_6" => context.GetVariable<int>("subscriber_months") > 6,
        "platform_is_twitch" => context.Platform == "Twitch",
        _ => false
    };

    if (shouldExecute)
    {
        await ExecuteActionAsync(action.TargetAction, context);
    }
    else
    {
        _logger.LogDebug(
            "Condition '{Condition}' not met for context {ContextId}",
            action.Condition,
            context.ContextId
        );
    }
}
```

---

### Example 5: Error Handling with Context

```csharp
public async Task ExecuteActionWithErrorHandlingAsync(IAction action, ActionContext context)
{
    try
    {
        await action.ExecuteAsync(context);
        context.SetVariable("last_action_success", true);
        context.SetVariable("last_action_error", null);
    }
    catch (Exception ex)
    {
        context.SetVariable("last_action_success", false);
        context.SetVariable("last_action_error", ex.Message);

        _logger.LogError(
            ex,
            "Action execution failed for context {ContextId} (Trigger: {TriggerId})",
            context.ContextId,
            context.TriggerId
        );

        // Next action can check for errors
        // if (context.GetVariable<bool>("last_action_success")) { ... }
    }
}
```

---

### Example 6: Cancellation Token Usage

```csharp
public class LongRunningAction
{
    public async Task ExecuteAsync(ActionContext context)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(30)); // 30-second timeout

        try
        {
            // Long-running operation
            await SomeLongOperationAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            if (context.CancellationToken.IsCancellationRequested)
            {
                _logger.LogInformation("Action cancelled by user");
            }
            else
            {
                _logger.LogWarning("Action timed out after 30 seconds");
            }
        }
    }
}
```

---

## Variable Substitution

### Substitution Syntax

**Template Format:**
```
{variable_name}
```

**Example Templates:**

| Template | Variables | Result |
|----------|-----------|--------|
| `"Hello {username}!"` | username: "JohnDoe" | "Hello JohnDoe!" |
| `"{username} subbed for {months} months"` | username: "Jane", months: 12 | "Jane subbed for 12 months" |
| `"Platform: {platform}"` | platform: "Twitch" | "Platform: Twitch" |
| `"{username} raided with {count} viewers"` | username: "Bob", count: 250 | "Bob raided with 250 viewers" |

### Substitution Engine Implementation

```csharp
public class VariableSubstitutionEngine
{
    private static readonly Regex VariablePattern = new(@"\{([^}]+)\}", RegexOptions.Compiled);

    public string Substitute(string template, ActionContext context)
    {
        return VariablePattern.Replace(template, match =>
        {
            var variableName = match.Groups[1].Value;
            var value = context.GetVariable<object>(variableName);

            return value switch
            {
                null => $"{{{variableName}}}",  // Keep placeholder if not found
                string s => s,
                int i => i.ToString(),
                bool b => b.ToString().ToLower(),
                DateTime dt => dt.ToString("yyyy-MM-dd HH:mm:ss"),
                _ => value.ToString() ?? $"{{{variableName}}}"
            };
        });
    }
}
```

### Advanced Substitution Features

**1. Default Values:**
```csharp
// Template: "Hello {username|viewer}!"
// If username not set, uses "viewer" as default

public string SubstituteWithDefaults(string template, ActionContext context)
{
    return Regex.Replace(template, @"\{([^}|]+)\|?([^}]*)\}", match =>
    {
        var variableName = match.Groups[1].Value;
        var defaultValue = match.Groups[2].Value;
        
        var value = context.GetVariable<string>(variableName);
        return value ?? defaultValue;
    });
}
```

**2. Formatting:**
```csharp
// Template: "Amount: {amount:C}"  → "Amount: $5.00"
// Template: "Date: {timestamp:yyyy-MM-dd}"  → "Date: 2024-01-15"

public string SubstituteWithFormatting(string template, ActionContext context)
{
    return Regex.Replace(template, @"\{([^}:]+):?([^}]*)\}", match =>
    {
        var variableName = match.Groups[1].Value;
        var format = match.Groups[2].Value;
        
        var value = context.GetVariable<object>(variableName);
        
        if (value is IFormattable formattable && !string.IsNullOrEmpty(format))
        {
            return formattable.ToString(format, CultureInfo.InvariantCulture);
        }
        
        return value?.ToString() ?? $"{{{variableName}}}";
    });
}
```

**3. Nested Properties:**
```csharp
// Template: "User: {user.name}, Level: {user.level}"

public string SubstituteNestedProperties(string template, ActionContext context)
{
    return Regex.Replace(template, @"\{([^}]+)\}", match =>
    {
        var path = match.Groups[1].Value.Split('.');
        object? current = context.GetVariable<object>(path[0]);
        
        for (int i = 1; i < path.Length && current != null; i++)
        {
            var property = current.GetType().GetProperty(path[i]);
            current = property?.GetValue(current);
        }
        
        return current?.ToString() ?? match.Value;
    });
}
```

---

## Integration Points

### 1. TriggerEngine Integration

```csharp
public class TriggerEngine
{
    public async Task ExecuteTriggerAsync(Trigger trigger, IEvent evt)
    {
        // Create context
        var context = new ActionContext
        {
            TriggerId = trigger.Id,
            Platform = evt.Platform,
            Username = evt.GetUsername(),
            CancellationToken = _cancellationTokenSource.Token
        };

        // Populate variables from event
        PopulateContextFromEvent(context, evt);

        // Execute actions
        await _actionDispatcher.ExecuteActionsAsync(trigger.Actions, context);
    }

    private void PopulateContextFromEvent(ActionContext context, IEvent evt)
    {
        switch (evt)
        {
            case TwitchChatMessageEvent chatEvt:
                context.SetVariable("username", chatEvt.Username);
                context.SetVariable("message", chatEvt.Message);
                context.SetVariable("is_moderator", chatEvt.IsModerator);
                context.SetVariable("is_subscriber", chatEvt.IsSubscriber);
                context.SetVariable("is_vip", chatEvt.IsVip);
                break;

            case TwitchSubscriptionEvent subEvt:
                context.SetVariable("username", subEvt.Username);
                context.SetVariable("subscriber_months", subEvt.Months);
                context.SetVariable("tier", subEvt.Tier);
                context.SetVariable("is_gift", subEvt.IsGift);
                break;

            case TwitchRaidEvent raidEvt:
                context.SetVariable("username", raidEvt.FromUsername);
                context.SetVariable("raid_viewer_count", raidEvt.ViewerCount);
                break;

            // More event types...
        }

        // Common variables
        context.SetVariable("timestamp", DateTime.Now);
        context.SetVariable("trigger_id", context.TriggerId.ToString());
    }
}
```

---

### 2. ActionDispatcher Integration

```csharp
public class ActionDispatcher
{
    public async Task ExecuteActionsAsync(List<TriggerAction> actions, ActionContext context)
    {
        _logger.LogInformation(
            "Executing {Count} actions for trigger {TriggerId} (Context: {ContextId})",
            actions.Count,
            context.TriggerId,
            context.ContextId
        );

        foreach (var action in actions)
        {
            if (context.CancellationToken.IsCancellationRequested)
            {
                _logger.LogInformation("Action execution cancelled");
                break;
            }

            try
            {
                await ExecuteActionAsync(action, context);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Action execution failed (Context: {ContextId})",
                    context.ContextId
                );
                
                // Store error in context for next actions
                context.SetVariable("last_error", ex.Message);
                
                if (action.StopOnError)
                {
                    break;
                }
            }
        }
    }
}
```

---

### 3. ProcessManager Integration

```csharp
public async Task ExecuteRunProgramActionAsync(RunProgramAction action, ActionContext context)
{
    // Substitute variables in arguments
    var args = SubstituteVariables(action.Arguments, context);

    var startInfo = new ProcessStartInfo
    {
        FileName = action.ExecutablePath,
        Arguments = args,
        UseShellExecute = false,
        WorkingDirectory = Path.GetDirectoryName(action.ExecutablePath)
    };

    // Start process with cancellation token from context
    var process = await _processManager.StartProcessAsync(
        startInfo, 
        context.CancellationToken
    );

    // Store PID in context
    context.SetVariable("last_process_id", process.Id);
    context.SetVariable("last_process_name", process.ProcessName);

    if (action.WaitForExit)
    {
        await process.WaitForExitAsync(context.CancellationToken);
        context.SetVariable("last_process_exit_code", process.ExitCode);
    }
}
```

---

## Design Patterns

### 1. Builder Pattern (via Object Initializer)

```csharp
var context = new ActionContext
{
    TriggerId = trigger.Id,       // Required
    Platform = "Twitch",          // Optional
    Username = "JohnDoe",         // Optional
    CancellationToken = token     // Optional
};
```

**Benefits:**
- Fluent syntax
- Named parameters
- Compile-time enforcement of required properties

---

### 2. Context Object Pattern

```
ActionContext is a classic context object:
├─ Carries data across layers
├─ Encapsulates execution state
├─ Provides helper methods
└─ Avoids parameter bloat
```

**Before (parameter hell):**
```csharp
ExecuteAction(triggerId, username, platform, variables, cancellationToken, ...)
```

**After (clean):**
```csharp
ExecuteAction(context)
```

---

### 3. Dictionary as State Bag

```
Variables dictionary acts as a state bag:
├─ Flexible schema (any key-value pairs)
├─ Shared state across actions
├─ Type-safe access via GetVariable<T>
└─ No predefined structure required
```

---

## Best Practices

### 1. Always Provide TriggerId

✅ **Good:**
```csharp
var context = new ActionContext
{
    TriggerId = trigger.Id  // Required by compiler
};
```

❌ **Bad (doesn't compile):**
```csharp
var context = new ActionContext { };  // ❌ Error: TriggerId required
```

---

### 2. Use Descriptive Variable Names

✅ **Good:**
```csharp
context.SetVariable("subscriber_months", 12);
context.SetVariable("raid_viewer_count", 250);
context.SetVariable("last_process_exit_code", 0);
```

❌ **Bad:**
```csharp
context.SetVariable("m", 12);  // What is 'm'?
context.SetVariable("count", 250);  // Count of what?
context.SetVariable("code", 0);  // Exit code? Error code?
```

---

### 3. Check for Null Before Using Variables

✅ **Good:**
```csharp
var username = context.GetVariable<string>("username") ?? "Unknown";
var months = context.GetVariable<int>("subscriber_months");
if (months == 0) months = 1;  // Default to 1
```

❌ **Bad:**
```csharp
var username = context.GetVariable<string>("username");
Console.WriteLine($"Hello {username}!");  // NullReferenceException if not set
```

---

### 4. Document Expected Variables

✅ **Good:**
```csharp
/// <summary>
/// Executes the send message action.
/// Requires variables: username (string), message (string)
/// Optional variables: platform (string)
/// </summary>
public async Task ExecuteAsync(ActionContext context)
{
    var username = context.GetVariable<string>("username") ?? throw new InvalidOperationException("username required");
    var message = context.GetVariable<string>("message") ?? throw new InvalidOperationException("message required");
    var platform = context.GetVariable<string>("platform") ?? context.Platform ?? "Unknown";
    
    // ...
}
```

---

### 5. Use CancellationToken for Long Operations

✅ **Good:**
```csharp
await Task.Delay(TimeSpan.FromSeconds(10), context.CancellationToken);
await httpClient.GetAsync(url, context.CancellationToken);
await processManager.StartProcessAsync(startInfo, context.CancellationToken);
```

❌ **Bad:**
```csharp
await Task.Delay(TimeSpan.FromSeconds(10));  // Cannot be cancelled
```

---

### 6. Store Action Results in Context

✅ **Good:**
```csharp
// Action 1: Start OBS
var process = await StartOBSAsync();
context.SetVariable("obs_pid", process.Id);

// Action 2 (later): Kill OBS
var obsPid = context.GetVariable<int>("obs_pid");
if (obsPid != 0)
{
    await _processManager.KillProcessAsync(obsPid);
}
```

---

### 7. Use ContextId in Logging

✅ **Good:**
```csharp
_logger.LogInformation(
    "Executing action for context {ContextId} (Trigger: {TriggerId})",
    context.ContextId,
    context.TriggerId
);
```

**Benefits:**
- Trace execution flow across logs
- Correlate actions to triggers
- Debug issues with specific executions

---

## Summary

### Key Features

| Feature | Implementation | Benefit |
|---------|---------------|---------|
| **Unique Identity** | ContextId auto-generated GUID | Trace execution instances |
| **Trigger Linking** | Required TriggerId property | Link actions to triggers |
| **Variable Storage** | Dictionary<string, object> | Share state between actions |
| **Type Safety** | GetVariable<T>() generic method | Prevent runtime cast errors |
| **Cancellation** | CancellationToken property | Graceful termination |
| **Metadata** | Platform and Username properties | Context-aware execution |
| **Immutability** | Init-only setters | Prevent accidental modification |

### When to Use ActionContext

✅ **Use ActionContext for:**
- Executing trigger actions
- Variable substitution in templates
- Storing action results for later actions
- Cancelling long-running operations
- Linking executions back to triggers

❌ **Don't use ActionContext for:**
- Global application state (use services)
- Cross-trigger data sharing (use database)
- User preferences (use profile settings)
- Long-term storage (use persistence layer)

### Performance Characteristics

| Operation | Time Complexity | Notes |
|-----------|----------------|-------|
| Context creation | O(1) | Lightweight object |
| SetVariable | O(1) | Dictionary insert |
| GetVariable | O(1) | Dictionary lookup |
| Variable substitution | O(n) | n = template length |

### Memory Footprint

- **Empty context**: ~200 bytes
- **Per variable**: ~50-100 bytes (depends on value type)
- **Typical usage**: 10-20 variables = ~1-2 KB per context
- **Lifetime**: Scoped to single trigger execution (short-lived)

---

**End of Action Context Model Documentation**

**Related Documentation:**
- [Trigger Engine](../services/trigger-engine.md) - Creates ActionContext
- [Action Dispatcher](../services/action-dispatcher.md) - Consumes ActionContext
- [Process Management](../services/process-management.md) - Uses CancellationToken from context
- [Entity Models](./entities.md) - Trigger and Action entities

**Last Updated:** 2024-01-15  
**Lines of Code:** 60  
**Dependencies:** None (pure data class)  
**Thread Safety:** ⚠️ Dictionary is not thread-safe (actions run sequentially)