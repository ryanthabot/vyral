# Statistics

> An analytics page over historical platform events — with the critical caveat that nothing in the app ever writes the table it reads, so it is always empty.

## Overview

The Statistics page aggregates past stream events (follows, subs, raids, cheers, and so on) into an event-count grid, per-event top-contributor leaderboards, and an all-time top-users list, with a date range, platform, and event-type filter plus CSV export.

The single most important fact about this page: **it reads the `EventStatistics` table, and no code anywhere in the app ever writes a row into it.** The write method exists in the data layer, but it has zero callers — every event the app processes (follows, subs, raids...) is reacted to in chat, sounds, alerts, and currency, but never recorded into statistics. Until rows are inserted (by hand into the SQLite database, or by future code calling the write path), the Statistics page will show zero events, empty leaderboards, and export a header-only CSV. The [Event History](../chat-features/event-history.md) page reads the same always-empty table.

Everything else about the page — filters, aggregation, export — is fully implemented and would work the moment data exists.

## User Guide

Open **Stream Tools → Statistics** in the sidebar (or the Statistics card on the Tools hub page). The page loads automatically and defaults to the **last 30 days**.

### Filters

| Filter | Values | Notes |
|---|---|---|
| **Start Date / End Date** | Any dates | Defaults to today minus 30 days through today. Applied to every section. |
| **Platform** | All (blank) / Kick / Twitch | Only affects the **Top Contributors** table — the event-count grid and Top Users are not filtered by it (see quirks below). |
| **Event Type** | All / follow / subscribe / giftsub / raid / cheer / host | Switching away from "All" loads the Top Contributors table for that event type. |

Press **Refresh** (or change the platform filter) to reload everything with the current settings.

### What the page shows

- **Event Counts** — a grid of Platform × Event Type rows with a Count each, plus a Total Events figure. In practice this is where you would see "Kick / follow / 214" style rows.
- **Top Contributors** (top 20) — for the selected event type, which users triggered it and how often (`Count`), plus a `TotalValue` summed from the event's data JSON (gift-sub counts, raid viewers, bits, and similar). Hidden while Event Type is "All".
- **Top Users** (top 20) — the users with the most events of any type in the date range, ordered by event count.

### Export to CSV

**Export to CSV** opens a save dialog (default filename `EventStatistics_<timestamp>.csv`) and writes up to 100,000 raw rows within the date range — one row per recorded event with columns `Timestamp, Platform, EventType, Username, EventData, WasAutoReplied`. Because the underlying table is never written, the export currently produces a header-only file.

### Getting data into the page

Since nothing in the app writes `EventStatistics`, the only way to see this page with content today is to insert rows yourself, directly into the active profile's SQLite database (see [Data Layer](../architecture/data-layer.md) for where profiles live). A row looks like:

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

The `EventData` JSON matters for contributor ranking: the keys `count`, `amount`, `viewers`, `bits`, then `value` (checked in that order) are summed into each user's `TotalValue`, which is how "top raiders by viewers brought" or "top gifters by sub count" style leaderboards would work. The [Event Testing](../chat-features/event-testing.md) console is the natural future write point — it already publishes synthetic events that run every other reaction path (sounds, alerts, currency); wiring a `SaveEventStatisticAsync` call into those handlers would light this page up for both tests and real traffic.

## How It Works (Developer)

`StatisticsService` implements `IStatisticsService` (both defined in the same file) and is registered as a singleton at `App.xaml.cs:177`. It is a pure read-side aggregator over `IDataService.GetEventStatisticsAsync`:

- `GetEventCountsByTypeAsync` (`StatisticsService.cs:54-81`) pulls up to 10,000 `EventStatistic` rows and groups them **in memory** by platform → event type, after applying the date range also in memory.
- `GetTopContributorsAsync` (`StatisticsService.cs:83-105`) groups by (username, platform), counts rows, and sums a numeric value extracted from each row's `EventData` JSON by `ExtractValueFromEventData` (`StatisticsService.cs:210-242`), which probes the keys `count`, `amount`, `viewers`, `bits`, then `value`. Rows sort by `TotalValue` when any is non-zero, otherwise by `Count`.
- `GetTopUsersAsync` (`StatisticsService.cs:107-128`) — same grouping across all event types, sorted by count.
- `ExportToCsvAsync` (`StatisticsService.cs:130-152`) — pulls up to 100,000 rows, filters by date, and emits quoted CSV.
- `GetUserStatisticsAsync` and `GetEventCountsByDateAsync` (chart-ready per-day buckets) exist on the interface but are not surfaced by any UI.

**The write side that does not exist.** `IDataService.SaveEventStatisticAsync` (`IDataService.cs:62`) is implemented in `DapperDataService.cs:264` and forwarded by `ProfileAwareDataService.cs:110`, and the `EventStatistics` table is created at `DapperDataService.cs:2397` — but a solution-wide search finds no other reference. No event handler, no service, no ViewModel ever records an event. The fix pattern would be to call `SaveEventStatisticAsync` from the same per-event handlers in `ChatViewModel` that already drive sounds, alerts, and currency bonuses (see [Event Reactions](../chat/event-reactions.md)).

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
| [DapperDataService.cs](../../src/VyralBot.Data/DapperDataService.cs) | SQLite: table creation (`:2397`), reads (`GetEventStatisticsAsync`), and the never-called `SaveEventStatisticAsync` (`:264`). |
| [IDataService.cs](../../src/VyralBot.Data/IDataService.cs) | Repository interface, including the unwired write at `IDataService.cs:62`. |
| [ProfileAwareDataService.cs](../../src/VyralBot.UI/Services/ProfileAwareDataService.cs) | Per-profile forwarding wrapper (`:110`). |
| [ChatViewModel.cs](../../src/VyralBot.UI/ViewModels/ChatViewModel.cs) | The per-event handlers that would be the natural write points (they do not record statistics today). |
| [MainViewModel.cs](../../src/VyralBot.UI/ViewModels/MainViewModel.cs) | Navigation: `NavigateToStatisticsAsync` at `MainViewModel.cs:985`. |

## Data & Settings

- **EventStatistics** — one row per event occurrence: `Platform` ("Kick"/"Twitch"), `EventType` (lowercase strings like "follow", "raid"), `Username`, `EventData` (JSON blob; `count`/`amount`/`viewers`/`bits`/`value` keys are what contributor ranking reads), `Timestamp`, `WasAutoReplied`, `CreatedAt`. Created at `DapperDataService.cs:2397`.

There are no statistics-specific settings. The same table backs the [Event History](../chat-features/event-history.md) page, which is equally empty for the same reason.

## Status & Known Issues

- **EventStatistics is never written — the page is always empty.** `SaveEventStatisticAsync` has zero callers in the solution; every grid, leaderboard, and the CSV export operate on a table nothing populates. This is the defining issue of this page. Tracked in the [known issues ledger](../reference/known-issues.md).
- **The Platform filter only affects Top Contributors** — `GetEventCountsByTypeAsync` and `GetTopUsersAsync` take no platform argument (`StatisticsService.cs:54,107`), so the event-count grid and top-users list ignore the dropdown even though changing it triggers a reload.
- **Event-type filter offers a fixed list** — All, follow, subscribe, giftsub, raid, cheer, host (`StatisticsViewModel.cs:48`); other event types (resub, host variants, reward redeems) are not offered even if rows existed.
- **Aggregation is client-side** — up to 10,000 rows (100,000 for export) are pulled and grouped in memory rather than aggregated in SQL; fine for realistic volumes, worth knowing for very large datasets.
- **Per-day chart data is implemented but unreachable** — `GetEventCountsByDateAsync` has no UI consumer.
