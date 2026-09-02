# Setup Wizard Views

## Overview
XAML views for OAuth and service setup wizards.

## Setup Views

| View File | ViewModel | Purpose |
|-----------|-----------|---------|
| `src/VyralBot.UI/Views/TwitchWebSetupView.xaml(.cs)` | `TwitchWebSetupViewModel` | Twitch OAuth setup |
| `src/VyralBot.UI/Views/KickWebSetupView.xaml(.cs)` | `KickWebSetupViewModel` | Kick OAuth setup |
| `src/VyralBot.UI/Views/GoogleDemoSetupView.xaml(.cs)` | `GoogleDemoSetupViewModel` | Google demo setup |
| `src/VyralBot.UI/Views/NgrokSetupView.xaml(.cs)` | `NgrokSetupViewModel` | Ngrok tunnel setup |
| `src/VyralBot.UI/Views/NgrokSetupWizardView.xaml(.cs)` | `NgrokSetupWizardViewModel` | Ngrok wizard |
| `src/VyralBot.UI/Views/DemoSetupView.xaml(.cs)` | `DemoSetupViewModel` | Demo setup |
| `src/VyralBot.UI/Views/GenericWebSetupView.xaml(.cs)` | (shared template) | Generic web setup template |

## ViewModel Base Classes
- **BaseOAuthSetupViewModel** (`src/VyralBot.UI/ViewModels/BaseOAuthSetupViewModel.cs`, 639 lines) — abstract base with WebView2
- **BaseWebSetupViewModel** (`src/VyralBot.UI/ViewModels/BaseWebSetupViewModel.cs`, 228 lines) — simplified base without WebView2

## Note
TwitchWebSetupViewModel, KickWebSetupViewModel, and NgrokSetupViewModel all extend `ObservableObject` directly (not ViewModelBase or the base setup classes).
