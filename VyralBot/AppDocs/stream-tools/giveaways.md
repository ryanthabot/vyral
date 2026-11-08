# Giveaways

> Cross-platform chat giveaways with a weighted drawing engine — fully built on the management side and live from chat since the command pipeline was wired up.

## Overview

The Giveaways tool lets a streamer create a giveaway in the app, start it, let viewers enter by typing a keyword in chat (for example `!enter`), and then draw one or more winners with a weighted random draw. Entries can come from any platform the bot is connected to, and subscribers can be given extra "tickets" so they win proportionally more often.

What works end to end (since the 2026-08-15 fix pass): chat entries via `!giveaway enter` or the custom keyword, real eligibility checks, the full lifecycle from the UI (Start / Draw Winners / Cancel / Delete), the weighted drawing logic, and persistence in the per-profile database. On top of that: per-giveaway **odds boosts** driven by [account linking](../chat-features/account-linking.md), a **live-updating page** (event-debounced reloads), and **adaptive entry intake** that batches under flood traffic.

Two eligibility caveats remain:

1. **Followers-only is platform-asymmetric.** On **Twitch** the follower check is a real Helix `channels/followers` lookup (5-minute cache; requires the stored token's `moderator:read:followers` scope — it degrades to `false` without it, KI-33). On **every other platform** the check falls back to the **recorded viewer status**: Kick follow webhooks (`KickFollowEvent`) mark a viewer followed while the [webhook server](../settings/web-server-ngrok.md) is receiving events, so Followers-only *is* enforceable on Kick once a follow has been seen — but a Kick viewer who followed before the webhooks were live (or while the app was down) is still rejected until a follow event records them. YouTube has no follow concept at all, so eligibility there stays `false` (KI-31, partially resolved).
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
| **Enable follower/sub-tier odds boosts** (toggle) | Per-giveaway odds boost: extra entries from sub tier, follower-anywhere, and linked-platform count — see [Odds boosts](#odds-boosts). | Off |
| **Min Watchtime (minutes)** | Viewers need at least this much tracked watchtime to enter. Works — real watchtime is checked at entry time. | `0` |
| **Followers Only** (toggle) | Only followers may enter. Works on **Twitch** (real Helix check, needs the `moderator:read:followers` scope) and on **Kick** through the recorded viewer status (a follow webhook must have marked the viewer); always rejects on YouTube. | Off |
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
7. **🗑 Delete** removes a non-active giveaway and all its entries after a confirmation dialog. Deleting an **Active** giveaway is refused with "Cancel the giveaway before deleting it" — an active giveaway's live entry intake would be orphaned — so cancel first, then delete.

The list, entries grid, and counters update themselves: every giveaway event (start, entry, winners drawn, cancel, intake-mode switch) funnels into a debounced reload (1 second), so you no longer need to press Refresh to see new entries arrive. Refresh still forces an immediate reload.

### Odds boosts

With **Enable follower/sub-tier odds boosts** checked on a giveaway, each entry's ticket count grows from three cross-platform facts resolved through [account linking](../chat-features/account-linking.md):

| Boost | Default | Source |
|---|---|---|
| Sub tier 1 / 2 / 3 (best tier across linked platforms) | +1 / +2 / +3 | `Odds.Tier1/2/3BonusEntries` |
| Follower (follows on any linked platform) | +1 | `Odds.FollowerBonusEntries` |
| Each extra linked platform (beyond the first, capped at 3) | +1 each | `Odds.LinkedPlatformBonusEntries` + `Odds.LinkedPlatformBonusCap` |

A sample breakdown the engine produces: `Tier 3 (+3), Follower (+1), 3 linked platforms (+2)`. With odds boosts on, the sub reward comes from the tier ladder only (there is no separate "Sub" part — a current subscriber with no recorded tier counts as tier 1). The breakdown is stored on the entry row (prefixed to the entry's message) so you can audit why an entry had the ticket count it did. The legacy **Subscriber Bonus** still applies on its own when the odds toggle is off — and stacks with it when both are on.

The thresholds and bonuses are config keys (30-second cache) — defaults above; see [Data & Settings](#data--settings).

### Adaptive entry intake (Live ↔ Timed)

Under a giveaway flood, entry intake switches modes automatically so the chat pipeline stays responsive:

- **Live** (normal) — every `!enter` is validated and persisted immediately, exactly as it always was.
- **Timed** (high traffic) — when the entry rate reaches **120 entries/minute** (`Giveaway.LiveMaxEntriesPerMinute`), new entries are queued in memory (up to 10,000) and flushed to the database in batches every **10 seconds** (`Giveaway.TimedFlushSeconds`; a 500-entry backlog forces an earlier flush). Duplicate entries within a batch collapse to the latest request. Only the duplicate-entry check is re-run at persist time (the queue may hold stale requests) — full eligibility was validated when the entry was accepted into the queue.
- **Back to Live** — when the rate falls to **60 entries/minute or less** (`Giveaway.TimedResumeEntriesPerMinute`), intake switches back and any pending batch is flushed immediately, so a draw always sees every entry.

Chatters are kept in the loop on every connected platform: switching to Timed announces "⏱ High traffic — giveaway entries are being batched every few seconds." and returning to Live announces "⚡ Traffic normalized — giveaway entries are live again." The page's intake badge and list update through the same debounced reload as everything else.

### Chat commands

These commands are implemented by the giveaway engine and available to chat (see [Commands](../chat-features/commands.md) and the [command catalog](../reference/command-catalog.md)):

| Command | Who | Effect |
|---|---|---|
| `!giveaway start` | Moderators | Starts the first Pending giveaway; announces the entry keyword. |
| `!enter` (or the giveaway's custom keyword) | Everyone | Enters the active giveaway. Also `!giveaway enter`. |
| `!giveaway winner` / `!giveaway draw` | Moderators | Draws winner(s) and completes the giveaway. |
| `!giveaway redraw` (alias `!giveaway reroll`) | Moderators | Re-draws the **most recent completed** giveaway, excluding the previous winners — the "winner never responded, reroll it" path. Previous winners' flags are cleared first (so `IsWinner` always reflects the *latest* draw only), new winners are drawn from the remaining entries with the **same EntryCount weighting**, the giveaway's winner list is replaced, and a winners-drawn event fires (alert/reload plumbing reacts like any draw). Refuses anything but a Completed giveaway, and a "no non-winner entries to re-draw from" attempt changes nothing. |
| `!giveaway cancel` | Moderators | Cancels the active giveaway. |
| `!giveaway status` | Everyone | Reports entries, participants, and winner count for the active giveaway. |

Unauthorized use of a moderator command gets a visible "Only moderators can..." reply. Bot responses are only ever sent back to Twitch or Kick; an entry accepted from another platform gets recorded, but its confirmation reply is dropped.

Mods assigned a tier in the [mod system](../architecture/moderator-guide.md) can also run giveaways from the private mod channel: `!mod startgiveaway <name> <winnerCount> <keyword>`, `!mod cancelgiveaway`, and `!mod draw` — every action audited and visible in the viewer profiles and the Moderator page's action log.

## How It Works (Developer)

Two Core services split the responsibility: `GiveawayService` is the engine, `GiveawayCommandService` is the chat-command adapter. Both are registered as singletons in `App.xaml.cs` (registration at `App.xaml.cs:129-130`).

**Lifecycle.** A `Giveaway` moves `Pending → Active → Completed|Cancelled`. `StartGiveawayAsync` (`GiveawayService.cs:66`) refuses to start unless the status is Pending and `GetActiveGiveawayAsync()` returns null — a single-active invariant enforced in code. `DrawWinnersAsync` (`GiveawayService.cs:207`) is the only path to Completed; `CancelGiveawayAsync` (`GiveawayService.cs:291`) accepts Pending or Active.

**Entry validation.** `EnterGiveawayAsync` (`GiveawayService.cs:100-202`) checks, in order: giveaway exists and is Active; platform is in the allow-list (comma-split, case-insensitive, `All` bypasses); follower requirement; subscriber requirement; minimum watchtime; no existing entry for the (username, platform) pair; and the optional max-entries cap. It then computes `EntryCount = 1 + SubscriberBonusEntries` when the bonus is enabled and the user is a subscriber, saves the entry, and refreshes denormalized `TotalEntries` / `UniqueParticipants` counters on the giveaway row.

**Weighted drawing.** `DrawWinnersAsync` expands every entry into its `EntryCount` tickets in an in-memory pool, then draws up to `min(WinnerCount, entries.Count)` unique winners, re-rolling duplicates with a cap of 100 attempts per winner. Winning rows get `IsWinner = true` and `WonAt`; the giveaway stores winners as a comma-joined `"name (platform)"` string and is marked Completed.

**Race safety.** Draws, redraws and the odds-recalculation path are serialized through a `SemaphoreSlim(1,1)` **draw gate** (`_drawGate`, `GiveawayService.cs:126`) — two mods typing `!giveaway draw` and `!giveaway redraw` at once, or a redraw racing the UI's Draw button, queue up instead of interleaving reads and writes of the winner flags. `RedrawWinnersAsync` (`:473`) additionally un-flags the previous winners *before* recording the new ones inside the same gate hold, so `IsWinner` never momentarily shows two generations. The chat-facing `!giveaway redraw` (alias `reroll`, moderator-only) targets the most recent **Completed** giveaway — a live giveaway cannot be redrawn; draw it first.

**Odds boosts.** When the giveaway has `OddsBoostEnabled`, the persist path resolves the viewer identity via `ILinkedAccountService` and the cross-platform aggregates via `IViewerStatusService` (best linked sub tier, follower-anywhere, linked-platform count), then computes bonus entries with the static `GiveawayOdds.Calculate` — which returns both the total and the human-readable breakdown ("Tier 3 (+3), Follower (+1), 3 linked platforms (+2)"). The breakdown is prefixed onto the entry's `Message` column, so the audit trail and the ticket math live on the same row. Config values come through a 30-second cached `OddsSettings` read.

**Adaptive intake.** `GiveawayService` keeps a rolling `EntryRateWindow`. In Live mode each `EnterGiveawayAsync` validates and persists synchronously. Crossing the Live threshold flips the internal mode to Timed, publishes `GiveawayIntakeModeChangedEvent`, and routes entries into a `ConcurrentQueue` drained by a lazily-started flush loop (batched every `Giveaway.TimedFlushSeconds`, early-flush at a 500-entry backlog, per-(username, platform) dedupe keeping the latest request, 10,000-entry queue cap with graceful rejection beyond it). Falling to the resume threshold flips back, flushes the pending batch immediately, and publishes the event again. `ChatViewModel` subscribes to the event and announces both transitions to every connected platform.

**Live UI.** `GiveawayViewModel` subscribes to the giveaway event family (started, entry, winners drawn, cancelled, intake-mode changed) and funnels all of them into a single 1-second debounced dispatcher reload — bursts of entries produce one refresh, not one per message. Deletion is blocked in the view model for Active giveaways before it ever reaches `DeleteGiveawayAsync`, which removes the giveaway and its entries together.

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
                         // other platforms: the recorded viewer status (Kick follow
                         // webhooks mark followers; YouTube stays false)
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

- **Giveaways** — one row per giveaway: `Name`, `Description`, `EntryKeyword`, `Status` (0=Pending, 1=Active, 2=Completed, 3=Cancelled), `CreatedAt/StartedAt/EndedAt`, `ScheduledEndTime` (stored, never acted on), `MaxEntries` (null = unlimited), `WinnerCount`, `SubscriberBonusEnabled/Entries`, `OddsBoostEnabled`, `MinWatchtimeMinutes`, `AllowedPlatforms`, `FollowersOnly`, `SubscribersOnly`, `Winners` (comma-joined names), `TotalEntries`, `UniqueParticipants`.
- **GiveawayEntries** — one row per participant: `GiveawayId`, `Username`, `Platform`, `PlatformUserId` (always null from the chat path), `EnteredAt`, `EntryCount` (1 + subscriber bonus + odds-boost bonuses), `IsSubscriber`, `IsFollower`, `WatchtimeMinutes` (snapshot at entry), `Message` (with the odds breakdown prefixed, e.g. `Tier 2 (+2), Follower (+1); <viewer message>`), `IsWinner`, `WonAt`.

Giveaway-specific config keys (read through the service's 30-second cache):

| Key | Default | Meaning |
|---|---|---|
| `Odds.Tier1BonusEntries` / `Tier2BonusEntries` / `Tier3BonusEntries` | 1 / 2 / 3 | Bonus entries for best linked sub tier 1/2/3. |
| `Odds.FollowerBonusEntries` | `1` | Bonus entries when the viewer follows on any linked platform. |
| `Odds.LinkedPlatformBonusEntries` | `1` | Bonus entries per linked platform beyond the first. |
| `Odds.LinkedPlatformBonusCap` | `3` | Maximum extra platforms that earn the bonus. |
| `Giveaway.LiveMaxEntriesPerMinute` | `120` | Entry rate that switches intake to Timed. |
| `Giveaway.TimedResumeEntriesPerMinute` | `60` | Rate at which intake switches back to Live. |
| `Giveaway.TimedFlushSeconds` | `10` | Batch flush interval in Timed mode. |

## Status & Known Issues

- **The chat command pipeline is live** — `!giveaway ...` and the entry keyword reach the engine from real chat on all connected platforms, and entries are recorded (KI-1 resolved 2026-08-15). Tracked in the [known issues ledger](../reference/known-issues.md).
- **Followers-only is platform-asymmetric** — real Helix follower check on Twitch (requires the stored token's `moderator:read:followers` scope — KI-33; degrades to `false` without it); on non-Twitch platforms the gate consults the recorded viewer status, so Kick followers are honored once a `KickFollowEvent` webhook has recorded them, while YouTube (no follow concept) still passes `false` (KI-31, partially resolved). Tracked in the [known issues ledger](../reference/known-issues.md).
- **`!giveaway redraw` (alias `reroll`) re-draws completed giveaways** — moderator-only, most recent Completed giveaway, previous winners excluded, same weighting, previous flags cleared inside the draw gate (see [Race safety](#how-it-works-developer)); a no-eligible-entries redraw changes nothing.
- **Min-watchtime works** — real watchtime from [Watchtime](watchtime.md) is checked at entry time on every platform.
- **Max Entries: 0 = unlimited** — the form defaults to `0`, which the engine treats as uncapped; a positive value caps total entries.
- **Scheduled end time is inert** — `ScheduledEndTime` is stored and displayed but no background job ends the giveaway when it passes.
- **Delete is blocked while Active** — the button refuses with "Cancel the giveaway before deleting it"; cancel first, then delete.
- **The page updates itself** — giveaway events drive a 1-second debounced reload of the list, entries, and counters; Refresh forces an immediate reload.
- **Adaptive intake is automatic** — Timed batching (10-second flushes) engages at 120 entries/min and releases at ≤60/min, with chat announcements on both transitions; only the duplicate-entry check is re-run at persist time (eligibility was validated at intake).
- **Odds boosts resolve through account linking** — tier/follower/linked-platform boosts need the viewer identity; an unlinked viewer only gets the sub bonus (if any) and the base ticket.
- **Replies only go to Twitch/Kick** — entries from other platforms are recorded, but the bot's response is only sent via the Twitch/Kick send paths.
- **No overlay output** — winners are announced in chat and in the page's status bar only; there is no giveaway browser-source page (see [Browser Sources](browser-sources.md)).
