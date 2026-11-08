# Session Summary Service

Stream session summaries — the post-stream report every streamer wants
(wave 16). `SessionSummaryService` watches live/offline transitions, and when
a stream ends it compiles a summary from existing data, persists it (last 20)
and publishes `SessionEndedEvent` for future consumers.

## Source Files
| File | Description |
|------|-------------|
| `src/VyralBot.Core/Services/Statistics/SessionSummaryService.cs` | Interface (`ISessionSummaryService`), `StreamSessionSummary`/`SessionChatterSummary` models, pure `SessionSummaryCompiler`, the service |
| `src/VyralBot.Core/Services/EventBus/Events/SessionEvents.cs` | `SessionEndedEvent` (carries the compiled summary) |
| `src/VyralBot.UI/App.xaml.cs` | Singleton + hosted-lifecycle registration (FirstTimeChatterService precedent) |
| `src/VyralBot.UI/ViewModels/StatisticsViewModel.cs` | Sessions tab state (Last session card + Recent sessions list) |
| `tests/VyralBot.UI.Tests/Wave16SessionSummaryTests.cs` | Compiler, duration math, persistence round-trip, lifecycle tests |

## Session definition

A session OPENS when the first platform goes live and ENDS when the last live
platform goes offline (multi-platform simulcast = one session). The signal is
`IStreamLiveRegistry.StreamLiveChanged` (fed by the Twitch/Kick/YouTube
stream-status events) — the service subscribes itself, so there is no
MainViewModel hook. Sessions shorter than 1 minute are discarded (the YouTube
live-chat state machine can flap). On app shutdown with a stream still live,
`StopAsync` compiles a best-effort summary from the data up to that moment.

## Data sources (compile time)

| Number | Source | Notes |
|--------|--------|-------|
| New followers / subs (Subscribe+Resub+GiftSub) / raids / cheers + full per-type breakdown | `IDataService.GetEventStatisticsAsync(start, end)` | Window-filtered EventStatistics rows; EventType matched OrdinalIgnoreCase (SQL `UPPER(EventType)` parity) |
| Raid viewers / bits | EventData JSON value sums | Same `count → amount → viewers → bits → value` precedence as `StatisticsService.ExtractValueFromEventData`, so numbers agree with the Statistics page |
| Currency awarded | `IDataService.GetCurrencyTransactionTotalsSinceAsync(start)` | Earned side of the transaction ledger (SQL-side SUM) |
| Chat messages + top chatter | In-memory per-username counter (`ChatMessageReceivedEvent` subscription) | **Honest omission:** only messages this app instance sees while the session is open; an app restart mid-stream resets the count (no durable per-message counter exists). Event/currency numbers are window-complete from the DB. |

## Persistence

Config-key JSON list: `Statistics.SessionSummaries` (category `Statistics`),
newest first, capped at 20 entries (`StreamSessionSummary` list, indented,
case-insensitive reads). Corrupt stored JSON logs a warning and starts a fresh
list — summaries are derived data, every number is recomputable from the
EventStatistics/currency tables.

## Event shape

```csharp
public record SessionEndedEvent : EventBase
{
    public required StreamSessionSummary Summary { get; init; }
}
```

`StreamSessionSummary`: `StartedAtUtc`, `EndedAtUtc`, `Platforms` (CSV),
`DurationMinutes`, `NewFollowers`, `NewSubs`, `Raids`, `RaidViewers`,
`Cheers`, `Bits`, `ChatMessages`, `CurrencyAwarded`, `TopChatters` (max 3,
ordinal tie-break), `EventCounts` (per-type breakdown). UI-only display
helpers (`DurationText`, `StartedLocalText`, `HeadlineText`) are
`[JsonIgnore]`d.

## Lifecycle & safety

- Registered `AddSingleton<ISessionSummaryService, SessionSummaryService>` +
  `AddHostedService` cast wrapper — started/stopped with the app lifetime, no
  per-profile start/stop.
- Registry handler does only set bookkeeping under a lock; the compile's DB
  IO runs fire-and-forget with its own try/catch (a summary bug must never
  disturb the live-status pipeline).
- `ChatMessageReceivedEvent` handler only increments counters; blank
  username/platform rows are ignored, usernames counted case-insensitively.
- Named handlers everywhere so `StopAsync` truly unsubscribes (zero-orphans).
