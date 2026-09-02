# Settings Tab: Per-Section Critical Settings — Design

Date: 2026-09-02
Status: Approved (user directive: "go through each one and determine critical settings"; Theme & Performance excluded — handled elsewhere)

## Problem

The Settings tab has seven "Coming Soon" placeholder categories (Data Management, Notifications,
Commands, Timers, Automation, Security, Advanced), the Platforms category only deep-links to
Twitch (stub) / Kick settings, and app launch auto-connects every platform whose DB status is
"Connected" with no user control. Token refresh at startup is hardcoded (Kick always pre-refreshes,
Twitch only reactively). Nothing in the app shows where data lives on disk.

## Decisions

- **Source of truth for per-platform auto-connect**: the existing `Platform.*ChatAutoStart` columns
  (already in the DB schema, only Kick's was wired). A one-time backfill migration
  (`Startup.AutoStartFlagsBackfilled` config key) sets each flag to `(Status == "Connected")` so
  current behavior is preserved for existing installs; after that the flags are authoritative and
  surfaced in Settings → Platforms and the Chat Hub.
- **Startup token refresh**: new config keys `Startup.Twitch.RefreshTokens` / `Startup.Kick.RefreshTokens`
  (default `true`). Twitch gains a real pre-connect refresh (falls back to stored access token on
  failure); Kick's existing pre-refresh becomes gated. YouTube refreshes intrinsically on connect
  and Discord bot tokens don't expire — no toggle for those.
- **Discord bots**: `Startup.DiscordBots.AutoConnect` (default `true`) gates `StartAllSavedAsync()`.
- **Connection logic sharing**: new `PlatformConnectionService` (UI singleton) extracts
  MainViewModel's four `LoadAndConnect*Async` flows; MainViewModel, Settings → Platforms and the
  Chat Hub all use it.
- **All other new settings** are config keys in the per-profile `Configs` table using the existing
  dotted `Area.Setting` convention, auto-saved on toggle (no Save button), loaded in each panel's
  Initialize.

## Panel-by-panel settings

| Panel | Settings |
|---|---|
| Profile | Unchanged (AutoLogin, support links, profile CRUD). |
| Platforms | Per-platform cards (Twitch/Kick/YouTube/Discord + TikTok placeholder): status, **Auto-connect at startup**, **Refresh tokens at startup** (Twitch/Kick), chat-enabled, Connect/Disconnect/Refresh-tokens-now buttons. Discord Bots: **start saved bots at startup**. Note pointing at API & Webhooks for webhook autostart. Existing Twitch/Kick deep links kept. |
| Data Management | All storage locations with computed path + size + Open button (profile folder, DB, credential files, ui-settings.json, sounds, logs, WebView2 cache, flow drafts/templates). Data inventory counts (commands, workflows, executions, event logs, chat messages, currency users, watchtime, giveaways, polls, rules, variables, bots). Cleanup: clear chat history / event logs / workflow executions, compact DB (VACUUM), export profile backup. |
| Notifications | Master toggles: event messages in chat (`Notifications.ShowEventsInChat`), event sounds (`Notifications.EventSoundsEnabled`). Links to Alert/Sounds pages. |
| Commands | Command prefixes (`Commands.Prefixes`, default `!?.`), per-platform command processing toggles (Twitch/Kick/Discord). |
| Variables | Unchanged (embedded VM). |
| Timers | Command timers master toggle (`Timers.CommandTimersEnabled`). |
| Automation | Workflows master toggle, command/event/timer trigger toggles, timer check interval seconds, execution-history retention days. |
| API & Webhooks | Unchanged (embedded VM; AutoStartOnLaunch already exists). |
| Security | Token storage info (what's encrypted, where), current-profile PIN status + Change PIN, auto-login note. |
| Advanced | App/version info, open-folder buttons, clear WebView2/flow-draft/block-template caches, compact DB. |

## Service changes

- `CommandService` (+interface): `InitializeAsync()` loads prefixes + per-platform gates; MainViewModel
  calls it after profile load.
- `WorkflowTriggerService`: instance settings (gates, tick interval) loaded in `StartAsync` via
  `ReloadSettingsAsync()` (also public for settings toggles); retention cleanup of old
  `WorkflowExecutions` on start.
- `CommandTimerService`: tick checks master toggle.
- `SoundService.PlayEventSoundAsync`: event-sounds master gate.
- `ChatViewModel`: cached `_showEventsInChat` gate on the eight `ShowInChat` event-handler sites.
- Chat settings VMs (Twitch/YouTube/Discord/TikTok): load + persist `AutoStartChat` (Chat Hub's
  "Auto-Start Chat" toggle becomes live).
- `ChatHubViewModel`/View: Connect / Disconnect / Refresh Tokens buttons for the selected platform.

## Data layer additions

`IDataService` (+DapperDataService, +ProfileAwareDataService passthrough):
`GetDataInventoryAsync()`, `DeleteAllChatMessagesAsync()`, `DeleteEventLogsAsync()`,
`DeleteWorkflowExecutionsAsync(workflowId?, olderThan?)`, `CompactDatabaseAsync()`.

## Defaults (all preserve today's behavior)

Auto-connect: backfilled to current status; refresh tokens: on; Discord bots autostart: on;
prefixes `!?.`; all command/timer/automation/notification toggles: on; timer interval 60s;
retention 90 days.

## Testing

`dotnet build` of the full solution; manual verification paths noted in code where UI-only.
