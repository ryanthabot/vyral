# Event System

## Overview
Channel-based event bus using `System.Threading.Channels` for decoupled communication between platform integrations, services, and UI. All platform events flow through the EventBus as strongly-typed records extending `EventBase`.

---

## Core Source Files

### IEvent Interface
- **File:** `src/VyralBot.Core/Services/EventBus/IEvent.cs`
- **Namespace:** `VyralBot.Core.Services.EventBus`
- **Interface `IEvent`:** `Guid EventId`, `DateTime Timestamp`, `string Source`, `Dictionary<string, object>? Metadata`
- **Abstract record `EventBase : IEvent`:** Provides default implementations (new Guid, DateTime.UtcNow, empty string, null)

### IEventBus Interface
- **File:** `src/VyralBot.Core/Services/EventBus/IEventBus.cs`
- **Namespace:** `VyralBot.Core.Services.EventBus`
- **Methods:**
  - `Task PublishAsync<TEvent>(TEvent evt) where TEvent : IEvent`
  - `Task<Guid> SubscribeAsync<TEvent>(Func<TEvent, Task> handler) where TEvent : IEvent` — returns subscription Guid
  - `void Unsubscribe(Guid subscriptionId)`
  - `int SubscriptionCount { get; }`

### EventBus Implementation
- **File:** `src/VyralBot.Core/Services/EventBus/EventBus.cs` (~205 lines)
- **Namespace:** `VyralBot.Core.Services.EventBus`
- **Class:** `EventBus : IEventBus, IDisposable`
- **Constructor:** `ILogger<EventBus>`
- **Channel:** `Channel.CreateBounded<(IEvent, Type)>` with capacity **500**, `BoundedChannelFullMode.DropOldest`, `SingleReader=true`, `SingleWriter=false`
- **Throttling:** `SemaphoreSlim(4, 4)` — max **4 concurrent handlers**
- **Subscriptions storage:** `ConcurrentDictionary<Type, ConcurrentDictionary<Guid, Delegate>>`
- **PublishAsync:** Write `(event, typeof(TEvent))` to channel (non-blocking, fire-and-forget write)
- **SubscribeAsync<TEvent>:** Creates new Guid, stores handler delegate keyed by event type → Guid
- **Unsubscribe:** Iterates all type dictionaries, removes matching Guid
- **ProcessEventsAsync:** Background `Task.Run` started in constructor, reads from channel via `ReadAllAsync`
- **ProcessEventAsync:** For each event, snapshots all matching handlers, dispatches each via `Task.Run` + `DynamicInvoke` guarded by SemaphoreSlim, awaits all with `Task.WhenAll`
- **Dispose:** Cancels CancellationTokenSource, completes channel writer, waits up to 5 seconds for processing task

---

## All Event Types

### Core Events (in VyralBot.Core)
**File:** `src/VyralBot.Core/Services/EventBus/ProfileSwitchedEvent.cs`
- `record ProfileSwitchedEvent : EventBase` — `string OldProfileName`, `string NewProfileName`
- `record PlatformChatRoutingChangedEvent : EventBase` — `string Platform`, `bool Enabled`
- `record PlatformConnectionChangedEvent : EventBase` — `string Platform`, `bool Connected`, `string? Channel`, `string? BotUsername`

### Kick Events (in VyralBot.Integrations)
**Directory:** `src/VyralBot.Integrations/Kick/Events/`

| Event Record | Key Properties |
|---|---|
| `KickChatMessageEvent` | Username, Message, Channel, ChatroomId, MessageId, Badges, IsModerator, IsSubscriber, IsOwner |
| `KickFollowEvent` | Username, FollowersCount |
| `KickSubscribeEvent` | Username, Months |
| `KickGiftSubEvent` | GifterUsername, GiftedUsernames (list), Count |
| `KickHostEvent` | HostUsername, ViewerCount |
| `KickKicksGiftedEvent` | GifterUsername, Amount |
| `KickRewardRedeemedEvent` | Username, RewardTitle, RewardCost |
| `KickStreamStatusEvent` | IsLive, Title, ViewerCount |

### Twitch Events (in VyralBot.Integrations)
**Directory:** `src/VyralBot.Integrations/Twitch/Events/`

| Event Record | Key Properties |
|---|---|
| `TwitchChatMessageEvent` | Username, Message, Channel, IsModerator, IsSubscriber, IsVip, IsBroadcaster, Badges |
| `TwitchFollowEvent` | Username, UserId |
| `TwitchSubscribeEvent` | Username, Tier, IsGift |
| `TwitchResubEvent` | Username, Tier, Months, Message |
| `TwitchGiftSubEvent` | GifterUsername, RecipientUsername, Tier, Total |
| `TwitchRaidEvent` | FromUsername, ViewerCount |
| `TwitchCheerEvent` | Username, Bits, Message |
| `TwitchChannelPointsRedeemEvent` | Username, RewardTitle, RewardId, UserInput, Status |

---

## Event Flow
```
Platform Integration (KickLibChatService / TwitchService / TwitchEventSubService)
  → EventBus.PublishAsync<SpecificEvent>()
    → Channel.Writer.TryWrite((event, type))
      → ProcessEventsAsync (background task)
        → Channel.Reader.ReadAllAsync()
          → ProcessEventAsync(event, type)
            → Snapshot handlers matching type
            → Dispatch each via Task.Run + SemaphoreSlim(4)
              → Handler callbacks in subscribers (ChatViewModel, TriggerEngine, etc.)
```

---

## Subscription Pattern
```csharp
// Subscribe - returns Guid for later unsubscription
_chatSubId = await _eventBus.SubscribeAsync<TwitchChatMessageEvent>(OnTwitchChatMessage);

// Handler
private async Task OnTwitchChatMessage(TwitchChatMessageEvent evt)
{
    // Process the event
}

// Unsubscribe (typically in cleanup/dispose)
_eventBus.Unsubscribe(_chatSubId);
```

---

## Key Design Decisions
- **Bounded channel (500)** with **DropOldest** prevents memory leaks under high event volume
- **SemaphoreSlim(4)** throttles concurrent handler execution to prevent thread pool exhaustion
- **Guid-based subscriptions** allow precise unsubscription without lambda reference issues
- **DynamicInvoke** used for type-erased handler invocation from the `Delegate` storage
- All events are **immutable records** extending `EventBase`
- Events are **fire-and-forget** published (non-blocking write to channel)
