# Trigger & Action Entity Documentation

## Overview

**Files:**
- `src/VyralBot.Data/Entities/Trigger.cs` (50 lines)
- `src/VyralBot.Data/Entities/BotAction.cs` (40 lines)
- `src/VyralBot.Data/Entities/EventLog.cs` (30 lines)

**Namespace:** `VyralBot.Data.Entities`  
**Pattern:** Entity Framework Core / Dapper POCO (Plain Old CLR Object) models  
**Database:** SQLite (one database per profile)  
**Purpose:** Define trigger → action automation system with event logging

---

## System Architecture

### Trigger-Action Flow Diagram

```
1. Platform Event Occurs (e.g., User follows on Twitch)
   ↓
2. Platform Service Publishes Event to EventBus
   ↓
3. TriggerEngine Receives Event
   ↓
4. TriggerEngine Queries Active Triggers from Database
   ↓
5. Match Event Against Trigger Conditions
   ├── EventType matches?
   ├── Platform matches? (or null for any)
   ├── Condition expression evaluates to true?
   ├── Permission check passes?
   └── Cooldown expired?
   ↓
6. IF MATCH → Increment FireCount, Update LastFiredAt
   ↓
7. Parse ActionSequence JSON (array of action IDs)
   ↓
8. Load BotAction Entities from Database
   ↓
9. ActionDispatcher Executes Actions in Sequence
   ├── Action 1: SendMessage "Thanks for the follow, {username}!"
   ├── Action 2: Delay 5 seconds
   └── Action 3: HttpRequest to Discord webhook
   ↓
10. Log Event to EventLog Table
    ├── Platform: "Twitch"
    ├── EventType: "Follow"
    ├── TriggerId: {matched trigger ID}
    └── TriggerFired: true
```

---

## Trigger Entity

### Class Definition

```csharp
/// <summary>
/// Represents a trigger that fires when events match conditions.
/// </summary>
public class Trigger
{
    public Guid Id { get; set; } = Guid.NewGuid();
    
    public required string Name { get; set; }
    
    public string? Description { get; set; }
    
    public bool Enabled { get; set; } = true;
    
    /// <summary>
    /// Event type to match (e.g., "ChatMessage", "Follow", "Sub").
    /// </summary>
    public required string EventType { get; set; }
    
    /// <summary>
    /// Platform filter (e.g., "Twitch", "YouTube", null for any).
    /// </summary>
    public string? Platform { get; set; }
    
    /// <summary>
    /// Condition expression (JSON, regex, or simple comparison).
    /// </summary>
    public string? Condition { get; set; }
    
    /// <summary>
    /// Cooldown in seconds (0 = no cooldown).
    /// </summary>
    public int CooldownSeconds { get; set; }
    
    /// <summary>
    /// Last time this trigger fired.
    /// </summary>
    public DateTime? LastFiredAt { get; set; }
    
    /// <summary>
    /// Total times this trigger has fired.
    /// </summary>
    public int FireCount { get; set; }
    
    /// <summary>
    /// Permission level required (Everyone, Subscriber, Moderator, Broadcaster).
    /// </summary>
    public string Permission { get; set; } = "Everyone";
    
    /// <summary>
    /// JSON array of action IDs to execute.
    /// </summary>
    public required string ActionSequence { get; set; } = "[]";
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
```

---

### Property Documentation

#### Id (Guid)
- **Type:** Unique identifier (GUID)
- **Default:** `Guid.NewGuid()` (auto-generated)
- **Purpose:** Primary key for database
- **Example:** `"3fa85f64-5717-4562-b3fc-2c963f66afa6"`

---

#### Name (required string)
- **Type:** Display name for trigger
- **Required:** Yes
- **Max Length:** Recommended 100 characters
- **Example:** `"Welcome New Followers"`, `"!hello Command"`

**Validation:**
```csharp
if (string.IsNullOrWhiteSpace(trigger.Name))
    throw new ValidationException("Trigger name is required");
```

---

#### Description (string?)
- **Type:** Optional description
- **Nullable:** Yes
- **Purpose:** Help text for users
- **Example:** `"Sends a welcome message when someone follows the channel"`

---

#### Enabled (bool)
- **Type:** Boolean flag
- **Default:** `true`
- **Purpose:** Temporarily disable trigger without deleting
- **UI:** Checkbox in triggers list

**Usage:**
```csharp
var activeTriggers = await _dataService.GetTriggersAsync();
activeTriggers = activeTriggers.Where(t => t.Enabled).ToList();
```

---

#### EventType (required string)
- **Type:** Event discriminator
- **Required:** Yes
- **Purpose:** Match specific event types from platform services

**Supported Event Types:**

| EventType | Platform | Description |
|-----------|----------|-------------|
| `"ChatMessage"` | All | User sends chat message |
| `"Follow"` | Twitch, Kick | User follows channel |
| `"Subscribe"` | Twitch, YouTube | User subscribes/joins membership |
| `"GiftSub"` | Twitch | User gifts subscription |
| `"Raid"` | Twitch, Kick | Another channel raids |
| `"ChannelPoints"` | Twitch | User redeems channel points |
| `"Donation"` | All (via alerts) | User donates money |
| `"ViewerJoin"` | All | User joins stream |
| `"ViewerLeave"` | All | User leaves stream |

**Example Matching:**
```csharp
if (evt.EventType == trigger.EventType)
{
    // Event type matches, continue evaluation
}
```

---

#### Platform (string?)
- **Type:** Platform filter
- **Nullable:** Yes (`null` = match all platforms)
- **Purpose:** Restrict trigger to specific platform

**Valid Values:**
- `"Twitch"` - Only Twitch events
- `"Kick"` - Only Kick events
- `"YouTube"` - Only YouTube events (future)
- `"TikTok"` - Only TikTok events (future)
- `null` - All platforms

**Example:**
```csharp
// Trigger fires for Twitch follows only
trigger.EventType = "Follow";
trigger.Platform = "Twitch";

// Trigger fires for follows on ANY platform
trigger.EventType = "Follow";
trigger.Platform = null;
```

---

#### Condition (string?)
- **Type:** Expression to evaluate
- **Nullable:** Yes (`null` = always true)
- **Format:** JSON, regex, or comparison expression
- **Purpose:** Fine-grained filtering beyond EventType

**Condition Types:**

**1. Simple String Match (Chat Commands):**
```json
{
  "type": "equals",
  "field": "Message",
  "value": "!hello"
}
```

**2. Regex Pattern:**
```json
{
  "type": "regex",
  "field": "Message",
  "pattern": "^!hello\\s+(.+)$"
}
```

**3. User-Specific:**
```json
{
  "type": "equals",
  "field": "Username",
  "value": "specific_user"
}
```

**4. Message Contains:**
```json
{
  "type": "contains",
  "field": "Message",
  "value": "twitch.tv/",
  "caseSensitive": false
}
```

**5. Channel Points Reward:**
```json
{
  "type": "equals",
  "field": "RewardId",
  "value": "abc-123-def-456"
}
```

**6. Complex Condition (AND/OR):**
```json
{
  "type": "and",
  "conditions": [
    {
      "type": "equals",
      "field": "EventType",
      "value": "ChatMessage"
    },
    {
      "type": "regex",
      "field": "Message",
      "pattern": "^!song"
    }
  ]
}
```

**Evaluation Logic (TriggerEngine):**
```csharp
private bool EvaluateCondition(Trigger trigger, EventBase evt)
{
    if (string.IsNullOrEmpty(trigger.Condition))
        return true;  // No condition = always true
    
    var condition = JsonSerializer.Deserialize<ConditionExpression>(trigger.Condition);
    return condition.Evaluate(evt);
}
```

---

#### CooldownSeconds (int)
- **Type:** Integer (seconds)
- **Default:** `0` (no cooldown)
- **Purpose:** Prevent trigger spam
- **Range:** 0 to 86400 (24 hours)

**Examples:**
- `0` - No cooldown (fire every time)
- `5` - 5-second cooldown between fires
- `60` - 1-minute cooldown
- `3600` - 1-hour cooldown

**Cooldown Check Logic:**
```csharp
if (trigger.CooldownSeconds > 0 && trigger.LastFiredAt.HasValue)
{
    var elapsed = DateTime.UtcNow - trigger.LastFiredAt.Value;
    if (elapsed.TotalSeconds < trigger.CooldownSeconds)
    {
        _logger.LogDebug("Trigger {Name} on cooldown for {Remaining}s", 
            trigger.Name, trigger.CooldownSeconds - elapsed.TotalSeconds);
        return false;  // Still on cooldown
    }
}
```

**Use Cases:**
- Chat command rate limiting (prevent spam)
- Expensive actions (API calls, HTTP requests)
- User experience (don't overwhelm chat with bot responses)

---

#### LastFiredAt (DateTime?)
- **Type:** UTC timestamp
- **Nullable:** Yes (null = never fired)
- **Purpose:** Track last execution for cooldown calculation
- **Updated:** Automatically by TriggerEngine when trigger fires

**Update Logic:**
```csharp
trigger.LastFiredAt = DateTime.UtcNow;
trigger.FireCount++;
await _dataService.UpdateTriggerAsync(trigger);
```

---

#### FireCount (int)
- **Type:** Integer counter
- **Default:** `0`
- **Purpose:** Statistics and diagnostics
- **Incremented:** Each time trigger successfully fires

**Usage:**
```csharp
// Display in UI: "This trigger has fired 47 times"
// Analytics: Most popular triggers
// Debugging: Verify trigger is firing as expected
```

---

#### Permission (string)
- **Type:** Permission level
- **Default:** `"Everyone"`
- **Purpose:** Role-based access control for triggers

**Valid Values:**

| Permission | Description | Use Case |
|-----------|-------------|----------|
| `"Everyone"` | Any viewer | General chat commands, public interactions |
| `"Subscriber"` | Subscribers only | Subscriber-exclusive commands |
| `"VIP"` | VIPs only | Special viewer perks |
| `"Moderator"` | Moderators only | Moderation commands |
| `"Broadcaster"` | Streamer only | Admin commands |

**Permission Check Logic:**
```csharp
private bool HasPermission(Trigger trigger, EventBase evt)
{
    if (trigger.Permission == "Everyone")
        return true;
    
    if (evt is TwitchChatMessageEvent twitchEvt)
    {
        return trigger.Permission switch
        {
            "Broadcaster" => twitchEvt.IsBroadcaster,
            "Moderator" => twitchEvt.IsModerator || twitchEvt.IsBroadcaster,
            "VIP" => twitchEvt.IsVip || twitchEvt.IsModerator || twitchEvt.IsBroadcaster,
            "Subscriber" => twitchEvt.IsSubscriber || twitchEvt.IsModerator || twitchEvt.IsBroadcaster,
            _ => false
        };
    }
    
    return false;
}
```

**Example:**
```csharp
// Moderator-only command
trigger.Name = "!timeout Command";
trigger.EventType = "ChatMessage";
trigger.Condition = "{\"type\":\"equals\",\"field\":\"Message\",\"value\":\"!timeout\"}";
trigger.Permission = "Moderator";
```

---

#### ActionSequence (required string)
- **Type:** JSON array of action IDs
- **Required:** Yes
- **Default:** `"[]"` (empty array)
- **Format:** `["action-id-1", "action-id-2", "action-id-3"]`
- **Purpose:** Define sequence of actions to execute when trigger fires

**Example ActionSequence:**
```json
[
  "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "7b3c1d8e-4f9a-4c2b-8a1e-9d7f6e5c4b3a",
  "1a2b3c4d-5e6f-7g8h-9i0j-1k2l3m4n5o6p"
]
```

**Execution Flow:**
```csharp
var actionIds = JsonSerializer.Deserialize<List<Guid>>(trigger.ActionSequence);

foreach (var actionId in actionIds)
{
    var action = await _dataService.GetActionByIdAsync(actionId);
    if (action != null)
    {
        await _actionDispatcher.ExecuteAsync(action, eventContext);
    }
}
```

**Action Chaining Example:**

```
Trigger: User follows
ActionSequence: [
  "send-message-action-id",  // Action 1: Send "Thanks for the follow!"
  "delay-action-id",          // Action 2: Wait 2 seconds
  "http-request-action-id"    // Action 3: POST to Discord webhook
]
```

---

#### CreatedAt / UpdatedAt (DateTime)
- **Type:** UTC timestamp
- **Default:** `DateTime.UtcNow`
- **Purpose:** Audit trail and sorting

**Usage:**
```csharp
// Sort triggers by creation date (newest first)
var sortedTriggers = triggers.OrderByDescending(t => t.CreatedAt);

// Track when trigger was last modified
if (trigger.UpdatedAt > DateTime.UtcNow.AddDays(-7))
{
    // Recently modified (within last week)
}
```

---

## BotAction Entity

### Class Definition

```csharp
/// <summary>
/// Represents an action that can be executed.
/// Named "BotAction" to avoid conflict with System.Action.
/// </summary>
public class BotAction
{
    public Guid Id { get; set; } = Guid.NewGuid();
    
    public required string Name { get; set; }
    
    public string? Description { get; set; }
    
    /// <summary>
    /// Action type (e.g., "SendMessage", "Delay", "HttpRequest", "RunScript").
    /// </summary>
    public required string ActionType { get; set; }
    
    /// <summary>
    /// JSON configuration for the action.
    /// </summary>
    public required string Configuration { get; set; } = "{}";
    
    /// <summary>
    /// Timeout in seconds (0 = use default).
    /// </summary>
    public int TimeoutSeconds { get; set; } = 30;
    
    /// <summary>
    /// Whether to retry on failure.
    /// </summary>
    public bool RetryOnFailure { get; set; }
    
    /// <summary>
    /// Max retry attempts.
    /// </summary>
    public int MaxRetries { get; set; } = 3;
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
```

---

### Property Documentation

#### ActionType (required string)
- **Type:** Action discriminator
- **Required:** Yes
- **Purpose:** Determine execution handler

**Supported Action Types:**

| ActionType | Description | Configuration Example |
|-----------|-------------|---------------------|
| `"SendMessage"` | Send chat message to platform | `{"platform":"Twitch","message":"Hello!"}` |
| `"Delay"` | Wait for specified duration | `{"seconds":5}` |
| `"HttpRequest"` | Make HTTP request | `{"method":"POST","url":"...","body":"..."}` |
| `"RunScript"` | Execute custom script | `{"scriptPath":"scripts/hello.js"}` |
| `"PlaySound"` | Play audio file | `{"filePath":"sounds/alert.mp3"}` |
| `"ShowNotification"` | Display desktop notification | `{"title":"Alert","message":"..."}` |
| `"OpenUrl"` | Open URL in browser | `{"url":"https://..."}` |
| `"ExecuteCommand"` | Run system command | `{"command":"obs.exe","args":"..."}`  |
| `"UpdateVariable"` | Set/modify variable | `{"name":"followerCount","value":"{{event.username}}"}` |
| `"ConditionalAction"` | If/else branching | `{"condition":"...","thenAction":"...","elseAction":"..."}` |

**Dispatcher Routing:**
```csharp
public async Task ExecuteAsync(BotAction action, ActionContext context)
{
    switch (action.ActionType)
    {
        case "SendMessage":
            await ExecuteSendMessageAsync(action, context);
            break;
        case "Delay":
            await ExecuteDelayAsync(action, context);
            break;
        case "HttpRequest":
            await ExecuteHttpRequestAsync(action, context);
            break;
        // ... more cases
    }
}
```

---

#### Configuration (required string)
- **Type:** JSON object
- **Required:** Yes
- **Default:** `"{}"`
- **Purpose:** Action-specific parameters

**Configuration Examples by ActionType:**

**1. SendMessage:**
```json
{
  "platform": "Twitch",
  "message": "Thanks for the follow, {username}!",
  "variables": {
    "username": "{{event.username}}"
  }
}
```

**2. Delay:**
```json
{
  "seconds": 5,
  "description": "Wait 5 seconds before next action"
}
```

**3. HttpRequest:**
```json
{
  "method": "POST",
  "url": "https://discord.com/api/webhooks/123/abc",
  "headers": {
    "Content-Type": "application/json"
  },
  "body": "{\"content\":\"{{event.username}} just followed!\"}"
}
```

**4. RunScript:**
```json
{
  "scriptPath": "scripts/custom_action.js",
  "arguments": {
    "username": "{{event.username}}",
    "platform": "{{event.platform}}"
  }
}
```

**5. PlaySound:**
```json
{
  "filePath": "sounds/follow_alert.mp3",
  "volume": 0.8
}
```

**Variable Substitution:**
- `{{event.username}}` → Replace with event's username property
- `{{event.platform}}` → Replace with event's platform property
- `{{trigger.name}}` → Replace with trigger's name
- `{{random.number(1,100)}}` → Generate random number 1-100

---

#### TimeoutSeconds (int)
- **Type:** Integer (seconds)
- **Default:** `30`
- **Purpose:** Prevent actions from hanging indefinitely

**Timeout Behavior:**
```csharp
var cts = new CancellationTokenSource(TimeSpan.FromSeconds(action.TimeoutSeconds));
try
{
    await ExecuteActionAsync(action, cts.Token);
}
catch (OperationCanceledException)
{
    _logger.LogError("Action {Name} timed out after {Timeout}s", action.Name, action.TimeoutSeconds);
}
```

**Recommended Timeouts:**
- `SendMessage`: 5-10 seconds
- `HttpRequest`: 30 seconds
- `RunScript`: 60 seconds
- `Delay`: No timeout (use delay value)

---

#### RetryOnFailure (bool)
- **Type:** Boolean flag
- **Default:** `false`
- **Purpose:** Automatic retry for transient failures

**Retry Logic:**
```csharp
if (action.RetryOnFailure)
{
    for (int attempt = 0; attempt <= action.MaxRetries; attempt++)
    {
        try
        {
            await ExecuteActionAsync(action, context);
            break;  // Success
        }
        catch (Exception ex)
        {
            if (attempt == action.MaxRetries)
            {
                _logger.LogError(ex, "Action {Name} failed after {Retries} retries", 
                    action.Name, action.MaxRetries);
                throw;
            }
            
            await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)));  // Exponential backoff
        }
    }
}
```

**Use Cases:**
- Network requests (temporary connection failures)
- External API calls (rate limiting)
- File operations (file locked by another process)

---

## EventLog Entity

### Class Definition

```csharp
/// <summary>
/// Log of events received by the system.
/// </summary>
public class EventLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    
    public required string Platform { get; set; }
    
    public required string EventType { get; set; }
    
    public string? Username { get; set; }
    
    public string? UserId { get; set; }
    
    /// <summary>
    /// JSON payload of the event.
    /// </summary>
    public string? Data { get; set; }
    
    /// <summary>
    /// Trigger ID that matched (if any).
    /// </summary>
    public Guid? TriggerId { get; set; }
    
    /// <summary>
    /// Whether a trigger was fired.
    /// </summary>
    public bool TriggerFired { get; set; }
    
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
```

---

### Purpose & Use Cases

**EventLog** provides:
1. **Audit Trail** - Record all events for debugging/analysis
2. **Trigger Diagnostics** - See which triggers fired for which events
3. **Event History** - Replay or analyze past events
4. **Analytics** - Count events by type, platform, user

**Example Queries:**

**Get recent follows:**
```csharp
var recentFollows = await _dataService.QueryAsync<EventLog>(
    "SELECT * FROM EventLog WHERE EventType = 'Follow' ORDER BY Timestamp DESC LIMIT 10");
```

**Count events by platform:**
```csharp
var stats = await _dataService.QueryAsync<dynamic>(
    "SELECT Platform, COUNT(*) as Count FROM EventLog GROUP BY Platform");
```

**Find events that triggered actions:**
```csharp
var triggeredEvents = await _dataService.QueryAsync<EventLog>(
    "SELECT * FROM EventLog WHERE TriggerFired = 1");
```

---

### Data Field (JSON Payload)

**Example: Twitch Follow Event**
```json
{
  "username": "viewer123",
  "userId": "123456789",
  "displayName": "Viewer123",
  "timestamp": "2025-12-02T10:30:00Z"
}
```

**Example: Chat Message Event**
```json
{
  "username": "chatuser",
  "message": "!hello",
  "color": "#FF0000",
  "isModerator": false,
  "isSubscriber": true,
  "badges": ["subscriber/6"]
}
```

**Storage Optimization:**
- Store only essential data (not entire event object)
- Consider retention policy (delete logs older than 30 days)
- Index by Timestamp and EventType for fast queries

---

## Database Schema

### SQLite Table Definitions

**Triggers Table:**
```sql
CREATE TABLE Triggers (
    Id TEXT PRIMARY KEY,
    Name TEXT NOT NULL,
    Description TEXT,
    Enabled INTEGER NOT NULL DEFAULT 1,
    EventType TEXT NOT NULL,
    Platform TEXT,
    Condition TEXT,
    CooldownSeconds INTEGER NOT NULL DEFAULT 0,
    LastFiredAt TEXT,
    FireCount INTEGER NOT NULL DEFAULT 0,
    Permission TEXT NOT NULL DEFAULT 'Everyone',
    ActionSequence TEXT NOT NULL DEFAULT '[]',
    CreatedAt TEXT NOT NULL,
    UpdatedAt TEXT NOT NULL
);

CREATE INDEX idx_triggers_eventtype ON Triggers(EventType);
CREATE INDEX idx_triggers_enabled ON Triggers(Enabled);
CREATE INDEX idx_triggers_platform ON Triggers(Platform);
```

**BotActions Table:**
```sql
CREATE TABLE BotActions (
    Id TEXT PRIMARY KEY,
    Name TEXT NOT NULL,
    Description TEXT,
    ActionType TEXT NOT NULL,
    Configuration TEXT NOT NULL DEFAULT '{}',
    TimeoutSeconds INTEGER NOT NULL DEFAULT 30,
    RetryOnFailure INTEGER NOT NULL DEFAULT 0,
    MaxRetries INTEGER NOT NULL DEFAULT 3,
    CreatedAt TEXT NOT NULL,
    UpdatedAt TEXT NOT NULL
);

CREATE INDEX idx_botactions_actiontype ON BotActions(ActionType);
```

**EventLogs Table:**
```sql
CREATE TABLE EventLogs (
    Id TEXT PRIMARY KEY,
    Platform TEXT NOT NULL,
    EventType TEXT NOT NULL,
    Username TEXT,
    UserId TEXT,
    Data TEXT,
    TriggerId TEXT,
    TriggerFired INTEGER NOT NULL DEFAULT 0,
    Timestamp TEXT NOT NULL
);

CREATE INDEX idx_eventlogs_timestamp ON EventLogs(Timestamp DESC);
CREATE INDEX idx_eventlogs_eventtype ON EventLogs(EventType);
CREATE INDEX idx_eventlogs_platform ON EventLogs(Platform);
CREATE INDEX idx_eventlogs_triggerid ON EventLogs(TriggerId);
```

---

## Example Trigger Scenarios

### Scenario 1: Welcome New Followers

**Trigger:**
```csharp
var trigger = new Trigger
{
    Name = "Welcome New Followers",
    Description = "Send thank you message when someone follows",
    Enabled = true,
    EventType = "Follow",
    Platform = null,  // All platforms
    Condition = null,  // No additional conditions
    CooldownSeconds = 0,  // No cooldown
    Permission = "Everyone",
    ActionSequence = "[\"send-message-action-id\"]"
};
```

**Action:**
```csharp
var action = new BotAction
{
    Id = Guid.Parse("send-message-action-id"),
    Name = "Send Welcome Message",
    ActionType = "SendMessage",
    Configuration = @"{
        ""platform"": ""{{event.platform}}"",
        ""message"": ""Thanks for the follow, {{event.displayName}}! 🎉""
    }",
    TimeoutSeconds = 10,
    RetryOnFailure = true,
    MaxRetries = 3
};
```

**Result:** When user follows on any platform, bot sends personalized thank you message.

---

### Scenario 2: !hello Chat Command

**Trigger:**
```csharp
var trigger = new Trigger
{
    Name = "!hello Command",
    Description = "Respond to !hello in chat",
    Enabled = true,
    EventType = "ChatMessage",
    Platform = "Twitch",
    Condition = @"{
        ""type"": ""equals"",
        ""field"": ""Message"",
        ""value"": ""!hello""
    }",
    CooldownSeconds = 5,  // 5-second cooldown
    Permission = "Everyone",
    ActionSequence = "[\"hello-response-action-id\"]"
};
```

**Action:**
```csharp
var action = new BotAction
{
    Id = Guid.Parse("hello-response-action-id"),
    Name = "Hello Response",
    ActionType = "SendMessage",
    Configuration = @"{
        ""platform"": ""Twitch"",
        ""message"": ""Hello {{event.username}}! Welcome to the stream! 👋""
    }",
    TimeoutSeconds = 5
};
```

---

### Scenario 3: Discord Notification for Subs

**Trigger:**
```csharp
var trigger = new Trigger
{
    Name = "New Subscriber Alert",
    EventType = "Subscribe",
    Platform = "Twitch",
    ActionSequence = "[\"discord-webhook-action-id\", \"chat-thank-you-action-id\"]"
};
```

**Actions:**
```csharp
// Action 1: Discord webhook
var discordAction = new BotAction
{
    Id = Guid.Parse("discord-webhook-action-id"),
    Name = "Send to Discord",
    ActionType = "HttpRequest",
    Configuration = @"{
        ""method"": ""POST"",
        ""url"": ""https://discord.com/api/webhooks/123/abc"",
        ""headers"": {
            ""Content-Type"": ""application/json""
        },
        ""body"": ""{\"content\":\"🎉 {{event.displayName}} just subscribed! (Tier {{event.tier}})\"}""
    }",
    TimeoutSeconds = 30,
    RetryOnFailure = true
};

// Action 2: Chat message
var chatAction = new BotAction
{
    Id = Guid.Parse("chat-thank-you-action-id"),
    Name = "Thank Subscriber",
    ActionType = "SendMessage",
    Configuration = @"{
        ""platform"": ""Twitch"",
        ""message"": ""Thanks for subscribing, {{event.displayName}}! You're amazing! ❤️""
    }"
};
```

---

## Testing Strategy

### Unit Test Examples

**Test: Trigger cooldown enforcement**
```csharp
[Fact]
public async Task TriggerEngine_RespectsCooldown()
{
    var trigger = new Trigger
    {
        EventType = "ChatMessage",
        CooldownSeconds = 10,
        LastFiredAt = DateTime.UtcNow.AddSeconds(-5)
    };
    
    var evt = new TwitchChatMessageEvent { Message = "test" };
    var canFire = await _triggerEngine.CanFireAsync(trigger, evt);
    
    Assert.False(canFire);  // Still on cooldown
}
```

**Test: Permission check**
```csharp
[Fact]
public void Trigger_ChecksModeratorPermission()
{
    var trigger = new Trigger { Permission = "Moderator" };
    var evt = new TwitchChatMessageEvent 
    { 
        IsModerator = false,
        IsSubscriber = true
    };
    
    var hasPermission = CheckPermission(trigger, evt);
    
    Assert.False(hasPermission);  // Subscriber but not moderator
}
```

---

## Summary

**Trigger Entity:**
- Defines WHEN to execute actions (event matching, conditions, cooldowns)
- Supports any platform, any event type
- Permission-based access control
- Cooldown system to prevent spam
- References actions via ActionSequence JSON array

**BotAction Entity:**
- Defines WHAT to execute (action type, configuration)
- Supports multiple action types (messages, HTTP, scripts, delays)
- Configurable timeouts and retry logic
- JSON configuration for flexibility

**EventLog Entity:**
- Records all events for audit trail
- Links events to triggers that fired
- Enables analytics and debugging
- Stores JSON payload for complete event data

**System Flow:**
1. Event occurs on platform → 2. TriggerEngine matches triggers → 3. Loads actions from database → 4. ActionDispatcher executes actions → 5. Logs to EventLog table

**Extension for YouTube:**
- Add YouTube event types: "Subscribe", "Like", "Comment", "MemberJoin"
- No trigger entity changes needed (platform-agnostic)
- Add YouTube action types: "PinComment", "HeartComment", "ReplyToComment"
- Configuration structure remains the same

**Total Entities:** 3 (Trigger, BotAction, EventLog)  
**Total Properties:** 32 across all entities  
**Database Tables:** 3 with indexes  
**JSON Fields:** 3 (Condition, ActionSequence, Configuration, Data)
