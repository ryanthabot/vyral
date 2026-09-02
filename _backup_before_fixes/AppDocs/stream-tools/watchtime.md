# Watchtime

> A per-viewer activity tracker that ticks up minutes for viewers seen in chat — currently recording nothing, because its only input call sits inside the dead generic chat pipeline.

## Overview

The Watchtime tool tracks how long each viewer has been "watching" and shows a dashboard of totals, top viewers, and currently-active viewers. It is also the bridge between hanging out in chat and earning currency: every minute of tracked watchtime awards 1 point through the [Currency](currency.md) system, and watchtime was intended to gate [Giveaways](giveaways.md) and [Polls](polls.md) eligibility (that gating is not wired — see those pages).

Two honest caveats define the current state:

1. **"Watchtime" is really chat-activity time.** A viewer is tracked only when they send a chat message, and they stay "active" for 5 minutes after their last message. A viewer who lurks silently for six hours without typing is never counted at all.
2. **Nothing is being recorded right now.** The one production call that marks a viewer active (`RecordUserActivityAsync` on every chat message) sits inside the generic chat-message handler that is dead in this build — no code publishes the event it listens to. The background ticker still runs, but it increments an empty active-viewer set, so totals stay at zero until the pipeline is fixed.

The dashboard itself works, and the manual **Reset Stream Session** button works.

## User Guide

Open **Stream Tools → Watchtime** in the sidebar (or the Watchtime card on the Tools hub page). The page is a read-only dashboard with three tabs and no editing — watchtime rows are created purely by activity.

### The tabs

| Tab | What it shows |
|---|---|
| **Top Viewers** | The viewers with the most total minutes. A number box above the list sets how many to show (1–100; the list reloads as you type). Defaults to 10. |
| **Active Viewers** | Viewers considered active right now (seen within the last 5 minutes), with a chip at the top showing the total count. |
| **All Viewers** | Every tracked viewer sorted by total minutes — the full leaderboard. |

Selecting a viewer anywhere shows a detail panel with their record: username, platform, total minutes, current-session minutes, first seen, last seen, and active flag.

### The controls

- **Refresh** — reloads all three lists and the active count from the database.
- **Reset Stream Session** — zeroes the *Stream Session Minutes* counter for every viewer at once. Use it when you start a new stream so "This stream:" times begin from zero. Total minutes are never touched. (Rows also self-reset their session when a new `streamId` is passed in, but no caller currently supplies one, so this button is the working path.)

### Chat commands

The `!watchtime` family is implemented and would be available to chat once the command pipeline is live (see [Commands](../chat-features/commands.md) and the [command catalog](../reference/command-catalog.md)):

| Command | Effect |
|---|---|
| `!watchtime` | Your own total watchtime and your time in the current stream, e.g. "@alice, Total watchtime: 3h 20m \| This stream: 45 minutes". |
| `!watchtime <user>` | Another user's total (the `@` is optional). |
| `!watchtime top [n]` | Leaderboard of the top *n* viewers by total minutes (also `!watchtime leaderboard` / `!watchtime lb`). Defaults to 5, capped at 10. |

Times are formatted progressively: `45 minutes`, then `3h 20m`, then `2d 5h`.

## How It Works (Developer)

Three pieces: `WatchtimeService` (the tracker), `WatchtimeBackgroundService` (the ticker), and `WatchtimeCommandService` (the chat adapter). Registration is at `App.xaml.cs:137-139`, with `WatchtimeBackgroundService` registered via `AddHostedService`.

**Activity recording.** `RecordUserActivityAsync(username, platform, streamId)` (`WatchtimeService.cs:33-71`) upserts a `UserWatchtime` row: new viewers get a row with `IsActive = true`; existing viewers get `LastSeenAt = DateTime.UtcNow` and `IsActive = true`. If a non-null `streamId` differs from the row's `LastStreamId`, the session counter resets — but the only production call passes no stream ID, so this path never fires from chat.

**The tick.** `WatchtimeBackgroundService.ExecuteAsync` (`WatchtimeBackgroundService.cs:25-59`) loops every **1 minute**, calling `IncrementActiveWatchtimeAsync(1)`, and every **2 minutes** calls `DeactivateInactiveViewersAsync()`. `IncrementActiveWatchtimeAsync` (`WatchtimeService.cs:76-108`) loads every viewer with `IsActive = true`, adds 1 to `TotalMinutes` and `StreamSessionMinutes`, saves each row, and — the currency integration — calls `ICurrencyService.AddCurrencyAsync(username, platform, 1, "Watchtime reward")` for each. This is an independent 1-point-per-minute award on top of (actually instead of — see below) the configurable watchtime earning in `CurrencySetting`.

**The inactivity threshold.** `DeactivateInactiveViewersAsync` (`WatchtimeService.cs:113-118`) flips `IsActive` to false for everyone whose `LastSeenAt` is older than 5 minutes (`_inactiveThreshold`, `WatchtimeService.cs:17`).

**The dead input.** The single production caller of `RecordUserActivityAsync` is at `ChatViewModel.cs:794`, inside `OnChatMessageReceivedAsync` — the same handler that routes commands and is subscribed to `ChatMessageReceivedEvent` (`ChatViewModel.cs:220`). That event is defined at `ChatViewModel.cs:2871` and published by nothing, so no viewer is ever marked active, and the minute ticker has no one to increment. See [Event Bus](../architecture/event-bus.md).

```csharp
// WatchtimeService.cs:76-99 — the tick: minutes + the currency award
public async Task IncrementActiveWatchtimeAsync(int minutes = 1)
{
    var activeViewers = await _dataService.GetActiveViewersAsync();
    var count = 0;

    foreach (var viewer in activeViewers)
    {
        viewer.TotalMinutes += minutes;
        viewer.StreamSessionMinutes += minutes;
        await _dataService.SaveUserWatchtimeAsync(viewer);

        // Award currency if currency service is available
        if (_currencyService != null)
        {
            try
            {
                // Award 1 currency per minute watched (configurable later)
                await _currencyService.AddCurrencyAsync(viewer.Username, viewer.Platform, minutes, "Watchtime reward");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to award currency for watchtime: {Username}", viewer.Username);
            }
        }
        count++;
    }
    // ...
}
```

## Files & Code

| File | Role |
|---|---|
| [WatchtimeView.xaml](../../src/VyralBot.UI/Views/WatchtimeView.xaml) | The Watchtime page — Top/Active/All viewers tabs, detail panel, refresh and reset buttons. |
| [WatchtimeViewModel.cs](../../src/VyralBot.UI/ViewModels/WatchtimeViewModel.cs) | Page logic: list loading, top-viewer limit (1–100), active count, session reset. |
| [WatchtimeService.cs](../../src/VyralBot.Core/Services/WatchtimeService.cs) | The tracker: activity upserts, minute increments, currency award, inactivity threshold, formatting. |
| [WatchtimeBackgroundService.cs](../../src/VyralBot.Core/Services/WatchtimeBackgroundService.cs) | Hosted service ticking every 1 minute (increment) and 2 minutes (deactivate stale viewers). |
| [WatchtimeCommandService.cs](../../src/VyralBot.Core/Services/WatchtimeCommandService.cs) | Chat-command adapter for `!watchtime` and the leaderboard. |
| [UserWatchtime.cs](../../src/VyralBot.Data/Entities/UserWatchtime.cs) | `UserWatchtime` entity. |
| [DapperDataService.cs](../../src/VyralBot.Data/DapperDataService.cs) | SQLite persistence; `UserWatchtime` table at `DapperDataService.cs:2870`. |
| [IDataService.cs](../../src/VyralBot.Data/IDataService.cs) | Repository interface: `GetUserWatchtimeAsync`, `GetActiveViewersAsync`, `GetTopWatchtimeUsersAsync`, `DeactivateInactiveViewersAsync`, etc. |
| [ChatViewModel.cs](../../src/VyralBot.UI/ViewModels/ChatViewModel.cs) | The (dead) per-message activity call at `ChatViewModel.cs:794`. |
| [MainViewModel.cs](../../src/VyralBot.UI/ViewModels/MainViewModel.cs) | Navigation: `NavigateToWatchtimeAsync` at `MainViewModel.cs:1195`. |

The activity upsert that never runs in the current build:

```csharp
// WatchtimeService.cs:55-70 — existing viewer path of RecordUserActivityAsync
else
{
    // Update last seen time and mark as active
    watchtime.LastSeenAt = DateTime.UtcNow;
    watchtime.IsActive = true;

    // If stream ID changed, reset session minutes (new stream)
    if (!string.IsNullOrEmpty(streamId) && streamId != watchtime.LastStreamId)
    {
        _logger.LogInformation("Stream ID changed for {Username} - resetting session", username);
        watchtime.StreamSessionMinutes = 0;
        watchtime.LastStreamId = streamId;
    }

    await _dataService.SaveUserWatchtimeAsync(watchtime);
}
```

## Data & Settings

One table in the per-profile SQLite database (see [Data Layer](../architecture/data-layer.md) and the [database reference](../reference/database-reference.md)):

- **UserWatchtime** — one row per (username, platform): `Username`, `Platform`, `TotalMinutes`, `StreamSessionMinutes`, `FirstSeenAt`, `LastSeenAt`, `LastStreamId` (nullable; never set by the current chat path), `IsActive`.

There are no watchtime-specific config keys. The 5-minute inactivity threshold and the 1-minute tick interval are hard-coded constants (`WatchtimeService.cs:17-18`, `WatchtimeBackgroundService.cs:14`), and the 1-currency-per-minute award is hard-coded in `IncrementActiveWatchtimeAsync` — it is *not* the `WatchTimeInterval`/`WatchTimeAmount` pair from the [Currency](currency.md) settings (that configurable path, `CurrencyService.AwardWatchTimeAsync`, has no callers).

## Status & Known Issues

- **Nothing is recorded right now** — the only activity call (`ChatViewModel.cs:794`) is inside the dead `ChatMessageReceivedEvent` pipeline, so no viewer ever becomes active, the ticker increments nobody, and the dashboard stays empty. Tracked in the [known issues ledger](../reference/known-issues.md).
- **Watchtime is chat activity, not viewing time** — even when working, only viewers who send messages are tracked; silent viewers are invisible to the system. This is by design of the current implementation, not a bug per se. Tracked in the [known issues ledger](../reference/known-issues.md).
- **The intended eligibility gating is not wired** — giveaways and polls receive `watchtimeMinutes: 0` regardless of what this tracker knows (see [Giveaways](giveaways.md) / [Polls](polls.md)); their min-watchtime settings therefore reject everyone.
- **Session reset via stream ID never triggers** — the chat call passes no `streamId`, so `LastStreamId`-based session resets never happen; use the **Reset Stream Session** button.
- **`!watchtime` commands are dead with the pipeline** — the command service is complete but unreachable from chat in this build; note also that a seeded built-in `!watchtime` row in the unified Commands table would shadow this service's response once the pipeline is re-wired (the unified command path returns first).
- **Currency award is unconditional** — the 1-point-per-minute award runs for every active viewer regardless of any [Currency](currency.md) settings toggle, because it calls `AddCurrencyAsync` directly rather than going through a gated earning method.
