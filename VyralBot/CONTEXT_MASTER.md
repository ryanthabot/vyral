# 🎯 CONTEXT MASTER - Required Reading for All Development

**This document ensures VyralBot remains the fastest, most maintainable, and most secure streaming automation platform.**

**READ THIS DOCUMENT FIRST. FOLLOW THE RULES. BUILD AMAZING SOFTWARE.** 🚀

**USE MCP TOOLS AS NEEDED**

**PURPOSE**: This document is the **single source of truth** for what must be read and followed when working on VyralBot. Every AI agent/developer working on this codebase **MUST** read all documents listed here before making any changes.

---

## ⚠️ CRITICAL: Read These Documents FIRST

Before making **ANY** code changes, architecture decisions, or implementations, you **MUST** read these documents in order:

### 1️⃣ **Architecture & Design** (READ FIRST)
```
📄 app_plan.md
```
- **WHY**: Complete architecture blueprint with text diagrams, module responsibilities, folder structure
- **WHAT TO CHECK**: Module boundaries, dependency rules, interface contracts, implementation status
- **WHEN**: Before adding any new feature, module, or integration

### 2️⃣ **Performance Requirements** (CRITICAL)
```
📄 docs/PERFORMANCE.md
📄 OPTIMIZATIONS.md
📄 PROCESS_MANAGEMENT.md
```
- **WHY**: VyralBot must be the **fastest possible** program with minimal CPU/memory usage and **zero child processes**
- **WHAT TO CHECK**:
  - Use ArrayPool<byte> for buffers
  - Limit concurrency (EventBus: 4 handlers)
  - Use Channel.BoundedOptions with DropOldest
  - Enable low-latency GC mode
  - Pre-allocate collections with capacity
  - Fire-and-forget for non-critical operations
  - Info-level logging only (not Debug)
  - **Track all processes in ProcessManager** (no direct Process.Start)
  - **Use HttpClient** instead of WebView for APIs
  - **Kill all processes on shutdown**
- **WHEN**: Before writing any async code, collections, network operations, UI updates, or spawning processes

### 3️⃣ **Implementation Guidelines**
```
📄 IMPLEMENTATION.md
📄 CONTRIBUTING.md
```
- **WHY**: Code style, security patterns, testing requirements, PR guidelines
- **WHAT TO CHECK**:
  - Naming conventions (PascalCase classes, camelCase private fields)
  - Security: No hardcoded secrets, validate all inputs
  - Testing: Unit tests required for all public APIs
  - Async/await patterns (no .Result or .Wait())
- **WHEN**: Before writing any new class, method, or test

### 4️⃣ **Build & Environment**
```
📄 BUILD.md
📄 README.md
```
- **WHY**: Project setup, dependencies, build commands, troubleshooting
- **WHAT TO CHECK**:
  - .NET 9.0 target (net9.0)
  - CommunityToolkit.Mvvm for MVVM
  - xUnit + Moq + FluentAssertions for testing
- **WHEN**: Before adding NuGet packages, changing target frameworks, or modifying project files

---

## 📋 Development Workflow Checklist

For **EVERY** feature/change, follow this checklist:

### ✅ Before Starting
- [ ] Read relevant sections of `app_plan.md` for the module you're modifying
- [ ] Review `PERFORMANCE.md` for optimization patterns
- [ ] Check `IMPLEMENTATION.md` for security and coding standards
- [ ] Verify the feature doesn't already exist (search codebase)

### ✅ During Implementation
- [ ] Follow performance guidelines (ArrayPool, bounded channels, low allocations)
- [ ] Use async/await properly (no blocking calls)
- [ ] Apply MVVM pattern for UI (ViewModel → View binding)
- [ ] Use dependency injection (IServiceCollection, IHostBuilder)
- [ ] Log at Info level only (not Debug unless debugging)
- [ ] Handle all exceptions gracefully (try-catch with logging)
- [ ] Validate all inputs (null checks, range checks)
- [ ] Track all spawned processes (log PID, handle disposal, kill on shutdown)
- [ ] Avoid WebView/browser components unless absolutely necessary(meaning if the user wants inline use webview2 like for the setup wizard and things that its important to not leave the app)
- [ ] Monitor child processes (no orphaned processes allowed)

### ✅ After Implementation
- [ ] Write unit tests (aim for 80%+ coverage)
- [ ] Run `dotnet build` and fix all warnings
- [ ] Run `dotnet test` and ensure all pass
- [ ] Check memory usage (should be 40-80 MB)
- [ ] Update documentation if adding public APIs
- [ ] Update `app_plan.md` implementation status if completing a module

---

## 🚫 FORBIDDEN PATTERNS (Never Do This)

Reading `PERFORMANCE.md` and `OPTIMIZATIONS.md` will reveal these **BANNED** patterns:

❌ **Memory/Performance Violations**
```csharp
// WRONG: Allocates new array every time
byte[] buffer = new byte[8192];

// RIGHT: Use ArrayPool
var buffer = ArrayPool<byte>.Shared.Rent(4096);
try { /* use buffer */ }
finally { ArrayPool<byte>.Shared.Return(buffer); }
```

❌ **Concurrency Violations**
```csharp
// WRONG: Unlimited parallel execution
await Task.WhenAll(items.Select(ProcessAsync));

// RIGHT: Throttle with SemaphoreSlim
var semaphore = new SemaphoreSlim(4, 4);
await Parallel.ForEachAsync(items, new ParallelOptions { MaxDegreeOfParallelism = 4 }, async (item, ct) => {
    await semaphore.WaitAsync(ct);
    try { await ProcessAsync(item); }
    finally { semaphore.Release(); }
});
```

❌ **Blocking Violations**
```csharp
// WRONG: Blocks thread pool thread
var result = SomeAsyncMethod().Result;
SomeAsyncMethod().Wait();

// RIGHT: Use async/await
var result = await SomeAsyncMethod();
```

❌ **Collection Violations**
```csharp
// WRONG: No capacity = multiple resizes
var list = new List<Event>();

// RIGHT: Pre-allocate capacity
var list = new List<Event>(capacity: 100);
```

❌ **Logging Violations**
```csharp
// WRONG: Debug level logging everywhere
_logger.LogDebug("Processing {Count} items", items.Count);

// RIGHT: Info for important events only
_logger.LogInformation("Connected to OBS Studio");
```

❌ **Process Management Violations**
```csharp
// WRONG: Spawning processes without tracking
Process.Start("external.exe");

// WRONG: WebView without process monitoring
var webView = new WebView2();

// RIGHT: Track and dispose all processes
var process = new Process { ... };
_activeProcesses.Add(process);
process.Exited += (s, e) => _activeProcesses.Remove(process);
process.Start();
_logger.LogInformation("Started process {ProcessName} (PID: {ProcessId})", process.ProcessName, process.Id);

// RIGHT: Dispose WebView and monitor child processes
await webView.EnsureCoreWebView2Async();
_logger.LogInformation("WebView2 initialized (PID: {ProcessId})", webView.CoreWebView2.BrowserProcessId);
// Always dispose when done
await webView.CoreWebView2.ExecuteScriptAsync("/* cleanup */");
webView.Dispose();
```

---

## 📚 Document Reference Matrix

| Task | Must Read | Optional Read |
|------|-----------|---------------|
| **Add new integration** (Twitch, YouTube, etc.) | app_plan.md (Module 4), PERFORMANCE.md, IMPLEMENTATION.md | BUILD.md |
| **Modify EventBus** | app_plan.md (Module 1), PERFORMANCE.md (concurrency), OPTIMIZATIONS.md | CONTRIBUTING.md |
| **Add UI view/feature** | app_plan.md (Module 2), PERFORMANCE.md (UI optimization), CONTRIBUTING.md | README.md |
| **Create plugin** | app_plan.md (Module 3), IMPLEMENTATION.md (security), CONTRIBUTING.md | docs/plugin-api.md |
| **Add scripting engine** | app_plan.md (Module 6), PERFORMANCE.md (pooling), IMPLEMENTATION.md, PROCESS_MANAGEMENT.md | - |
| **Performance tuning** | PERFORMANCE.md, OPTIMIZATIONS.md, PROCESS_MANAGEMENT.md, app_plan.md | - |
| **Security review** | IMPLEMENTATION.md (security), CONTRIBUTING.md, app_plan.md | - |
| **Write tests** | IMPLEMENTATION.md (testing), CONTRIBUTING.md | BUILD.md |
| **Fix bugs** | IMPLEMENTATION.md, PERFORMANCE.md | app_plan.md, BUILD.md |
| **Spawn external process** | PROCESS_MANAGEMENT.md (critical), PERFORMANCE.md, IMPLEMENTATION.md | - |

---

## 🎯 Critical Rules Summary

### Architecture Rules (from `app_plan.md`)
1. **Module Boundaries**: Core → Integrations → Plugins → UI (no reverse dependencies)
2. **EventBus Communication**: All modules communicate via EventBus (no direct calls)
3. **Plugin Isolation**: AssemblyLoadContext isolation, permission-based security
4. **Dependency Injection**: Everything registered in Program.cs via IServiceCollection
5. **Process Management**: All external processes tracked in ProcessManager service, logged on create/exit, disposed properly

### Performance Rules (from `PERFORMANCE.md`)
1. **Max 4 concurrent event handlers** (EventBus throttling)
2. **Use ArrayPool<byte>** for all buffers >= 1KB
3. **Low-latency GC mode** enabled
4. **Pre-allocate collections** with capacity
5. **Fire-and-forget** for non-critical operations
6. **Info-level logging** only (not Debug)
7. **Bounded channels** with DropOldest backpressure
8. **No untracked processes** - All Process/WebView instances tracked, logged, and disposed
9. **Lazy process creation** - Only spawn processes when absolutely needed
10. **Process cleanup** - All child processes must be killed on app shutdown

### Security Rules (from `IMPLEMENTATION.md`)
1. **No hardcoded secrets** (use configuration)
2. **Validate all inputs** (null checks, range checks, sanitization)
3. **Plugin sandboxing** (limited permissions)
4. **HTTPS only** for external APIs
5. **Rate limiting** on all external calls

### Code Style Rules (from `CONTRIBUTING.md`)
1. **PascalCase**: Classes, methods, properties, public fields
2. **camelCase**: Private fields (with `_` prefix), local variables, parameters
3. **Async suffix**: All async methods end with "Async"
4. **Interfaces**: Prefix with "I" (IEventBus, IPlugin)
5. **File scoped namespaces**: `namespace App.Core.EventBus;`

---

## 🔄 When to Update This Document

Update `CONTEXT_MASTER.md` when:
- Adding a new major documentation file (e.g., API_SPEC.md, DEPLOYMENT.md)
- Discovering a new critical pattern/anti-pattern
- Architecture rules change (e.g., new module added)
- Performance requirements change (e.g., new optimization targets)

---

## 🚀 Quick Start for AI Agents

**Every time you receive a prompt about VyralBot:**

1. **Read this file first** (CONTEXT_MASTER.md)
2. **Identify the task type** (see matrix above)
3. **Read the required documents** for that task type
4. **Apply the rules** from those documents
5. **Verify compliance** before responding

**Example:**
- **Prompt**: "Add Twitch integration"
- **Read**: app_plan.md (Module 4), PERFORMANCE.md, IMPLEMENTATION.md
- **Check**: EventBus usage, ArrayPool buffers, rate limiting, security
- **Implement**: ITwitchAdapter, TwitchAdapter, TwitchConfig, Events
- **Test**: Unit tests with Moq, verify no blocking calls
- **Update**: app_plan.md implementation status

---

## 📖 Document Locations

All documents are in the repository root unless specified:

```
📁 VyralBot/
├── 📄 CONTEXT_MASTER.md          ← YOU ARE HERE
├── 📄 app_plan.md                ← Architecture blueprint
├── 📄 PERFORMANCE.md             ← Performance guide (root)
├── 📄 OPTIMIZATIONS.md           ← Optimization summary
├── 📄 PROCESS_MANAGEMENT.md      ← Process tracking rules (CRITICAL)
├── 📄 IMPLEMENTATION.md          ← Security, testing, code style
├── 📄 CONTRIBUTING.md            ← Contribution guidelines
├── 📄 BUILD.md                   ← Build instructions
├── 📄 README.md                  ← Project overview
├── 📄 LICENSE                    ← MIT License
├── 📁 docs/
│   └── 📄 PERFORMANCE.md         ← Detailed performance guide
└── 📁 src/
    └── ...                       ← Source code
```

---

## ✅ Compliance Verification

Before submitting any code, verify:

- [ ] ✅ Read all required documents for your task type
- [ ] ✅ Followed performance guidelines (ArrayPool, bounded channels, etc.)
- [ ] ✅ No forbidden patterns (blocking calls, unlimited concurrency, etc.)
- [ ] ✅ Applied code style rules (PascalCase, async suffix, etc.)
- [ ] ✅ Security patterns applied (input validation, no secrets, etc.)
- [ ] ✅ Unit tests written (80%+ coverage)
- [ ] ✅ Build succeeds (`dotnet build`)
- [ ] ✅ Tests pass (`dotnet test`)
- [ ] ✅ Documentation updated (if adding public APIs)

---
