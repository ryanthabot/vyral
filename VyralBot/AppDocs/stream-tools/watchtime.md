# Watchtime

> A per-viewer activity tracker that ticks up minutes for viewers seen in chat, feeds giveaway/poll min-watchtime eligibility, and awards watch-time currency — fully live since the command pipeline was wired up.

## Overview

The Watchtime tool tracks how long each viewer has been "watching" and shows a dashboard of totals, top viewers, and currently-active viewers. It is also the bridge between hanging out in chat and earning currency: tracked watchtime awards currency through the configured [Currency](currency.md) watch-time settings, and it gates [Giveaways](giveaways.md) and [Polls](polls.md) min-watchtime eligibility (real minutes are checked at entry/vote time).

Two things define the system:

1. **"Watchtime" is really chat-activity time.** A viewer is tracked only when they send a chat message, and they stay "active" for 5 minutes after their last message. A viewer who lurks silently for six hours without typing is never counted at all.
2. **Earning is configurable — and the default changed.** Watchtime currency earning now honors the Currency settings' `WatchTimeEnabled` / `WatchTimeAmount` / `WatchTimeInterval` (plus the full multiplier stack). **BEHAVIOR CHANGE (2026-08-15):** the seeded defaults award **10 points per minute** where older builds hard-coded 1 per minute. Streamers who want the old rate should set Points Per Interval to 1 on the Currency Settings tab (KI-30 in the [known issues ledger](../reference/known-issues.md)).

Recording is live on every platform: the per-message activity call sits in the generic chat-message handler, which is fed by all three chat integrations.

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

The `!watchtime` family is implemented and live in chat (see [Commands](../chat-features/commands.md) and the [command catalog](../reference/command-catalog.md)):

| Command | Effect |
|---|---|
| `!watchtime` | Your own total watchtime and your time in the current stream, e.g. "@alice, Total watchtime: 3h 20m \| This stream: 45 minutes". |
| `!watchtime <user>` | Another user's total (the `@` is optional). |
| `!watchtime top [n]` | Leaderboard of the top *n* viewers by total minutes (also `!watchtime leaderboard` / `!watchtime lb`). Defaults to 5, capped at 10. |

Times are formatted progressively: `45 minutes`, then `3h 20m`, then `2d 5h`.

## How It Works (Developer)

Three pieces: `WatchtimeService` (the tracker), `WatchtimeBackgroundService` (the ticker), and `WatchtimeCommandService` (the chat adapter). Registration is at `App.xaml.cs:137-139`, with `WatchtimeBackgroundService` registered via `AddHostedService`.

**Activity recording.** `RecordUserActivityAsync(username, platform, streamId)` (`WatchtimeService.cs:33-71`) upserts a `UserWatchtime` row: new viewers get a row with `IsActive = true`; existing viewers get `LastSeenAt = DateTime.UtcNow` and `IsActive = true`. If a non-null `streamId` differs from the row's `LastStreamId`, the session counter resets — the production call still passes no stream ID, so session resets come from the manual button.

**The tick.** `WatchtimeBackgroundService.ExecuteAsync` (`WatchtimeBackgroundService.cs:25-59`) loops every **1 minute**, calling `IncrementActiveWatchtimeAsync(1)`, and every **2 minutes** calls `DeactivateInactiveViewersAsync()`. `IncrementActiveWatchtimeAsync` loads every viewer with `IsActive = true`, adds 1 to `TotalMinutes` and `StreamSessionMinutes`, saves each row, and awards currency through the configured earning path: the `CurrencySetting` toggles (`WatchTimeEnabled` / `WatchTimeAmount` / `WatchTimeInterval`) are honored, and the full multiplier stack (platform × boost × tier × role, `Math.Floor`) is applied. The award goes through the currency service rather than a raw `AddCurrencyAsync`, so the enable toggle actually gates it.

**The inactivity threshold.** `DeactivateInactiveViewersAsync` (`WatchtimeService.cs:113-118`) flips `IsActive` to false for everyone whose `LastSeenAt` is older than 5 minutes (`_inactiveThreshold`, `WatchtimeService.cs:17`).

**The live input.** The production caller of `RecordUserActivityAsync` sits inside `ChatViewModel.OnChatMessageReceivedAsync` (`ChatViewModel.cs:794`), which consumes the platform-agnostic [ChatMessageReceivedEvent](../../src/VyralBot.Core/Models/ChatMessageReceivedEvent.cs) published by all three chat integrations — every Twitch/Kick/YouTube message marks its sender active. `GetTotalMinutesAsync(username, platform)` (`WatchtimeService.cs:135`) is the read used by giveaway/poll eligibility. See [Event Bus](../architecture/event-bus.md).

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
| [ChatMessageReceivedEvent.cs](../../src/VyralBot.Core/Models/ChatMessageReceivedEvent.cs) | The platform-agnostic event whose handler records activity per message |
| [ChatViewModel.cs](../../src/VyralBot.UI/ViewModels/ChatViewModel.cs) | The per-message activity call at `ChatViewModel.cs:794` (live) plus the `!watchtime` routing. |
| [MainViewModel.cs](../../src/VyralBot.UI/ViewModels/MainViewModel.cs) | Navigation: `NavigateToWatchtimeAsync` at `MainViewModel.cs:1195`. |

The activity upsert that runs on every chat message now:

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

The earning rate is configured on the [Currency](currency.md) Settings tab: `WatchTimeEnabled`, `WatchTimeAmount` (default 10), `WatchTimeInterval` (default 60 s), with the full multiplier stack applied per award. The 5-minute inactivity threshold and the 1-minute tick interval remain hard-coded constants (`WatchtimeService.cs:17-18`, `WatchtimeBackgroundService.cs:14`).

## Status & Known Issues

- **Recording is live** — every chat message on Twitch/Kick/YouTube marks its sender active; the ticker increments totals and awards configured currency (KI-1 resolved 2026-08-15). Tracked in the [known issues ledger](../reference/known-issues.md).
- **Watchtime is chat activity, not viewing time** — only viewers who send messages are tracked; silent viewers are invisible to the system. This is by design of the current implementation, not a bug per se.
- **The earning rate default changed (KI-30)** — configurable now (`WatchTimeAmount`/`WatchTimeInterval`, plus multipliers), but the seeded default is 10 points per minute where older builds hard-coded 1. Set Points Per Interval to 1 on the Currency Settings tab to restore the old rate.
- **Eligibility gating is wired** — giveaways and polls receive real `GetTotalMinutesAsync` totals (see [Giveaways](giveaways.md) / [Polls](polls.md)); their min-watchtime settings work.
- **Session reset via stream ID never triggers** — the chat call passes no `streamId`, so `LastStreamId`-based session resets never happen; use the **Reset Stream Session** button.
- **`!watchtime` commands are live** — the command service answers from chat, and the seeded built-in row defers to it (no shadowing).
