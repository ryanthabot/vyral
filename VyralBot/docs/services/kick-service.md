# Kick Chat Service

## Overview
KickLib-based chat service implementing `IKickService`. Manages WebSocket chat connection and REST API access to Kick via the KickLib NuGet package.

## Source Files

### IKickService Interface
- **File:** `src/VyralBot.Integrations/Kick/IKickService.cs`
- **Namespace:** `VyralBot.Integrations.Kick`
- **Properties:** `bool IsConnected`, `string? Channel`, `string? BotUsername`
- **Methods:**
  - `ConnectAsync(string username, string accessToken, string channelName, int broadcasterUserId)`
  - `DisconnectAsync()`
  - `SendMessageAsync(string message)`

### KickLibChatService Implementation
- **File:** `src/VyralBot.Integrations/Kick/KickLibChatService.cs` (~574 lines)
- **Namespace:** `VyralBot.Integrations.Kick`
- **Class:** `KickLibChatService : IKickService, IDisposable`
- **Constructor:** `ILogger<KickLibChatService>`, `IDataService`, `IEventBus`, `IChatMessageStore?` (optional)
- **KickLib dependencies:** `IKickClient` (WebSocket), `IKickApi` (REST)
- **Private fields:**
  - `IKickClient _kickClient`, `IKickApi _kickApi`
  - `Platform? _platform`, `int _chatroomId`, `int _channelId`
  - `string? _currentChannel`, `string? _botUsername`, `bool _disposed`
- **Public methods:**
  - `ConnectAsync(string username, string accessToken, string channelName, int broadcasterUserId)` — creates KickClient/KickApi, subscribes events, joins chatroom
  - `ConnectAsync(string username, string accessToken, string? refreshToken, string channelName, int broadcasterUserId)` — overload with refresh token
  - `DisconnectAsync()` — leaves chatroom, disconnects client
  - `SendMessageAsync(string message)` — sends via KickApi
  - `Dispose()` — unsubscribes events, disconnects

### Event Handler Mappings
| KickLib Event | Handler | EventBus Event Published |
|---|---|---|
| `OnChatMessage` | Parses message | `KickChatMessageEvent` + IChatMessageStore |
| `OnSubscription` | Sub notification | `KickSubscribeEvent` + chat message |
| `OnGiftedSubscription` | Gift sub notif | `KickGiftSubEvent` + chat message |
| `OnFollowersUpdated` | Follower count | `KickFollowEvent` + updates variable |
| `OnRewardRedeemed` | Reward redemption | `KickRewardRedeemedEvent` |
| `OnKicksGifted` | Kicks gifted | `KickKicksGiftedEvent` |
| `OnStreamStatusChanged` | Stream go-live | Chat message only |
| `OnStreamHost` | Host event | Chat message only |
| `OnUserBanned/Unbanned` | Moderation | Logging only |
| `OnMessageDeleted` | Message deleted | Logging only |
| `OnConnected/Disconnected` | Connection | Logging only |

## All Files
| File | Description |
|------|-------------|
| `src/VyralBot.Integrations/Kick/IKickService.cs` | Interface |
| `src/VyralBot.Integrations/Kick/KickLibChatService.cs` | Implementation (574 lines) |
| `src/VyralBot.Integrations/Kick/Events/` | 8 event record files |
