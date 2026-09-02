# Event History

> A filterable, paginated browser for past stream events (follows, subs, raids, ...) with per-record details and an auto-reply previewer — now fed by the live event handlers, which record a row for every event they react to.

## Overview

Event History is your logbook: every follow, subscription, raid, cheer, and other tracked event, recorded with platform, user, timestamp, and event-specific data (bits amount, sub tier, viewer count...). The page gives you filters (platform, event type, username, date range), pagination, a details viewer for the raw event data, and a "View auto-reply" tool that reconstructs the auto-reply message an event produced.

Since the 2026-08-15 fix pass, all 13 event handlers in `ChatViewModel` persist an `EventStatistic` row after reacting to an event (`EventData` = the handler's variable dictionary as JSON; `WasAutoReplied` set from the actual auto-reply outcome), so the page fills with real traffic. One caveat: the six handlers whose whole reaction chain is gated on Show-in-chat (Kick GiftSub/Host/KicksGifted/RewardRedeemed/StreamStatus + Twitch GiftSub) skip recording when "Show in chat" is off for that event — those event types only appear in history when the chat message is enabled (KI-26 in the [known issues ledger](../reference/known-issues.md)).

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
- **Auto-Replied** — whether the event produced an auto-reply; set by the writer from the actual auto-reply outcome.
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

The entity is [EventStatistic.cs](../../src/VyralBot.Data/Entities/EventStatistic.cs): `Platform`, `EventType`, `Username`, `EventData` (JSON), `Timestamp`, `WasAutoReplied`.

**The write side.** Each of the 13 per-event handlers in `ChatViewModel` (the same handlers that run sounds, alerts, currency, and auto-replies — see [Event Reactions](../chat/event-reactions.md)) calls `SaveEventStatisticAsync` with the handler's variable dictionary serialized into `EventData` and `WasAutoReplied` set from the auto-reply step's outcome. The [Statistics](../stream-tools/statistics.md) page reads the same table. The write happens inside each handler like this:

```csharp
// after the reaction chain (incl. the auto-reply decision):
await _dataService.SaveEventStatisticAsync(new EventStatistic
{
    Platform = "Kick",
    EventType = "Follow",
    Username = evt.Username,
    EventData = JsonSerializer.Serialize(variables),   // the handler's variable dict
    Timestamp = evt.Timestamp,
    WasAutoReplied = replyWasSent
});
```

The six ShowInChat-gated handlers (Kick GiftSub/Host/KicksGifted/RewardRedeemed/StreamStatus, Twitch GiftSub) return before this call when "Show in chat" is off, so those event types leave no row in that case (KI-26).

Note the event-type vocabulary: the page filters (and the live handlers) use `Follow`, `Subscribe`, `Resub`, `GiftSub`, `Raid`, `Host`, `Cheer`, `KicksGifted`, `RewardRedeemed`, `StreamStatus` — while the [Conditional Rules](conditional-rules.md) page offers a different list (`Subscription`, `Bits`, ...). Any writer should stick to the handler vocabulary or the filters will not match.

## Files & Code

| File | Role |
|---|---|
| [EventHistoryViewModel.cs](../../src/VyralBot.UI/ViewModels/EventHistoryViewModel.cs) | Filters, in-memory filtering, pagination, details + auto-reply previewer |
| [EventHistoryView.xaml](../../src/VyralBot.UI/Views/EventHistoryView.xaml) | Page layout: filter bar, DataGrid, pager |
| [EventHistoryView.xaml.cs](../../src/VyralBot.UI/Views/EventHistoryView.xaml.cs) | Code-behind |
| [EventHistoryConverters.cs](../../src/VyralBot.UI/Converters/EventHistoryConverters.cs) | Display converters for the grid |
| [EventStatistic.cs](../../src/VyralBot.Data/Entities/EventStatistic.cs) | Entity: platform, event type, username, event data JSON, timestamp, auto-replied flag |
| [DapperDataService.cs](../../src/VyralBot.Data/DapperDataService.cs) | `EventStatistics` table + indexes (lines 2395-2410), `SaveEventStatisticAsync` (line 264), `GetEventStatisticsAsync` |
| [ChatViewModel.cs](../../src/VyralBot.UI/ViewModels/ChatViewModel.cs) | The 13 event handlers that write the rows |
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
- Written by the 13 event handlers in `ChatViewModel`; no seeder and no page-specific settings.

## Status & Known Issues

- **The page now has real data** — every event that reaches an event handler is recorded, with an accurate `WasAutoReplied`. — [known issues ledger](../reference/known-issues.md) (KI-3 resolved)
- **ShowInChat-off skips recording for 6 event types** — Kick GiftSub/Host/KicksGifted/RewardRedeemed/StreamStatus and Twitch GiftSub leave no history rows when their "Show in chat" toggle is off (KI-26). — [known issues ledger](../reference/known-issues.md)
- Filtering happens **in memory after fetching the whole table** — fine at realistic volumes, a scaling concern for very long histories.
- "View auto-reply" is a **previewer**, not a re-send: nothing is posted to chat, and it does not pass through the 30-second rate limiter that the live path uses.
- The previewer substitutes only `{username}`, `{platform}`, `{eventtype}` and five EventData keys; other placeholders are left verbatim.
- The platform filter offers only Kick and Twitch; if other platforms' events were ever recorded they would be unreachable through the UI filter.

### Inspecting the data behind the page

Rows can also be inserted directly into the profile database (`appdata/profiles/<Name>/data/vyralbot.db`, see [Data Layer](../architecture/data-layer.md)):

```sql
INSERT INTO EventStatistics (Id, Platform, EventType, Username, EventData, Timestamp, WasAutoReplied, CreatedAt)
VALUES (lower(hex(randomblob(16))), 'Kick', 'Follow', 'TestUser123',
        '{"count": 43}', datetime('now'), 0, datetime('now'));
```

Reload the page and the row appears with working filters, Details, and the auto-reply previewer. Rows added this way are also picked up by the Statistics page. The [Event Testing](event-testing.md) console fires the same *events* end-to-end through the real handlers — and since the writers live in those handlers, test events are now recorded here too (subject to the KI-26 ShowInChat caveat for the six gated types).
