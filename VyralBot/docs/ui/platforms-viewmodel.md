# Platforms ViewModel

## Overview
Manages platform connection UI for Twitch and Kick. Handles OAuth setup flows and connection/disconnection.

## Source File
- **File:** `src/VyralBot.UI/ViewModels/PlatformsViewModel.cs` (~1126 lines)
- **Namespace:** `VyralBot.UI.ViewModels`
- **Class:** `PlatformsViewModel : ViewModelBase`

## Constructor Dependencies
- `ILogger<PlatformsViewModel>`
- `IDataService`
- `ITwitchService`
- `TwitchOAuthService`
- `TwitchWebSetupViewModel`
- `IKickService`
- `KickLibOAuthService`
- `KickWebSetupViewModel`
- `IProfileService`

## Observable Properties
- `string TwitchStatus`, `bool IsTwitchConnected`, `string TwitchChannel`, `string TwitchUsername`
- `string KickStatus`, `bool IsKickConnected`, `string KickUsername`
- `bool ShowTwitchConnection`, `bool ShowKickConnection`
- `string StatusMessage`, `bool IsConnecting`, `bool NeedsSetup`, `bool IsConfigured`
- `bool ShowPlatformSelection`, `bool ShowTwitchSetup`, `bool ShowKickSetup`

## Exposed Sub-ViewModels
- `TwitchWebSetupViewModel WebSetupViewModel` — Twitch OAuth browser flow
- `KickWebSetupViewModel KickWebSetupViewModel` — Kick OAuth browser flow

## Callbacks
- `Func<Task>? _onPlatformConnectionChanged`
- `Func<Task>? _onNavigateToPlatforms`

## All Files
| File | Description |
|------|-------------|
| `src/VyralBot.UI/ViewModels/PlatformsViewModel.cs` | Platforms ViewModel (1126 lines) |
