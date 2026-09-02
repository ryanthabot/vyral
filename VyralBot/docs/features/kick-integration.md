# Kick Integration

## Overview
KickLib-based Kick chat integration. Uses the KickLib NuGet package for WebSocket chat connection and API access. Replaces the old WebView2-based approach.

## Source Files

### Interface
- **File:** `src/VyralBot.Integrations/Kick/IKickService.cs`
- **Namespace:** `VyralBot.Integrations.Kick`
- **Properties:** `bool IsConnected`, `string? Channel`, `string? BotUsername`
- **Methods:** `ConnectAsync(string username, string accessToken, string channelName, int broadcasterUserId)`, `DisconnectAsync()`, `SendMessageAsync(string message)`

### Implementation: KickLibChatService
- **File:** `src/VyralBot.Integrations/Kick/KickLibChatService.cs` (~574 lines)
- **Namespace:** `VyralBot.Integrations.Kick`
- **Class:** `KickLibChatService : IKickService, IDisposable`
- **Constructor params:** `ILogger<KickLibChatService>`, `IDataService`, `IEventBus`, `IChatMessageStore?` (optional)
- **Fields:** `IKickClient _kickClient`, `IKickApi _kickApi`, `Platform? _platform`, `int _chatroomId`, `int _channelId`, `string? _currentChannel`, `string? _botUsername`, `bool _disposed`
- **Public methods:**
  - `ConnectAsync(string username, string accessToken, string channelName, int broadcasterUserId)` — creates KickClient/KickApi, sets up event subscriptions, joins chatroom
  - `ConnectAsync(string username, string accessToken, string? refreshToken, string channelName, int broadcasterUserId)` — overload with refresh token support
  - `DisconnectAsync()` — leaves chatroom, disconnects
  - `SendMessageAsync(string message)` — sends via KickApi
  - `Dispose()` — unsubscribes events, disconnects

### Event Handler Mappings (KickLib → EventBus)
| KickLib Event | EventBus Event | Notes |
|---|---|---|
| `OnChatMessage` | `KickChatMessageEvent` | Also stores in IChatMessageStore |
| `OnSubscription` | `KickSubscribeEvent` | Also publishes chat message |
| `OnGiftedSubscription` | `KickGiftSubEvent` | Also publishes chat message |
| `OnFollowersUpdated` | `KickFollowEvent` | Updates `kick-followercount` variable |
| `OnRewardRedeemed` | `KickRewardRedeemedEvent` | |
| `OnKicksGifted` | `KickKicksGiftedEvent` | |
| `OnStreamStatusChanged` | (chat message only) | On go-live |
| `OnStreamHost` | (chat message only) | |
| `OnUserBanned` | (logging only) | |
| `OnUserUnbanned` | (logging only) | |
| `OnMessageDeleted` | (logging only) | |
| `OnConnected` | (logging only) | |
| `OnDisconnected` | (logging only) | |

### OAuth: KickLibOAuthService
- **File:** `src/VyralBot.Integrations/Kick/KickLibOAuthService.cs` (~178 lines)
- **Namespace:** `VyralBot.Integrations.Kick`
- **Class:** `KickLibOAuthService`
- **Constructor params:** `ILogger<KickLibOAuthService>`
- **Uses:** `KickOAuthGenerator` from KickLib (PKCE flow)
- **Methods:**
  - `GenerateAuthorizationUrl(string clientId, string redirectUri)` — generates PKCE code verifier/challenge, returns auth URL, stores verifier
  - `ExchangeCodeForTokenAsync(string code, string clientId, string clientSecret, string redirectUri)` — exchanges auth code for tokens using stored PKCE verifier
  - `RefreshTokenAsync(string refreshToken, string clientId, string clientSecret)` — refreshes expired tokens

### Kick Events (defined in Integrations project)
- **File:** `src/VyralBot.Integrations/Kick/Events/` (one file per event)
- All extend `EventBase` (which implements `IEvent`)
- Events:
  - `KickChatMessageEvent` — Username, Message, Channel, ChatroomId, MessageId, Badges, IsModerator, IsSubscriber, IsOwner
  - `KickFollowEvent` — Username, FollowersCount
  - `KickSubscribeEvent` — Username, Months
  - `KickGiftSubEvent` — GifterUsername, GiftedUsernames (list), Count
  - `KickHostEvent` — HostUsername, ViewerCount
  - `KickKicksGiftedEvent` — GifterUsername, Amount
  - `KickRewardRedeemedEvent` — Username, RewardTitle, RewardCost
  - `KickStreamStatusEvent` — IsLive, Title, ViewerCount

### Connection UI
- **PlatformsViewModel** handles Kick connection UI in `src/VyralBot.UI/ViewModels/PlatformsViewModel.cs`
  - Injects `IKickService`, `KickLibOAuthService`, `KickWebSetupViewModel`
  - `KickWebSetupViewModel` manages the OAuth browser flow

## Architecture
```
KickLib (NuGet)
  └── IKickClient (WebSocket chat)
  └── IKickApi (REST API)
        ↓
KickLibChatService (IKickService impl)
  ├── Subscribes to KickLib events
  ├── Publishes to EventBus
  └── Stores messages in IChatMessageStore
        ↓
KickLibOAuthService
  └── PKCE OAuth flow via KickOAuthGenerator
```
