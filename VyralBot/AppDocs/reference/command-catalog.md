# Command Catalog

> **The chat command pipeline is live.** Every command in this catalog executes from real chat: the per-platform chat services (Twitch IRC, Kick Pusher, YouTube polling, Discord gateway) each publish a `ChatMessageReceivedEvent` onto the shared EventBus, and `ChatViewModel.OnChatMessageReceivedAsync` runs the full routing chain described below — with per-message dedupe (Kick Pusher + webhook double delivery is safe) and a bot-echo skip so the bot does not answer its own messages. Resolved as [Known Issues Ledger](known-issues.md) entry **KI-1** on 2026-08-15.

This page is the exhaustive catalog of every chat command the app knows how to handle. Each table lists the command, its arguments, its permission gate, what it actually does, and the source file that implements it. Everything documented here is real, implemented code that runs when a viewer types it in a connected chat.

Related pages: [Commands](../chat-features/commands.md), [Currency & Rewards](../stream-tools/currency.md), [Giveaways](../stream-tools/giveaways.md), [Polls](../stream-tools/polls.md), [Watchtime](../stream-tools/watchtime.md).

---

## How commands are routed

When the pipeline is alive, every incoming chat message flows through `OnChatMessageReceivedAsync` in [ChatViewModel.cs](../../src/VyralBot.UI/ViewModels/ChatViewModel.cs), fed by the platform-agnostic [ChatMessageReceivedEvent](../../src/VyralBot.Core/Models/ChatMessageReceivedEvent.cs):

1. Per-platform display filters are applied.
2. **Unified commands** (`CommandService`): if the message starts with `!`, `?`, or `.` (`CommandService.cs:23`), the message is looked up in the `Commands` table. If a row matches (and is enabled, platform-allowed, permission-passing, off-cooldown), its response is sent and processing **stops**.
3. **Currency commands**: any `!` message falls through to `CurrencyCommandService.ProcessCommandAsync` (`ChatViewModel.cs:3367`).
4. **Giveaway commands**: any `!` message falls through to `GiveawayCommandService.ProcessCommandAsync` (`ChatViewModel.cs:3428`) — includes custom entry keywords.
5. **Poll commands**: messages starting with `!poll` (or the `!vote` alias) go to `PollCommandService.ProcessCommandAsync` (`ChatViewModel.cs:3462`).
6. **Watchtime commands**: messages starting with `!watchtime` go to `WatchtimeCommandService.ProcessCommandAsync` (`ChatViewModel.cs:3496`).
7. **Quote commands**: messages starting with `!quote` go to `QuoteService.ProcessCommandAsync` (`ChatViewModel.cs:3530`).
8. **Song requests**: messages starting with `!sr`/`!songrequest` go to `SongRequestService.ProcessCommandAsync` (`ChatViewModel.cs:3619`).
9. **Mod & stream tools**: any `!` message falls through to `StreamToolsCommandService.ProcessCommandAsync` (`ChatViewModel.cs:3653`) — `!so`, `!clip`, `!raid`, `!title`, `!game`, `!commercial`, `!uptime`, `!followage`.
10. **Death counter**: messages starting with `!death` go to `DeathCounterService.ProcessCommandAsync` (`ChatViewModel.cs:3683`).
11. **TTS**: messages starting with `!tts` go to `TtsService.ProcessCommandAsync` (`ChatViewModel.cs:3717`).
12. **Chat modes**: any `!` message falls through to `ChatModeCommandService.ProcessCommandAsync` (`ChatViewModel.cs:3761`) — `!emoteonly`, `!subonly`, `!followers`, `!slow` (Twitch-only).
13. **Warnings**: any `!` message falls through to `WarningCommandService.ProcessCommandAsync` (`ChatViewModel.cs:3795`) — `!warn`, `!warnings`, `!clearwarn`.
14. Finally, watchtime activity is recorded and chat-activity currency is awarded (for non-command messages).

Notes that apply across the whole catalog:

- **Prefixes**: the unified command engine treats `!`, `?`, and `.` identically — `.socials` and `?socials` both resolve to `!socials`. The feature command services (currency/giveaway/poll/watchtime/stream tools/death counter/TTS) only recognize `!`.
- **Response delivery**: command responses are sent back to **Twitch, Kick, YouTube, and Discord** — each fallback branch switches on the event's platform and sends through that platform's service (Discord via `SendDiscordReplyAsync` through the **bot that received the message**, to that bot's active channel, picked on the [Discord bots page](../platforms/discord.md#multiple-bots), not necessarily the channel the command was typed in). Entries, votes, and balances are recorded for any platform; a platform with no send path (e.g. TikTok) has its reply silently dropped.
- **Per-bot scope (Discord only)**: a custom command's `Commands.BotId` pins it to one Discord bot; `null` (the default) means every bot answers it. Execution compares the stored `BotId` against the origin bot of the triggering message and silently skips mismatches (`CommandService.cs:175-183`). The tables below do not carry a scope column because scope is per-row state you set in the builder — see [Commands](../chat-features/commands.md#bot-scope). Twitch and Kick are never affected by bot scope, and neither are the built-ins' platform toggles.
- **Two permission behaviors**: `CommandService` silently ignores unauthorized and on-cooldown users (no feedback). The currency/giveaway/poll services reply with an explicit "You must be a moderator to use !x" / "Only moderators can..." message.
- **Cooldowns**: unified commands have per-command global and per-user cooldowns (stored in `CommandCooldowns`); the feature command services have none of their own (games have their own internal cooldowns via `CurrencyGameSettings`).
- **Command context**: `CommandContext.IsBroadcaster` is fed from the event's `IsBroadcaster` flag, so broadcaster-only commands fire for the channel owner on every platform (broadcaster = channel owner on Twitch/Kick, video owner on YouTube). `CommandContext.DiscordBotId` carries the origin bot for Discord messages (null elsewhere).

---

## Unified commands (CommandService)

Implemented in [CommandService.cs](../../src/VyralBot.Core/Services/Commands/CommandService.cs); definitions stored in the `Commands` table; managed from the Commands page ([Commands](../chat-features/commands.md)).

### Seeded built-ins (21 rows)

Seeded once into an empty database at [DapperDataService.cs](../../src/VyralBot.Data/DapperDataService.cs):4799-5158; a migration pass at `DapperDataService.cs:5198+` re-adds missing currency commands on existing databases.

The 5 meta commands have real handler code; the 16 currency rows are discoverability/enable flags only — their seeded `Response` text is help text, and actual behavior comes from `CurrencyCommandService` (next section).

| Command | Arguments | Permission | What it does | Source |
|---|---|---|---|---|
| `!commands` | none | Everyone | Lists up to 10 enabled commands the caller has permission for on that platform. Does not filter by bot scope — it can list a command pinned to another bot (KI-37). | [CommandService.cs](../../src/VyralBot.Core/Services/Commands/CommandService.cs) (`ExecuteCommandsListAsync`) |
| `!uptime` | none | Everyone | Replies "Stream uptime: Xh Ym (since HH:mm UTC)" computed from the `Stream.StartedAtUtc` config value (written by ChatViewModel whenever the Twitch stream status changes; cleared when offline). Works from any connected platform's chat — the data source is the Twitch live status, so without a Twitch connection it replies "Stream is offline or uptime not tracked yet." **This seeded row answers first** — the newer Helix-based `!uptime` in [Mod & stream tools](#mod--stream-tools-commands) below only answers when this built-in is disabled or deleted on the Commands page (KI-59). | [CommandService.cs](../../src/VyralBot.Core/Services/Commands/CommandService.cs) (`ExecuteUptimeAsync` :447) |
| `!socials` | none | Everyone | Returns the stored response template, which resolves `{twitter}`, `{discord}`, `{youtube}` from the Variables table. | [CommandService.cs](../../src/VyralBot.Core/Services/Commands/CommandService.cs) |
| `!discord` | none | Everyone | Returns the stored template resolving `{discord}`. | [CommandService.cs](../../src/VyralBot.Core/Services/Commands/CommandService.cs) |
| `!watchtime` | none | Everyone | Seed row for discoverability; the built-in handler deliberately defers (`CommandService.cs:355` returns null) so the real `WatchtimeCommandService` below answers instead. | [DapperDataService.cs](../../src/VyralBot.Data/DapperDataService.cs) (seed) |
| `!points` | `[@user]` | Everyone | Seeded help row; real behavior in the currency table below. | [DapperDataService.cs](../../src/VyralBot.Data/DapperDataService.cs) (seed) |
| `!leaderboard` | `[n]` | Everyone | Seeded help row. | [DapperDataService.cs](../../src/VyralBot.Data/DapperDataService.cs) (seed) |
| `!gamble` | `<amount\|all>` | Everyone | Seeded help row. | [DapperDataService.cs](../../src/VyralBot.Data/DapperDataService.cs) (seed) |
| `!redeem` | `<reward> [input]` | Everyone | Seeded help row. | [DapperDataService.cs](../../src/VyralBot.Data/DapperDataService.cs) (seed) |
| `!rewards` | none | Everyone | Seeded help row. | [DapperDataService.cs](../../src/VyralBot.Data/DapperDataService.cs) (seed) |
| `!daily` | none | Everyone | Seeded help row. | [DapperDataService.cs](../../src/VyralBot.Data/DapperDataService.cs) (seed) |
| `!rank` | none | Everyone | Seeded help row. | [DapperDataService.cs](../../src/VyralBot.Data/DapperDataService.cs) (seed) |
| `!store` | `[page\|category]` | Everyone | Seeded help row. | [DapperDataService.cs](../../src/VyralBot.Data/DapperDataService.cs) (seed) |
| `!slots` | `<amount>` | Everyone | Seeded help row. | [DapperDataService.cs](../../src/VyralBot.Data/DapperDataService.cs) (seed) |
| `!duel` | `@user <amount>` | Everyone | Seeded help row. | [DapperDataService.cs](../../src/VyralBot.Data/DapperDataService.cs) (seed) |
| `!accept` | none | Everyone | Seeded help row. | [DapperDataService.cs](../../src/VyralBot.Data/DapperDataService.cs) (seed) |
| `!heist` | `<amount>` | Everyone | Seeded help row. | [DapperDataService.cs](../../src/VyralBot.Data/DapperDataService.cs) (seed) |
| `!roulette` | `<amount> <red\|black\|number>` | Everyone | Seeded help row. | [DapperDataService.cs](../../src/VyralBot.Data/DapperDataService.cs) (seed) |
| `!link` | `<platform> [code]` | Everyone | Seeded help row. | [DapperDataService.cs](../../src/VyralBot.Data/DapperDataService.cs) (seed) |
| `!give` | `@user <amount>` | Moderator | Seeded help row. | [DapperDataService.cs](../../src/VyralBot.Data/DapperDataService.cs) (seed) |
| `!take` | `@user <amount>` | Moderator | Seeded help row. | [DapperDataService.cs](../../src/VyralBot.Data/DapperDataService.cs) (seed) |

### Custom commands (user-defined)

Created in the [visual flow editor](../chat-features/commands.md#from-command-to-flow) (Commands → NEW COMMAND; **SAVE** adopts the command into a flow). Any name starting with `!`, `?`, or `.` (name is lower-cased on save); cannot collide with a built-in. The response is a template supporting all [variables](variables-catalog.md), plus these context variables supplied by the engine: `{username}`, `{platform}`, `{channel}`, `{balance}`, `{role}` (`CommandService.cs:525+`, `ParseVariables`). Each row can optionally be pinned to one Discord bot via its `BotId` ("This bot only" in the bot scope bar) — null runs on every bot.

| Command | Arguments | Permission | What it does | Source |
|---|---|---|---|---|
| `!<name>` (any) | whatever the response expects | Everyone / Subscriber / VIP / Moderator / Broadcaster | Returns the stored response template with variables resolved. Permission hierarchy: each level passes for equal-or-higher (a Subscriber-level command passes for VIPs, mods, broadcaster). Unauthorized and on-cooldown uses are silently ignored. If pinned to a bot, other bots skip it silently. | [CommandService.cs](../../src/VyralBot.Core/Services/Commands/CommandService.cs) |

---

## Currency commands

Implemented in [CurrencyCommandService.cs](../../src/VyralBot.Core/Services/Currency/CurrencyCommandService.cs) — the alias switch is at `CurrencyCommandService.cs:67-98`. 28 handlers, 36 recognized names. Any `!` message reaches this service (fallback after the unified engine). Feature page: [Currency & Rewards](../stream-tools/currency.md).

Moderator-gated commands reply "You must be a moderator to use !x" when denied (visible, unlike the unified engine's silence).

### Balances and leaderboard

| Command | Arguments | Permission | What it does | Source |
|---|---|---|---|---|
| `!points` / `!balance` / `!currency` | none, or `@user` | Everyone | Shows the caller's balance (or the named user's) in the configured currency name. | [CurrencyCommandService.cs](../../src/VyralBot.Core/Services/Currency/CurrencyCommandService.cs) |
| `!leaderboard` / `!top` | `[n]` (default 5, max 10) | Everyone | Top balances with medal markers. | [CurrencyCommandService.cs](../../src/VyralBot.Core/Services/Currency/CurrencyCommandService.cs) |
| `!rank` / `!tier` | none | Everyone | Loyalty tier (Newcomer through Legend), earn multiplier, and next-tier progress. | [CurrencyCommandService.cs](../../src/VyralBot.Core/Services/Currency/CurrencyCommandService.cs) |

### Moderation

| Command | Arguments | Permission | What it does | Source |
|---|---|---|---|---|
| `!give` | `@user <amount>` | Moderator | Adds currency to a user (reason "Gift"). | [CurrencyCommandService.cs](../../src/VyralBot.Core/Services/Currency/CurrencyCommandService.cs) |
| `!take` | `@user <amount>` | Moderator | Removes currency from a user (reason "Penalty"). | [CurrencyCommandService.cs](../../src/VyralBot.Core/Services/Currency/CurrencyCommandService.cs) |
| `!boost` | `<multiplier> <minutes> [reason]` | Moderator | Starts a timed global/platform earn multiplier (1.0x-10.0x, up to 1440 minutes). | [CurrencyCommandService.cs](../../src/VyralBot.Core/Services/Currency/CurrencyCommandService.cs) |
| `!auction` | `start <item> <startbid> <minutes>` / `end` / `cancel` / `info` | Moderator | Runs a currency auction: create with starting bid and duration, resolve (highest bid wins and pays), cancel (refunds bidders), or show current state. | [CurrencyCommandService.cs](../../src/VyralBot.Core/Services/Currency/CurrencyCommandService.cs) |

### Rewards store

| Command | Arguments | Permission | What it does | Source |
|---|---|---|---|---|
| `!rewards` | none | Everyone | Lists the first 5 enabled rewards with costs and cooldowns. | [CurrencyCommandService.cs](../../src/VyralBot.Core/Services/Currency/CurrencyCommandService.cs) |
| `!redeem` | `<reward name> [input text]` | Everyone | Redeems a reward (case-insensitive name match): checks cost, cooldown, limits, approval; deducts currency; queues for approval when required. | [CurrencyCommandService.cs](../../src/VyralBot.Core/Services/Currency/CurrencyCommandService.cs) |
| `!store` / `!shop` | `[page]` or `[category] [page]` | Everyone | Paged store listing (5 per page) with rarity badges for tiered rewards. | [CurrencyCommandService.cs](../../src/VyralBot.Core/Services/Currency/CurrencyCommandService.cs) |

### Games

| Command | Arguments | Permission | What it does | Source |
|---|---|---|---|---|
| `!gamble` / `!bet` | `<amount\|all>` | Everyone | 50/50 double-or-nothing coin flip; minimum bet 10. (Simple inline implementation — distinct from the configured house-edge games below.) | [CurrencyCommandService.cs](../../src/VyralBot.Core/Services/Currency/CurrencyCommandService.cs) |
| `!slots` | `<amount>` | Everyone | Slot machine spin (min/max bet and cooldown from `CurrencyGameSettings`). | [CurrencyCommandService.cs](../../src/VyralBot.Core/Services/Currency/CurrencyCommandService.cs) via [CurrencyGameService.cs](../../src/VyralBot.Core/Services/Currency/CurrencyGameService.cs) |
| `!duel` | `@user <amount>` | Everyone | Challenges another user to a duel for the staked amount. | [CurrencyCommandService.cs](../../src/VyralBot.Core/Services/Currency/CurrencyCommandService.cs) via [CurrencyGameService.cs](../../src/VyralBot.Core/Services/Currency/CurrencyGameService.cs) |
| `!accept` | none | Everyone | Accepts a pending duel challenge; resolves the duel. | [CurrencyCommandService.cs](../../src/VyralBot.Core/Services/Currency/CurrencyCommandService.cs) via [CurrencyGameService.cs](../../src/VyralBot.Core/Services/Currency/CurrencyGameService.cs) |
| `!heist` | `<amount>` | Everyone | Starts or joins a group heist; self-resolves automatically 60 seconds after the first join (race-safe; payouts use the original-case platform name). | [CurrencyCommandService.cs](../../src/VyralBot.Core/Services/Currency/CurrencyCommandService.cs) via [CurrencyGameService.cs](../../src/VyralBot.Core/Services/Currency/CurrencyGameService.cs) |
| `!roulette` | `<amount> <red\|black\|number>` | Everyone | Roulette bet on color or number. | [CurrencyCommandService.cs](../../src/VyralBot.Core/Services/Currency/CurrencyCommandService.cs) via [CurrencyGameService.cs](../../src/VyralBot.Core/Services/Currency/CurrencyGameService.cs) |

### Earning, banking, linking

| Command | Arguments | Permission | What it does | Source |
|---|---|---|---|---|
| `!daily` | none | Everyone | Claims the daily bonus with streak multipliers (day 1/5/10 escalation). | [CurrencyCommandService.cs](../../src/VyralBot.Core/Services/Currency/CurrencyCommandService.cs) |
| `!season` | none | Everyone | Live [loyalty season](../stream-tools/currency.md#seasons) status: "Season: {name} — day {n}. Previous winner: {user}." ("No season is running yet — ask the streamer to start one!" before the first). | [CurrencyCommandService.cs](../../src/VyralBot.Core/Services/Currency/CurrencyCommandService.cs) via [SeasonService.cs](../../src/VyralBot.Core/Services/Currency/SeasonService.cs) |
| `!link` | `<twitch\|kick\|youtube> [code]` | Everyone | Cross-platform account linking: without a code, initiates a link and returns a 6-digit code with the 5-minute verification window; with a code, verifies and unifies balances. | [CurrencyCommandService.cs](../../src/VyralBot.Core/Services/Currency/CurrencyCommandService.cs) via [LinkedAccountService.cs](../../src/VyralBot.Core/Services/Currency/LinkedAccountService.cs) |
| `!bid` | `<amount>` | Everyone | Places a bid on the active auction (must beat current bid + minimum increment). | [CurrencyCommandService.cs](../../src/VyralBot.Core/Services/Currency/CurrencyCommandService.cs) via [CurrencyAuctionService.cs](../../src/VyralBot.Core/Services/Currency/CurrencyAuctionService.cs) |
| `!deposit` | `<amount\|all>` | Everyone | Moves currency from the live balance into the bank account (earns daily interest). | [CurrencyCommandService.cs](../../src/VyralBot.Core/Services/Currency/CurrencyCommandService.cs) via [CurrencyBankService.cs](../../src/VyralBot.Core/Services/Currency/CurrencyBankService.cs) |
| `!withdraw` | `<amount\|all>` | Everyone | Moves currency from the bank back to the live balance. | [CurrencyCommandService.cs](../../src/VyralBot.Core/Services/Currency/CurrencyCommandService.cs) via [CurrencyBankService.cs](../../src/VyralBot.Core/Services/Currency/CurrencyBankService.cs) |
| `!bank` | none | Everyone | Shows bank balance, daily interest rate, and total interest earned. | [CurrencyCommandService.cs](../../src/VyralBot.Core/Services/Currency/CurrencyCommandService.cs) via [CurrencyBankService.cs](../../src/VyralBot.Core/Services/Currency/CurrencyBankService.cs) |

---

## Discord built-in moderation commands

Per-bot, always available (see [Discord](../platforms/discord.md)): in-app `/` prefix or in-Discord `!` prefix (moderators only) — `kick`, `ban`, `unban`, `mute`, `unmute`, `slowmode`, `purge`/`clear`, `help`.

## Giveaway commands

Implemented in [GiveawayCommandService.cs](../../src/VyralBot.Core/Services/GiveawayCommandService.cs) (`ProcessCommandAsync` at `GiveawayCommandService.cs:26-93`); engine in [GiveawayService.cs](../../src/VyralBot.Core/Services/GiveawayService.cs). Any `!` message reaches this service. Feature page: [Giveaways](../stream-tools/giveaways.md).

| Command | Arguments | Permission | What it does | Source |
|---|---|---|---|---|
| `!giveaway start` | none | Moderator | Starts the first Pending giveaway (created in the UI). Only one giveaway can be active at a time. | [GiveawayCommandService.cs](../../src/VyralBot.Core/Services/GiveawayCommandService.cs) |
| `!giveaway enter` | none | Everyone | Enters the active giveaway. Eligibility: platform allow-list, follower/subscriber requirements, min watchtime, duplicate check, entry cap. Entries get +bonus tickets for subscribers. | [GiveawayCommandService.cs](../../src/VyralBot.Core/Services/GiveawayCommandService.cs) |
| `!giveaway winner` / `!giveaway draw` | none | Moderator | Draws winner(s): weighted pool (each entry contributes its EntryCount tickets), unique winners, marks rows, stores winner names, sets status Completed. | [GiveawayCommandService.cs](../../src/VyralBot.Core/Services/GiveawayCommandService.cs) |
| `!giveaway cancel` | none | Moderator | Cancels the active giveaway. | [GiveawayCommandService.cs](../../src/VyralBot.Core/Services/GiveawayCommandService.cs) |
| `!giveaway status` | none | Everyone | Shows the active giveaway's name and entry stats. | [GiveawayCommandService.cs](../../src/VyralBot.Core/Services/GiveawayCommandService.cs) |
| `!<entry keyword>` (e.g. `!enter`) | none | Everyone | While a giveaway is active, any `!` command matching its configured EntryKeyword (default `enter`, case-insensitive) registers an entry — same path as `!giveaway enter`. | [GiveawayCommandService.cs](../../src/VyralBot.Core/Services/GiveawayCommandService.cs) |

Eligibility note: real watchtime is resolved through `WatchtimeService` for every platform, and `isFollower` is a real Helix `channels/followers` check for Twitch (5-minute cache; requires the stored token's `moderator:read:followers` scope, degrades to false otherwise). Kick and YouTube still pass `isFollower: false` — KickLib 1.8.0 has no followers API and YouTube has no follower concept — so Followers-only giveaways still reject everyone on those platforms. See [Known Issues](known-issues.md) KI-5, KI-31, KI-33.

---

## Poll commands

Implemented in [PollCommandService.cs](../../src/VyralBot.Core/Services/PollCommandService.cs) (`ProcessCommandAsync` at `PollCommandService.cs:26-76`); engine in [PollService.cs](../../src/VyralBot.Core/Services/PollService.cs). Only messages starting with `!poll` reach this service. Feature page: [Polls](../stream-tools/polls.md).

| Command | Arguments | Permission | What it does | Source |
|---|---|---|---|---|
| `!poll start` | none, or `[duration] question \| option A \| option B …` | Moderator | **Bare** `!poll start` starts the first Pending poll (created in the UI). **With arguments** it creates and starts an ad-hoc poll straight from chat: an optional duration token first (`5m`, `90s`, `1h30m`; 5 s – 24 h), then the question (≤ 200 chars) and 2–6 pipe-separated options (≤ 100 chars each). With a duration, the poll auto-ends at the scheduled time and announces results on every connected platform (the giveaway/poll scheduler). | [PollCommandService.cs](../../src/VyralBot.Core/Services/PollCommandService.cs) |
| `!poll vote <n>` / `!vote <n>` | option number (1-based) | Everyone | Votes for an option — `!vote <n>` is the short everyone-usable alias (same exact vote path, so the spellings can never disagree). Eligibility checks mirror giveaways (platform/follower/sub/watchtime). When AllowMultipleVotes is on, a repeat vote **moves** rather than rejects. | [PollCommandService.cs](../../src/VyralBot.Core/Services/PollCommandService.cs) |
| `!poll results` | none | Everyone | Per-option counts and percentages; falls back to the most recent completed poll when none is active. | [PollCommandService.cs](../../src/VyralBot.Core/Services/PollCommandService.cs) |
| `!poll end` | none | Moderator | Ends the active poll: aggregates counts into `VoteResults` JSON, announces the winning option, sets status Completed. | [PollCommandService.cs](../../src/VyralBot.Core/Services/PollCommandService.cs) |
| `!poll cancel` | none | Moderator | Cancels the active poll without results. | [PollCommandService.cs](../../src/VyralBot.Core/Services/PollCommandService.cs) |
| `!poll status` | none | Everyone | Shows the active poll's question, options, and vote counts. | [PollCommandService.cs](../../src/VyralBot.Core/Services/PollCommandService.cs) |

```
!poll start 5m What game next? | Balatro | Elden Ring | Minecraft
📊 Poll started: What game next? - Options: (1) Balatro (2) Elden Ring (3) Minecraft - ends in 5m - Vote with !poll vote <n> or !vote <n>
```

Same eligibility note as giveaways: real watchtime everywhere; real followers on Twitch only (scope caveat), `false` on Kick/YouTube. See [Known Issues](known-issues.md) KI-5, KI-31, KI-33. Live results also render on the [poll overlay](../stream-tools/browser-sources.md#poll-results-polloverlayhtml) browser source.

---

## Chat-mode commands (Twitch)

Implemented in [ChatModeCommandService.cs](../../src/VyralBot.Core/Services/Commands/ChatModeCommandService.cs) (`ProcessCommandAsync`); Twitch is reached through the `ITwitchChatModeApplier` seam (implemented by the Integrations `TwitchService` — Helix chat-settings updates). Any `!` message reaches this service after the TTS handler; the reply is sent on the **originating** platform. Added in 1.1.0 (wave 16).

Twitch chat-protection modes with **auto-revert**: enabling any mode arms a timed revert (default **10 minutes**, capped at 120) so a raid-shielded chat always opens up again. Re-issuing a command re-arms its timer; `<mode> off` disables immediately and cancels the pending revert.

| Command | Arguments | Permission | What it does | Source |
|---|---|---|---|---|
| `!emoteonly [minutes]` / `!emoteonly off` | optional revert window (1–120, default 10) | **Moderator+** | Enables/disables emote-only chat. Enable reply carries the window ("Emote-only enabled for 10 minutes."); auto-reverts when it expires. | `HandleSimpleModeAsync` |
| `!subonly [minutes]` / `!subonly off` | optional revert window | **Moderator+** | Enables/disables subscribers-only chat, same auto-revert shape. | `HandleSimpleModeAsync` |
| `!followers [follow age] [minutes]` / `!followers off` | follow age (`30`, `2h` — max 90 days) + revert window | **Moderator+** | Enables/disables followers-only chat, optionally requiring a minimum follow age ("must have followed for 2 hours"). Bare `!followers` = any follower. | `HandleFollowersAsync` |
| `!slow <seconds> [minutes]` / `!slow off` | seconds **required** (3–120, Helix bounds) + revert window | **Moderator+** | Enables/disables slow mode with the given per-message wait. | `HandleSlowAsync` |

- **Twitch-only**: every command replies "`!slow` is Twitch-only." (and so on) on other platforms — Kick/YouTube/Discord have no equivalent API surface wired here. Non-mods get "That's a moderator-only command."
- **Auto-revert accuracy**: the checker runs on a one-shot timer that ticks at most every 30 seconds, so a revert fires up to ~30 s late; a revert whose Twitch call fails is dropped after one attempt (a mod re-issues `<mode> off`); pending reverts are **in-memory** — restarting VyralBot leaves the modes as-is on Twitch ([Known Issues](known-issues.md) KI-62).
- Scopes: the Helix chat-settings write needs `moderator:manage:chat_settings` on the stored broadcaster token — tokens issued before that scope was added to the authorization URL fail the update until the Twitch connection is re-run (same re-auth pattern as KI-33).

```
!emoteonly           → Emote-only enabled for 10 minutes.
!followers 2h 30     → Followers-only enabled (must have followed for 2 hours) for 30 minutes.
!slow 30 20          → Slow mode enabled (30s) for 20 minutes.
!slow off            → Slow mode disabled.
```

---

## Warning commands

Implemented in [WarningCommandService.cs](../../src/VyralBot.Core/Services/Moderation/WarningCommandService.cs) (`ProcessCommandAsync`); strikes live in the auto-mod engine's shared escalation state ([ChatModerationEngine.cs](../../src/VyralBot.Core/Services/Moderation/ChatModerationEngine.cs) `RegisterManualWarning`). Any `!` message reaches this service last; the reply is sent on the **originating** platform. Added in 1.1.0 (wave 16).

| Command | Arguments | Permission | What it does | Source |
|---|---|---|---|---|
| `!warn <user> [reason]` | target user (reason ≤ 200 chars, "no reason given" when empty) | **Moderator+** | Adds a manual strike that climbs the **same escalation ladder as auto-mod rule violations**. Warning-level strike: audit row + "⚠ {user} warned (n total) — next: …". When the warn itself tips the viewer onto the Timeout/Ban rung, that action fires **immediately** through the normal moderation pipeline (executed, degraded and audited like a rule violation). | `HandleWarnAsync` |
| `!warnings <user>` | target user | **Moderator+** | Shows the viewer's live strike count, the next rung, and the ladder config (timeout/ban thresholds + decay window). | `HandleWarnings` |
| `!clearwarn <user>` | target user | **Moderator+** | Resets the viewer's strikes to zero (audited). | `HandleClearWarnAsync` |

How manual warns feed the auto-mod ladder:

- One shared strike store — a manual `!warn` and a rule violation are indistinguishable downstream: identical decay window (`StrikeWindowMinutes`), identical rungs (Timeout at `StrikesToTimeout` with base seconds doubling per extra strike, Ban at `StrikesToBan`), and the viewer's *next* rule violation escalates from the warn-inflated count.
- Manual warns work **even while auto-mod is master-switched off** — a warn is a moderator decision, not a detection.
- Every warn/clearwarn writes an audit row (`ModActions`) with the issuing mod as actor, so the Moderator dashboard's Action Log shows the full history.
- **Restart clears warnings**: strike counts are the engine's in-memory escalation state (5,000-viewer cap) — restarting VyralBot resets everyone to zero strikes. Only the audit rows survive ([Known Issues](known-issues.md) KI-64).

```
!warn @spammer flooding chat     → ⚠ spammer warned (1 total) — next: warning
!warn @spammer again             → ⚠ spammer warned (2 total) — escalated: timeout 600s — next: ban
!warnings @spammer               → ⚠ spammer: 2 warning(s) — next: ban (timeout at 2, ban at 3, 60m decay window)
!clearwarn @spammer              → ✅ Warnings cleared for spammer (had 2).
```

---

## Watchtime commands

Implemented in [WatchtimeCommandService.cs](../../src/VyralBot.Core/Services/WatchtimeCommandService.cs) (`ProcessCommandAsync` at `WatchtimeCommandService.cs:26-57`). Only messages starting with `!watchtime` reach this service. Feature page: [Watchtime](../stream-tools/watchtime.md).

| Command | Arguments | Permission | What it does | Source |
|---|---|---|---|---|
| `!watchtime` | none | Everyone | Shows the caller's total watchtime and this-stream session time (formatted "3h 20m" style). | [WatchtimeCommandService.cs](../../src/VyralBot.Core/Services/WatchtimeCommandService.cs) |
| `!watchtime @user` | `@user` or bare name | Everyone | Shows another user's total watchtime. | [WatchtimeCommandService.cs](../../src/VyralBot.Core/Services/WatchtimeCommandService.cs) |
| `!watchtime top` / `leaderboard` / `lb` | `[n]` (default 5, max 10) | Everyone | Watchtime leaderboard across platforms. | [WatchtimeCommandService.cs](../../src/VyralBot.Core/Services/WatchtimeCommandService.cs) |

Routing note: messages starting with `!watchtime` reach this service directly; the seeded built-in `!watchtime` row in the `Commands` table deliberately defers (its handler returns null at `CommandService.cs:148`), so the real implementation above always answers (KI-18's shadowing half is fixed).

---

## Quote commands

Implemented in [QuoteService.cs](../../src/VyralBot.Core/Services/Quotes/QuoteService.cs) (`ProcessCommandAsync`). Only messages starting with `!quote` (which includes `!quotecount`) reach this service, and the reply is sent on the **originating** platform — Twitch, Kick, YouTube, or Discord. Feature page: [Quotes](../chat-features/quotes.md).

| Command | Arguments | Permission | What it does | Source |
|---|---|---|---|---|
| `!quote` | none | Everyone | A random quote, with its number and author. | [QuoteService.cs](../../src/VyralBot.Core/Services/Quotes/QuoteService.cs) |
| `!quote <n>` | quote number | Everyone | A specific quote by number. | [QuoteService.cs](../../src/VyralBot.Core/Services/Quotes/QuoteService.cs) |
| `!quote search <text>` (alias `find`) | substring | Everyone | Up to 3 matches ("Found 2/40: #3 … \| #17 …"), 60-char excerpts. | [QuoteService.cs](../../src/VyralBot.Core/Services/Quotes/QuoteService.cs) |
| `!quote add <text>` | the quote text | **Moderator+** | Adds the quote (records user + platform). | [QuoteService.cs](../../src/VyralBot.Core/Services/Quotes/QuoteService.cs) |
| `!quote del <n>` (aliases `remove`, `delete`) | quote number | **Moderator+** | Deletes the quote permanently. | [QuoteService.cs](../../src/VyralBot.Core/Services/Quotes/QuoteService.cs) |
| `!quote help` | none | Everyone | One-line usage summary. | [QuoteService.cs](../../src/VyralBot.Core/Services/Quotes/QuoteService.cs) |
| `!quotecount` | none | Everyone | "There are N quotes in the database." | [QuoteService.cs](../../src/VyralBot.Core/Services/Quotes/QuoteService.cs) |

Permission note: unlike the legacy command engine's silent denials, non-moderator `add`/`del` attempts get an explicit "You must be a moderator to add/delete quotes" reply.

---

## Mod & stream tools commands

Implemented in [StreamToolsCommandService.cs](../../src/VyralBot.Integrations/StreamTools/StreamToolsCommandService.cs) (`ProcessCommandAsync` — the interface lives in Core at [IStreamToolsCommandService.cs](../../src/VyralBot.Core/Services/Commands/IStreamToolsCommandService.cs); the implementation sits in Integrations because it depends on `ITwitchService`). Any `!` message reaches this service (fallback after song requests); the reply is sent on the **originating** platform. Added in 1.1.0.

The classic mod/stream commands — shoutouts, clipping, raiding, and title/category/commercial control — driven from chat. `@` and `#` decorations on user arguments are stripped automatically.

| Command | Arguments | Permission | Platform | What it does | Source |
|---|---|---|---|---|---|
| `!so <user>` | user handle | **Moderator+** | All | Shoutout: replies "Check out {user} at {url} — go show them some love!" with the platform's channel URL (twitch.tv/, kick.com/, youtube.com/@, tiktok.com/@). | `HandleShoutout` |
| `!clip` | none | **Moderator+** | Twitch only | Creates a clip of the live stream via Helix and replies with the clip URL. Fails with "Clip failed (Twitch only, and the stream must be live)" on other platforms or when offline. | `HandleClipAsync` |
| `!raid <channel>` / `!raid cancel` | target channel, or `cancel` | **Moderator+** | Twitch only | Starts a raid on the target channel, or cancels the raid in progress. | `HandleRaidAsync` |
| `!title [text]` | none, or the new title | **Moderator+** | Twitch only | Without arguments, reads the current title (and game). With text, updates the stream title — everything after the command is the title. | `HandleTitleAsync` |
| `!game [name]` | none, or the game name | **Moderator+** | Twitch only | Without arguments, reads the current category. With a name, resolves it against Twitch's top-games list and updates the category. | `HandleGameAsync` |
| `!commercial <length>` | `30\|60\|90\|120\|150\|180` | **Moderator+** | Twitch only | Starts a commercial of the given length (seconds). Any other value replies with the usage line. | `HandleCommercialAsync` |
| `!uptime` | none | Everyone | All | Twitch: the **real** uptime from Helix `started_at` ("Stream uptime: 2h 14m"; offline when null). Other platforms: live/offline from the live registry — when live it says "duration unknown (no start time available)" because only a bool is tracked. | `HandleUptimeAsync` |
| `!followage [user]` | optional user (defaults to the caller) | Everyone | Twitch only | "X has been following for 2 months, 3 days" (largest two non-zero units). | `HandleFollowAgeAsync` |

### Gating, scopes, and the re-auth note

- **Moderator gate**: everything except `!uptime` and `!followage` replies "That's a moderator-only command." for non-mods.
- **Twitch-only gate**: `!clip`, `!raid`, `!title`, `!game`, `!commercial` and `!followage` reply "… is Twitch-only." on other platforms; `Twitch`-connected checks short-circuit with "Twitch is not connected." when the Twitch service is absent/disconnected.
- **Scopes**: the Twitch-only writes need scopes on the stored broadcaster token — `clips:edit` (`!clip`), `channel:manage:raids` (`!raid`), `channel:manage:broadcast` (`!title`, `!game`), `channel:edit:commercial` (`!commercial`). **The scopes themselves pre-exist** — `TwitchOAuthService` has requested all four since before this feature landed — but **tokens issued before those scope lines were added to the authorization URL do not carry them**: if `!commercial` or `!raid` keeps failing, re-run the Twitch connection once (**Home → Connections → Twitch**) so the token is re-issued with the full scope set (same pattern as KI-33's `moderator:read:followers`).
- **Honest degradations**:
  - `!so` cannot fetch the **target's** channel info — `ITwitchService`'s channel-info read only covers your own channel, so the shoutout is a platform URL + generic hype text rather than "{user} was last playing …" (see [Known Issues](known-issues.md) KI-58). Discord (no channel URL concept) gets plain hype text.
  - `!game <name>` resolves the name through the **top-games search** and takes the **first** match — an exact-name lookup only, not fuzzy search; an unusual spelling replies "Game "X" not found on Twitch." (KI-58).
  - `!followage` cannot distinguish "doesn't follow" from "lookup failed" (the API wrapper returns null for both) — the reply says "isn't following the channel." either way.
  - `!uptime` on non-Twitch platforms reports only live/offline ("duration unknown" when live) — the live registry tracks a bool, not start times; Discord is never tracked and reports offline.
  - Any handler exception degrades to "That command failed — check the logs." — a chat command never throws into the chat pipeline.

### Examples

```
!so @vyralbot                  → Check out vyralbot at twitch.tv/vyralbot — go show them some love!
!raid shroud                   → Raiding shroud!
!raid cancel                   → Raid cancelled.
!title Late night horror — !discord   → Title updated.
!game Balatro                  → Game updated to Balatro.
!commercial 60                 → Commercial started (60s).
!uptime                        → Stream uptime: 2h 14m
!followage @someviewer         → someviewer has been following for 1 year, 2 months.
```

Routing precedence: the unified Commands table runs **first** — the seeded built-in `!uptime` row answers `!uptime` on default installs (KI-59); disable or delete that row on the Commands page to get the Helix-based version above.

---

## Death counter commands

Implemented in [DeathCounterService.cs](../../src/VyralBot.Core/Services/Counters/DeathCounterService.cs) (`ProcessCommandAsync`). Messages starting with `!death` reach this service; the reply is sent on the **originating** platform. State lives in two per-profile config rows (`DeathCounter.Count`, `DeathCounter.History` — newest-first, capped at 50 events), shared with the **Death Counter card on the Tools page** and the [/counters/death overlay widget](../stream-tools/browser-sources.md). Added in 1.1.0.

| Command | Arguments | Permission | What it does | Source |
|---|---|---|---|---|
| `!death` | none | Everyone | Current count ("💀 Deaths: 12"). | `DeathCounterService.cs` |
| `!death+` (alias `!death add`, `!death add <n>`) | optional count | **Moderator+** | +1 (or +n, capped at 10,000 per command). | `DeathCounterService.cs` |
| `!death-` (alias `!death sub/remove <n>`) | optional count | **Moderator+** | −1 (or −n), floored at 0. | `DeathCounterService.cs` |
| `!death set <n>` | the new count | **Moderator+** | Sets the counter (0–999,999). | `DeathCounterService.cs` |
| `!death reset` | none | **Moderator+** | Back to 0 — no confirmation, but every change is logged and recorded in the history. | `DeathCounterService.cs` |
| `!death help` | none | Everyone | Usage summary. | `DeathCounterService.cs` |

Non-moderator change attempts get an explicit "Only moderators can change the death counter" reply. Every mutation is serialized through a semaphore (two simultaneous `!death+` messages cannot lose an update) and lands in the history the Tools card shows.

---

## TTS command

Implemented in [TextToSpeechService.cs](../../src/VyralBot.Core/Services/Audio/TextToSpeechService.cs) (`ProcessCommandAsync`). Messages starting with `!tts` (exactly — `!ttsxyz` is not ours) reach this service; the reply is sent on the **originating** platform. Settings live on the [Alert Settings page's Text-to-Speech card](../stream-tools/visual-alerts.md#text-to-speech-card). Added in 1.1.0.

| Command | Arguments | Permission | What it does | Source |
|---|---|---|---|---|
| `!tts <message>` | the text to speak | Everyone (or mods only — see `Tts.AllowViewers`) | Speaks the message through the configured SAPI voice and replies "🔊 {user} says: {text}". Bare `!tts` replies "Usage: !tts <message>". | `TextToSpeechService.cs` |

Rules, in order: the master toggle (`Tts.Enabled`) must be on (disabled = silently ignored, like other disabled commands); if `Tts.AllowViewers` is off, only moderators pass (silently for viewers); **Do Not Disturb** (`Notifications.DoNotDisturb` — the same key Sounds and Alerts respect) mutes TTS entirely; non-mods are rate-limited to one use per 30 seconds ("⏳ {user}, you can use !tts again in {n}s."); text longer than `Tts.MaxLength` is truncated to that length. Speech is queued on a single-consumer bounded queue (10 max, drop-oldest) — see [Visual Alerts → Text-to-Speech](../stream-tools/visual-alerts.md#text-to-speech-card) for the full settings reference.

---

## See also

- [Commands](../chat-features/commands.md) — the Commands page and visual flow editor (command → flow adoption), permission hierarchy, cooldowns, platform toggles, per-bot scope
- [Variables Catalog](variables-catalog.md) — every placeholder usable in command responses
- [Database Reference](database-reference.md) — the `Commands` (including `BotId`), `CommandCooldowns`, and `DiscordBots` tables
- [Known Issues Ledger](known-issues.md) — KI-1 (dead pipeline, resolved 2026-08-15), KI-37 (`!commands` ignores bot scope), and related wiring gaps
