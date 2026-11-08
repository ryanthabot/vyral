# FirstTimeChatterService (first-time-chatter detection)

Welcome-newcomer retention feature: publishes `FirstTimeChatterEvent` the first
time a viewer ever chats, so the app can greet them (welcome alerts/replies and
workflows).

- Source: `src/VyralBot.Core/Services/Chat/FirstTimeChatterService.cs`
  (`IFirstTimeChatterService` in the same file)
- Event record: `FirstTimeChatterEvent` in
  `src/VyralBot.Core/Services/EventBus/Events/ViewerEvents.cs`
- DI: singleton + hosted lifetime (`App.xaml.cs` — the generic host calls
  `StartAsync`/`StopAsync`, the `WatchtimeBackgroundService` precedent; no
  per-profile lifecycle because the handler is read-only against the active
  profile's `IDataService`).

## Detection semantics

1. Subscribes to the platform-agnostic `ChatMessageReceivedEvent` (published by
   every chat integration).
2. **Burst dedup first** — a bounded in-memory map (`"platform|username"` → last
   sighting, 60-second window, pruned past 1024 entries) skips repeated
   sightings entirely: no query, no second fire. This also coalesces DB checks
   for regular chatters to at most one per minute per viewer.
3. **Source of truth** — the viewer is FIRST-TIME when
   `IDataService.HasAnyActivityAsync(username)` returns false: no
   `UserWatchtime` row AND no `UserCurrency` row on ANY platform (single
   read-only `SELECT EXISTS(...) OR SELECT EXISTS(...)`, case-insensitive
   username). Those are exactly the rows the app's own pipelines create the
   first time a viewer participates (`WatchtimeService.RecordUserActivityAsync`
   runs on every chat message; currency rows appear with the first point).
4. **No row creation** — the check is read-only BY DESIGN: if it created rows,
   the lookup itself would mark every newcomer "known" and the event would stop
   firing. The watchtime recorder (a parallel handler for the same message)
   creates the durable row, so the event fires exactly once per viewer.
5. Publishes `FirstTimeChatterEvent` and logs an info line. Every failure is
   logged and swallowed — detection must never disturb the chat pipeline.

## Event shape (`FirstTimeChatterEvent : EventBase`)

| Property | Type | Notes |
|---|---|---|
| `Platform` | `string` (required) | "Twitch", "Kick", "YouTube", ... |
| `Username` | `string` (required) | login name of the newcomer |
| `Message` | `string` (required) | first message's raw text |
| `DisplayName` | `string?` | null from live detection (`ChatMessageReceivedEvent` carries no display name); the test publisher fills it |

Plus `EventId`/`Timestamp`/`Source`/`Metadata` from `EventBase` (`Source` =
platform).

## Consumers

- **WorkflowTriggerService** (`src/VyralBot.UI/Services/WorkflowTriggerService.cs`):
  dispatches the canonical workflow event key **`firstmessage`** (aliases
  `firstchatter`, `firsttimechatter`, `first message` — separator stripping
  handles the spaced/underscored spellings). Variables: `username`,
  `platform`, `message` (context) + `displayName`. The event-type list in the
  flow editor (`FlowEditorViewModel.TriggerEventTypeOptions`) offers
  `firstmessage`.
- **ChatViewModel** welcome alert/reply: subscribes to `FirstTimeChatterEvent`
  (parallel wiring; this service only publishes).
- **Test fire**: `TestEventPublisher` id **`firstmessage.fire`** publishes a
  Twitch-shaped `FirstTimeChatterEvent` directly (bypassing detection), wired
  to the Event Testing page's Twitch card ("First Message" button).

## Data-layer addition

`IDataService.HasAnyActivityAsync(username)` (implemented in
`DapperDataService`, forwarded by `ProfileAwareDataService`): read-only EXISTS
pair over `UserWatchtime` + `UserCurrency` — never creates or mutates rows.
