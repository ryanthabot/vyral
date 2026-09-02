# Streamer.Bot Clone — Architecture & Starter Template (C# / WPF)

> Purpose: provide a complete, production-minded architecture for building a Streamer.bot-style desktop app on Windows using C# (.NET 8) and WPF/WinUI. This doc includes high-level design, components, data models, threading rules, recommended packages, folder layout, APIs, and starter skeletons.

---

## 1. Goals & Non-functional requirements

**Primary goals**

* Full parity with Streamer.bot features: triggers, actions, sequences, scripting, device/hotkey hooks, OBS/Twitch/YouTube integration, UI for authoring.
* Responsive, low-latency real-time event handling.
* Stable multi-threaded runtime with safe plugin/scripting sandboxing.
* Easy packaging (single EXE), auto-update support, and extensible plugin system.

**Non-functional**

* Target platform: Windows 10/11 (native-first). Later: cross-platform via MAUI if needed.
* Performance: sub-100ms latency for local event→action flows.
* Reliability: crash isolation for user scripts/plugins.
* Security: sandboxing, explicit permission model for plugins/actions that access network/files.

---

## 2. High-level architecture

Components (brief):

* **UI Layer (WPF / WinUI)** — authoring, dashboards, logs, visual editor.
* **Core (Host) / Service Layer** — event bus, scheduler, plugin host, action dispatcher.
* **Integrations** — Twitch (PubSub/IRC), YouTube (Pub/Sub), OBS WebSocket, Webhooks, Hotkey/Device handlers.
* **Scripting & Plugin Hosts** — Roslyn (C# scripting), MoonSharp (Lua) sandbox, separate AppDomain/process for untrusted code.
* **Data storage** — configuration, actions, triggers, logs (SQLite or LiteDB).
* **IPC & HTTP API** — local REST/WS for external control and dashboard.
* **Installer/Updater** — MSIX or single-file publish with updater.

Diagram (conceptual):

```
+-----------------------------+
|        WPF UI (Main)        |
+----+----------------+-------+
     |                |
     v                v
+----+----------------+-------+
|    Core Host / Service Layer |
|  - Event Bus (inproc)        |
|  - Scheduler (timers)        |
|  - Action Dispatcher         |
+----+----------------+-------+
     |                |  
     v                v
+----+--+    +--------+-------+    +-------------+
|Twitch |    |   OBS WS      |    | Hotkeys/USB |
|Client |    | Integration    |    | Input       |
+-------+    +----------------+    +-------------+
     |                |                |
     v                v                v
  (PubSub/IRC)     (Websocket)     (WinAPI hooks)

Scripting/Plugins run in sandboxed hosts (separate process or constrained AssemblyLoadContext)
Storage (SQLite) and logger are shared via well-defined APIs.
```

---

## 3. Core concepts

### Event → Trigger → Action model

* **Event**: incoming signal (Twitch chat msg, OBS scene change, local hotkey, webhook).
* **Trigger**: user-defined rule that matches events & conditions (filters, regex, cooldown, probability).
* **Action**: sequence of operations executed when trigger fires (send chat, run script, show overlay, play audio, call HTTP).

Each trigger registers on the Event Bus with a matching function. When matched the Trigger enqueues a Task with a priority and context.

### Action execution guarantees

* Actions run in an isolated execution context with a per-action timeout and optional cancellation.
* Actions can be synchronous or asynchronous. Blocking long operations must be offloaded to the TaskScheduler.
* Provide action-level retries and error handling policies.

---

## 4. Event Bus & Concurrency

**Event Bus**

* In-process, concurrent pub/sub.
* Use `System.Threading.Channels` or a lightweight in-memory message bus.
* Events carry a typed payload and metadata (timestamp, source, originating connection id).

**Concurrency model**

* UI thread (WPF) only for view updates.
* Core host uses a thread pool (Task-based). Avoid running long work on UI thread.
* Low-latency handlers (hotkeys) should post events to the Event Bus with minimal blocking.
* Scripts/plugins run in separate `AssemblyLoadContext` or isolated process to protect host.

---

## 5. Scripting & Plugin system

**Design goals**: allow users to write custom logic while protecting host stability.

**Options**

1. **Roslyn Scripting (C#)** — compile & run user scripts with limited references. Use `Microsoft.CodeAnalysis.CSharp.Scripting` with `ScriptOptions` limiting assemblies.
2. **Lua (MoonSharp)** — lightweight, easy to sandbox; good for user-created mini-scripts.
3. **Plugin System (.NET plugins)** — load compiled DLLs via `AssemblyLoadContext` and a strict plugin interface.

**Sandboxing strategies**

* Preferred: run untrusted code in a **separate worker process**. Communicate via named pipes or gRPC. Worker process enforces timeouts and resource limits.
* Alternative: limit accessible APIs with Roslyn's `HostObject` and carefully curated references.

**API surface for scripts** (expose minimal, stable APIs):

* `IActionContext` (SendMessage, Delay, GetVariable, SetVariable, CallAction)
* `IHttpClient` wrapper (rate-limited)
* `IStorage` for small persistent state (per-script)

---

## 6. Integrations (detailed)

### Twitch

* Use **tmi/twitchlib** equivalents (TwitchLib for .NET). Support both IRC chat and PubSub for Bits/subs.
* Provide reconnect/backoff strategy and multi-account support.

### OBS

* Use **OBS Websocket** client library (ObsWebsocket.NET). Handle connection loss and queued commands.

### YouTube

* Polling + PubSub if available. Use OAuth for account actions.

### Webhooks

* Local HTTP endpoint (Kestrel) optionally tunneled (ngrok-style) for external services.

### Hotkeys & input

* Use `GlobalLowLevelHook` (e.g. `Gma.System.MouseKeyHook`) or native Win32 hooks for global hotkeys.
* Controller/XInput: use SharpDX/XInput or `Windows.Gaming.Input` where appropriate.

### Audio triggers

* Use WASAPI (NAudio) to monitor device levels and detect thresholds.

---

## 7. Data storage and models

**Choose**: SQLite (via `Microsoft.Data.Sqlite` + Dapper/EF Core) or LiteDB for pure .NET single file DB.

**Core tables/collections**

* `Triggers` (id, name, eventType, filter, cooldown, enabled)
* `Actions` (id, name, sequenceJson, timeout)
* `Variables` (scope, key, value, type)
* `Plugins` (id, meta, enabled)
* `Logs` (timestamp, level, source, message, payload)

Keep action sequences as versioned JSON blobs for editor compatibility.

---

## 8. Local API and External Control

Expose a local HTTP + WebSocket API (bound to `localhost` only by default). Support:

* Query triggers/actions/config
* Fire an event (for testing)
* Subscribe to real-time logs/events
* Admin endpoints for importing/exporting config

Authentication: support optional API token stored encrypted.

---

## 9. UI & UX

**Main windows**

* Dashboard: status of services, recent events/logs
* Editor: triggers & actions WYSIWYG visual editor (drag & drop actions into sequence)
* Scripting console: test/run scripts with sandboxed output
* Connection manager: manage Twitch/OBS accounts
* Debug/Logs: searchable, filterable logging

**Key UX features**

* Visual action blocks with parameter editing
* Live test firing + step-through debugger for action sequences
* Undo/Redo for editor
* Templates / Marketplace for actions

---

## 10. Packaging & Deployment

* Use `dotnet publish -r win-x64 --self-contained` for single EXE (or MSIX for Windows Store).
* Updater: Squirrel/WinSparkle/Custom updater that checks signed releases.
* Auto-backups before upgrade.

---

## 11. Security & Privacy

* Scripts/plugins run sandboxed and ask permissions to access network/files/hardware.
* Encrypted storage for tokens (Windows DPAPI via `ProtectedData`/`DataProtectionProvider`).
* Provide a plugin permission manager UI.

---

## 12. Testing & Observability

* Unit tests for core services and event bus (xUnit/NUnit).
* Integration tests using test harness that can simulate Twitch/OBS.
* Telemetry (opt-in) for crash reporting (Sentry or AppCenter).

---

## 13. Recommended NuGet & Libraries

* `Microsoft.Extensions.Hosting` (hosted services model)
* `Microsoft.Extensions.DependencyInjection` (DI)
* `Microsoft.Extensions.Logging` (ILogger)
* `System.Threading.Channels` (event bus primitive)
* `Microsoft.Data.Sqlite` or `LiteDB` (storage)
* `Dapper` or `EFCore` (data access)
* `TwitchLib.Client` / `TwitchLib.PubSub` (Twitch)
* `ObsWebsocket.DotNet` (OBS Websocket client)
* `Gma.System.MouseKeyHook` (global hotkeys)
* `NAudio` (audio input monitoring)
* `MoonSharp` (Lua scripting) or `CSharpScript` (Roslyn scripting)
* `Newtonsoft.Json` / `System.Text.Json` (JSON)
* `Polly` (resilience/retries)

---

## 14. Starter folder structure (recommended)

```
/src
  /App.UI               -> WPF front-end project
  /App.Core             -> core services, event bus, dispatcher
  /App.Integrations     -> Twitch/OBS/YouTube adapters
  /App.Scripting        -> script host(s)
  /App.Plugins          -> plugin interfaces & loader
  /App.Data             -> data layer (SQLite/LiteDB)
  /App.Api              -> local REST/WebSocket API
  /App.Workers          -> worker processes (sandboxed script host)
/tests
  /Unit
  /Integration
/tools
  /Installer
  /DevTools

/deploy
  /releases
  /installer

/docs
  architecture.md
  api.md
```

---

## 15. Example skeletons (quick pointers)

* **Event Bus**: implement a typed `IEvent` interface and a `Channel<T>`-backed `EventPublisher` + `EventSubscriber` helpers.
* **Trigger**: model as a record with `ITriggerCondition` and `ITriggerAction` interfaces. Use DI to resolve actions at runtime.
* **Action Dispatcher**: execute actions in a controlled `Task` with `CancellationTokenSource` and timeout.
* **Script host**: worker process that exposes a small RPC (named pipes) to the host. Worker evaluates script and returns result + logs.

---

## 16. Gradual roadmap (MVP → v1 → v2)

**MVP (0.1)**

* Core host, simple UI, Twitch chat connect, OBS connect, basic triggers/actions (send chat, HTTP call, play audio), local storage.

**v1**

* Full visual action editor, scripting host (Lua), hotkeys & device input, plugin loader, local API, packaging.

**v2+**

* Roslyn C# scripting, marketplace, cloud sync, cross-platform adaptation, advanced integrations.


