# Dependency Injection Architecture

## Hosting Pattern

VyralBot uses the .NET Generic Host (`Host.CreateDefaultBuilder()`) to wire up all services, configure logging, and host the Kestrel web server. The host is built inside the `App()` constructor in `App.xaml.cs`, **not** in `OnStartup`.

```csharp
_host = Host.CreateDefaultBuilder()
    .UseSerilog()               // Structured logging via Serilog
    .ConfigureServices((ctx, services) =>
    {
        // All registrations happen here
    })
    .Build();
```

- **Serilog** is wired in via `.UseSerilog()` for structured logging.
- **Kestrel** runs as part of the host on port 5000, serving webhook endpoints for Kick and other integrations.
- The built host is stored in `_host`; its service provider is exposed as `ServiceProvider = _host.Services` during startup.

---

## Service Registrations by Category

All registrations below are **Singleton** unless noted otherwise.

### Profile Service

| Interface | Implementation |
|-----------|---------------|
| `IProfileService` | `ProfileService` |

### Database (Profile-Aware)

| Interface | Implementation |
|-----------|---------------|
| `IDataService` | `ProfileAwareDataService` |

Uses the current profile to resolve the correct SQLite database path so each profile has isolated data.

### Core Services

| Interface / Type | Implementation | Lifetime |
|-----------------|---------------|----------|
| `IEventBus` | `EventBus` | Singleton |
| `IChatMessageStore` | `VyralBot.UI.Services.Chat.DataChatMessageStore` | Singleton |
| `IChatEventSettingsService` | `ChatEventSettingsService` | Singleton |
| `IRuleEvaluator` | `RuleEvaluator` | Singleton |
| `IAutoReplyService` | `AutoReplyService` | Singleton |
| `IActionDispatcher` | `ActionDispatcher` | Singleton |
| `ITriggerEngine` | `TriggerEngine` | Singleton |
| `IProcessManager` | `ProcessManager` | Singleton |
| `PerformanceMonitor` | — | Hosted Service |

### Currency System

| Interface / Type | Implementation |
|-----------------|---------------|
| `ICurrencyService` | `CurrencyService` |
| `ICurrencyRewardsService` | `CurrencyRewardsService` |
| `ICurrencyCommandService` | `CurrencyCommandService` |

### Giveaway System

| Type |
|------|
| `GiveawayService` |
| `GiveawayCommandService` |

Registered as concrete singletons (no interface abstraction).

### Poll System

| Type |
|------|
| `PollService` |
| `PollCommandService` |

Registered as concrete singletons (no interface abstraction).

### Watchtime System

| Type | Lifetime |
|------|----------|
| `WatchtimeService` | Singleton |
| `WatchtimeCommandService` | Singleton |
| `WatchtimeBackgroundService` | Hosted Service |

### Variable System

| Interface | Implementation |
|-----------|---------------|
| `IVariableReplacementService` | `VariableReplacementService` |

### Command System

| Interface | Implementation |
|-----------|---------------|
| `ICommandService` | `CommandService` |

### Integration Services — Twitch

| Interface / Type | Implementation | Lifetime |
|-----------------|---------------|----------|
| `ITwitchService` | `TwitchService` | Singleton |
| `TwitchOAuthService` | — | **Transient** |
| `ITwitchEventSubService` | `TwitchEventSubService` | Singleton |

`AddTwitchLibEventSubWebsockets()` is also called to register TwitchLib's EventSub WebSocket internals.

### Integration Services — Kick

| Interface / Type | Implementation | Lifetime |
|-----------------|---------------|----------|
| `IKickService` | `KickLibChatService` | Singleton |
| `KickLibOAuthService` | — | Singleton |

`AddKickLib()` is called to register KickLib's internal services.

### Webhook Services

| Type |
|------|
| `WebServerDependencyTracker` |
| `WebServerService` |
| `NgrokService` |
| `KickWebhookSubscriptionService` |
| `WebhookCoordinatorService` |

All registered as singletons. These power the local Kestrel web server, ngrok tunnel, and Kick webhook subscription management.

### Audio Services

| Interface | Implementation |
|-----------|---------------|
| `Core.Services.Audio.ISoundService` | `Core.Services.Audio.SoundService` |

### Statistics Service

| Interface | Implementation |
|-----------|---------------|
| `Core.Services.Statistics.IStatisticsService` | `Core.Services.Statistics.StatisticsService` |

### Alert Service

| Interface | Implementation |
|-----------|---------------|
| `UI.Services.IAlertService` | `UI.Services.AlertService` |

### Profile Settings

| Type |
|------|
| `ProfileSettingsLoader` |

---

## ViewModel Registrations

Almost every ViewModel is registered as a **Singleton** to prevent memory leaks from event subscriptions and bindings that would accumulate with transient instances.

### Singleton ViewModels

| ViewModel |
|-----------|
| `DashboardViewModel` |
| `PlatformsViewModel` |
| `TwitchWebSetupViewModel` |
| `TwitchDashboardViewModel` |
| `TwitchSettingsViewModel` |
| `TwitchChatSettingsViewModel` |
| `YoutubeChatSettingsViewModel` |
| `TiktokChatSettingsViewModel` |
| `DiscordChatSettingsViewModel` |
| `KickWebSetupViewModel` |
| `KickDashboardViewModel` |
| `KickSettingsViewModel` |
| `ChatViewModel` |
| `CurrencyViewModel` |
| `CommandsViewModel` |
| `VariablesViewModel` |
| `VariablePickerViewModel` |
| `SettingsViewModel` |
| `AudioSettingsViewModel` |
| `SoundsViewModel` |
| `StatisticsViewModel` |
| `EventHistoryViewModel` |
| `AlertSettingsViewModel` |
| `ConditionalRulesViewModel` |
| `GiveawayViewModel` |
| `PollViewModel` |
| `WatchtimeViewModel` |
| `EventTestingViewModel` |
| `MainViewModel` |
| `WebServerSettingsViewModel` |

### Transient ViewModels

| ViewModel | Reason |
|-----------|--------|
| `ProfileSelectorViewModel` | Fresh instance each time the profile selector overlay appears |
| `GoogleDemoSetupViewModel` | One-time wizard; discarded after use |
| `NgrokSetupWizardViewModel` | One-time wizard; discarded after use |

---

## View Registrations

All Views are registered as **Transient**. They are created on-demand when navigated to.

| View |
|------|
| `Views.TwitchDashboardView` |
| `Views.TwitchSettingsView` |
| `Views.KickDashboardView` |
| `Views.KickWebSetupView` |
| `Views.KickSettingsView` |
| `Views.ChatView` |
| `Views.CurrencyView` |
| `Views.SettingsView` |
| `Views.AudioSettingsView` |
| `Views.SoundsView` |
| `Views.StatisticsView` |
| `Views.EventHistoryView` |
| `Views.AlertSettingsView` |
| `Views.ConditionalRulesView` |
| `Views.EventTestingView` |
| `Views.NgrokSetupWizardView` |

### Windows

| Type | Lifetime |
|------|----------|
| `MainWindow` | Singleton |

---

## Startup Flow (`OnStartup`)

The `App()` constructor builds the host. `OnStartup` then executes the following sequence:

1. **Kill leftover WebView2 processes** — cleans up any orphaned browser processes from a prior session.
2. **`await _host.StartAsync()`** — starts the generic host, which boots Kestrel on port 5000 for webhook endpoints.
3. **`ServiceProvider = _host.Services`** — exposes the DI container for manual resolution where needed.
4. **`dataService.EnsureDatabaseCreatedAsync()`** — creates/migrates the SQLite database for the current profile.
5. **`triggerEngine.StartAsync()`** — loads triggers from the database and subscribes to EventBus events.
6. **Resolve and show `MainWindow`** — the main window is pulled from DI and displayed.
7. **Profile state check** — if no profile is set (first run), the profile selector overlay is shown; otherwise the last-used profile is auto-loaded.

---

## Key Interface Signatures

### IEventBus

Located in `VyralBot.Core.Services.EventBus`. Async pub/sub message bus used throughout the app.

```csharp
public interface IEventBus
{
    Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
        where TEvent : IEvent;

    Task<Guid> SubscribeAsync<TEvent>(Func<TEvent, Task> handler, CancellationToken cancellationToken = default)
        where TEvent : IEvent;

    void Unsubscribe(Guid subscriptionId);

    int SubscriptionCount { get; }
}
```

### ITwitchService

Located in `VyralBot.Integrations.Twitch`. Handles Twitch IRC chat connectivity.

```csharp
public interface ITwitchService
{
    Task ConnectAsync(string username, string accessToken, string channel);
    Task ConnectAsync(string username, string accessToken, string? refreshToken, string channel);
    Task DisconnectAsync();
    Task SendMessageAsync(string message);

    bool IsConnected { get; }
    string? Channel { get; }
    string? BotUsername { get; }
}
```

### IKickService

Located in `VyralBot.Integrations.Kick`. Mirrors `ITwitchService` with an additional `broadcasterUserId` parameter.

```csharp
public interface IKickService
{
    Task ConnectAsync(string username, string accessToken, string channel, int broadcasterUserId);
    Task ConnectAsync(string username, string accessToken, string? refreshToken, string channel, int broadcasterUserId);
    Task DisconnectAsync();
    Task SendMessageAsync(string message);

    bool IsConnected { get; }
    string? Channel { get; }
    string? BotUsername { get; }
}
```

### IProfileService

Located in `VyralBot.Core.Services.Profiles`. Manages multi-profile isolation (data, credentials, settings).

```csharp
public interface IProfileService
{
    string? GetCurrentProfileName();
    Task<Profile> GetCurrentProfileAsync();
    Task<IEnumerable<Profile>> GetProfilesAsync();
    Task<Profile> CreateProfileAsync(string name, string? displayName = null, string? pinCode = null);
    Task SwitchProfileAsync(string name, string? pinCode = null);
    Task DeleteProfileAsync(string name);
    string GetProfileDataPath(string profileName);
    string GetProfileCredentialsPath(string profileName);
    Task<bool> ValidatePinAsync(string profileName, string pinCode);
    Task UpdateProfilePinAsync(string profileName, string? currentPin, string? newPin);
    Task UpdateProfileAsync(Profile profile);
    Task<string> ExportProfileAsync(string profileName, string destinationPath);
    Task<Profile> ImportProfileAsync(string zipPath);
}
```

### IDataService

Located in `VyralBot.Data`. Lightweight Dapper-based data access replacing EF Core. Covers all entity CRUD operations.

```csharp
public interface IDataService
{
    // Platforms
    Task<IEnumerable<Platform>> GetPlatformsAsync();
    Task<Platform?> GetPlatformByNameAsync(string name);
    Task<Platform?> GetPlatformByIdAsync(Guid id);
    Task SavePlatformAsync(Platform platform);
    Task DeletePlatformAsync(Guid id);

    // Configs
    Task<string?> GetConfigValueAsync(string key);
    Task SetConfigValueAsync(string key, string value, string? category = null);
    Task<IEnumerable<Config>> GetConfigsByCategoryAsync(string category);

    // Triggers
    Task<IEnumerable<Trigger>> GetTriggersAsync(bool? enabledOnly = null);
    Task<Trigger?> GetTriggerByIdAsync(Guid id);
    Task SaveTriggerAsync(Trigger trigger);
    Task DeleteTriggerAsync(Guid id);

    // Actions
    Task<IEnumerable<BotAction>> GetActionsAsync();
    Task<BotAction?> GetActionByIdAsync(Guid id);
    Task SaveActionAsync(BotAction action);
    Task DeleteActionAsync(Guid id);

    // EventLogs
    Task<IEnumerable<EventLog>> GetEventLogsAsync(int limit = 100);
    Task LogEventAsync(EventLog log);

    // WebServer
    Task<WebServerSettings> GetWebServerSettingsAsync();
    Task SaveWebServerSettingsAsync(WebServerSettings settings);
    Task<IEnumerable<WebhookSubscription>> GetWebhookSubscriptionsAsync(string? platform = null);
    Task SaveWebhookSubscriptionAsync(WebhookSubscription subscription);
    Task DeleteWebhookSubscriptionAsync(string id);
    Task DeleteWebhookSubscriptionsByPlatformAsync(string platform);

    // Database management
    Task EnsureDatabaseCreatedAsync();

    // Chat messages
    Task<bool> SaveChatMessageAsync(ChatMessage message);
    Task<IEnumerable<ChatMessage>> GetChatMessagesAsync(string platform, string channel, int limit = 200);
    Task DeleteChatMessagesAsync(string platform, string channel);

    // Chat Event Settings
    Task<ChatEventSetting?> GetChatEventSettingAsync(string platform, string eventType);
    Task<IEnumerable<ChatEventSetting>> GetAllChatEventSettingsAsync(string? platform = null);
    Task SaveChatEventSettingAsync(ChatEventSetting setting);
    Task DeleteChatEventSettingAsync(Guid id);

    // Event Statistics
    Task SaveEventStatisticAsync(EventStatistic statistic);
    Task<IEnumerable<EventStatistic>> GetEventStatisticsAsync(string? platform = null, string? eventType = null, int limit = 100);
    Task<IEnumerable<EventStatistic>> GetEventStatisticsByUsernameAsync(string username, string? platform = null);

    // Currency System
    Task<CurrencySetting?> GetCurrencySettingsAsync();
    Task SaveCurrencySettingsAsync(CurrencySetting settings);
    Task<UserCurrency?> GetUserCurrencyAsync(string username, string platform);
    Task<IEnumerable<UserCurrency>> GetUserCurrenciesAsync(string username);
    Task<IEnumerable<UserCurrency>> GetCurrencyLeaderboardAsync(int limit = 10, string? platform = null);
    Task SaveUserCurrencyAsync(UserCurrency currency);
    Task SaveCurrencyTransactionAsync(CurrencyTransaction transaction);
    Task<IEnumerable<CurrencyTransaction>> GetCurrencyTransactionsAsync(string? username = null, string? platform = null, int limit = 100);
    Task<CurrencyReward?> GetCurrencyRewardAsync(string id);
    Task<IEnumerable<CurrencyReward>> GetCurrencyRewardsAsync(bool? enabledOnly = null);
    Task SaveCurrencyRewardAsync(CurrencyReward reward);
    Task DeleteCurrencyRewardAsync(string id);
    Task<CurrencyRedemption?> GetCurrencyRedemptionAsync(string id);
    Task<IEnumerable<CurrencyRedemption>> GetCurrencyRedemptionsAsync(string? status = null, string? username = null);
    Task SaveCurrencyRedemptionAsync(CurrencyRedemption redemption);
    Task DeleteCurrencyRedemptionAsync(string id);

    // Commands
    Task<Command?> GetCommandAsync(string id);
    Task<Command?> GetCommandByNameAsync(string name);
    Task<IEnumerable<Command>> GetCommandsAsync(bool? enabledOnly = null, bool? builtInOnly = null);
    Task SaveCommandAsync(Command command);
    Task DeleteCommandAsync(string id);
    Task IncrementCommandUsageAsync(string id);

    // Command Cooldowns
    Task<CommandCooldown?> GetCommandCooldownAsync(string commandId, string? username, string platform);
    Task SaveCommandCooldownAsync(CommandCooldown cooldown);
    Task DeleteExpiredCooldownsAsync();

    // Auto-Reply Rules
    Task<IEnumerable<AutoReplyRule>> GetAutoReplyRulesAsync();
    Task<IEnumerable<AutoReplyRule>> GetAutoReplyRulesByPlatformAndEventAsync(string platform, string eventType);
    Task<AutoReplyRule?> GetAutoReplyRuleAsync(int id);
    Task<int> SaveAutoReplyRuleAsync(AutoReplyRule rule);
    Task DeleteAutoReplyRuleAsync(int id);
    Task IncrementRuleTriggerCountAsync(int id);

    // Giveaways
    Task<IEnumerable<Giveaway>> GetGiveawaysAsync();
    Task<Giveaway?> GetGiveawayAsync(Guid id);
    Task<Giveaway?> GetActiveGiveawayAsync();
    Task SaveGiveawayAsync(Giveaway giveaway);
    Task DeleteGiveawayAsync(Guid id);

    // Giveaway Entries
    Task<IEnumerable<GiveawayEntry>> GetGiveawayEntriesAsync(Guid giveawayId);
    Task<GiveawayEntry?> GetGiveawayEntryAsync(Guid giveawayId, string username, string platform);
    Task SaveGiveawayEntryAsync(GiveawayEntry entry);
    Task<int> GetGiveawayEntryCountAsync(Guid giveawayId);
    Task<int> GetGiveawayUniqueParticipantsAsync(Guid giveawayId);

    // Polls
    Task<IEnumerable<Poll>> GetPollsAsync();
    Task<Poll?> GetPollAsync(Guid id);
    Task<Poll?> GetActivePollAsync();
    Task SavePollAsync(Poll poll);
    Task DeletePollAsync(Guid id);

    // Poll Votes
    Task<IEnumerable<PollVote>> GetPollVotesAsync(Guid pollId);
    Task<PollVote?> GetPollVoteAsync(Guid pollId, string username, string platform);
    Task SavePollVoteAsync(PollVote vote);
    Task<int> GetPollVoteCountAsync(Guid pollId);
    Task<Dictionary<int, int>> GetPollVoteResultsAsync(Guid pollId);

    // Watchtime
    Task<UserWatchtime?> GetUserWatchtimeAsync(string username, string platform);
    Task<IEnumerable<UserWatchtime>> GetAllUserWatchtimeAsync();
    Task<IEnumerable<UserWatchtime>> GetTopWatchtimeUsersAsync(int limit = 10);
    Task<IEnumerable<UserWatchtime>> GetActiveViewersAsync();
    Task<int> GetTotalActiveViewersCountAsync();
    Task SaveUserWatchtimeAsync(UserWatchtime watchtime);
    Task DeactivateInactiveViewersAsync(DateTime inactiveSince);
    Task ResetStreamSessionMinutesAsync();

    // Variables
    Task<Variable?> GetVariableAsync(string id);
    Task<Variable?> GetVariableByNameAsync(string name);
    Task<IEnumerable<Variable>> GetVariablesAsync();
    Task<IEnumerable<Variable>> GetVariablesByCategoryAsync(string category);
    Task<IEnumerable<string>> GetVariableCategoriesAsync();
    Task SaveVariableAsync(Variable variable);
    Task DeleteVariableAsync(string id);
    Task UpdateVariableValueAsync(string name, string? value);
}
```

### IActionDispatcher

Located in `VyralBot.Core.Services.ActionDispatcher`. Executes actions with timeout and cancellation support.

```csharp
public interface IActionDispatcher
{
    Task<bool> ExecuteActionAsync(Guid actionId, ActionContext context);
    Task<bool> ExecuteSequenceAsync(IEnumerable<Guid> actionIds, ActionContext context);
    void CancelTriggerActions(Guid triggerId);

    int ActiveActionCount { get; }
}
```

### ITriggerEngine

Located in `VyralBot.Core.Services.TriggerEngine`. Matches EventBus events to triggers and dispatches actions.

```csharp
public interface ITriggerEngine
{
    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync();
    Task FireTriggerAsync(Guid triggerId, Dictionary<string, object>? variables = null);
    Task ReloadTriggersAsync();

    int ActiveTriggerCount { get; }
}
```

---

## Lifetime Summary

| Lifetime | Used For |
|----------|----------|
| **Singleton** | All services, most ViewModels, `MainWindow` |
| **Transient** | All Views, `TwitchOAuthService`, `ProfileSelectorViewModel`, `GoogleDemoSetupViewModel`, `NgrokSetupWizardViewModel`, `NgrokSetupWizardView` |
| **Hosted Service** | `PerformanceMonitor`, `WatchtimeBackgroundService` |

Hosted services are managed by the generic host — they start with `_host.StartAsync()` and stop with `_host.StopAsync()`.
