# Kick Web Setup ViewModel

## Overview
Manages the Kick OAuth browser-based setup wizard. Extends ObservableObject directly (not ViewModelBase).

## Source File
- **File:** `src/VyralBot.UI/ViewModels/KickWebSetupViewModel.cs` (~975 lines)
- **Namespace:** `VyralBot.UI.ViewModels`
- **Class:** `KickWebSetupViewModel : ObservableObject`

## Constructor Dependencies
- `ILogger<KickWebSetupViewModel>`
- `KickLibOAuthService` — PKCE OAuth flow
- `IDataService` — save platform credentials
- `IKickService` — connect after setup

## Observable Properties
- `int CurrentStep` — wizard step tracking

## Setup Flow
1. Generate authorization URL via `KickLibOAuthService.GenerateAuthorizationUrl()`
2. Open browser for user authorization
3. Capture redirect with auth code
4. Exchange code for tokens via `KickLibOAuthService.ExchangeCodeForTokenAsync()`
5. Save tokens to Platform entity via IDataService
6. Connect via IKickService

## All Files
| File | Description |
|------|-------------|
| `src/VyralBot.UI/ViewModels/KickWebSetupViewModel.cs` | Setup ViewModel (975 lines) |
| `src/VyralBot.UI/Views/KickWebSetupView.xaml(.cs)` | Setup view |
