# Twitch Integration

## Overview
Custom Twitch integration using LightweightTwitchIrc (custom WebSocket IRC client) for chat and TwitchLib.EventSub.Websockets for EventSub event subscriptions. OAuth uses PKCE flow via TwitchOAuthService.

## Source Files

### ITwitchService Interface
- **File:** `src/VyralBot.Integrations/Twitch/ITwitchService.cs`
- **Namespace:** `VyralBot.Integrations.Twitch`
- **Properties:** `bool IsConnected`, `string? Channel`, `string? BotUsername`
- **Methods:**
  - `ConnectAsync(string username, string accessToken, string channel)`
  - `ConnectAsync(string username, string accessToken, string? refreshToken, string channel)`
  - `DisconnectAsync()`
  - `SendMessageAsync(string message)`

### TwitchService (Implementation)
- **File:** `src/VyralBot.Integrations/Twitch/TwitchService.cs` (~418 lines)
- **Namespace:** `VyralBot.Integrations.Twitch`
- **Class:** `TwitchService : ITwitchService, IDisposable`
- **Constructor params:** `ILogger<TwitchService>`, `ILoggerFactory`, `IEventBus`, `TwitchOAuthService`, `IChatMessageStore?` (optional), `IDataService?` (optional), `ITwitchEventSubService?` (optional)
- **Private fields:** `LightweightTwitchIrc? _client`, `string? _channel/_botUsername/_accessToken/_refreshToken`, `bool _disposed/_intentionalDisconnect`, `CancellationTokenSource? _reconnectCts`, `Platform? _platform`
- **ConnectAsync (4-param overload):**
  - Creates `LightweightTwitchIrc` instance
  - Wires up event handlers (Connected, Disconnected, AuthenticationFailed, MessageReceived)
  - Calls `_client.ConnectAsync()` then connects `ITwitchEventSubService` if available
- **Event handling:**
  - `OnConnected` — logging
  - `OnAuthenticationFailed` → `RefreshAndReconnectAsync()` (uses TwitchOAuthService to refresh)
  - `OnDisconnected` → `ReconnectAsync()` with exponential backoff (max 5 retries, up to 30 seconds)
  - `OnMessageReceived` → publishes `TwitchChatMessageEvent` to EventBus, stores in `IChatMessageStore` (respects `TwitchChatEnabled` flag from Platform entity)

### LightweightTwitchIrc (Custom WebSocket IRC Client)
- **File:** `src/VyralBot.Integrations/Twitch/LightweightTwitchIrc.cs` (~377 lines)
- **Namespace:** `VyralBot.Integrations.Twitch`
- **Class:** `sealed class LightweightTwitchIrc : IDisposable`
- **Constructor:** `ILogger<LightweightTwitchIrc>`
- **WebSocket:** `System.Net.WebSockets.ClientWebSocket` to `wss://irc-ws.chat.twitch.tv:443`
- **Public properties:** `string Username`, `string Channel`, `bool IsConnected`
- **Public events:**
  - `MessageReceived` (`TwitchMessageEventArgs` — Username, Message, Channel, IsModerator, IsSubscriber, IsVip, IsBroadcaster, Badges dictionary)
  - `Connected`
  - `Disconnected`
  - `AuthenticationFailed`
- **Public methods:**
  - `ConnectAsync(string username, string oauthToken, string channel)` — sends PASS/NICK/CAP REQ (tags,commands)/JOIN, starts read + keepalive tasks
  - `SendMessageAsync(string message)` — sends PRIVMSG
  - `Disconnect()` — cancels tasks, disposes
  - `Dispose()` — closes WebSocket gracefully
- **Internal:** `ReadMessagesAsync()` loop using `ArrayPool<byte>`, `ProcessIrcMessage()` parses IRC tags/badges/PRIVMSG, `KeepAliveAsync()` sends PING every 60 seconds

### ITwitchEventSubService Interface
- **File:** `src/VyralBot.Integrations/Twitch/ITwitchEventSubService.cs`
- **Namespace:** `VyralBot.Integrations.Twitch`
- **Methods:** `ConnectAsync(string clientId, string accessToken, string userId)`, `DisconnectAsync()`
- **Implements:** `IDisposable`

### TwitchEventSubService (Implementation)
- **File:** `src/VyralBot.Integrations/Twitch/TwitchEventSubService.cs` (~338 lines)
- **Namespace:** `VyralBot.Integrations.Twitch`
- **Class:** `TwitchEventSubService : ITwitchEventSubService`
- **Constructor params:** `ILogger<TwitchEventSubService>`, `IEventBus`, `IDataService`, `EventSubWebsocketClient` (from TwitchLib.EventSub.Websockets)
- **Private fields:** `TwitchAPI _twitchApi`, `bool _isConnected`, `string? _userId`, `string? _accessToken`
- **EventSub channel subscriptions (via `SubscribeToEventsAsync()`):**
  - `channel.follow` (v2)
  - `channel.subscribe` (v1)
  - `channel.subscription.message` (v1)
  - `channel.subscription.gift` (v1)
  - `channel.raid` (v1)
  - `channel.cheer` (v1)
- **Event handlers → EventBus:**
  - `OnChannelFollow` → `TwitchFollowEvent` (also updates `twitch-followercount` variable)
  - `OnChannelSubscribe` → `TwitchSubscribeEvent` (also updates `twitch-subscribercount` variable)
  - `OnChannelSubscriptionMessage` → `TwitchResubEvent`
  - `OnChannelSubscriptionGift` → `TwitchGiftSubEvent`
  - `OnChannelRaid` → `TwitchRaidEvent`
  - `OnChannelCheer` → `TwitchCheerEvent`
- **Reconnection:** `OnWebsocketDisconnected` → `ReconnectAsync()` after 2-second delay

### TwitchOAuthService
- **File:** `src/VyralBot.Integrations/Twitch/TwitchOAuthService.cs`
- **Namespace:** `VyralBot.Integrations.Twitch`
- **Class:** `TwitchOAuthService`
- **Constructor params:** `ILogger<TwitchOAuthService>`, `IDataService`, `IProfileService`
- **Uses PKCE OAuth flow**
- **Key methods:**
  - `AuthenticateAsync()` — full OAuth PKCE flow
  - `RefreshTokenAsync(string refreshToken)` — refreshes expired tokens
  - Token validation and storage

### Twitch Events (defined in Integrations project)
- **File:** `src/VyralBot.Integrations/Twitch/Events/` (one file per event)
- All extend `EventBase` (which implements `IEvent`)
- Events:
  - `TwitchChatMessageEvent` — Username, Message, Channel, IsModerator, IsSubscriber, IsVip, IsBroadcaster, Badges
  - `TwitchFollowEvent` — Username, UserId
  - `TwitchSubscribeEvent` — Username, Tier, IsGift
  - `TwitchResubEvent` — Username, Tier, Months, Message
  - `TwitchGiftSubEvent` — GifterUsername, RecipientUsername, Tier, Total
  - `TwitchRaidEvent` — FromUsername, ViewerCount
  - `TwitchCheerEvent` — Username, Bits, Message
  - `TwitchChannelPointsEvent` — Username, RewardTitle, RewardCost, UserInput
  - `TwitchChannelPointsRedeemEvent` — Username, RewardTitle, RewardId, UserInput, Status

### Connection UI
- **PlatformsViewModel** handles Twitch connection in `src/VyralBot.UI/ViewModels/PlatformsViewModel.cs`
  - Injects `ITwitchService`, `TwitchOAuthService`, `TwitchWebSetupViewModel`
  - `TwitchWebSetupViewModel` manages the OAuth browser flow

## Architecture
```
LightweightTwitchIrc (Custom WebSocket)  ──→  wss://irc-ws.chat.twitch.tv
  └── PRIVMSG parsing, badges, IRC tags
        ↓
TwitchService (ITwitchService impl)
  ├── Creates/manages LightweightTwitchIrc instance
  ├── Auto-reconnect with exponential backoff
  ├── Auth refresh via TwitchOAuthService
  ├── Publishes TwitchChatMessageEvent to EventBus
  └── Stores messages in IChatMessageStore

TwitchLib.EventSub.Websockets (NuGet)
        ↓
TwitchEventSubService (ITwitchEventSubService impl)
  ├── Subscribes to 6 EventSub event types
  ├── Publishes typed events to EventBus
  └── Auto-reconnect on disconnect

TwitchOAuthService
  └── PKCE OAuth flow, token refresh
```
