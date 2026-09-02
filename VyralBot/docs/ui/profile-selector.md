# Profile Selector

## Overview
Modal window for profile selection on app startup. Uses manual INotifyPropertyChanged (not CommunityToolkit.Mvvm).

## Source Files
- **ViewModel:** `src/VyralBot.UI/ViewModels/ProfileSelectorViewModel.cs` (~363 lines)
  - **Class:** `ProfileSelectorViewModel : INotifyPropertyChanged` (manual implementation, not ViewModelBase)
  - **Constructor:** `IProfileService`, `ILogger<ProfileSelectorViewModel>`
  - **Properties (manual):** `SelectedProfile`, `EnteredPin`, `NewProfileName`, profiles list, dialog state flags
- **View:** `src/VyralBot.UI/Views/ProfileSelectorWindow.xaml`
- **Code-behind:** `src/VyralBot.UI/Views/ProfileSelectorWindow.xaml.cs`

## Features
- Profile list display
- PIN entry for protected profiles
- Create new profile dialog
- Auto-login support

## All Files
| File | Description |
|------|-------------|
| `src/VyralBot.UI/ViewModels/ProfileSelectorViewModel.cs` | Manual INPC ViewModel (363 lines) |
| `src/VyralBot.UI/Views/ProfileSelectorWindow.xaml` | Window XAML |
| `src/VyralBot.UI/Views/ProfileSelectorWindow.xaml.cs` | Code-behind |
