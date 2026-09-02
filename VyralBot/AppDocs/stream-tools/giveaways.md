# Giveaways

> Cross-platform chat giveaways with a weighted drawing engine — fully built on the management side and live from chat since the command pipeline was wired up.

## Overview

The Giveaways tool lets a streamer create a giveaway in the app, start it, let viewers enter by typing a keyword in chat (for example `!enter`), and then draw one or more winners with a weighted random draw. Entries can come from any platform the bot is connected to, and subscribers can be given extra "tickets" so they win proportionally more often.

What works end to end (since the 2026-08-15 fix pass): chat entries via `!giveaway enter` or the custom keyword, real eligibility checks, the full lifecycle from the UI (Start / Draw Winners / Cancel), the weighted drawing logic, and persistence in the per-profile database.

Two eligibility caveats remain:

1. **Followers-only works on Twitch only.** The follower check is a real Helix `channels/followers` lookup for Twitch (5-minute cache; requires the stored token's `moderator:read:followers` scope — it degrades to `false` without it). KickLib 1.8.0 has no followers API and YouTube has no follower concept, so Followers-only still rejects everyone on those platforms (KI-31).
2. **Min-watchtime works everywhere.** Real watchtime is resolved through the [Watchtime](watchtime.md) service at entry time — this restriction is fully live.

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
| **Max Entries** | Cap on total entries. `0` (the default) means **unlimited** — the hint reads "Max Total Entries (0 = unlimited)"; any positive number caps total entries. | `0` (unlimited) |
| **Subscriber Bonus** (toggle) | Gives subscribers extra entries. | Off |
| **Subscriber Bonus Entries** | Extra entries a subscriber gets (on top of their 1). | `5` |
| **Min Watchtime (minutes)** | Viewers need at least this much tracked watchtime to enter. Works — real watchtime is checked at entry time. | `0` |
| **Followers Only** (toggle) | Only followers may enter. Works on **Twitch** (real Helix check, needs the `moderator:read:followers` scope); always rejects on Kick/YouTube. | Off |
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

These commands are implemented by the giveaway engine and available to chat (see [Commands](../chat-features/commands.md) and the [command catalog](../reference/command-catalog.md)):

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

**Command routing (live).** `ChatViewModel.OnChatMessageReceivedAsync` orchestrates: for any `!`-prefixed message it calls `GiveawayCommandService.ProcessCommandAsync`, which handles `!giveaway <subcommand>` plus any custom keyword matching the active giveaway's `EntryKeyword` (`GiveawayCommandService.cs:39-55`). The orchestrator consumes the platform-agnostic [ChatMessageReceivedEvent](../../src/VyralBot.Core/Models/ChatMessageReceivedEvent.cs) published by all three chat integrations, with MessageId dedupe and a bot-echo skip. See [Event Bus](../architecture/event-bus.md).

```csharp
// ChatViewModel.cs — the giveaway call with real eligibility data
var response = await _giveawayCommandService.ProcessCommandAsync(
    evt.Message,
    evt.Username,
    evt.Platform,
    evt.IsModerator,
    evt.IsSubscriber,
    isFollower,          // Twitch: real Helix channels/followers check (5-min cache);
                         // Kick/YouTube: false (no followers API / no follower concept)
    watchtimeMinutes);   // real total minutes from WatchtimeService
```

`IsSubscriber` is passed through, so Subscribers-only and subscriber bonuses work. The follower check's scope requirement (Twitch) and the Kick/YouTube gaps are tracked as [KI-31](../reference/known-issues.md) and [KI-33](../reference/known-issues.md).

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
| [ChatViewModel.cs](../../src/VyralBot.UI/ViewModels/ChatViewModel.cs) | Command routing into the giveaway engine (live) plus the eligibility lookups. |
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

And the entry-eligibility gates the eligibility data now flows through:

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

- **The chat command pipeline is live** — `!giveaway ...` and the entry keyword reach the engine from real chat on all connected platforms, and entries are recorded (KI-1 resolved 2026-08-15). Tracked in the [known issues ledger](../reference/known-issues.md).
- **Followers-only is Twitch-only** — real Helix follower check on Twitch (requires the stored token's `moderator:read:followers` scope — KI-33; degrades to `false` without it); hard `false` on Kick/YouTube because no followers API exists there (KI-31). Tracked in the [known issues ledger](../reference/known-issues.md).
- **Min-watchtime works** — real watchtime from [Watchtime](watchtime.md) is checked at entry time on every platform.
- **Max Entries: 0 = unlimited** — the form defaults to `0`, which the engine treats as uncapped; a positive value caps total entries.
- **Scheduled end time is inert** — `ScheduledEndTime` is stored and displayed but no background job ends the giveaway when it passes.
- **Replies only go to Twitch/Kick** — entries from other platforms are recorded, but the bot's response is only sent via the Twitch/Kick send paths.
- **No overlay output** — winners are announced in chat and in the page's status bar only; there is no giveaway browser-source page (see [Browser Sources](browser-sources.md)).
