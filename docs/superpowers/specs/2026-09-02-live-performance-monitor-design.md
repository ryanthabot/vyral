# Live Performance Monitor (Settings → Performance) — Design

Date: 2026-09-02
Status: Implemented

## Problem

The Settings → Performance category is a "Coming Soon" placeholder. The user
wants live, in-app visibility into what every platform (Twitch, Kick, YouTube,
TikTok, Discord) and every major component (event bus, workflow engine, web
server, child processes) is doing resource-wise on the PC.

Today the only performance instrumentation is `PerformanceMonitor`
(VyralBot.Core), a background service that logs process memory/CPU/GC numbers
to Serilog every 15s. Nothing is exposed to the UI, and nothing is attributed
per platform or per component.

## Goals

1. Live (default 1s refresh) performance view inside Settings → Performance.
2. Process-level metrics: CPU %, working set, private memory, threads,
   handles, GC (per-generation collections, heap, allocation rate),
   thread pool, uptime.
3. Per-platform activity: connection state, events in (by type), chat
   messages in, outgoing sends, last activity time.
4. Per-component activity: EventBus (published/processed rates, queue
   depth, handler errors, subscriptions), workflow engine (runs, runs/s,
   average duration, failures), web server (running state, port, request
   counts + rates, error responses).
5. Child process visibility: VyralBot-owned WebView2 processes and ngrok —
   PID, memory, CPU time.
6. Sparkline history for CPU and memory.
7. Negligible overhead when the tab is closed (sampling is cheap; UI refresh
   timer only runs while the view is loaded).

## Non-Goals

- No historical persistence (no DB writes) — live session only.
- No per-line-code profiling or ETW tracing.
- No ability to kill processes from the UI (display only, v1).
- TikTok has no chat connection in the app yet; its row shows DB connection
  status only.

## Architecture

### 1. `IMetricsRegistry` / `MetricsRegistry` (Core, singleton)

Thread-safe, allocation-cheap counter store used by all instrumentation
points. Keys are (source, metric) string pairs backed by `Interlocked`
operations on `long`.

```csharp
void Increment(string source, string metric, long amount = 1);
void RecordDuration(string source, double durationMs);   // count + total ms
MetricsSnapshot GetSnapshot();                            // immutable copy
```

`MetricsSnapshot` exposes:
- `IReadOnlyDictionary<string, IReadOnlyDictionary<string, long>> Counters`
- `IReadOnlyDictionary<string, (long Count, double TotalMs)> Durations`

### 2. `PerformanceTelemetryService` (Core, BackgroundService, 1s tick)

- Samples every second: process CPU % (TotalProcessorTime delta),
  working set, private bytes, thread count, handle count, GC
  gen0/1/2 collection totals, heap bytes, allocated-bytes rate,
  thread-pool thread/pending counts. Stored in a 180-slot ring buffer
  of immutable `TelemetrySample`s.
- Every 5th tick: scans child processes — `msedgewebview2` filtered to
  VyralBot's `VyralBot_WebView2_Temp` user-data folder (same KI-23 scoping
  as App.xaml.cs cleanup) and `ngrok` — capturing PID, memory, CPU time.
- Exposes `Latest`, `History`, `ChildProcesses`, `StartedUtc`.
- Runs always (registered as hosted service) so counts accumulate even
  when the tab is closed; sampling cost is a few microseconds per tick.

### 3. Instrumentation points (each a few lines)

| Point | What it records |
|---|---|
| `EventBus.PublishAsync` | `EventBus/published`, `{event.Source}/events:{TypeName}` and `{event.Source}/events` |
| `EventBus.ProcessEventAsync` | `EventBus/processed`, `EventBus/handler_errors` |
| `WorkflowTriggerService.ExecuteInBackground` | `Workflows/runs`, duration via `RecordDuration("Workflows", ms)`, `Workflows/failures` |
| `WorkflowMessageSender.SendAsync` | `Sends/{platform}` success + `Sends/{platform}/failed` |
| `WebServerService` middleware (`app.Use` before routing) | `WebServer/requests`, `WebServer/errors` (status ≥ 400) |

Per-platform chat messages in are derived from
`{Platform}/events:ChatMessageReceivedEvent` — no changes needed inside the
integration projects.

Constructors gain optional trailing `IMetricsRegistry?` parameters (same
null-tolerant pattern `WorkflowTriggerService` already uses for the Discord
bridge) so old construction sites and tests keep compiling.

### 4. `PerformanceViewModel` (UI, singleton)

- `DispatcherTimer` (default 1s; 1s/2s/5s selector, pause button).
  `Start()`/`Stop()` are called from the view's `Loaded`/`Unloaded`, so the
  UI refresh cost only exists while the tab is visible.
- Each tick pulls an immutable snapshot from the registry + telemetry
  service and computes rates as deltas against the previous tick.
- Sections exposed as bindable properties/collections:
  - Summary tiles: CPU %, memory, threads, GC total, events/s, workflows/s.
  - `CpuHistory` / `MemoryHistory` `PointCollection`s for sparklines.
  - `PlatformRows`: Twitch, Kick, YouTube, TikTok, Discord — status
    (live `IsConnected` from services / `DiscordBotManager.Connected`, with
    DB `Platform.Status` polled every 5s), events in (total + /s),
    chat msgs in, sends out, last activity.
  - `ComponentRows`: EventBus, Workflows, Web server.
  - `ProcessRows`: child processes from the telemetry service.
  - Uptime, refresh interval, pause state.

### 5. `PerformanceView` (UI, UserControl)

- Header: uptime, pause/resume toggle, refresh-interval selector.
- Summary tiles row + two sparkline charts (CPU, memory).
- Platforms grid, Components grid, Child processes grid.
- Uses only theme `DynamicResource` brushes
  (`BackgroundSecondaryBrush`, `CardBackgroundBrush`, `TextPrimaryBrush`,
  `PrimaryBrush`, …) so it tracks the theme system.
- Code-behind only wires `Loaded`/`Unloaded` → VM start/stop.

### 6. Wiring

- `App.xaml.cs`: register `IMetricsRegistry` singleton,
  `PerformanceTelemetryService` hosted service, `PerformanceViewModel`
  singleton.
- `MainWindow.xaml`: `DataTemplate` for `PerformanceViewModel` →
  `PerformanceView` (same pattern as `VariablesViewModel`).
- `SettingsViewModel`: expose `PerformanceViewModel`; `SettingsView.xaml`
  Performance panel becomes `ContentControl Content="{Binding
  PerformanceViewModel}"` replacing the "Coming Soon" placeholder.

## Error handling

- Every telemetry read is wrapped; a failed sample keeps the previous
  values and logs at debug level. Monitoring must never crash the app.
- Registry getters return immutable copies so the UI never iterates live
  mutable state.
- Missing optional registry (null) makes instrumentation no-ops.

## Testing

- `MetricsRegistryTests` (xunit, tests/VyralBot.UI.Tests): counter
  accumulation across sources, duration averaging, snapshot isolation
  (mutating the registry after taking a snapshot doesn't change it).
- `PerformanceTelemetryService` sample logic is exercised via a public
  `SampleOnce()` used by the background loop (deterministic, no timers in
  tests).
- Manual: build, run, open Settings → Performance, verify live numbers move
  and chat messages bump platform rows.
