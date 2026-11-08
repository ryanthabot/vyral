# Commands

> A full command system — prefixes, permissions, cooldowns, per-platform toggles, template variables, per-Discord-bot scoping, and (since 2026-08-29) **unified editing on the visual flow editor**: every command — built-in or custom — opens as an editable flow, and **SAVE adopts it** — with a working management UI **and** a live chat-triggered execution pipeline (since 2026-08-15, when the `ChatMessageReceivedEvent` wiring gap was fixed).

## Overview

The Commands page lets you manage chat commands: static text responses that fire when a viewer types a message starting with `!`, `?`, or `.` in your Twitch, Kick, or Discord chat. You can create custom commands, set who is allowed to use them (everyone up to broadcaster), set global and per-user cooldowns, enable or disable them per platform, and use template variables like `{username}` and `{balance}` in the responses. The app also seeds 21 built-in commands covering chat meta (`!commands`, `!socials`, ...) and the currency game suite (`!points`, `!gamble`, `!duel`, ...).

Editing is **unified on the visual flow editor** (the drag-and-drop canvas from [Automation](automation.md#visual-flow-editor)): **NEW COMMAND** and every **EDIT** — built-in or custom, plus double-click — open the editor in a page overlay, seeded from a flow **converted** out of the command row. **SAVE** writes the flow and *adopts* the command: from then on the flow, not the legacy row, answers (see [From command to flow](#from-command-to-flow)). The old form-based builder (response modes / actions list editor) is removed.

When you run more than one Discord bot, a custom command can be **pinned to a single bot** ("This bot only") or left unscoped so every bot runs it — see [Bot scope](#bot-scope).

Typing a command in a real chat works end to end: every chat integration publishes a platform-agnostic `ChatMessageReceivedEvent`, and the orchestrator handler runs the unified command engine plus the currency, giveaway, poll, and watchtime fallbacks (details in [How It Works](#how-it-works-developer)). The same pipeline records watchtime activity and awards chat-activity currency. Replies are sent back to the platform the command was typed on — on Discord, through the bot that received the message (see below).

## User Guide

Open **Chat Features → Commands** in the sidebar. The page is titled **Commands** ("Every command — built-in or custom — edits in the Visual Builder. SAVE adopts it into a flow Vyral runs.") and has a bot scope bar plus five tabs (**BUILT-IN**, **CUSTOM**, **TIMERS**, **QUOTES**, **HELP**). The header also carries the **MARKETPLACE** button — the community store of shared command flows, browsable and importable without leaving the page ([Marketplace & Sharecodes](marketplace.md)). The QUOTES tab manages the `!quote` database — it has its own page: [Quotes](quotes.md).

### The bot scope bar

Above the tabs, a **Bot scope** selector lists **All bots** plus one entry per configured Discord bot (refreshed live as bots are added or removed on the [Discord Bots page](../platforms/discord.md#multiple-bots)). It filters what the grids show:

- **All bots** (default) — every command, whatever its scope.
- **A specific bot** — that bot's pinned commands plus the unscoped ones, so you see exactly what that bot can answer. A **Bot Scope** toggle appears on each custom row to pin/unpin it, and a hint line under the bar explains the current view.

With no Discord bots configured the bar shows just "All bots" and the bot-scope UI stays out of the way.

### Tab 1: Built-in Commands

A grid of the 21 seeded commands, one row per command:

| Column | What it shows / does |
|---|---|
| **Command** | The name (always shown with the `!` prefix). Hovering a flow-adopted command shows the "🎨 managed by a flow" hint. |
| **Response** | The stored response template or help text. |
| **Permission** | Minimum user level required (`Everyone` ... `Broadcaster`). |
| **Bot** | The Discord bot scope ("All bots" or the bot's name). |
| **Enabled** | Checkbox — turn a built-in command off entirely (a flow-adopted built-in shows as disabled — its flow answers instead). |
| **Kick** | Checkbox — allow the command in Kick chat. |
| **Twitch** | Checkbox — allow the command in Twitch chat. |
| **Usage** | How many times the command has fired (increments on live executions). |

**EDIT** (or double-clicking a row) opens the **Visual Flow Editor** in an overlay on a flow converted from the built-in — a 🧩 Run Built-in Command block (or a 🔨 Moderate Member block for the Discord moderation commands) plus its stored response. Saving the flow adopts the command; **DELETE** on a flow-adopted built-in removes only the flow and re-enables the original. Plain built-ins (no adopting flow) cannot be deleted. See [From command to flow](#from-command-to-flow).

### Tab 2: Custom Commands

A grid of your own commands and three action buttons — **NEW COMMAND**, **EDIT**, and **DELETE**. Double-clicking a row opens it for editing (same as EDIT). All three paths open the **Visual Flow Editor** in a page overlay; the old form-based builder is gone.

| Column | What it shows / does |
|---|---|
| **Command** | The name (always shown with the `!` prefix); hovering shows the "🎨 managed by a flow" hint when adopted. |
| **Response** | The stored response template (the pre-adoption text). |
| **Permission** | Minimum user level required (`Everyone` ... `Broadcaster`). |
| **Cooldowns (G/U)** | Global / per-user cooldown in seconds. |
| **Bot** | The Discord bot scope ("All bots" or the bot's name). |
| **Bot Scope** | A pin/unpin button — visible only while a specific bot is selected in the scope bar. |
| **Status** | Enabled/disabled. A flow-adopted command shows as disabled — the flow answers instead. |
| **Usage** | How many times the command has fired. |

The editor starts from a flow converted out of the command row (single reply → Send Message, multiple random replies → a Switch fan-out, action steps → the matching blocks), so the canvas reproduces what the command did — edit from there and **SAVE**. **DELETE** removes a command *and* its adopting flow, so the command fully goes away.

### From command to flow

Every edit opens the same [Visual Flow Editor](automation.md#visual-flow-editor) the Automation page uses — hosted in an overlay on the Commands page, and on the [Discord bot detail page](../platforms/discord.md) too, where the **Commands card**'s built-in moderation rows (kick/ban/unban/mute/unmute/slowmode/purge/clear) and this bot's custom command rows each carry an **EDIT** button. What you see on the canvas is **converted from the command row**:

- **Built-in commands** convert to a flow that runs the built-in via a **🧩 Run Built-in Command** block — it invokes the real engine, so permission/cooldown/variable behavior is preserved, and the response lands in `{builtin_response}` (a Send Message node with the stored response follows when the row has one). The command's cooldown and permission carry over as flow **Options**.
- **Discord moderation commands** convert to a **🔨 Moderate Member** block (Kick/Ban/Unban/Timeout/Remove Timeout/Purge/Slowmode — real actions through the bot) plus a confirmation message (the stored response, or "✅ {username}: done." when the row has none).
- **Custom commands** convert by shape:
  - A **single reply** → one **📨 Send Message** block.
  - **Multiple random replies** → **🎲 Random Number + 🔀 Switch**, with one Send Message branch per reply. (The Switch block is no longer placeable from the editor's palette — conditions are the only branching blocks there — but converted flows like this one keep running and rendering.)
  - **Action steps** (`send`, `wait`, `grant_role`, `remove_role`, `add_currency`, `remove_currency`) → the matching flow blocks, chained in order after the response.

The trigger name and `!` prefix, the Discord platform filter (a bot-pinned command converts to a Discord-only trigger), and the cooldown/permission Options carry over as well. Both new blocks are also in the palette for hand-building — see the [block library](automation.md#block-library).

#### SAVE adopts the command

Saving a flow created from a command **retires the legacy command row** (built-ins are disabled, not deleted) and, when the name matches a Discord built-in, merges the name into every bot's `DisabledBuiltIns` list so the in-Discord moderation handler stands down too. Nothing answers twice: **the flow now owns the command.** Adopted rows are marked with the "🎨 managed by a flow" tooltip in the list.

- **Reopening:** an EDIT first looks for a flow whose `SourceCommandId` matches the command and reopens *your edited graph* verbatim; only when none exists does it convert fresh from the legacy row.
- **Deleting a custom command** that has an adopting flow deletes **both** — the command and its flow.
- **Deleting a built-in** that has an adopting flow deletes the **flow** and re-enables the built-in.
- **Plain built-ins** (no adopting flow) still refuse deletion.

Two conversion edges worth knowing: **KI-41** — converted timeout/slowmode/purge flows use the Moderate block's fixed defaults (10 minutes / 10 seconds / 10 messages); a Number parameter can't take `{args}`, so `!mute @user 30` (minutes from args) needs a manual edit. **KI-42** — a converted random-reply command gets one Switch case per reply; adding a 4th reply later means adding the case and node manually. Both are in the [known issues ledger](../reference/known-issues.md).

### Bot scope

Per-bot scoping is Discord-only and answers one question: *which Discord bot answers this command?*

- **All bots (default, `BotId = null`)** — every Discord bot runs the command, exactly as before. Twitch and Kick behavior is never affected by bot scope.
- **This bot only (`BotId` = a `DiscordBots` row id)** — only that bot answers; the command is silently skipped for every other bot. Useful when two bots live in two servers and should each have their own `!rules`.

The scope applies at execution time from the **origin bot** — the bot that actually received the message (see [How It Works](#how-it-works-developer)). The Twitch/Kick toggles are independent: a command with Kick unchecked never fires on Kick whatever its bot scope.

### Tab 3: Timers

Schedules any command to fire automatically — see [Timers (scheduled commands)](#timers-scheduled-commands) below.

### Tab 4: Quotes

A searchable grid over the `!quote` database (add, browse, search, confirm-guarded delete) — full documentation on the dedicated [Quotes](quotes.md) page.

### Tab 5: Help

A static reference page listing the command variables (`{username}`, `{platform}`, `{channel}`, `{balance}`, `{role}`), the permission hierarchy, the two bot-scope modes, and examples. It mirrors the rules enforced in code.

### What the prefixes mean

Any of `!`, `?`, or `.` starts a command, and they all normalize to the same name — `?socials` and `.socials` both run `!socials`. This is why custom command names must start with one of those three characters.

### Where responses go

Command responses are sent back only to the **platform the command was typed on** (Twitch, Kick, or Discord). A Discord reply goes through the bot that received the message, to that bot's active channel (see [Discord](../platforms/discord.md)). There are no per-platform enable flags for YouTube or TikTok.

## How It Works (Developer)

Two engines answer commands, in order: a saved **flow** with a command trigger intercepts matching messages first (the workflow trigger service hooks the chat pipeline ahead of the command engine — see [Automation](automation.md)), and the legacy engine below answers everything else. Adoption (above) is what moves a command from the second engine to the first, which is why an adopted row is left disabled — the `IsEnabled` gate in step 3 makes sure it never double-fires.

The legacy engine is [CommandService.cs](../../src/VyralBot.Core/Services/Commands/CommandService.cs) (registered via DI). `ExecuteAsync(message, CommandContext, string? discordBotId = null)` runs this pipeline:

1. **Parse** — strip the prefix, split on spaces; the command name is normalized to `"!" + firstToken.ToLower()` (`CommandService.cs:72`), so `?socials`, `.socials`, `!socials` all resolve to `!socials`.
2. **Look up** the row in the `Commands` table via `IDataService.GetCommandByNameAsync`.
3. **Gate on flags** — skip if `IsEnabled` is off, or if the message's platform is Kick and `EnabledOnKick` is off (same for Twitch). Other platforms are not gated (no flags exist for them).
4. **Bot-scope check** (`CommandService.cs:84-96`) — the effective bot id is the explicit `discordBotId` argument, falling back to `context.DiscordBotId`; when both the command's `BotId` and the effective id are set and differ, the command is skipped (silently). A null `BotId` always passes, and non-Discord platforms pass because no bot id is ever in play.
5. **Permission check** (`HasPermission`, `CommandService.cs:247`) — hierarchy `everyone < subscriber < vip < moderator < broadcaster`; each stored level passes when the user holds that flag or higher. Unknown permission strings deny.
6. **Cooldown check** (`CheckCooldownAsync`) — global row first (username `null`), then per-user row, both keyed per platform. On cooldown the command is **silently ignored** — no feedback to the user.
7. **Build the response** — for built-ins, only `!commands` and `!uptime` have code (`ExecuteBuiltInCommandAsync`, `CommandService.cs:152`): `!commands` lists up to 10 enabled commands the user has permission for on that platform; `!uptime` returns a "not yet implemented" placeholder; `!watchtime` deliberately defers (returns null) so the real `WatchtimeCommandService` in the fallback chain answers. Everything else (including `!socials`, `!discord`, and all currency commands) just returns the stored `Response` template — for the currency commands the real behavior lives in `CurrencyCommandService`, and the `Commands` rows mainly act as discoverability/enable flags.
8. **Variable replacement** — `ParseVariables` (`CommandService.cs:324`) supplies `username`, `platform`, `channel`, `balance`, `role` plus any `CommandContext.CustomVariables` as context, then delegates to `IVariableReplacementService.ReplaceVariablesAsync` (called sync-over-async with `.GetAwaiter().GetResult()`).
9. **Persist effects** — write `CommandCooldown` rows (`ApplyCooldownsAsync`) and increment `UsageCount` / `LastUsedAt`.

`RegisterCommandAsync` refuses a custom command whose name collides with a built-in.

`CommandContext` gained a `DiscordBotId` property (`ICommandService.cs:68-101`) — the `DiscordBots` row id ("primary" for the setup wizard's singleton, null for non-Discord platforms) — set by the orchestrator alongside the explicit argument.

### The chat entry point

The orchestrator is `ChatViewModel.OnChatMessageReceivedAsync` (`ChatViewModel.cs:894`), subscribed to [ChatMessageReceivedEvent](../../src/VyralBot.Core/Models/ChatMessageReceivedEvent.cs) — a Core-model event published fire-and-forget by all four chat integrations (`TwitchService.OnMessageReceived`, `KickLibChatService.OnChatMessage`, the `YouTubeChatService` polling loop, and `DiscordService.OnMessageReceivedAsync`). For Discord messages the orchestrator first resolves the **origin bot** — `ResolveDiscordBotOrigin` looks the message id up in a bounded message-id→bot-id map that the typed `DiscordChatMessageEvent` handler keeps filled (details in [Discord](../platforms/discord.md#how-it-works-developer)) — then passes it to `ExecuteAsync(evt.Message, context, discordBotId)` and to the reply path, which sends through that bot. Its flow:

```
ChatMessageReceivedEvent (published by Twitch / Kick / YouTube / Discord chat services)
  └─ MessageId dedupe + bot-echo skip
  └─ platform filter flags
  └─ (Discord) resolve origin bot by MessageId → CommandContext.DiscordBotId
  └─ workflow command triggers (adopted commands answer here — see Automation)
  └─ CommandService.ExecuteAsync(message, context, discordBotId) ─── response? ──►
        send to originating Twitch/Kick/Discord (Discord: the origin bot) ──► return
  └─ fallback chain for "!" messages (each may also reply to Twitch/Kick/Discord):
       CurrencyCommandService.ProcessCommandAsync   (any "!..." message)
       GiveawayCommandService.ProcessCommandAsync   (any "!..." message; "!giveaway ..." + custom entry keyword)
       PollCommandService.ProcessCommandAsync       ("!poll*")
       WatchtimeCommandService.ProcessCommandAsync  ("!watchtime*")
       QuoteService.ProcessCommandAsync             ("!quote*" — replies on Twitch/Kick/YouTube/Discord; see [Quotes](quotes.md))
  └─ WatchtimeService.RecordUserActivityAsync       (watchtime tracking)
  └─ CurrencyService.AwardChatActivityAsync         (chat-activity currency, non-"!" messages)
```

The unified path short-circuits with `return` once a DB command matches; the fallback services otherwise run in sequence. `CommandContext.IsBroadcaster` is fed from the event, so broadcaster-only commands work. The full alias surface of the currency/giveaway/poll/watchtime commands is catalogued in the [Command Catalog](../reference/command-catalog.md) and the individual tool pages ([Currency](../stream-tools/currency.md), [Giveaways](../stream-tools/giveaways.md), [Polls](../stream-tools/polls.md), [Watchtime](../stream-tools/watchtime.md)).

### The scope and adoption plumbing

[CommandsViewModel.cs](../../src/VyralBot.UI/ViewModels/CommandsViewModel.cs) injects the optional `IDiscordBotManager` and subscribes to its `BotsChanged` (dispatcher-hopped — the manager raises from background threads). `RefreshBotsAsync` (`CommandsViewModel.cs:339`) rebuilds the scope bar: "All bots" plus one `DiscordBotScopeOption` per configured bot, preserving the previous selection when the bot still exists. `ApplyBotScope` (`:412`) filters the grids client-side for a specific bot (pinned + unscoped rows); `ToggleBotScopeAsync` (`:457`) flips a row's `BotId` between null and the selected bot. The old form's live preview and unknown-placeholder warnings left with the form.

The flow side: `RefreshFlowManagedIdsAsync` (`:263`) tracks the Command row ids adopted by a flow (first match on `Workflow.SourceCommandId`) — it drives the "🎨 managed by a flow" tooltips (`CommandFlowManagedToolTipConverter`, `:905`) and the flow-aware delete (`FindAdoptingFlowIdAsync` `:298` + `DeleteAdoptingFlowAsync` `:318`). The page's code-behind ([CommandsView.xaml.cs](../../src/VyralBot.UI/Views/CommandsView.xaml.cs)) hosts the overlay: `OpenFlowEditorForCommandAsync` reuses the adopting flow when one exists (the canvas loads verbatim), otherwise seeds a fresh flow via `FlowEditorViewModel.InitializeFromCommandAsync` ([CommandFlowTemplates.cs](../../src/VyralBot.UI/Services/CommandFlowTemplates.cs) does the row → graph conversion — never throws; a malformed row degrades to a minimal valid command-triggered flow). Save-time adoption lives in `FlowEditorViewModel.AdoptCommandOwnershipAsync` (`FlowEditorViewModel.cs:2414`): the legacy row is disabled, and same-named Discord built-ins (a built-in `Commands` row or a moderation command claimed by the in-Discord handler) are shadowed by merging the name into every bot's `DiscordBot.{botId}.DisabledBuiltIns` config — the exact list `ChatViewModel` reads before running the in-Discord mod-command handler.

## Files & Code

| File | Role |
|---|---|
| [CommandService.cs](../../src/VyralBot.Core/Services/Commands/CommandService.cs) | Legacy command engine: parsing, permission hierarchy, cooldowns, per-bot scope check, built-in dispatch, variable passthrough |
| [ICommandService.cs](../../src/VyralBot.Core/Services/Commands/ICommandService.cs) | Service interface (`IsCommand`, `ExecuteAsync` with `discordBotId`, CRUD helpers, `HasPermission`) + `CommandContext` with `DiscordBotId` |
| [CommandFlowTemplates.cs](../../src/VyralBot.UI/Services/CommandFlowTemplates.cs) | Command row → editable flow template conversion: built-ins to a `builtin` node, moderation commands to a `moderate` node, custom commands to send/random-switch/action chains |
| [CommandsViewModel.cs](../../src/VyralBot.UI/ViewModels/CommandsViewModel.cs) | Page ViewModel: bot scope bar (live via `BotsChanged`), flow-managed id tracking, flow-aware delete, built-in/custom split |
| [CommandsView.xaml](../../src/VyralBot.UI/Views/CommandsView.xaml) | Page layout: scope bar, five tabs (built-in/custom/timers/quotes/help grids), and the full-size Visual Builder overlay host |
| [CommandsView.xaml.cs](../../src/VyralBot.UI/Views/CommandsView.xaml.cs) | Code-behind: opens the overlay (fresh conversion vs. reusing the adopting flow) |
| [CommandTimerService.cs](../../src/VyralBot.UI/Services/CommandTimerService.cs) | Timers scheduler: 30 s tick, per-platform chat counters for the `MinimumMessages` gate, counter reset after each fire |
| [FlowEditorViewModel.cs](../../src/VyralBot.UI/ViewModels/FlowEditorViewModel.cs) | The shared visual editor VM: `InitializeFromCommandAsync` conversion entry, save-time adoption (`AdoptCommandOwnershipAsync`, `MergeDisabledBuiltInsAsync`) — see [Automation](automation.md) |
| `CommandFlowManagedToolTipConverter` (in [CommandsViewModel.cs](../../src/VyralBot.UI/ViewModels/CommandsViewModel.cs)) | "🎨 managed by a flow" grid tooltip converter |
| [Command.cs](../../src/VyralBot.Data/Entities/Command.cs) | Entity: name, response, permission, cooldowns, platform flags, `BotId` (null = all bots), usage counters |
| [CommandCooldown.cs](../../src/VyralBot.Data/Entities/CommandCooldown.cs) | Entity: per (CommandId, Username or null, Platform) expiry rows |
| [DapperDataService.cs](../../src/VyralBot.Data/DapperDataService.cs) | Persistence + schema (`Commands`, `Commands.BotId` migration, `GetCommandsByBotAsync`) + seeding of the 21 built-ins |
| [IDiscordBotManager.cs](../../src/VyralBot.Integrations/Discord/IDiscordBotManager.cs) | The bot list behind the scope bar (see [Discord](../platforms/discord.md)) |
| [ChatMessageReceivedEvent.cs](../../src/VyralBot.Core/Models/ChatMessageReceivedEvent.cs) | The platform-agnostic event feeding the orchestrator (Platform, Username, Message, badges, MessageId) |
| [ChatViewModel.cs](../../src/VyralBot.UI/ViewModels/ChatViewModel.cs) | The command orchestrator and fallback chain (:894 onwards), Discord origin resolution (:685) |

Permission hierarchy exactly as enforced:

```csharp
public bool HasPermission(CommandContext context, string permission)
{
    return permission.ToLower() switch
    {
        "everyone" => true,
        "subscriber" or "sub" => context.IsSubscriber || context.IsVIP || context.IsModerator || context.IsBroadcaster,
        "vip" => context.IsVIP || context.IsModerator || context.IsBroadcaster,
        "moderator" or "mod" => context.IsModerator || context.IsBroadcaster,
        "broadcaster" => context.IsBroadcaster,
        _ => false
    };
}
```

Seeded built-in commands (21, inserted only when the `Commands` table is empty; a second migration pass re-adds missing currency commands on existing databases):

| Command | Permission | Notes |
|---|---|---|
| `!commands` | Everyone | Lists up to 10 permitted commands |
| `!uptime` | Everyone | Placeholder — "not yet implemented" |
| `!socials` | Everyone | Returns template using `{twitter}`, `{discord}`, `{youtube}` |
| `!discord` | Everyone | Returns `{discord}` template |
| `!watchtime` | Everyone | Defers to the real `WatchtimeCommandService` (built-in handler returns null) |
| `!points` `!leaderboard` `!gamble` `!redeem` `!rewards` `!daily` `!rank` `!store` `!slots` `!duel` `!accept` `!heist` `!roulette` `!link` | Everyone | Currency commands — behavior lives in `CurrencyCommandService`, rows are discoverability/flags |
| `!give` `!take` | Moderator | Currency admin commands |

## Timers (scheduled commands)

The **Timers** tab schedules any command to fire automatically: pick a command, set an interval in minutes (minimum 5), choose the target — Twitch, Kick, or a specific Discord bot (the bot's active channel) — and optionally set **Min messages between fires**. The scheduler ticks every 30 seconds, resolves the command's response with variables, sends it, and records Last Run. Timers bypass chat permissions and cooldowns (they are scheduled, not user invocations) and only run while the app is open with a profile loaded.

### MinimumMessages — the chat-activity gate

**Min messages between fires (0 = always)** keeps timers quiet in dead chat: a timer with the field set to N only fires when **at least N chat messages have arrived on its target platform since that platform's last timer fire**. The grid shows the value in a **Min Msgs** column (validated 0–1000).

How the gate behaves (all verified against `CommandTimerService`):

- **0 (the default) = always fire** — the interval alone decides. This is classic timer behavior.
- **The gate is per-platform, per-fire-window.** The service counts every chat message (any chatter, any content) on each platform; firing any timer on a platform **resets that platform's counter to 0**, so all timers targeting it must wait for N fresh messages again.
- **A gated timer fires at the first tick after the threshold** — the 30-second tick checks interval first, then the gate; while the gate is unsatisfied the timer's Last Run is *not* stamped, so it does not skip a cycle or restart the interval.
- Typical use: `!socials` every 30 minutes with Min Msgs = 10 → the announcement waits until chat is actually alive instead of talking to an empty room at 3 AM.

## Data & Settings

Stored in the per-profile SQLite database (see [Data Layer](../architecture/data-layer.md) and [Database Reference](../reference/database-reference.md)):

- **Commands** — one row per command. Key columns: `Name` (normalized `!name`), `Response`, `Permission` (default `Everyone`), `GlobalCooldown`, `UserCooldown`, `IsEnabled`, `IsBuiltIn`, `EnabledOnKick`, `EnabledOnTwitch`, `BotId` (null = all Discord bots; added by `ALTER TABLE` migration on first run), `UsageCount`, `LastUsedAt`, `CreatedAt`, `UpdatedAt`. Deletes are guarded server-side with `AND IsBuiltIn = 0`. A flow-adopted row keeps all of this — it is only disabled (`IsEnabled = 0`), and the owning flow carries `SourceCommandId` = the row's id.
- **CommandCooldowns** — expiry rows per (CommandId, Username `null` for global, Platform), written on every successful execution.
- **CommandTimers** — one row per timer (Timers tab): `Id`, `Name`, `CommandName`, `IntervalMinutes`, `MinimumMessages` (chat-activity gate, 0 = always), `TargetPlatform` ("Twitch"/"Kick"/"Discord"), `BotId` (Discord destination bot), `IsEnabled`, `LastRunAt`. See [Timers](#timers-scheduled-commands).
- **Quotes** — the `!quote` database; see [Quotes → Data & Settings](quotes.md#data--settings) and the [Database Reference](../reference/database-reference.md).

## Status & Known Issues

- **The command pipeline is live** (since 2026-08-15). `ChatMessageReceivedEvent` ([ChatMessageReceivedEvent.cs](../../src/VyralBot.Core/Models/ChatMessageReceivedEvent.cs)) is published by all four chat integrations (Twitch, Kick, YouTube, and Discord) and consumed by `ChatViewModel.OnChatMessageReceivedAsync`; unified commands, currency/giveaway/poll/watchtime commands, watchtime tracking, and chat-activity currency all run from live chat. The Usage column reflects real executions. — [known issues ledger](../reference/known-issues.md) (KI-1 resolved)
- **Command editing is unified on the visual flow editor** (since 2026-08-29): NEW COMMAND and every EDIT (built-in or custom, plus double-click, and the Discord bot detail Commands card's per-row EDIT buttons) open the editor in an overlay on a flow converted from the command row; the old form-based builder is removed. SAVE adopts the command (legacy row retired, Discord built-ins shadowed via `DisabledBuiltIns`), DELETE is flow-aware, and reopening reuses the adopting flow by `SourceCommandId`.
- The same pipeline drives currency/giveaway/poll/watchtime chat commands — see the [Currency](../stream-tools/currency.md), [Giveaways](../stream-tools/giveaways.md), [Polls](../stream-tools/polls.md), and [Watchtime](../stream-tools/watchtime.md) pages. Event-driven features (auto-replies, sounds, alerts, event currency bonuses) run on their own handlers — see [Event Reactions](../chat/event-reactions.md).
- `!uptime` still returns a "coming soon" placeholder (no uptime tracking exists); the seeded `!watchtime` row now defers to the real `WatchtimeCommandService` (the shadowing placeholder is gone). — [known issues ledger](../reference/known-issues.md)
- Cooldown violations and permission failures are **silently ignored** — no user-visible feedback (by design, `CommandService.cs:107`).
- Platform gating flags exist for Kick and Twitch only; there are no YouTube/TikTok flags, and responses are sent back to Twitch, Kick, or Discord (a Discord reply goes through the origin bot to its active channel — see [Discord](../platforms/discord.md)).
- **`!commands` ignores bot scope** (KI-37): the built-in list can advertise a command pinned to a different bot than the one answering. The rare Discord origin-resolution race that falls back to the Primary bot is KI-38. Both are in the [known issues ledger](../reference/known-issues.md).
- **Converted moderation flows use fixed defaults** (KI-41): a Number parameter can't take `{args}`, so timeout minutes / slowmode seconds / purge counts from command arguments need a manual edit. **Converted random-reply commands get one Switch case per reply** (KI-42). Both are in the [known issues ledger](../reference/known-issues.md).
- Command permission behavior differs from the currency/giveaway/poll services, which reply with explicit "Only moderators can ..." messages instead of silence.
