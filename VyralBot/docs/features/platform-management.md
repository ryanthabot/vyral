# Platform Management

## Overview
Multi-platform configuration and connection management. The Platform entity stores credentials, connection state, and per-platform settings for Twitch, Kick, YouTube, TikTok, and Discord. PlatformsViewModel provides the UI for connecting/disconnecting platforms.

## Source Files

### Platform Entity
- **File:** `src/VyralBot.Data/Entities/Platform.cs`
- **Namespace:** `VyralBot.Data.Entities`
- **Properties:**
  - `Guid Id`
  - `string Name` — platform identifier (e.g., "twitch", "kick")
  - `bool Enabled`
  - `string? AccessToken`, `string? RefreshToken`, `DateTime? TokenExpiresAt`
  - `string? Username`, `string? UserId`
  - `string? ChannelId`, `string? ChatroomId` (Kick-specific)
  - `string? ClientId`, `string? ClientSecret`
  - `string? Status`
  - Per-platform chat/auto-start flags:
    - `bool KickChatEnabled`, `bool KickAutoStart`
    - `bool TwitchChatEnabled`, `bool TwitchAutoStart`
    - `bool YoutubeChatEnabled`, `bool YoutubeAutoStart`
    - `bool TiktokChatEnabled`, `bool TiktokAutoStart`
    - `bool DiscordChatEnabled`, `bool DiscordAutoStart`

### PlatformsViewModel
- **File:** `src/VyralBot.UI/ViewModels/PlatformsViewModel.cs` (~1126 lines)
- **Namespace:** `VyralBot.UI.ViewModels`
- **Class:** `PlatformsViewModel : ViewModelBase`
- **Constructor params:**
  - `ILogger<PlatformsViewModel>`
  - `IDataService`
  - `ITwitchService`
  - `TwitchOAuthService`
  - `TwitchWebSetupViewModel`
  - `IKickService`
  - `KickLibOAuthService`
  - `KickWebSetupViewModel`
  - `IProfileService`
- **Observable properties:**
  - `string TwitchStatus`, `bool IsTwitchConnected`, `string TwitchChannel`, `string TwitchUsername`
  - `string KickStatus`, `bool IsKickConnected`, `string KickUsername`
  - `bool ShowTwitchConnection`, `bool ShowKickConnection`
  - `string StatusMessage`, `bool IsConnecting`, `bool NeedsSetup`, `bool IsConfigured`
  - `bool ShowPlatformSelection`, `bool ShowTwitchSetup`, `bool ShowKickSetup`
- **Exposed sub-ViewModels:**
  - `TwitchWebSetupViewModel WebSetupViewModel`
  - `KickWebSetupViewModel KickWebSetupViewModel`
- **Callbacks:** `Func<Task>? _onPlatformConnectionChanged`, `Func<Task>? _onNavigateToPlatforms`

### Platform Events
- **File:** `src/VyralBot.Core/Services/EventBus/ProfileSwitchedEvent.cs`
- `record PlatformChatRoutingChangedEvent : EventBase` — `string Platform`, `bool Enabled`
- `record PlatformConnectionChangedEvent : EventBase` — `string Platform`, `bool Connected`, `string? Channel`, `string? BotUsername`

### Data Operations
- **IDataService** methods for platforms (in `src/VyralBot.Data/IDataService.cs`):
  - `Task<Platform?> GetPlatformAsync(string name)`
  - `Task<List<Platform>> GetPlatformsAsync()`
  - `Task SavePlatformAsync(Platform platform)`

## Platform Connection Flow
```
PlatformsViewModel
  ├── Show platform selection (Twitch / Kick)
  │
  ├── Twitch Setup:
  │   └── TwitchWebSetupViewModel → TwitchOAuthService (PKCE)
  │     → Save tokens to Platform entity
  │     → ITwitchService.ConnectAsync(username, accessToken, refreshToken, channel)
  │       → LightweightTwitchIrc connects to IRC
  │       → TwitchEventSubService connects via WebSocket
  │
  └── Kick Setup:
      └── KickWebSetupViewModel → KickLibOAuthService (PKCE)
        → Save tokens to Platform entity
        → IKickService.ConnectAsync(username, accessToken, channelName, broadcasterUserId)
          → KickLib client connects via WebSocket
```

## Key Design Decisions
- **Single Platform entity** stores all platform configs (one row per platform name)
- **Per-platform chat enable/auto-start flags** on the Platform entity itself
- **OAuth handled by dedicated services** (TwitchOAuthService, KickLibOAuthService) not the Platform entity
- **Web setup ViewModels** (TwitchWebSetupViewModel, KickWebSetupViewModel) manage browser-based OAuth flows
- **EventBus notifications** for connection state changes via PlatformConnectionChangedEvent
