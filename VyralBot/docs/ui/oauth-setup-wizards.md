# OAuth Setup Wizards

## Overview
Browser-based OAuth setup ViewModels and views for platform authentication.

## Base Classes

### BaseOAuthSetupViewModel
- **File:** `src/VyralBot.UI/ViewModels/BaseOAuthSetupViewModel.cs` (~639 lines)
- **Class:** `BaseOAuthSetupViewModel : ViewModelBase` (abstract)
- **Constructor:** `ILogger`, `IDataService`
- **Purpose:** Shared OAuth setup logic with WebView2 integration

### BaseWebSetupViewModel
- **File:** `src/VyralBot.UI/ViewModels/BaseWebSetupViewModel.cs` (~228 lines)
- **Class:** `BaseWebSetupViewModel : ViewModelBase` (abstract)
- **Constructor:** `ILogger`
- **Properties:** `_currentStep`, `_showSuccessScreen`
- **Purpose:** Simplified web setup base without WebView2

## Platform-Specific Implementations

### TwitchWebSetupViewModel
- **File:** `src/VyralBot.UI/ViewModels/TwitchWebSetupViewModel.cs` (~753 lines)
- **Class:** `TwitchWebSetupViewModel : ObservableObject` (extends ObservableObject directly)
- **Constructor:** `ILogger`, `TwitchOAuthService`, `IDataService`
- **View:** `src/VyralBot.UI/Views/TwitchWebSetupView.xaml(.cs)`

### KickWebSetupViewModel
- **File:** `src/VyralBot.UI/ViewModels/KickWebSetupViewModel.cs` (~975 lines)
- **Class:** `KickWebSetupViewModel : ObservableObject` (extends ObservableObject directly)
- **Constructor:** `ILogger`, `KickLibOAuthService`, `IDataService`, `IKickService`
- **View:** `src/VyralBot.UI/Views/KickWebSetupView.xaml(.cs)`

### GoogleDemoSetupViewModel
- **File:** `src/VyralBot.UI/ViewModels/GoogleDemoSetupViewModel.cs` (~113 lines)
- **Class:** `GoogleDemoSetupViewModel : BaseOAuthSetupViewModel`
- **View:** `src/VyralBot.UI/Views/GoogleDemoSetupView.xaml(.cs)`

### NgrokSetupWizardViewModel
- **File:** `src/VyralBot.UI/ViewModels/NgrokSetupWizardViewModel.cs` (~416 lines)
- **Class:** `NgrokSetupWizardViewModel : BaseOAuthSetupViewModel`
- **View:** `src/VyralBot.UI/Views/NgrokSetupWizardView.xaml(.cs)`

### NgrokSetupViewModel
- **File:** `src/VyralBot.UI/ViewModels/NgrokSetupViewModel.cs` (~491 lines)
- **Class:** `NgrokSetupViewModel : ObservableObject` (extends ObservableObject directly)
- **Constructor:** `ILogger`, `IDataService`, `NgrokService`, `WebServerService`
- **View:** `src/VyralBot.UI/Views/NgrokSetupView.xaml(.cs)`

### DemoSetupViewModel
- **File:** `src/VyralBot.UI/ViewModels/DemoSetupViewModel.cs` (~75 lines)
- **Class:** `DemoSetupViewModel : BaseWebSetupViewModel`
- **View:** `src/VyralBot.UI/Views/DemoSetupView.xaml(.cs)`

### GenericWebSetupView
- **View:** `src/VyralBot.UI/Views/GenericWebSetupView.xaml(.cs)`
- **Purpose:** Shared web setup UI template

## All Files
| File | Description |
|------|-------------|
| `src/VyralBot.UI/ViewModels/BaseOAuthSetupViewModel.cs` | Abstract base with WebView2 (639 lines) |
| `src/VyralBot.UI/ViewModels/BaseWebSetupViewModel.cs` | Simplified web setup base (228 lines) |
| `src/VyralBot.UI/ViewModels/TwitchWebSetupViewModel.cs` | Twitch OAuth setup (753 lines) |
| `src/VyralBot.UI/ViewModels/KickWebSetupViewModel.cs` | Kick OAuth setup (975 lines) |
| `src/VyralBot.UI/ViewModels/GoogleDemoSetupViewModel.cs` | Google demo setup (113 lines) |
| `src/VyralBot.UI/ViewModels/NgrokSetupWizardViewModel.cs` | Ngrok wizard (416 lines) |
| `src/VyralBot.UI/ViewModels/NgrokSetupViewModel.cs` | Ngrok setup (491 lines) |
| `src/VyralBot.UI/ViewModels/DemoSetupViewModel.cs` | Demo setup (75 lines) |
