# Known Issues Ledger

The numbered, severity-tagged inventory of every known defect, wiring gap, dead surface, and doc-drift item in the current build — verified against code by the six-pass review that produced this documentation.

**Every feature page in these docs references this ledger** rather than repeating caveats inline. If a page says "see the Known Issues Ledger", the relevant entry is here. Severity meanings:

- **Critical** — silences or corrupts a shipped feature; the feature cannot work as built.
- **Wiring gap** — the feature runs but a connection, persistence, or plumbing step is missing or wrong.
- **Dead surface** — UI or code that renders/exists but does nothing.
- **Security** — hardening or trust concern, not necessarily a functional break.
- **Doc drift** — shipped root documents that disagree with the code.

Counts: 7 Critical, 9 Wiring gaps, 2 Dead surface, 6 Security, 1 Doc drift = 25 entries.

---

## Summary

| ID | Severity | Area | Issue | Where documented |
|---|---|---|---|---|
| [KI-1](#ki-1-critical--chat-command-pipeline-is-dead) | Critical | Chat / Commands / Currency / Giveaways / Polls / Watchtime | `ChatMessageReceivedEvent` is never published — no chat command can execute | [Commands](../chat-features/commands.md), [Command Catalog](command-catalog.md) |
| [KI-2](#ki-2-critical--twitch-eventsub-likely-never-connects) | Critical | Twitch events | EventSub gated on `Platform.UserId`, which no Twitch code path sets | [Twitch](../platforms/twitch.md), [Event Reactions](../chat/event-reactions.md) |
| [KI-3](#ki-3-critical--eventstatistics-is-never-written) | Critical | Statistics / Event History | `SaveEventStatisticAsync` has zero callers — both pages read an always-empty table | [Statistics](../stream-tools/statistics.md), [Event History](../chat-features/event-history.md) |
| [KI-4](#ki-4-critical--browser-source-sounds-never-play) | Critical | Browser sources / Sounds | Server broadcasts PascalCase `AudioEvent` JSON; audio.html only understands `{"type":"play"}` | [Browser Sources](../stream-tools/browser-sources.md), [Sounds](../stream-tools/sounds.md) |
| [KI-5](#ki-5-critical--giveawaypoll-follower-and-watchtime-eligibility-always-reject) | Critical | Giveaways / Polls | `isFollower:false` and `watchtimeMinutes:0` hard-passed — FollowersOnly / MinWatchtime reject everyone | [Giveaways](../stream-tools/giveaways.md), [Polls](../stream-tools/polls.md) |
| [KI-6](#ki-6-critical--alert-settings-page-is-a-facade) | Critical | Visual Alerts | Alert Settings never loads or saves; real alerts use hard-coded animation/position/duration | [Visual Alerts](../stream-tools/visual-alerts.md) |
| [KI-7](#ki-7-critical--currency-settings-tab-does-not-persist) | Critical | Currency | Currency Settings "Save" logs success and calls no save API | [Currency & Rewards](../stream-tools/currency.md) |
| [KI-8](#ki-8-wiring-gap--twitch-chat-enabled-toggle-is-cosmetic) | Wiring gap | Chat settings | Twitch has no routing-event publisher, so its chat filter can never turn off | [Chat Settings](../chat/chat-settings.md) |
| [KI-9](#ki-9-wiring-gap--youtube-chat-history-is-never-persisted) | Wiring gap | Chat history | `DataChatMessageStore` maps Kick/Twitch events only | [Chat Window](../chat/chat-window.md) |
| [KI-10](#ki-10-wiring-gap--ngrok-token-edits-in-settings-dont-persist) | Wiring gap | Web server / ngrok | Settings' Save writes only DB fields; only the wizard saves the token. `IsEnabled` read by nothing | [API, Web Server & ngrok](../settings/web-server-ngrok.md) |
| [KI-11](#ki-11-wiring-gap--chateventsettingsservice-cache-is-not-profile-aware) | Wiring gap | Chat settings / Profiles | Cache keyed platform:eventType only; stale rows served across profile switches until restart | [Chat Settings](../chat/chat-settings.md), [Profiles](../getting-started/profiles.md) |
| [KI-12](#ki-12-wiring-gap--auto-reply-templates-use-simple-replace) | Wiring gap | Auto-replies | `string.Replace` only; DB/social variables do not resolve; `@{username}` defaults leave a stray `@` | [Auto-Replies](../chat-features/auto-replies.md), [Variables Catalog](variables-catalog.md) |
| [KI-13](#ki-13-wiring-gap--ngrok-url-rotation-requires-manual-recovery) | Wiring gap | ngrok / Kick | On tunnel URL change, Kick webhook subscriptions are not repointed; user must toggle off/on | [API, Web Server & ngrok](../settings/web-server-ngrok.md), [Kick](../platforms/kick.md) |
| [KI-14](#ki-14-wiring-gap--auto-connect-silently-flips-chat-enabled-to-true) | Wiring gap | Chat settings | First successful auto-connect force-sets `<Platform>ChatEnabled = true` in the DB | [Chat Settings](../chat/chat-settings.md) |
| [KI-15](#ki-15-wiring-gap--binding-mismatches) | Wiring gap | UI bindings | Kick settings `Channel` box binds a nonexistent property; profile-selector padlock binds nonexistent `HasPin` | [Kick](../platforms/kick.md), [Profiles](../getting-started/profiles.md) |
| [KI-16](#ki-16-wiring-gap--audio-port-pinned-to-5000-connected-clients-stub) | Wiring gap | Audio | Port edits reverted (DTO default 8080 never surfaces); "connected clients" is a stub showing 0 | [Audio Settings](../stream-tools/audio.md) |
| [KI-17](#ki-17-dead-surface--dead-buttons) | Dead surface | Navigation | Sidebar Events & Alerts / Browser Sources; Home Discord / Ideas; Settings platform cards; All-Platforms popup actions | [App Shell](../home/app-shell.md) |
| [KI-18](#ki-18-dead-surface--dead-and-orphaned-code) | Dead surface | Code | Unreachable pages, caller-less engines, stub subsystems (full inventory below) | [Data Layer](../architecture/data-layer.md) |
| [KI-19](#ki-19-security--kick-webhook-test-payload-signature-bypass) | Security | Webhooks | `test-` ids / `"test":true` payloads bypass RSA signature validation | [Kick](../platforms/kick.md), [Web Server](../architecture/web-server.md) |
| [KI-20](#ki-20-security--unsalted-sha256-pin-hashes) | Security | Profiles | PINs hashed with unsalted SHA256; length validation only exists in dead code | [Profiles](../getting-started/profiles.md) |
| [KI-21](#ki-21-security--inconsistent-token-storage) | Security | Platforms | Twitch user tokens DPAPI-encrypted; Kick/YouTube plaintext in DB; client secrets plaintext JSON | [Twitch](../platforms/twitch.md), [Kick](../platforms/kick.md) |
| [KI-22](#ki-22-security--wizards-write-windows-user-environment-variables) | Security | Wizards | Demo/ngrok wizards write user env vars; ngrok wizard writes machine-level ngrok config | [API, Web Server & ngrok](../settings/web-server-ngrok.md) |
| [KI-23](#ki-23-security--machine-wide-webview2-process-kills) | Security | Startup/exit | Startup and exit kill all `msedgewebview2` processes on the machine, not just its own | [Startup & Lifecycle](../architecture/startup-lifecycle.md) |
| [KI-24](#ki-24-security--robustness-notes) | Security | Event bus / Echo filter / Stub wizards | DropOldest event loss; content-based echo suppression eats duplicate viewer text; TikTok/Discord wizards mark "Connected" with empty tokens | [Event Bus](../architecture/event-bus.md), [TikTok, Discord & OBS](../platforms/tiktok-discord-obs.md) |
| [KI-25](#ki-25-doc-drift--root-markdown-vs-code) | Doc drift | Docs | README, profile-selector doc, app-entry diagram, OBS/KICK docs disagree with code | [Overview](../index.md) |

---

## Critical

### KI-1 (Critical) — Chat command pipeline is dead

The single biggest defect in the app. `ChatViewModel.OnChatMessageReceivedAsync` (`ChatViewModel.cs:592`) is the orchestrator for the entire command system: unified [CommandService](../../src/VyralBot.Core/Services/Commands/CommandService.cs), currency, giveaway, poll, and watchtime command handling, watchtime activity recording, and chat-activity currency. It subscribes to `ChatMessageReceivedEvent` at `ChatViewModel.cs:220` — but that event type is defined at `ChatViewModel.cs:2871` and **published by nothing in the solution**. The real per-platform handlers (`OnTwitchChatMessageReceivedAsync`, `OnKickChatMessageReceivedAsync`, `OnYouTubeChatMessageReceivedAsync`) do echo-filtering and UI rendering only.

Consequences: every command in the [Command Catalog](command-catalog.md) is inert; watchtime is never recorded (so the Watchtime dashboard stays empty and per-minute currency earning never accrues); chat-activity currency never awards. What still works: event reactions (auto-reply, sounds, alerts, event-bonus currency) — those subscribe to real, published platform events.

**Likely fix**: have the per-platform message handlers publish `ChatMessageReceivedEvent` on the shared EventBus (or refactor the generic handler to accept the typed per-platform events directly). Verified independently by three review passes.

### KI-2 (Critical) — Twitch EventSub likely never connects

`TwitchService.cs:115-123` only connects EventSub when the Twitch `Platform` row has a non-empty `UserId` **and** `ClientId`. A grep of all writers of `Platform.UserId` shows only Kick paths set it ([KickLibOAuthService.cs:116](../../src/VyralBot.Integrations/Kick/KickLibOAuthService.cs), `KickWebSetupViewModel.cs:896`) and one place nulls it (`PlatformsViewModel.cs:1260`) — no Twitch flow ever populates it. Without EventSub, Twitch follow/sub/resub/gift/raid/cheer events (and their auto-replies, sounds, alerts, and currency bonuses) probably never arrive, and Twitch-side auto-updating variables never sync.

**Likely fix**: fetch the authenticated user's id once after token exchange (e.g. GET `/users`) and persist it to the platform row, mirroring what the Kick flow already does.

### KI-3 (Critical) — EventStatistics is never written

`IDataService.SaveEventStatisticAsync` (implemented in [DapperDataService.cs](../../src/VyralBot.Data/DapperDataService.cs):264) has zero domain callers — nothing anywhere inserts `EventStatistic` rows. Both [StatisticsService.cs](../../src/VyralBot.Core/Services/Statistics/StatisticsService.cs) and the Event History page read only this table, so both pages are permanently empty in a running app. The `WasAutoReplied` flag can never be set either.

**Likely fix**: call `SaveEventStatisticAsync` from the ChatViewModel event handlers (the same 13 handlers that already do sound/alert/currency fan-out), ideally right after the auto-reply decision so `WasAutoReplied` is accurate.

### KI-4 (Critical) — Browser-source sounds never play

The only WebSocket producer is `SoundService` serializing the `AudioEvent` class with default PascalCase JSON (`EventType`, `Platform`, `SoundFile`, `Volume`). The browser source page ([audio.html](../../src/VyralBot.Api/wwwroot/audio.html) `:83-88`) only reacts to `{"type":"play","sound":...}` camelCase messages. Nothing in the app ever sends that shape, so viewer-side (OBS) sound playback cannot fire even when the web server is running and the page is connected. Local streamer-side playback still works — but only for `.wav` files (`SoundPlayer` limitation; mp3/ogg/m4a log a warning and play nothing locally).

**Likely fix**: serialize with a camelCase `JsonSerializerOptions`, or change the page to accept the `AudioEvent` shape (and honor the `volume` field it currently ignores).

### KI-5 (Critical) — Giveaway/poll follower and watchtime eligibility always reject

The chat-side entry paths pass `false` for `isFollower` and `0` for `watchtimeMinutes` — `ChatViewModel.cs:699-700` (giveaways) and `ChatViewModel.cs:734-735` (polls), each commented "can be tracked in future". Result: a giveaway or poll with **FollowersOnly** or **MinWatchtime > 0** rejects every single entry. SubscribersOnly works (`IsSubscriber` is passed through). Watchtime data exists in `WatchtimeService` but is not plumbed in.

**Likely fix**: resolve watchtime via `WatchtimeService.GetUserWatchtimeAsync` at entry time; source follower status from platform APIs or a cached webhook-derived set.

### KI-6 (Critical) — Alert Settings page is a facade

[AlertSettingsViewModel.cs](../../src/VyralBot.UI/ViewModels/AlertSettingsViewModel.cs):82 (`// TODO: Load settings from database`) and :119 (`// TODO: Save settings from database`). The Save button shows "saved successfully" without persisting anything, settings reset to defaults on every navigation (the VM is disposed on navigate-away), and the real alert path — `ShowEventAlertAsync` (`ChatViewModel.cs:2629-2650`) — uses hard-coded SlideInFromTop / TopCenter / 5 s regardless of the page's controls. Per-event toggles, duration, animation, and position have no effect. Alert settings are not persisted anywhere (see [Database Reference](database-reference.md)).

**Likely fix**: persist an alert-settings row (or a `Configs` JSON blob), load it in `InitializeAsync`, and have `ShowEventAlertAsync` read from the service instead of literals.

### KI-7 (Critical) — Currency Settings tab does not persist

`CurrencyViewModel.SaveSettingsAsync` (`CurrencyViewModel.cs:105-119`) logs "Settings saved successfully!" and calls no save API — the comment claims "Settings are saved through the service", but nothing in the flow writes the `CurrencySettings` row from this page. The entire Settings tab (currency name/symbol, earning rates, event bonuses, multipliers) is non-persistent in the current UI; the defaults created by `CurrencyService` remain in force.

**Likely fix**: call `SaveCurrencySettingAsync` (or the equivalent `IDataService` method) with the edited entity in the save command.

---

## Wiring gaps

### KI-8 (Wiring gap) — Twitch "Chat Enabled" toggle is cosmetic

Kick/YouTube/TikTok/Discord chat-settings toggles persist a `<Platform>ChatEnabled` Platform flag **and** publish `PlatformChatRoutingChangedEvent` so the chat feed's display filter updates live. Twitch has no such publisher — its toggle writes nothing, `TwitchFilterEnabled` is never fed from the DB, and Twitch messages can never be filtered out of the feed. This is a wiring gap, not a design choice.

### KI-9 (Wiring gap) — YouTube chat history is never persisted

[DataChatMessageStore.cs](../../src/VyralBot.UI/Services/Chat/DataChatMessageStore.cs) maps only `KickChatMessageEvent` and `TwitchChatMessageEvent` into the `ChatMessages` table. YouTube messages render in the feed but are never stored, so Load History never surfaces them. Also: the load button retrieves up to 200 messages per platform while its tooltip says 50 (minor drift), and the Twitch/Kick quick-settings popups build fresh VMs while YouTube's reuses the DI singleton (inconsistent lifecycle).

### KI-10 (Wiring gap) — ngrok token edits in Settings don't persist

`WebServerSettingsViewModel.SaveSettingsAsync` (`WebServerSettingsViewModel.cs:436-452`) writes only the DB row (`IsEnabled`, `Port`, `NgrokRegion`, `AutoStartOnLaunch`, `CurrentTunnelUrl`). The Authentication Token textbox on the Ngrok Configuration screen is effectively display-only — only the setup wizard persists the token via `NgrokService.SaveAuthTokenAsync`. Related dead setting: `WebServerSettings.IsEnabled` is loaded and saved but read by nothing and has no UI toggle.

### KI-11 (Wiring gap) — ChatEventSettingsService cache is not profile-aware

[ChatEventSettingsService.cs](../../src/VyralBot.Core/Services/Chat/ChatEventSettingsService.cs) caches settings in a process-lifetime dictionary keyed only `"{Platform}:{EventType}"`. On profile switch, `ProfileSwitchedEvent` triggers a full teardown/reload — but nothing invalidates this cache, so the new profile can be served the previous profile's chat-event settings until the app restarts. (The variables cache is invalidated properly on edits; this one is not.)

### KI-12 (Wiring gap) — Auto-reply templates use simple replace

`AutoReplyService.ProcessTemplate` (`AutoReplyService.cs:37-51`) is a case-insensitive `string.Replace` over only the context keys the handler passes (`{username}`, `{platform}`, event fields). Two visible consequences:

1. **Database and social variables do not resolve in auto-reply templates** — `{twitter}` etc. is sent literally. Unlike command responses, which run the full `VariableReplacementService`.
2. **Default templates ship as `@{username}`** (e.g. "Thanks for the follow @{username}!" in [KickChatSettingsViewModel.cs](../../src/VyralBot.UI/ViewModels/KickChatSettingsViewModel.cs):34) — the replace only covers `{username}`, so a stray `@` remains in the sent message.

**Likely fix**: route auto-reply templates through `VariableReplacementService` (context-over-DB merge), or at minimum strip a leading `@` when replacing `{username}`.

### KI-13 (Wiring gap) — ngrok URL rotation requires manual recovery

`NgrokService` monitors the tunnel URL every 30 s and raises `PublicUrlChanged`; the coordinator updates `CurrentTunnelUrl` in the DB but, if Kick webhook subscriptions were active, it only logs a warning — it lacks the OAuth token in that context to re-subscribe. On free ngrok plans the URL rotates routinely, so Kick webhooks silently point at a dead URL until the user toggles **Enable Webhooks** off/on in Kick settings.

### KI-14 (Wiring gap) — Auto-connect silently flips Chat Enabled to true

After any successful platform auto-connect at startup, `MainViewModel.cs:437-447` (Twitch; Kick and YouTube equivalents follow) force-sets `<Platform>ChatEnabled = true` in the DB when it was false. A user who deliberately disabled a platform's chat display loses that setting merely by being connected. It is a settings value changed as a side effect of connecting.

### KI-15 (Wiring gap) — Binding mismatches

- [KickSettingsView.xaml](../../src/VyralBot.UI/Views/KickSettingsView.xaml) `:159` binds `Channel`, but the ViewModel property is `ChannelName` — the read-only Channel box never populates.
- The startup profile selector's PIN padlock binds `Profile.HasPin`, but the `Profile` model has no such property (only the `PinCode` string) — the "PIN Protected" lock icon silently never renders. PIN entry still works because `ShowPinEntry` is driven by selection logic.

### KI-16 (Wiring gap) — Audio port pinned to 5000, connected-clients stub

The `AudioSettings` DTO defaults `WebSocketPort` to 8080, but `AudioSettingsViewModel` (`:195-204`) rejects any port edit and forces 5000 on every init — the persisted port is effectively always 5000 and the DTO default never surfaces. The "Restart Server" button is a no-op message box, and the WebSocket panel's "connected clients" count is a stub that always shows 0. The browser-source URL shown for OBS is likewise hard-coded to `http://localhost:5000/audio.html`.

---

## Dead surface

### KI-17 (Dead surface) — Dead buttons

Buttons that render as clickable but have no Command binding (verified in `MainWindow.xaml` / `SettingsView.xaml` / chat header popups):

- Sidebar: **Events & Alerts** (`MainWindow.xaml:251-259`), **Browser Sources** (`MainWindow.xaml:324-332`). (The sidebar **Help** button was dead until the in-app Documentation browser shipped — it now opens the docs.)
- Home support section: **Discord**, **Ideas & Suggestions**.
- Settings → Platforms cards: **YouTube / TikTok / Discord / OBS** (only Twitch/Kick navigate; OBS has no settings view at all).
- Chat header "All Platforms" popup action buttons: Start Giveaway / Start Poll / Send Announcement / Trigger Event.
- YouTube/TikTok/Discord quick-settings decorative checkboxes.
- 8 of the 13 Settings categories are "Coming Soon" placeholders (Theme, Data Management, Notifications, Commands, Timers, Automation, Performance, Security, Advanced).

### KI-18 (Dead surface) — Dead and orphaned code

- **Unreachable pages**: `VariablesView`/`VariablesViewModel` (hosted in Settings → Variables — no sidebar entry, no `NavigateToVariables` command); `ProfileSelectorWindow` + VM (superseded by the MainWindow overlay; the dead VM is also the only place with PIN-length validation).
- **Caller-less engines**: `AutoReplyService.GetAutoReplyAsync` — the full conditional-rules evaluation path — has no production callers, so rules created on the [Conditional Rules](../chat-features/conditional-rules.md) page never fire; `TriggerEngine`/`ActionDispatcher` start at boot but are TODO stubs (`ActiveTriggerCount = 0`, `Task.Delay(100)` simulation), and the `Triggers`/`Actions`/`EventLogs`/`ViewerPerks` tables have no writers (see [Database Reference](database-reference.md)).
- **Superseded/orphaned files**: `NgrokSetupView`/`NgrokSetupViewModel` (+ `.backup`/`.old` variants) — previous-generation ngrok UI; `DemoSetupView`/`DemoSetupViewModel`; `App.AutoConnectTwitchAsync`; `InMemoryChatMessageStore` (superseded by `DataChatMessageStore`); legacy port-8080 `AudioWebSocketServer`; `ImportProfileAsync` (no UI caller — only Export exists); `BadgesToImagesConverter` (instantiated, unused by the message template); `ChatEventSetting` sound columns `SoundEnabled`/`SoundFile`/`SoundVolume` (no UI, no callers — sounds are configured in Sounds Management instead).
- **Related shadowing**: the seeded, enabled `!watchtime` built-in returns a placeholder and runs before the real `WatchtimeCommandService`, so even a fixed pipeline would serve the placeholder until that row is disabled; `CommandContext.IsBroadcaster` is hard-coded `false` (`ChatViewModel.cs:629`), so broadcaster-only commands can never fire; commands have platform-gating flags for Kick/Twitch only.

---

## Security

### KI-19 (Security) — Kick webhook test-payload signature bypass

[KickWebhookValidationMiddleware.cs](../../src/VyralBot.Api/Middleware/KickWebhookValidationMiddleware.cs):64 skips RSA-SHA256 validation for any payload whose message id starts with `test-` or whose body contains `"test":true`. Anyone who knows the public endpoint can post forged "test" events that flow into the same EventBus and trigger chat messages, sounds, alerts, and currency bonuses.

**Likely fix**: restrict the bypass to a dev mode or a shared secret, or remove it entirely in release builds.

### KI-20 (Security) — Unsalted SHA256 PIN hashes

Profile PINs are hashed with plain SHA256 → base64 in `profile.json` — trivially brute-forceable for 4-6 digit inputs, and unsalted so hashes are comparable across profiles. Proper length/format validation exists only in the dead `ProfileSelectorViewModel`. Not a fund-security boundary (data is local), but the weakest link in the profile lock.

**Likely fix**: salt + a slow KDF (PBKDF2/Argon2).

### KI-21 (Security) — Inconsistent token storage

Twitch user access/refresh tokens are DPAPI-encrypted in the `Platforms` table; Kick and YouTube tokens sit plaintext in the same table (Kick's is deliberate per code comments, but the inconsistency is undocumented); OAuth client id/secrets are plaintext JSON (`twitch_credentials.json`); the ngrok token is plaintext JSON. Anyone with file/DB access gets full API access for the plaintext set. The `Configs.IsEncrypted` column exists but is unused.

### KI-22 (Security) — Wizards write Windows user environment variables

The ngrok wizard's vestigial OAuth methods write `VYRALBOT_NGROK_CLIENT_ID/SECRET` as **user environment variables** (`NgrokSetupWizardViewModel.cs:185-208`); the demo wizard writes dummy `VYRALBOT_*` vars the same way. The ngrok wizard also runs `ngrok config add-authtoken`, which writes the **machine/user-level** ngrok config shared across all VyralBot profiles even though the token JSON is per-profile. Hidden global state outside appdata.

### KI-23 (Security) — Machine-wide WebView2 process kills

At startup and exit, the app kills leftover `msedgewebview2` processes and deletes the WebView2 temp cache — machine-wide, not scoped to its own user-data folder. Other running WebView2-based applications on the same machine can have their processes killed.

**Likely fix**: scope cleanup to processes whose command line references the app's WebView2 user-data folder.

### KI-24 (Security) — Robustness notes

- **EventBus DropOldest**: the bounded channel (500 events) drops the oldest under burst — events can be lost silently by design; with 4 concurrent handlers, slow handlers amplify this.
- **Content-based echo suppression**: outgoing text is matched against incoming text within 10 s (15 s YouTube) — a viewer typing the identical text in that window is filtered as an echo.
- **Stub wizards mark "Connected"**: finishing the TikTok or Discord wizard writes a `Platform` row with `Status="Connected"` and empty tokens — the UI then treats a shell with nothing behind it as connected. Discord's "Send Test Message" opens a raw IRC connection to `discord.com:6667` (Discord has no IRC server) and writes `VYRALBOT_TWITCH_*` env vars. See [TikTok, Discord & OBS](../platforms/tiktok-discord-obs.md).
- **Single instance**: a named mutex enforces one process — profiles cannot run concurrently (documented behavior worth knowing rather than a defect).

---

## Doc drift

### KI-25 (Doc drift) — Root markdown vs code

The repo root contains 40+ planning/audit markdown documents; several materially disagree with the current code. Treat `AppDocs/` as authoritative:

- `README.md` lists implemented features (currency, commands, YouTube, giveaways, polls, statistics) as "planned".
- `docs/ui/profile-selector.md` describes the dead `ProfileSelectorWindow` as the live selector (the live one is the MainWindow overlay).
- `docs/application/app-entry.md` startup diagram is stale (Kestrel timing, DI registrations).
- `vyralbot.html` is a marketing mockup, not a spec.
- `OBS-addition.md` / `OBS_INTEGRATION_PLAN.md` describe unimplemented libobs-embedding plans; the only real OBS touchpoint is the audio browser source URL.
- KICK integration docs are historically accurate but the implemented webhook route is `/webhooks/kick` (not `/kick/webhook`).

---

## Cross-reference index

Which ledger entries affect which feature areas:

| Feature | Entries |
|---|---|
| [Commands](../chat-features/commands.md) | KI-1, KI-18 |
| [Variables](../chat-features/variables.md) | KI-12 |
| [Auto-Replies](../chat-features/auto-replies.md) | KI-1, KI-12 |
| [Conditional Rules](../chat-features/conditional-rules.md) | KI-18 |
| [Event History](../chat-features/event-history.md) | KI-3 |
| [Event Testing](../chat-features/event-testing.md) | none (fully functional) |
| [Giveaways](../stream-tools/giveaways.md) | KI-1, KI-5 |
| [Polls](../stream-tools/polls.md) | KI-1, KI-5 |
| [Watchtime](../stream-tools/watchtime.md) | KI-1 |
| [Currency & Rewards](../stream-tools/currency.md) | KI-1, KI-7 |
| [Statistics](../stream-tools/statistics.md) | KI-3 |
| [Browser Sources](../stream-tools/browser-sources.md) | KI-4, KI-17 |
| [Sounds](../stream-tools/sounds.md) | KI-4 |
| [Audio Settings](../stream-tools/audio.md) | KI-16 |
| [Visual Alerts](../stream-tools/visual-alerts.md) | KI-6 |
| [Chat Window](../chat/chat-window.md) | KI-9 |
| [Chat Settings](../chat/chat-settings.md) | KI-8, KI-11, KI-14 |
| [Event Reactions](../chat/event-reactions.md) | KI-2 |
| [Twitch](../platforms/twitch.md) | KI-2, KI-21 |
| [Kick](../platforms/kick.md) | KI-13, KI-19, KI-21 |
| [YouTube](../platforms/youtube.md) | KI-9, KI-21 |
| [TikTok, Discord & OBS](../platforms/tiktok-discord-obs.md) | KI-17, KI-24 |
| [Profiles](../getting-started/profiles.md) | KI-11, KI-15, KI-20 |
| [API, Web Server & ngrok](../settings/web-server-ngrok.md) | KI-10, KI-13, KI-22 |
| [App Shell](../home/app-shell.md) | KI-17 |
| [Event Bus](../architecture/event-bus.md) | KI-1, KI-24 |
| [Web Server](../architecture/web-server.md) | KI-19 |
| [Startup & Lifecycle](../architecture/startup-lifecycle.md) | KI-23 |
| [Data Layer](../architecture/data-layer.md) | KI-3, KI-18 |
