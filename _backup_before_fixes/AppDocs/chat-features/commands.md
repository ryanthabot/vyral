# Commands

> A full command system — prefixes, permissions, cooldowns, per-platform toggles, and template variables — whose management UI works completely but whose chat-triggered execution is currently dead because the event that feeds it is never published.

## Overview

The Commands page lets you manage chat commands: static text responses that fire when a viewer types a message starting with `!`, `?`, or `.` in your Twitch or Kick chat. You can create custom commands, set who is allowed to use them (everyone up to broadcaster), set global and per-user cooldowns, enable or disable them per platform, and use template variables like `{username}` and `{balance}` in the responses. The app also seeds 21 built-in commands covering chat meta (`!commands`, `!socials`, ...) and the currency game suite (`!points`, `!gamble`, `!duel`, ...).

**The critical thing to know about the current build:** none of this fires from chat. The handler that orchestrates all command processing subscribes to a `ChatMessageReceivedEvent` that nothing in the entire solution ever publishes, so typing `!socials` in a real chat does nothing today. The Commands page itself is a working editor for the command database, and commands still respond in one place only — nowhere at runtime. This single wiring gap also disables the currency, giveaway, poll, and watchtime chat commands, which hang off the same dead pipeline. See [Status & Known Issues](#status--known-issues) below.

## User Guide

Open **Chat Features → Commands** in the sidebar. The page is titled "Command System" and has three tabs.

### Tab 1: Built-in Commands

A read-only grid of the 21 seeded commands, one row per command:

| Column | What it shows / does |
|---|---|
| **Command** | The name (always shown with the `!` prefix). |
| **Response** | The stored response template or help text. |
| **Permission** | Minimum user level required (`Everyone` ... `Broadcaster`). |
| **Enabled** | Checkbox — turn a built-in command off entirely. |
| **Kick** | Checkbox — allow the command in Kick chat. |
| **Twitch** | Checkbox — allow the command in Twitch chat. |
| **Usage** | How many times the command has fired (always 0 in the current build, see below). |

Built-in commands **cannot be edited or deleted** — the response text and permission level are fixed. You can only toggle Enabled and the two platform checkboxes.

### Tab 2: Custom Commands

A grid of your own commands plus an add/edit form.

**To create a command:**

1. Click **Add Command**. The form opens with defaults: permission `Everyone`, global cooldown 5 s, user cooldown 30 s, enabled on both platforms.
2. Enter the **Command Name**. It must start with `!`, `?`, or `.` (for example `!discord`, `?lurk`, `.so`). The name is lower-cased when saved. You cannot reuse the name of a built-in command.
3. Enter the **Response**. This is the text the bot sends. Use the **VariablePicker button** next to the field (or right-click any text box) to insert template variables such as `{username}`, `{platform}`, `{channel}`, `{balance}`, `{role}` — see [Variables](variables.md).
4. Pick a **Permission** level: `Everyone`, `Subscriber`, `VIP`, `Moderator`, or `Broadcaster`. Higher levels automatically pass for lower requirements (a moderator passes a Subscriber-only command).
5. Set **Global Cooldown** (seconds between any two uses by anyone) and **User Cooldown** (seconds before the same user can use it again). Both apply together; `0` disables that cooldown.
6. Leave **Enabled** on and check/uncheck **Kick** / **Twitch** to choose where the command can fire.
7. Click **Save**. The command appears in the grid.

**To edit or delete:** select a custom command and use the edit/delete actions. Deleting a built-in command is refused. The grid also shows per-command **Status** (enabled/disabled), cooldowns, and **Usage** counters.

### Tab 3: Help

A static reference page listing the five command variables (`{username}`, `{platform}`, `{channel}`, `{balance}`, `{role}`) and the permission hierarchy. It mirrors the rules enforced in code.

### What the prefixes mean

Any of `!`, `?`, or `.` starts a command, and they all normalize to the same name — `?socials` and `.socials` both run `!socials`. This is why custom command names must start with one of those three characters.

### Where responses go

Command responses are sent back only to the **platform the command was typed on** (Twitch or Kick). There are no per-platform enable flags for YouTube, TikTok, or Discord.

## How It Works (Developer)

The engine is [CommandService.cs](../../src/VyralBot.Core/Services/Commands/CommandService.cs) (registered via DI). `ExecuteAsync(message, CommandContext)` runs this pipeline:

1. **Parse** — strip the prefix, split on spaces; the command name is normalized to `"!" + firstToken.ToLower()` (`CommandService.cs:76`), so `?socials`, `.socials`, `!socials` all resolve to `!socials`.
2. **Look up** the row in the `Commands` table via `IDataService.GetCommandByNameAsync`.
3. **Gate on flags** — skip if `IsEnabled` is off, or if the message's platform is Kick and `EnabledOnKick` is off (same for Twitch). Other platforms are not gated (no flags exist for them).
4. **Permission check** (`HasPermission`, `CommandService.cs:248`) — hierarchy `everyone < subscriber < vip < moderator < broadcaster`; each stored level passes when the user holds that flag or higher. Unknown permission strings deny.
5. **Cooldown check** (`CheckCooldownAsync`) — global row first (username `null`), then per-user row, both keyed per platform. On cooldown the command is **silently ignored** — no feedback to the user.
6. **Build the response** — for built-ins, only `!commands`, `!uptime`, and `!watchtime` have code (`ExecuteBuiltInCommandAsync`, `CommandService.cs:142`): `!commands` lists up to 10 enabled commands the user has permission for on that platform; `!uptime` returns a "not yet implemented" placeholder; `!watchtime` returns a placeholder that reports the user's currency balance. Everything else (including `!socials`, `!discord`, and all currency commands) just returns the stored `Response` template — for the currency commands the real behavior lives in `CurrencyCommandService`, and the `Commands` rows mainly act as discoverability/enable flags.
7. **Variable replacement** — `ParseVariables` (`CommandService.cs:325`) supplies `username`, `platform`, `channel`, `balance`, `role` plus any `CommandContext.CustomVariables` as context, then delegates to `IVariableReplacementService.ReplaceVariablesAsync` (called sync-over-async with `.GetAwaiter().GetResult()`).
8. **Persist effects** — write `CommandCooldown` rows (`ApplyCooldownsAsync`) and increment `UsageCount` / `LastUsedAt`.

`RegisterCommandAsync` refuses a custom command whose name collides with a built-in.

### The (dead) chat entry point

The intended orchestrator is `ChatViewModel.OnChatMessageReceivedAsync` (`ChatViewModel.cs:592`), subscribed to `ChatMessageReceivedEvent` at `ChatViewModel.cs:220`. Its designed flow:

```
ChatMessageReceivedEvent (never published — pipeline dead)
  └─ platform filter flags
  └─ CommandService.ExecuteAsync ─── response? ──► send to originating Twitch/Kick ──► return
  └─ fallback chain for "!" messages (each may also reply to Twitch/Kick):
       CurrencyCommandService.ProcessCommandAsync   (any "!..." message)
       GiveawayCommandService.ProcessCommandAsync   (any "!..." message; "!giveaway ..." + custom entry keyword)
       PollCommandService.ProcessCommandAsync       ("!poll*")
       WatchtimeCommandService.ProcessCommandAsync  ("!watchtime*")
  └─ WatchtimeService.RecordUserActivityAsync       (watchtime tracking)
  └─ CurrencyService.AwardChatActivityAsync         (chat-activity currency, non-"!" messages)
```

The unified path short-circuits with `return` once a DB command matches; the fallback services otherwise run in sequence. Note `CommandContext.IsBroadcaster` is hard-coded `false` with a TODO (`ChatViewModel.cs:629`), so broadcaster-only commands could never fire from chat even once the pipeline is re-wired. The full alias surface of the currency/giveaway/poll/watchtime commands is catalogued in the [Command Catalog](../reference/command-catalog.md) and the individual tool pages ([Currency](../stream-tools/currency.md), [Giveaways](../stream-tools/giveaways.md), [Polls](../stream-tools/polls.md), [Watchtime](../stream-tools/watchtime.md)).

## Files & Code

| File | Role |
|---|---|
| [CommandService.cs](../../src/VyralBot.Core/Services/Commands/CommandService.cs) | Command engine: parsing, permission hierarchy, cooldowns, built-in dispatch, variable passthrough |
| [ICommandService.cs](../../src/VyralBot.Core/Services/Commands/ICommandService.cs) | Service interface (`IsCommand`, `ExecuteAsync`, CRUD helpers, `HasPermission`) |
| [CommandsViewModel.cs](../../src/VyralBot.UI/ViewModels/CommandsViewModel.cs) | 3-tab page ViewModel: built-in/custom split, add/edit validation, toggles |
| [CommandsView.xaml](../../src/VyralBot.UI/Views/CommandsView.xaml) | Page layout: three tabs, grids, edit form with inline VariablePicker |
| [CommandsView.xaml.cs](../../src/VyralBot.UI/Views/CommandsView.xaml.cs) | Code-behind |
| [CommandEditTitleConverter.cs](../../src/VyralBot.UI/Converters/CommandEditTitleConverter.cs) | Add/Edit form title converter |
| [Command.cs](../../src/VyralBot.Data/Entities/Command.cs) | Entity: name, response, permission, cooldowns, platform flags, usage counters |
| [CommandCooldown.cs](../../src/VyralBot.Data/Entities/CommandCooldown.cs) | Entity: per (CommandId, Username or null, Platform) expiry rows |
| [DapperDataService.cs](../../src/VyralBot.Data/DapperDataService.cs) | Persistence + schema (`Commands` ~line 2924) + seeding of the 21 built-ins (lines 3172-3581) |
| [ChatViewModel.cs](../../src/VyralBot.UI/ViewModels/ChatViewModel.cs) | The (currently dead) command orchestrator and fallback chain, lines 592-820 |

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
| `!watchtime` | Everyone | Placeholder returning currency balance |
| `!points` `!leaderboard` `!gamble` `!redeem` `!rewards` `!daily` `!rank` `!store` `!slots` `!duel` `!accept` `!heist` `!roulette` `!link` | Everyone | Currency commands — behavior lives in `CurrencyCommandService`, rows are discoverability/flags |
| `!give` `!take` | Moderator | Currency admin commands |

## Data & Settings

Stored in the per-profile SQLite database (see [Data Layer](../architecture/data-layer.md) and [Database Reference](../reference/database-reference.md)):

- **Commands** — one row per command. Key columns: `Name` (normalized `!name`), `Response`, `Permission` (default `Everyone`), `GlobalCooldown`, `UserCooldown`, `IsEnabled`, `IsBuiltIn`, `EnabledOnKick`, `EnabledOnTwitch`, `UsageCount`, `LastUsedAt`, `CreatedAt`, `UpdatedAt`. Deletes are guarded server-side with `AND IsBuiltIn = 0`.
- **CommandCooldowns** — expiry rows per (CommandId, Username `null` for global, Platform), written on every successful execution.

## Status & Known Issues

- **The entire command pipeline is dead from chat.** `ChatMessageReceivedEvent` is defined at `ChatViewModel.cs:2871`, subscribed at `:220`, and **published by nothing in the solution**. `CommandService.ExecuteAsync` and the currency/giveaway/poll/watchtime command services therefore never run from live chat, and watchtime tracking plus chat-activity currency (also in this handler) never record. The management UI works; runtime execution does not. — [known issues ledger](../reference/known-issues.md)
- The gap also disables currency/giveaway/poll/watchtime chat commands — see the [Currency](../stream-tools/currency.md), [Giveaways](../stream-tools/giveaways.md), [Polls](../stream-tools/polls.md), and [Watchtime](../stream-tools/watchtime.md) pages. Only *event*-driven features (auto-replies, sounds, alerts, event currency bonuses) currently run — see [Event Reactions](../chat/event-reactions.md).
- `!uptime` and `!watchtime` built-ins return "coming soon" placeholders; the real `!watchtime` implementation lives in `WatchtimeCommandService` and would be shadowed by the seeded enabled `!watchtime` row once the pipeline is re-wired (the unified path returns first). — [known issues ledger](../reference/known-issues.md)
- Cooldown violations and permission failures are **silently ignored** — no user-visible feedback (by design, `CommandService.cs:107`).
- `CommandContext.IsBroadcaster` is hard-coded `false` in the pipeline (`ChatViewModel.cs:629`), so broadcaster-only commands could never fire from chat as wired.
- Platform gating flags exist for Kick and Twitch only; there are no YouTube/TikTok/Discord flags, and responses are only ever sent back to Twitch or Kick.
- Command permission behavior differs from the currency/giveaway/poll services, which reply with explicit "Only moderators can ..." messages instead of silence.
- The Usage column can only ever show 0 in the current build (usage is incremented only by the dead execution path).
