# Statistics

> An analytics page over historical platform events — fed by the event handlers, which now record every event they react to, with one ShowInChat caveat.

## Overview

The Statistics page aggregates past stream events (follows, subs, raids, cheers, and so on) into an event-count grid, per-event top-contributor leaderboards, and an all-time top-users list, with a date range, platform, and event-type filter plus CSV export.

Since the 2026-08-15 fix pass, all 13 event handlers in `ChatViewModel` persist an `EventStatistic` row after reacting to an event (`EventData` = the handler's variable dictionary as JSON; `WasAutoReplied` accurate), so the page fills with real traffic and the CSV export produces real rows. One caveat: the six handlers whose whole chain is gated on Show-in-chat (Kick GiftSub/Host/KicksGifted/RewardRedeemed/StreamStatus + Twitch GiftSub) skip recording when "Show in chat" is off, so those event types are missing from statistics in that case — KI-26 in the [known issues ledger](../reference/known-issues.md). The [Event History](../chat-features/event-history.md) page reads the same table.

## User Guide

Open **Stream Tools → Statistics** in the sidebar (or the Statistics card on the Tools hub page). The page loads automatically and defaults to the **last 30 days**.

### Filters

| Filter | Values | Notes |
|---|---|---|
| **Start Date / End Date** | Any dates | Defaults to today minus 30 days through today. Applied to every section. |
| **Platform** | All (blank) / Kick / Twitch | Applies to the **Top Contributors** table and — since the 2026-08-15 fix — to the **event-count grid and Top Users** list too. |
| **Event Type** | All / follow / subscribe / giftsub / raid / cheer / host | Switching away from "All" loads the Top Contributors table for that event type. |

Press **Refresh** (or change the platform filter) to reload everything with the current settings.

### What the page shows

- **Event Counts** — a grid of Platform × Event Type rows with a Count each, plus a Total Events figure. In practice this is where you would see "Kick / follow / 214" style rows.
- **Top Contributors** (top 20) — for the selected event type, which users triggered it and how often (`Count`), plus a `TotalValue` summed from the event's data JSON (gift-sub counts, raid viewers, bits, and similar). Hidden while Event Type is "All".
- **Top Users** (top 20) — the users with the most events of any type in the date range, ordered by event count.

### Export to CSV

**Export to CSV** opens a save dialog (default filename `EventStatistics_<timestamp>.csv`) and writes up to 100,000 raw rows within the date range — one row per recorded event with columns `Timestamp, Platform, EventType, Username, EventData, WasAutoReplied`. Every event the handlers react to is in there, so the export reflects real traffic (minus the KI-26 ShowInChat-off gap for the six gated event types).

### Getting data into the page

Real events are recorded automatically. Rows can also be inserted directly into the active profile's SQLite database for testing (see [Data Layer](../architecture/data-layer.md) for where profiles live). A row looks like:

```sql
INSERT INTO EventStatistics (Id, Platform, EventType, Username, EventData, Timestamp, WasAutoReplied, CreatedAt)
VALUES (
  lower(hex(randomblob(16))),
  'Twitch', 'raid', 'someviewer',
  '{"viewers": 42}',
  '2026-01-15T20:30:00Z', 0,
  '2026-01-15T20:30:00Z'
);
```

The `EventData` JSON matters for contributor ranking: the keys `count`, `amount`, `viewers`, `bits`, then `value` (checked in that order) are summed into each user's `TotalValue`, which is how "top raiders by viewers brought" or "top gifters by sub count" style leaderboards work — and since the writers serialize the handlers' own variable dictionaries, those keys are present in real rows. The [Event Testing](../chat-features/event-testing.md) console publishes synthetic events through the real handlers, so test events are recorded here too (subject to the KI-26 ShowInChat caveat).

## How It Works (Developer)

`StatisticsService` implements `IStatisticsService` (both defined in the same file) and is registered as a singleton at `App.xaml.cs:177`. It is a pure read-side aggregator over `IDataService.GetEventStatisticsAsync`:

- `GetEventCountsByTypeAsync` (`StatisticsService.cs:54-81`) pulls up to 10,000 `EventStatistic` rows and groups them **in memory** by platform → event type, after applying the date range and platform filter also in memory.
- `GetTopContributorsAsync` (`StatisticsService.cs:83-105`) groups by (username, platform), counts rows, and sums a numeric value extracted from each row's `EventData` JSON by `ExtractValueFromEventData` (`StatisticsService.cs:210-242`), which probes the keys `count`, `amount`, `viewers`, `bits`, then `value`. Rows sort by `TotalValue` when any is non-zero, otherwise by `Count`.
- `GetTopUsersAsync` (`StatisticsService.cs:107-128`) — same grouping across all event types, sorted by count; the platform filter is applied here too (fixed 2026-08-15).
- `ExportToCsvAsync` (`StatisticsService.cs:130-152`) — pulls up to 100,000 rows, filters by date, and emits quoted CSV.
- `GetUserStatisticsAsync` and `GetEventCountsByDateAsync` (chart-ready per-day buckets) exist on the interface but are not surfaced by any UI.

**The write side.** `IDataService.SaveEventStatisticAsync` (`IDataService.cs:62`), implemented in `DapperDataService.cs:264` and forwarded by `ProfileAwareDataService.cs:110`, is called by all 13 per-event handlers in `ChatViewModel` (the same handlers that drive sounds, alerts, currency, and auto-replies — see [Event Reactions](../chat/event-reactions.md)), with the handler's variable dictionary serialized into `EventData` and `WasAutoReplied` set from the auto-reply outcome. The six ShowInChat-gated handlers skip the write when "Show in chat" is off (KI-26).

```csharp
// StatisticsService.cs:83-99 — top contributors: group, count, and sum event values
var userStats = stats
    .GroupBy(s => new { s.Username, s.Platform })
    .Select(g => new UserStatistic
    {
        Username = g.Key.Username,
        Platform = g.Key.Platform,
        EventType = eventType,
        Count = g.Count(),
        TotalValue = g.Sum(s => ExtractValueFromEventData(s.EventData))
    })
    .OrderByDescending(u => u.TotalValue > 0 ? u.TotalValue : u.Count)
    .Take(limit)
    .ToList();
```

## Files & Code

| File | Role |
|---|---|
| [StatisticsView.xaml](../../src/VyralBot.UI/Views/StatisticsView.xaml) | The Statistics page — filter bar, event-count grid, contributor and top-user tables, export button. |
| [StatisticsViewModel.cs](../../src/VyralBot.UI/ViewModels/StatisticsViewModel.cs) | Page logic: filters (defaults at `StatisticsViewModel.cs:60-63`), loading, CSV save dialog. |
| [StatisticsService.cs](../../src/VyralBot.Core/Services/Statistics/StatisticsService.cs) | Aggregation engine + `IStatisticsService` + `UserStatistic`/`UserStatisticDetail` models. |
| [EventStatistic.cs](../../src/VyralBot.Data/Entities/EventStatistic.cs) | `EventStatistic` entity — the row shape the whole page depends on. |
| [DapperDataService.cs](../../src/VyralBot.Data/DapperDataService.cs) | SQLite: table creation (`:2397`), reads (`GetEventStatisticsAsync`), and `SaveEventStatisticAsync` (`:264`, called by the event handlers). |
| [IDataService.cs](../../src/VyralBot.Data/IDataService.cs) | Repository interface, including the unwired write at `IDataService.cs:62`. |
| [ProfileAwareDataService.cs](../../src/VyralBot.UI/Services/ProfileAwareDataService.cs) | Per-profile forwarding wrapper (`:110`). |
| [ChatViewModel.cs](../../src/VyralBot.UI/ViewModels/ChatViewModel.cs) | The per-event handlers that write the statistics rows. |
| [MainViewModel.cs](../../src/VyralBot.UI/ViewModels/MainViewModel.cs) | Navigation: `NavigateToStatisticsAsync` at `MainViewModel.cs:985`. |

## Data & Settings

- **EventStatistics** — one row per event occurrence: `Platform` ("Kick"/"Twitch"), `EventType` (lowercase strings like "follow", "raid"), `Username`, `EventData` (JSON blob; `count`/`amount`/`viewers`/`bits`/`value` keys are what contributor ranking reads), `Timestamp`, `WasAutoReplied`, `CreatedAt`. Created at `DapperDataService.cs:2397`.

There are no statistics-specific settings. The same table backs the [Event History](../chat-features/event-history.md) page.

## Status & Known Issues

- **EventStatistics is written by the event handlers — the page has real data.** Grids, leaderboards, and the CSV export all operate on live rows (KI-3 resolved 2026-08-15). Tracked in the [known issues ledger](../reference/known-issues.md).
- **ShowInChat-off skips recording for 6 event types** — Kick GiftSub/Host/KicksGifted/RewardRedeemed/StreamStatus and Twitch GiftSub leave no rows when their "Show in chat" toggle is off (KI-26). [known issues ledger](../reference/known-issues.md)
- **The Platform filter applies to every section** — event-count grid, Top Contributors, and Top Users (fixed 2026-08-15).
- **Event-type filter offers a fixed list** — All, follow, subscribe, giftsub, raid, cheer, host (`StatisticsViewModel.cs:48`); other event types (resub, reward redeems, stream status) are recorded but not selectable in the filter.
- **Aggregation is client-side** — up to 10,000 rows (100,000 for export) are pulled and grouped in memory rather than aggregated in SQL; fine for realistic volumes, worth knowing for very large datasets.
- **Per-day chart data is implemented but unreachable** — `GetEventCountsByDateAsync` has no UI consumer.
