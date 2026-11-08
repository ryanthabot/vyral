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
  - `TimeoutUserAsync(string userId, TimeSpan duration, string reason)` → `Task<bool>` — Kick API v1 `POST /v1/moderation/bans` (temporary), scope `moderation:ban`; durations round up to whole minutes (Kick is minute-granular, 1 min to 7 days)
  - `BanUserAsync(string userId, string reason)` → `Task<bool>` — Kick API v1 `POST /v1/moderation/bans` (permanent), scope `moderation:ban`
  - `DeleteMessageAsync(string messageId)` → `Task<bool>` — Kick API v1 `DELETE /v1/channels/chat/messages/{id}`, scope `moderation:chat_message:manage` (tokens granted before Wave 9 need one re-auth)

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
  - `TimeoutUserAsync/BanUserAsync/DeleteMessageAsync` (Wave 9) — best-effort moderation via `KickApi.Moderation` / `KickApi.Chat` (never throw, return false + log on failure); shared `EnsureFreshAccessTokenAsync` refreshes expiring tokens first, reasons truncate to 100 chars
  - `Dispose()` — unsubscribes events, disconnects

### Event Handler Mappings
| KickLib Event | Handler | EventBus Event Published |
|---|---|---|
| `OnChatMessage` | Parses message (includes raw Pusher `MessageId` since Wave 9 — the auto-mod delete path needs it) | `KickChatMessageEvent` + IChatMessageStore |
| `OnSubscription` | Sub notification (cross-transport dedup via `KickEventRouting.TryBeginEvent("sub:{user}")` since Wave 10 — skips if the webhook processor already published it) | `KickSubscribeEvent` + chat message |
| `OnGiftedSubscription` | Gift sub notif (dedup key `giftsub:{gifter}` since Wave 10) | `KickGiftSubEvent` + chat message |
| `OnFollowersUpdated` | Follower count | `KickFollowEvent` + updates variable (suppressed while `KickEventRouting.WebhookFollowsActive` — webhooks carry the real username) |
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
