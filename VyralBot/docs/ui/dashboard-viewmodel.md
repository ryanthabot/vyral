# Dashboard ViewModel

## Overview
ViewModel for the Home page: high-level bot status plus the first-run
"Get started" strip state (dismiss persists app-wide via ui-settings.json;
platform-connection visibility is bound in XAML to MainViewModel flags).

## Source File
- **File:** `src/VyralBot.UI/ViewModels/DashboardViewModel.cs`
- **Namespace:** `VyralBot.UI.ViewModels`
- **Class:** `DashboardViewModel : ViewModelBase`
- **Constructor:** `DashboardViewModel(UiPreferencesService)` — loads
  `HomeGettingStartedDismissed` at startup.

## Observable Properties
- `int ActiveTriggerCount`
- `int TotalActionCount`
- `int ConnectedPlatformCount`
- `string Status`
- `bool IsGettingStartedDismissed` — Home strip dismiss state

## Commands
- `DismissGettingStartedCommand` — hides the strip and persists
  `HomeGettingStartedDismissed=true` through `UiPreferencesService`
  (loads fresh before saving so theme prefs are never clobbered).

## All Files
| File | Description |
|------|-------------|
| `src/VyralBot.UI/ViewModels/DashboardViewModel.cs` | Dashboard ViewModel |
| `src/VyralBot.UI/Services/UiPreferencesService.cs` | Persists ui-settings.json |
