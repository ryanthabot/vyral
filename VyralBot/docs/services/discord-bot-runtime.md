# Discord Bot Runtime (gateway, multi-bot manager)

Files: `VyralBot.Integrations/Discord/DiscordService.cs` (one gateway session),
`VyralBot.Integrations/Discord/DiscordBotManager.cs` (N sessions + persistence),
`Events/DiscordChatMessageEvent.cs`. UI: `DiscordBotsViewModel` (list),
`DiscordBotDetailViewModel` (per-bot dashboard).

## Gateway session (DiscordService)

- Client: `DiscordSocketClient` with intents `Guilds | GuildMessages |
  MessageContent | DirectMessages` (MessageContent is required for text
  commands). Heartbeat, session resume, auto-reconnect with backoff and REST
  rate-limit buckets are handled by Discord.Net.
- `ConnectAsync(token)` is guarded by a per-service connection lock and a
  process-wide `LiveTokenSessions` registry: at most ONE gateway per token
  across every service instance (a second IDENTIFY kills the older session
  with close 4003).
- `DisconnectAsync` unhooks all handlers before stop/logout; the stopped client
  is parked and disposed lazily on the next connect or `Dispose`.
- `ConnectionStateChanged` (raised from `SetConnectionState`, defensively)
  fires on initial connect, every auto-reconnect and every drop. The bot
  manager subscribes while the service is running and fans out `BotsChanged`
  so open UI re-reads the live `IsConnected` flag — the bots list stays
  truthful after network blips and permanent session loss. The persisted row
  Status is NOT rewritten on flaps (it is the next launch's auto-restore hint).
- Identity: `BotIdentityDetected` (username/application id/avatar from READY)
  is persisted by the manager onto the bot's `DiscordBots` row.

## Multi-bot manager (DiscordBotManager)

- One `DiscordService` per `DiscordBots` row; `_running` dictionary guarded by
  a lock, all start/stop serialized by an operation gate. One bot's failure
  never touches the others (`StopAllAsync` wraps each stop in try/catch).
- `StartBotAsync`: refuses duplicate tokens across bots; evicts a stale entry
  for the SAME bot and — when that entry may still be auto-reconnecting —
  fully shuts it down first (Disconnect + Dispose) so no orphaned gateway
  keeps the token's live-session slot; adoption path claims the legacy
  wizard singleton's session instead of opening a second one.
- `RemoveBotAsync` (removing a bot from the Discord Bots page) also deletes
  the bot's SCOPED data so no orphans linger: Command rows pinned to the bot
  (`Commands.BotId`), command timers pinned to it, its Discord role→mod-tier
  mappings, its monitored connected channels and its per-bot Configs rows
  (`DiscordBot.{botId}.Integrations` / `.DisabledBuiltIns`, removed via
  `IDataService.DeleteConfigValueAsync`). Each group is best-effort
  (a failing group is logged and never blocks the removal).
- Tokens live in the per-profile SQLite DB (`DiscordBots.Token`, legacy
  `Platform` row `Name="Discord"`), masked in UI (PasswordBox) and never
  logged; error paths surface `ex.Message` only (no token material).
- Home/"primary" connection is funneled through `AdoptPlatformTokenAsync`
  (single gateway per token across legacy and manager worlds).

## Message pipeline

`MessageReceived` → self/system filtered → `DiscordChatMessageEvent` (carries
`BotId` for reply routing) + generic `ChatMessageReceivedEvent` → EventBus →
`ChatViewModel.OnDiscordChatMessageReceivedAsync` (unified chat, DM routing,
`DiscordFilterEnabled` filter). Listening is NOT restricted to the selected
channel: the selected channel is the SEND target only (`SendMessageAsync`,
used by announcements); `AssignedChannelIds` gates currency earning only.

## Lifecycle at app level

- Launch auto-start: `MainViewModel.AutoStartDiscordBotsAsync` (Platform row
  flag + per-bot `AutoStart`), one-shot guarded, master toggle
  `Startup.DiscordBots.AutoConnect`.
- Shutdown: `App.OnExit` calls `StopAllAsync` (6s budget, shutdown watchdog
  force-kills); DI disposal then runs `DiscordBotManager.Dispose` which
  disposes every remaining non-legacy service — zero orphan sessions.
