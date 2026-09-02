# Settings System

## Overview
Settings are managed through `SettingsViewModel` which handles profile management, PIN security, and exposes sub-ViewModels for web server configuration and variables. Application configuration (key-value pairs) is stored via the `Config` entity in the per-profile SQLite database.

---

## Source Files

### SettingsViewModel
- **File:** `src/VyralBot.UI/ViewModels/SettingsViewModel.cs` (~604 lines)
- **Namespace:** `VyralBot.UI.ViewModels`
- **Class:** `SettingsViewModel : ViewModelBase`
- **Constructor params:**
  - `ILogger<SettingsViewModel>`
  - `IProfileService`
  - `IDataService`
  - `WebServerSettingsViewModel`
  - `VariablesViewModel`
- **Exposed sub-ViewModels:**
  - `WebServerSettingsViewModel WebServerSettingsViewModel`
  - `VariablesViewModel VariablesViewModel`
- **Observable properties:**
  - `ObservableCollection<Profile> Profiles`
  - `Profile? CurrentProfile`
  - `bool AutoLogin`
  - `string SelectedCategory` (defaults to `"Profile"`, fires `OnSelectedCategoryChanged` partial method)
  - `string? StatusMessage`
  - Profile management dialogs: `bool ShowDeleteConfirmation`, `bool ShowChangePinDialog`, `bool ShowCreateProfileDialog`, `bool ShowSwitchProfileDialog`
  - PIN fields: `string CurrentPin`, `string NewPin`, `string ConfirmNewPin`, `string SwitchProfilePin`
  - New profile fields: `string NewProfileName`, `string NewProfileDisplayName`, `string NewProfilePin`
  - Platform connection state: `bool IsTwitchConnected`, `bool IsKickConnected`, `bool IsYouTubeConnected`
  - Platform settings: `object? SelectedPlatformViewModel`, `bool ShowPlatformGrid`

### Config Entity (Key-Value Settings)
- **File:** `src/VyralBot.Data/Entities/Config.cs`
- **Namespace:** `VyralBot.Data.Entities`
- **Properties:**
  - `string Key` (primary key, no separate Id)
  - `string Value`
  - `string? Category`
  - `bool IsEncrypted`

### IDataService Config Methods
- **File:** `src/VyralBot.Data/IDataService.cs`
- **Methods:**
  - `Task<Config?> GetConfigAsync(string key)`
  - `Task<List<Config>> GetConfigsAsync()`
  - `Task<List<Config>> GetConfigsByCategoryAsync(string category)`
  - `Task SaveConfigAsync(Config config)`
  - `Task DeleteConfigAsync(string key)`

### Profile Model (used by SettingsViewModel)
- **File:** `src/VyralBot.Core/Models/Profile.cs`
- **Properties:**
  - `required string Name`
  - `string? DisplayName`
  - `string? Description`
  - `string? PinCode` (SHA256 hashed)
  - `Dictionary<string, string> Settings`

### WebServerSettings Entity
- **File:** `src/VyralBot.Data/Entities/WebServerSettings.cs`
- **Namespace:** `VyralBot.Data.Entities`
- **Properties:**
  - `int Id` (always 1)
  - `bool IsEnabled`
  - `int Port`
  - `string NgrokRegion`
  - `string? CurrentTunnelUrl`
  - `bool AutoStartOnLaunch`

### Related ViewModels
- **`WebServerSettingsViewModel`** — manages web server on/off, port, ngrok settings
- **`VariablesViewModel`** — manages custom variables (CRUD on Variable entities)

---

## Settings Categories

The `SettingsViewModel` uses `SelectedCategory` to switch between settings views:

- **"Profile"** — Profile management (create, switch, delete, PIN)
- **"Platforms"** — Platform connection grid (Twitch, Kick, YouTube)
- Other categories handled by `OnSelectedCategoryChanged` partial method

---

## Key Design Decisions

- **Config entity** is simple key-value storage (no separate Id — `Key` is the PK)
- **Per-profile settings** stored in each profile's SQLite database
- **Profile metadata** (name, display name, PIN) stored in `profile.json` file, not database
- **Sub-ViewModel composition** — `WebServerSettingsViewModel` and `VariablesViewModel` are injected and exposed
- **PIN security** uses SHA256 hashing for profile protection
