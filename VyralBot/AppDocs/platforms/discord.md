# Discord

> Connect Vyral to your Discord servers as one bot — or several at once: a four-step bot-token setup wizard for the primary bot, a **Discord Bots** page that manages any number of additional bots (per-bot start/stop, per-bot active channel, per-bot commands), and Discord messages flowing through the same unified chat feed and command pipeline as Twitch, Kick, and YouTube.

## Overview

Discord is Vyral's fourth fully integrated platform. Unlike Twitch, Kick, and YouTube — which connect through OAuth authorization in an embedded browser — Discord connects as a **bot account with a bot token**: you create an application in Discord's Developer Portal, copy its bot token, invite the bot to your server, and paste the token into Vyral. No OAuth dance, no redirect URI.

Since 2026-08-16 Vyral supports **multiple Discord bots at once**. A `DiscordBotManager` owns one `DiscordService` instance per configured bot; every bot listens to every text channel (and direct messages) in every server it is a member of and feeds messages into the unified [Chat Window](../chat/chat-window.md), and — because Discord messages also publish the platform-agnostic `ChatMessageReceivedEvent` — the **entire command pipeline works on Discord**: custom commands, currency, giveaways, polls, and watchtime. Each bot **sends** through its own designated channel — the *active channel* you pick on that bot's dashboard — and replies route back to the bot that received the message.

What Discord integration deliberately is not: it is chat-only. There are no Discord-native slash commands from the command system itself (the app's `!`-prefix commands work) — the one exception is the [visual command builder](#visual-command-builder-workflows) on the Automation page, whose Slash Command triggers register real `/` commands. Discord events beyond chat messages (member joins, boosts, and so on) are not surfaced. See [Status & Known Issues](#status--known-issues).

## User Guide

### Before you start

- A Discord account that can sign in at the [Developer Portal](https://discord.com/developers/applications).
- The **Manage Server** permission on the server you want to invite the bot into (Discord requires it to authorize bot invites).
- Roughly five minutes.

### The setup wizard (four steps)

Open **Platforms** in the sidebar and click the **Discord** card. The wizard runs inside the page.

**Step 1 — Create Application.** Click the button to open the Discord Developer Portal inside Vyral's embedded browser. Sign in if asked, click **New Application**, give it a name (for example "Vyral Bot"), and create it. Every bot belongs to an application, so this comes first.

**Step 2 — Bot & Token.** In the portal, open your application, go to the **Bot** tab, click **Reset Token**, confirm, and copy the token — Discord shows it only once. Then, still on the Bot tab, scroll to **Privileged Gateway Intents** and enable **MESSAGE CONTENT INTENT** (Vyral cannot read messages without it) and save changes. Back in Vyral, click **Save Bot Token** and paste the token into the popup. It is stored masked; treat it like a password.

**Step 3 — Invite & Authorize.** Vyral derives the application ID from the token and builds the invite link automatically:

```
https://discord.com/oauth2/authorize?client_id={id}&scope=bot&permissions=8
```

The link requests **Administrator** permissions (the owner's choice — the bot can fully manage the server without re-inviting later). Click **Open Invite Page** to authorize it inside the embedded browser (pick your server, click Authorize). Then click **Connect Bot**: Vyral validates the token, opens the gateway connection, and reports "Connected as {bot}". If you skipped the token save in step 2, paste the token into the manual fallback field and use **Use Token**.

**Step 4 — Test & Finish.** The page shows the bot account Vyral is connected as. **Send Test Message** posts a confirmation to the active channel — if none is set yet it tells you to pick one on the Discord page after finishing, which is normal on a first connect. Click **Finish**. The wizard only saves when a token was entered *and* validated — you cannot finish into a fake connected state.

After finishing, the Discord card on the Platforms page flips to a connection view (bot username, RECONNECT / DISCONNECT), and the **Discord** entry in the sidebar's Platforms group (always visible — it is the door to the [Discord Bots page](#multiple-bots), not a connected-only dashboard anymore) opens bot management.

### Multiple bots

Click **Discord** in the sidebar (Platforms group; the window title reads "Vyral - Discord Bots"). The page manages every bot in this profile:

- **Quick Add** — the primary add path: type a **name** and paste the bot **token** (from that application's Developer Portal **Bot** tab — remember the MESSAGE CONTENT intent for every bot), then click **Add**. The bot appears as a card, stopped; click **START** on the card to connect it.
- **Per-bot cards** — each bot gets one card with its name, status (Connected / Disconnected / Error), and buttons: **OPEN** (the bot's own dashboard — next section), **START**, **STOP**, **RENAME** (inline), and **REMOVE** (stops the bot if running and deletes it from the list).
- **RUN FULL WIZARD** — the secondary add path: opens the full four-step wizard described above. Note the wizard configures the **primary setup bot** (its token goes to the legacy `Platform` row, not a per-bot row) — for additional bots, Quick Add is the per-bot path ([KI-36](../reference/known-issues.md)).
- **Auto-start** — on every app launch, each bot that was connected when the app last ran is started again automatically (`StartAllSavedAsync` after profile load). Stopping a bot and exiting means it stays stopped; starting it and exiting means it comes back.
- **Primary migration** — the first time the bots page loads an empty bot list, the legacy single-bot setup (the wizard's token on the `Platform` row) is migrated into a bot named **"Primary"**, keeping its token, status, and selected channel. Your old setup keeps working unchanged.

Bot names must be unique within the profile; adding a duplicate name is refused. Every bot's token, status, and selected channel live in the per-profile `DiscordBots` table (see [Data & Settings](#data--settings)).

### The per-bot dashboard

Click **OPEN** on a bot card (title "Vyral - Discord Bot"). The page is that bot's own control room:

- **Connection** — "Connected as {bot}" with status and **START** / **STOP** / **RECONNECT** buttons. RECONNECT drops and re-establishes the gateway session; it reads the token from the shared legacy `Platform` row, so it is only correct for the bot whose token that row holds (normally the migrated Primary or the wizard's setup bot) — for any other bot, use STOP + START, which reads the bot's own saved token ([KI-36](../reference/known-issues.md)).
- **Servers** — one row for **every server that bot is in**: server name, approximate member count, and a drop-down of the text channels visible to the bot. Pick a channel and click **SET ACTIVE CHANNEL** — this is a *per-bot* choice, so two bots can talk in two different channels (or servers).
- **Test Message** — the owner-ping test: in every server the bot is in, it reuses (or creates) the private `#vyral-test` channel visible only to the server owner and the bot, and posts a `@owner` confirmation there.

The **active channel** is the single channel that bot sends in: manual replies routed to that bot and its test messages go there. A bot *listens* across every channel of every server it is in, but its replies always post to its own active channel — so point each bot at the channel where you want it to talk. The choice is saved per bot and survives restarts and reconnects.

### Discord chat in the unified window

Messages from **every connected bot** appear together in the [Chat Window](../chat/chat-window.md) like any other platform: Discord logo, Discord blurple (`#5865F2`) username color, and up to two badges — a moderator badge for members with the Discord **Manage Messages** permission, a broadcaster badge for the server owner and members with the **Administrator** permission. Direct messages show the channel name "DirectMessage". The feed does not separate bots — a message from any bot is just a Discord message.

Replying to a Discord message in the unified chat sends through **the bot that received it** (the message-id-to-bot mapping is remembered per message); manual sends and send-to-all use the **Primary** bot — the first connected bot, falling back to the wizard's setup bot (see [How It Works](#how-it-works-developer)).

Discord also appears in the platform selector under the chat input (with **Send to All** including it), and the Discord icon button in the chat header opens its quick-settings popup with a **show/hide messages** toggle — see [Chat Settings](../chat/chat-settings.md).

### Commands on Discord

Everything documented in [Command Catalog](../reference/command-catalog.md) works when typed in Discord: `!points`, `!leaderboard`, giveaway entry keywords, `!poll` votes, `!watchtime`, and every custom command you have defined. The same `!`/`?`/`.` prefixes apply — the built-in command system has no slash commands (workflow Slash Command triggers are the exception — next section).

Discord-specific behaviors:

- **Replies go through the originating bot, to that bot's active channel.** A command typed in a chat the second bot listens to is answered by the second bot, in the channel selected on that bot's dashboard — not necessarily where it was typed. (Origin resolution has a rare race that falls back to the Primary bot — see [KI-38](../reference/known-issues.md).)
- **Commands can be scoped per bot.** On the [Commands](../chat-features/commands.md) page a custom command can be pinned to one bot ("This bot only") or left unscoped so every bot runs it. Built-in commands and the Twitch/Kick toggles are unaffected by bot scope.
- **Permissions map from Discord roles**: members with **Manage Messages** count as moderators (mod-only commands), and the server owner plus anyone with **Administrator** counts as the broadcaster (broadcaster-only commands).

### Visual command builder (workflows)

The [Automation page](../chat-features/automation.md) hosts a drag-and-drop node editor for command workflows (Automation → 🎨 VISUAL EDITOR, a workflow row's 🎨 CANVAS button, or this page's per-bot dashboard → Commands card → 🎨 VISUAL COMMAND BUILDER — whose moderation and custom command rows each carry an **EDIT** button that opens the same editor seeded from the command). The Discord specifics:

- **Slash Command triggers register as real `/` commands.** Every flow with a Slash Command trigger is auto-registered with Discord in **every guild the bot is in**; the registration is refreshed on reconnect and on each save. The bot must be online for the registration to land — flows saved while it is offline register on the next save/reconnect (KI-40).
- **Typed parameters become typed slash options.** The editor's COMMAND PARAMETERS entries register as real Discord option types — Text→String, Number→Integer, User/Channel/Role→their Discord pickers, Choice→Boolean, Attachment→Attachment — capped at Discord's 25 options per command, names normalized to Discord's identifier rules. Their values arrive in the flow as `{parameterName}` variables (also mirrored under the normalized name); User/Channel/Role options yield names, Choice yields `true`/`false`. A command with no parameters keeps the legacy single optional `args` text option. Prefix (`!`) commands never register options — they still pass everything as `{args}` (KI-44).
- **Slash invocations carry `{args}`** into the flow's variables (alongside the per-parameter variables above).
- **Buttons and menus are real components** — Send Message / Send Embed blocks with a Buttons & Menus section send Discord action rows (up to 5 buttons, one select menu with up to 25 options). On save, each wired sub-chain becomes a **component child workflow** (Category "Component", `vyralwf:{parentWorkflowId}:{nodeId}:btn{i}` / `:menu` custom-id key); a click dispatches by that key straight to the child flow, which sees `{customId}`, `{buttonId}` (the id tail, e.g. `…:btn0`) and `{values}`. The button ids are positional — reordering buttons renumbers them and breaks `{buttonId}` comparisons until re-edited (KI-46). Discord-only — other platforms get the plain text/embed.
- **Options are enforced first** — cooldown, moderator requirement, and channel restrictions are checked before the flow runs.
- **Embeds are native** — the embed builder's JSON is sent as real Discord embeds.

Building the flows themselves (palette, wires, the full block library, variables, execution) is documented in [Automation — Visual Flow Editor](../chat-features/automation.md#visual-flow-editor).

### Reconnecting and disconnecting

- On the next app launch, every bot that was connected auto-starts from its saved token — both the wizard path (the legacy `Platform` row) and the bots page path (the `DiscordBots` rows). A bot whose token is already being served by the legacy setup bot is skipped, so no token ever runs two gateway sessions at once.
- **RECONNECT** on the Platforms card re-opens the wizard at step 3 (invite & connect) using the stored token; **RECONNECT** on a per-bot dashboard re-establishes that bot's gateway session (reading the shared legacy token — see the caveat above).
- **DISCONNECT** (Platforms card) stops the setup bot and clears the stored token, username, and application ID from the `Platform` row; the row stays in the database with `Status="Disconnected"`. **STOP** on a bot card or dashboard stops that bot and marks its `DiscordBots` row `Status="Disconnected"`; its token and channel are kept, so START brings it straight back.
- Disconnecting a bot is graceful: the gateway event handlers are unhooked first, then the client is stopped and logged out, and the fully-stopped client is parked for later disposal — it is never disposed while live (this ordering fixed a hard crash; see [KI-35](../reference/known-issues.md), resolved 2026-08-16). Disconnect-all and profile switching stop every running bot (`StopAllAsync`).

## How It Works (Developer)

### The service layer

`DiscordService` ([DiscordService.cs](../../src/VyralBot.Integrations/Discord/DiscordService.cs), a DI singleton registered at `App.xaml.cs:165`) wraps a Discord.Net 3.20.1 `DiscordSocketClient`. The client is created with exactly four gateway intents:

```csharp
GatewayIntents = GatewayIntents.Guilds
               | GatewayIntents.GuildMessages
               | GatewayIntents.MessageContent
               | GatewayIntents.DirectMessages
```

Each instance carries a `BotId` (DiscordService.cs:52) — `"primary"` for the setup wizard's singleton, or the bot's `DiscordBots` row id assigned by the manager right after construction — published on every `DiscordChatMessageEvent` so replies can route back to the receiving bot.

`ValidateTokenAsync` does a REST-only login (no gateway session) to check a token before committing. `ConnectAsync` loads (or creates) the `Platform` row with `Name="Discord"`, restores the saved channel selection from `Platform.ChannelId`, logs in, starts the gateway, waits up to 30 seconds for `Ready` (a timeout is logged but not fatal — Discord.Net keeps the session alive), snapshots the guild list, and persists `AccessToken` (the bot token), `Username`/`UserId` (the bot account), and `Status="Connected"`. On failure the row is written as `Status="Error"` with `LastError`. Discord.Net auto-reconnects on drops; the `Disconnected` handler only surfaces the drop and flips `IsConnected` until the reconnect lands.

`DisconnectAsync` is deliberately staged (DiscordService.cs:268-345): unhook **all** gateway event handlers first (so the manual stop/logout cannot reenter them), then best-effort `StopAsync` + `LogoutAsync`, then *park* the fully stopped client in `_stoppedClient` — it is disposed lazily by the next `ConnectAsync` or by `Dispose`, never while live. Disposing a live client mid-drain is what used to crash the app (KI-35, resolved 2026-08-16). `ReconnectAsync` (DiscordService.cs:348) loads the token from the shared `Platform` row, disconnects if connected, and reconnects — which is why the per-bot RECONNECT button is only token-correct for the bot the legacy row describes.

All state (`IsConnected`, `BotUsername`, `SelectedChannelId`, `ConnectedGuilds`) is served from a lock-guarded snapshot; `ConnectedGuilds` is refreshed on `GuildAvailable`/`JoinedGuild`/`LeftGuild` and sorted by name, with each guild's text channels sorted by name.

### The bot manager

[DiscordBotManager.cs](../../src/VyralBot.Integrations/Discord/DiscordBotManager.cs) (DI singleton, `App.xaml.cs:167`) owns N independent `DiscordService` instances, one per row in the `DiscordBots` table — each created with its own logger/data/event-bus and connected with that row's explicitly passed token. All start/stop work is serialized through a semaphore; the `_running` dictionary is lock-guarded.

- **CRUD** — `LoadBotsAsync` / `AddBotAsync` (name and token required, name unique) / `UpdateBotAsync` (null/empty token or channel keeps the stored value; a token change live-restarts a running bot; a channel change is applied to the running instance) / `RemoveBotAsync` (stop, then delete the row). Every mutation raises `BotsChanged` (defensively — one throwing subscriber cannot break the others; handlers must dispatcher-hop to the UI thread).
- **Legacy migration** — `LoadBotsAsync` on an empty table migrates the legacy `Platform` row (token, username, channel, status) into a bot named **"Primary"**.
- **Duplicate-token guards** — `StartBotAsync` refuses to start a second gateway session for a token another running bot already uses (two sessions would duplicate every incoming event), and `StartAllSavedAsync` additionally skips tokens the legacy setup bot (the DI-singleton service auto-connected from the `Platform` row) already serves.
- **Lifecycle** — `StartAllSavedAsync` starts every bot whose saved status is Connected; `MainViewModel` calls it right after profile load (MainViewModel.cs:298), and `StopAllAsync` runs on disconnect-all and profile switches (MainViewModel.cs:675). `Connected` snapshots the connected services; `Primary` is the first connected one — the V1 target for sends that have no per-bot origin.
- **Legacy row note** — each managed `DiscordService` still persists the shared `Platform` row on connect/disconnect (last writer wins); the manager intentionally ignores that row in favor of the `DiscordBots` table as the source of truth for per-bot tokens/status/channels.

### Message intake and permission mapping

`OnMessageReceivedAsync` (DiscordService.cs:670) drops three categories of traffic: the bot's own messages (`Author.Id == CurrentUser.Id`), and anything whose `Message.Source != MessageSource.User` — which covers system messages, other bots, and webhooks. Everything else is mapped:

```csharp
var isModerator = permissions.Has(GuildPermission.ManageMessages);
var isBroadcaster = guildUser.Id == guildUser.Guild.OwnerId
                    || permissions.Has(GuildPermission.Administrator);
```

Each message is then published **twice**, fire-and-forget like the other integrations:

1. `DiscordChatMessageEvent` — the Discord-shaped payload (guild name, channel name/ID, `MessageId` as `"discord:{snowflake}"`, and this instance's `BotId`) consumed by the chat UI.
2. `ChatMessageReceivedEvent` with `Platform="Discord"` — the platform-agnostic event that drives the full command pipeline (commands → currency → giveaways → polls → watchtime). It carries **no** bot id; the orchestrator recovers the origin by message id (below). `IsSubscriber`/`IsVip` are always `false`; Discord has no equivalent in the chat event.

State changes publish `PlatformConnectionChangedEvent` (`Source="DiscordService"`, with `BotUsername` and the selected channel). The full flow:

```
Discord gateway (WebSocket) ──► DiscordService.OnMessageReceivedAsync   (one service per bot)
                                   ├─ skip: own messages, other bots, webhooks
                                   ├─ permission map: ManageMessages→mod / owner|Admin→broadcaster
                                   ├─► DiscordChatMessageEvent (BotId) ──► ChatViewModel (feed, history, badges)
                                   └─► ChatMessageReceivedEvent (Platform="Discord")
                                          └─► command pipeline (origin bot resolved by MessageId)
                                                 └─► reply via the origin bot's SendMessageAsync
                                                        (sent to that bot's selected channel)
```

### The wizard

[DiscordWebSetupViewModel.cs](../../src/VyralBot.UI/ViewModels/DiscordWebSetupViewModel.cs) is deliberately *not* built on `BaseOAuthSetupViewModel` — Discord has no OAuth code flow here. It keeps the same step/modal/WebView2 surface. The wizard opens as its **own full-window page** (not a panel inside the Platforms page): clicking the Discord card navigates to it via `NavigateToDiscordSetupCommand`, and finishing returns to the Platforms page. Its step panels deliberately use plain Grids without ScrollViewers — nested step ScrollViewers desynchronized input hit-testing from layout on scaled displays, making buttons unclickable (resolved 2026-08-16). Two derivations do the heavy lifting:

- `TryGetApplicationIdFromToken` — a Discord bot token's first `.`-separated segment is the base64url-encoded application ID, so the invite link can be built without a second portal visit.
- `BuildInviteUrlFromToken` — `https://discord.com/oauth2/authorize?client_id={id}&scope=bot&permissions=8` (Administrator = 8, per the owner's preference).

`ConnectBotAsync` runs `ValidateTokenAsync` then `ConnectAsync`; `CompleteSetupAsync` refuses to finish unless `_tokenValidated` is true — the `Status="Connected"` row is only ever written from a real, validated connection. The wizard drives the **setup bot** (the DI-singleton `DiscordService` and its legacy `Platform` row); it does not create or edit `DiscordBots` rows — Quick Add on the bots page is the per-bot path (KI-36).

### The bots pages

[DiscordBotsViewModel.cs](../../src/VyralBot.UI/ViewModels/DiscordBotsViewModel.cs) (DI singleton) loads the bot list through the manager, surfaces Quick Add / start / stop / rename / remove commands, and re-refreshes on every `BotsChanged`. **RUN FULL WIZARD** invokes a callback that MainViewModel wires to `NavigateToDiscordSetupCommand`. **OPEN** calls back into `NavigateToDiscordBotDetailAsync` (MainViewModel.cs:1807), which scope-resolves [DiscordBotDetailViewModel.cs](../../src/VyralBot.UI/ViewModels/DiscordBotDetailViewModel.cs) for the selected bot: it renders the manager's live service state, START/STOP/RECONNECT, the guild/channel snapshot with **SET ACTIVE CHANNEL** (`SetSelectedChannelAsync`, persisted per bot), and the owner-ping test (`SendOwnerTestMessageAsync` reusing each guild's private `#vyral-test` channel).

The previous single-bot dashboard ([DiscordDashboardViewModel.cs](../../src/VyralBot.UI/ViewModels/DiscordDashboardViewModel.cs)) is superseded by the per-bot dashboard — its navigation command is generated but bound nowhere, making it an orphaned surface (see [UI Reference](../reference/ui-reference.md) and KI-36).

### Shell and chat wiring

- **Auto-connect**: `MainViewModel.CheckPlatformConnectionsAsync` reads the Discord row; `IsDiscordConnected` requires `Status == "Connected"` *and* a non-empty token, and `LoadAndConnectDiscordAsync` connects the setup bot from the stored token. Afterwards `StartAllSavedAsync` brings up the managed bots (MainViewModel.cs:298). `DisconnectAllPlatforms` includes Discord and stops every managed bot (MainViewModel.cs:675).
- **Sidebar**: the Platforms-group Discord entry (`MainWindow.xaml:221-231`, logo `discord-logo.png`) is **always visible** and invokes `NavigateToDiscordBotsCommand`, which sets the window title to "Vyral - Discord Bots".
- **Platforms page**: `PlatformsViewModel` treats Discord as a first-class platform — `SelectDiscordAsync`, `LoadDiscordPlatformAsync` (auto-connect + status refresh after the wizard completes), `ReconnectDiscordAsync` (wizard at step 3), and `DisconnectDiscord` (keeps the row, nulls the token and identity fields).
- **Chat**: `ChatViewModel` subscribes to `DiscordChatMessageEvent` (ChatViewModel.cs:276). The handler (ChatViewModel.cs:2536) first records the originating bot — `RememberDiscordBotOrigin(evt.MessageId, evt.BotId)` into a bounded message-id→bot-id map (ChatViewModel.cs:648, oldest entries evicted) — then skips the bot's own account, applies `DiscordFilterEnabled`, persists the message through `DataChatMessageStore`, and appends a `ChatMessage` with blurple color and synthesized moderator/broadcaster badges. When the generic `ChatMessageReceivedEvent` arrives for a Discord message, `ResolveDiscordBotOrigin` (ChatViewModel.cs:685) looks the bot id up by message id and passes it both to `CommandService.ExecuteAsync` (per-bot command filtering) and to `SendDiscordReplyAsync`, which resolves the send target as: the originating bot's manager instance → the wizard setup bot for the `"primary"` tag → the first connected manager bot, else the setup bot (ChatViewModel.cs:592-624). Because the typed and generic events are published concurrently, the generic handler can rarely lose that race and falls back to Primary — a safe degradation (KI-38). Manual sends and send-to-all always use `DiscordSendTarget` (Primary, else the setup bot); `BuildContextVariables` supplies `bot-username` and `channel` for Discord sends.

## Files & Code

| File | Role |
|---|---|
| [DiscordService.cs](../../src/VyralBot.Integrations/Discord/DiscordService.cs) | The gateway client: connect/validate/graceful disconnect (unhook → stop → logout → park), reconnect, guild snapshot, message intake with `BotId` tagging, dual event publish (899 lines) |
| [IDiscordService.cs](../../src/VyralBot.Integrations/Discord/IDiscordService.cs) | Service contract: `BotId`, `IsConnected`, `BotUsername`, `SelectedChannelId`, `ConnectedGuilds`, validate/connect/disconnect/reconnect/set-channel/send |
| [IDiscordBotManager.cs](../../src/VyralBot.Integrations/Discord/IDiscordBotManager.cs) | Multi-bot manager contract: per-bot CRUD/start/stop, `StartAllSavedAsync`, `Connected`, `Primary`, `BotsChanged` |
| [DiscordBotManager.cs](../../src/VyralBot.Integrations/Discord/DiscordBotManager.cs) | Default manager: N `DiscordService` instances, legacy "Primary" migration, duplicate-token guards, status persistence (671 lines) |
| [DiscordBotConfig.cs](../../src/VyralBot.Integrations/Discord/DiscordBotConfig.cs) | `record` mirroring a `DiscordBots` row (Id, Name, Token, Status, SelectedChannelId) |
| [DiscordChatMessageEvent.cs](../../src/VyralBot.Integrations/Discord/Events/DiscordChatMessageEvent.cs) | Typed event: `GuildName`, `Channel`/`ChannelId`, flags, `MessageId`, `BotId` |
| [DiscordGuildInfo.cs](../../src/VyralBot.Integrations/Discord/Models/DiscordGuildInfo.cs) | `DiscordGuildInfo` + `DiscordChannelInfo` snapshot records |
| [VyralBot.Integrations.csproj](../../src/VyralBot.Integrations/VyralBot.Integrations.csproj) | References Discord.Net 3.20.1 |
| [DiscordWebSetupViewModel.cs](../../src/VyralBot.UI/ViewModels/DiscordWebSetupViewModel.cs) | The bot-token wizard (setup bot): portal/token/invite/test steps, application-ID derivation, invite URL, validated-finish guard |
| [DiscordWebSetupView.xaml](../../src/VyralBot.UI/Views/DiscordWebSetupView.xaml) | Wizard UI (942 lines) |
| [DiscordBotsViewModel.cs](../../src/VyralBot.UI/ViewModels/DiscordBotsViewModel.cs) | Bots page: Quick Add, per-bot start/stop/rename/remove, `BotsChanged` refresh, wizard/detail callbacks |
| [DiscordBotsView.xaml](../../src/VyralBot.UI/Views/DiscordBotsView.xaml) | Bots page layout: Quick Add card, per-bot cards (395 lines) |
| [DiscordBotDetailViewModel.cs](../../src/VyralBot.UI/ViewModels/DiscordBotDetailViewModel.cs) | Per-bot dashboard: start/stop/reconnect, guild/channel snapshot, per-bot active channel, owner-ping test |
| [DiscordBotDetailView.xaml](../../src/VyralBot.UI/Views/DiscordBotDetailView.xaml) | Per-bot dashboard layout (484 lines) |
| [WorkflowDiscordBridge.cs](../../src/VyralBot.UI/Services/WorkflowDiscordBridge.cs) | Workflow bridge: slash-command registration + role/channel name lookups for the flow editor |
| [DiscordDashboardViewModel.cs](../../src/VyralBot.UI/ViewModels/DiscordDashboardViewModel.cs) | Legacy single-bot dashboard — superseded, navigation command bound nowhere (KI-36) |
| [ChatViewModel.cs](../../src/VyralBot.UI/ViewModels/ChatViewModel.cs) | Discord feed handler (:2536), origin map (:648, :685), send-target resolution (:592-624), echo suppression |
| [DataChatMessageStore.cs](../../src/VyralBot.UI/Services/Chat/DataChatMessageStore.cs) | Persists Discord chat history rows (maps `DiscordChatMessageEvent` both directions) |
| [PlatformsViewModel.cs](../../src/VyralBot.UI/ViewModels/PlatformsViewModel.cs) | Discord card: connect/reconnect/disconnect, post-wizard auto-connect |
| [MainViewModel.cs](../../src/VyralBot.UI/ViewModels/MainViewModel.cs) | Startup auto-connect, `StartAllSavedAsync` (:298), `StopAllAsync` on disconnect-all (:675), bots-page navigation (:1763, :1807) |
| [MainWindow.xaml](../../src/VyralBot.UI/MainWindow.xaml) | Sidebar Discord entry, always visible (:221-231) + bots/detail DataTemplates (:825-833) |
| [App.xaml.cs](../../src/VyralBot.UI/App.xaml.cs) | DI: `IDiscordService` singleton (:165), `IDiscordBotManager` singleton (:167), bots VMs (:208-209) |
| [Platform.cs](../../src/VyralBot.Data/Entities/Platform.cs) | Entity carrying the setup bot's token, selected channel, and flags |
| [IDataService.cs](../../src/VyralBot.Data/IDataService.cs) | `DiscordBot` entity + `DiscordBots` CRUD contract |

The per-bot scope check in the command engine (CommandService.cs:84-96) — a command pinned to one bot never fires for another; null means all bots:

```csharp
// Per-bot scope (Discord only): a command pinned to one bot via its
// BotId never fires for a different bot; commands with a null BotId
// are "all bots" and always pass.
var effectiveBotId = discordBotId ?? context.DiscordBotId;
if (!string.IsNullOrEmpty(effectiveBotId)
    && !string.IsNullOrEmpty(command.BotId)
    && !string.Equals(command.BotId, effectiveBotId, StringComparison.Ordinal))
{
    return null;
}
```

And the reply-target resolution that routes each response through the bot that received the message (ChatViewModel.cs:602-624):

```csharp
// Managed bot instance that published the event (managed bots carry
// their DiscordBots row id as BotId).
if (!string.IsNullOrWhiteSpace(botId) && !string.Equals(botId, "primary", ...))
{
    var byId = _discordBotManager.Get(botId);
    if (byId != null && byId.IsConnected)
        return byId;
}
// Wizard setup bot: its events carry the default "primary" tag.
if (string.Equals(botId, "primary", ...) && _discordService.IsConnected)
    return _discordService;
// Origin unknown, disconnected, or untagged: first connected manager
// bot, else the setup bot.
return DiscordSendTarget;
```

The finish guard that keeps "Connected" honest (DiscordWebSetupViewModel.cs:427):

```csharp
// Guard: never persist "Connected" unless a token was actually entered and validated.
if (string.IsNullOrWhiteSpace(_botToken) || !_tokenValidated)
{
    TestMessageStatus = "A validated bot token is required. Go back to Step 3 and connect the bot first.";
    return;
}
```

And the application-ID derivation that makes the invite link possible without another portal round-trip (DiscordWebSetupViewModel.cs:111):

```csharp
var firstSegment = botToken.Trim().Split('.')[0];

// Base64url -> Base64, then fix up padding
var base64 = firstSegment.Replace('-', '+').Replace('_', '/');
base64 = base64.PadRight(base64.Length + ((4 - base64.Length % 4) % 4), '=');

var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(base64));
return ulong.TryParse(decoded, out _) ? decoded : null;
```

## Built-in moderation commands

Every bot ships with moderation commands, usable two ways:

- **From the app's chat box** with the `/` prefix (Discord-scoped; the result appears as a system message in Vyral — nothing is posted to Discord): `/kick <@user|id> [reason]`, `/ban <@user|id> [reason]`, `/unban <id>`, `/mute <@user|id> <minutes>`, `/unmute <@user|id>`, `/slowmode <seconds>` (applies to the bot's active channel; `/slowmode <#channel|id> <seconds>` for another), `/purge <1-100>` (alias `/clear`), `/help`.
- **Inside Discord** by server moderators/owner with the `!` prefix (`!kick @user spam` etc.) — the bot replies in that channel. Regular chatters get "You don't have permission to use moderation commands."

They can also be **edited as flows**: the **EDIT** buttons on this page's Commands card (and the Commands page's Built-in tab) open the visual editor on a flow converted from the command — a 🔨 Moderate Member block plus a confirmation message — and saving adopts the command (the flow answers instead; see [Commands — From command to flow](../chat-features/commands.md#from-command-to-flow)). Converted timeout/slowmode/purge flows use fixed defaults rather than `{args}` (KI-41).

The commands execute through the bot that received the message (origin routing); app-issued `/` commands run on the primary connected bot. Mute uses Discord's native timeout (1–40320 minutes); slowmode 0–21600 seconds (0 disables); purge bulk-deletes up to 100 messages. The `!` cross-platform commands (giveaways, polls, currency) are unaffected.

## Integrations (external bots) &amp; auto-detect

Each bot dashboard has an **Integrations** card: register external bot services (BotGhost, MEE6, Dyno, Carl-bot, or custom), open their dashboards to sign in, and list the commands they run. Vyral flags conflicts with its own commands for that bot — custom commands can be disabled/re-enabled with one click, and built-ins can be registered as disabled (Vyral then declines them at runtime so the external service owns them).

**Auto-detect**: bots that share a server with your Vyral bot appear automatically (detected from the Discord message stream). Pick one, set its help command (default `!help`), and Vyral sends it, captures the reply, and parses the command names — including Discord slash-command mentions — straight into the integration's command list.

## Data & Settings

Two stores side by side (both per-profile — see [Database Reference](../reference/database-reference.md)):

**The legacy `Platform` row** (`Name="Discord"`) — the setup wizard's bot:

| Field | Discord meaning |
|---|---|
| `AccessToken` | The setup bot's token (plaintext, like Kick/YouTube — not DPAPI-encrypted like Twitch) |
| `ChannelId` | Snowflake ID of the setup bot's selected active channel |
| `Username` / `UserId` | The setup bot account's name and ID |
| `ClientId` | The application ID derived from the token |
| `Status` | "Connected" / "Disconnected" / "Error" |
| `DiscordChatEnabled` | Show/hide Discord messages in the unified feed (Chat Hub toggle + quick-settings popup, via `PlatformChatRoutingChangedEvent`) |

**The `DiscordBots` table** — one row per managed bot (`Id`, `Name`, `Token`, `Status`, `SelectedChannelId`), written by the bot manager's CRUD and status updates; the "Primary" migration copies the legacy row's fields here on first load. Managed services still write the legacy `Platform` row on connect/disconnect (last writer wins) — harmless, and intentionally ignored by the manager.

Custom commands carry an optional `Commands.BotId` — the `DiscordBots` row id a command is pinned to; null runs on every bot (see [Commands](../chat-features/commands.md)).

Chat history rows are written to the `ChatMessages` table with `Platform="Discord"` (channel name as the store key; badges synthesized from the moderator/broadcaster flags). Nothing else is stored — guild and channel lists are always live snapshots from the gateway, never persisted.

## Status & Known Issues

**Works**: the full bot-token wizard (portal → token → invite → validated connect), gateway connections with auto-reconnect for any number of bots, Quick Add / start / stop / rename / remove on the bots page, per-bot dashboards (status, servers list, per-bot active-channel picker, owner-ping test), startup auto-start of saved bots with duplicate-token protection, the legacy "Primary" migration, messages from every bot in the unified feed with badges and blurple, per-origin reply routing with Primary fallback, per-bot command scoping, manual sends and send-to-all via Primary, chat history persistence, and the show/hide feed toggle.

Current limitations, tracked in the [known issues ledger](../reference/known-issues.md):

- **The wizard still configures the setup bot only** — the four-step wizard writes the legacy `Platform` row; there is no per-bot wizard, and the per-bot RECONNECT button reads that shared token. Quick Add is the per-bot path (KI-36; also covers the orphaned legacy dashboard view).
- **`!commands` does not bot-filter** — the built-in command list ignores bot scope, so it can advertise a command that another bot would answer (KI-37).
- **Origin-race fallback** — very rarely, the generic command event beats the typed Discord event and the reply routes through Primary instead of the receiving bot (safe degradation, KI-38).
- **Message Content intent is mandatory per bot** — if it is not enabled on an application's Bot tab, that bot receives no message content. This is a Discord policy, not an app bug; the wizard's step 2 calls it out (Quick Add does not — remember to enable it for every bot you add there) (KI-34).
- **One active channel per bot** — each bot listens everywhere but sends only to its own single selected channel. Guild-wide listening plus per-guild reply routing is future work (KI-34, updated from app-wide to per-bot by multi-bot).
- **No Discord-native slash commands** — the app's `!`-prefix commands work; `/`-commands are not registered by the command system (KI-34). Workflow flows with a Slash Command trigger *do* register real `/` commands, typed parameters included — see [Visual command builder](#visual-command-builder-workflows) (online requirement: KI-40; prefix commands stay `{args}`-only: KI-44).
- **Workflow components are Discord-only** — buttons/menus attached by flow messages and the music/voice/premium BotGhost block suite (Create Music Player, voice-channel actions, IFTTT, transcripts, Components V2, Premium Check) do not exist here; the typed "Music Smart Search" parameter is a plain text parameter (KI-45).
- **No non-chat Discord events** — member joins, boosts, and similar are not ingested, so there are no Discord event reactions or auto-replies (KI-34).
- **History rows are written but not reloaded** — the Chat Window's Load Chat History button currently loads only Kick/Twitch/YouTube; Discord rows persist in the database but are not yet merged back into the feed (KI-34).

The disconnect crash that preceded the graceful-stop work is resolved (KI-35, 2026-08-16). See the [known issues ledger](../reference/known-issues.md) for cross-app defects.
