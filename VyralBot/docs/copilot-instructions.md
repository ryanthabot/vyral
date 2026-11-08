# Copilot Instructions - VyralBot Project

## 🎯 Core Principles

### Documentation Synchronization
**EVERY change to code must be reflected in documentation. EVERY documentation change must reference actual code.**

This project maintains a comprehensive documentation system that serves as the single source of truth for understanding, extending, and maintaining VyralBot. When making any changes, you MUST update all related documentation files to maintain accuracy and consistency.

### Performance First
**VyralBot must be the FASTEST, most EFFICIENT streaming automation platform with MINIMAL resource usage.**

- **Target Memory:** 40-80 MB working set
- **Target CPU:** <1% idle, 2-3% during events
- **Process Count:** 1 (VyralBot.exe ONLY - no child processes unless absolutely necessary)
- **GC Pauses:** <10ms (99th percentile)

### Zero Orphans Policy
**ALL external processes MUST be tracked, logged, and killed on shutdown. NO orphaned processes allowed.**

---

## 📋 Change Management Protocol

### When Adding New Code

**1. Create the code file(s)**
**2. IMMEDIATELY document it:**

| File Type | Documentation Required | Location |
|-----------|------------------------|----------|
| ViewModel | Create `{name}-viewmodel.md` | `docs/ui/` |
| Service | Create `{name}-service.md` | `docs/services/` |
| Model/Entity | Update `docs/models/entities.md` or create new | `docs/models/` |
| View (XAML) | Update `docs/ui/{name}-view.md` | `docs/ui/` |
| Feature | Create `{name}.md` | `docs/features/` |
| Architecture Change | Update relevant file in `docs/architecture/` | `docs/architecture/` |

**3. Update Master README:**
- Add entry to `docs/README.md` under appropriate section
- Mark as ✅ Complete with description
- Update file counts and progress percentages

**4. Update related files:**
- If adding a platform: Update `docs/features/platform-management.md`
- If adding event type: Update `docs/models/platform-events.md`
- If adding DI registration: Update `docs/architecture/dependency-injection.md`

---

### When Editing Existing Code

**1. Make code changes**
**2. Update corresponding documentation:**
   - Find the relevant `.md` file(s)
   - Update method signatures, properties, descriptions
   - Update code examples if they've changed
   - Update sequence diagrams or flow descriptions

**3. Check cross-references:**
   - Search `docs/` for references to changed classes/methods
   - Update all files that reference the changed code
   - Example: Changing `IDataService` affects `data-service.md`, `profile-aware-data-service.md`, `interfaces.md`

**4. Update README if:**
   - Feature status changes (e.g., 🔄 In Progress → ✅ Complete)
   - File descriptions change
   - New subsections added

---

### When Removing Code

**⚠️ CRITICAL: Follow this exact sequence to avoid breaking the app**

**1. Identify all references:**
```powershell
# Search entire codebase
rg "ClassName" --type cs
rg "ClassName" --type xaml
```

**2. Remove references FIRST:**
   - Dependency injection registrations (`App.xaml.cs`)
   - Field/property declarations in other classes
   - XAML references
   - Using statements

**3. Verify build succeeds:**
```powershell
dotnet build src/VyralBot.sln
```

**4. Remove the actual files**

**5. Update documentation:**
   - Move documentation to `docs/architecture/deprecated-components.md`
   - Remove from `docs/README.md` OR mark as ⚠️ Deprecated
   - Update file counts
   - Document why it was removed and what replaced it

**6. Update cross-references:**
   - Remove from all files that referenced it
   - Update architecture diagrams if needed

---

## 📁 Documentation Structure Map

### Architecture (`docs/architecture/`)
- `mvvm-pattern.md` - CommunityToolkit.Mvvm patterns
- `viewmodel-base.md` - ViewModelBase abstract class
- `event-bus.md` - Channel-based EventBus (capacity 500, SemaphoreSlim 4)
- `data-layer.md` - Dapper/SQLite data access pattern
- `dependency-injection.md` - DI container setup, 60+ service registrations
- `deprecated-components.md` - Removed/legacy code reference

**Update when:** Changing DI registrations, adding new projects, modifying core patterns

---

### Features (`docs/features/`)
- `chat-system.md` - Multi-platform chat (17+ deps ChatViewModel, message store, auto-reply)
- `event-system.md` - All event types (3 core + 8 Kick + 9 Twitch) and EventBus flow
- `kick-integration.md` - KickLib integration (KickLibChatService, KickLibOAuthService)
- `marketplace-sharecodes.md` - Command marketplace (GitHub store + BotGhost relay), sharecode/offline-code import safety
- `quotes.md` - Quote database + `!quote` command family (QuoteService, QUOTES tab)
- `giveaways-and-polls.md` - Weighted-raffle giveaways + chat polls (race-hardened draws/votes)
- `restream.md` - FFmpeg multi-platform restream (ingest, fan-out, verified acquisition)
- `workflow-templates.md` - Starter workflow templates (Automation page one-click import)
- `twitch-integration.md` - LightweightTwitchIrc + TwitchEventSubService + TwitchOAuthService
- `vyraldeck.md` - VyralDeck mobile companion: LAN server (5199/5198), PIN pairing, 22 deck actions
- `youtube-integration.md` - YouTube Data API chat + SuperChat
- `platform-management.md` - Platform entity, PlatformsViewModel, connection flows
- `profile-system.md` - ProfileService, portable storage, per-profile databases
- `settings-system.md` - SettingsViewModel, Config entity, sub-VMs
- `flow-editor-canvas.md` - Visual workflow editor canvas
- `variables-and-conditional-rules.md` - Variable substitution engine (VariableReplacementService, auto-updating counts) + conditional auto-reply rules (RuleEvaluator, AutoReplyService)

**Update when:** Adding new platforms, changing OAuth flows, modifying automation logic

---

### Application (`docs/application/`)
- `app-entry.md` - App.xaml.cs startup, Host builder, DI registration, profile selection

**Update when:** Changing startup sequence, DI registrations, profile selection UX

---

### UI Components (`docs/ui/`)
**ViewModels:**
- `main-viewmodel.md` - Navigation, 17 scoped VMs, profile orchestration
- `dashboard-viewmodel.md` - Dashboard stats (35 lines, parameterless)
- `chat-viewmodel.md` - Multi-platform chat (2562 lines, 17+ deps)
- `settings-viewmodel.md` - Profile management, sub-VMs
- `platforms-viewmodel.md` - Platform connection management (1126 lines)
- `profile-selector.md` - Profile selector (manual INPC, not ViewModelBase)
- `kick-ui-viewmodels.md` - KickSettings, KickDashboard, KickChatSettings, KickWebSetup
- `kick-web-setup-viewmodel.md` - Kick OAuth setup (975 lines)
- `twitch-settings-viewmodel.md` - Twitch settings + related VMs
- `settings-viewmodels.md` - Sub-setting VMs (WebServer, Audio, Sounds, Alerts, Variables, per-platform chat)
- `oauth-setup-wizards.md` - All OAuth/setup base classes and implementations
- `event-testing-viewmodel.md` - Event Testing page (fires real events via TestEventPublisher)
- `event-history-viewmodel.md` - Event History page (event log viewer, CSV export)
- `statistics-viewmodel.md` - Statistics page (dashboards, aggregated CSV export)

**Views:**
- `main-window.md` - MainWindow layout
- `dashboard-view.md` - Dashboard UI
- `chat-view.md` - Chat display UI
- `commands-view.md` - Commands page (built-in/custom/timers/quotes tabs)
- `settings-view.md` - Settings UI
- `platforms-view.md` - Platform management UI
- `setup-views-xaml.md` - Setup wizard XAML views
- `settings-views-xaml.md` - Settings XAML views (including per-platform chat settings)
- `view-code-behind.md` - Code-behind patterns, UI services, controls, behaviors
- `discord-dashboard.md` - Discord DASHBOARD tab (fleet KPIs, mod actions, workflow runs)
- `discord-bot-detail.md` - Per-bot Discord dashboard + visual command builder
- `performance-view.md` - Performance tab (live metrics, component health)
- `viewer-hub.md` - Viewer Hub (linking, bonuses, odds, goals)
- `sounds-view.md` - Sounds page (sound library, test playback)
- `data-management.md` - Settings → Data Management (storage, inventory, backup/import)

**Other:**
- `theme-system.md` - Resource dictionary theming
- `THEMING.md` - Theming guide (switch/extend themes)
- `converters.md` - 19 IValueConverter files overview
- `converters-reference.md` - All converter implementations detailed
- `dialogs-and-modals.md` - ConfirmDialog, ErrorDialog, CustomDialog, AlertOverlay

**Update when:** Adding UI components, changing navigation, modifying themes

---

### Services (`docs/services/`)
- `interfaces.md` - All service interfaces (Core + Integrations)
- `trigger-engine.md` - Event → Action automation (ITriggerEngine, TriggerEngine)
- `action-dispatcher.md` - Action execution with timeout/cancellation
- `profile-service.md` - ProfileService file-based profile management
- `data-service.md` - IDataService/DapperDataService/ProfileAwareDataService overview
- `data-services-detailed.md` - Detailed DapperDataService method reference
- `twitch-service.md` - TwitchService (LightweightTwitchIrc + EventSub)
- `kick-service.md` - KickLibChatService (KickLib WebSocket + REST)
- `kick-oauth-service.md` - KickLibOAuthService (PKCE via KickLib)
- `lightweight-twitch-irc.md` - WebSocket IRC client (377 lines)
- `moderation.md` - Auto-moderation + mod commands + platform parity
- `workflow-runtime.md` - Workflow graph interpreter + run cancellation
- `discord-bot-runtime.md` - Discord gateway + multi-bot manager (welcome/auto-role)
- `embedded-web-server.md` - Embedded Kestrel server (webhooks + Bearer control API)
- `test-event-publisher-service.md` - TestEventPublisher (canned events for the Event Testing page + control API)
- `crash-pipeline.md` - Global exception handling, secret scrubbing, crash dialog
- `obs-websocket-client.md` - obs-websocket 5 remote-control client
- `performance-monitor.md` - PerformanceMonitor, EventPool, PooledBuffer
- `process-management.md` - Process management

**Update when:** Adding service methods, changing service contracts, modifying OAuth flows

---

### Models (`docs/models/`)
- `entities.md` - All 23 data entities with Guid IDs
- `trigger-and-action-entities.md` - Trigger, BotAction entities
- `action-context-model.md` - ActionContext for trigger execution
- `platform-events.md` - All event records (3 core + 8 Kick + 9 Twitch)

**Update when:** Adding entity properties, creating new event types, changing database schema

---

### Master Reference (`docs/README.md`)
**Central hub linking to all documentation with complete ViewModel inventory, service listing, and doc index.**

**Update EVERY TIME:**
- New documentation file created
- New ViewModel/service/entity added
- File status changes

---

## 🚀 VyralBot Architecture

### Technology Stack
- **Framework:** .NET 10.0
- **UI:** WPF (Windows Presentation Framework) + MaterialDesignInXamlToolkit
- **Data:** SQLite + Dapper (no Entity Framework - 40MB memory savings)
- **Real-time:** WebSocket (Twitch IRC), KickLib WebSocket (Kick), EventSub (Twitch)
- **Encryption:** Windows DPAPI for credentials at rest (v1: envelope on
  platform secrets + Discord bot tokens — see `services/data-service.md`)
- **Patterns:** MVVM, Event Bus, Clean Architecture

### Supported Platforms
- ✅ **Twitch:** OAuth 2.0, IRC WebSocket, Helix API, EventSub, full
  auto-mod enforcement (Helix timeout/ban/delete)
- ✅ **Kick:** OAuth 2.1 with PKCE, KickLib WebSocket, REST API, full
  auto-mod enforcement (ban/timeout/delete via Kick API v1)
- ✅ **YouTube:** Data API v3, LiveChat polling, SuperChat events, full
  auto-mod enforcement (native delete; ban/timeout degrade to delete)
- ✅ **Discord:** Multi-bot gateway, commands, message posting, member/boost
  events, full auto-mod enforcement (ban/timeout)
- ✅ **OBS Studio:** Embedded libobs hosting + obs-websocket v5
- 🔜 **TikTok:** Coming soon — web setup guide only today, no chat/API
  integration yet (auto-mod logged-only)

### Project Structure
```
VyralBot/
├── src/
│   ├── VyralBot.Core/           # Domain logic, EventBus, TriggerEngine, ActionDispatcher
│   ├── VyralBot.UI/             # WPF ViewModels, Views, Converters, Theme
│   ├── VyralBot.Data/           # SQLite + Dapper, Entity definitions
│   ├── VyralBot.Integrations/   # Platform adapters (Twitch, Kick, YouTube, etc.)
│   │   ├── Twitch/              # TwitchService, IRC, OAuth
│   │   ├── Kick/                # KickLibChatService, KickLibOAuthService
│   │   └── ...
│   ├── VyralBot.Plugins/        # Plugin system (future)
│   └── VyralBot.Scripting/      # Scripting engine (future)
├── appdata/                     # ⚠️ USER DATA (git ignored, portable)
│   ├── profiles/                # Profile-isolated storage
│   │   └── {ProfileName}/
│   │       ├── profile.json     # Profile metadata
│   │       ├── data/            # SQLite databases
│   │       │   └── vyralbot.db
│   │       ├── twitch_credentials.json  # Encrypted OAuth tokens
│   │       └── kick_credentials.json    # Encrypted OAuth tokens
│   ├── current_profile.txt      # Active profile tracker
│   └── README.md                # Documentation for users
├── logs/                        # ⚠️ Application logs (git ignored)
├── docs/                        # 88 documentation files
│   ├── architecture/            # 6 files
│   ├── features/                # 16 files
│   ├── ui/                      # 34 files
│   ├── services/                # 19 files
│   ├── models/                  # 4 files
│   ├── integrations/            # 1 file
│   ├── application/             # 1 file
│   ├── README.md                # Master doc index
│   ├── CHANGELOG.md             # Release changelog
│   ├── release-qa-checklist.md  # Manual pre-release QA checklist
│   └── copilot-instructions.md  # THIS FILE
├── CONTEXT_MASTER.md            # Meta-document with critical rules
├── DISTRIBUTION.md              # Guide for sharing/distribution
├── .gitignore                   # Ignores appdata/, logs/
└── VyralBot.sln
```

### Module Boundaries (Clean Architecture)
```
┌─────────────────────────────────────────────┐
│ VyralBot.UI (WPF)                           │
│ ├─ ViewModels (MVVM)                        │
│ ├─ Views (XAML)                             │
│ └─ Converters, Theme                        │
└─────────────────┬───────────────────────────┘
                  │ References ↓
┌─────────────────┴───────────────────────────┐
│ VyralBot.Integrations                       │
│ ├─ ITwitchService, IKickService, etc.       │
│ └─ Platform-specific implementations        │
└─────────────────┬───────────────────────────┘
                  │ References ↓
┌─────────────────┴───────────────────────────┐
│ VyralBot.Core                               │
│ ├─ EventBus (pub/sub)                       │
│ ├─ TriggerEngine (event → action)           │
│ ├─ ActionDispatcher (action execution)      │
│ ├─ ProfileService (profile CRUD)            │
│ └─ ProcessManager (background processes)    │
└─────────────────┬───────────────────────────┘
                  │ Uses ↓
┌─────────────────┴───────────────────────────┐
│ VyralBot.Data                               │
│ ├─ IDataService (interface)                 │
│ ├─ DapperDataService (SQLite)               │
│ └─ Entities (Profile, Platform, Config)     │
└─────────────────────────────────────────────┘
```

**RULE:** No reverse dependencies. UI → Integrations → Core → Data. No Core → UI references.

---

## ⚡ CRITICAL: Performance Requirements

VyralBot must be the **fastest possible** program with **minimal CPU/memory usage**.

### Memory Optimizations

#### 1. ArrayPool for Buffers (MANDATORY)
**RULE:** ALL buffers >= 1KB MUST use `ArrayPool<byte>.Shared`

```csharp
// ❌ WRONG: New array allocation
byte[] buffer = new byte[8192];
await stream.ReadAsync(buffer, 0, buffer.Length);

// ✅ RIGHT: ArrayPool reuse
var buffer = ArrayPool<byte>.Shared.Rent(4096);
try
{
    int bytesRead = await stream.ReadAsync(buffer, 0, 4096);
    // Process buffer
}
finally
{
    ArrayPool<byte>.Shared.Return(buffer);
}
```

**Impact:** Eliminates ~1000 allocations/minute for WebSocket connections

#### 2. Pre-Allocate Collections
```csharp
// ❌ WRONG: No capacity specified
var list = new List<Event>();
foreach (var item in items)
{
    list.Add(ProcessEvent(item));
}

// ✅ RIGHT: Pre-allocate capacity
var list = new List<Event>(capacity: items.Count);
foreach (var item in items)
{
    list.Add(ProcessEvent(item));
}
```

**Impact:** Prevents multiple array resizes, reduces GC pressure

---

### Concurrency Optimizations

#### 1. EventBus Throttling (MANDATORY)
**RULE:** Max 4 concurrent event handlers allowed

```csharp
// ❌ WRONG: Unlimited concurrent handlers
await Task.WhenAll(handlers.Select(h => h.HandleAsync(eventData)));

// ✅ RIGHT: Throttle with SemaphoreSlim
private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(4, 4);

public async Task PublishAsync<T>(T eventData) where T : IEvent
{
    var tasks = handlers.Select(async handler =>
    {
        await _semaphore.WaitAsync();
        try
        {
            await handler.HandleAsync(eventData);
        }
        finally
        {
            _semaphore.Release();
        }
    });

    await Task.WhenAll(tasks);
}
```

**Impact:** Prevents thread pool exhaustion, CPU spike during event bursts

#### 2. Bounded Channels with Backpressure
```csharp
// ✅ EventBus channel configuration
var options = new BoundedChannelOptions(capacity: 500)
{
    FullMode = BoundedChannelFullMode.DropOldest
};
_channel = Channel.CreateBounded<IEvent>(options);
```

**Impact:** 50% less buffer memory, prevents blocking

---

### Blocking Operations (FORBIDDEN)

#### Never Use .Result or .Wait()
```csharp
// ❌ WRONG: Blocking on async
var result = SomeAsyncMethod().Result;
var data = Task.Run(() => FetchDataAsync()).Wait();

// ✅ RIGHT: Async/await all the way
var result = await SomeAsyncMethod();
var data = await FetchDataAsync();
```

**Impact:** Prevents deadlocks, thread pool starvation

---

### Logging (CRITICAL)

#### Info-Level Logging Only
**RULE:** NO Debug logging unless actively debugging

```csharp
// ❌ WRONG: Debug logging everywhere
_logger.LogDebug("Processing {Count} items", items.Count);
_logger.LogDebug("Connecting to {Url}", url);
_logger.LogDebug("Buffer size: {Size}", buffer.Length);

// ✅ RIGHT: Info for important events only
_logger.LogInformation("Connected to Twitch IRC");
_logger.LogInformation("Profile changed: {ProfileName}", profile.Name);
_logger.LogError(ex, "Failed to authenticate with Kick");
```

**Impact:** 70% fewer log statements, 5-10% CPU reduction

---

### Fire-and-Forget for Non-Critical Operations
```csharp
// ✅ Fire-and-forget pattern
_ = Task.Run(async () =>
{
    try
    {
        await UpdateStatisticsAsync();
    }
    catch (Exception ex)
    {
        _logger.LogWarning(ex, "Statistics update failed");
    }
});
```

**Use for:** Statistics updates, UI refresh, metrics logging (NOT for critical operations)

---

### GC Configuration

**Applied in Program.cs:**
```csharp
GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;
```

**Target Metrics:**
- GC Gen 0: <5 collections/minute
- GC Pauses: <10ms (99th percentile)

---

## 🔒 CRITICAL: Process Management (Zero Orphans Policy)

**RULE: ALL external processes MUST be tracked. NO orphaned processes allowed.**

### Process Count Target
**Expected:** 1 process in Task Manager (`VyralBot.exe`)
**Warning:** >1 process = child process leak
**Forbidden:** Orphaned processes after app closes

### ProcessManager Service (MANDATORY)

#### Never Use Direct Process.Start()
```csharp
// ❌ WRONG: Untracked process
Process.Start("external-tool.exe");

// ✅ RIGHT: Track through ProcessManager
private readonly IProcessManager _processManager;

public async Task RunToolAsync()
{
    var startInfo = new ProcessStartInfo
    {
        FileName = "external-tool.exe",
        Arguments = "--config config.json",
        UseShellExecute = false,
        RedirectStandardOutput = true,
        CreateNoWindow = true
    };

    var process = await _processManager.StartProcessAsync(startInfo);
    _logger.LogInformation("Started {Name} (PID: {PID})", "external-tool", process.Id);
}
```

### Avoid WebView2 Components
**RULE:** WebView2 spawns 5+ child processes (20-30 MB each). Use `HttpClient` instead.

| Scenario | ❌ WebView2 (Bad) | ✅ HttpClient (Good) |
|----------|-------------------|----------------------|
| **API Calls** | WebView2.CoreWebView2.ExecuteScriptAsync() | HttpClient.GetAsync() |
| **Process Count** | 6+ (parent + 5 children) | 1 (in-process) |
| **Memory** | 100-150 MB | 40-60 MB |
| **Performance** | Slow (browser startup) | Fast (instant) |

**Exception:** OAuth wizard flows (TwitchWebSetupView, KickWebSetupView) - acceptable use

### Shutdown Cleanup (MANDATORY)
**ALL processes MUST be killed when app closes:**

```csharp
// In App.xaml.cs - Application_Exit event
protected override async void OnExit(ExitEventArgs e)
{
    try
    {
        var processManager = _serviceProvider.GetService<IProcessManager>();
        if (processManager != null)
        {
            await processManager.KillAllProcessesAsync();
            await processManager.DisposeAsync();
        }
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error during shutdown cleanup");
    }

    base.OnExit(e);
}
```

### Monitor WebView2 Child Processes
```csharp
// Log BrowserProcessId when WebView2 is initialized
webView.CoreWebView2InitializationCompleted += (s, e) =>
{
    if (e.IsSuccess)
    {
        var browserPid = webView.CoreWebView2.BrowserProcessId;
        _logger.LogInformation("WebView2 initialized (Browser PID: {PID})", browserPid);
        _processManager.TrackExternalProcess(browserPid, "msedgewebview2");
    }
};
```

### Process Tracking Best Practices
1. **Log PID on create:** Every `StartProcessAsync()` must log PID
2. **Log PID on exit:** Track when processes terminate
3. **Lazy creation:** Don't spawn processes on startup
4. **In-process alternatives:** Prefer libraries over external executables
5. **Monitor count:** PerformanceMonitor checks process count every 30s

---

## 🚫 Forbidden Patterns (NEVER Do This)

### 1. Memory Violations
```csharp
// ❌ WRONG
byte[] buffer = new byte[8192];
var list = new List<Event>(); // No capacity
var dict = new Dictionary<string, object>(); // No capacity

// ✅ RIGHT
var buffer = ArrayPool<byte>.Shared.Rent(4096);
var list = new List<Event>(capacity: 100);
var dict = new Dictionary<string, object>(capacity: 50);
```

### 2. Concurrency Violations
```csharp
// ❌ WRONG: Unlimited concurrent operations
await Task.WhenAll(items.Select(ProcessAsync));

// ✅ RIGHT: Throttle to 4 concurrent max
var semaphore = new SemaphoreSlim(4, 4);
var tasks = items.Select(async item =>
{
    await semaphore.WaitAsync();
    try { await ProcessAsync(item); }
    finally { semaphore.Release(); }
});
await Task.WhenAll(tasks);
```

### 3. Blocking Violations
```csharp
// ❌ WRONG
var result = SomeAsyncMethod().Result;
Task.Run(() => DoWorkAsync()).Wait();
lock (_lock) { await SomeAsyncMethod(); } // DEADLOCK RISK

// ✅ RIGHT
var result = await SomeAsyncMethod();
await DoWorkAsync();
await _asyncLock.WaitAsync();
try { await SomeAsyncMethod(); }
finally { _asyncLock.Release(); }
```

### 4. Logging Violations
```csharp
// ❌ WRONG: Debug spam
_logger.LogDebug("Processing item {Index}", i);
_logger.LogDebug("Buffer size: {Size}", buffer.Length);

// ✅ RIGHT: Info for important events only
_logger.LogInformation("Connected to {Platform}", platformName);
_logger.LogError(ex, "Authentication failed");
```

### 5. Process Management Violations
```csharp
// ❌ WRONG: Untracked process
Process.Start("tool.exe");
var webView = new WebView2(); // Spawns 5+ child processes

// ✅ RIGHT: Tracked process + HttpClient
var process = await _processManager.StartProcessAsync(startInfo);
var response = await _httpClient.GetAsync("https://api.example.com");
```

---

## 🔄 Specific Update Scenarios

### Adding a New Platform (e.g., YouTube)

**Code Changes:**
1. Create `src/VyralBot.Integrations/YouTube/YouTubeService.cs`
2. Create `src/VyralBot.Integrations/YouTube/IYouTubeService.cs`
3. Create `src/VyralBot.Integrations/YouTube/YouTubeOAuthService.cs`
4. Create `src/VyralBot.Integrations/YouTube/Events/YouTubeChatMessageEvent.cs`
5. Create `src/VyralBot.UI/ViewModels/YouTubeWebSetupViewModel.cs`
6. Create `src/VyralBot.UI/ViewModels/YouTubeSettingsViewModel.cs`
7. Create `src/VyralBot.UI/Views/YouTubeWebSetupView.xaml`
8. Create `src/VyralBot.UI/Views/YouTubeSettingsView.xaml`
9. Register in `App.xaml.cs` DI container
10. Add to `PlatformsViewModel.cs` connection logic

**Documentation Updates:**
1. **Create** `docs/services/youtube-service.md` (full implementation)
2. **Create** `docs/services/youtube-oauth-service.md` (OAuth flow)
3. **Create** `docs/ui/youtube-ui-viewmodels.md` (ViewModels)
4. **Create** `docs/features/youtube-integration.md` (feature overview)
5. **Update** `docs/models/platform-events.md` (add YouTube events)
6. **Update** `docs/services/interfaces.md` (add IYouTubeService)
7. **Update** `docs/architecture/dependency-injection.md` (add DI registrations)
8. **Update** `docs/ui/platforms-viewmodel.md` (add YouTube connection logic)
9. **Update** `docs/features/platform-management.md` (add YouTube to supported platforms)
10. **Update** `docs/README.md` (add all new files to index)

**Performance Checklist:**
- [ ] ✅ Use `HttpClient` for API calls (not WebView2)
- [ ] ✅ Use `ArrayPool<byte>` for buffers >= 1KB
- [ ] ✅ Pre-allocate collections with capacity
- [ ] ✅ Throttle concurrent operations (max 4 with SemaphoreSlim)
- [ ] ✅ Info-level logging only (no Debug spam)
- [ ] ✅ Fire-and-forget for non-critical updates
- [ ] ✅ No blocking (.Result/.Wait())

**Process Management Checklist:**
- [ ] ✅ All processes tracked through `IProcessManager`
- [ ] ✅ No direct `Process.Start()` calls
- [ ] ✅ WebView2 only for OAuth (not API calls)
- [ ] ✅ Monitor WebView2 BrowserProcessId if used
- [ ] ✅ Cleanup registered in `App.OnExit`

---

### Adding a New Trigger Type

**Code Changes:**
1. Add to `TriggerEngine.cs` event subscription logic
2. Add to `ActionDispatcher.cs` if special handling needed
3. Create event class in `Events/` if new event type

**Documentation Updates:**
1. **Update** `docs/services/trigger-engine.md` (add trigger type, conditions)
2. **Update** `docs/features/event-system.md` (add examples)
3. **Update** `docs/models/platform-events.md` (if new event)
4. **Update** `docs/services/action-dispatcher.md` (if dispatch logic changed)

---

### Changing Database Schema

**Code Changes:**
1. Update entity in `src/VyralBot.Data/Entities/`
2. Update `DapperDataService.cs` SQL statements
3. Update migration in `EnsureDatabaseCreatedAsync()`

**Documentation Updates:**
1. **Update** `docs/models/entities.md` (entity properties, relationships)
2. **Update** `docs/services/data-services-detailed.md` (SQL changes, schema)
3. **Update** `docs/architecture/data-layer.md` (if architectural change)
4. **Update** any ViewModels that use the entity (e.g., `platforms-viewmodel.md`)

---

### Modifying OAuth Flow

**Code Changes:**
1. Update `{Platform}OAuthService.cs`
2. Update `{Platform}WebSetupViewModel.cs` if UI changes
3. Update `{Platform}WebSetupView.xaml` if steps change

**Documentation Updates:**
1. **Update** `docs/services/{platform}-oauth-service.md` (flow diagrams, steps)
2. **Update** `docs/ui/{platform}-ui-viewmodels.md` (ViewModel changes)
3. **Update** `docs/ui/oauth-setup-wizards.md` (pattern changes)
4. **Update** `docs/features/{platform}-integration.md` (OAuth section)
5. (Security practices documented in this file and `docs/architecture/dependency-injection.md`)

---

## ✅ Documentation Quality Standards

### Every Documentation File Must Have:

1. **Title Header**
```markdown
# {Component Name} Documentation

## Overview
**Purpose:** Brief description
**Pattern:** MVVM/Service/Entity
**Responsibility:** What it does
```

2. **Dependencies Section**
```markdown
## Dependencies

\`\`\`csharp
public {ClassName}({params})
\`\`\`

| Dependency | Purpose |
|-----------|---------|
```

3. **Properties/Methods Tables**
```markdown
## Properties (X total)

| Property | Type | Purpose |
|----------|------|---------|

## Methods (X total)

| Method | Parameters | Returns | Purpose |
|--------|------------|---------|---------|
```

4. **Code Examples**
- Include actual working code from the project
- Show common usage patterns
- Demonstrate edge cases or important scenarios

5. **Flow Diagrams** (for complex logic)
```markdown
\`\`\`
Step 1: Action
  ├─→ Sub-action
  └─→ Result

Step 2: Next action
\`\`\`
```

6. **Integration Points**
```markdown
## Integration Points

**Called By:** List of components
**Calls:** List of dependencies
**Events:** Published/subscribed events
```

7. **Testing Strategy** (where applicable)
```markdown
## Testing Strategy

### Unit Test Examples
\`\`\`csharp
[Fact]
public void Method_Scenario_ExpectedBehavior()
{
    // Arrange, Act, Assert
}
\`\`\`
```

8. **Extension Templates** (for platform code)
```markdown
## YouTube Extension Template
\`\`\`csharp
// Template code for implementing in YouTube
\`\`\`
```

---

## 📚 Additional Reference

### Root-Level Reference Files
The project root contains key reference documents:

- **CONTEXT_MASTER.md** - Meta-document with critical rules, checklist, and reference matrix
- **DISTRIBUTION.md** - Guide for sharing/distributing VyralBot

**When to Reference:**
- Before implementing new features: Read CONTEXT_MASTER.md
- Distribution: Review DISTRIBUTION.md for packaging guidance
- Architecture decisions: Consult docs/architecture/ files

---

## 🎯 Success Metrics

### Performance Targets
- **Memory:** 40-80 MB working set
- **CPU (idle):** <1%
- **CPU (events):** 2-3%
- **GC Gen 0:** <5 collections/minute
- **GC Pauses:** <10ms (99th percentile)
- **Process Count:** 1 (VyralBot.exe only)
- **Child Processes:** 0 (unless tracked via ProcessManager)

### Build Quality
- **Build:** 0 errors, 0 warnings
- **Tests:** >80% code coverage
- **Startup Time:** <2 seconds
- **Documentation:** 100% of code documented

---

## ✅ Code Quality Standards

### Naming Conventions
- **PascalCase:** Classes, methods, properties, public fields, interfaces (prefix with `I`)
- **camelCase:** Private fields (prefix with `_`), local variables, parameters
- **Async Suffix:** All async methods end with `Async` (e.g., `ConnectAsync()`)

### Code Structure
- **File-Scoped Namespaces:** `namespace VyralBot.Core.Services;` (not blocks)
- **Nullable Reference Types:** Enabled in all projects
- **XML Comments:** Required for all public APIs
- **Regions:** Avoid unless absolutely necessary for large files

### Architecture Principles
- **SOLID:** Single Responsibility, Open/Closed, Liskov Substitution, Interface Segregation, Dependency Inversion
- **Clean Architecture:** Core → Integrations → UI (no reverse dependencies)
- **EventBus Communication:** All cross-module communication via EventBus (no direct service calls)
- **Async First:** All I/O operations async, no blocking

### Security
- **No Hardcoded Secrets:** Use configuration (`appsettings.json` or environment variables)
- **Input Validation:** Validate all user inputs (null checks, range checks, sanitization)
- **DPAPI Encryption:** Encrypt all tokens/credentials before storage
- **Portable Storage:** All user data in `appdata/` folder (NOT Windows AppData/Roaming)
- **HTTPS Only:** All external API calls via HTTPS
- **Rate Limiting:** Throttle external API calls to avoid bans
- **Localhost Default:** All network services bind to 127.0.0.1 by default

### Testing
- **Framework:** xUnit + Moq + FluentAssertions
- **Coverage:** >80% target
- **Naming:** `MethodName_Should_ExpectedBehavior_When_Condition`
- **Structure:** Arrange, Act, Assert
- **Mocks:** Mock external dependencies (APIs, databases, file system)

---

## 🚫 What NOT to Do

### ❌ DON'T:
1. **Use Windows AppData folder for storage**
   - ALWAYS use `AppDomain.CurrentDomain.BaseDirectory + "appdata"` via ProfileService
   - NEVER use `Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)`
   - Keep VyralBot fully portable - all data in local `appdata/` folder

2. **Make code changes without updating docs**
   - Every commit with code changes should include doc updates

2. **Create placeholder documentation**
   - "TODO: Document this later" → NO
   - Document it NOW or don't commit

3. **Leave outdated documentation**
   - If code changes, docs MUST change
   - Better to delete outdated docs than leave them wrong

4. **Duplicate information without cross-references**
   - Use links: `See [Data Service](services/data-service.md)`
   - Don't copy/paste entire sections

5. **Document implementation details that might change**
   - Document WHAT it does, not HOW it does it (unless HOW is important)
   - Focus on contracts, interfaces, public APIs

6. **Forget to update README.md**
   - It's the entry point - keep it current

7. **Remove files without documenting why**
   - Always update `deprecated-components.md` before deletion
   - Explain what replaced it

8. **Create documentation without verifying the code**
   - Read the actual source code
   - Run the code if possible
   - Verify method signatures, parameters, return types

---

## 🔍 Verification Checklist

### Before Completing ANY Task:

- [ ] All new code has corresponding documentation
- [ ] All modified code has updated documentation
- [ ] `docs/README.md` updated with new files
- [ ] Cross-references checked and updated
- [ ] Code examples tested and verified
- [ ] File counts and percentages accurate
- [ ] Status markers correct (✅ 🔄 ⚠️)
- [ ] All relative links work
- [ ] Tables formatted correctly
- [ ] Code blocks have language tags
- [ ] No placeholder/TODO markers left

### For Removals:

- [ ] Removal reason documented in `deprecated-components.md`
- [ ] All references removed from code BEFORE file deletion
- [ ] Build succeeds after removal
- [ ] All doc references updated or removed
- [ ] README.md updated
- [ ] Alternative/replacement documented

---

## 📊 File Count Maintenance

**Current Status:** 88 documentation files in docs/ (as of November 2026)

### When Adding Files:
1. Update `docs/README.md` progress indicators
2. Calculate new percentage: `(documented / total) * 100`
3. Update section counts (Architecture: X files, Features: Y files, etc.)

### When Removing Files:
1. Update totals in README
2. Recalculate percentages
3. Move to deprecated section if needed

---

## 🎨 Formatting Standards

### File Names:
- Lowercase with hyphens: `kick-oauth-service.md`
- Match code file names: `KickOAuthService.cs` → `kick-oauth-service.md`
- Descriptive: `data-services-detailed.md` not `data-services-2.md`

### Headers:
- `#` - File title only
- `##` - Major sections
- `###` - Subsections
- `####` - Details (avoid deeper nesting)

### Code Blocks:
```markdown
\`\`\`csharp
// Always specify language
public class Example { }
\`\`\`
```

### Links:
```markdown
[Text](relative/path/to/file.md)
[Section](file.md#section-anchor)
```

### Status Markers:
- ✅ Complete - Fully documented, current, accurate
- 🔄 In Progress - Partial documentation, being developed
- ⚠️ Deprecated - Legacy code, replaced, not recommended
- 🔜 Planned - Future feature, not yet implemented

---

## 💡 Documentation Philosophy

**"If it's not documented, it doesn't exist."**

This project treats documentation as first-class code:
- Documentation is NOT optional
- Documentation is NOT "nice to have"
- Documentation IS the specification
- Documentation ENABLES rapid development

**Goal:** Any developer (including future you) should be able to:
1. Understand the entire codebase from documentation alone
2. Make changes confidently without breaking existing features
3. Add new platforms/features following established patterns
4. Debug issues by understanding system architecture
5. Optimize performance using documented best practices

---

## 📊 Performance Monitoring

### Built-in Performance Monitor
VyralBot includes a `PerformanceMonitor` service that logs metrics every 30 seconds:

```
[INFO] Performance: Memory=45MB, GC/30s=12, Threads=10
```

**Healthy Metrics:**
- Memory: 40-80 MB
- GC/30s: <20 collections
- Threads: 8-15
- Active Processes: 0 (or known tracked count)

**Warning Signs:**
- Memory >150 MB → Possible memory leak, check for unreleased ArrayPool buffers
- GC/30s >30 → Allocation churn, reduce object creation
- Threads >20 → Thread leak, check for untracked background tasks
- Active Processes >3 → Process leak, verify ProcessManager cleanup

### Manual Performance Checks

**Task Manager:**
1. Open Task Manager (Ctrl+Shift+Esc)
2. Filter by "VyralBot"
3. Verify:
   - 1 process (VyralBot.exe)
   - Memory: 40-80 MB
   - CPU: <1% idle, 2-3% during events

**dotnet-counters (Advanced):**
```powershell
# Install tool
dotnet tool install -g dotnet-counters

# Monitor live metrics
dotnet-counters monitor --process-id <pid> --counters System.Runtime,Microsoft.AspNetCore.Hosting

# Watch for:
# - Gen 0 GC Count: <5/minute
# - Working Set: 40-80 MB
# - ThreadPool Completed Work Items/sec: smooth (no spikes)
```

### Performance Testing Scenarios

**1. Idle State:**
- No platforms connected
- Expected: <1% CPU, 40-50 MB memory

**2. Single Platform Connected:**
- Twitch IRC connected
- Expected: 1-2% CPU, 50-60 MB memory

**3. Heavy Event Load:**
- 100+ chat messages/second
- Expected: 2-5% CPU, 60-80 MB memory, no lag

**4. Multi-Platform:**
- Twitch + Kick both connected
- Expected: 3-5% CPU, 70-90 MB memory

---

## 🔧 Troubleshooting Common Issues

### High Memory Usage (>150 MB)

**Diagnosis:**
1. Check `PerformanceMonitor` logs for GC frequency
2. Look for `ArrayPool<byte>` buffers not returned
3. Check for large collections not being cleared
4. Verify WebView2 processes are tracked

**Solutions:**
- Use `ArrayPool<byte>.Shared.Return(buffer)` in `finally` blocks
- Clear large collections when done: `list.Clear()`, `dict.Clear()`
- Dispose `HttpClient` instances properly
- Kill WebView2 processes on disconnect

### High CPU Usage (>10%)

**Diagnosis:**
1. Check logging level (should be Info, not Debug)
2. Look for unlimited concurrent operations
3. Check for tight loops without `await Task.Delay()`
4. Profile with dotnet-trace

**Solutions:**
- Set logging to Info level in `appsettings.json`
- Throttle concurrency with `SemaphoreSlim(4, 4)`
- Add delays in polling loops: `await Task.Delay(100)`
- Use reactive patterns (EventBus) instead of polling

### Orphaned Processes

**Diagnosis:**
1. Open Task Manager
2. Look for processes without VyralBot parent
3. Check `ProcessManager` logs for PID tracking

**Solutions:**
- Verify `IProcessManager.StartProcessAsync()` used for all process creation
- Ensure `App.OnExit` calls `ProcessManager.KillAllProcessesAsync()`
- Add WebView2 `BrowserProcessId` to ProcessManager tracking
- Use `HttpClient` instead of WebView2 for API calls

### Slow OAuth Flow

**Diagnosis:**
- WebView2 taking >5 seconds to load
- Multiple WebView2 processes spawned

**Solutions:**
- Preload WebView2 runtime on app startup (background task)
- Cache WebView2 user data folder
- Use localhost redirect URLs (faster than waiting for cloud callbacks)

---

## 🎓 Learning Path for New Contributors

### Phase 1: Understanding (Week 1)
1. Read `CONTEXT_MASTER.md` (project root) - Critical rules
2. Review `docs/README.md` - Documentation map and project inventory
3. Study `docs/architecture/dependency-injection.md` - DI setup and project structure
4. Read `docs/architecture/event-bus.md` - Channel-based EventBus pattern
5. Read `docs/architecture/mvvm-pattern.md` - CommunityToolkit.Mvvm patterns

### Phase 2: Core Systems (Week 2)
1. `docs/features/profile-system.md` - Profile isolation
2. `docs/services/data-services-detailed.md` - Dapper/SQLite data access
3. `docs/services/trigger-engine.md` - Automation system
4. `docs/architecture/data-layer.md` - Data access architecture
5. `docs/application/app-entry.md` - App startup and DI registration

### Phase 3: Platform Integration (Week 3)
1. `docs/features/twitch-integration.md` - Reference implementation
2. `docs/services/twitch-service.md` - Service details
3. `docs/services/kick-oauth-service.md` - OAuth 2.1 with PKCE
4. `docs/ui/platforms-viewmodel.md` - Connection management
5. Try implementing YouTube integration following Twitch pattern

### Phase 4: Performance & Best Practices (Week 4)
1. Review performance guidelines in this file (copilot-instructions.md)
2. `docs/services/performance-monitor.md` - PerformanceMonitor, EventPool, PooledBuffer
3. `docs/services/process-management.md` - Zero orphans policy
4. Run performance tests, verify metrics
5. Profile with dotnet-counters, identify bottlenecks

---

## 🎯 Final Checklist: Am I Following VyralBot Standards?

### Before Every Commit:
- [ ] All code changes have documentation updates
- [ ] Build succeeds with 0 errors, 0 warnings
- [ ] Tests pass (if test project exists)
- [ ] Performance checklist verified (ArrayPool, throttling, etc.)
- [ ] Process management verified (no untracked processes)
- [ ] Logging is Info-level (no Debug spam)
- [ ] No blocking calls (.Result/.Wait())
- [ ] Collections pre-allocated with capacity
- [ ] `docs/README.md` updated if files added/removed
- [ ] Cross-references checked in affected docs

### Before Every Pull Request:
- [ ] All commits follow format: `<type>(<scope>): <subject>`
- [ ] Feature complete and tested
- [ ] Documentation complete and accurate
- [ ] Performance metrics within targets
- [ ] No security issues (secrets, validation, encryption)
- [ ] Code follows naming conventions
- [ ] Architecture boundaries respected
- [ ] EventBus used for cross-module communication

---

## 📞 Quick Reference

| Need | Check This File |
|------|----------------|
| Critical rules | `CONTEXT_MASTER.md` (project root) |
| Performance targets | `docs/copilot-instructions.md` (this file) |
| Process tracking | `docs/services/process-management.md` |
| DI setup / Project structure | `docs/architecture/dependency-injection.md` |
| EventBus pattern | `docs/architecture/event-bus.md` |
| Data layer | `docs/architecture/data-layer.md` |
| App startup | `docs/application/app-entry.md` |
| Profile system | `docs/features/profile-system.md` |
| Twitch integration | `docs/features/twitch-integration.md` |
| Kick integration | `docs/features/kick-integration.md` |
| All services | `docs/services/` folder (19 files) |
| All UI components | `docs/ui/` folder (34 files) |
| All models | `docs/models/` folder (4 files) |
| Documentation map | `docs/README.md` |

---

**Remember:** VyralBot is built on three pillars:
1. **📚 Documentation First** - If it's not documented, it doesn't exist
2. **⚡ Performance First** - Fastest, most efficient streaming automation platform
3. **🔒 Zero Orphans** - All processes tracked, monitored, and cleaned up

Follow these principles, and VyralBot will remain maintainable, performant, and reliable for all users.

---

*Last Updated: November 2026*
*Documentation Files: 88*

Goals:
1. Understand the entire system from docs alone
2. Implement new platforms by following templates
3. Debug issues by understanding architecture
4. Extend features without asking "how does this work?"

**When in doubt:**
- Over-document rather than under-document
- Add examples rather than just descriptions
- Link to related files rather than duplicate content
- Update immediately rather than "later"

---

## 🚀 Quick Commands

### Before Starting Work:
```powershell
# Verify all docs are current
cd docs
rg "TODO|FIXME|PLACEHOLDER" --type md
```

### After Making Changes:
```powershell
# Check for broken links (install markdown-link-check)
npx markdown-link-check docs/**/*.md

# Verify code references exist
rg "ClassName" src/**/*.cs  # Replace ClassName with what you documented
```

### Before Committing:
```powershell
# Build to verify no errors
dotnet build src/VyralBot.sln

# Check git diff includes doc updates
git diff --name-only | grep "docs/"
```

---

## 📞 When to Update This File

Update `copilot-instructions.md` when:
- Adding new documentation standards
- Changing project structure
- Adding new documentation categories
- Establishing new patterns
- Learning from mistakes (add to "Don't" section)

**This file itself should be treated as living documentation.**

---

## ✨ Summary

**Core Rule:** Code changes = Documentation changes. Always. No exceptions.

**Process:**
1. Plan change → Identify affected docs
2. Make code change
3. Update docs IMMEDIATELY
4. Update README.md
5. Verify cross-references
6. Check build succeeds
7. Review checklist

**Result:** Self-documenting codebase that scales, maintainable by anyone, serves as template for new platforms.

---

## 📦 Portable Architecture

### Storage Location Rules

**VyralBot uses a PORTABLE architecture - all user data stored in local `appdata/` folder.**

#### ✅ CORRECT Way to Access Storage:

```csharp
// Use ProfileService for all storage paths
public class MyService
{
    private readonly IProfileService _profileService;

    public MyService(IProfileService profileService)
    {
        _profileService = profileService;
    }

    public async Task SaveDataAsync()
    {
        var profile = await _profileService.GetCurrentProfileAsync();
        var dataPath = _profileService.GetProfileDataPath(profile.Name);
        var credentialsPath = _profileService.GetProfileCredentialsPath(profile.Name);

        // dataPath = {AppDirectory}/appdata/profiles/{ProfileName}/data/
        // credentialsPath = {AppDirectory}/appdata/profiles/{ProfileName}/
    }
}
```

#### ❌ FORBIDDEN - Windows AppData:

```csharp
// NEVER DO THIS - Breaks portability
var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
var path = Path.Combine(appDataPath, "VyralBot", "profiles");  // ❌ WRONG
```

### Why Portable Architecture?

1. **Easy Distribution**: Delete `appdata/` → Clean copy for GitHub/friends
2. **No Registry**: Zero Windows system modifications
3. **USB Compatible**: Run from USB drive or network share
4. **Easy Backup**: Copy `appdata/` folder = full backup
5. **Multi-User**: Each Windows user gets their own `appdata/`
6. **Developer Friendly**: Test fresh installs by deleting `appdata/`

### Distribution Workflow

```powershell
# Prepare clean distribution
Remove-Item -Recurse -Force .\appdata\
Remove-Item -Recurse -Force .\logs\
Remove-Item -Force .\run-debug.log -ErrorAction SilentlyContinue

# Verify .gitignore
# appdata/, logs/, run-debug.log should be ignored

# Commit and release
git add .
git commit -m "Clean release"
git push
```

### Storage Directory Structure

```
{AppDirectory}/
├── appdata/                     # All user data (git ignored)
│   ├── profiles/
│   │   └── {ProfileName}/
│   │       ├── profile.json     # Profile metadata (PIN, dates)
│   │       ├── data/
│   │       │   └── vyralbot.db  # SQLite database
│   │       ├── twitch_credentials.json  # Encrypted tokens
│   │       └── kick_credentials.json    # Encrypted tokens
│   ├── current_profile.txt      # Active profile
│   └── README.md                # User documentation
└── logs/                        # Application logs (git ignored)
```

### ProfileService Implementation

```csharp
// ProfileService constructor (simplified)
public ProfileService(ILogger<ProfileService> logger)
{
    // Portable: Use application directory, not Windows AppData
    var baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
    var portableDataPath = Path.Combine(baseDirectory, "appdata");

    _profilesRootPath = Path.Combine(portableDataPath, "profiles");
    _currentProfilePath = Path.Combine(portableDataPath, "current_profile.txt");

    Directory.CreateDirectory(_profilesRootPath);
}
```

### Migration from Old Versions

**Old versions** stored data in:
- `C:\Users\{User}\AppData\Roaming\VyralBot\profiles\`

**Current version** stores data in:
- `{VyralBot Directory}\appdata\profiles\`

Users can export/import profiles via UI to migrate data. OAuth tokens need re-authentication (DPAPI is machine-specific).

### Security Considerations

- OAuth tokens encrypted with Windows DPAPI (machine-specific)
- Moving `appdata/` to another PC requires re-authentication
- Safe to delete `appdata/` for clean distribution
- Profile exports remain encrypted (unusable on other machines without re-auth)

### See Also

- [DISTRIBUTION.md](../DISTRIBUTION.md) - Complete distribution guide
- [profile-service.md](services/profile-service.md) - ProfileService API

---

*Last Updated: November 2026*
*Documentation Files: 88*
*Project Status: Active Development*
