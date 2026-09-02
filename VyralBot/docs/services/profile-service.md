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
  - `SwitchProfileAsync(string name, string? pinCode)` — validates PIN (SHA256), writes current_profile.txt, publishes ProfileSwitchedEvent
  - `DeleteProfileAsync(string name)` — recursive delete
  - `GetProfileDataPath(string)` → `profiles/{name}/data/`
  - `GetProfileCredentialsPath(string)` → `profiles/{name}/`
  - `ValidatePinAsync(string, string)` — SHA256 hash comparison
  - `UpdateProfilePinAsync(string, string?, string?)`
  - `UpdateProfileAsync(Profile)`
  - `ExportProfileAsync(string, string)` → ZIP export
  - `ImportProfileAsync(string)` → ZIP import

### Profile Model
- **File:** `src/VyralBot.Core/Models/Profile.cs`
- **Properties:** `required string Name`, `string? DisplayName`, `string? Description`, `string? PinCode` (SHA256), `Dictionary<string, string> Settings`

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
