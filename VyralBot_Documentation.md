# VyralBot — Complete Feature Documentation

> Compiled 2026-08-15 from a full six-pass code review of the working tree.
> Source reviews (detailed, with file:line citations): `VyralAppReview1.md` (Home), `VyralAppReview2.md` (Chat), `VyralAppReview3.md` (Platforms), `VyralAppReview4.md` (Chat Features), `VyralAppReview5.md` (Stream Tools), `VyralAppReview6.md` (Settings).
> All paths in this document are relative to `VyralBot/VyralBot/` unless stated otherwise. Every claim was verified against code — including the "known issues" section, which is a core part of accurate documentation for this app.

---

## Table of Contents

1. [What VyralBot Is](#1-what-vyralbot-is)
2. [Architecture Fundamentals](#2-architecture-fundamentals)
3. [Home / Dashboard](#3-home--dashboard)
4. [Chat System](#4-chat-system)
5. [Platforms (Connections & Integrations)](#5-platforms-connections--integrations)
6. [Chat Features](#6-chat-features)
7. [Stream Tools](#7-stream-tools)
8. [Settings](#8-settings)
9. [Feature Interaction Map](#9-feature-interaction-map)
10. [The ngrok & Web Server Stack](#10-the-ngrok--web-server-stack)
11. [Known Issues, Dead Code & Quirks](#11-known-issues-dead-code--quirks)
12. [Persistence Reference](#12-persistence-reference)

---

## 1. What VyralBot Is

VyralBot is a **.NET WPF desktop application for live streamers** that unifies chat and stream management across multiple platforms — **Twitch, Kick, and YouTube** are functionally integrated; **TikTok and Discord exist as UI shells only**; an **OBS card is a "Coming Soon" placeholder**.

Core capabilities (as wired in the current build):

- **Unified multi-platform chat** — one merged chat feed from Twitch + Kick + YouTube with per-message platform logos, badges, colors, and timestamps; send messages to one selected platform or fan-out to **all connected platforms at once**.
- **Platform connections** — OAuth setup wizards (embedded WebView2) for each platform, automatic reconnection, per-platform dashboards with live stream previews.
- **Event reactions** — follows/subs/raids/cheers/hosts/etc. each trigger up to five side effects: a system message in chat, a sound (local + OBS browser source), a visual alert overlay, a currency bonus, and an auto-reply with a customizable template.
- **Chat tools** — commands, variables (template placeholders), auto-replies, conditional rules (rule engine), giveaways, polls, watchtime tracking, currency & rewards economy (games, bank, auctions, loyalty tiers), event history, statistics, event testing.
- **Local web server + ngrok** — an embedded Kestrel server (localhost:5000) that serves an OBS browser-source audio page, a WebSocket for pushing sounds to OBS, and receives **Kick webhooks through an ngrok tunnel**.
- **Profile system** — fully portable per-profile data (own SQLite DB, credentials, settings), PIN-protected, hot-switchable.

### Solution layout

| Project | Role |
|---|---|
| `src/VyralBot.UI` | WPF app: all Views/ViewModels, MainWindow shell, profile overlay, setup wizards |
| `src/VyralBot.Core` | Domain services: EventBus, chat, commands, variables, rules, currency, giveaways, polls, watchtime, statistics, audio, webhooks |
| `src/VyralBot.Data` | SQLite + Dapper persistence (`DapperDataService`, 36 tables), entities |
| `src/VyralBot.Api` | Embedded Kestrel server: WebhooksController, Kick validation middleware, NgrokService, WebhookCoordinatorService, wwwroot (audio browser source + sounds) |
| `src/VyralBot.Integrations` | Twitch (custom WebSocket IRC + EventSub), Kick (KickLib/Pusher), YouTube (polling) |
| `src/VyralBot.Plugins`, `src/VyralBot.Scripting` | Present in solution, effectively unused by shipped features |

Scale: ~256 C# files, ~49 XAML views. Root folder also contains 40+ planning/audit markdown docs (many now stale relative to code).

---

## 2. Architecture Fundamentals

### 2.1 Startup sequence (`src/VyralBot.UI/App.xaml.cs`)

1. **Single instance** enforced via named mutex `VyralBot_SingleInstance_Mutex` (profiles cannot run concurrently).
2. Serilog → console + rolling daily `logs/vyralbot-.txt` (7-day retention). `SustainedLowLatency` GC mode. WebView2 pre-warmed on a background thread.
3. Generic Host with **full DI**; nearly every service **and every ViewModel registered as singletons**.
4. Leftover `msedgewebview2` processes are killed and the WebView2 temp cache deleted (note: kills WebView2 processes machine-wide, not just its own).
5. `MainWindow` shows **first**, then the profile decision tree:
   - No profiles / missing current profile → **profile selection overlay** (first run forces creating a profile).
   - Multiple profiles → selector shown unless the current profile's `Settings["AutoLogin"]` is `"True"`.
   - Exactly one profile → silently auto-loads.
6. `OnProfileLoadedAsync` (`MainViewModel.cs:219-292`) runs the real boot:
   1. `WebhookCoordinatorService.InitializeAsync()` — auto-starts Kestrel + ngrok if `AutoStartOnLaunch` and an ngrok token exist.
   2. `SoundService.InitializeDefaultSoundsAsync()`.
   3. `ProfileSettingsLoader.LoadAllSettingsAsync(...)` — loads Kick settings, web-server settings, chat settings **before** any connection (ordering is deliberate: EventBus subscriptions must exist before messages can arrive).
   4. `CheckPlatformConnectionsAsync()` — auto-connects Twitch/Kick/YouTube whose DB `Status == "Connected"` (Twitch tokens DPAPI-decrypted; Kick refreshes its token first). Side effect: force-sets each `<Platform>ChatEnabled = true` on first auto-connect.
   5. `AutoEnableKickWebhooksAsync()` — re-enables Kick webhook subscriptions if infra is running and subscriptions exist.
7. `OnExit`: 10-second kill watchdog thread, platform disconnects, ChatViewModel cleanup, web server + ngrok teardown, WebView2 processes killed again, aggressive GC.

### 2.2 The EventBus — the app's nervous system (`src/VyralBot.Core/Services/EventBus/EventBus.cs`)

- In-process, typed pub/sub by exact CLR event type.
- Bounded channel of **500 events, DropOldest** under burst (events can be lost by design), max **4 concurrent handlers**.
- **One shared instance** across the WPF UI and the embedded Kestrel web server — this is why a Kick webhook arriving over HTTP reaches the exact same handlers as a WebSocket chat message.
- Producers: Twitch/Kick/YouTube integration services, WebhooksController, Event Testing page, profile service (`ProfileSwitchedEvent`), settings UI (`PlatformChatRoutingChangedEvent`, `PlatformConnectionChangedEvent`).
- Main consumer: `ChatViewModel` (~20 subscriptions). `TriggerEngine` subscribes to everything but is a stub (see §11).

### 2.3 Data layer & profiles

- `IDataService` (200+ method facade) → `ProfileAwareDataService` (decorator in UI) → `DapperDataService` (SQLite + Dapper).
- On **every call**, `ProfileAwareDataService` resolves the current profile and lazily points at `appdata/profiles/<Name>/data/vyralbot.db` (fallback profile `"temp"` when none selected). This is the mechanism that makes *everything* per-profile.
- On-disk layout (fully portable — no registry, no %APPDATA%):
```
appdata/
├── current_profile.txt                  # active profile name
└── profiles/<Name>/
    ├── profile.json                     # metadata, PIN hash, Settings dict (AutoLogin)
    ├── data/vyralbot.db                 # 36-table SQLite DB
    ├── twitch_credentials.json          # OAuth client id/secret (plaintext)
    ├── kick_credentials.json            # (checked by PlatformsViewModel)
    └── ngrok_credentials.json           # ngrok auth token (plaintext)
```
- Profile switching is **hot**: `ProfileSwitchedEvent` → MainViewModel disconnects all platforms, cleans ChatViewModel, disposes cached VMs, re-runs `OnProfileLoadedAsync`, navigates Home.

### 2.4 Navigation & memory model

- `MainViewModel.CurrentViewModel` + DataTemplates in `MainWindow.xaml` drive everything; views are created lazily by WPF.
- Every navigation: setup-wizard guard → `CurrentViewModel = null` → dispose other VMs → layout flush + 100 ms wait + **blocking Gen2 compacting GC** → fresh VM. Dashboard and Chat VMs are never disposed.
- Because all VMs are DI singletons, the per-navigation "scopes" return the same instances — disposal logging is somewhat misleading, but the lifecycle logic still works.
- Memory pressure drove many design choices (500-message chat cap, on-demand history loading after a 46 MB spike, `LazyPopupContent` popups, replacing TwitchLib/KickLib WebView2 with lighter transports).

---

## 3. Home / Dashboard

**Where:** inline DataTemplate in `MainWindow.xaml:505-769` (there is no DashboardView.xaml). ViewModel: `src/VyralBot.UI/ViewModels/DashboardViewModel.cs`.

The Home page is a **static navigation launcher** — four clickable cards plus a support section:

| Card | Navigates to |
|---|---|
| **Connections** — "Manage your platform connections" | Platforms page |
| **Chat** — "Complete chat management for all platforms" | Chat **Hub** (settings), not the chat feed |
| **Tools** — "Stream tools, chat features, and more" | Tools hub (a larger card-grid copy of the sidebar) |
| **Integrations** — "Third-party integrations and bots" | Integrations page (all items "Coming soon") |

Support rows: **Documentation** (a "Coming Soon" placeholder page), **Discord** and **Ideas & Suggestions** (dead buttons — hover styling but no command).

`DashboardViewModel` is a stub: four counter properties all hard-zeroed with a `// TODO`; none are bound in XAML. **Home shows no live data** — the only live "status" in the shell is the sidebar, where **Twitch/Kick entries appear only while connected** (`IsTwitchConnected`/`IsKickConnected` from DB `Platform.Status`; `IsYouTubeConnected` is tracked but has no sidebar entry).

### Profile selection (startup overlay)

The live selector is an inline modal overlay in `MainWindow.xaml:910-1161`: profile list ordered by last use, PIN entry inline (4-6 digit), and a **Create New Profile** card (name regex `^[a-zA-Z0-9_-]+$`, optional display name, optional PIN). A complete standalone alternative (`ProfileSelectorWindow` + VM, including delete-profile support the overlay lacks) exists but is **never instantiated** — dead code. PINs are stored as **unsalted SHA256** hashes.

The **Demo Wizard** (sidebar bottom) is a 4-step Google OAuth demo of the reusable `BaseOAuthSetupViewModel` wizard framework — purely demonstrative; it saves dummy credentials to Windows user environment variables and connects nothing.

---

## 4. Chat System

### 4.1 Unified chat page

**Where:** sidebar **Chat** → `ChatView.xaml` + `ChatViewModel.cs` (2,880-line singleton VM, created at profile load *before* platforms connect so subscriptions exist first).

**Merged feed.** Messages from Twitch, Kick, and YouTube arrive as typed EventBus events and render in one virtualized list (500-message cap, recycling). Each message shows: timestamp (`h:mm tt`), platform logo, badge images (broadcaster/mod/VIP/subscriber/verified mapped to Twitch CDN URLs — works for Kick's core badges via case-insensitive match), colored username, wrapped text. System/event messages render gold (`#FFD700`).

**Header controls:**
- Per-platform icon buttons → **quick-settings popups** (`LazyPopupContent` hosting each platform's chat-settings view). The **"All Platforms" (Vyral logo) button** opens a popup with *placeholder* action buttons — Start Giveaway / Start Poll / Send Announcement / Trigger Event — that have **no command bindings**.
- Toggle Auto-Scroll, Load Chat History, Clear Chat (UI only), Delete Chat History (from DB).

**Auto-scroll engine** (code-behind): tracks whether the user is within 20 px of the bottom; when scrolled up, incoming messages increment a "**N new messages**" jump-to-latest banner instead of scrolling.

**Input bar:** platform selector + message input (Enter = send) + SEND + a separate **round "send to all connected platforms"** button.

### 4.2 Multi-platform sending (the core differentiator)

- **Send to one platform:** `SendMessageAsync` dispatches via the selected platform's service. Variable placeholders (`{...}`) in the typed text are resolved first (context: `bot-username`, `channel`, `platform`).
- **Send to ALL platforms:** `SendToAllPlatformsAsync` (`ChatViewModel.cs:1161`) fans out in **parallel** (`Task.Run` per connected platform), collects per-platform failures, shows a single "Partial Send Failure" MessageBox if any fail, and adds one synthetic teal **"Vyral"** message to the feed representing the multi-post.
- Transports: Twitch → IRC PRIVMSG; Kick → KickLib REST **as the broadcaster's account** (auto-refreshes OAuth token if expiring <5 min); YouTube → `liveChat/messages` POST (401/403 → refresh + one retry). **TikTok/Discord sending is `TODO`.**
- **Echo suppression:** outgoing message text is recorded (lowercased) per platform; incoming matches within 10 s (15 s YouTube, due to polling) are dropped so the bot doesn't render its own sends twice. Content-based, so a viewer typing the identical text in the window is also filtered.

### 4.3 Chat history

- Not loaded on init (a former 46 MB spike); on-demand button loads up to **200 messages per platform** (tooltip says 50 — drift), **Kick + Twitch only** (YouTube is never persisted: `DataChatMessageStore` maps only Kick/Twitch events).
- Deduped via `Platform:User:Ticks:Message` keys; deletes per platform+channel from the DB with confirmation.

### 4.4 Chat Hub (settings hub)

Home's **Chat** card → `ChatHubView` — *not* a chat feed, but a settings hub with five platform tabs and DB-driven connection status (area dims + warning when the platform isn't connected):

- **General card** (per platform): "Chat Enabled" (show messages from this platform), "Auto-Start Chat" (connect on launch), "Auto-Replies".
- **Twitch tab:** per-event cards (Follow, Subscribe, Resub, Gift Sub, Raid, Cheer) — each with *Show in chat*, *Auto-reply*, and a template TextBox (`{username}`, `{tier}`, `{months}`, `{gifter}`, `{count}`, `{raider}`, `{viewers}`, `{bits}`).
- **Kick tab:** same for Follow, Subscribe, Gift Sub, Host, Kicks Gifted, Reward Redeemed (+ Stream Status show-in-chat only).
- **YouTube/TikTok/Discord tabs:** show-messages toggle only ("event auto-replies in a future update").

Persistence differs by platform (see §8.3): Twitch/Kick chat settings persist `ChatEventSetting` rows; YouTube/TikTok/Discord persist a `Platform`-entity flag and publish `PlatformChatRoutingChangedEvent` which live-updates the chat feed's display filter. **Twitch has no routing-event publisher, so its "Chat Enabled" toggle is cosmetic and Twitch messages can never be filtered out** — a wiring gap, not a design choice.

### 4.5 Incoming message flow (end-to-end)

```
Twitch IRC WebSocket ──┐
Kick Pusher WebSocket ─┼─→ typed event → EventBus → ChatViewModel → filter flag → echo check → render in feed
YouTube polling loop ──┘                                   └→ IChatMessageStore → per-profile SQLite (Kick/Twitch only)
```

- Twitch: custom `LightweightTwitchIrc` (wss://irc-ws.chat.twitch.tv), replaces TwitchLib (~30 MB saved). Publishes fire-and-forget (a former sync publish stalled the read loop).
- Kick: KickLib Pusher WebSocket for **chat**; channel *events* deliberately come from webhooks instead (duplicate-prevention — see §5.2). Some Pusher handlers also emit synthetic bot-authored display messages ("🎉 X just subscribed!") in addition to the typed events. Outgoing messages are sent **as the streamer**, not a bot account.
- YouTube: polling `liveChat/messages` honoring API `pollingIntervalMillis`; discovers `liveChatId` via `liveBroadcasts?mine=true` (active → all, includes upcoming/testing); when offline, stays "connected" and retries every 30 s. Only `textMessageEvent` items (super chats/memberships filtered out).

### 4.6 Event reactions pipeline (the biggest interaction hub)

For every Kick/Twitch follow/sub/gift/host/raid/cheer/reward event (13 handlers in ChatViewModel), gated by that event's `ChatEventSetting`:

1. **System chat message** (if *Show in chat*) — gold system entry.
2. **Sound** — `SoundService.PlayEventSoundAsync(type, platform)` (local playback + browser-source broadcast; see §7.2/7.3).
3. **Visual alert** — `AlertService.ShowAlertAsync` → WPF overlay window (currently hard-coded SlideInFromTop / TopCenter / 5 s regardless of the Alert Settings page).
4. **Currency bonus** — `CurrencyService.AwardEventBonusAsync` (e.g. follow 100, sub 500, 50/gifted-sub, 10/raider, 1/bit, host 200 — all configurable via `CurrencySetting`, × platform earn-multiplier).
5. **Auto-reply** — if enabled, template processed (`{username}`, `{displayname}`, `{tier}`, `{months}`, `{streak}`, `{bits}`, `{viewers}`, `{gifter}`, `{count}`, `{amount}`, `{reward}`, `{cost}`, `{input}`, `{hoster}`, `{sender}`, `{message}`, `{platform}`) and sent to that platform, **rate-limited to one per user/event/platform per 30 s**.

### 4.7 What chat does NOT have

- **No moderation**: no banned words, filters, timeouts, bans, deletes, or purges anywhere (only passive display of Kick `moderation.banned` webhook notices).
- No emote rendering (plain text), no mention highlighting, no search, no popout chat.
- **The generic command pipeline is dead** — see §6.1 and §11.

---

## 5. Platforms (Connections & Integrations)

**Where:** Home → **Connections** → `PlatformsView` — cards for Twitch, Kick, YouTube, TikTok, Discord, and an OBS "Coming Soon" placeholder. Each card opens a **WebView2 setup wizard**, then a connection view with masked credentials, a static OAuth-permissions checklist (hardcoded XAML, not scope-driven), and RECONNECT/DISCONNECT. Connected platforms also appear in the sidebar (Twitch/Kick) with dashboard pages featuring **live stream preview** via embedded player (`player.twitch.tv` / `player.kick.com`, `parent=localhost`).

All wizards share redirect `http://localhost:3000` (navigation intercepted client-side inside WebView2 — no local listener needed except Twitch's browser-flow variant). Wizards are built on `BaseOAuthSetupViewModel` / `BaseWebSetupViewModel` frameworks (4 steps: login → developer console/paste credentials → authorize → test).

### 5.1 Twitch (fully implemented)

- **Auth:** OAuth 2.0 authorization-code, ~75 scopes (duplicated in two lists in `TwitchOAuthService.cs` — drift risk), CSRF `state`, `force_verify=true`. Client id/secret in plaintext `twitch_credentials.json` per profile; **user tokens DPAPI-encrypted** in the DB row.
- **Chat:** custom WebSocket IRC (`LightweightTwitchIrc`) — caps `twitch.tv/tags commands membership`, keep-alive pings, auto-reconnect ≤5 attempts with backoff, auth-failure → token refresh + reconnect.
- **Events:** TwitchLib **EventSub WebSocket** (no ngrok/webhooks needed) subscribing to `channel.follow` v2, subscribe, subscription.message, subscription.gift, raid, cheer.
  - ⚠ **Likely never connects**: EventSub requires `Platform.UserId`, and no code path ever sets it for Twitch — so follow/sub/raid/cheer events probably never arrive unless the field is populated externally (§11).
- **Disconnect semantics:** deletes the platform's DB row entirely (unique among platforms).

### 5.2 Kick (fully implemented, hybrid transport)

- **Auth:** KickLib OAuth with PKCE; scopes `user.read channel.read chat.write events.subscribe moderation.ban kicks.read`. Tokens stored **unencrypted** in the DB (deliberate, per code comments — contrasts with Twitch).
- **Chat:** KickLib **Pusher WebSocket** (`ListenToChatRoomAsync(chatroomId)`). Requires `ChannelId`+`ChatroomId` stored by the wizard — which obtains them by navigating the embedded WebView2 to the *unofficial* `kick.com/api/v2/channels/{name}` endpoint and scraping the JSON (Cloudflare workaround).
- **Channel events:** official **webhooks through ngrok** (see §10). Duplicate-prevention design (from the WEBHOOK_DUPLICATE_AUDIT work): coordinator **unsubscribes everything, then subscribes only to webhook-only event types** (`LivestreamMetadataUpdated`, `ModerationUserBanned`) because Pusher covers the rest. `chat.message.sent` webhooks are accepted but ignored.
- **Webhook security:** RSA-SHA256 signature validation against Kick's published public key (cached 1 h), requiring signature/message-id/timestamp headers. ⚠ Test payloads (`test-` id prefix or `"test":true`) **bypass validation** — spoofable.
- **Quirk:** the Pusher follow event carries no username (`Username="Unknown"`); the webhook path is the one that knows it.
- Historical: this service replaced a WebView2-based Kick client (40–60 MB savings).

### 5.3 YouTube (implemented, chat only)

- **Auth:** Google OAuth, scope `youtube.force-ssl` only, redirect `/oauth2callback`; wizard walks Google Cloud Console setup with deep links. Tokens plaintext in DB; auto-refresh when <5 min to expiry.
- **Chat:** polling (see §4.5). Only `YouTubeChatMessageEvent` exists — no follow/sub event ingestion.
- Sending fails with "You must be streaming live" when no broadcast is active.

### 5.4 TikTok & Discord (UI shells — no backend)

- Both wizards are template copies with TODO stubs: OAuth URLs are hardcoded placeholder strings, callbacks always return null, credentials save/load are no-ops.
- ⚠ **Finishing either wizard still writes a `Platform` row with `Status="Connected"` and empty tokens** — the UI then treats the platform as connected with nothing behind it.
- Discord absurdity: its "Send Test Message" opens a raw **IRC connection to discord.com:6667** (Discord has no IRC server) and writes `VYRALBOT_TWITCH_*` env vars — Twitch code copied verbatim.

### 5.5 OBS

Placeholder card on the Platforms page and Settings page; no OBS integration exists in code. The root `OBS-addition.md` / `OBS_INTEGRATION_PLAN.md` describe unimplemented libobs-embedding plans. The only real OBS touchpoint is the **audio browser source URL** (§7.1).

---

## 6. Chat Features

Sidebar **Chat Features** group contains: *Events & Alerts* (dead button — no command binding), **Currency & Rewards**, **Commands**, **Event History**. Conditional Rules lives under Stream Tools; Event Testing is a standalone sidebar item; Variables has no sidebar entry at all.

### 6.1 Commands

**UI:** `CommandsView` — three tabs: **Built-in Commands** (read-only grid with Enabled / Kick / Twitch toggles + usage counter), **Custom Commands** (full CRUD with permission, global/user cooldowns, per-platform enable, inline VariablePicker on the Response field), **Help**.

**Engine** (`src/VyralBot.Core/Services/Commands/CommandService.cs`):
- Prefixes: **`!`, `?`, `.`** (all normalize to `!name`).
- Permission hierarchy: Everyone < Subscriber < VIP < Moderator < Broadcaster (each level passes for equal-or-higher). Unauthorized and on-cooldown uses are **silently ignored**.
- Global cooldown then per-user cooldown (`CommandCooldown` rows); usage counting.
- Built-in special-cases: `!commands` (lists up to 10 permitted), `!uptime` ("coming soon" placeholder), `!watchtime` (placeholder returning currency balance — the real one lives in WatchtimeCommandService). Others return their stored template.
- **Seeded built-ins (21):** `!commands`, `!uptime`, `!socials`, `!discord`, `!watchtime` + 16 currency commands (`!points`, `!leaderboard`, `!gamble`, `!redeem`, `!rewards`, `!daily`, `!rank`, `!store`, `!slots`, `!duel`, `!accept`, `!heist`, `!roulette`, `!link`, `!give`, `!take`).
- Responses support variables: `{username} {platform} {channel} {balance} {role}` + custom context.
- Platform gating flags exist for Kick/Twitch only.

⚠ **Critical wiring gap (confirmed by three reviewers independently):** the *entire* incoming-chat command pipeline is dead. `ChatViewModel.OnChatMessageReceivedAsync` — which orchestrates CommandService → Currency → Giveaway → Poll → Watchtime command handling, watchtime activity tracking, and chat-activity currency — subscribes to `ChatMessageReceivedEvent`, which **nothing in the solution ever publishes**. The real per-platform message handlers only render UI. Consequence: **`!` commands typed in chat currently do nothing.** The management UIs work; runtime execution does not. (What *does* run from events: auto-replies, sounds, alerts, currency event bonuses — those hang off real published events.)

### 6.2 Variables (template placeholder system)

**Engine:** `VariableReplacementService` — `{kebab-case}` placeholders; merges DB variables with per-call context (context wins); recursive resolution to depth 5 with circular detection; 5-minute cache invalidated on edits; unresolvable placeholders left as-is.

**Seeded catalog:**
- **19 system/context variables** (`Value=null`, must be supplied by caller): `username`, `message`, `months`, `number`, `platform`, `viewers`, `bits`, `reward`, `tier`, `gifter`, `raider`, `hoster`, `sender`, `amount`, `balance`, `role`, `channel`, `uptime`, `watchtime`.
- **6 auto-updating platform variables** (synced from events/API): `kick-followercount`, `kick-subscribercount`, `kick-viewcount`, `twitch-followercount`, `twitch-subscribercount`, `twitch-viewcount`.
- **6 editable social variables:** `twitter`, `discord`, `youtube`, `instagram`, `tiktok`, plus composite `full-socials` (`Twitter: {twitter} | Discord: {discord} | YouTube: {youtube}`).

**UI:** a full Variables manager exists (`VariablesView` + VM, hosted in Settings → Variables) with category filter, search, add/edit/delete of editable variables — but it has **no navigation path from the sidebar** (unreachable except via Settings). The **VariablePicker** is attached to *every TextBox application-wide* via a global style in `App.xaml` (context-menu insert at caret).

**Consumers:** command responses (full service incl. DB variables), manual chat sends (`{` triggers replacement), auto-reply templates (⚠ simple `string.Replace` only — DB/social variables do *not* resolve there, and default templates using `@{username}` leave a stray `@`).

### 6.3 Auto-Replies

- **Live mechanism** = per-event `ChatEventSetting` rows (ShowInChat / AutoReplyEnabled / AutoReplyMessage) edited in the Chat Hub or chat-header quick-settings popups; executed by the ChatViewModel event handlers with a 30 s/user/event/platform rate limiter.
- **Dormant mechanism** = `AutoReplyService.GetAutoReplyAsync` — a full conditional-rules engine (loads `AutoReplyRule` rows ordered by priority, evaluates `RuleCondition` JSON, counts triggers). **Zero production callers.**

### 6.4 Conditional Rules (Auto-Reply Rules)

**UI:** sidebar Stream Tools → **Conditional Rules** (`ConditionalRulesView`, header literally "Auto-Reply Rules"). Edits `AutoReplyRule` rows: Name, Platform (All/Twitch/Kick/Discord), Event Type (Follow, Subscription, Raid, Host, Bits, Donation, SubscriptionGift, ChatMessage, ChannelPointRedemption), Enabled, Priority, Condition JSON (builder + raw editing; multiple conditions wrap into AND trees), Reply Template.

**Condition engine (`RuleEvaluator`) — fully implemented:**
- Condition types: **Value** (numeric-first compare with epsilon, else case-insensitive string ops), **Status** (boolean), **Time** (local `time_of_day`/`day_of_week`/`hour`/`date`), **Complex** (recursive AND/OR).
- Operators: Equals, NotEquals, GreaterThan, LessThan, GreaterOrEqual, LessOrEqual, Contains, StartsWith, EndsWith, **Modulo** (milestones: `count % 100 == 0` via `ModuloDivisor`).

⚠ Rules created here are persisted but **never fire** — the only evaluator caller is the dormant `GetAutoReplyAsync` (§6.3). "Test Rule" shows a MessageBox summary without evaluating.

### 6.5 Trigger Engine & Action Dispatcher (scaffolding)

`Trigger`/`BotAction` entities and `TriggerEngine`/`ActionDispatcher` services exist and the engine starts at boot subscribing to all events — but the handler is a TODO stub (`ActiveTriggerCount = 0`), and the dispatcher simulates work with `Task.Delay(100)`. Not wired to real behavior.

### 6.6 Event History & Event Testing

- **Event History:** filters (platform/event-type/username/date-range, default 7 days), client-side pagination (50/page), record details viewer, and a "View auto-reply" previewer that substitutes variables from the record's `EventData`. ⚠ Reads the `EventStatistics` table, which **no code ever writes** (`SaveEventStatisticAsync` has zero callers) — the page can only ever be empty.
- **Event Testing** (standalone sidebar item, after Settings): a developer tool publishing **synthetic events onto the real EventBus** — 6 Kick + 6 Twitch buttons (Follow, Subscribe, GiftSub, Host/KicksGifted, RewardRedeemered/Raid, Cheer, etc. with realistic payloads). Everything downstream (chat message, sound, alert, currency, auto-reply) reacts exactly as for live events. Fully functional; note the 30 s auto-reply rate limiter suppresses repeated clicks.

### 6.7 Currency & Rewards (also see §7.8)

Chat-features-relevant surface: the currency **chat command set** (~25 commands — aliases under §7.8), reward redemptions with an approval queue, event bonuses (§4.6), watchtime earning, chat-activity earning, first-chat and daily-streak bonuses. The CurrencyView **Settings tab's Save button does not persist anything** (logs success, calls no save API).

---

## 7. Stream Tools

Sidebar **Stream Tools** group: Browser Sources (**dead button** — no command), Audio Settings, Sounds Management, Statistics, Visual Alerts, Conditional Rules (§6.4), Giveaways, Polls, Watchtime. The Tools hub page is a static card grid bound to the same navigation commands.

### 7.1 Browser Sources

There is **one** browser-source page in the entire app: `audio.html` — intended as an OBS Browser Source that connects back over WebSocket and plays sounds over HTTP when events fire. **No alert/poll/giveaway overlay HTML pages exist** (alerts are an in-app WPF window).

- Two copies ship: `BrowserSource/audio.html` (legacy, hardcodes port 8080) and `src/VyralBot.Api/wwwroot/audio.html` (current, hardcodes `ws://localhost:5000/audio-ws`). Behavior: connect → status pill (auto-hides) → auto-reconnect every 3 s → play `http://<host>/sounds/<file>` on message. Volume field in messages is ignored browser-side.
- Served by the embedded Kestrel server (§10) with `/sounds/*` static files and the `/audio-ws` WebSocket; a static `AudioWebSocketBroadcaster` delegate bridges Core's SoundService to connected OBS pages.
- ⚠ **Broken in practice:** SoundService broadcasts PascalCase `AudioEvent` JSON (`{"EventType":...}`), while audio.html only handles `{"type":"play","sound":...}` — **viewer-side browser-source sounds can never fire** as shipped. Local (streamer) playback still works (.wav only).
- ngrok nuance: the server binds localhost; ngrok's public URL exists for webhooks. audio.html hardcodes localhost:5000, so the tunnel is *not* how OBS consumes it.

### 7.2 Audio Settings

Two output toggles — **Streamer Sounds** (play locally) and **Stream Sounds** (send to browser source) — plus **Master Volume** (0–100), a server status panel (port hard-pinned to 5000; edits reverted), the **Browser Source URL** (`http://localhost:5000/audio.html`) with copy button and OBS step-by-step instructions, and Test Sound. Persisted as one JSON blob in Configs key `AudioSettings`; read by SoundService on every play (toggles gate local/browser paths; volume = master × per-event). No device selection, routing, or ducking; local playback is **.wav only** (mp3/ogg/m4a log a warning and play nothing locally).

### 7.3 Sounds Management

- **Library:** upload (.wav/.mp3/.ogg/.m4a; duplicates suffixed `_1`,`_2`), delete (also clears event assignments), preview. Files live in `Api/wwwroot/sounds` (served by Kestrel).
- **Event mapping table:** fixed list of **10 events** — Kick: follow/subscribe/giftsub/host; Twitch: follow/subscribe/resub/giftsub/raid/cheer — each with file assignment, per-event volume slider, and Test Event Sound. Persisted immediately to Configs keys `EventSound_<Platform>_<event>` / `EventVolume_<Platform>_<event>`.
- Triggers live in the ChatViewModel event handlers (§4.6). No YouTube/TikTok/Discord mappings, no soundboard/hotkeys, no command-triggered sounds. Ships no bundled default sounds (missing defaults only logged).

### 7.4 Visual Alerts

- **Runtime:** in-app WPF `AlertOverlayWindow` (AlertService) — title/username/message/optional image, 4 animations (SlideInFromTop/Bottom, FadeIn, Bounce), 9 screen positions computed against the primary screen, auto-close timer + fade. Single-alert queue (a new alert replaces the current one). No TTS/GIFs/templates.
- ⚠ **The Alert Settings page is a façade:** load/save are TODOs — Save shows success but persists nothing, and real alerts hard-code SlideInFromTop / TopCenter / 5 s regardless of the page's controls.
- `src/VyralBot.Core/Services/Alerts/` is an empty folder.

### 7.5 Giveaways

- **Create:** name, description, entry keyword (default `enter` → chat command `!enter`), winner count, max entries, subscriber bonus (toggle + extra entries, default 5), min watchtime, followers-only, subscribers-only, allowed platforms (default "Twitch,Kick"), optional scheduled end.
- **Engine** (`GiveawayService`): lifecycle Pending → Active → Completed/Cancelled, **only one active giveaway**; weighted drawing (each entry contributes `EntryCount` tickets; unique winners with retry cap 100); winners stored and posted to chat.
- **Chat commands:** `!giveaway start|enter|winner|draw|cancel|status` (mod-gated for start/draw/cancel) + the custom entry keyword. Cross-platform entries are recorded, but replies only go to Twitch/Kick — and the whole command path is currently dead (§6.1).
- ⚠ **FollowersOnly and MinWatchtime permanently reject everyone**: ChatViewModel passes `isFollower:false` and `watchtimeMinutes:0` when invoking the service (watchtime data exists but isn't plumbed in). SubscribersOnly works.
- No overlay output for winners.

### 7.6 Polls

- **Create:** question, options (one per line, min 2), allow multiple votes (= vote *changing*), min watchtime, followers-only, subscribers-only, allowed platforms (default "All"), scheduled end.
- **Engine** (`PollService`): same lifecycle pattern, single active poll; `VoteAsync` validates option/platform/follower/sub/watchtime/duplicate (or *moves* the vote when multiple-votes allowed); `EndPollAsync` aggregates per-option counts into `VoteResults` JSON and announces the winner.
- **Chat:** `!poll start|vote <n>|results|end|cancel|status` (mod-gated start/end/cancel). Same isFollower/watchtime gap and Twitch/Kick-only replies; command path dead (§6.1). No overlay output for live results.

### 7.7 Watchtime

- **Read-only dashboard:** all viewers (by total minutes), top viewers (limit 1–100), active viewers + count, selected-viewer detail, refresh, manual "Reset stream session".
- **Engine:** `RecordUserActivityAsync` upserts `UserWatchtime` rows; `WatchtimeBackgroundService` ticks **every 1 minute** incrementing active viewers (+**1 currency/minute**) and every 2 minutes deactivates viewers silent for **5 minutes**.
- **Chat:** `!watchtime`, `!watchtime @user`, `!watchtime top|leaderboard|lb [n≤10]` (command path currently dead, §6.1).
- ⚠ **Watchtime is chat-activity time, not viewing time** — only viewers who send messages are ever tracked (and the tracking call sits in the dead generic pipeline, so nothing is being recorded at all right now).

### 7.8 Statistics & Currency

**Statistics:** date-range (default 30 days) / platform / event-type filters; event-count summary grid, top contributors per event type (top 20, value extracted from `EventData` JSON: `count`/`amount`/`viewers`/`bits`), top users overall, CSV export. ⚠ Reads the never-written `EventStatistics` table — **always empty** (§6.6).

**Currency & Rewards** (top-level "Currency" nav + Tools hub card; the app's largest subsystem):

- **Earning:** chat activity (per message, cooldown-gated), watchtime (per interval + the 1/min from WatchtimeService), event bonuses (follow 100 / sub 500 / giftsub 50×count / raid 10×viewers / cheer 1×bit / host 200 — toggles + amounts in `CurrencySetting`), daily streaks (day 1/5/10 escalating), first-chat bonus; role multipliers (Sub 2.0 / VIP 1.5 / Mod 1.5) and per-platform earn multipliers.
- **Rewards store:** CRUD items (name/description/cost/requires-approval/cooldown/limits), redemption → currency deduction → `CurrencyRedemption` (Pending when approval required) → **approve / reject (refund) / complete** queue in the Redemptions tab.
- **Games:** `!slots`, `!duel`+`!accept`, `!heist`, `!roulette` (`CurrencyGameService`, with play limits).
- **Bank:** deposit/withdraw + **daily interest** payouts (`CurrencyBankService`).
- **Auctions:** mod-run (`!auction start/end/cancel/info`, `!bid`).
- **Boosts:** timed global/platform earn multipliers (mod `!boost`).
- **Loyalty tiers** from total earned; **cross-platform linked accounts** with verification codes (`!link`).
- **Decay:** hourly background service applying inactivity decay, bank interest, boost expiry.
- **Command aliases:** `!points|!balance|!currency`, `!leaderboard|!top`, `!give`/`!take` (mod), `!redeem`, `!rewards`, `!gamble|!bet`, `!daily`, `!rank|!tier`, `!store|!shop`, `!slots`, `!duel`, `!accept`, `!heist`, `!roulette`, `!link`, `!boost` (mod), `!bid`, `!auction` (mod), `!deposit`, `!withdraw`, `!bank`. Moderator-gated ones reply with an explicit "must be a moderator" message (unlike CommandService's silence).
- Full transaction audit trail (`CurrencyTransaction`); economy stats API exists but isn't surfaced.
- ⚠ Settings tab Save doesn't persist (§6.7); all chat earning is dead with the command pipeline (§6.1) — only **event bonuses** currently actually award currency.

---

## 8. Settings

### 8.1 The Settings page (13 categories; 4 implemented)

Two-pane layout (280 px category sidebar + content):

| Category | State |
|---|---|
| **Profile** | ✅ Auto-Login toggle + full profile management (list/create/switch/export/delete, PIN change) |
| Theme, Data Management, Notifications, Commands, Timers, Automation, Performance, Security, Advanced | ❌ "Coming Soon" placeholders (9 of 13) |
| **Platforms** | ✅ 6 cards — but only Twitch/Kick navigate anywhere; YouTube/TikTok/Discord/OBS cards are dead buttons |
| **Variables** | ✅ hosts the Variables manager (§6.2) |
| **API & Webhooks** | ✅ Web server + ngrok (§8.2/§10) |

Dialogs: delete-profile confirmation, change PIN (4–6 digits), create profile, switch profile (PIN).

### 8.2 API & Webhooks (web server + ngrok configuration)

- **Ngrok Tunnel card:** RUN SETUP (launches the 4-step wizard) or CONFIGURE (inline token + region editor).
- **Webhook Server card:** START/STOP (stop blocked while features depend on the server — `WebServerDependencyTracker`), status, public URL display + COPY, TEST HEALTH (opens `{PublicUrl}/webhooks/health`).
- **Server Configuration:** Auto-start on Launch toggle (turning it on while stopped starts the stack immediately), Local Port (default 5000, disabled while running).
- ⚠ **Ngrok auth-token edits here don't persist** — the Save command writes only the DB fields; only the wizard saves the token.
- ⚠ `WebServerSettings.IsEnabled` is loaded/saved but read by nothing and has no UI toggle.

**Ngrok Setup Wizard** (4 steps, extends `BaseOAuthSetupViewModel`): install check (Microsoft Store link + `ngrok version`) → sign-in link → WebView2 onto the authtoken dashboard + paste modal → **Test Connection** (format sanity check, spins up server+tunnel, health-checks through the tunnel, tears down). Completion saves the token per-profile **and** runs machine-level `ngrok config add-authtoken` (shared across profiles — the token JSON is per-profile but the ngrok agent config is global). Vestigial methods also write `VYRALBOT_NGROK_CLIENT_ID/SECRET` user env vars.

### 8.3 Per-platform settings — three distinct families (don't confuse them)

1. **Settings → Platforms → Twitch/Kick cards:**
   - *Twitch settings page is an empty stub* ("configuration coming soon").
   - *Kick settings page is real:* connection status + disconnect; account fields (⚠ `Channel` binding mismatch — binds a nonexistent property, box stays empty); **Webhook Settings** toggle (requires coordinator running + Kick AccessToken + UserId; enable → subscribe webhook-only events; auto-unchecks if the server stops; state from `WebhookSubscriptions` rows); **Chat Settings** — Enable Kick Chat (persists `Platform.KickChatEnabled` + publishes the routing event so the chat feed filter updates live), Auto-start chat on launch (`KickChatAutoStart`), Start/Stop Chat button, Enable auto-replies (Configs key `Kick.AutoRepliesEnabled`).
2. **Chat Hub per-platform chat settings** (§4.4) — `ChatEventSetting` rows (ShowInChat/AutoReply/template) for Twitch (6 events) + Kick (7); YouTube/TikTok/Discord persist a `<Platform>ChatEnabled` Platform flag + routing event.
3. **Platforms page web-setup wizards** — OAuth flows that *write* the Platform rows/tokens (setup, not settings).

### 8.4 Cross-cutting settings behaviors

- **Load order matters:** `ProfileSettingsLoader` loads Kick → WebServer → Chat settings *before* auto-connect so subscriptions/settings exist before traffic.
- **Auto-connect side effect:** first successful auto-connect silently flips `<Platform>ChatEnabled` false→true in the DB.
- ⚠ **`ChatEventSettingsService` cache is not profile-aware** and isn't invalidated on profile switch — stale rows from the previous profile can be served until restart.
- Audio/Alert/Sounds settings live on their own top-level pages (§7.2–7.4), not in the Settings page.

---

## 9. Feature Interaction Map

How the major pieces talk to each other (the "spine" of the app):

```
                        ┌────────────────────────── VyralBot process ──────────────────────────┐
                        │                                                                       │
 Twitch IRC WS ────────►│                                                                       │
 Kick Pusher WS ───────►│  typed events ──► EventBus (500 cap, 4 workers) ──┬─► ChatViewModel  │
 YouTube polling ──────►│                                                          │   ├─ render feed │
                        │                                                          │   ├─ sound       │
 ngrok tunnel ──► Kestrel :5000 ──► /webhooks/kick ──► validated ──► events ────┘   ├─ alert       │
      ▲                │      ├─ /audio-ws  ◄─── SoundService (browser sounds) ────┤─ currency    │
      │                │      └─ wwwroot (audio.html + /sounds/*) ──► OBS          └─ auto-reply  │
      │                │                                                                       │
 OBS browser source ◄──┘   (intended; JSON-shape mismatch currently breaks it — §7.1)            │
                        │                                                                       │
 Outgoing chat ◄───────── ChatViewModel send: selected platform | ALL platforms (parallel)        │
                        │   Twitch=IRC · Kick=broadcaster account · YouTube=liveChat POST        │
                        │                                                                       │
 (intended, DEAD) ──────► ChatMessageReceivedEvent → Commands → Currency → Giveaway → Poll →      │
                        │   Watchtime → watchtime tracking → chat-activity currency (§6.1)        │
                        │                                                                       │
 Watchtime ──1 currency/min──► Currency ──► Rewards/Games/Bank/Auctions/Tiers                    │
 Events ──bonuses──► Currency          Variables ←── auto-updated by follower/sub counts         │
 Profiles ◄── ProfileAwareDataService re-points every DB call; switch = full teardown+reload      │
                        └───────────────────────────────────────────────────────────────────────┘
```

Key interaction facts:

- **One EventBus, two front doors.** A Kick follow arriving via ngrok→Kestrel→WebhooksController and a Twitch cheer arriving via EventSub WebSocket hit the *same* handlers with the same side effects.
- **ChatViewModel is the integration hub.** Every event reaction (message/sound/alert/currency/auto-reply) is orchestrated there, not in the individual tool services.
- **Currency has five income streams** (chat activity, watchtime, event bonuses, daily/first-chat, admin give) and many sinks (rewards, games, bank, auctions) — all per (username, platform) with a full transaction ledger.
- **The web server is shared infrastructure**: webhooks, browser-source audio page, sounds hosting, health endpoint — one Kestrel instance, one ngrok tunnel, dependency-tracked shutdown.
- **Profiles isolate everything**: DB, credentials, ngrok token, and all feature data are per-profile; hot-switching rebuilds the world.

---

## 10. The ngrok & Web Server Stack

Everything internet-facing runs through one pipeline:

1. **WebhookCoordinatorService** (orchestrator): start = Kestrel (`WebServerService`, localhost:Port from `WebServerSettings`, default 5000) → load per-profile ngrok token → `NgrokService.StartTunnelAsync` (`ngrok http <port> --region <r> --authtoken <t>`) → poll `127.0.0.1:4040/api/tunnels` for the https URL → persist `CurrentTunnelUrl`.
2. **NgrokService** monitors the URL every 30 s and raises `PublicUrlChanged`. ⚠ On rotation (free plan), Kick subscriptions are **not** auto-repointed — the coordinator lacks the OAuth token in that context, so it only warns and the user must toggle Kick webhooks off/on.
3. **Kestrel middleware chain:** WebSockets → static files (`audio.html`, `/sounds/*`) → routing → `KickWebhookValidationMiddleware` (RSA-SHA256 vs Kick's public key; GETs and "test" payloads bypass) → controllers (`POST /webhooks/kick`, `GET /webhooks/health`) + `/audio-ws` endpoint.
4. **Kick webhook subscriptions** (`EnableKickWebhooksAsync`): unsubscribe-all first (duplicate prevention), then subscribe **only** to `LivestreamMetadataUpdated` + `ModerationUserBanned` (Pusher covers the rest), saving `WebhookSubscription` rows and registering a dependency that blocks stopping the server.
5. **What ngrok is NOT used for:** Twitch (EventSub WebSocket — no inbound needed), YouTube (outbound polling), the OBS audio page (hardcoded localhost URL).

---

## 11. Known Issues, Dead Code & Quirks

The single most important section for anyone working on this app. All verified by multiple reviewers against code.

### Critical (feature-silencing)

1. **The chat command pipeline is dead.** `ChatMessageReceivedEvent` is never published by anything, so commands, currency/giveaway/poll/watchtime chat commands, watchtime tracking, and chat-activity currency never execute. Everything currently works off *event* handlers only. (Would likely be fixed by having platform services publish this event or routing typed events into the generic handler.)
2. **Twitch EventSub almost certainly never connects** — gated on `Platform.UserId`, which no code path sets. Twitch follows/subs/raids/cheers likely never arrive.
3. **`EventStatistics` is never written** (`SaveEventStatisticAsync` has zero callers) → Statistics and Event History pages read a permanently empty table.
4. **Browser-source sounds can never play** — PascalCase `AudioEvent` JSON vs audio.html's `{"type":"play"}` contract mismatch.
5. **Giveaway/poll eligibility**: `isFollower:false` and `watchtimeMinutes:0` are hard-passed → FollowersOnly and MinWatchtime options reject everyone.
6. **Alert Settings page persists nothing** (Save is a TODO that fakes success); real alerts use hard-coded animation/position/duration.
7. **Currency Settings tab Save doesn't persist** (same fake-success pattern).

### Wiring gaps & drift

8. Twitch "Chat Enabled" toggle is cosmetic (no routing-event publisher; filter can never turn off).
9. YouTube chat history never persisted (`DataChatMessageStore` maps Kick/Twitch only).
10. ngrok token edits in Settings don't persist (only the wizard saves); `WebServerSettings.IsEnabled` read by nothing.
11. `ChatEventSettingsService` cache not invalidated on profile switch (stale cross-profile data until restart).
12. Auto-reply templates: `@{username}` defaults leave a stray `@` (processor only replaces `{username}`); DB/social variables don't resolve in auto-replies (simple string-replace path).
13. ngrok URL rotation requires manual Kick webhook re-enable.
14. First auto-connect silently flips `<Platform>ChatEnabled` to true.
15. `KickSettingsView` binds a nonexistent `Channel` property (box stays empty); startup selector's PIN padlock binds nonexistent `Profile.HasPin` (icon never renders).
16. Audio port stored but pinned to 5000 (DTO default 8080 never surfaces); "connected clients" display on the Audio page is a stub showing 0.
17. History tooltip says 50, loads 200/platform. Twitch/Kick quick-settings popups build fresh VMs while YouTube's uses the DI singleton (inconsistent).
18. Command replies/announcements only deliver to Twitch/Kick even where entries/votes record cross-platform; `CommandContext.IsBroadcaster` hard-coded `false`; no YouTube/TikTok/Discord platform-gating flags on commands.

### Dead buttons (render as clickable, do nothing)

Sidebar **Events & Alerts**, **Browser Sources**, **Help**; Home **Discord**, **Ideas & Suggestions**; Settings platform cards **YouTube/TikTok/Discord/OBS**; Chat header **All-Platforms popup** action buttons (Start Giveaway/Start Poll/Send Announcement/Trigger Event); YouTube/TikTok/Discord quick-settings decorative checkboxes.

### Dead / orphaned code

- `ProfileSelectorWindow` + VM (full standalone selector incl. delete — unreachable; the live overlay can't delete profiles except via Settings).
- `DemoSetupView`/`DemoSetupViewModel` (old demo wizard), `App.AutoConnectTwitchAsync`.
- `NgrokSetupView`/`NgrokSetupViewModel` (+ `.backup`/`.old` variants) — previous-generation ngrok UI.
- Variables manager page has no navigation path; `AutoReplyService.GetAutoReplyAsync` (conditional rules path) has no callers; `TriggerEngine`/`ActionDispatcher`/`Trigger`/`BotAction` are stubs; `InMemoryChatMessageStore` superseded by `DataChatMessageStore`; legacy 8080 `AudioWebSocketServer`; `ImportProfileAsync` has no UI caller; `BadgesToImagesConverter` (rich badge table) instantiated but unused by the message template; `ChatEventSetting` sound columns (SoundEnabled/File/Volume) have no UI and no callers.

### Security & robustness notes

- Kick webhook **test-payload signature bypass** (spoofable `test-` ids / `"test":true`).
- **Unsalted SHA256 PIN hashes**; PIN-length validation only exists in the dead selector VM.
- Inconsistent token storage: Twitch user tokens DPAPI-encrypted; Kick/YouTube plaintext in DB (Kick deliberate); client secrets plaintext JSON.
- Demo/ngrok wizards write **Windows user environment variables**; ngrok wizard writes machine-level ngrok config.
- Startup/exit kill **all** `msedgewebview2` processes machine-wide (can kill other apps' WebView2).
- EventBus DropOldest can silently drop events under burst; Echo suppression is content-based (can eat legitimate viewer messages).
- Finishing the TikTok/Discord stub wizards marks the platform "Connected" with empty tokens.

### Doc drift (root *.md vs code)

README lists implemented features (currency, commands, YouTube, giveaways, polls, statistics) as "planned"; `docs/ui/profile-selector.md` describes the dead window as live; `docs/application/app-entry.md` startup diagram is stale (Kestrel timing, registrations); `vyralbot.html` is a marketing mockup, not a spec; OBS docs are aspirational; KICK integration docs are historically accurate but the implemented webhook path is `/webhooks/kick` (not `/kick/webhook`).

---

## 12. Persistence Reference

| Data | Where | Written by |
|---|---|---|
| Active profile name | `appdata/current_profile.txt` | ProfileService |
| Profile metadata, PIN hash, `AutoLogin` | `appdata/profiles/<Name>/profile.json` | ProfileService / Settings page |
| **Everything else below** | per-profile SQLite `data/vyralbot.db` (36 tables) | via `IDataService` |
| Platform rows (tokens, status, chat flags, Kick channel IDs) | `Platforms` | wizards, settings pages, auto-connect |
| Command definitions + cooldowns + usage | `Commands`, `CommandCooldowns` | Commands page, CommandService (seeds 21 built-ins) |
| Variables | `Variables` | Variables manager (seeds 31) |
| Per-event chat settings | `ChatEventSettings` | Chat Hub / quick-settings popups |
| Auto-reply rules (conditional) | `AutoReplyRules` | Conditional Rules page |
| Chat history (Kick/Twitch only) | `ChatMessages` | DataChatMessageStore |
| Giveaways / entries | `Giveaways`, `GiveawayEntries` | GiveawayService |
| Polls / votes | `Polls`, `PollVotes` | PollService |
| Watchtime | `UserWatchtime` | WatchtimeService/BackgroundService |
| Currency (balances, settings, rewards, redemptions, transactions, games, bank, auctions, boosts, tiers, streaks, linked accounts) | `Currency*` family | Currency services |
| Event statistics (**never written**) | `EventStatistics` | — (no callers) |
| Web server settings (port, region, autostart, tunnel URL) | `WebServerSettings` (Id=1) | WebServerSettingsViewModel + coordinator |
| Kick webhook subscriptions | `WebhookSubscriptions` | WebhookCoordinatorService |
| Audio settings blob | `Configs["AudioSettings"]` | AudioSettingsViewModel |
| Event→sound/volume mappings | `Configs["EventSound_*"], ["EventVolume_*"]` | SoundsViewModel |
| Kick auto-replies toggle | `Configs["Kick.AutoRepliesEnabled"]` | KickSettingsViewModel |
| ngrok auth token | `<profile>/ngrok_credentials.json` (plaintext) | Ngrok wizard → NgrokService |
| Twitch client id/secret | `<profile>/twitch_credentials.json` (plaintext) | TwitchOAuthService |
| Alert settings | **not persisted anywhere** | — (TODOs) |

---

*End of compiled documentation. For exhaustive per-section detail with exact file:line citations, see `VyralAppReview1.md` through `VyralAppReview6.md` in this folder.*
