# 🔄 MIGRATION GUIDE - Integrating External Projects into VyralBot

**PURPOSE**: This document guides the migration of external projects/folders into VyralBot, ensuring compliance with all architecture, performance, and code standards defined in `CONTEXT_MASTER.md`.

---

## ⚠️ CRITICAL: Pre-Migration Checklist

Before migrating **ANY** external code:

1. ✅ **Read `CONTEXT_MASTER.md` first** - Understand all rules and standards
2. ✅ **Read `app_plan.md`** - Know the target architecture
3. ✅ **Read `PERFORMANCE.md`** - Understand performance requirements
4. ✅ **Read `IMPLEMENTATION.md`** - Know security and testing standards

---

## 📋 Migration Process

### Phase 1: Audit External Project

When you receive a folder/project to migrate, perform this audit:

#### 1.1 Project Analysis
```
- [ ] Framework: WPF / WinForms / Console?
- [ ] .NET Version: .NET Framework / .NET Core / .NET 5-9?
- [ ] Dependencies: What NuGet packages are used?
- [ ] Architecture: MVVM / MVC / Code-behind / Other?
- [ ] Features: What does this project do?
- [ ] Integration Points: OBS / Twitch / APIs / File System?
- [ ] Code Quality: Tests? Documentation? Warnings?
```

#### 1.2 Compatibility Check
```
✅ CAN MIGRATE DIRECTLY:
- .NET 6+ projects
- Async/await patterns
- Dependency injection usage
- MVVM architecture
- Modern C# (8.0+)

⚠️ NEEDS CONVERSION:
- .NET Framework → .NET 9
- Synchronous code → Async/await
- Static globals → Dependency injection
- Code-behind → MVVM ViewModels

🚫 REQUIRES MAJOR REFACTOR:
- Blocking I/O operations
- Unlimited concurrency
- Memory leaks (no disposal)
- Hardcoded secrets
- No error handling
```

---

## 🔧 Common Migration Scenarios

### Scenario A: WPF → Avalonia Migration

**Source**: (.NET Framework or .NET 8)
**Target**: (.NET 9)

#### Step 1: Analyze WPF Project Structure
```
📁 Source WPF Project/
├── 📄 App.xaml / App.xaml.cs
├── 📁 Views/ (Windows, UserControls)
├── 📁 ViewModels/
├── 📁 Models/
├── 📁 Services/
└── 📁 Resources/ (Styles, Images)
```

#### Step 2: Map to VyralBot Architecture
```
WPF Component          →  VyralBot Location
─────────────────────────────────────────────────────
App.xaml               →  src/App.UI/App.axaml
MainWindow.xaml        →  src/App.UI/Views/[Feature]View.axaml
ViewModels/            →  src/App.UI/ViewModels/
Services/APIs          →  src/App.Integrations/[Platform]/
Services/Business      →  src/App.Core/Services/
Models/Events          →  src/App.Core/EventBus/Events/
Models/DTOs            →  src/App.Core/Models/
Resources/Styles       →  src/App.UI/Styles/
```

#### Step 3: XAML Conversion Checklist

| WPF | Avalonia | Notes |
|-----|----------|-------|
| `<Window>` | `<Window>` | Same, but different namespace |
| `xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"` | `xmlns="https://github.com/avaloniaui"` | Change namespace |
| `<Grid>` | `<Grid>` | Same |
| `<StackPanel>` | `<StackPanel>` | Same |
| `<TextBlock>` | `<TextBlock>` | Same |
| `<Button>` | `<Button>` | Same |
| `<ListView>` | `<ListBox>` or `<DataGrid>` | Different controls |
| `<DataGrid>` | `<DataGrid>` | Requires `Avalonia.Controls.DataGrid` package |
| `{Binding}` | `{Binding}` | Same syntax |
| `Command={Binding}` | `Command={Binding}` | Same, use `CommunityToolkit.Mvvm` |
| `Style` | `Style` | Same concept, different resources |
| `Trigger` | `DataTrigger` or code | Limited trigger support |

#### Step 4: Code-Behind Migration

**WPF Code-Behind (❌ OLD)**:
```csharp
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        LoadData(); // Synchronous, blocks UI
    }
    
    private void Button_Click(object sender, RoutedEventArgs e)
    {
        // Event handler in code-behind
        MessageBox.Show("Clicked!");
    }
}
```

**Avalonia MVVM (✅ NEW)**:
```csharp
// View (MainWindow.axaml.cs)
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = App.ServiceProvider.GetRequiredService<MainViewModel>();
    }
}

// ViewModel (MainViewModel.cs)
public partial class MainViewModel : ViewModelBase
{
    private readonly IDataService _dataService;
    
    [ObservableProperty]
    private string _message = "Hello";
    
    public MainViewModel(IDataService dataService)
    {
        _dataService = dataService;
        _ = LoadDataAsync(); // Fire-and-forget initialization
    }
    
    [RelayCommand]
    private async Task OnButtonClickAsync()
    {
        // Command handler in ViewModel
        Message = "Clicked!";
        await _dataService.SaveAsync(Message);
    }
    
    private async Task LoadDataAsync()
    {
        // Async data loading
        var data = await _dataService.LoadAsync();
        Message = data;
    }
}
```

#### Step 5: Dependency Injection Migration

**WPF Static Access (❌ OLD)**:
```csharp
public class SomeService
{
    public void DoWork()
    {
        // Direct static access
        var config = ConfigManager.Instance.GetConfig();
        var logger = LogManager.GetLogger();
    }
}
```

**VyralBot DI (✅ NEW)**:
```csharp
public class SomeService
{
    private readonly IConfiguration _config;
    private readonly ILogger<SomeService> _logger;
    
    public SomeService(IConfiguration config, ILogger<SomeService> logger)
    {
        _config = config;
        _logger = logger;
    }
    
    public void DoWork()
    {
        // Injected dependencies
        var setting = _config.GetValue<string>("SomeSetting");
        _logger.LogInformation("Working...");
    }
}

// Register in Program.cs
services.AddSingleton<SomeService>();
```

---

### Scenario B: .NET 8 → .NET 9 Migration

#### Step 1: Update Project Files
```xml
<!-- OLD (.NET 8) -->
<TargetFramework>net8.0</TargetFramework>

<!-- NEW (.NET 9) -->
<TargetFramework>net9.0</TargetFramework>
```

#### Step 2: Update Dependencies
```bash
# Update all packages to .NET 9 compatible versions
dotnet add package Microsoft.Extensions.Hosting --version 9.0.0
dotnet add package Avalonia --version 11.1.3
```

#### Step 3: Code Changes (if any)
- Most .NET 8 code is .NET 9 compatible
- Check for obsolete APIs: https://learn.microsoft.com/en-us/dotnet/core/compatibility/9.0

---

### Scenario C: Integration/Adapter Migration

If migrating an **integration** (OBS, Twitch, YouTube, etc.):

#### Step 1: Create Adapter Interface
```csharp
// src/App.Integrations/[Platform]/I[Platform]Adapter.cs
public interface ITwitchAdapter
{
    Task<bool> ConnectAsync(TwitchConfig config, CancellationToken ct = default);
    Task DisconnectAsync();
    Task<TwitchUser> GetUserAsync(string username);
    event EventHandler<TwitchConnectionState>? ConnectionStateChanged;
}
```

#### Step 2: Implement Adapter with Performance Rules
```csharp
// src/App.Integrations/[Platform]/[Platform]Adapter.cs
public class TwitchAdapter : ITwitchAdapter
{
    private readonly IEventBus _eventBus;
    private readonly ILogger<TwitchAdapter> _logger;
    private readonly HttpClient _httpClient;
    private readonly SemaphoreSlim _rateLimiter = new(800, 800); // Twitch rate limit
    
    public TwitchAdapter(IEventBus eventBus, ILogger<TwitchAdapter> logger, HttpClient httpClient)
    {
        _eventBus = eventBus;
        _logger = logger;
        _httpClient = httpClient;
    }
    
    public async Task<TwitchUser> GetUserAsync(string username)
    {
        // CRITICAL: Throttle API calls
        await _rateLimiter.WaitAsync();
        try
        {
            // Use pooled buffer if reading response
            var buffer = ArrayPool<byte>.Shared.Rent(4096);
            try
            {
                // Make API call
                var response = await _httpClient.GetAsync($"https://api.twitch.tv/helix/users?login={username}");
                response.EnsureSuccessStatusCode();
                
                // Parse response
                var user = await response.Content.ReadFromJsonAsync<TwitchUser>();
                
                // Publish event (fire-and-forget)
                await _eventBus.PublishAsync(new TwitchUserFetchedEvent(user));
                
                return user!;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch Twitch user: {Username}", username);
            throw;
        }
        finally
        {
            _rateLimiter.Release();
        }
    }
}
```

#### Step 3: Create Events
```csharp
// src/App.Integrations/[Platform]/Events/[Platform]Events.cs
public sealed record TwitchUserFetchedEvent(TwitchUser User) : BaseEvent;
public sealed record TwitchFollowerEvent(string FollowerName) : BaseEvent;
public sealed record TwitchSubscriptionEvent(string SubscriberName, int Tier) : BaseEvent;
```

#### Step 4: Register in DI
```csharp
// src/App.UI/Program.cs
services.AddSingleton<ITwitchAdapter, TwitchAdapter>();
services.AddHttpClient<TwitchAdapter>(client =>
{
    client.BaseAddress = new Uri("https://api.twitch.tv/");
    client.DefaultRequestHeaders.Add("Client-ID", "<from-config>");
});
```

---

## 🎯 Migration Rules (from CONTEXT_MASTER.md)

### Architecture Rules
✅ **DO**:
- Place adapters in `src/App.Integrations/[Platform]/`
- Use EventBus for all cross-module communication
- Implement interfaces (`I[Name]Adapter`, `I[Name]Service`)
- Register everything in DI container
- Use MVVM for UI (ViewModel → View)

❌ **DON'T**:
- Create reverse dependencies (UI → Core is OK, Core → UI is NOT)
- Use static singletons or globals
- Put business logic in code-behind
- Make synchronous blocking calls

### Performance Rules
✅ **DO**:
- Use `ArrayPool<byte>` for buffers >= 1KB
- Throttle concurrency with `SemaphoreSlim`
- Pre-allocate collections: `new List<T>(capacity)`
- Use `async`/`await` for all I/O
- Fire-and-forget non-critical operations: `_ = Task.Run(...)`
- Log at `LogLevel.Information` only

❌ **DON'T**:
- Allocate `new byte[size]` repeatedly
- Use `Task.WhenAll` without concurrency limit
- Block with `.Result` or `.Wait()`
- Use `LogLevel.Debug` in production code
- Create unbounded channels or queues

### Security Rules
✅ **DO**:
- Validate all inputs (null checks, range checks)
- Use configuration for secrets (no hardcoding)
- Rate limit external APIs
- Use HTTPS for all external calls
- Dispose resources (`using` or `IAsyncDisposable`)

❌ **DON'T**:
- Trust external input without validation
- Hardcode API keys or tokens
- Make unlimited API calls
- Use HTTP (always HTTPS)
- Leave connections open indefinitely

---

## 📝 Migration Workflow

When user says: **"Migrate [folder/project] into VyralBot"**

### Step 1: Audit
```
1. Read CONTEXT_MASTER.md
2. Scan the folder structure
3. Identify framework (WPF/WinForms/Console)
4. Identify .NET version
5. List all dependencies (NuGet packages)
6. Identify features/integrations
7. Check for code quality (tests, warnings, style)
```

### Step 2: Plan
```
1. Map source files to VyralBot architecture
2. Identify required conversions:
   - WPF → Avalonia
   - .NET 8 → .NET 9
   - Sync → Async
   - Static → DI
   - Code-behind → MVVM
3. List performance violations to fix
4. List security issues to fix
5. Determine if new modules are needed
```

### Step 3: Execute
```
1. Create target folders in VyralBot structure
2. Convert XAML (if WPF)
3. Convert code-behind to ViewModels
4. Implement interfaces for adapters/services
5. Apply performance patterns (ArrayPool, SemaphoreSlim, etc.)
6. Apply security patterns (validation, rate limiting)
7. Register in DI (Program.cs)
8. Create events for EventBus
9. Write unit tests
10. Update app_plan.md with new modules
```

### Step 4: Verify
```
1. Run: dotnet build (fix all warnings)
2. Run: dotnet test (all tests must pass)
3. Check: Memory usage (40-80 MB target)
4. Check: CPU usage (<1% idle, 2-3% active)
5. Check: No blocking calls (.Result or .Wait)
6. Check: All inputs validated
7. Check: All resources disposed
```

### Step 5: Document
```
1. Update app_plan.md (mark module as implemented)
2. Add API documentation (if public APIs added)
3. Update README.md (if new features added)
```

---

## 🔍 Migration Audit Template

Use this when analyzing external projects:

```markdown
# Migration Audit: [Project Name]

## Source Analysis
- **Framework**: WPF / WinForms / Avalonia / Console
- **Target Framework**: net4.8 / net6.0 / net7.0 / net8.0
- **.NET Standard**: 2.0 / 2.1 / N/A
- **Architecture**: MVVM / MVC / Code-behind / Other
- **UI Framework**: WPF / WinForms / Avalonia / None
- **DI Framework**: None / Microsoft.Extensions.DI / Autofac / Other

## Dependencies
- [ ] List all NuGet packages
- [ ] Check .NET 9 compatibility
- [ ] Identify Avalonia equivalents (if WPF)

## Features
- [ ] Feature 1: Description
- [ ] Feature 2: Description
- [ ] Integration points: OBS / Twitch / APIs

## Code Quality
- [ ] Unit tests present? (Y/N)
- [ ] Build warnings? (Count)
- [ ] Async/await used? (Y/N)
- [ ] Error handling? (Y/N)
- [ ] Logging present? (Y/N)

## Performance Issues
- [ ] Blocking calls (.Result, .Wait)
- [ ] Unbounded concurrency (Task.WhenAll)
- [ ] Memory leaks (no disposal)
- [ ] Large allocations (new byte[])
- [ ] Synchronous I/O

## Security Issues
- [ ] Hardcoded secrets
- [ ] No input validation
- [ ] No rate limiting
- [ ] HTTP (not HTTPS)
- [ ] SQL injection risks

## Migration Plan
1. Convert WPF → Avalonia: [List files]
2. Convert .NET 8 → .NET 9: [List projects]
3. Add async/await: [List methods]
4. Add DI: [List services]
5. Fix performance: [List issues]
6. Fix security: [List issues]
7. Add tests: [List coverage gaps]

## Target VyralBot Modules
- [ ] App.Core: [Services to add]
- [ ] App.Integrations: [Adapters to add]
- [ ] App.UI: [Views to add]
- [ ] App.Plugins: [Plugins to add]

## Estimated Effort
- Audit: X hours
- Conversion: X hours
- Testing: X hours
- Documentation: X hours
**Total**: X hours
```

---

## ✅ Migration Success Criteria

Migration is complete when:

- [ ] ✅ All code builds without warnings (`dotnet build`)
- [ ] ✅ All tests pass (`dotnet test`)
- [ ] ✅ Memory usage: 40-80 MB
- [ ] ✅ CPU usage: <1% idle, 2-3% active
- [ ] ✅ No blocking calls (`.Result`, `.Wait()`)
- [ ] ✅ ArrayPool used for buffers >= 1KB
- [ ] ✅ Concurrency throttled (SemaphoreSlim)
- [ ] ✅ All inputs validated
- [ ] ✅ All resources disposed (`using`, `IAsyncDisposable`)
- [ ] ✅ EventBus used for cross-module communication
- [ ] ✅ DI used (no static singletons)
- [ ] ✅ MVVM pattern for UI (no code-behind logic)
- [ ] ✅ Unit tests written (80%+ coverage)
- [ ] ✅ `app_plan.md` updated
- [ ] ✅ Documentation updated (if needed)

---

## 🚀 Quick Migration Command

**User Prompt Template**:
```
Read CONTEXT_MASTER.md and MIGRATION_GUIDE.md, then migrate the project in [folder path] into VyralBot.

The source project is:
- Framework: [WPF/WinForms/Console]
- .NET Version: [net8.0/net6.0/etc]
- Features: [Description]

Perform full audit, plan, execute, verify, and document.
```

---

**This guide ensures all migrated code follows VyralBot standards for performance, security, and architecture.** 🎯
