# Event Bus

> The in-process EventBus is Vyral's nervous system: one shared, typed pub/sub channel that carries every platform message and event from the integrations (and the embedded web server) to the UI and feature services.

## Overview

`EventBus` (`src/VyralBot.Core/Services/EventBus/EventBus.cs`) is built on `System.Threading.Channels` with deliberately conservative constants:

- **Bounded capacity 500, `DropOldest`** — under burst, the oldest queued events are silently discarded. Event loss under load is by design, not a bug.
- **Maximum 4 concurrent handlers** — a `SemaphoreSlim(4)` throttles how many handler invocations run at once (the class doc cites this as a project requirement).
- **Single reader** — one background task drains the channel; writes are non-blocking.
- **Exact-type matching** — subscriptions are keyed by the concrete CLR event type. Publishing a derived type does not trigger handlers registered for a base type (and vice versa), because dispatch looks up `typeof(TEvent)` passed at publish time in a `ConcurrentDictionary<Type, ...>`.
- **One shared instance** — registered once as a DI singleton in the WPF host, and *pulled into the embedded Kestrel container* by `WebServerService` (`WebServerService.cs:62`). This is the reason a Kick webhook arriving over HTTP and a Twitch message arriving over WebSocket hit the exact same handlers with the same side effects.

Handler invocation is `DynamicInvoke` inside `Task.Run`, with per-handler exception isolation — one throwing handler does not kill the others, and the drain loop keeps running.

Related pages: [Web Server](web-server.md) for the HTTP front door into the bus, [Startup & Lifecycle](startup-lifecycle.md) for why subscription order matters, [Chat Settings](../chat/chat-settings.md) and [Event Reactions](../chat/event-reactions.md) for what the main consumer does with events.

## Flow

```
Twitch IRC WebSocket ─────┐
Twitch EventSub WS ───────┤   typed events        ┌─► ChatViewModel (~20 subscriptions: feed,
Kick Pusher WebSocket ────┼─► EventBus ───────────┤     sounds, alerts, currency, auto-replies,
YouTube polling loop ─────┤   (Channel<(IEvent,Type)>,  │     statistics, command pipeline)
Discord.Net gateway ──────┤    500 cap, DropOldest,    ├─► TriggerEngine (stub — subscribes to
ngrok → Kestrel →         │    1 reader, ≤4 handlers)  │    everything but does nothing)
  WebhooksController ─────┘                           └─► MainViewModel (ProfileSwitchedEvent)

Also publishing: EventTestingViewModel (synthetic events), ProfileService (ProfileSwitchedEvent),
settings ViewModels (PlatformChatRoutingChangedEvent), chat services (PlatformConnectionChangedEvent),
ChatEventSettingsService (ProfileSwitchedEvent → cache invalidation)
```

### Producers (everything that calls `PublishAsync`)

| Producer | Events |
|---|---|
| `TwitchService` + `LightweightTwitchIrc` | `TwitchChatMessageEvent` and `ChatMessageReceivedEvent` (both published fire-and-forget — a former synchronous publish stalled the IRC read loop) |
| `TwitchEventSubService` | `TwitchFollowEvent`, `TwitchSubscribeEvent`, `TwitchResubEvent`, `TwitchGiftSubEvent`, `TwitchRaidEvent`, `TwitchCheerEvent`, `TwitchChannelPointsEvent`, `TwitchChannelPointsRedeemEvent` |
| `KickLibChatService` (Pusher) | `KickChatMessageEvent` and `ChatMessageReceivedEvent` (MessageId `"kick:{id}"` for dedupe), `KickFollowEvent`, `KickSubscribeEvent`, `KickGiftSubEvent`, `KickHostEvent`, `KickKicksGiftedEvent`, `KickRewardRedeemedEvent`, `KickStreamStatusEvent`, `PlatformConnectionChangedEvent`; some events additionally emit a synthesized bot-authored `KickChatMessageEvent` for chat display |
| `YouTubeChatService` | `YouTubeChatMessageEvent` and `ChatMessageReceivedEvent` (MessageId `"youtube:{id}"`), `PlatformConnectionChangedEvent` |
| `DiscordService` (Discord.Net gateway — N instances, one per managed bot) | `DiscordChatMessageEvent` (guild/channel names, mod/broadcaster flags, **`BotId`** identifying the publishing instance) and `ChatMessageReceivedEvent` (Platform `"Discord"`, MessageId `"discord:{id}"`, no bot id) — both published fire-and-forget — plus `PlatformConnectionChangedEvent` on connect/disconnect |
| `WebhooksController` (Kestrel) | `KickFollowEvent`, `KickSubscribeEvent`, `KickGiftSubEvent`, and synthesized `KickChatMessageEvent`s for unfollows / stream status / moderation / kicks-gifted |
| `EventTestingViewModel` | Any of the above, with realistic synthetic payloads — see [Event Testing](../chat-features/event-testing.md) |
| `ProfileService` | `ProfileSwitchedEvent` (drives hot profile switching) |
| `KickSettingsViewModel`, `TwitchChatSettingsViewModel`, `YoutubeChatSettingsViewModel`, `TiktokChatSettingsViewModel`, `DiscordChatSettingsViewModel` | `PlatformChatRoutingChangedEvent` (live-updates the chat feed's display filter) |
| `ChatEventSettingsService` | `ProfileSwitchedEvent` subscription (invalidates its cache on profile switch) |

Typed event classes live in `src/VyralBot.Integrations/{Twitch,Kick,YouTube,Discord}/Events/`; cross-platform events (`ProfileSwitchedEvent`, `PlatformChatRoutingChangedEvent`, `PlatformConnectionChangedEvent`) live in `src/VyralBot.Core/Services/EventBus/ProfileSwitchedEvent.cs`. The generic `ChatMessageReceivedEvent` lives in [src/VyralBot.Core/Models/ChatMessageReceivedEvent.cs](../../src/VyralBot.Core/Models/ChatMessageReceivedEvent.cs).

### Consumers

| Consumer | What it subscribes to |
|---|---|
| `ChatViewModel` | ~20 subscriptions: all chat-message events, every follow/sub/gift/host/raid/cheer/reward event from Twitch and Kick, `PlatformChatRoutingChangedEvent`, `PlatformConnectionChangedEvent`, and `ChatMessageReceivedEvent` |
| `MainViewModel` | `ProfileSwitchedEvent` (teardown/reload on profile change) |
| `TriggerEngine` | Subscribes to everything but its handler is a TODO stub — currently inert |

### Side-effect variable writes

Some producers also write [variables](../chat-features/variables.md) directly as a side effect of receiving traffic:

- `KickLibChatService.OnFollowersUpdated` sets `kick-followercount` to the absolute count from the event (`KickLibChatService.cs:472`).
- `TwitchEventSubService` *increments* `twitch-followercount` and `twitch-subscribercount` — but only when the variable row already exists, so the counters never bootstrap from zero (`TwitchEventSubService.cs:203-236`).

### The command pipeline event

`ChatMessageReceivedEvent` ([ChatMessageReceivedEvent.cs](../../src/VyralBot.Core/Models/ChatMessageReceivedEvent.cs)) is the generic "a chat message arrived" event that `ChatViewModel.OnChatMessageReceivedAsync` (`ChatViewModel.cs:592`) consumes. That handler is the orchestrator for the entire command pipeline: `CommandService` (`!commands`), currency/giveaway/poll/watchtime chat commands, watchtime activity tracking, and chat-activity currency.

Since the 2026-08-15 fix, **all chat integrations publish it** — `TwitchService.OnMessageReceived`, `KickLibChatService.OnChatMessage` (MessageId `"kick:{id}"`), the `YouTubeChatService` polling loop (`"youtube:{id}"`), and `DiscordService.OnMessageReceivedAsync` (`"discord:{id}"`, Platform `"Discord"`) — fire-and-forget alongside their typed events. The consumer dedupes by `MessageId` (Kick's Pusher + webhook double delivery is safe) and skips the bot's own echoed messages. This was previously the single biggest wiring gap in the app (KI-1, resolved); see the [known issues ledger](../reference/known-issues.md) and [Commands](../chat-features/commands.md).

**Per-origin Discord routing.** With multiple Discord bots (2026-08-16), each `DiscordService` tags its typed `DiscordChatMessageEvent` with its `BotId` (the `DiscordBots` row id, or `"primary"` for the setup wizard's singleton), but the generic `ChatMessageReceivedEvent` carries no origin. `ChatViewModel` bridges the two: the typed handler records message-id→bot-id in a bounded map *before* anything else, and the generic handler looks the origin up by `MessageId` to route the reply through the receiving bot and to apply per-bot command filtering (`CommandContext.DiscordBotId`). Because both events are published concurrently, the generic handler can rarely lose that race and falls back to the Primary bot — a safe degradation (KI-38).

## Files & Code

| File | Role |
|---|---|
| [EventBus.cs](../../src/VyralBot.Core/Services/EventBus/EventBus.cs) | The channel, subscription dictionary, throttled dispatch, dispose |
| [IEventBus.cs](../../src/VyralBot.Core/Services/EventBus/IEventBus.cs) | The `IEventBus` contract consumed everywhere |
| [IEvent.cs](../../src/VyralBot.Core/Services/EventBus/IEvent.cs) | Marker interface + `EventBase` all events derive from |
| [ProfileSwitchedEvent.cs](../../src/VyralBot.Core/Services/EventBus/ProfileSwitchedEvent.cs) | Cross-platform events: profile switch, chat routing, connection changed |
| [ChatViewModel.cs](../../src/VyralBot.UI/ViewModels/ChatViewModel.cs) | The main consumer (~20 subscriptions), including the command pipeline fed by `ChatMessageReceivedEvent` |
| [WebhooksController.cs](../../src/VyralBot.Api/Controllers/WebhooksController.cs) | HTTP front door that publishes Kick events onto the bus |
| [EventTestingViewModel.cs](../../src/VyralBot.UI/ViewModels/EventTestingViewModel.cs) | Publishes synthetic events for testing every downstream reaction |

Using the bus — the pattern every producer and consumer follows:

```csharp
// Defining an event: a record deriving from EventBase
public record TwitchCheerEvent : EventBase
{
    public string Username { get; init; } = "";
    public int Bits { get; init; }
}

// Publishing (producer side — fire-and-forget is fine; the channel write is non-blocking)
await _eventBus.PublishAsync(new TwitchCheerEvent
{
    Username = message.Username,
    Bits = bits,
    Source = "TwitchEventSub"
});

// Subscribing (consumer side — keep the Guid if you ever need to unsubscribe)
_subscriptionId = await _eventBus.SubscribeAsync<TwitchCheerEvent>(OnCheerAsync);
...
_eventBus.Unsubscribe(_subscriptionId.Value);
```

The core constants and the bounded channel:

```csharp
// EventBus.cs
private const int MaxConcurrentHandlers = 4;
private const int ChannelCapacity = 500;

_eventChannel = Channel.CreateBounded<(IEvent, Type)>(new BoundedChannelOptions(ChannelCapacity)
{
    FullMode = BoundedChannelFullMode.DropOldest,  // events can be lost under burst
    SingleReader = true,
    SingleWriter = false
});
_handlerSemaphore = new SemaphoreSlim(MaxConcurrentHandlers, MaxConcurrentHandlers);
```

Exact-type dispatch — only handlers registered under `typeof(TEvent)` run:

```csharp
// EventBus.cs — ProcessEventAsync
if (!_subscriptions.TryGetValue(eventType, out var typeSubscriptions) || typeSubscriptions.IsEmpty)
    return;   // no base-class walk, no interface matching — exact CLR type only

foreach (var (subscriptionId, handler) in handlers)
{
    await _handlerSemaphore.WaitAsync(_cts.Token);   // max 4 in flight
    var task = Task.Run(async () =>
    {
        try { await (Task)handler.DynamicInvoke(eventInstance)!; }
        catch (Exception ex) { _logger.LogError(ex, "..."); }   // isolated per handler
        finally { _handlerSemaphore.Release(); }
    }, _cts.Token);
}
```

## Gotchas

- **Events can be silently dropped.** With 500 capacity and DropOldest, a burst faster than four handlers can drain will lose the oldest events — sounds/alerts/currency for those events never fire. There is no drop counter or warning surfaced anywhere.
- **`ChatMessageReceivedEvent` is published by all four chat integrations** (Twitch, Kick, YouTube, and Discord — KI-1 resolved 2026-08-15); the command pipeline it feeds is live. On Discord the typed twin event carries the `BotId` origin; the generic one does not, and the message-id bridge in ChatViewModel can rarely lose the race (KI-38).
- **Exact-type matching cuts both ways** — adding a new event subtype without also publishing/subscribing it explicitly produces silently dead wiring, the same failure mode as the generic event.
- **Handler order is not guaranteed.** Handlers run as concurrent `Task.Run` work items; anything requiring ordering across handlers must not rely on subscription order.
- **`DynamicInvoke` reflection overhead** per handler is acceptable at chat scale but is not free; hot paths feel it at high message rates.
- **Dispose cancels in-flight work** — `Dispose()` cancels the CTS and waits only 5 seconds for the drain task; events still queued at shutdown are lost.
