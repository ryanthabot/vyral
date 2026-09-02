# Settings Sub-ViewModels

## Overview
ViewModels for specific settings areas: web server, audio, sounds, alerts, variables, and per-platform chat settings.

## Web Server Settings
### WebServerSettingsViewModel
- **File:** `src/VyralBot.UI/ViewModels/WebServerSettingsViewModel.cs` (~502 lines)
- **Class:** `WebServerSettingsViewModel : ViewModelBase`
- **Constructor:** `ILogger`, `IDataService`, `WebhookCoordinatorService?`, `NgrokService?`
- **Observable properties:** `_isEnabled`, `_port`, `_ngrokAuthToken`
- **View:** `src/VyralBot.UI/Views/WebServerSettingsView.xaml(.cs)`

## Audio Settings
### AudioSettingsViewModel
- **File:** `src/VyralBot.UI/ViewModels/AudioSettingsViewModel.cs` (~206 lines)
- **Class:** `AudioSettingsViewModel : ViewModelBase`
- **Constructor:** `ILogger`, `IDataService`, `ISoundService`
- **Observable properties:** `_streamerSoundsEnabled`, `_streamSoundsEnabled`, `_masterVolume`, `_webSocketPort`, `_connectedClients`
- **View:** `src/VyralBot.UI/Views/AudioSettingsView.xaml(.cs)`

## Sounds Management
### SoundsViewModel
- **File:** `src/VyralBot.UI/ViewModels/SoundsViewModel.cs` (~363 lines)
- **Class:** `SoundsViewModel : ViewModelBase`
- **Constructor:** `ILogger`, `ISoundService`, `IDataService`
- **Observable properties:** `_soundFiles`, `_selectedSoundFile`, `_eventMappings`
- **View:** `src/VyralBot.UI/Views/SoundsView.xaml(.cs)`

## Alert Settings
### AlertSettingsViewModel
- **File:** `src/VyralBot.UI/ViewModels/AlertSettingsViewModel.cs` (~159 lines)
- **Class:** `AlertSettingsViewModel : ViewModelBase`
- **Constructor:** `ILogger`, `IAlertService`
- **Observable properties:** `_alertsEnabled`, `_alertDuration`, `_selectedAnimation`, `_selectedPosition`
- **View:** `src/VyralBot.UI/Views/AlertSettingsView.xaml(.cs)`

## Variables Management
### VariablesViewModel
- **File:** `src/VyralBot.UI/ViewModels/VariablesViewModel.cs` (~350 lines)
- **Class:** `VariablesViewModel : ViewModelBase`
- **Constructor:** `ILogger`, `IDataService`, `IVariableReplacementService`
- **Observable properties:** `_variables`, `_categories`
- **View:** `src/VyralBot.UI/Views/VariablesView.xaml(.cs)`

### VariablePickerViewModel
- **File:** `src/VyralBot.UI/ViewModels/VariablePickerViewModel.cs` (~198 lines)
- **Class:** `VariablePickerViewModel : ObservableObject` (extends ObservableObject directly)
- **Constructor:** `ILogger`, `IVariableReplacementService`
- **Observable properties:** `_variables`, `_filteredVariables`, `_categories`, `_selectedCategory`
- **Control:** `src/VyralBot.UI/Controls/VariablePicker.xaml(.cs)`

## Per-Platform Chat Settings ViewModels
| ViewModel | File | Lines | Constructor |
|-----------|------|-------|-------------|
| `TwitchChatSettingsViewModel` | `TwitchChatSettingsViewModel.cs` | ~224 | `ILogger`, `IChatEventSettingsService` |
| `KickChatSettingsViewModel` | `KickChatSettingsViewModel.cs` | ~242 | `ILogger`, `IChatEventSettingsService` |
| `YoutubeChatSettingsViewModel` | `YoutubeChatSettingsViewModel.cs` | ~93 | `ILogger`, `IDataService`, `IEventBus` |
| `TiktokChatSettingsViewModel` | `TiktokChatSettingsViewModel.cs` | ~93 | `ILogger`, `IDataService`, `IEventBus` |
| `DiscordChatSettingsViewModel` | `DiscordChatSettingsViewModel.cs` | ~93 | `ILogger`, `IDataService`, `IEventBus` |

All have properties: `_isChatEnabled`, `_autoStartChat`, `_autoRepliesEnabled`, `_status`
Twitch and Kick additionally have: `_showFollowsInChat`

## Chat Settings Views
| View File |
|-----------|
| `src/VyralBot.UI/Views/TwitchChatSettingsView.xaml(.cs)` |
| `src/VyralBot.UI/Views/KickChatSettingsView.xaml(.cs)` |
| `src/VyralBot.UI/Views/YoutubeChatSettingsView.xaml(.cs)` |
| `src/VyralBot.UI/Views/TiktokChatSettingsView.xaml(.cs)` |
| `src/VyralBot.UI/Views/DiscordChatSettingsView.xaml(.cs)` |
