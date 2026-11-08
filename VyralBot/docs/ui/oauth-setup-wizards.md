# OAuth Setup Wizards

## Overview
Browser-based OAuth setup ViewModels and views for platform authentication.

## Base Classes

### BaseOAuthSetupViewModel
- **File:** `src/VyralBot.UI/ViewModels/BaseOAuthSetupViewModel.cs` (~639 lines)
- **Class:** `BaseOAuthSetupViewModel : ViewModelBase` (abstract)
- **Constructor:** `ILogger`, `IDataService`
- **Purpose:** Shared OAuth setup logic with WebView2 integration
- **Secrets policy:** client IDs/secrets, modal inputs (e.g. the ngrok auth
  token) and OAuth callback URLs (they carry the authorization code) are never
  logged — log statements record the outcome only

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
- **File:** `src/VyralBot.UI/ViewModels/GoogleDemoSetupViewModel.cs` (~125 lines)
- **Class:** `GoogleDemoSetupViewModel : BaseOAuthSetupViewModel`
- **View:** `src/VyralBot.UI/Views/GoogleDemoSetupView.xaml(.cs)`
- **Demo safety:** credentials are kept in process-scoped environment variables
  (session memory only, never persisted); the OAuth callback returns dummy
  tokens only and rejects callbacks without an authorization code (truthful
  error path); scopes are read-only identity (`openid email profile`)

### NgrokSetupWizardViewModel
- **File:** `src/VyralBot.UI/ViewModels/NgrokSetupWizardViewModel.cs` (~470 lines)
- **Class:** `NgrokSetupWizardViewModel : BaseOAuthSetupViewModel`
- **View:** `src/VyralBot.UI/Views/NgrokSetupWizardView.xaml(.cs)`
- **Token persistence:** the auth token is saved to `ngrok_credentials.json` in
  the profile credentials folder via `NgrokService.SaveAuthTokenAsync` and is
  restored on wizard open / `LoadCredentialsAsync` via `NgrokService.LoadAuthTokenAsync`
  (masked in UI and logs — never logged in clear). The token is NOT passed to
  `ngrok config add-authtoken` — that would expose it on a machine-readable
  command line; `NgrokService.StartTunnelAsync` passes `--authtoken` instead.
- **Token entry:** masked PasswordBox in the modal (code-behind feeds
  `ModalInput`, clears the box when the modal closes).
- **Process handling:** all external processes (store deep link, `ngrok version`
  check) go through `IProcessManager` — no raw `Process.Start` in the wizard.
- **Test lifecycle:** the Step 4 test captures whether the web server / ngrok
  tunnel were already running and only stops what it started itself, so a live
  webhook tunnel is never torn down by the wizard. Distinct error states for
  "not installed" vs "port in use" vs "bad token"; public URL has a copy button.

### DemoSetupViewModel
- **File:** `src/VyralBot.UI/ViewModels/DemoSetupViewModel.cs` (~75 lines)
- **Class:** `DemoSetupViewModel : BaseWebSetupViewModel`
- **View:** `src/VyralBot.UI/Views/DemoSetupView.xaml(.cs)`
- **Demo safety:** the Client ID modal discards its input (nothing persisted,
  nothing logged) — component demo only, not reachable from the sidebar

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
| `src/VyralBot.UI/ViewModels/DemoSetupViewModel.cs` | Demo setup (75 lines) |
