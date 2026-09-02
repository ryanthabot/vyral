# Command Catalog

> **WARNING: the entire chat command pipeline is currently dead.**
> The orchestrator that runs every command in this catalog — `ChatViewModel.OnChatMessageReceivedAsync` — subscribes to `ChatMessageReceivedEvent` (`ChatViewModel.cs:220`), and **nothing in the solution ever publishes that event**. It is defined at `ChatViewModel.cs:2871` and referenced nowhere else. The per-platform message handlers (`OnTwitchChatMessageReceivedAsync`, `OnKickChatMessageReceivedAsync`, `OnYouTubeChatMessageReceivedAsync`) only render messages in the chat window.
>
> **Consequence: typing any `!` command in chat currently does nothing.** This applies to unified commands, currency commands, giveaway/poll/watchtime commands, watchtime activity tracking, and chat-activity currency earning. The management UIs (Commands page, Currency page, Giveaways page, Polls page) still work — they read and write their data — but nothing executes from chat.
>
> See [Known Issues Ledger](known-issues.md) entry **KI-1** for details and the likely fix direction.

This page is the exhaustive catalog of every chat command the app knows how to handle. Each table lists the command, its arguments, its permission gate, what it actually does, and the source file that implements it. Everything documented here is real, implemented code — but per the warning above, none of it can currently be triggered from chat.

Related pages: [Commands](../chat-features/commands.md), [Currency & Rewards](../stream-tools/currency.md), [Giveaways](../stream-tools/giveaways.md), [Polls](../stream-tools/polls.md), [Watchtime](../stream-tools/watchtime.md).

---

## How commands are routed

When the pipeline is alive, every incoming chat message flows through `OnChatMessageReceivedAsync` in [ChatViewModel.cs](../../src/VyralBot.UI/ViewModels/ChatViewModel.cs) (currently dead — KI-1):

1. Per-platform display filters are applied.
2. **Unified commands** (`CommandService`): if the message starts with `!`, `?`, or `.` (`CommandService.cs:15`), the message is looked up in the `Commands` table. If a row matches (and is enabled, platform-allowed, permission-passing, off-cooldown), its response is sent and processing **stops**.
3. **Currency commands**: any `!` message falls through to `CurrencyCommandService.ProcessCommandAsync` (`ChatViewModel.cs:660`).
4. **Giveaway commands**: any `!` message falls through to `GiveawayCommandService.ProcessCommandAsync` (`ChatViewModel.cs:689`) — includes custom entry keywords.
5. **Poll commands**: messages starting with `!poll` go to `PollCommandService.ProcessCommandAsync` (`ChatViewModel.cs:724`).
6. **Watchtime commands**: messages starting with `!watchtime` go to `WatchtimeCommandService.ProcessCommandAsync` (`ChatViewModel.cs:759`).
7. Finally, watchtime activity is recorded and chat-activity currency is awarded (for non-command messages).

Notes that apply across the whole catalog:

- **Prefixes**: the unified command engine treats `!`, `?`, and `.` identically — `.socials` and `?socials` both resolve to `!socials`. The feature command services (currency/giveaway/poll/watchtime) only recognize `!`.
- **Response delivery**: command responses are only ever sent back to **Twitch or Kick** (`ChatViewModel.cs:640-647`). Entries, votes, and balances are recorded for any platform, but a message from YouTube/TikTok/Discord would get its reply silently dropped.
- **Two permission behaviors**: `CommandService` silently ignores unauthorized and on-cooldown users (no feedback). The currency/giveaway/poll services reply with an explicit "You must be a moderator to use !x" / "Only moderators can..." message.
- **Cooldowns**: unified commands have per-command global and per-user cooldowns (stored in `CommandCooldowns`); the feature command services have none of their own (games have their own internal cooldowns via `CurrencyGameSettings`).
- **Command context quirk**: `CommandContext.IsBroadcaster` is hard-coded `false` in the pipeline (`ChatViewModel.cs:629`), so broadcaster-only commands can never fire even once the pipeline is fixed (KI-18).

---

## Unified commands (CommandService)

Implemented in [CommandService.cs](../../src/VyralBot.Core/Services/Commands/CommandService.cs); definitions stored in the `Commands` table; managed from the Commands page ([Commands](../chat-features/commands.md)).

### Seeded built-ins (21 rows)

Seeded once into an empty database at [DapperDataService.cs](../../src/VyralBot.Data/DapperDataService.cs):3172-3554; a migration pass at `DapperDataService.cs:3556-3581` re-adds the 16 currency commands on existing databases.

The 5 meta commands have real handler code; the 16 currency rows are discoverability/enable flags only — their seeded `Response` text is help text, and actual behavior comes from `CurrencyCommandService` (next section).

| Command | Arguments | Permission | What it does | Source |
|---|---|---|---|---|
| `!commands` | none | Everyone | Lists up to 10 enabled commands the caller has permission for on that platform. | [CommandService.cs](../../src/VyralBot.Core/Services/Commands/CommandService.cs) (`ExecuteCommandsListAsync`) |
| `!uptime` | none | Everyone | Placeholder: replies "Stream uptime tracking not yet implemented. Coming soon!" — no uptime tracking exists. | [CommandService.cs](../../src/VyralBot.Core/Services/Commands/CommandService.cs) (`ExecuteUptimeAsync`) |
| `!socials` | none | Everyone | Returns the stored response template, which resolves `{twitter}`, `{discord}`, `{youtube}` from the Variables table. | [CommandService.cs](../../src/VyralBot.Core/Services/Commands/CommandService.cs) |
| `!discord` | none | Everyone | Returns the stored template resolving `{discord}`. | [CommandService.cs](../../src/VyralBot.Core/Services/Commands/CommandService.cs) |
| `!watchtime` | none | Everyone | **Placeholder** returning the caller's currency balance ("you have N points. Watch time tracking coming soon!"). Shadows the real `WatchtimeCommandService` implementation below whenever both are active, because the unified path runs first. | [CommandService.cs](../../src/VyralBot.Core/Services/Commands/CommandService.cs) (`ExecuteWatchTimeAsync`) |
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

Created in the Commands page. Any name starting with `!`, `?`, or `.` (name is lower-cased on save); cannot collide with a built-in. The response is a template supporting all [variables](variables-catalog.md), plus these context variables supplied by the engine: `{username}`, `{platform}`, `{channel}`, `{balance}`, `{role}` (`CommandService.cs:325-353`).

| Command | Arguments | Permission | What it does | Source |
|---|---|---|---|---|
| `!<name>` (any) | whatever the response expects | Everyone / Subscriber / VIP / Moderator / Broadcaster | Returns the stored response template with variables resolved. Permission hierarchy: each level passes for equal-or-higher (a Subscriber-level command passes for VIPs, mods, broadcaster). Unauthorized and on-cooldown uses are silently ignored. | [CommandService.cs](../../src/VyralBot.Core/Services/Commands/CommandService.cs) |

---

## Currency commands

Implemented in [CurrencyCommandService.cs](../../src/VyralBot.Core/Services/Currency/CurrencyCommandService.cs) — the alias switch is at `CurrencyCommandService.cs:53-78`. 22 handlers, 28 recognized names. Any `!` message reaches this service (fallback after the unified engine). Feature page: [Currency & Rewards](../stream-tools/currency.md).

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
| `!heist` | `<amount>` | Everyone | Starts or joins a group heist; resolves after the join window. | [CurrencyCommandService.cs](../../src/VyralBot.Core/Services/Currency/CurrencyCommandService.cs) via [CurrencyGameService.cs](../../src/VyralBot.Core/Services/Currency/CurrencyGameService.cs) |
| `!roulette` | `<amount> <red\|black\|number>` | Everyone | Roulette bet on color or number. | [CurrencyCommandService.cs](../../src/VyralBot.Core/Services/Currency/CurrencyCommandService.cs) via [CurrencyGameService.cs](../../src/VyralBot.Core/Services/Currency/CurrencyGameService.cs) |

### Earning, banking, linking

| Command | Arguments | Permission | What it does | Source |
|---|---|---|---|---|
| `!daily` | none | Everyone | Claims the daily bonus with streak multipliers (day 1/5/10 escalation). | [CurrencyCommandService.cs](../../src/VyralBot.Core/Services/Currency/CurrencyCommandService.cs) |
| `!link` | `<twitch\|kick\|youtube> [code]` | Everyone | Cross-platform account linking: without a code, initiates a link and returns instructions; with a code, verifies and unifies balances. | [CurrencyCommandService.cs](../../src/VyralBot.Core/Services/Currency/CurrencyCommandService.cs) via [LinkedAccountService.cs](../../src/VyralBot.Core/Services/Currency/LinkedAccountService.cs) |
| `!bid` | `<amount>` | Everyone | Places a bid on the active auction (must beat current bid + minimum increment). | [CurrencyCommandService.cs](../../src/VyralBot.Core/Services/Currency/CurrencyCommandService.cs) via [CurrencyAuctionService.cs](../../src/VyralBot.Core/Services/Currency/CurrencyAuctionService.cs) |
| `!deposit` | `<amount\|all>` | Everyone | Moves currency from the live balance into the bank account (earns daily interest). | [CurrencyCommandService.cs](../../src/VyralBot.Core/Services/Currency/CurrencyCommandService.cs) via [CurrencyBankService.cs](../../src/VyralBot.Core/Services/Currency/CurrencyBankService.cs) |
| `!withdraw` | `<amount\|all>` | Everyone | Moves currency from the bank back to the live balance. | [CurrencyCommandService.cs](../../src/VyralBot.Core/Services/Currency/CurrencyCommandService.cs) via [CurrencyBankService.cs](../../src/VyralBot.Core/Services/Currency/CurrencyBankService.cs) |
| `!bank` | none | Everyone | Shows bank balance, daily interest rate, and total interest earned. | [CurrencyCommandService.cs](../../src/VyralBot.Core/Services/Currency/CurrencyCommandService.cs) via [CurrencyBankService.cs](../../src/VyralBot.Core/Services/Currency/CurrencyBankService.cs) |

---

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

Eligibility caveat: the pipeline passes `isFollower: false` and `watchtimeMinutes: 0` to this service (`ChatViewModel.cs:699-700`), so a FollowersOnly or MinWatchtime giveaway rejects everyone. SubscribersOnly works. See [Known Issues](known-issues.md) KI-5.

---

## Poll commands

Implemented in [PollCommandService.cs](../../src/VyralBot.Core/Services/PollCommandService.cs) (`ProcessCommandAsync` at `PollCommandService.cs:26-76`); engine in [PollService.cs](../../src/VyralBot.Core/Services/PollService.cs). Only messages starting with `!poll` reach this service. Feature page: [Polls](../stream-tools/polls.md).

| Command | Arguments | Permission | What it does | Source |
|---|---|---|---|---|
| `!poll start` | none | Moderator | Starts the first Pending poll (created in the UI) and posts the question with numbered options. Only one active poll at a time. | [PollCommandService.cs](../../src/VyralBot.Core/Services/PollCommandService.cs) |
| `!poll vote` | `<option number>` | Everyone | Votes for an option. Eligibility checks mirror giveaways (platform/follower/sub/watchtime). When AllowMultipleVotes is on, a repeat vote **moves** rather than rejects. | [PollCommandService.cs](../../src/VyralBot.Core/Services/PollCommandService.cs) |
| `!poll results` | none | Everyone | Per-option counts and percentages; falls back to the most recent completed poll when none is active. | [PollCommandService.cs](../../src/VyralBot.Core/Services/PollCommandService.cs) |
| `!poll end` | none | Moderator | Ends the active poll: aggregates counts into `VoteResults` JSON, announces the winning option, sets status Completed. | [PollCommandService.cs](../../src/VyralBot.Core/Services/PollCommandService.cs) |
| `!poll cancel` | none | Moderator | Cancels the active poll without results. | [PollCommandService.cs](../../src/VyralBot.Core/Services/PollCommandService.cs) |
| `!poll status` | none | Everyone | Shows the active poll's question, options, and vote counts. | [PollCommandService.cs](../../src/VyralBot.Core/Services/PollCommandService.cs) |

Same eligibility caveat as giveaways (`isFollower: false`, `watchtimeMinutes: 0` at `ChatViewModel.cs:734-735`) — see [Known Issues](known-issues.md) KI-5.

---

## Watchtime commands

Implemented in [WatchtimeCommandService.cs](../../src/VyralBot.Core/Services/WatchtimeCommandService.cs) (`ProcessCommandAsync` at `WatchtimeCommandService.cs:26-57`). Only messages starting with `!watchtime` reach this service. Feature page: [Watchtime](../stream-tools/watchtime.md).

| Command | Arguments | Permission | What it does | Source |
|---|---|---|---|---|
| `!watchtime` | none | Everyone | Shows the caller's total watchtime and this-stream session time (formatted "3h 20m" style). | [WatchtimeCommandService.cs](../../src/VyralBot.Core/Services/WatchtimeCommandService.cs) |
| `!watchtime @user` | `@user` or bare name | Everyone | Shows another user's total watchtime. | [WatchtimeCommandService.cs](../../src/VyralBot.Core/Services/WatchtimeCommandService.cs) |
| `!watchtime top` / `leaderboard` / `lb` | `[n]` (default 5, max 10) | Everyone | Watchtime leaderboard across platforms. | [WatchtimeCommandService.cs](../../src/VyralBot.Core/Services/WatchtimeCommandService.cs) |

Shadowing note: the seeded, enabled `!watchtime` row in the `Commands` table returns a placeholder (currency balance) and runs **before** this service in the pipeline, so the real implementation above would never be reached until that built-in row is disabled or the shadowing is resolved (KI-1, KI-18).

---

## See also

- [Commands](../chat-features/commands.md) — the management UI, permission hierarchy, cooldowns, platform toggles
- [Variables Catalog](variables-catalog.md) — every placeholder usable in command responses
- [Database Reference](database-reference.md) — the `Commands` and `CommandCooldowns` tables
- [Known Issues Ledger](known-issues.md) — KI-1 (dead pipeline) and related wiring gaps
