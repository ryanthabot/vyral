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
- **Helix moderation APIs (Wave 3 — drive auto-mod via
  `TwitchModerationAdapter`, see `services/moderation.md`):**
  - `TimeoutUserAsync(string username, TimeSpan duration, string reason)` —
    POST `/helix/moderation/bans` with a duration (username resolved to a user
    id via GET `/helix/users`; duration clamped to Twitch's 1s–14d window;
    scope `moderator:manage:banned_users`). Returns false, never throws.
  - `BanUserAsync(string username, string reason)` — same endpoint, no
    duration. Returns false, never throws.
  - `DeleteMessageAsync(string messageId)` — DELETE `/helix/moderation/chat`
    (scope `moderator:manage:chat_messages`). The IRC pipeline does not parse
    message ids yet, so the auto-mod adapter's delete-without-id falls back to
    a 1-second timeout (a timeout purges the sender's messages).
  - Chat-surface helpers on the same interface: `ClearChatAsync`,
    `SetChatModeAsync` (emote/follower-only), `SetShieldModeAsync`,
    raid start/stop.
  - `ApplyChatModeAsync(TwitchChatMode mode, bool enabled, int? duration)` —
    Core-seam implementation of
    `VyralBot.Core.Services.Commands.ITwitchChatModeApplier` (Wave 16, drives
    the `!emoteonly/!subonly/!followers/!slow` command family — see
    `services/chat-mode-command-service.md`). One mode per PUT
    `/helix/chat/settings`, which adds the subscriber-only, slow
    (3–120 s wait) and follower-age (0–129600 min) coverage the two-flag
    `SetChatModeAsync` quick action cannot express; clamps the durations to
    the Helix bounds, returns false (never throws) on failure.

### TwitchService Implementation
- **File:** `src/VyralBot.Integrations/Twitch/TwitchService.cs` (~418 lines)
- **Namespace:** `VyralBot.Integrations.Twitch`
- **Class:** `TwitchService : ITwitchService, ITwitchChatModeApplier, IDisposable`
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
