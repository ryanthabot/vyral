# Twitch Chat Service

## Overview
Twitch chat integration using a custom WebSocket IRC client (`LightweightTwitchIrc`). Includes auto-reconnect with exponential backoff and token refresh via `TwitchOAuthService`.

## Source Files

### ITwitchService Interface
- **File:** `src/VyralBot.Integrations/Twitch/ITwitchService.cs`
- **Namespace:** `VyralBot.Integrations.Twitch`
- **Properties:** `bool IsConnected`, `string? Channel`, `string? BotUsername`
- **Methods:**
  - `ConnectAsync(string username, string accessToken, string channel)`
  - `ConnectAsync(string username, string accessToken, string? refreshToken, string channel)` — also connects EventSub
  - `DisconnectAsync()`
  - `SendMessageAsync(string message)`

### TwitchService Implementation
- **File:** `src/VyralBot.Integrations/Twitch/TwitchService.cs` (~418 lines)
- **Namespace:** `VyralBot.Integrations.Twitch`
- **Class:** `TwitchService : ITwitchService, IDisposable`
- **Constructor:** `ILogger<TwitchService>`, `ILoggerFactory`, `IEventBus`, `TwitchOAuthService`, `IChatMessageStore?`, `IDataService?`, `ITwitchEventSubService?`
- **Private fields:**
  - `LightweightTwitchIrc? _client`
  - `string? _channel`, `_botUsername`, `_accessToken`, `_refreshToken`
  - `bool _disposed`, `_intentionalDisconnect`
  - `CancellationTokenSource? _reconnectCts`
  - `Platform? _platform`
- **Event handling:**
  - `OnConnected` → logging
  - `OnAuthenticationFailed` → `RefreshAndReconnectAsync()` using TwitchOAuthService
  - `OnDisconnected` → `ReconnectAsync()` with exponential backoff (max 5 retries, up to 30s)
  - `OnMessageReceived` → publishes `TwitchChatMessageEvent` to EventBus, stores in IChatMessageStore (respects `TwitchChatEnabled` flag)

## All Files
| File | Description |
|------|-------------|
| `src/VyralBot.Integrations/Twitch/ITwitchService.cs` | Interface |
| `src/VyralBot.Integrations/Twitch/TwitchService.cs` | Implementation (418 lines) |
| `src/VyralBot.Integrations/Twitch/Events/` | 9 event record files |
