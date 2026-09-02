# Giveaways

> Cross-platform chat giveaways with a weighted drawing engine — fully built on the management side, but currently unable to receive entries from chat because the command pipeline that feeds it is not wired up.

## Overview

The Giveaways tool lets a streamer create a giveaway in the app, start it, let viewers enter by typing a keyword in chat (for example `!enter`), and then draw one or more winners with a weighted random draw. Entries can come from any platform the bot is connected to, and subscribers can be given extra "tickets" so they win proportionally more often.

Two things every user should know before relying on it in the current build:

1. **Chat entries never happen right now.** The chat-message pipeline that routes `!giveaway ...` and `!enter` commands into the giveaway engine is dead — no code publishes the event it listens to. You can still create, start, draw, and cancel giveaways from the Giveaways page, but the entry list will stay empty no matter what viewers type. See [Status & Known Issues](#status--known-issues).
2. **Followers-only and min-watchtime restrictions reject everyone.** Even with the pipeline fixed, the chat wiring passes `isFollower: false` and `watchtimeMinutes: 0` for every message, so any giveaway that enables Followers-only or a minimum watchtime will decline every single entry. Leave both off.

What does work today: creating giveaways, the full lifecycle from the UI (Start / Draw Winners / Cancel), the weighted drawing logic, and persistence in the per-profile database.

## User Guide

Open **Stream Tools → Giveaways** in the sidebar (or the Giveaways card on the Tools hub page). The page has three areas: a create form on the left, the giveaway list in the middle, and details/actions for the selected giveaway.

### Creating a giveaway

Fill in the create form and press **Create Giveaway**:

| Field | What it does | Default |
|---|---|---|
| **Name** (required) | Display name, e.g. "Anniversary Key Giveaway". | *(empty)* |
| **Description** | Free text shown in the details pane; not posted to chat. | *(empty)* |
| **Entry Keyword** (required) | The word viewers type (without the `!`) to enter — the default `enter` means viewers type `!enter`. Treated case-insensitively. | `enter` |
| **Winner Count** | How many unique winners to draw (minimum 1). | `1` |
| **Max Entries** | Cap on total entries. The database supports "no limit", but the form always sends a number — note the default is **1**, so raise it or only the very first viewer can enter. | `1` |
| **Subscriber Bonus** (toggle) | Gives subscribers extra entries. | Off |
| **Subscriber Bonus Entries** | Extra entries a subscriber gets (on top of their 1). | `5` |
| **Min Watchtime (minutes)** | Viewers need at least this much tracked watchtime to enter. **Currently rejects everyone — see issues.** | `0` |
| **Followers Only** (toggle) | Only followers may enter. **Currently rejects everyone — see issues.** | Off |
| **Subscribers Only** (toggle) | Only subscribers may enter. This restriction works. | Off |
| **Allowed Platforms** | Comma-separated platform list (`Twitch,Kick`) or `All`. Entries from other platforms are declined with a chat message. | `Twitch,Kick` |
| **Scheduled End** (date picker) | Optional end time. It is stored and displayed, but nothing automatically ends the giveaway at that time — you must draw or cancel manually. | *(none)* |

After creation the giveaway appears in the list with status **Pending**. The form clears back to its defaults.

### Running a giveaway from start to finish

1. **Create** it (above). It starts as Pending.
2. Select it in the list and press **Start Giveaway**. Status becomes **Active** and the entries panel comes alive. Only one giveaway can be Active at a time — starting another fails until you draw or cancel the current one.
3. Viewers enter by typing the entry keyword in chat (`!enter` by default, or `!giveaway enter`). Each (username, platform) pair can enter once; subscribers get their bonus entries recorded on the single entry row.
4. Press **Refresh** any time to reload the list, the selected giveaway's entries, and the Total Entries / Unique Participants counters.
5. Press **Draw Winners** to end the giveaway. The draw picks unique winners from a weighted pool (each entry contributes its entry count in tickets), marks the winning entries, stores the winner names on the giveaway, and flips the status to **Completed**.
6. **Cancel Giveaway** aborts an Active or Pending giveaway (status **Cancelled**, no winners drawn).

### Chat commands

These commands are implemented by the giveaway engine and would be available to chat once the command pipeline is live (see [Commands](../chat-features/commands.md) and the [command catalog](../reference/command-catalog.md)):

| Command | Who | Effect |
|---|---|---|
| `!giveaway start` | Moderators | Starts the first Pending giveaway; announces the entry keyword. |
| `!enter` (or the giveaway's custom keyword) | Everyone | Enters the active giveaway. Also `!giveaway enter`. |
| `!giveaway winner` / `!giveaway draw` | Moderators | Draws winner(s) and completes the giveaway. |
| `!giveaway cancel` | Moderators | Cancels the active giveaway. |
| `!giveaway status` | Everyone | Reports entries, participants, and winner count for the active giveaway. |

Unauthorized use of a moderator command gets a visible "Only moderators can..." reply. Bot responses are only ever sent back to Twitch or Kick; an entry accepted from another platform gets recorded, but its confirmation reply is dropped.

## How It Works (Developer)

Two Core services split the responsibility: `GiveawayService` is the engine, `GiveawayCommandService` is the chat-command adapter. Both are registered as singletons in `App.xaml.cs` (registration at `App.xaml.cs:129-130`).

**Lifecycle.** A `Giveaway` moves `Pending → Active → Completed|Cancelled`. `StartGiveawayAsync` (`GiveawayService.cs:66`) refuses to start unless the status is Pending and `GetActiveGiveawayAsync()` returns null — a single-active invariant enforced in code. `DrawWinnersAsync` (`GiveawayService.cs:207`) is the only path to Completed; `CancelGiveawayAsync` (`GiveawayService.cs:291`) accepts Pending or Active.

**Entry validation.** `EnterGiveawayAsync` (`GiveawayService.cs:100-202`) checks, in order: giveaway exists and is Active; platform is in the allow-list (comma-split, case-insensitive, `All` bypasses); follower requirement; subscriber requirement; minimum watchtime; no existing entry for the (username, platform) pair; and the optional max-entries cap. It then computes `EntryCount = 1 + SubscriberBonusEntries` when the bonus is enabled and the user is a subscriber, saves the entry, and refreshes denormalized `TotalEntries` / `UniqueParticipants` counters on the giveaway row.

**Weighted drawing.** `DrawWinnersAsync` expands every entry into its `EntryCount` tickets in an in-memory pool, then draws up to `min(WinnerCount, entries.Count)` unique winners, re-rolling duplicates with a cap of 100 attempts per winner. Winning rows get `IsWinner = true` and `WonAt`; the giveaway stores winners as a comma-joined `"name (platform)"` string and is marked Completed.

**Command routing (currently dead).** `ChatViewModel.OnChatMessageReceivedAsync` is the intended orchestrator: for any `!`-prefixed message it calls `GiveawayCommandService.ProcessCommandAsync` (`ChatViewModel.cs:693-700`), which handles `!giveaway <subcommand>` plus any custom keyword matching the active giveaway's `EntryKeyword` (`GiveawayCommandService.cs:39-55`). The orchestrator subscribes to `ChatMessageReceivedEvent` at `ChatViewModel.cs:220`, but that event (defined at `ChatViewModel.cs:2871`) is published by nothing in the solution — the real per-platform chat handlers render messages into the UI and never invoke this pipeline. See [Event Bus](../architecture/event-bus.md).

```csharp
// ChatViewModel.cs:693-700 — the giveaway call with hard-coded eligibility gaps
var response = await _giveawayCommandService.ProcessCommandAsync(
    evt.Message,
    evt.Username,
    evt.Platform,
    evt.IsModerator,
    evt.IsSubscriber,
    false, // isFollower - can be tracked in future
    0);    // watchtimeMinutes - can be tracked in future
```

Because `isFollower` is always `false`, `EnterGiveawayAsync`'s check at `GiveawayService.cs:132` rejects every entry to a Followers-only giveaway; because `watchtimeMinutes` is always `0`, the check at `GiveawayService.cs:144` rejects every entry when `MinWatchtimeMinutes > 0`. The watchtime data exists in the [Watchtime](watchtime.md) tables but is not plumbed in. `IsSubscriber` *is* passed through, so Subscribers-only and subscriber bonuses work (once entries can happen at all).

## Files & Code

| File | Role |
|---|---|
| [GiveawayView.xaml](../../src/VyralBot.UI/Views/GiveawayView.xaml) | The Giveaways page — create form, giveaway list, entries grid, action buttons. |
| [GiveawayViewModel.cs](../../src/VyralBot.UI/ViewModels/GiveawayViewModel.cs) | Page logic: form state, create/start/draw/cancel/refresh commands, stats. |
| [GiveawayService.cs](../../src/VyralBot.Core/Services/GiveawayService.cs) | The engine: lifecycle, entry validation, weighted winner drawing. |
| [GiveawayCommandService.cs](../../src/VyralBot.Core/Services/GiveawayCommandService.cs) | Chat-command adapter: `!giveaway` subcommands and custom entry keywords. |
| [Giveaway.cs](../../src/VyralBot.Data/Entities/Giveaway.cs) | `Giveaway` entity and the `GiveawayStatus` enum. |
| [GiveawayEntry.cs](../../src/VyralBot.Data/Entities/GiveawayEntry.cs) | `GiveawayEntry` entity (one row per participant). |
| [DapperDataService.cs](../../src/VyralBot.Data/DapperDataService.cs) | SQLite persistence; `Giveaways` table at `DapperDataService.cs:2769`, `GiveawayEntries` at `:2799`. |
| [IDataService.cs](../../src/VyralBot.Data/IDataService.cs) | Repository interface: `SaveGiveawayAsync`, `GetActiveGiveawayAsync`, `GetGiveawayEntryCountAsync`, etc. |
| [ChatViewModel.cs](../../src/VyralBot.UI/ViewModels/ChatViewModel.cs) | Intended command routing into the giveaway engine (currently dead). |
| [MainViewModel.cs](../../src/VyralBot.UI/ViewModels/MainViewModel.cs) | Navigation: `NavigateToGiveawaysAsync` at `MainViewModel.cs:1125`. |

The weighted pool construction, the heart of the drawing logic:

```csharp
// GiveawayService.cs:226-245 — each entry contributes EntryCount tickets
var entryPool = new List<GiveawayEntry>();
foreach (var entry in entries)
{
    for (int i = 0; i < entry.EntryCount; i++)
        entryPool.Add(entry);
}

var winners = new List<string>();
var winnerEntries = new HashSet<Guid>(); // Track to prevent duplicate winners

int winnersToDraw = Math.Min(giveaway.WinnerCount, entries.Count);

for (int i = 0; i < winnersToDraw; i++)
{
    GiveawayEntry? winnerEntry = null;
    int attempts = 0;
    const int maxAttempts = 100;
    while (winnerEntry == null && attempts < maxAttempts)
    {
        int randomIndex = _random.Next(entryPool.Count);
        var candidate = entryPool[randomIndex];
        if (!winnerEntries.Contains(candidate.Id))
            winnerEntry = candidate;
        attempts++;
    }
    // ...mark IsWinner/WonAt, add to winners
}
```

And the entry-eligibility gate that the two known issues flow through:

```csharp
// GiveawayService.cs:132-147 — follower/sub/watchtime checks at entry time
if (giveaway.FollowersOnly && !isFollower)
    return (false, "You must be a follower to enter this giveaway.");

if (giveaway.SubscribersOnly && !isSubscriber)
    return (false, "You must be a subscriber to enter this giveaway.");

if (giveaway.MinWatchtimeMinutes > 0 && watchtimeMinutes < giveaway.MinWatchtimeMinutes)
    return (false, $"You need at least {giveaway.MinWatchtimeMinutes} minutes of watchtime to enter.");
```

## Data & Settings

Everything lives in the per-profile SQLite database (see [Data Layer](../architecture/data-layer.md) and the [database reference](../reference/database-reference.md)):

- **Giveaways** — one row per giveaway: `Name`, `Description`, `EntryKeyword`, `Status` (0=Pending, 1=Active, 2=Completed, 3=Cancelled), `CreatedAt/StartedAt/EndedAt`, `ScheduledEndTime` (stored, never acted on), `MaxEntries` (null = unlimited), `WinnerCount`, `SubscriberBonusEnabled/Entries`, `MinWatchtimeMinutes`, `AllowedPlatforms`, `FollowersOnly`, `SubscribersOnly`, `Winners` (comma-joined names), `TotalEntries`, `UniqueParticipants`.
- **GiveawayEntries** — one row per participant: `GiveawayId`, `Username`, `Platform`, `PlatformUserId` (always null from the chat path), `EnteredAt`, `EntryCount` (1 + subscriber bonus), `IsSubscriber`, `IsFollower`, `WatchtimeMinutes` (snapshot at entry), `Message`, `IsWinner`, `WonAt`.

There are no giveaway-specific config keys; all settings are per-giveaway columns.

## Status & Known Issues

- **The chat command pipeline is dead** — `ChatMessageReceivedEvent` is subscribed to but never published anywhere, so `!giveaway ...` and the entry keyword never reach the engine and no entries can be recorded from chat. Management from the UI (create/start/draw/cancel) still works; the entry list will simply stay empty. Tracked in the [known issues ledger](../reference/known-issues.md).
- **Followers-only permanently rejects everyone** — `isFollower` is hard-coded `false` at the call site (`ChatViewModel.cs:699`), so every entry to a Followers-only giveaway is declined. Leave the toggle off. Tracked in the [known issues ledger](../reference/known-issues.md).
- **Min watchtime permanently rejects everyone** — `watchtimeMinutes` is hard-coded `0` at the same call site (`ChatViewModel.cs:700`), so any `MinWatchtimeMinutes > 0` declines every entry, even though the data exists in the [Watchtime](watchtime.md) tables. Keep it at 0.
- **Max Entries form default is 1** — `NewGiveawayMaxEntries` is a non-nullable `int` defaulting to 1 (`GiveawayViewModel.cs:46`), so a giveaway created without touching the field allows exactly one entry, and "unlimited" cannot be expressed from the UI even though the column supports null.
- **Scheduled end time is inert** — `ScheduledEndTime` is stored and displayed but no background job ends the giveaway when it passes.
- **Replies only go to Twitch/Kick** — entries from other platforms would be recorded, but the bot's response is only sent via the Twitch/Kick send paths (`ChatViewModel.cs:705-712`).
- **No overlay output** — winners are announced in chat and in the page's status bar only; there is no giveaway browser-source page (see [Browser Sources](browser-sources.md)).
