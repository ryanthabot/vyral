# Kick UI ViewModels

## Overview
ViewModels for Kick platform configuration, dashboard, and chat settings.

## Source Files

### KickSettingsViewModel
- **File:** `src/VyralBot.UI/ViewModels/KickSettingsViewModel.cs` (~422 lines)
- **Class:** `KickSettingsViewModel : ViewModelBase`
- **Constructor:** `ILogger`, `IDataService`, `IEventBus`, `IKickService`, `WebhookCoordinatorService`
- **Observable properties:** `_username`, `_channelName`, `_status`, `_isConnected`

### KickDashboardViewModel
- **File:** `src/VyralBot.UI/ViewModels/KickDashboardViewModel.cs` (~181 lines)
- **Class:** `KickDashboardViewModel : ViewModelBase`
- **Constructor:** `ILogger`, `IDataService`, `IKickService`
- **Observable properties:** `_username`, `_channelName`

### KickChatSettingsViewModel
- **File:** `src/VyralBot.UI/ViewModels/KickChatSettingsViewModel.cs` (~242 lines)
- **Class:** `KickChatSettingsViewModel : ViewModelBase`
- **Constructor:** `ILogger`, `IChatEventSettingsService`
- **Observable properties:** `_isChatEnabled`, `_autoStartChat`, `_autoRepliesEnabled`, `_status`, `_showFollowsInChat`

### KickWebSetupViewModel
- **File:** `src/VyralBot.UI/ViewModels/KickWebSetupViewModel.cs` (~975 lines)
- **Class:** `KickWebSetupViewModel : ObservableObject` (extends ObservableObject directly, not ViewModelBase)
- **Constructor:** `ILogger`, `KickLibOAuthService`, `IDataService`, `IKickService`
- **Observable properties:** `_currentStep`
- **Purpose:** Manages the Kick OAuth browser-based setup flow

## View Files
| View | Description |
|------|-------------|
| `src/VyralBot.UI/Views/KickSettingsView.xaml(.cs)` | Kick settings |
| `src/VyralBot.UI/Views/KickDashboardView.xaml(.cs)` | Kick dashboard |
| `src/VyralBot.UI/Views/KickChatSettingsView.xaml(.cs)` | Kick chat settings |
| `src/VyralBot.UI/Views/KickWebSetupView.xaml(.cs)` | Kick OAuth setup wizard |
