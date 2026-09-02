# 🚫 Process Management - Zero Orphans Policy

## Overview

VyralBot enforces a **strict zero-child-process policy** to ensure maximum performance with minimal resource usage. All external processes are tracked, logged, and cleaned up automatically.

---

## 🎯 Core Rules

### 1. Single Process Architecture
**VyralBot MUST run as a single process (`VyralBot.exe`) with NO child processes unless absolutely necessary.**

✅ **Target**: 1 process in Task Manager
❌ **Forbidden**: Multiple processes, orphaned child processes, WebView processes

### 2. ProcessManager Service
**ALL external processes MUST be managed through `IProcessManager`.**

```csharp
// ✅ RIGHT: Tracked process
private readonly IProcessManager _processManager;

public async Task RunToolAsync()
{
    var startInfo = new ProcessStartInfo { FileName = "tool.exe" };
    var process = await _processManager.StartProcessAsync(startInfo);
    // Process is tracked, logged, and will be killed on shutdown
}

// ❌ WRONG: Direct process creation
Process.Start("tool.exe"); // FORBIDDEN - not tracked!
```

### 3. Avoid WebView2
**WebView2 spawns 5+ child processes (20-30 MB each). Use HttpClient instead.**

| Need | ❌ WebView2 (Bad) | ✅ HttpClient (Good) |
|------|------------------|---------------------|
| **API Calls** | WebView2.CoreWebView2.ExecuteScriptAsync() | HttpClient.GetAsync() |
| **Process Count** | 6+ (parent + 5 children) | 1 (in-process) |
| **Memory** | 100-150 MB | 40-60 MB |
| **Performance** | Slow (browser startup) | Fast (instant) |

### 4. Shutdown Cleanup
**All spawned processes MUST be killed when app closes.**

```csharp
// In App.axaml.cs - Avalonia's Exit event
public override void OnFrameworkInitializationCompleted()
{
    if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
    {
        // Register shutdown handler - executes when X button clicked
        desktop.Exit += OnExit;
    }
}

private async void OnExit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
{
    // Kill all tracked processes
    var processManager = Services.GetService<IProcessManager>();
    if (processManager != null)
    {
        await processManager.KillAllProcessesAsync();
        await processManager.DisposeAsync();
    }
}
```

**This ensures 100% cleanup when:**
- User clicks the X button on window
- App exits via Alt+F4
- App terminates via Task Manager
- System shutdown

### 5. Lazy Process Creation
**Only spawn processes when needed, NOT on app startup.**

```csharp
// ❌ WRONG: Spawning on startup
public MyService(IProcessManager pm)
{
    _process = pm.StartProcessAsync(startInfo).Result; // Blocks + wastes resources
}

// ✅ RIGHT: Spawn on-demand
public async Task DoWorkAsync()
{
    if (_process == null || _process.HasExited)
    {
        _process = await _processManager.StartProcessAsync(startInfo);
    }
}
```

### 6. Process Monitoring
**Log process creation/exit with PID tracking.**

ProcessManager automatically logs:
```
[INFO] Process started: cmd (PID: 12345, Args: /c echo test)
[INFO] Process exited: cmd (PID: 12345)
```

PerformanceMonitor checks every 30s:
```
[INFO] Active Processes: 0
[WARNING] High process count detected - possible leak!
```

---

## 🔧 IProcessManager API

### Interface
```csharp
public interface IProcessManager : IAsyncDisposable
{
    // Start and track a process
    Task<Process> StartProcessAsync(ProcessStartInfo startInfo, CancellationToken ct = default);
    
    // Get all active processes
    IReadOnlyList<ProcessInfo> GetActiveProcesses();
    
    // Kill a specific process
    Task KillProcessAsync(int processId, CancellationToken ct = default);
    
    // Kill all processes (shutdown)
    Task KillAllProcessesAsync(CancellationToken ct = default);
}

public sealed record ProcessInfo(int ProcessId, string ProcessName, DateTime StartTime, bool IsRunning);
```

### Usage Example
```csharp
public class ExternalToolService
{
    private readonly IProcessManager _processManager;
    private readonly ILogger<ExternalToolService> _logger;
    private Process? _toolProcess;

    public ExternalToolService(IProcessManager processManager, ILogger<ExternalToolService> logger)
    {
        _processManager = processManager;
        _logger = logger;
    }

    public async Task StartToolAsync()
    {
        if (_toolProcess != null && !_toolProcess.HasExited)
        {
            _logger.LogWarning("Tool already running (PID: {ProcessId})", _toolProcess.Id);
            return;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = "external-tool.exe",
            Arguments = "--config config.json",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            CreateNoWindow = true
        };

        _toolProcess = await _processManager.StartProcessAsync(startInfo);
        _logger.LogInformation("Tool started (PID: {ProcessId})", _toolProcess.Id);
    }

    public async Task StopToolAsync()
    {
        if (_toolProcess != null && !_toolProcess.HasExited)
        {
            await _processManager.KillProcessAsync(_toolProcess.Id);
            _toolProcess = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopToolAsync();
    }
}
```

---

## 🚀 In-Process Alternatives

**Prefer in-process solutions over spawning external processes:**

| External Process | In-Process Alternative | Memory Savings |
|------------------|------------------------|----------------|
| **python.exe** | Python.NET | ~50 MB |
| **node.exe** | Jint (JavaScript) | ~40 MB |
| **csc.exe** | Roslyn (C#) | ~80 MB |
| **ffmpeg.exe** | NAudio, ImageSharp | ~30 MB |
| **chrome/msedgewebview2.exe** | HttpClient | ~120 MB |
| **7z.exe** | SharpZipLib | ~10 MB |

### Example: Scripting with Jint (In-Process)
```csharp
// ❌ WRONG: Spawn node.exe
var process = await _processManager.StartProcessAsync(new ProcessStartInfo
{
    FileName = "node.exe",
    Arguments = "script.js"
});

// ✅ RIGHT: Use Jint (in-process JavaScript)
using Jint;
var engine = new Engine();
engine.Execute(File.ReadAllText("script.js"));
// No external process, instant execution, 0 MB overhead
```

---

## 📊 Monitoring Process Count

### Manual Check (Task Manager)
1. Open Task Manager (Ctrl+Shift+Esc)
2. Filter by "VyralBot"
3. **Expected**: 1 process (VyralBot.exe)
4. **Warning**: >1 process = child process leak

### Programmatic Check
```csharp
var activeProcesses = _processManager.GetActiveProcesses();
if (activeProcesses.Count > 0)
{
    _logger.LogWarning("Active processes: {Count}", activeProcesses.Count);
    foreach (var proc in activeProcesses)
    {
        _logger.LogWarning("  - {ProcessName} (PID: {ProcessId})", 
            proc.ProcessName, proc.ProcessId);
    }
}
```

### Performance Metrics
```
Active Processes: 0          ✅ Perfect
Active Processes: 1-2        ⚠️ Check if necessary
Active Processes: 3+         🚫 Leak detected!
```

---

## ✅ Compliance Checklist

Before deploying code:

- [ ] ✅ No `Process.Start()` calls (use `IProcessManager`)
- [ ] ✅ No WebView2 usage (use HttpClient)
- [ ] ✅ All processes tracked and logged
- [ ] ✅ Shutdown handler registered (`AppDomain.ProcessExit`)
- [ ] ✅ Lazy process creation (not on startup)
- [ ] ✅ Process count monitored in PerformanceMonitor
- [ ] ✅ In-process alternatives used where possible
- [ ] ✅ All resources disposed (`IAsyncDisposable`)

---

## 🔍 Troubleshooting

### Problem: Orphaned Processes
**Symptom**: Processes remain in Task Manager after closing VyralBot

**Solution**:
1. Check if process was started through `IProcessManager`
2. Verify `AppDomain.ProcessExit` handler is registered
3. Ensure `ProcessManager.DisposeAsync()` is called on shutdown

### Problem: High Memory Usage
**Symptom**: VyralBot using >150 MB memory

**Solution**:
1. Check `_processManager.GetActiveProcesses()` count
2. Look for WebView2 processes (msedgewebview2.exe)
3. Replace WebView2 with HttpClient
4. Use in-process scripting engines (Jint, Roslyn)

### Problem: "Too Many Processes" Warning
**Symptom**: PerformanceMonitor logs "High process count detected"

**Solution**:
1. Review all `StartProcessAsync()` calls
2. Verify processes are being disposed
3. Check for missing `await` on `KillProcessAsync()`
4. Look for process leaks in plugins

---

## 📚 Documentation Files

- **CONTEXT_MASTER.md**: Master document referencing all rules
- **PERFORMANCE.md**: Performance optimization guide
- **PROCESS_MANAGEMENT.md**: This file (process-specific rules)
- **MIGRATION_GUIDE.md**: WPF → Avalonia migration (includes process handling)

---

## 🎯 Success Criteria

VyralBot process management is successful when:

1. ✅ **Task Manager shows 1 process** (VyralBot.exe only)
2. ✅ **Memory usage: 40-60 MB** (no child process overhead)
3. ✅ **No orphaned processes** after app close
4. ✅ **All process creation/exit logged** with PIDs
5. ✅ **Zero WebView2 usage** (unless absolutely necessary)
6. ✅ **In-process alternatives used** for scripting/tools

---

**VyralBot: Single process. Zero orphans. Maximum performance.** 🚀
