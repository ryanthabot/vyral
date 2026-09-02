# Event History

> A filterable, paginated browser for past stream events (follows, subs, raids, ...) with per-record details and an auto-reply previewer — reading a table that no code ever writes, so the page is always empty in the current build.

## Overview

Event History is meant to be your logbook: every follow, subscription, raid, cheer, and other tracked event, recorded with platform, user, timestamp, and event-specific data (bits amount, sub tier, viewer count...). The page gives you filters (platform, event type, username, date range), pagination, a details viewer for the raw event data, and a "View auto-reply" tool that reconstructs the auto-reply message an event produced.

The honest status: **the page is permanently empty.** It reads the `EventStatistics` table, and the write method for that table (`SaveEventStatisticAsync`) is implemented but has **zero callers** anywhere in the app — no event handler ever records a row. Unless rows are inserted into the database externally, every filter returns nothing. The UI itself is complete and would work if the data existed.

## User Guide

Open **Chat Features → Event History** in the sidebar.

### Filters (top bar)

| Control | Options | Notes |
|---|---|---|
| **Start Date / End Date** | Date pickers | Defaults to the **last 7 days**. |
| **Platform** | `All`, `Kick`, `Twitch` | No YouTube option. |
| **Event Type** | `All`, `Follow`, `Subscribe`, `Resub`, `GiftSub`, `Raid`, `Host`, `Cheer`, `KicksGifted`, `RewardRedeemed`, `StreamStatus` | Matches the Kick + Twitch event vocabulary. |
| **Username** | Free text | Substring, case-insensitive match. |

Changing platform or event type reloads immediately. **Search** applies all filters (and resets to page 1); **Clear Filters** restores the defaults.

### The list

A grid of events, newest first, showing Timestamp, Platform, Event Type, Username, an **Auto-Replied** yes/no column, and per-row actions. The list is paginated at **50 rows per page** with Previous/Next controls and a total count.

Column semantics:

- **Timestamp** — when the event was recorded (UTC), formatted `yyyy-MM-dd HH:mm:ss`.
- **Auto-Replied** — whether the event produced an auto-reply. The flag would be set by the writer at record time; in the current build it can only ever show "No" (see below).
- **Actions** — the Details and View auto-reply buttons described next.

Pagination notes: with zero matching rows the total count is 0 and there is a single empty page; filters reset to page 1 on Search and on Clear Filters, and platform/event-type changes reload from whatever page you were on.

### Row actions

- **Details** — a MessageBox showing the full record: platform, event type, username, timestamp, whether an auto-reply was sent, and the record's `EventData` JSON pretty-printed.
- **View auto-reply** — an auto-reply *previewer*: it loads the [auto-reply](auto-replies.md) configured for that platform/event type, substitutes `{username}`, `{platform}`, `{eventtype}` plus `count`/`amount`/`viewers`/`bits`/`months` from the record's `EventData`, and shows the resulting message in a MessageBox. Despite the command name it **does not re-send** anything to chat.

## How It Works (Developer)

[EventHistoryViewModel.cs](../../src/VyralBot.UI/ViewModels/EventHistoryViewModel.cs) drives the page. `LoadEventsAsync`:

1. Calls `IDataService.GetEventStatisticsAsync()` — **fetches the full table** (the method accepts optional platform/eventType/limit parameters, but the page passes none).
2. Applies platform, event type, username-substring, and date-range filters **in memory** over the fetched list.
3. Sorts newest-first, computes totals, then paginates client-side (`PageSize = 50`).

`ViewDetails` pretty-prints the `EventData` JSON via `JsonDocument`. `ReplayAutoReplyAsync` does its own plain `string.Replace` substitution (it does **not** use `AutoReplyService.ProcessTemplate` or the [variables engine](variables.md)) and warns if no auto-reply is configured for that platform/event.

The entity is [EventStatistic.cs](../../src/VyralBot.Data/Entities/EventStatistic.cs): `Platform`, `EventType`, `Username`, `EventData` (JSON), `Timestamp`, `WasAutoReplied`. The `WasAutoReplied` flag would be set by whoever writes rows — and since nothing writes them, it can only ever be `false`.

**The gap:** `SaveEventStatisticAsync` is declared in `IDataService` (`IDataService.cs:62`), implemented in `DapperDataService.cs:264`, and forwarded by `ProfileAwareDataService.cs:110` — a solution-wide search finds **no other reference**. None of the live event handlers in `ChatViewModel` (which do run sounds, alerts, currency, and auto-replies for the same events — see [Event Reactions](../chat/event-reactions.md)) ever persist an `EventStatistic`. The [Statistics](../stream-tools/statistics.md) page reads the same never-written table, so it is empty for the same reason.

Reviving the page means calling `SaveEventStatisticAsync` from the per-event handlers in `ChatViewModel` (with the event's payload serialized into `EventData`), ideally setting `WasAutoReplied` after the auto-reply step. Sketch of the insertion point — every handler follows this shape (e.g. `OnKickFollowAsync` at `ChatViewModel.cs:1700`):

```csharp
// after the auto-reply step decides whether to send:
await _dataService.SaveEventStatisticAsync(new EventStatistic
{
    Platform = "Kick",
    EventType = "Follow",
    Username = evt.Username,
    EventData = JsonSerializer.Serialize(new { count = evt.FollowerCount }),
    Timestamp = evt.Timestamp,
    WasAutoReplied = replyWasSent
});
```

Note the event-type vocabulary: the page filters (and the live handlers) use `Follow`, `Subscribe`, `Resub`, `GiftSub`, `Raid`, `Host`, `Cheer`, `KicksGifted`, `RewardRedeemed`, `StreamStatus` — while the [Conditional Rules](conditional-rules.md) page offers a different list (`Subscription`, `Bits`, ...). Any writer should stick to the handler vocabulary or the filters will not match.

## Files & Code

| File | Role |
|---|---|
| [EventHistoryViewModel.cs](../../src/VyralBot.UI/ViewModels/EventHistoryViewModel.cs) | Filters, in-memory filtering, pagination, details + auto-reply previewer |
| [EventHistoryView.xaml](../../src/VyralBot.UI/Views/EventHistoryView.xaml) | Page layout: filter bar, DataGrid, pager |
| [EventHistoryView.xaml.cs](../../src/VyralBot.UI/Views/EventHistoryView.xaml.cs) | Code-behind |
| [EventHistoryConverters.cs](../../src/VyralBot.UI/Converters/EventHistoryConverters.cs) | Display converters for the grid |
| [EventStatistic.cs](../../src/VyralBot.Data/Entities/EventStatistic.cs) | Entity: platform, event type, username, event data JSON, timestamp, auto-replied flag |
| [DapperDataService.cs](../../src/VyralBot.Data/DapperDataService.cs) | `EventStatistics` table + indexes (lines 2395-2410), `SaveEventStatisticAsync` (line 264, never called), `GetEventStatisticsAsync` |
| [StatisticsService.cs](../../src/VyralBot.Core/Services/Statistics/StatisticsService.cs) | Reads the same table for the Statistics page |

The auto-reply previewer's substitution (plain replaces, not the template engine):

```csharp
var message = setting.AutoReplyMessage
    .Replace("{username}", evt.Username)
    .Replace("{platform}", evt.Platform)
    .Replace("{eventtype}", evt.EventType);

// then, from the record's EventData JSON:
//   count, amount, viewers, bits, months
```

## Data & Settings

- **EventStatistics** table (per-profile SQLite, see [Database Reference](../reference/database-reference.md)): `Id`, `Platform`, `EventType`, `Username`, `EventData` (JSON), `Timestamp`, `WasAutoReplied`, `CreatedAt`. Indexed on Platform, EventType, Username, Timestamp.
- No seeder, no runtime writer, no settings — the table can only gain rows if some future code (or an external tool) inserts them.

## Status & Known Issues

- **The page is always empty** — `SaveEventStatisticAsync` has zero callers, so no code ever records events. Filters, pagination, details, and the previewer all function, but over zero rows. — [known issues ledger](../reference/known-issues.md)
- The same gap empties the [Statistics](../stream-tools/statistics.md) page (same table, read by `StatisticsService`).
- Filtering happens **in memory after fetching the whole table** — fine while the table is empty, a scaling concern once a writer exists.
- "View auto-reply" is a **previewer**, not a re-send: nothing is posted to chat, and it does not pass through the 30-second rate limiter that the live path uses.
- The previewer substitutes only `{username}`, `{platform}`, `{eventtype}` and five EventData keys; other placeholders (and `@`-prefixed ones — see the stray-`@` note on [Auto-Replies](auto-replies.md)) are left verbatim.
- The platform filter offers only Kick and Twitch; if YouTube events were ever recorded they would be unreachable through the UI filter.

### Verifying the UI despite the gap

The page itself works — you can prove it by inserting rows directly into the profile database (`appdata/profiles/<Name>/data/vyralbot.db`, see [Data Layer](../architecture/data-layer.md)):

```sql
INSERT INTO EventStatistics (Id, Platform, EventType, Username, EventData, Timestamp, WasAutoReplied, CreatedAt)
VALUES (lower(hex(randomblob(16))), 'Kick', 'Follow', 'TestUser123',
        '{"count": 43}', datetime('now'), 0, datetime('now'));
```

Reload the page and the row appears with working filters, Details, and the auto-reply previewer. Rows added this way are also picked up by the Statistics page. The [Event Testing](event-testing.md) console fires the same *events* end-to-end, but does not write this table.

### Revival checklist (developer)

1. Call `SaveEventStatisticAsync` from each per-event handler in `ChatViewModel` (sketch above).
2. Use the handler event-type vocabulary (`Follow`, `GiftSub`, `Cheer`, ...) so the filter list matches.
3. Set `WasAutoReplied` from the auto-reply step's outcome.
4. Consider passing the platform/event filters down to `GetEventStatisticsAsync` instead of filtering in memory once real volumes exist.
