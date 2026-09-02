# Main ViewModel

## Overview
Central navigation and orchestration ViewModel. Manages sidebar navigation, lazy-loaded scoped ViewModels, profile selection, and platform connection state.

## Source File
- **File:** `src/VyralBot.UI/ViewModels/MainViewModel.cs` (~1711 lines)
- **Namespace:** `VyralBot.UI.ViewModels`
- **Class:** `MainViewModel : ViewModelBase`

## Constructor Dependencies
- `DashboardViewModel` — always-loaded dashboard
- `IServiceProvider` — for creating scoped ViewModels
- `IProfileService` — profile operations
- `ProfileSettingsLoader` — loads profile settings on switch
- `ILogger<MainViewModel>`

## Scoped ViewModels (Lazy-Loaded)
Each gets its own `IServiceScope` created on first navigation:

| ViewModel | Scope Field |
|-----------|-------------|
| `PlatformsViewModel` | `_platformsScope` |
| `ChatViewModel` | `_chatScope` |
| `TwitchDashboardViewModel` | `_twitchDashboardScope` |
| `KickDashboardViewModel` | `_kickDashboardScope` |
| `SettingsViewModel` | `_settingsScope` |
| `AudioSettingsViewModel` | `_audioSettingsScope` |
| `SoundsViewModel` | `_soundsScope` |
| `StatisticsViewModel` | `_statisticsScope` |
| `EventHistoryViewModel` | `_eventHistoryScope` |
| `AlertSettingsViewModel` | `_alertSettingsScope` |
| `ConditionalRulesViewModel` | `_conditionalRulesScope` |
| `GiveawayViewModel` | `_giveawayScope` |
| `PollViewModel` | `_pollScope` |
| `WatchtimeViewModel` | `_watchtimeScope` |
| `EventTestingViewModel` | `_eventTestingScope` |
| `GoogleDemoSetupViewModel` | `_demoSetupScope` |
| `NgrokSetupWizardViewModel` | `_ngrokSetupWizardScope` |

## Observable Properties
- `ViewModelBase? CurrentViewModel` — currently displayed ViewModel
- `string Title` — window title ("VyralBot")
- `bool IsSidebarExpanded`
- `bool IsPlatformsExpanded`, `bool IsChatFeaturesExpanded`, `bool IsStreamToolsExpanded` — sidebar section toggles
- `bool IsTwitchConnected`, `bool IsKickConnected` — platform state
- `bool ShowProfileSelector`, `bool ShowPinEntry`, `bool ShowCreateProfile`
- `string NewProfileName`

## Navigation Pattern
RelayCommands navigate by setting `CurrentViewModel` to the appropriate scoped ViewModel (created on first access via `IServiceProvider.CreateScope()`).

## All Files
| File | Description |
|------|-------------|
| `src/VyralBot.UI/ViewModels/MainViewModel.cs` | Main ViewModel (1711 lines) |
