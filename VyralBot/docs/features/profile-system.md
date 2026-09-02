# Profile System

## Overview
File-based profile management with portable storage. Each profile has its own SQLite database, credentials, and settings. Profile data is stored at `{projectRoot}/appdata/profiles/` for portability (not %AppData%).

## Source Files

### IProfileService Interface
- **File:** `src/VyralBot.Core/Services/Profiles/IProfileService.cs`
- **Namespace:** `VyralBot.Core.Services.Profiles`

### ProfileService Implementation
- **File:** `src/VyralBot.Core/Services/Profiles/ProfileService.cs` (~418 lines)
- **Namespace:** `VyralBot.Core.Services.Profiles`
- **Class:** `ProfileService : IProfileService`
- **Constructor params:** `ILogger<ProfileService>`, `IEventBus?` (optional)
- **Storage paths:**
  - `_profilesRootPath` = `{projectRoot}/appdata/profiles/`
  - `_currentProfilePath` = `{projectRoot}/appdata/current_profile.txt`
  - Navigates up 6 levels from `AppDomain.CurrentDomain.BaseDirectory` to find project root
- **Public methods:**
  - `GetCurrentProfileName()` — sync, reads `current_profile.txt`
  - `GetCurrentProfileAsync()` — loads `profile.json`, creates default if missing
  - `GetProfilesAsync()` — enumerates profile directories
  - `CreateProfileAsync(string name, string? displayName, string? pinCode)` — validates alphanumeric name, creates directory + profile.json, auto-sets as current if first profile
  - `SwitchProfileAsync(string name, string? pinCode)` — validates PIN (SHA256 hash), writes to `current_profile.txt`, publishes `ProfileSwitchedEvent` via EventBus
  - `DeleteProfileAsync(string name)` — recursive directory delete, clears current if deleted profile was active
  - `GetProfileDataPath(string name)` → `profiles/{name}/data/`
  - `GetProfileCredentialsPath(string name)` → `profiles/{name}/`
  - `ValidatePinAsync(string name, string pin)` — SHA256 hash comparison
  - `UpdateProfilePinAsync(string name, string? oldPin, string? newPin)`
  - `UpdateProfileAsync(Profile profile)` — saves profile.json
  - `ExportProfileAsync(string name, string exportPath)` — exports profile to ZIP
  - `ImportProfileAsync(string importPath)` — imports profile from ZIP

### Profile Model
- **File:** `src/VyralBot.Core/Models/Profile.cs`
- **Namespace:** `VyralBot.Core.Models`
- **Properties:**
  - `required string Name`
  - `string? DisplayName`
  - `string? Description`
  - `string? PinCode` (SHA256 hashed)
  - `Dictionary<string, string> Settings`

### ProfileAwareDataService
- **File:** `src/VyralBot.UI/Services/ProfileAwareDataService.cs` (~195 lines)
- **Namespace:** `VyralBot.UI.Services`
- **Class:** `ProfileAwareDataService : IDataService`
- **Constructor params:** `IProfileService`, `ILogger<DapperDataService>`
- **Pattern:** Delegation wrapper for IDataService
- **`GetCurrentService()`** — checks if current profile changed, creates new `DapperDataService` pointing at `profiles/{name}/data/vyralbot.db`
- **`ClearConnection()`** — nulls current service, calls `SqliteConnection.ClearAllPools()`, forces GC
- **All IDataService methods** delegate to `GetCurrentService()` result

### Profile Events
- **File:** `src/VyralBot.Core/Services/EventBus/ProfileSwitchedEvent.cs`
- **Events:**
  - `record ProfileSwitchedEvent : EventBase` — `OldProfileName`, `NewProfileName` (published on profile switch)

### Profile Settings UI
- **SettingsViewModel** manages profile CRUD in `src/VyralBot.UI/ViewModels/SettingsViewModel.cs`
  - Constructor: `ILogger<SettingsViewModel>`, `IProfileService`, `IDataService`, `WebServerSettingsViewModel`, `VariablesViewModel`
  - Observable properties: `Profiles`, `CurrentProfile`, `AutoLogin`, `ShowDeleteConfirmation`, `ShowChangePinDialog`, `ShowCreateProfileDialog`, `ShowSwitchProfileDialog`, and related PIN/name fields
  - Exposes `WebServerSettingsViewModel` and `VariablesViewModel` as sub-VMs

## Storage Layout
```
{projectRoot}/
  appdata/
    current_profile.txt          ← Contains current profile name
    profiles/
      {ProfileName}/
        profile.json             ← Profile metadata (name, display, pin hash, settings)
        data/
          vyralbot.db            ← SQLite database (per-profile)
        (credential files)       ← Platform tokens etc.
      {AnotherProfile}/
        profile.json
        data/
          vyralbot.db
```

## Profile Switch Flow
```
User selects profile → SettingsViewModel.SwitchProfileCommand
  → ProfileService.SwitchProfileAsync(name, pin)
    → ValidatePinAsync (SHA256 comparison)
    → Write name to current_profile.txt
    → EventBus.PublishAsync(ProfileSwitchedEvent)
      ↓
ProfileAwareDataService (subscribed? checks on next call)
  → GetCurrentService() detects profile change
  → Creates new DapperDataService with new DB path
      ↓
All data operations now use new profile's database
```

## Key Design Decisions
- **Portable storage** at project root (not %AppData%) for easy backup/transfer
- **File-based** profile metadata (profile.json) — not in database
- **Per-profile SQLite databases** for complete data isolation
- **SHA256 PIN hashing** for profile protection
- **EventBus integration** for profile switch notifications
- **ZIP export/import** for profile portability
