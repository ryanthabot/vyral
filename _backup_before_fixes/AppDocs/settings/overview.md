# Settings Overview

> The Settings page is a 13-category hub where 4 categories are implemented today — Profile management, Platforms, Variables, and API & Webhooks — and 9 are "Coming Soon" placeholders.

## Overview

Open **Settings** from the bottom of the sidebar. The page is a two-pane layout: a 280px category list on the left and a content area on the right that swaps based on `SelectedCategory` (`SettingsView.xaml`).

| Category | State | What is actually there |
|---|---|---|
| **Profile** | Implemented | Auto-Login toggle plus full profile management (list, create, switch, export, delete, change PIN) |
| **Theme** | Placeholder | "Coming Soon..." text only |
| **Platforms** | Implemented | Grid of 6 platform cards — but only Twitch and Kick navigate anywhere |
| **Data Management** | Placeholder | "Coming Soon..." text only |
| **Notifications** | Placeholder | "Coming Soon..." text only |
| **Commands** | Placeholder | "Coming Soon..." text only (real commands page lives under Chat Features) |
| **Variables** | Implemented | Hosts the full Variables manager |
| **Timers** | Placeholder | "Coming Soon..." text only |
| **Automation** | Placeholder | "Coming Soon..." text only |
| **API & Webhooks** | Implemented | Web server + ngrok configuration — see [API, Web Server & ngrok](web-server-ngrok.md) |
| **Performance** | Placeholder | "Coming Soon..." text only |
| **Security** | Placeholder | "Coming Soon..." text only |
| **Advanced** | Placeholder | "Coming Soon..." text only |

Note that Audio, Alert, and Sounds settings do **not** live here — they are separate top-level pages in the sidebar ([Audio Settings](../stream-tools/audio.md), [Visual Alerts](../stream-tools/visual-alerts.md), [Sounds](../stream-tools/sounds.md)).

## User Guide

### Profile category

The Profile category has two sections:

**General — Auto-Login.** A single toggle ("Auto-Login") with a Save Settings button. When enabled and you have more than one profile, VyralBot skips the profile selector at launch and loads the current profile automatically. With only one profile the app auto-loads anyway, so the toggle only matters for multi-profile setups. The value is stored in the profile's own `profile.json` file, not the database.

**Profile Management.** A list of all profiles (each showing display name, name, and dates) with these actions:

- **New Profile** — dialog asking for Name (letters, numbers, underscore, hyphen only), optional Display Name, and an optional 4–6 digit PIN. Creating here does not switch to the new profile.
- **Switch** — if the profile has a PIN, a PIN-entry dialog appears first. Switching is hot: the app disconnects all platforms, reloads every setting from the new profile's database, and drops you back on Home. See [Profiles](../getting-started/profiles.md) for the full story.
- **Export** — zips the profile's entire folder (database, credentials, metadata) to your Documents folder as a timestamped backup.
- **Delete** — asks for confirmation; the currently active profile cannot be deleted from here. Import exists in the service layer but has no button anywhere.
- **Change PIN** — dialog asking for current PIN (if one is set), new PIN, and confirmation, 4–6 digits.

PINs are stored as unsalted SHA256 hashes — treat them as a convenience lock, not cryptographic protection ([known issues ledger](../reference/known-issues.md)).

### Platforms category

Six cards: Twitch, Kick, YouTube, TikTok, Discord, OBS.

- **Twitch** card navigates to a settings page that is currently a stub — "Configuration settings coming soon...". Real Twitch configuration happens on the [Platforms](../platforms/overview.md) page (the OAuth setup wizard) and the Twitch dashboard.
- **Kick** card navigates to a real settings page (see below).
- **YouTube / TikTok / Discord / OBS** cards have no navigation wired at all. They render and dim based on connection state, but clicking them does nothing even when the platform is connected.

**The Kick settings page** (only reached from here) shows, when Kick is connected:

- **Connection Status** — status dot/text and a Disconnect button.
- **Account Settings** — Username and Channel boxes (both read-only) plus Save Changes. Be aware: the Channel box never populates because of a binding bug (see Gotchas).
- **Webhook Settings** — an "Enable Webhooks" toggle. Enabling requires a Kick access token, a user ID, and the webhook server to be running (otherwise you are told to start it in Settings → API & Webhooks first). The toggle un-checks itself if the webhook server stops. Details in [API, Web Server & ngrok](web-server-ngrok.md).
- **Chat Settings** — three real toggles, each saved immediately:
  - *Enable Kick Chat (Receive Messages)* — controls whether Kick messages appear in the unified chat feed; the change applies live.
  - *Auto-start chat on app launch* — reconnects Kick chat automatically on startup.
  - *Start/Stop Chat* button — connects or disconnects using stored credentials.
  - *Enable auto replies (thank followers / respond to unfollows)* — stored separately from the per-event auto-reply templates.

### Variables category

Hosts the complete [Variables](../chat-features/variables.md) manager — search, category filter, add/edit/delete of editable variables, and a view of the seeded system and social variables. This is currently the only navigation path to the Variables manager; it has no sidebar entry of its own.

## How It Works (Developer)

### Three families of per-platform settings

The codebase has three distinct things called "platform settings" — keep them straight when reading code:

1. **Settings → Platforms → Twitch/Kick cards** → `TwitchSettingsViewModel` / `KickSettingsViewModel` sub-pages (this page). The Twitch one is a 27-line stub; the Kick one is real.
2. **Chat Hub per-platform chat settings** — `TwitchChatSettingsViewModel`, `KickChatSettingsViewModel`, etc. These edit per-event display/auto-reply rows (`ChatEventSettings` table) and the `<Platform>ChatEnabled` routing flags. Covered in [Chat Settings](../chat/chat-settings.md).
3. **Platforms page web-setup wizards** — `TwitchWebSetupViewModel` and friends; OAuth flows that *write* the platform rows and tokens. These are setup, not settings.

### What each setting persists, and where

| Setting | Storage | Written by |
|---|---|---|
| AutoLogin | `profile.json` → `Settings["AutoLogin"]` | Settings page |
| Profile metadata, PIN hash | `profile.json` | `ProfileService` |
| Web server port / region / auto-start / tunnel URL | DB `WebServerSettings` row (Id=1) | Web server settings page + coordinator |
| ngrok auth token | `<profile>/ngrok_credentials.json` (plaintext) | ngrok wizard only |
| Twitch OAuth client id/secret | `<profile>/twitch_credentials.json` | `TwitchOAuthService` |
| Platform tokens, status, chat enabled / auto-start flags | DB `Platforms` row | Setup wizards, settings pages, auto-connect |
| Kick auto-replies toggle | DB `Configs` key `Kick.AutoRepliesEnabled` | Kick settings page |
| Per-event display/auto-reply settings | DB `ChatEventSettings` rows | Chat Hub VMs via `ChatEventSettingsService` |
| Custom variables | DB `Variables` table | Variables manager |

See [Data Layer](../architecture/data-layer.md) for how the per-profile database routing works.

### Load order at startup

`ProfileSettingsLoader.LoadAllSettingsAsync` runs after a profile loads and initializes, in strict order: (optional) platform credentials → Kick settings → web server settings → ChatViewModel. This deliberately happens **before** platform auto-connect so that EventBus subscriptions and settings exist before any message can arrive (`ProfileSettingsLoader.cs:28-68`). The full boot sequence is documented in [Startup & Lifecycle](../architecture/startup-lifecycle.md).

### The auto-connect side effect

When `MainViewModel.CheckPlatformConnectionsAsync` auto-connects a platform whose DB status is "Connected", it silently flips that platform's `<Platform>ChatEnabled` flag from false to true in the database on first connect (`MainViewModel.cs:437-443` for Twitch, with matching blocks for Kick and YouTube). A display setting is therefore changed as a side effect of connecting — if you turn chat display off but stay connected, the flag comes back on at next auto-connect.

### ChatEventSettingsService cache is not profile-aware

`ChatEventSettingsService` (used by the Chat Hub settings pages) caches all `ChatEventSettings` rows in a plain `Dictionary` keyed only by `platform:eventType`. The cache loads once per process and is never invalidated on profile switch, so after switching profiles the app can serve the previous profile's event settings until restart (`ChatEventSettingsService.cs:32-64`).

## Files & Code

| File | Role |
|---|---|
| [SettingsView.xaml](../../src/VyralBot.UI/Views/SettingsView.xaml) | The whole Settings page: category sidebar, all 13 category panels, and the four dialogs (delete confirm, change PIN, create profile, switch profile) |
| [SettingsViewModel.cs](../../src/VyralBot.UI/ViewModels/SettingsViewModel.cs) | Category selection, AutoLogin load/save, all profile-management dialogs, Twitch/Kick settings navigation commands |
| [TwitchSettingsViewModel.cs](../../src/VyralBot.UI/ViewModels/TwitchSettingsViewModel.cs) | 27-line stub — status message only |
| [KickSettingsViewModel.cs](../../src/VyralBot.UI/ViewModels/KickSettingsViewModel.cs) | Real Kick settings: disconnect, account save, webhook toggle, chat toggles, auto-replies flag |
| [KickSettingsView.xaml](../../src/VyralBot.UI/Views/KickSettingsView.xaml) | Kick settings UI (contains the `Channel` binding bug) |
| [VariablesViewModel.cs](../../src/VyralBot.UI/ViewModels/VariablesViewModel.cs) | Variables manager hosted under the Variables category |
| [ProfileService.cs](../../src/VyralBot.Core/Services/Profiles/ProfileService.cs) | Profile CRUD, PIN hashing, switch (publishes `ProfileSwitchedEvent`), export |
| [ProfileSettingsLoader.cs](../../src/VyralBot.UI/Services/ProfileSettingsLoader.cs) | Strictly-ordered settings load after profile selection |
| [ChatEventSettingsService.cs](../../src/VyralBot.Core/Services/Chat/ChatEventSettingsService.cs) | Cached wrapper over `ChatEventSettings` rows (cache not profile-aware) |
| [WebServerEntities.cs](../../src/VyralBot.Data/Entities/WebServerEntities.cs) | `WebServerSettings` and `WebhookSubscription` entities |

How the AutoLogin flag is persisted (plain string in the profile's settings dictionary):

```csharp
// SettingsViewModel.cs — SaveAutoLoginAsync
var currentProfile = await _profileService.GetCurrentProfileAsync();
currentProfile.Settings["AutoLogin"] = AutoLogin.ToString();
await _profileService.UpdateProfileAsync(currentProfile);
```

How the Kick chat toggle routes live to the chat feed (saved immediately, then broadcast):

```csharp
// KickSettingsViewModel.cs — toggle handler
platform.KickChatEnabled = value;
await _dataService.SavePlatformAsync(platform);
await _eventBus.PublishAsync(new PlatformChatRoutingChangedEvent { ... });
// ChatViewModel subscribes to PlatformChatRoutingChangedEvent and
// updates its per-platform display filter without a restart.
```

## Gotchas / Status & Known Issues

- **9 of 13 categories are placeholders** — Theme, Data Management, Notifications, Commands, Timers, Automation, Performance, Security, and Advanced show only "Coming Soon...".
- **YouTube/TikTok/Discord/OBS platform cards are dead buttons** — no command binding; only Twitch and Kick navigate. See the [known issues ledger](../reference/known-issues.md).
- **Kick settings Channel box stays empty** — `KickSettingsView.xaml:159` binds a `Channel` property that does not exist on the ViewModel (it is named `ChannelName`), a binding mismatch.
- **Auto-connect silently re-enables chat display** — the `<Platform>ChatEnabled` flag is force-set true on first successful auto-connect, overriding an explicit off.
- **`ChatEventSettingsService` serves stale rows after a profile switch** until the process restarts.
- **Twitch settings page is a stub** — nothing loads or saves there.
- **Alert Settings is not part of this page and persists nothing** — its Save button shows success but writes no data ([known issues ledger](../reference/known-issues.md)).
