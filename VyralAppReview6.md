# VyralBot Settings Review (App Review #6)

Scope: ALL settings — settings pages, profile system, persistence, and how each setting affects other subsystems.
Method: read-only code review. All claims sourced from code with file references.

---

## 1. Settings Page Overview (src/VyralBot.UI/Views/SettingsView.xaml)

The Settings page is a two-pane layout: a 280px category sidebar + content area bound to `SelectedCategory`. There are 13 categories (SettingsView.xaml:45-317), most of which are placeholders ("Coming Soon..."):

| Category | State | Backing |
|---|---|---|
| Profile | IMPLEMENTED | General (Auto-Login) + Profile Management (list/create/switch/export/delete/PIN) |
| Theme | Placeholder ("Coming Soon") | none |
| Platforms | IMPLEMENTED | Grid of 6 platform cards (Twitch, Kick, YouTube, TikTok, Discord, OBS) → per-platform settings sub-views |
| Data Management | Placeholder | none |
| Notifications | Placeholder | none |
| Commands | Placeholder | none |
| Variables | IMPLEMENTED | Hosts `VariablesViewModel` via ContentControl (SettingsView.xaml:833) |
| Timers | Placeholder | none |
| Automation | Placeholder | none |
| API & Webhooks | IMPLEMENTED | Hosts `WebServerSettingsViewModel` via ContentControl (SettingsView.xaml:861) |
| Performance | Placeholder | none |
| Security | Placeholder | none |
| Advanced | Placeholder | none |

Only Twitch and Kick cards navigate anywhere (`NavigateToTwitchSettingsCommand` / `NavigateToKickSettingsCommand`, SettingsViewModel.cs:576-594). The YouTube/TikTok/Discord/OBS cards have no Command binding — they are dead buttons, disabled unless `IsYouTubeConnected` etc. (which is loaded from the DB `Platforms` table via `IDataService.GetPlatformByNameAsync`, SettingsViewModel.cs:154-180).

Overlays implemented in SettingsView: Delete Profile confirmation, Change PIN dialog (current/new/confirm, 4-6 digits), Create Profile dialog (name/display name/PIN), Switch Profile dialog (PIN entry).

## 2. Profile System

### 2.1 Model — src/VyralBot.Core/Models/Profile.cs
- `Name` (required), `DisplayName`, `CreatedAt`, `LastUsedAt`, `Description`, `PinCode` (SHA256 base64 hash), `Settings` — a `Dictionary<string,string>` for arbitrary per-profile key/values (currently the only known key used is `"AutoLogin"`).

### 2.2 Storage layout (appdata/)
- `appdata/current_profile.txt` — plain text file containing current profile name (e.g. `Drip`).
- `appdata/profiles/<Name>/profile.json` — profile metadata incl. hashed PIN + Settings dict.
- `appdata/profiles/<Name>/data/vyralbot.db` — per-profile SQLite database.
- `appdata/profiles/<Name>/*.json` — per-profile credential files (observed: `ngrok_credentials.json`, `twitch_credentials.json`).
- ProfileService resolves the appdata root by walking up 6 directories from the exe location (src/VyralBot.Core/Services/Profiles/ProfileService.cs:29-40); falls back to exe dir for published builds (portable-style storage).

### 2.3 ProfileService (src/VyralBot.Core/Services/Profiles/ProfileService.cs)
- `GetCurrentProfileName()` — reads `current_profile.txt`, returns null on first run (ProfileService.cs:52-68).
- `GetCurrentProfileAsync()` — loads/deserializes profile.json; creates one if missing; updates `LastUsedAt` and saves on every load (ProfileService.cs:70-118).
- `CreateProfileAsync(name, displayName, pin)` — validates name regex `^[a-zA-Z0-9_-]+$`; hashes PIN with SHA256→base64; if it is the first profile (or no current_profile.txt) auto-sets it current (ProfileService.cs:152-190).
- `SwitchProfileAsync(name, pin)` — validates PIN, rewrites `current_profile.txt`, clears cache, publishes `ProfileSwitchedEvent` on the shared EventBus (ProfileService.cs:192-223).
- `DeleteProfileAsync` — GC.Collect + delay hacks to release SQLite file locks, recursive directory delete; clears `current_profile.txt` if the deleted one was current (ProfileService.cs:225-263).
- `ValidatePinAsync`, `UpdateProfilePinAsync` (requires current PIN if set), `UpdateProfileAsync`, `ExportProfileAsync` (zips profile dir to Documents), `ImportProfileAsync` (unzips; requires profile.json; refuses duplicate name) (ProfileService.cs:275-389).

### 2.4 Startup flow (src/VyralBot.UI/App.xaml.cs:329-373)
On startup, after MainWindow shows: if 0 profiles or no current profile → force profile selector overlay; if current profile name not in list → selector; if >1 profiles → selector unless current profile's `Settings["AutoLogin"]` is `"True"` (auto-login skips selector and calls `MainViewModel.OnProfileLoadedAsync()` fire-and-forget); if exactly 1 profile → auto-load without selector (AutoLogin only matters when multiple profiles exist).

### 2.5 Auto-Login setting
- UI: Settings → Profile → General → "Auto-Login" toggle + Save Settings button (SettingsView.xaml:356-387).
- Read: `SettingsViewModel.LoadSettingsAsync()` from `profile.Settings["AutoLogin"]` (SettingsViewModel.cs:289-309); consumed at startup in App.xaml.cs:349-367.
- Write: `SaveAutoLoginCommand` → `currentProfile.Settings["AutoLogin"] = AutoLogin.ToString(); UpdateProfileAsync()` → persisted to profile.json (SettingsViewModel.cs:311-335).

### 2.6 ProfileSettingsLoader (src/VyralBot.UI/Services/ProfileSettingsLoader.cs)
Called after profile load (from MainViewModel). Loads, in order:
1. Platform credentials via `PlatformsViewModel.LoadTwitchPlatformAsync()` + `LoadKickPlatformAsync()` (only if PlatformsViewModel supplied — lazy otherwise).
2. `KickSettingsViewModel.InitializeAsync()` (chat/webhooks/auto-replies settings).
3. `WebServerSettingsViewModel.InitializeAsync()` (ngrok auth token, port, region, auto-start).
4. `ChatViewModel.InitializeAsync()` (chat display settings).
All read from the profile database so that "ALL settings are loaded BEFORE any auto-connections or feature initialization" (ProfileSettingsLoader.cs:28-68).

(Section to be expanded: MainViewModel.OnProfileLoadedAsync / switching effects, WebServerSettings, Ngrok, per-platform settings, persistence layer.)

## 3. MainViewModel & Where "Settings" Pages Live (src/VyralBot.UI/ViewModels/MainViewModel.cs)

Navigation is a sidebar of top-level destinations (MainViewModel.cs:608-1553). Settings-related pages and their grouping:
- **Settings** (the SettingsView above) — `NavigateToSettingsAsync` (MainViewModel.cs:874-912); SettingsViewModel is always disposed & recreated on each navigation for fresh state.
- **Audio Settings** — separate top-level page (`NavigateToAudioSettingsAsync`, MainViewModel.cs:914-947), NOT under the Settings page.
- **Alert Settings** — separate top-level page (`NavigateToAlertSettingsAsync`, MainViewModel.cs:1054-1087), NOT under Settings.
- **Sounds** (sound file/volume mapping management) — separate top-level page (MainViewModel.cs:949-982).
- **Ngrok Setup Wizard** — full-page wizard reachable from Web Server settings (`NavigateToNgrokSetupWizard`, MainViewModel.cs:1299-1331).
- Sidebar also groups expanders: Platforms, Chat Features, Stream Tools (IsPlatformsExpanded etc., MainViewModel.cs:79-89).

### 3.1 OnProfileLoadedAsync — what runs after a profile is selected (MainViewModel.cs:219-292)
1. `UpdateTitleAsync` → window title "VyralBot (ProfileDisplayName)".
2. `WebhookCoordinatorService.InitializeAsync()` — loads WebServerSettings row from DB; auto-starts webhook infra if `AutoStartOnLaunch` is true AND ngrok auth token JSON exists (WebhookCoordinatorService.cs:49-84).
3. `SoundService.InitializeDefaultSoundsAsync()`.
4. `ProfileSettingsLoader.LoadAllSettingsAsync(platformsViewModel: null, kickSettings, webServerSettings, chat)` — deliberate order: settings BEFORE auto-connections so EventBus subscriptions exist before messages arrive.
5. `CheckPlatformConnectionsAsync()` — for each of Twitch/Kick/YouTube with DB `Status == "Connected"`, decrypts/loads tokens and calls `ConnectAsync`; **side effect: forces `<Platform>ChatEnabled = true` in DB on first auto-connect** (MainViewModel.cs:437-443, 499-505, 541-547).
6. `AutoEnableKickWebhooksAsync()` — if webhook infra running + active Kick subscriptions in DB + Kick credentials → `EnableKickWebhooksAsync` (MainViewModel.cs:296-343).

### 3.2 Profile switching effects (ProfileSwitchedEvent subscriber, MainViewModel.cs:154-210)
When profile actually changes (old != new): disconnect Twitch/Kick/YouTube services, `ChatViewModel.Cleanup()` + null it, null out cached Platforms/Settings VMs, dispose their scopes, re-run `OnProfileLoadedAsync()` (which re-points `ProfileAwareDataService` at the new profile DB, restarts webhook infra per AutoStartOnLaunch, auto-reconnects platforms saved as Connected in the NEW profile DB), then navigate back Home if currently viewing a profile-specific view. Same-profile selection just calls `OnProfileLoadedAsync()` without cleanup.
NOTE: switching profile from within Settings (SettingsViewModel.cs:216-268) does NOT restart the app — the running WebServerSettingsViewModel/KickSettingsViewModel instances are re-initialized only if the user re-navigates; the event-based cleanup above covers Platforms/Settings/Chat only.

## 4. Web Server + ngrok configuration (Settings → API & Webhooks)

### 4.1 UI (src/VyralBot.UI/Views/WebServerSettingsView.xaml, embedded in SettingsView via ContentControl)
Two screens toggled by `ShowNgrokSettings`:
- **Main overview** (WebServerSettingsView.xaml:110-401):
  - "Ngrok Tunnel" card — button is RUN SETUP (→ wizard) when `IsSetupComplete == false`, else CONFIGURE (→ inline ngrok settings).
  - "Webhook Server" card — START/STOP buttons, status dot/text, PublicUrl display, COPY URL and TEST HEALTH (opens `{PublicUrl}/webhooks/health` in browser).
  - "Server Configuration" card — **Auto-start on Launch** toggle ("Start server when application launches") and **Local Port** text box (default 5000, disabled while running).
  - Static "About Webhook Infrastructure" card.
- **Ngrok Configuration screen** (WebServerSettingsView.xaml:17-108): Authentication Token text box + Region combo (us, eu, ap, au, sa, jp, in; default us) + SAVE SETTINGS.

### 4.2 ViewModel (src/VyralBot.UI/ViewModels/WebServerSettingsViewModel.cs)
Properties: `IsEnabled` (loaded/saved but NO toggle in current XAML — hidden setting), `Port` (default 5000), `NgrokAuthToken` (kept in memory, loaded from ngrok_credentials.json), `NgrokRegion` (default "us"), `AutoStartOnLaunch` (default false), `Status`, `PublicUrl`, `IsRunning`, `HasActiveDependencies`/`DependenciesText`, `IsSetupComplete`, `ShowNgrokSetup`/`ShowNgrokSettings`.
- `InitializeAsync()` loads WebServerSettings DB row + ngrok token from JSON (WebServerSettingsViewModel.cs:92-142).
- `IsSetupComplete = ngrok on PATH && auth token non-empty` — checked by spawning `ngrok version` (WebServerSettingsViewModel.cs:144-189).
- `StartAsync` requires setup complete, saves settings, then `WebhookCoordinatorService.StartAsync()`. `StopAsync` refused while `CanDisableWebServer()` is false (active feature dependencies).
- `OnAutoStartOnLaunchChanged` — persists immediately when user toggles, and if turned ON while stopped, starts the infrastructure immediately (WebServerSettingsViewModel.cs:482-499).
- **Save path**: `WebServerSettings` row (Id=1) via `IDataService.SaveWebServerSettingsAsync` — stored in per-profile SQLite `WebServerSettings` table. Ngrok auth token is NOT saved by this VM's Save button (only the wizard saves it); the token textbox edits only take effect through... nothing — the Save command saves only DB fields (WebServerSettingsViewModel.cs:436-452). The token box on this screen is effectively display-only unless re-saved by the wizard.

### 4.3 Entities/defaults (src/VyralBot.Data/Entities/WebServerSettings in WebServerEntities.cs:3-13)
`Id=1` single row; `IsEnabled` (default false), `Port` (default 5000), `NgrokRegion` (default "us"), `CurrentTunnelUrl`, `AutoStartOnLaunch` (default false), `LastStarted`, `LastStopped`. Table created at DapperDataService.cs:2302.

### 4.4 NgrokService (src/VyralBot.Api/Services/NgrokService.cs)
- `StartTunnelAsync(port, authToken, region)` spawns `ngrok http {port} --region {region} --authtoken {token}` as a hidden process (NgrokService.cs:60-73); requires non-empty token and ngrok on PATH; polls `http://127.0.0.1:4040/api/tunnels` up to 5 times/2s to fetch the https `public_url` (NgrokService.cs:80-95, 160-191).
- URL-change monitor: a 30s `Timer` polls the local ngrok API and raises `PublicUrlChanged` on change/disconnect (NgrokService.cs:193-234).
- Auth token persistence: `ngrok_credentials.json` in the CURRENT PROFILE folder (`{profileRoot}/ngrok_credentials.json`, plain JSON `{"AuthToken": ...}`) via `SaveAuthTokenAsync`/`LoadAuthTokenAsync` (NgrokService.cs:258-319) — per-profile, not in DB, unencrypted.

### 4.5 WebhookCoordinatorService (src/VyralBot.Api/Services/WebhookCoordinatorService.cs)
- `StartAsync` order: Kestrel (WebServerService) on `settings.Port` → load ngrok token → `NgrokService.StartTunnelAsync(port, token, settings.NgrokRegion)` → persist `CurrentTunnelUrl`/`LastStarted` back to DB (WebhookCoordinatorService.cs:86-148).
- `StopAsync` stops tunnel + server with 5s timeout, nulls `CurrentTunnelUrl`, sets `LastStopped`.
- `EnableKickWebhooksAsync(accessToken, broadcasterUserId)`: cleans duplicate/legacy `chat.message.sent` subscriptions, then subscribes ONLY webhook-only events (`LivestreamMetadataUpdated`, `ModerationUserBanned` — the rest come from KickLib Pusher WebSocket), endpoint `{PublicUrl}/webhooks/kick`, saving `WebhookSubscription` rows and registering a dependency "Kick" in `WebServerDependencyTracker` (which is what `CanDisableWebServer()` consults) (WebhookCoordinatorService.cs:195-305).
- On `PublicUrlChanged`: updates `CurrentTunnelUrl` in DB; if Kick subscriptions were active, only warns — user must manually re-enable Kick webhooks (no OAuth token available here) (WebhookCoordinatorService.cs:345-384).

### 4.6 WebServerService (src/VyralBot.Api/Services/WebServerService.cs)
Embedded ASP.NET Core (Kestrel) child `WebApplication` listening on localhost:{Port} (Http1AndHttp2). Serves: MVC controllers from VyralBot.Api (WebhooksController etc.), `KickWebhookValidationMiddleware` (RSA signature validation), static files (wwwroot — audio.html/sounds for browser sources), and a `/audio-ws` WebSocket endpoint used to broadcast audio-play commands to browser sources (`AudioWebSocketBroadcaster.BroadcastAsync` is wired to the client set) (WebServerService.cs:50-135). Port availability pre-checked by socket bind (WebServerService.cs:227-239).
Everything ngrok/web-server enables: Kick webhooks (follows/subs/etc. via web), the browser-source audio/sounds pages, and the public health endpoint.

### 4.7 Ngrok Setup Wizard (src/VyralBot.UI/ViewModels/NgrokSetupWizardViewModel.cs)
4-step wizard (extends BaseOAuthSetupViewModel): Step 1 install check (opens Microsoft Store `ms-windows-store://pdp/?productid=9MVS1J51GMK6`, verifies `ngrok version`); Step 2 sign-in link `https://dashboard.ngrok.com/login`; Step 3 opens embedded WebView2 at `https://dashboard.ngrok.com/get-started/your-authtoken` + a modal to paste the token (kept in memory only); Step 4 TEST CONNECTION (SendTestMessageAsync): token format sanity check (length ≥ 20 and contains "_"), starts web server on port 5000, starts tunnel with saved token + region from DB, GETs `{publicUrl}/webhooks/health`, then tears both down (NgrokSetupWizardViewModel.cs:307-414).
On complete (`OnCompleteSetupAsync`): saves token via `NgrokService.SaveAuthTokenAsync` (per-profile JSON) AND runs `ngrok config add-authtoken {token}` (NgrokSetupWizardViewModel.cs:210-256). `OnFinishSetup` disposes SettingsViewModel and navigates back to Settings (NgrokSetupWizardViewModel.cs:273-295).
Legacy OAuth-style methods in the wizard save/load `VYRALBOT_NGROK_CLIENT_ID/SECRET` **Windows user environment variables** (NgrokSetupWizardViewModel.cs:185-208) — vestigial; not used by the token flow.

### 4.8 Dead code
`NgrokSetupView.xaml`/`NgrokSetupViewModel.cs` (plus `.backup`, `.old` variants) are NOT referenced by App.xaml.cs registrations or any navigation (grep across src returns no usage). They are a previous generation of the wizard; the active one is `NgrokSetupWizardView(NModel)`.

## 5. Per-Platform Settings (Settings → Platforms)

Three distinct families of "platform settings" UI exist — do not confuse them:
1. **Settings → Platforms → {Twitch,Kick} card** → per-platform settings sub-view (this section).
2. **Chat Hub → per-platform Chat Settings** (TwitchChatSettingsView, KickChatSettingsView, YoutubeChatSettingsView, TiktokChatSettingsView, DiscordChatSettingsView) — event display/auto-reply settings (section 5.3).
3. **Platforms page → Web Setup wizards** (TwitchWebSetupViewModel, KickWebSetupViewModel, etc. extending BaseWebSetupViewModel) — OAuth connect flows that WRITE the platform rows/tokens; they are setup, not settings.

### 5.1 Twitch Settings (src/VyralBot.UI/ViewModels/TwitchSettingsViewModel.cs — 27 lines)
Stub. Entire VM is a `StatusMessage = "Twitch configuration settings coming soon..."`; TwitchSettingsView.xaml shows a Twitch icon + "Configuration settings coming soon...". Nothing is loaded or saved. (Real Twitch configuration lives in the Platforms page web-setup wizard and the Twitch Dashboard page, out of this review's scope.)

### 5.2 Kick Settings (src/VyralBot.UI/ViewModels/KickSettingsViewModel.cs, View KickSettingsView.xaml)
Cards shown when connected (IsConnected from DB Platform row `Status == "Connected"` + `IKickService.IsConnected`):
- **Connection Status** — dot + status text + DISCONNECT (`DisconnectCommand` sets DB Status="Disconnected" and disconnects IKickService, KickSettingsViewModel.cs:131-158).
- **Account Settings** — Username/Channel text boxes (both read-only in XAML) + SAVE CHANGES (`SaveSettingsCommand` persists Username to Platforms row + Kick.AutoRepliesEnabled config, KickSettingsViewModel.cs:160-188). NOTE: XAML binds `Channel` (KickSettingsView.xaml:159) but the VM property is `ChannelName` — a binding mismatch; the Channel box never populates.
- **Webhook Settings** — "Enable Webhooks" toggle. Requires: Kick AccessToken in DB, webhook coordinator running (else prompts "start it in Settings → API & Webhooks"), UserId present. Enable → `WebhookCoordinatorService.EnableKickWebhooksAsync`; disable → `DisableKickWebhooksAsync` (KickSettingsViewModel.cs:190-244). State derived from `WebhookSubscriptions` rows (`IsActive` count) on load (KickSettingsViewModel.cs:257-284). Auto-uncheks itself if the webhook server stops (StatusChanged handler, KickSettingsViewModel.cs:246-255).
- **Chat Settings (WebView2 title, now KickLib)** — checkboxes:
  - "Enable Kick Chat (Receive Messages)" → `Platform.KickChatEnabled`; persisted immediately on change AND publishes `PlatformChatRoutingChangedEvent` on the EventBus so ChatViewModel filters update live (KickSettingsViewModel.cs:287-326).
  - "Auto-start chat on app launch" → `Platform.KickChatAutoStart`, saved immediately (KickSettingsViewModel.cs:328-345).
  - Start/Stop Chat button (`ToggleChatCommand`) — connects/disconnects IKickService using DB credentials (KickSettingsViewModel.cs:347-399).
  - "Enable auto replies (thank followers / respond to unfollows)" → persisted in **Configs table key `Kick.AutoRepliesEnabled`** — saved immediately on toggle AND on Save (KickSettingsViewModel.cs:57-61, 177).
  
### 5.3 Per-platform *Chat* Settings (hosted in Chat Hub — ChatHubViewModel.cs:72-76 resolves all five)
Persisted via `IChatEventSettingsService` (cached wrapper over DB `ChatEventSettings` table, ChatEventSettingsService.cs:10-80). Each row: Platform, EventType, ShowInChat, AutoReplyEnabled, AutoReplyMessage. Defaults are in-code initial values; DB values (if row exists) override.
- **TwitchChatSettingsViewModel** — per event type Follow / Subscribe / Resub / GiftSub / Raid / Cheer: ShowInChat (default true), AutoReplyEnabled (default false), AutoReplyMessage (default templates with `{username}`, `{months}`, `{count}`, `{gifter}`, `{raider}`, `{viewers}`, `{bits}` placeholders) (TwitchChatSettingsViewModel.cs:26-84). Save button writes all six rows (TwitchChatSettingsViewModel.cs:163-222). Note `IsChatEnabled`/`AutoStartChat` observable properties exist but are never loaded/saved in this VM.
- **KickChatSettingsViewModel** — Follow / Subscribe / GiftSub / Host / KicksGifted / RewardRedeemed (+StreamStatus ShowInChat only, auto-reply forced off) (KickChatSettingsViewModel.cs:26-89, 223-228).
- **YoutubeChatSettingsViewModel / TiktokChatSettingsViewModel / DiscordChatSettingsViewModel** — simpler: an `IsChatEnabled` toggle persisted to `Platform.YoutubeChatEnabled` / `TiktokChatEnabled` / `DiscordChatEnabled` (DB Platforms row) + `PlatformChatRoutingChangedEvent` published on change (DiscordChatSettingsViewModel.cs:63-90, TiktokChatSettingsViewModel.cs:62-89). All three force-enable chat when platform Status=="Connected" on init.
- Effect flow: `ShowInChat`/`AutoReplyEnabled`/`AutoReplyMessage` are consumed by chat-event handling (ChatViewModel/AutoReplyService); `<Platform>ChatEnabled` flags feed ChatViewModel's per-platform filters (ChatViewModel.cs:595-599) and are force-set true after any successful auto-connect (MainViewModel.cs:437-447).

## 6. Persistence Layer (src/VyralBot.Data)

### 6.1 Engine & routing
- `DapperDataService` (SQLite + Dapper) is the only repository implementation; `IDataService` is the 200+ method façade (IDataService.cs).
- `ProfileAwareDataService` (src/VyralBot.UI/Services/ProfileAwareDataService.cs) wraps it: every call resolves `GetCurrentProfileName()` (falls back to profile `"temp"` when none) and lazily builds a `DapperDataService` pointed at `appdata/profiles/{name}/data/vyralbot.db` (ProfileAwareDataService.cs:26-46). This is THE mechanism that makes every DB-backed setting per-profile. `ClearConnection()` drops the cache, clears SQLite connection pools and GCs so the DB file can be deleted (used by profile deletion, ProfileAwareDataService.cs:48-64).
- Schema created/migrated on demand by `EnsureDatabaseCreatedAsync` (DapperDataService.cs:2092+): 36 tables incl. settings-relevant **Platforms, Configs, WebServerSettings, WebhookSubscriptions, ChatEventSettings, Variables, Triggers, Commands**, plus currency/giveaway/poll/watchtime/etc. Column additions are done with pragma_table_info checks + ALTER TABLE (e.g. per-platform chat flags, DapperDataService.cs:2132-2234).

### 6.2 What is stored WHERE
| Setting | Storage | Written by |
|---|---|---|
| AutoLogin | profile.json `Settings["AutoLogin"]` | Settings page (SettingsViewModel.cs:311-335) |
| Profile metadata/PIN hash | profile.json | ProfileService |
| Web server: Port, NgrokRegion, AutoStartOnLaunch, CurrentTunnelUrl, LastStarted/Stopped, IsEnabled | DB `WebServerSettings` (Id=1) row | WebServerSettingsViewModel + WebhookCoordinatorService |
| ngrok auth token | `{profile}/ngrok_credentials.json` (plain JSON) | NgrokSetupWizardViewModel via NgrokService.SaveAuthTokenAsync + `ngrok config add-authtoken` |
| Twitch OAuth client id/secret | `{profile}/twitch_credentials.json` | TwitchOAuthService.SaveCredentialsAsync (TwitchOAuthService.cs:267-296) |
| Twitch/Kick/YouTube access+refresh tokens, username, status, chat enabled/auto-start flags | DB `Platforms` row per platform name | Web-setup wizards, settings pages, auto-connect |
| Kick auto-replies toggle | DB `Configs` key `Kick.AutoRepliesEnabled` | KickSettingsViewModel |
| Audio settings (StreamerSoundsEnabled, StreamSoundsEnabled, MasterVolume, WebSocketPort) | DB `Configs` key `AudioSettings` (JSON blob) | AudioSettingsViewModel |
| Event sound/volume mappings | DB `Configs` keys `EventSound_{Platform}_{eventType}` / `EventVolume_{Platform}_{eventType}` | SoundsViewModel |
| Chat event display/auto-reply settings | DB `ChatEventSettings` rows (platform+eventType) | per-platform ChatSettings VMs via ChatEventSettingsService |
| Custom variables | DB `Variables` table | Settings → Variables (VariablesViewModel) |
| Alert settings | **NOT PERSISTED** (VM has TODO comments; save shows success but writes nothing) | — |

### 6.3 Configs table keys actually used in code
`AudioSettings`, `Kick.AutoRepliesEnabled`, `EventSound_{Platform}_{eventType}`, `EventVolume_{Platform}_{eventType}` (grep of Get/SetConfigValueAsync across src). `Configs` also has an `IsEncrypted` column and Category support (DapperDataService.cs:2238-2246) — unused by current callers.

## 7. Profile System End-to-End

1. **Creation**: three entry points — first-run overlay create dialog (MainViewModel.ConfirmCreateProfileAsync, creates AND switches, MainViewModel.cs:1892-1934), Settings → Profile → New Profile (SettingsViewModel.cs:465-502, creates only), and the unused ProfileSelectorWindow path. Name regex `^[a-zA-Z0-9_-]+$`; optional display name; optional 4-6 digit PIN (SHA256+base64 hashed in profile.json).
2. **Selection at startup**: App.xaml.cs:329-373 — first run/missing/invalid current profile forces the selector; >1 profiles shows selector unless current profile AutoLogin; exactly 1 profile auto-loads. The selector is an **overlay inside MainWindow** (MainWindow.xaml:909+, bound to `MainViewModel.ShowProfileSelector`), listing profiles by LastUsedAt with lock icons for PIN-protected ones and inline PIN entry (`SelectProfile`/`ConfirmProfileSelectionAsync`, MainViewModel.cs:1573-1632).
3. **What is per-profile**: the entire SQLite DB (all tables above), ngrok_credentials.json, twitch_credentials.json (+ kick_credentials.json checked by PlatformsViewModel.cs:533-547), and profile.json metadata. What is GLOBAL: app install, DI singletons (in-memory caches like ChatEventSettingsService's settings cache), logs, and the ngrok agent's own config (ngrok config add-authtoken writes to the user-level ngrok config, shared across profiles).
4. **Switching**: `SwitchProfileAsync` validates PIN → rewrites current_profile.txt → publishes `ProfileSwitchedEvent`. MainViewModel's subscriber (MainViewModel.cs:154-210) disconnects platforms, cleans ChatViewModel, disposes Platforms/Settings scopes, and re-runs the full load (section 3.1). Because `ProfileAwareDataService` re-points on next call, all subsequent DB access hits the new profile. Caveat: `ChatEventSettingsService` caches settings per process in a Dictionary (ChatEventSettingsService.cs:32-64) — the cache is keyed only by platform:eventType, not by profile, so stale rows from the previous profile could be served until process restart (no cache invalidation on profile switch found).
5. **Deletion**: blocked for the current profile in Settings (SettingsViewModel.cs:337-351); ProfileAwareDataService.ClearConnection() + 200 ms delay + GC before `Directory.Delete` recursive (SettingsViewModel.cs:512-549).
6. **Export/Import**: Settings → Export zips the whole profile directory to `Documents/{name}_backup.zip` (actually `{name}_{timestamp}.zip`, ProfileService.cs:344-358). ImportProfileAsync exists on the service but has NO UI caller (Settings page only has Export).
7. **Dead code**: `ProfileSelectorWindow.xaml` + `ProfileSelectorViewModel.cs` are not referenced by anything (grep clean) — superseded by the MainWindow overlay. ProfileSelectorViewModel additionally has create/delete logic of its own.
8. **Observed data**: `appdata/profiles/Drip/` contains profile.json, ngrok_credentials.json, twitch_credentials.json, data/vyralbot.db; `appdata/profiles/temp/data/vyralbot.db` is the no-profile fallback; `current_profile.txt` = `Drip`. Profile.json `Settings` dict observed empty except when AutoLogin used.

## 8. Audio / Alert / Sounds Settings (separate top-level pages, not the Settings page)

### 8.1 Audio Settings (AudioSettingsView + AudioSettingsViewModel)
- UI controls: "Streamer Sounds" toggle (play locally), "Stream Sounds" toggle (send to browser source), Master Volume slider 0-100 (clamped), WebSocket Server status card (read-only; port forced to 5000, `OnWebSocketPortChanged` rejects any other value with a warning log, AudioSettingsViewModel.cs:195-204), Browser Source URL box `http://localhost:5000/audio.html` + copy button + OBS instructions, TEST SOUND (plays first file in Sounds/ at MasterVolume), Restart Server button (now a no-op message box saying "always running on port 5000").
- Persistence: single JSON blob in Configs key `AudioSettings` (AudioSettingsViewModel.cs:48-111). The `AudioSettings` DTO defaults WebSocketPort to 8080 (SoundService.cs:321-329) but the VM forces 5000 on every init — the persisted port value is effectively always 5000.
- Effect: `SoundService` reads the same Configs blob (SoundService.cs:239) — StreamerSoundsEnabled/StreamSoundsEnabled/MasterVolume gate local playback vs `/audio-ws` browser-source broadcast (the WebSocket lives in the embedded Kestrel server, section 4.6). The browser source only works while the web server is running.

### 8.2 Alert Settings (AlertSettingsView + AlertSettingsViewModel)
- UI: AlertsEnabled toggle, AlertDuration (5s default), animation picker (SlideInFromTop/Bottom, FadeIn, Bounce), position picker (9 positions), per-event toggles (Follow, Subscribe, GiftSub, Raid, Host, Cheer), TEST ALERT, SAVE, RESET TO DEFAULTS.
- Persistence: **none** — `InitializeAsync` has "// TODO: Load settings from database", `SaveSettingsAsync` has "// TODO: Save settings to database" and just shows a success MessageBox (AlertSettingsViewModel.cs:78-138). Settings reset to defaults on every navigation (VM is disposed on navigate-away, MainViewModel.cs:1696-1704).

### 8.3 Sounds (SoundsView + SoundsViewModel)
Event→sound mapping page: for Kick (follow/subscribe/giftsub/host) and Twitch (follow/subscribe/resub/giftsub/raid/cheer), assigns a sound file and volume, persisted immediately to Configs keys `EventSound_{Platform}_{eventType}`/`EventVolume_{Platform}_{eventType}` (SoundsViewModel.cs:240-260, 320-340). Consumed by SoundService when events fire.

## 9. Hidden / Dead / Surprising Settings Findings

1. **`WebServerSettings.IsEnabled`** — loaded and saved by WebServerSettingsViewModel but NO service ever reads it (only the VM's InitializeAsync touches it, WebServerSettingsViewModel.cs:100). No toggle exists in the XAML either. Effectively dead.
2. **Ngrok auth token edits in API & Webhooks → Ngrok Configuration don't persist** — `SaveCommand` only saves DB fields; `NgrokAuthToken` textbox edits are lost unless re-entered in the wizard (WebServerSettingsViewModel.cs:436-452).
3. **Alert Settings is a UI-only facade** (no persistence, see 8.2).
4. **AudioSettings.WebSocketPort** stored in the JSON blob but pinned to 5000 by the VM; SoundService DTO default (8080) never surfaces.
5. **KickSettingsView `Channel` binding mismatch** — binds nonexistent `Channel` property (VM has `ChannelName`), so the read-only Channel box stays empty (KickSettingsView.xaml:159).
6. **Settings page platform cards for YouTube/TikTok/Discord/OBS have no navigation commands** — clicking them does nothing even when connected (SettingsView.xaml:659-793); only Twitch/Kick wire `NavigateTo*SettingsCommand`. OBS has no settings view at all anywhere.
7. **8 of 13 Settings categories are "Coming Soon" placeholders** (Theme, Data, Notifications, Commands, Timers, Automation, Performance, Security, Advanced).
8. **Auto-connect side effect**: first successful auto-connect silently flips `<Platform>ChatEnabled` from false→true in the DB (MainViewModel.cs:437-447) — a settings value changed as a side effect of connecting.
9. **`ChatEventSettingsService` cache is not profile-aware** (see 7.4 caveat).
10. **Windows user environment variables** `VYRALBOT_NGROK_CLIENT_ID/SECRET` written by the ngrok wizard's vestigial OAuth methods (NgrokSetupWizardViewModel.cs:190-193) — hidden global state outside profile/appdata.
11. **ngrok agent-level config**: the wizard also runs `ngrok config add-authtoken` — writes to the machine/user-level ngrok config, shared across all VyralBot profiles even though the token JSON is per-profile.
12. **Dead files**: NgrokSetupView(.backup/.old)/NgrokSetupViewModel(.backup), ProfileSelectorWindow/ViewModel, SettingsView.xaml.old — unreferenced.
13. **Profile-aware single-instance**: app enforces a single process (mutex "VyralBot_SingleInstance_Mutex", App.xaml.cs:57-66) — profiles cannot run concurrently.
14. **Twitch auto-connect helper `AutoConnectTwitchAsync` exists in App.xaml.cs:385-422 but is never called** (auto-connection is done in MainViewModel instead).
15. **Startup profile selector lock icon never renders** — MainWindow.xaml:999 binds `HasPin` (BooleanToVisibilityConverter) but the `Profile` model has no `HasPin` property (only `PinCode` string), so the "PIN Protected" padlock silently fails to appear; PIN entry still works because `ShowPinEntry` is driven by `SelectProfile` logic.

## 10. Cross-Reference: Root Docs vs Code
- `CENTRALIZED_SETTINGS_IMPLEMENTATION.md` and `PROFILE_SETTINGS_QUICK_REFERENCE.md` describe the ProfileSettingsLoader flow accurately, but are stale in one respect: they claim PlatformsViewModel is created/loaded upfront; current MainViewModel passes `platformsViewModel: null` to lazy-load Platforms on demand (MainViewModel.cs:264-269, ProfileSettingsLoader.cs:38-47).
- `SETTINGS_REDESIGN_PLAN.md` matches the implemented SettingsView (sidebar categories), though implemented with Buttons rather than the planned RadioButtons and with 13 categories (adds Platforms, Variables, API & Webhooks beyond the plan's 11).
- Both docs correctly identify the storage split: Platforms/WebServerSettings/Configs/ChatEventSettings tables in per-profile DB + JSON credential files per profile.
