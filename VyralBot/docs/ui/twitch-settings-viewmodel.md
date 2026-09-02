# Twitch Settings ViewModel

## Overview
Minimal Twitch settings ViewModel. Most Twitch configuration is handled by PlatformsViewModel and TwitchWebSetupViewModel.

## Source File
- **File:** `src/VyralBot.UI/ViewModels/TwitchSettingsViewModel.cs` (~28 lines)
- **Namespace:** `VyralBot.UI.ViewModels`
- **Class:** `TwitchSettingsViewModel : ViewModelBase`
- **Constructor:** `ILogger<TwitchSettingsViewModel>`

## Observable Properties
- `string StatusMessage`

## Related ViewModels
- **TwitchWebSetupViewModel** (`src/VyralBot.UI/ViewModels/TwitchWebSetupViewModel.cs`, ~753 lines) — OAuth browser setup, extends `ObservableObject` directly
  - Constructor: `ILogger`, `TwitchOAuthService`, `IDataService`
  - Properties: `_currentStep`, `_showSuccessScreen`
- **TwitchDashboardViewModel** (`src/VyralBot.UI/ViewModels/TwitchDashboardViewModel.cs`, ~172 lines) — Twitch dashboard
  - Constructor: `ILogger`, `IDataService`
  - Properties: `_username`, `_channelName`, `_status`, `_isConnected`
- **TwitchChatSettingsViewModel** (`src/VyralBot.UI/ViewModels/TwitchChatSettingsViewModel.cs`, ~224 lines) — Chat settings
  - Constructor: `ILogger`, `IChatEventSettingsService`
  - Properties: `_isChatEnabled`, `_autoStartChat`, `_autoRepliesEnabled`, `_status`, `_showFollowsInChat`

## View Files
| File | Description |
|------|-------------|
| `src/VyralBot.UI/Views/TwitchSettingsView.xaml(.cs)` | Settings view |
| `src/VyralBot.UI/Views/TwitchWebSetupView.xaml(.cs)` | OAuth setup wizard |
| `src/VyralBot.UI/Views/TwitchDashboardView.xaml(.cs)` | Dashboard view |
| `src/VyralBot.UI/Views/TwitchChatSettingsView.xaml(.cs)` | Chat settings view |
