# Performance View

## Overview
Live performance monitor for the Settings → Performance tab. Shows app-level
resource usage, per-platform event/message rates, component health and
VyralBot-owned child processes, refreshed on a UI timer that only runs while
the tab is visible.

## Source Files
- **ViewModel:** `src/VyralBot.UI/ViewModels/PerformanceViewModel.cs`
- **View:** `src/VyralBot.UI/Views/PerformanceView.xaml` (+ `.cs`)
- **Telemetry:** `src/VyralBot.Core/Services/Performance/` —
  `PerformanceTelemetryService` (1s process sampling, 5s child scan),
  `TelemetrySample`, `IMetricsRegistry`/`MetricsRegistry` (shared counters),
  `PerformanceMonitor` (15s info-level log).

## What it shows
- **Summary tiles:** CPU %, working-set memory, threads, GC collections,
  events/s, workflows/s.
- **Sparklines:** 3-minute CPU and memory history (bounded 180-sample ring in
  `PerformanceTelemetryService.History`).
- **Platforms:** per-platform status, event-type breakdown, events/s,
  messages-in/s, sends, failed sends — counters aggregated from
  `IMetricsRegistry` snapshots; DB status polled every 5th tick.
- **Components:** Event Bus (subscribers/pending/handler errors), Workflow
  Engine (runs/avg/failed), Web Server (requests/errors/port).
- **Child processes:** WebView2 runtime (attributed by parent-PID walk) and
  ngrok.
- **Secondary strip:** private memory, **managed heap** (`TelemetrySample.HeapBytes`,
  GC heap via `GC.GetGCMemoryInfo()`), handles, managed allocation rate,
  thread-pool threads, pending queue items, web requests/s.

## Refresh lifecycle
- `PerformanceView` code-behind calls `viewModel.Start()` on
  Loaded/IsVisibleChanged and `Stop()` on Unloaded/hidden — a closed tab costs
  nothing; the underlying counters keep accumulating app-wide.
- `DispatcherTimer` interval is user-selectable (1/2/5s); `TogglePauseCommand`
  freezes the UI refresh without stopping sampling.

## Registration
- `PerformanceTelemetryService` singleton + hosted wrapper in
  `App.xaml.cs` `ConfigureServices` (same instance started with the host);
  `PerformanceViewModel` singleton, injected into `SettingsViewModel`.
