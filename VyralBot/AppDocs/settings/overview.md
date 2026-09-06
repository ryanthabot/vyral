# Settings Overview

> The Settings page is a 13-category hub where 5 categories are implemented today — Profile management, Platforms, Variables, Viewer Hub, and API & Webhooks — and 8 are "Coming Soon" placeholders.

## Overview

Open **Settings** from the bottom of the sidebar. The page is a two-pane layout: a 280px category list on the left and a content area on the right that swaps based on `SelectedCategory` (`SettingsView.xaml`).

| Category | State | What is actually there |
|---|---|---|
| **Profile** | Implemented | Auto-Login toggle plus full profile management (list, create, switch, export, delete, change PIN) |
| **Theme** | Placeholder | "Coming Soon..." text only |
| **Platforms** | Implemented | Grid of 6 platform cards (only Twitch and Kick navigate) plus the **Startup & Connection** section — see below |
| **Data Management** | Placeholder | "Coming Soon..." text only |
| **Notifications** | Placeholder | "Coming Soon..." text only |
| **Commands** | Placeholder | "Coming Soon..." text only (real commands page lives under Chat Features) |
| **Variables** | Implemented | Hosts the full Variables manager |
| **Viewer Hub** | Implemented | Multi-Platform Bonus mode/settings, Vyral Goals management, and Linked Viewers with per-account unlink — see below |
| **Timers** | Placeholder | "Coming Soon..." text only |
| **Automation** | Placeholder | "Coming Soon..." text only |
| **API & Webhooks** | Implemented | Web server + ngrok configuration — see [API, Web Server & ngrok](web-server-ngrok.md) |
| **Performance** | Placeholder | "Coming Soon..." text only |
| **Security** | Placeholder | "Coming Soon..." text only |
| **Advanced** | Placeholder | "Coming Soon..." text only |

Note that Audio, Alert, and Sounds settings do **not** live here — they are separate top-level pages in the sidebar ([Audio Settings](../stream-tools/audio.md), [Visual Alerts](../stream-tools/visual-alerts.md), [Sounds](../stream-tools/sounds.md)).

## User Guide

Themes apply **live** — picking a theme (or applying a custom one) re-colors the whole app instantly, no restart needed: every page, card, dialog, the Chat window, and even the flow editor's canvas/wires/cards re-skin in place. Built-in themes also clear any leftover custom-accent overrides, so switching from a Custom theme back to a built-in one is clean. A handful of colors are intentionally theme-independent: platform identity accents (Kick green, Twitch purple, Discord blurple), the flow editor's per-panel rail accents, alert-overlay colors (rendered over live video), and the color-picker swatches. The **Advanced** tab also has a **🔄 Restart App** button that closes Vyral and starts it again for you. The Theme page also has a **Brightness Levels** card with two sliders — one for dark themes, one for light — that darken or lighten the entire app's backgrounds, cards, borders, and canvas in real time as you drag (0 = the theme's native palette, up to ±100 toward black or white). Your level is remembered per base theme (dark and light separately), applies on startup, survives theme switches, and the flow editor's canvas follows it too. Custom themes also get a full **color picker** (saturation/value square + hue strip + live hex and RGB readout) — drag it and the entire app's accent recolors in real time.

### Profile category

The Profile category has two sections:

**General — Auto-Login.** A single toggle ("Auto-Login") with a Save Settings button. When enabled and you have more than one profile, Vyral skips the profile selector at launch and loads the current profile automatically. With only one profile the app auto-loads anyway, so the toggle only matters for multi-profile setups. The value is stored in the profile's own `profile.json` file, not the database.

**Support Links.** Two text boxes — **Discord Invite URL** and **Ideas URL** — with a Save button (`SaveSupportLinksCommand`). The values persist to the `Configs` keys `Support.DiscordUrl` and `Support.IdeasUrl` (category "Support") and drive the Home page's [Discord and Ideas & Suggestions rows](../home/dashboard.md): clicking those rows opens the configured URL in the system browser. Until a URL is set, the rows show a MessageBox pointing here.

**Profile Management.** A list of all profiles (each showing display name, name, and dates) with these actions:

- **New Profile** — dialog asking for Name (letters, numbers, underscore, hyphen only), optional Display Name, and an optional 4–6 digit PIN. Creating here does not switch to the new profile.
- **Switch** — if the profile has a PIN, a PIN-entry dialog appears first. Switching is hot: the app disconnects all platforms, reloads every setting from the new profile's database, and drops you back on Home. See [Profiles](../getting-started/profiles.md) for the full story.
- **Export** — zips the profile's entire folder (database, credentials, metadata) to your Documents folder as a timestamped backup.
- **Delete** — asks for confirmation; the currently active profile cannot be deleted from here. Import exists in the service layer but has no button anywhere.
- **Change PIN** — dialog asking for current PIN (if one is set), new PIN, and confirmation, 4–6 digits.

PINs are stored as salted PBKDF2-SHA256 hashes (100k iterations, 16-byte salt, `v2.salt.hash`; legacy unsalted hashes verify constant-time and transparently re-hash on success) — still a convenience lock rather than cryptographic protection, but no longer trivially brute-forceable ([known issues ledger](../reference/known-issues.md)).

### Platforms category

Six cards: Twitch, Kick, YouTube, TikTok, Discord, OBS.

- **Twitch** card navigates to a settings page that is currently a stub — "Configuration settings coming soon...". Real Twitch configuration happens on the [Platforms](../platforms/overview.md) page (the OAuth setup wizard) and the Twitch dashboard.
- **Kick** card navigates to a real settings page (see below).
- **YouTube / Discord / OBS cards are disabled with explanatory tooltips** (fixed 2026-08-15), and TikTok's is too: YouTube and Discord are configured entirely through their Platforms-page wizards (Discord's bots and channel selection live on its [Discord Bots page](../platforms/discord.md#multiple-bots)), and neither platform has a settings sub-page; TikTok/OBS have no backend at all. The cards no longer look clickable-and-dead — note this says nothing about the platforms themselves, only that this page has nothing to configure for them.

**Startup & Connection.** Launch behavior for the app and its Discord bot, in one place:

- *Discord auto-start* — starts your **primary Discord bot** when the app launches. Extra bots each have their own **"Start this bot when the app launches"** toggle on the [Discord Bots page](../platforms/discord.md#multiple-bots) instead.
- *"Launch VyralBot when my PC starts"* — registers a **Windows Run-key entry for the current user**, so Vyral starts automatically when you sign in to Windows. It is per-user: it follows the Windows account you set it under, and other users' sign-ins are unaffected.

**The Kick settings page** (only reached from here) shows, when Kick is connected:

- **Connection Status** — status dot/text and a Disconnect button.
- **Account Settings** — Username and Channel boxes (both read-only) plus Save Changes. The Channel box populates correctly (the `Channel` binding mismatch was fixed 2026-08-15).
- **Webhook Settings** — an "Enable Webhooks" toggle. Enabling requires a Kick access token, a user ID, and the webhook server to be running (otherwise you are told to start it in Settings → API & Webhooks first). The toggle un-checks itself if the webhook server stops. Details in [API, Web Server & ngrok](web-server-ngrok.md).
- **Chat Settings** — three real toggles, each saved immediately:
  - *Enable Kick Chat (Receive Messages)* — controls whether Kick messages appear in the unified chat feed; the change applies live.
  - *Auto-start chat on app launch* — reconnects Kick chat automatically on startup.
  - *Start/Stop Chat* button — connects or disconnects using stored credentials.
  - *Enable auto replies (thank followers / respond to unfollows)* — stored separately from the per-event auto-reply templates.

### Variables category

Hosts the complete [Variables](../chat-features/variables.md) manager — search, category filter, add/edit/delete of editable variables, and a view of the seeded system and social variables. This is currently the only navigation path to the Variables manager; it has no sidebar entry of its own.

### Viewer Hub category

The Viewer Hub is the streamer's control panel for the viewer-facing economy that grew out of [account linking](../chat-features/account-linking.md). Four areas:

**Multi-Platform Bonus.** Picks the mode for the cross-platform earnings bonus documented in [Vyral Goals](../stream-tools/vyral-goals.md#the-multi-platform-bonus): **Off** (default — linked viewers earn at 1x), **OptIn** (only viewers who typed `!mp on` get it), or **Auto** (every linked viewer). The bonus ladder amounts are editable here too — 1.25x at 2 linked platforms, 1.5x at 3, 2.0x at 4, 2.5x at 5+ — and persist to the `MultiPlatform.Mode` / `MultiPlatform.Bonus2..Bonus5` config keys (30-second service cache, so changes apply within half a minute).

**Vyral Goals.** Manage the goal list documented in [Vyral Goals](../stream-tools/vyral-goals.md): the four seeded defaults (Bridge Builder, Everywhere At Once, Loyal Follower, Tier 2 Supporter) plus anything you add. Each goal has a name, condition type (`LinkedPlatforms` / `FollowPlatforms` / `SubAnyPlatform` / `SubTier`), target count, reward type (points / multiplier / badge), and reward value; goals can be disabled without deleting. Completing a goal is announced in chat on every connected platform.

**Linked Viewers.** Lists every [link group](../chat-features/viewer-profiles.md) account-by-account, and each linked account row carries its own **Unlink** button — the streamer-side override for removing an account a viewer can't remove themselves (banned users, disputes). Clicking through opens the same [viewer profile window](../chat-features/viewer-profiles.md) that chat username clicks open.

**Account Linking.** The **Account linking Discord bot** selector picks which Discord bot handles the [link/delink DM flow](../chat-features/account-linking.md#choosing-your-linking-bot): single-select, falling back to the Primary bot when unset, and kept in sync with the Roles card on each bot's page in the [Discord section](../platforms/discord.md#bot-roles). Replies to link DMs always come from the bot that received the DM.

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
| Profile metadata, PIN hash | `profile.json` | ProfileService |
| Web server port / region / auto-start / tunnel URL | DB `WebServerSettings` row (Id=1) | Web server settings page + coordinator |
| ngrok auth token | `<profile>/ngrok_credentials.json` (plaintext) | ngrok wizard + the settings-page token editor |
| Twitch OAuth client id/secret | `<profile>/twitch_credentials.json` | `TwitchOAuthService` |
| Platform tokens, status, chat enabled / auto-start flags | DB `Platforms` row | Setup wizards, settings pages, auto-connect |
| Launch-on-Windows-startup (Run key) | Windows registry Run key (HKCU, per-user) | Settings → Platforms → Startup & Connection |
| Kick auto-replies toggle | DB `Configs` key `Kick.AutoRepliesEnabled` | Kick settings page |
| Support links (Discord invite, Ideas URL) | DB `Configs` keys `Support.DiscordUrl` / `Support.IdeasUrl` | Settings → Profile → Support Links |
| Per-event display/auto-reply settings | DB `ChatEventSettings` rows | Chat Hub VMs via `ChatEventSettingsService` |
| Custom variables | DB `Variables` table | Variables manager |

See [Data Layer](../architecture/data-layer.md) for how the per-profile database routing works.

### Load order at startup

`ProfileSettingsLoader.LoadAllSettingsAsync` runs after a profile loads and initializes, in strict order: (optional) platform credentials → Kick settings → web server settings → ChatViewModel. This deliberately happens **before** platform auto-connect so that EventBus subscriptions and settings exist before any message can arrive (`ProfileSettingsLoader.cs:28-68`). The full boot sequence is documented in [Startup & Lifecycle](../architecture/startup-lifecycle.md).

### The auto-connect side effect (intended behavior)

When `MainViewModel.CheckPlatformConnectionsAsync` auto-connects a platform whose DB status is "Connected", it silently flips that platform's `<Platform>ChatEnabled` flag from false to true in the database on first connect (`MainViewModel.cs:437-443` for Twitch, with matching blocks for Kick and YouTube). After review this is **intended**: a connected platform is expected to show in the unified feed by default (KI-14). If you turn chat display off but stay connected, the flag comes back on at next auto-connect.

### ChatEventSettingsService cache (profile-aware since 2026-08-15)

`ChatEventSettingsService` caches `ChatEventSettings` rows in a dictionary keyed `platform:eventType`. It subscribes to `ProfileSwitchedEvent` and invalidates the cache on every switch, so the new profile's event settings load immediately instead of serving stale rows until restart.

## Files & Code

| File | Role |
|---|---|
| [SettingsView.xaml](../../src/VyralBot.UI/Views/SettingsView.xaml) | The whole Settings page: category sidebar, all 13 category panels, and the four dialogs (delete confirm, change PIN, create profile, switch profile) |
| [SettingsViewModel.cs](../../src/VyralBot.UI/ViewModels/SettingsViewModel.cs) | Category selection, AutoLogin load/save, all profile-management dialogs, Twitch/Kick settings navigation commands |
| [TwitchSettingsViewModel.cs](../../src/VyralBot.UI/ViewModels/TwitchSettingsViewModel.cs) | 27-line stub — status message only |
| [KickSettingsViewModel.cs](../../src/VyralBot.UI/ViewModels/KickSettingsViewModel.cs) | Real Kick settings: disconnect, account save, webhook toggle, chat toggles, auto-replies flag |
| [KickSettingsView.xaml](../../src/VyralBot.UI/Views/KickSettingsView.xaml) | Kick settings UI (binds `ChannelName`) |
| [VariablesViewModel.cs](../../src/VyralBot.UI/ViewModels/VariablesViewModel.cs) | Variables manager hosted under the Variables category |
| [ProfileService.cs](../../src/VyralBot.Core/Services/Profiles/ProfileService.cs) | Profile CRUD, PIN hashing, switch (publishes `ProfileSwitchedEvent`), export |
| [ProfileSettingsLoader.cs](../../src/VyralBot.UI/Services/ProfileSettingsLoader.cs) | Strictly-ordered settings load after profile selection |
| [ChatEventSettingsService.cs](../../src/VyralBot.Core/Services/Chat/ChatEventSettingsService.cs) | Cached wrapper over `ChatEventSettings` rows (invalidated on profile switch) |
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

- **8 of 13 categories are placeholders** — Theme, Data Management, Notifications, Commands, Timers, Automation, Performance, Security, and Advanced show only "Coming Soon...".
- **YouTube/TikTok/Discord/OBS platform cards are disabled with explanatory tooltips** — they no longer look clickable (fixed 2026-08-15); only Twitch and Kick navigate. (Discord being disabled here does not mean the platform is unavailable — it is configured through the [Platforms wizard](../platforms/discord.md) and has no settings sub-page.) See the [known issues ledger](../reference/known-issues.md).
- **Auto-connect re-enables chat display** — the `<Platform>ChatEnabled` flag is force-set true on first successful auto-connect (intended behavior — KI-14).
- **Twitch settings page is a stub** — nothing loads or saves there.
- **Alert Settings is not part of this page** — it lives under Stream Tools → Visual Alerts and persists to the `AlertSettings` Configs key (works fully since 2026-08-15).
