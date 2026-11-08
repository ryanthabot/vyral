# Application Entry Point

## Overview
WPF application startup with Microsoft.Extensions.Hosting for DI, Serilog logging, and profile-aware initialization.

## Source Files

### App.xaml.cs
- **File:** `src/VyralBot.UI/App.xaml.cs` (~557 lines)
- **Namespace:** `VyralBot.UI`
- **Class:** `App : Application`

### Startup Sequence
```
App Constructor
  → Check single instance (Mutex)
  → Configure Serilog (console + file)
  → Host.CreateDefaultBuilder()
    → UseSerilog()
    → ConfigureWebHostDefaults (Kestrel, port 5000)
    → ConfigureServices (60+ registrations)

App.OnStartup
  → (fire-and-forget) WebView2 leftover kill + temp-cache clear (background task, bounded 5s)
  → Start host
  → Initialize database (DapperDataService)
  → Start TriggerEngine
  → Show MainWindow
  → Check profile state (show ProfileSelector if needed)
```

### DI Registration Summary (60+ services)
**Singletons:**
- Core: `IEventBus` → `EventBus`, `IProfileService` → `ProfileService`, `IDataService` → `ProfileAwareDataService`
- Integrations: `ITwitchService` → `TwitchService`, `IKickService` → `KickLibChatService`, `ITwitchEventSubService` → `TwitchEventSubService`, `TwitchOAuthService`, `KickLibOAuthService`
- Services: `IActionDispatcher` → `ActionDispatcher`, `ITriggerEngine` → `TriggerEngine`, `IChatMessageStore` → `InMemoryChatMessageStore`, `IChatEventSettingsService` → `ChatEventSettingsService`, `IAutoReplyService` → `AutoReplyService`, `IRuleEvaluator` → `RuleEvaluator`, `ICommandService` → `CommandService`, `ICurrencyService` → `CurrencyService`, `ICurrencyCommandService` → `CurrencyCommandService`, `ICurrencyRewardsService` → `CurrencyRewardsService`, `ISoundService` → `SoundService`, `IAudioWebSocketServer` → `AudioWebSocketServer`, `IVariableReplacementService` → `VariableReplacementService`, `IStatisticsService` → `StatisticsService`
- Standalone: `GiveawayService`, `GiveawayCommandService`, `PollService`, `PollCommandService`, `WatchtimeService`, `WatchtimeCommandService`
- Background: `PerformanceMonitor` (hosted), `WatchtimeBackgroundService` (hosted)
- ViewModels (Singleton): `MainViewModel`, `DashboardViewModel`, `ChatViewModel`, `PlatformsViewModel`, `SettingsViewModel`, `KickSettingsViewModel`, `KickDashboardViewModel`, `TwitchDashboardViewModel`, `AudioSettingsViewModel`, `SoundsViewModel`, `AlertSettingsViewModel`, `StatisticsViewModel`, `EventHistoryViewModel`, `ConditionalRulesViewModel`, `CommandsViewModel`, `CurrencyViewModel`, `GiveawayViewModel`, `PollViewModel`, `WatchtimeViewModel`, `EventTestingViewModel`, `WebServerSettingsViewModel`, `VariablesViewModel`, `TwitchChatSettingsViewModel`, `KickChatSettingsViewModel`, `YoutubeChatSettingsViewModel`, `TiktokChatSettingsViewModel`, `DiscordChatSettingsViewModel`, `VariablePickerViewModel`, `KickWebSetupViewModel`, `TwitchWebSetupViewModel`, `DemoSetupViewModel`

**Transient:**
- `ProfileSelectorViewModel`, `GoogleDemoSetupViewModel`, `NgrokSetupWizardViewModel`

### Web Server (Kestrel)
- Port 5000
- Handles webhook callbacks and API endpoints
- Configured via `ConfigureWebHostDefaults`

### Serilog Configuration
- Console sink
- File sink (rolling, in appdata or project root)
- Minimum level: Information

## Profile Selection Flow
```
OnStartup
  → IProfileService.GetProfilesAsync()
  → If no profiles: create default "Default" profile
  → If multiple profiles (and no auto-login): show ProfileSelectorWindow
  → If single profile or auto-login: proceed directly
  → Initialize database for selected profile
```

## All Files
| File | Description |
|------|-------------|
| `src/VyralBot.UI/App.xaml` | Application XAML (resource dictionaries) |
| `src/VyralBot.UI/App.xaml.cs` | Application startup and DI (557 lines) |
