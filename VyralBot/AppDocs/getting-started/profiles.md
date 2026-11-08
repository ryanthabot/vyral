# Profiles

> Profiles give each streamer persona its own isolated database, credentials, and settings — all stored portably in the `appdata/` folder next to the app, protected by an optional PIN and switchable without restarting.

## Overview

A **profile** is a complete, self-contained Vyral identity. When you create one, Vyral creates a folder under `appdata/profiles/<Name>/` containing its own SQLite database, its own credential files (Twitch, ngrok), and its own `profile.json` metadata. Everything the bot knows — platform connections, commands, currency balances, polls, chat history, sounds assignments — lives inside that one folder.

Why this matters to you:

- **Multiple personas**: run a bot for two channels (or a "test" and a "production" setup) from one install, switching between them at any time.
- **Portable**: nothing is written to the registry or `%APPDATA%`. Copy the `appdata/` folder (or one profile folder) to another machine and everything comes with it. Delete `appdata/` for a completely fresh start.
- **PIN protection**: an optional 4-6 digit PIN stops other people using the app on your PC from opening your profile.

The app enforces a **single instance** (a named mutex), so two profiles can never run at the same time — switching happens inside one running process.

## User Guide

### First run — creating your first profile

On first launch Vyral shows a full-window overlay ("Select Your Profile") over the main window. With no profiles yet, the list is empty, so:

1. Click **New Profile**.
2. Enter a **Profile Name** — this becomes the folder name. Only letters, numbers, underscores, and hyphens are allowed (validated when you click Create).
3. Optionally enter a **Display Name** (shown in the window title and selector; defaults to the profile name).
4. Optionally enter a **PIN Code** (the field hints "4-6 digits"). If set, this PIN is requested every time the profile is selected.
5. Click **Create**.

The profile is created, becomes the current profile, and the app loads it. On first run this overlay has no cancel button — creating a profile is the only way forward. From there, continue with [Quick Start](quick-start.md) to connect your first platform and run your first command.

### Every launch after that — choosing a profile

What you see at startup depends on how many profiles exist (`App.xaml.cs:332-373`):

| Situation | What happens |
|---|---|
| No profiles, or `current_profile.txt` is empty/missing | Selector overlay with an empty list — you must create a profile |
| Saved current profile no longer exists on disk | Selector overlay |
| Exactly one profile | Loads silently — no selector shown |
| Multiple profiles, current profile has **Auto-Login** enabled | Loads silently |
| Multiple profiles, no Auto-Login | Selector overlay |

The selector lists profiles ordered by **last used** (most recent first). A padlock icon marks PIN-protected profiles — it renders correctly since the `Profile` model gained its computed `HasPin` property (fixed 2026-08-15).

To select a profile:

- **No PIN set**: click the profile row — it loads immediately.
- **PIN set**: clicking the row reveals an **Enter PIN** box (max 6 characters). Type the PIN and press **Continue**. A wrong PIN shows "Invalid PIN. Please try again." and lets you retry; the overlay stays up.

### Creating additional profiles

Two places:

- **Startup selector** — click **New Profile**, fill the form, **Create**. The new profile is created *and* immediately switched to.
- **Settings → Profile → Profile Management → New Profile** — creates the profile only; you stay on your current profile. (See [Settings Overview](../settings/overview.md).)

### Switching profiles while the app is running

From **Settings → Profile → Profile Management**, each profile row offers **Switch Profile** (PIN prompt if protected). Switching is *hot* — no restart needed:

1. All platform connections (Twitch, Kick, YouTube) are disconnected.
2. Workflow triggers and — if running — the **restream engine** are stopped: an FFmpeg fan-out is streaming the *previous* profile's destinations and keys and must not keep streaming under the new one (the engine is a singleton that outlives its page, so the switch stops it explicitly).
3. The chat window is cleaned and rebuilt.
4. The database layer re-points at the new profile's `vyralbot.db`, and data-bearing caches **follow the switch**: currency invalidates its balances/leaderboards (`CurrencyService` subscribes to the switch event), sounds reload from the new profile's folder (`SoundService`), and event-reaction settings drop their cache (`ChatEventSettingsService`) — so pages opened next show the new profile's data, not the old profile's leftovers.
5. The new profile's settings are loaded and its saved platform connections auto-reconnect.
6. If you were on any page other than Home, the app navigates back to Home so you never see the previous profile's stale data.

### Auto-Login

Settings → Profile → General has an **Auto-Login** toggle with a **Save Settings** button. When enabled for the current profile, startup skips the selector and loads that profile directly. It only matters when **more than one** profile exists — with a single profile, Vyral always auto-loads it.

### PINs

- Set at creation time, or changed later via Settings → Profile → **Change PIN** (asks for the current PIN if one exists, then the new one twice, 4-6 digits enforced in this dialog).
- The PIN is verified against a salted PBKDF2-SHA256 hash stored in `profile.json` (100,000 iterations, 16-byte salt, `v2.salt.hash` format). It is never stored in plain text, and legacy unsalted hashes from older builds still verify (constant-time) and are transparently upgraded to v2 on the next successful unlock. See the security note in [Status & Known Issues](#status--known-issues).

### Deleting, exporting, backing up

All in Settings → Profile → Profile Management:

- **Delete Profile** — asks for confirmation; the *current* profile cannot be deleted from here (switch away first). Deletion removes the profile's entire folder from disk.
- **Export Profile** — zips the whole profile folder as a complete backup (database + credentials + metadata). From Profile Management this writes `<name>_backup.zip` straight to your Documents folder with no further questions; the **Settings → Data Management** export path picks the destination with a save dialog and — because the backup includes your saved platform logins, encrypted with Windows DPAPI **bound to this PC and this Windows user** — first shows a **machine-bound warning**: the credentials cannot be restored on another machine (or another Windows account), where you will need to reconnect Twitch, Kick, YouTube and Discord after importing. The Data Management completion message repeats the warning.
- Importing a backup zip is implemented in the backend but currently has **no button anywhere** in the UI (see [known issues ledger](../reference/known-issues.md)).

Manual backup works too: everything lives in `appdata/`, so copying the folder *while the app is closed* is a full backup.

## How It Works (Developer)

### Model and service

`Profile` (`src/VyralBot.Core/Models/Profile.cs`) is deliberately simple — the profile is mostly a *folder*, not a row: `Name` (folder name), `DisplayName`, `CreatedAt`, `LastUsedAt`, `Description`, `PinCode` (PBKDF2/Base64 **hash**, `v2.salt.hash` format), a computed `HasPin` (drives the selector's padlock), and a `Settings` string dictionary (the only key used today is `"AutoLogin"`).

[ProfileService.cs](../../src/VyralBot.Core/Services/Profiles/ProfileService.cs) implements [IProfileService.cs](../../src/VyralBot.Core/Services/Profiles/IProfileService.cs):

- **Path resolution** (`ProfileService.cs:29-46`): walks **6 directories up** from the exe to find the repo/app root, then uses `<root>/appdata/`. Falls back to the exe's own directory in published builds — this is what makes the app portable. There is no registry or `%APPDATA%` use.
- `GetCurrentProfileName()` — reads `appdata/current_profile.txt`; returns `null` on first run.
- `GetCurrentProfileAsync()` — loads `profile.json`, creates one if the file is missing, and stamps `LastUsedAt` on every load (which is what drives the selector ordering).
- `CreateProfileAsync()` — validates the name against `^[a-zA-Z0-9_-]+$`, hashes the PIN, and auto-writes `current_profile.txt` if this is the first profile (or no current file exists).
- `SwitchProfileAsync()` — validates the PIN, rewrites `current_profile.txt`, clears the in-memory cache, and publishes a `ProfileSwitchedEvent` on the shared [Event Bus](../architecture/event-bus.md). Despite a stale doc comment claiming a restart is required, the app hot-switches.
- `DeleteProfileAsync()` — `GC.Collect` + delay to release SQLite file handles, then recursive directory delete; clears `current_profile.txt` if the deleted profile was current.
- `ValidatePinAsync` / `UpdateProfilePinAsync` / `UpdateProfileAsync` / `ExportProfileAsync` (ZIP to a destination) / `ImportProfileAsync` (unzips, requires `profile.json`, refuses duplicate names).

### Startup decision tree

`App.OnStartup` (`App.xaml.cs:332-373`) shows `MainWindow` **first** (so there is always a window), then decides: no profiles / missing current profile → selector overlay; invalid current name → selector; multiple profiles → selector unless `Settings["AutoLogin"]` is `"True"`; single profile → auto-load. Auto-login and single-profile paths call `MainViewModel.OnProfileLoadedAsync()` fire-and-forget. Full startup sequence: [Startup & Lifecycle](../architecture/startup-lifecycle.md).

### Hot switching

The live selector is **not a window** — it is an inline overlay in [MainWindow.xaml](../../src/VyralBot.UI/MainWindow.xaml) at `MainWindow.xaml:910-1161`, driven by `MainViewModel` state (`ShowProfileSelector`, `ShowPinEntry`, `ShowCreateProfile`, `Profiles`, `ProfilePin`). The PasswordBox handlers in [MainWindow.xaml.cs](../../src/VyralBot.UI/MainWindow.xaml.cs) push the typed PIN into the ViewModel (`MainWindow.xaml.cs:66-80`).

`MainViewModel` subscribes to `ProfileSwitchedEvent` (`MainViewModel.cs:154-210`):

- **Same profile re-selected** → just `OnProfileLoadedAsync()` (no teardown).
- **Different profile** → update title → `DisconnectAllPlatformsAsync()` → `ChatViewModel.Cleanup()` and null it → null the cached Platforms/Settings VMs and dispose their scopes → `OnProfileLoadedAsync()` → navigate Home if any other page is showing.

`OnProfileLoadedAsync` (`MainViewModel.cs:219-292`) then: updates the window title, initializes the webhook coordinator (auto-starts Kestrel + ngrok if configured), initializes default sounds, creates Kick/WebServer/Chat ViewModels, runs `ProfileSettingsLoader.LoadAllSettingsAsync(...)` **before** any auto-connect (so EventBus subscriptions exist before messages can arrive), then `CheckPlatformConnectionsAsync()` auto-connects every platform saved as `Status == "Connected"`, then re-enables Kick webhooks if subscriptions exist.

### Per-profile databases

The mechanism that makes *everything* profile-scoped is [ProfileAwareDataService.cs](../../src/VyralBot.UI/Services/ProfileAwareDataService.cs) — a decorator registered as the singleton `IDataService`. On **every call** it resolves the current profile name and, if it changed, builds a fresh `DapperDataService` aimed at `appdata/profiles/<Name>/data/vyralbot.db`. With no profile selected it falls back to profile name `"temp"` (which is why an `appdata/profiles/temp/` folder appears on disk). `ClearConnection()` drops the cached service, clears all SQLite connection pools and GCs so the DB file can be deleted. Details: [Data Layer](../architecture/data-layer.md).

[ProfileSettingsLoader.cs](../../src/VyralBot.UI/Services/ProfileSettingsLoader.cs) orchestrates the ordered settings load (Kick settings → web-server settings → Chat) after a profile becomes current.

## Files & Code

| File | Role |
|---|---|
| [ProfileService.cs](../../src/VyralBot.Core/Services/Profiles/ProfileService.cs) | Profile backend: create/switch/delete/validate PIN, export/import, path resolution |
| [IProfileService.cs](../../src/VyralBot.Core/Services/Profiles/IProfileService.cs) | Service interface |
| [Profile.cs](../../src/VyralBot.Core/Models/Profile.cs) | Profile model (metadata + PIN hash + Settings dict) |
| [ProfileSwitchedEvent.cs](../../src/VyralBot.Core/Services/EventBus/ProfileSwitchedEvent.cs) | Event Bus event fired on every switch |
| [ProfileAwareDataService.cs](../../src/VyralBot.UI/Services/ProfileAwareDataService.cs) | Routes every `IDataService` call to the current profile's SQLite DB |
| [ProfileSettingsLoader.cs](../../src/VyralBot.UI/Services/ProfileSettingsLoader.cs) | Ordered settings load after profile switch |
| [App.xaml.cs](../../src/VyralBot.UI/App.xaml.cs) | Startup profile decision tree; single-instance mutex |
| [MainViewModel.cs](../../src/VyralBot.UI/ViewModels/MainViewModel.cs) | Selector overlay state, PIN validation, switch handling, `OnProfileLoadedAsync` |
| [MainWindow.xaml](../../src/VyralBot.UI/MainWindow.xaml) | Selector/creation overlay UI (`MainWindow.xaml:910-1161`) |
| [MainWindow.xaml.cs](../../src/VyralBot.UI/MainWindow.xaml.cs) | PasswordBox → ViewModel plumbing for PINs |
| [SettingsViewModel.cs](../../src/VyralBot.UI/ViewModels/SettingsViewModel.cs) | Settings-page profile management (create/switch/delete/export/change PIN, Auto-Login) |
| [SettingsView.xaml](../../src/VyralBot.UI/Views/SettingsView.xaml) | Settings → Profile UI and dialogs |
| [ProfileSelectorWindow.xaml](../../src/VyralBot.UI/Views/ProfileSelectorWindow.xaml) | Dead code — a full standalone selector window, never instantiated |
| [ProfileSelectorViewModel.cs](../../src/VyralBot.UI/ViewModels/ProfileSelectorViewModel.cs) | Dead code — its ViewModel (has delete support the live overlay lacks) |

Switching — write the file, then tell the world via the EventBus:

```csharp
public async Task SwitchProfileAsync(string name, string? pinCode = null)
{
    var oldProfileName = GetCurrentProfileName() ?? "none";
    var profilePath = GetProfilePath(name);

    if (!Directory.Exists(profilePath))
        throw new InvalidOperationException($"Profile '{name}' does not exist");

    // Validate PIN if required
    if (!await ValidatePinAsync(name, pinCode ?? ""))
        throw new UnauthorizedAccessException("Invalid PIN code");

    // Write new current profile
    await File.WriteAllTextAsync(_currentProfilePath, name);

    // Clear cached current profile to force reload
    _currentProfile = null;

    // Publish profile switched event
    if (_eventBus != null)
    {
        await _eventBus.PublishAsync(new ProfileSwitchedEvent
        {
            Source = "System",
            OldProfileName = oldProfileName,
            NewProfileName = name
        });
    }
}
```

How every database call becomes profile-aware (`ProfileAwareDataService.cs:26-46`):

```csharp
private DapperDataService GetCurrentService()
{
    var profileName = _profileService.GetCurrentProfileName() ?? "temp";

    // If profile changed, recreate the service with new database path
    if (_currentService == null || _currentProfileName != profileName)
    {
        _currentProfileName = profileName;

        var profileDataPath = _profileService.GetProfileDataPath(profileName);
        Directory.CreateDirectory(profileDataPath);

        var dbPath = Path.Combine(profileDataPath, "vyralbot.db");
        var connectionString = $"Data Source={dbPath}";

        _currentService = new DapperDataService(connectionString, _logger);
    }

    return _currentService;
}
```

The startup decision tree (`App.xaml.cs:337-373`):

```csharp
// First run - no profiles exist, force create new profile
if (profiles.Count() == 0 || string.IsNullOrEmpty(initialProfileName))
{
    await mainViewModel.ShowProfileSelectorAsync();
}
// Validate current profile exists in profiles list
else if (!profiles.Any(p => p.Name == initialProfileName))
{
    await mainViewModel.ShowProfileSelectorAsync();
}
else if (profiles.Count() > 1)
{
    var currentProfile = await profileService.GetCurrentProfileAsync();
    var hasAutoLogin = currentProfile?.Settings.TryGetValue("AutoLogin", out var autoLoginValue) == true
        && bool.TryParse(autoLoginValue, out var enabled) && enabled;

    if (hasAutoLogin)
        _ = mainViewModel.OnProfileLoadedAsync();   // skip selector
    else
        await mainViewModel.ShowProfileSelectorAsync();
}
else
{
    _ = mainViewModel.OnProfileLoadedAsync();       // single profile: auto-load
}
```

## Data & Settings

On-disk layout (all under the app root's `appdata/` — see also [Database Reference](../reference/database-reference.md)):

```
appdata/
├── current_profile.txt              # active profile name (plain text, e.g. "DripDrip")
└── profiles/
    ├── <Name>/
    │   ├── profile.json             # Name, DisplayName, CreatedAt, LastUsedAt, PinCode (hash), Settings{}
    │   ├── data/vyralbot.db         # this profile's ENTIRE SQLite database (34 tables)
    │   ├── twitch_credentials.json  # Twitch OAuth client id/secret (plaintext)
    │   ├── kick_credentials.json    # checked by the Platforms page
    │   └── ngrok_credentials.json   # ngrok auth token (plaintext)
    └── temp/data/vyralbot.db        # fallback DB used before any profile is selected
```

| Data | Where | Notes |
|---|---|---|
| Active profile name | `appdata/current_profile.txt` | Rewritten on every switch |
| Profile metadata + PIN hash + `AutoLogin` | `appdata/profiles/<Name>/profile.json` | JSON, written by ProfileService |
| All feature data (platforms, tokens, commands, currency, polls, chat history, ...) | `appdata/profiles/<Name>/data/vyralbot.db` | Per-profile SQLite; see [Data Layer](../architecture/data-layer.md) |
| Twitch / Kick / ngrok credentials | JSON files in the profile folder | Not in the DB |
| **Global, not per-profile** | app install, `logs/`, DI singleton caches, the ngrok agent's own config | The ngrok wizard also writes a machine/user-level `ngrok config add-authtoken`, shared across profiles — see [Web Server & ngrok](../settings/web-server-ngrok.md) |

## Status & Known Issues

**Works**: creating, listing, selecting (with PIN), hot switching with full teardown/reload, Auto-Login, per-profile DB isolation, export to ZIP, delete + change PIN via the Settings page, fully portable storage, PBKDF2-hashed PINs with transparent legacy upgrade, and the padlock icon on the startup selector.

Honest caveats — details in the [known issues ledger](../reference/known-issues.md):

- **PIN hashes are salted PBKDF2 now** (fixed 2026-08-15) — 100k iterations of PBKDF2-SHA256 with a per-profile salt and constant-time comparisons. The PIN remains a convenience lock rather than full-disk security (the profile data itself is unencrypted on disk), but it is no longer trivially brute-forceable from `profile.json`.
- **No PIN format validation in the live UI** — the create/entry boxes only enforce `MaxLength=6`; nothing enforces digits or a 4-char minimum. The Settings change-PIN dialog does validate 4-6 digits.
- **`ImportProfileAsync` has no UI caller** — exported backups cannot be restored through the app; unzip manually into `appdata/profiles/`.
- **Dead code**: `ProfileSelectorWindow`/`ProfileSelectorViewModel` are a complete standalone selector (including delete-profile, which the live overlay lacks) that is never instantiated — only a DI registration remains (`App.xaml.cs:223`).
- **Single instance enforced** (`VyralBot_SingleInstance_Mutex`, `App.xaml.cs:59`) — two profiles cannot run side by side.
- Profile deletion is blocked for the current profile in Settings; deletion works by GC-pause + recursive folder delete and can fail if any file is still locked.
