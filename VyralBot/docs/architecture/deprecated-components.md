# Deprecated Components

## Status: All Removed

All previously deprecated components have been removed from the codebase. This document serves as a historical record.

## Removed Components

### Twitch Setup (Old)

| Component | Replaced By |
|---|---|
| `TwitchSetupViewModel.cs` | `TwitchWebSetupViewModel` |
| `TwitchSetupView.xaml` / `.cs` | Web-based Twitch setup flow |

These were removed after the Twitch setup was migrated to a WebView2-based OAuth flow.

### Kick Services (Old)

| Component | Replaced By |
|---|---|
| `KickService` | `KickLibChatService` |
| `KickOAuthService` | `KickLibOAuthService` |

These were removed as part of the KickLib migration, replacing custom implementations with the KickLib library.

### Placeholder Files

Five `Class1.cs` scaffold files were removed from the following projects:

- `VyralBot.Api`
- `VyralBot.Core`
- `VyralBot.Integrations`
- `VyralBot.Plugins`
- `VyralBot.Scripting`

These were default files generated during project creation and contained no meaningful code.

## Current State

The codebase is clean of all deprecated items listed above. No further action is required.
