# Profile Service

## Overview
File-based profile management with portable storage at `{projectRoot}/appdata/profiles/`. Each profile has its own directory containing profile.json metadata and a data/ subdirectory with a SQLite database.

## Source Files

### IProfileService Interface
- **File:** `src/VyralBot.Core/Services/Profiles/IProfileService.cs`
- **Namespace:** `VyralBot.Core.Services.Profiles`

### ProfileService Implementation
- **File:** `src/VyralBot.Core/Services/Profiles/ProfileService.cs` (~418 lines)
- **Namespace:** `VyralBot.Core.Services.Profiles`
- **Class:** `ProfileService : IProfileService`
- **Constructor:** `ILogger<ProfileService>`, `IEventBus?` (optional)
- **Storage:**
  - `_profilesRootPath` = `{projectRoot}/appdata/profiles/`
  - `_currentProfilePath` = `{projectRoot}/appdata/current_profile.txt`
  - Project root found by navigating up 6 levels from `AppDomain.CurrentDomain.BaseDirectory`
- **Public methods:**
  - `GetCurrentProfileName()` — sync, reads current_profile.txt
  - `GetCurrentProfileAsync()` — loads profile.json, creates default if missing
  - `GetProfilesAsync()` — enumerates profile directories
  - `CreateProfileAsync(string name, string? displayName, string? pinCode)` — validates alphanumeric, creates dir + profile.json, auto-sets as current if first
  - `SwitchProfileAsync(string name, string? pinCode)` — validates PIN (PBKDF2 v2, legacy SHA256 auto-migrated), writes current_profile.txt, publishes `ProfileSwitchedEvent`. Live mid-session switch: MainViewModel's event handler stops command timers/workflows/restream, disconnects platforms and reloads.
  - `DeleteProfileAsync(string name)` — recursive delete (confirm dialog first; the profile-selector delete path prompts for the profile's PIN when one is set — see `docs/ui/profile-selector.md`. The separate Settings → Profiles delete flow in `SettingsViewModel` does not ask for the PIN)
  - `GetProfileDataPath(string)` → `profiles/{name}/data/`
  - `GetProfileCredentialsPath(string)` → `profiles/{name}/`
  - `ValidatePinAsync(string, string)` — PBKDF2 v2 (`v2.salt.hash`, 100k iterations, fixed-time compare); legacy unsalted SHA256 hashes verified then transparently re-hashed to v2
  - `UpdateProfilePinAsync(string, string?, string?)` — change/remove PIN (requires current PIN; exposed in Settings)
  - `UpdateProfileAsync(Profile)`
  - `ExportProfileAsync(string, string)` → ZIP of the WHOLE profile directory, including the credentials folder. Platform tokens in there are DPAPI-wrapped (CurrentUser scope, machine+user bound) — Data Management shows a pre-export warning that imported logins only work on the exporting PC.
  - `ImportProfileAsync(string)` → ZIP import (called by Data Management's "Import Profile Backup" button in Settings → Data Management — see `docs/ui/data-management.md`; refuses with `InvalidOperationException` when a profile with the ZIP's name already exists)

### Profile Model
- **File:** `src/VyralBot.Core/Models/Profile.cs`
- **Properties:** `required string Name`, `string? DisplayName`, `string? Description`, `string? PinCode` (PBKDF2 v2 hash), `Dictionary<string, string> Settings`

## Storage Layout
```
{projectRoot}/appdata/
  current_profile.txt
  profiles/
    {name}/
      profile.json
      data/
        vyralbot.db
```

## All Files
| File | Description |
|------|-------------|
| `src/VyralBot.Core/Services/Profiles/IProfileService.cs` | Interface |
| `src/VyralBot.Core/Services/Profiles/ProfileService.cs` | Implementation (418 lines) |
| `src/VyralBot.Core/Models/Profile.cs` | Profile model |
