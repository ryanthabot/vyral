# Discord Bot Runtime (gateway, multi-bot manager)

Files: `VyralBot.Integrations/Discord/DiscordService.cs` (one gateway session),
`VyralBot.Integrations/Discord/DiscordBotManager.cs` (N sessions + persistence),
`Events/DiscordChatMessageEvent.cs`, `Events/DiscordMemberJoinedEvent.cs`,
`Events/DiscordBoostEvent.cs`. UI: `DiscordBotsViewModel` (list),
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
  (`DiscordBot.{botId}.Integrations` / `.DisabledBuiltIns` /
  `.WelcomeSettings`, removed via
  `IDataService.DeleteConfigValueAsync`). Each group is best-effort
  (a failing group is logged and never blocks the removal).
- Tokens live in the per-profile SQLite DB (`DiscordBots.Token`, legacy
  `Platform` row `Name="Discord"`), stored DPAPI-wrapped at rest by
  `DapperDataService`/`SecretProtector` (`"v1:"` envelope; plaintext rows are
  unwrapped on read as-is and re-wrapped on next save), masked in UI
  (PasswordBox) and never logged; error paths surface `ex.Message` only (no
  token material).
- Home/"primary" connection is funneled through `AdoptPlatformTokenAsync`
  (single gateway per token across legacy and manager worlds).

## Message pipeline

`MessageReceived` → self/system filtered → `DiscordChatMessageEvent` (carries
`BotId` for reply routing) + generic `ChatMessageReceivedEvent` → EventBus →
`ChatViewModel.OnDiscordChatMessageReceivedAsync` (unified chat, DM routing,
`DiscordFilterEnabled` filter). Listening is NOT restricted to the selected
channel: the selected channel is the SEND target only (`SendMessageAsync`,
used by announcements); `AssignedChannelIds` gates currency earning only.

## Workflow interactions (slash commands & vyralwf components)

`SlashCommandExecuted` / component handlers (`HandleWorkflowSlashInvocationAsync`,
`HandleWorkflowComponentInteractionAsync`) own the interaction respond path:

- **Defer at entry:** `DeferInteractionAsync(interaction, label)` acknowledges
  EVERY workflow interaction with an ephemeral defer ("thinking…") BEFORE the
  engine runs, so flows slower than Discord's 3-second window (HTTP, wait,
  loop nodes) never show "interaction failed". A lost ack is logged at debug
  and swallowed.
- **Ephemeral replies:** `RespondEphemeralAsync(interaction, message)`
  (exposed to the engine as the `Respond` delegate on
  `DiscordSlashInvocation`/`DiscordComponentInteraction`, mapped to `Reply` on
  the engine records) sends `RespondAsync` when unacknowledged, else an
  ephemeral `FollowupAsync` — both with `MessageFlags.Ephemeral` (invoker-only
  visibility). Denials, cooldown notices and the end-of-run completion followup
  all go through it.
- **send_message "Ephemeral" param:** `SendMessageNode` honors the block's
  optional `Ephemeral` bool on slash/component-triggered runs by replying
  through that ephemeral interaction path instead of a channel send; the flag
  is silently ignored for normal channel sends, Buttons/Menu messages,
  platform overrides and non-Discord platforms (see BlockCatalog).

## Guild member events (joins & boosts)

`UserJoined` (GUILD_MEMBER_ADD) publishes `DiscordMemberJoinedEvent`;
`GuildMemberUpdated` publishes `DiscordBoostEvent` ONLY on an observed
`PremiumSince` null → value transition (a boost started).

- **Dedup:** joins are deduped per (guild, user) inside a 10-minute window
  (`_recentMemberJoins`, pruned past 512 entries), so reconnect-handshake
  replays never double-fire. Boosts use `_premiumSinceByMember` (last seen
  PremiumSince per member): a KNOWN null → value transition publishes, an
  unknown prior (first sight of a member — e.g. a reconnect snapshot of an
  existing booster) is recorded silently and never published. Both maps are
  cleared on manual disconnect but survive Discord.Net auto-reconnects.
- **Publishing** mirrors the chat pipeline (fire-and-forget `Task.Run` +
  `IEventBus.PublishAsync`, info-level logs, `BotId` tagging).
- **INTENT GATE:** GUILD_MEMBER_ADD / GUILD_MEMBER_UPDATE are only delivered
  with the privileged `GatewayIntents.GuildMembers`, which is deliberately
  NOT requested in `CreateClient` — requesting it without the "Server Members
  Intent" toggle in the Discord Developer Portal gets the IDENTIFY rejected
  (close 4014) and kills ALL connectivity. Handlers are wired regardless:
  enable the portal toggle, then add the intent flag; no other code change.
- **Consumers:** `WorkflowTriggerService` dispatches workflow event triggers
  `discordmemberjoined` / `discordboost` (aliases "join", "memberjoined",
  "boost", "serverboost", ...). Alerts/stats are NOT wired yet — they live in
  `ChatViewModel` (see `OnTwitchFollowAsync` for the pattern to mirror).

## Welcome + auto-role on member join (per bot)

`DiscordBotManager` subscribes ONCE to `DiscordMemberJoinedEvent` (registered
fire-and-forget in its constructor, unsubscribed in `Dispose`) and dispatches
per bot — the owning `DiscordService` tags every join with its `BotId`.

- **Settings** (`DiscordBotWelcomeSettings`, Configs key
  `DiscordBot.{botId}.WelcomeSettings`, JSON PascalCase like the sibling
  per-bot keys): `Enabled`, `GuildId`, `ChannelId`, `Message`, `AutoRoleId`.
  `GuildId` is the guild the channel/role were picked from — joins of any
  OTHER guild are ignored (snowflake ids are guild-scoped). Manager API:
  `GetWelcomeSettingsAsync` (never null; malformed rows fall back to defaults)
  / `SaveWelcomeSettingsAsync`; removed with the bot by `RemoveBotAsync`.
- **Handler** (`OnDiscordMemberJoinedAsync`): loads the settings FRESH on
  every join (no cache — detail-page saves apply to the very next join), then,
  when enabled and the guild matches, (1) sends
  `DiscordBotWelcomeSettings.FormatMessage(message, ...)` to the channel via
  the bot's own `SendToChannelAsync` and (2) grants the role via
  `GrantRoleAsync(guildId, userId, roleId)` (the same API the mod command
  engine uses). Template variables: `{user}` (mention, plain-name fallback),
  `{username}`, `{server}`. The two halves are independent try/catch — one
  failing logs at info and never blocks the other, and the whole handler is
  wrapped so the bus pipeline can never crash.
- **UI:** the Welcome & Auto-Role card on the bot detail page
  (`DiscordBotDetailViewModel.LoadWelcomeAsync`/`SaveWelcomeSettingsAsync`)
  builds channel + role pickers from the bot's FIRST guild (same first-guild
  convention as the mod role tiers; @everyone and managed roles skipped) and
  offers "(none)" entries to clear a pick.
- **Intent gate applies:** joins (and therefore this feature) only arrive with
  the privileged GuildMembers intent (see above).

## Per-bot ALLOWED CHANNELS (command permissions)

`DiscordBot.{botId}.AllowedChannels` (Configs key, plain JSON string array —
the same shape as `DiscordBot.{botId}.DisabledBuiltIns`) holds the snowflake
ids of the channels a bot answers commands in.

- **UI:** the Command channels card on the bot detail page
  (`DiscordBotDetailViewModel.LoadChannelFeaturesAsync`/`SaveAllowedChannelsAsync`)
  — a checkbox list of the bot's first guild's text channels, persisted through
  `IDiscordBotManager.SaveAllowedChannelsAsync`. Empty list = every channel
  allowed (legacy behavior until channels are picked).
- **Enforcement:** `CommandService.ExecuteAsync` (Core) — where every unified
  command is matched. After the per-bot `BotId` scope check it reads the key
  live through a 30-second TTL cache (`IsDiscordChannelAllowedAsync`) and
  returns null when the triggering channel is not on the list. The channel id
  flows in on `CommandContext.DiscordChannelId` (set from
  `ChatMessageReceivedEvent.ChannelId`). Moderators/broadcasters bypass the
  check, and the built-in `!mod` family routes through `IDiscordCommandHandler`
  (`ChatViewModel.TryHandleDiscordModCommandAsync`) — a separate path the
  allowlist never gates, so mod commands always work for mods.
- **Model:** `DiscordBotAllowedChannels` (Integrations) owns the key/JSON
  helpers; Core cannot reference Integrations, so `CommandService` parses the
  same JSON inline (the `DisabledBuiltIns` precedent).
- **Cleanup:** removed with the bot by `RemoveBotAsync` →
  `CleanupBotScopedDataAsync` (per-bot Configs keys).

## Scheduled messages (per-bot recurring announcements)

`DiscordBot.{botId}.ScheduledMessages` (Configs key, JSON list of
`DiscordBotScheduledMessage`: `Id`, `Enabled`, `ChannelId`, `IntervalMinutes`
5..1440, `Message`, `NextRunUtc`). Bounded at 25 entries per bot
(`MaxPerBot`).

- **Timer:** ONE manager-wide `System.Threading.Timer` in
  `DiscordBotManager` (created in the constructor, disposed in `Dispose`)
  ticks every 30 seconds as a self-rearming one-shot —
  `RunScheduledMessagesTickAsync` overlap-guards with an
  `Interlocked` flag and re-arms in its `finally`, so ticks never stack.
- **Evaluation:** each tick snapshots the running bot ids, loads each bot's
  schedules fresh (detail-page saves apply to the very next tick) and, per due
  entry, sends `FormatMessage` text (`{time}`/`{date}`, local culture) through
  the bot's own `SendToChannelAsync`, then persists the moved next-run clocks
  once per bot per tick. A due entry fires ONCE and resets a full interval out
  — ticks missed while the bot/app was offline are never caught up. A freshly
  saved entry (`NextRunUtc` null) schedules its first run one interval out.
  Entries fire only while their bot is connected; every failure is logged at
  info and never blocks the other bots or entries.
- **UI:** the Scheduled messages card on the bot detail page (add/edit/remove
  rows + SAVE through `IDiscordBotManager.SaveScheduledMessagesAsync`, which
  bounds the list and nulls every `NextRunUtc` so saving restarts the cycle).
- **Cleanup:** key removed with the bot by `CleanupBotScopedDataAsync`; timer
  disposed first in `DiscordBotManager.Dispose`.

## Lifecycle at app level

- Launch auto-start: `MainViewModel.AutoStartDiscordBotsAsync` (Platform row
  flag + per-bot `AutoStart`), one-shot guarded, master toggle
  `Startup.DiscordBots.AutoConnect`.
- Shutdown: `App.OnExit` calls `StopAllAsync` (6s budget, shutdown watchdog
  force-kills); DI disposal then runs `DiscordBotManager.Dispose` which
  disposes every remaining non-legacy service — zero orphan sessions.
