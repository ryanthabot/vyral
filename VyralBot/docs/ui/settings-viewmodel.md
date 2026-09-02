# Settings ViewModel

## Overview
Manages profile settings, PIN security, and exposes sub-ViewModels for web server and variables configuration.

## Source File
- **File:** `src/VyralBot.UI/ViewModels/SettingsViewModel.cs` (~604 lines)
- **Namespace:** `VyralBot.UI.ViewModels`
- **Class:** `SettingsViewModel : ViewModelBase`

## Constructor Dependencies
- `ILogger<SettingsViewModel>`
- `IProfileService`
- `IDataService`
- `WebServerSettingsViewModel`
- `VariablesViewModel`

## Exposed Sub-ViewModels
- `WebServerSettingsViewModel WebServerSettingsViewModel`
- `VariablesViewModel VariablesViewModel`

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
