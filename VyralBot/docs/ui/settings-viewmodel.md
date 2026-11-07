# Settings ViewModel

## Overview
Manages profile settings, PIN security, and exposes sub-ViewModels for web server and variables configuration.

## Source File
- **File:** `src/VyralBot.UI/ViewModels/SettingsViewModel.cs` (~604 lines)
- **Namespace:** `VyralBot.UI.ViewModels`
- **Class:** `SettingsViewModel : ViewModelBase, IDisposable`

## Constructor Dependencies
- `ILogger<SettingsViewModel>`
- `IProfileService`
- `IDataService`
- `WebServerSettingsViewModel`
- `VariablesViewModel`
- `PerformanceViewModel`, `ViewerHubViewModel`, `PlatformStartupSettingsViewModel`, `DataManagementViewModel` and platform/OBS services (see the source for the full list)

## Exposed Sub-ViewModels
- `WebServerSettingsViewModel WebServerSettingsViewModel`
- `VariablesViewModel VariablesViewModel`
- `PerformanceViewModel PerformanceViewModel`
- `ViewerHubViewModel ViewerHubViewModel` (singleton; see [viewer-hub.md](viewer-hub.md))
- `PlatformStartupSettingsViewModel PlatformStartupSettingsViewModel`
- `DataManagementViewModel DataManagementViewModel`

## Lifecycle
- Implements `IDisposable`: `Dispose` stops the Viewer Hub pending-codes
  30s poller (`ViewerHubViewModel.Cleanup`) — the poller otherwise outlives
  the page because ViewerHubViewModel is a singleton. The DI scope disposes
  the page VM on navigation away or Settings re-entry.
- `OnSelectedCategoryChanged` starts the poller while `SelectedCategory` is
  `"ViewerHub"` and stops it for any other category.

## Observable Properties
- `ObservableCollection<Profile> Profiles`
- `Profile? CurrentProfile`
- `bool AutoLogin`
- `string SelectedCategory` (defaults to "Profile", partial method `OnSelectedCategoryChanged`)
- `string? StatusMessage`
- Dialog flags: `ShowDeleteConfirmation`, `ShowChangePinDialog`, `ShowCreateProfileDialog`, `ShowSwitchProfileDialog`
- PIN fields: `CurrentPin`, `NewPin`, `ConfirmNewPin`, `SwitchProfilePin`
- New profile: `NewProfileName`, `NewProfileDisplayName`, `NewProfilePin`
- Platform state: `IsTwitchConnected`, `IsKickConnected`, `IsYouTubeConnected`
- Platform settings: `SelectedPlatformViewModel`, `ShowPlatformGrid`

## All Files
| File | Description |
|------|-------------|
| `src/VyralBot.UI/ViewModels/SettingsViewModel.cs` | Settings ViewModel (604 lines) |
