# Data Layer Architecture

## Overview

The data layer lives in the `VyralBot.Data` project and provides all database access for the application. It uses **SQLite** as the database engine and **Dapper** as the micro-ORM. A profile-aware wrapper in `VyralBot.UI` enables per-profile database isolation.

### Key Files

| File | Purpose |
|------|---------|
| `src/VyralBot.Data/IDataService.cs` | Interface defining all data operations (157 lines) |
| `src/VyralBot.Data/DapperDataService.cs` | Dapper-based SQLite implementation (2749 lines) |
| `src/VyralBot.Data/Entities/` | All entity/model definitions |
| `src/VyralBot.UI/Services/ProfileAwareDataService.cs` | Per-profile delegation wrapper (195 lines) |

---

## Entities

All entities are defined in `src/VyralBot.Data/Entities/`. The five core entities are documented in full below, followed by a summary of all remaining entities.

### Platform (`Platform.cs`, ~128 lines)

Represents a connected streaming platform (Kick, Twitch, YouTube, TikTok, Discord).

| Property | Type | Notes |
|----------|------|-------|
| `Id` | `Guid` | Primary key |
| `Name` | `string` | **Required.** Platform name |
| `Enabled` | `bool` | Whether the platform connection is active |
| `AccessToken` | `string?` | OAuth access token |
| `RefreshToken` | `string?` | OAuth refresh token |
| `TokenExpiresAt` | `DateTime?` | Token expiration timestamp |
| `Username` | `string?` | Bot's username on the platform |
| `UserId` | `string?` | Bot's user ID on the platform |
| `ChannelId` | `string?` | Target channel ID |
| `ChatroomId` | `string?` | Chatroom identifier (Kick-specific) |
| `ClientId` | `string?` | OAuth client ID |
| `ClientSecret` | `string?` | OAuth client secret |
| `Status` | `string` | Connection status |
| `LastError` | `string?` | Last error message, if any |
| `ConnectedAt` | `DateTime?` | When the platform was last connected |
| `KickChatEnabled` | `bool` | Kick chat feature toggle |
| `KickChatAutoStart` | `bool` | Auto-start Kick chat on launch |
| `TwitchChatEnabled` | `bool` | Twitch chat feature toggle |
| `TwitchChatAutoStart` | `bool` | Auto-start Twitch chat on launch |
| `YoutubeChatEnabled` | `bool` | YouTube chat feature toggle |
| `YoutubeChatAutoStart` | `bool` | Auto-start YouTube chat on launch |
| `TiktokChatEnabled` | `bool` | TikTok chat feature toggle |
| `TiktokChatAutoStart` | `bool` | Auto-start TikTok chat on launch |
| `DiscordChatEnabled` | `bool` | Discord chat feature toggle |
| `DiscordChatAutoStart` | `bool` | Auto-start Discord chat on launch |
| `CreatedAt` | `DateTime` | Row creation timestamp |
| `UpdatedAt` | `DateTime` | Last modification timestamp |

### Trigger (`Trigger.cs`, 60 lines)

Defines an event-driven trigger that fires actions when conditions are met.

| Property | Type | Notes |
|----------|------|-------|
| `Id` | `Guid` | Primary key |
| `Name` | `string` | **Required.** Display name |
| `Description` | `string?` | Optional description |
| `Enabled` | `bool` | Whether the trigger is active |
| `EventType` | `string` | **Required.** The event type that activates this trigger |
| `Platform` | `string?` | Restrict to a specific platform, or null for all |
| `Condition` | `string?` | Optional condition expression |
| `CooldownSeconds` | `int` | Minimum seconds between firings |
| `LastFiredAt` | `DateTime?` | Timestamp of last firing |
| `FireCount` | `int` | Total number of times fired |
| `Permission` | `string` | Required permission level |
| `ActionSequence` | `string` | **Required.** JSON array of actions to execute |
| `CreatedAt` | `DateTime` | Row creation timestamp |
| `UpdatedAt` | `DateTime` | Last modification timestamp |

### BotAction (`BotAction.cs`, 44 lines)

A reusable action definition that can be referenced by triggers.

| Property | Type | Notes |
|----------|------|-------|
| `Id` | `Guid` | Primary key |
| `Name` | `string` | **Required.** Display name |
| `Description` | `string?` | Optional description |
| `ActionType` | `string` | **Required.** The type of action (e.g., SendMessage, PlaySound) |
| `Configuration` | `string` | **Required.** JSON object with action-specific configuration |
| `TimeoutSeconds` | `int` | Execution timeout |
| `RetryOnFailure` | `bool` | Whether to retry on failure |
| `MaxRetries` | `int` | Maximum retry attempts |
| `CreatedAt` | `DateTime` | Row creation timestamp |
| `UpdatedAt` | `DateTime` | Last modification timestamp |

### Config (`Config.cs`, 25 lines)

Key-value configuration storage. **Does not have an `Id` column** — the `Key` is the primary identifier.

| Property | Type | Notes |
|----------|------|-------|
| `Key` | `string` | **Required.** Configuration key (primary identifier) |
| `Value` | `string` | **Required.** Configuration value |
| `Category` | `string?` | Optional grouping category |
| `IsEncrypted` | `bool` | Whether the value is stored encrypted |
| `UpdatedAt` | `DateTime` | Last modification timestamp |

### EventLog (`EventLog.cs`, 35 lines)

Records platform events for auditing and debugging.

| Property | Type | Notes |
|----------|------|-------|
| `Id` | `Guid` | Primary key |
| `Platform` | `string` | **Required.** Source platform |
| `EventType` | `string` | **Required.** Type of event |
| `Username` | `string?` | Username involved |
| `UserId` | `string?` | User ID involved |
| `Data` | `string?` | Serialized event data |
| `TriggerId` | `Guid?` | Associated trigger, if any |
| `TriggerFired` | `bool` | Whether a trigger was executed |
| `Timestamp` | `DateTime` | When the event occurred |

### Additional Entities

All located in `src/VyralBot.Data/Entities/`:

| Entity | File | Purpose |
|--------|------|---------|
| `ChatMessage` | `ChatMessage.cs` | Stored chat messages per platform/channel |
| `ChatEventSetting` | `ChatEventSetting.cs` | Per-platform, per-event-type display/behavior settings |
| `EventStatistic` | `EventStatistic.cs` | Aggregated event statistics by platform/user |
| `CurrencySetting` | `CurrencySetting.cs` | Global currency system configuration |
| `UserCurrency` | `UserCurrency.cs` | Per-user currency balances per platform |
| `CurrencyTransaction` | `CurrencyTransaction.cs` | Currency transaction log |
| `CurrencyReward` | `CurrencyReward.cs` | Redeemable currency rewards |
| `CurrencyRedemption` | `CurrencyRedemption.cs` | User redemption records |
| `Command` | `Command.cs` | Chat command definitions |
| `CommandCooldown` | `CommandCooldown.cs` | Per-user command cooldown tracking |
| `AutoReplyRule` | `AutoReplyRule.cs` | Automatic reply rules by platform/event |
| `Giveaway` | `Giveaway.cs` | Giveaway definitions |
| `GiveawayEntry` | `GiveawayEntry.cs` | Per-user giveaway entries |
| `Poll` | `Poll.cs` | Poll definitions |
| `PollVote` | `PollVote.cs` | Per-user poll votes |
| `UserWatchtime` | `UserWatchtime.cs` | Per-user watchtime tracking |
| `Variable` | `Variable.cs` | User-defined variables (categorized) |
| `WebServerSettings` | `WebServerEntities.cs` | Web server configuration |
| `WebhookSubscription` | `WebServerEntities.cs` | Webhook subscription records |

---

## IDataService Interface

Defined in `src/VyralBot.Data/IDataService.cs` (157 lines). Every method is `async` (returns `Task` or `Task<T>`). The interface is organized into the following method categories:

### Platforms

```csharp
Task<IEnumerable<Platform>> GetPlatformsAsync();
Task<Platform?> GetPlatformByNameAsync(string name);
Task<Platform?> GetPlatformByIdAsync(Guid id);
Task SavePlatformAsync(Platform platform);
Task DeletePlatformAsync(Guid id);
```

### Configs

```csharp
Task<string?> GetConfigValueAsync(string key);
Task SetConfigValueAsync(string key, string value, string? category = null);
Task<bool> DeleteConfigValueAsync(string key, string? category = null);
Task<IEnumerable<Config>> GetConfigsByCategoryAsync(string category);
```

### Triggers

```csharp
Task<IEnumerable<Trigger>> GetTriggersAsync(bool? enabledOnly = null);
Task<Trigger?> GetTriggerByIdAsync(Guid id);
Task SaveTriggerAsync(Trigger trigger);
Task DeleteTriggerAsync(Guid id);
```

### Actions

```csharp
Task<IEnumerable<BotAction>> GetActionsAsync();
Task<BotAction?> GetActionByIdAsync(Guid id);
Task SaveActionAsync(BotAction action);
Task DeleteActionAsync(Guid id);
```

### Event Logs

```csharp
Task<IEnumerable<EventLog>> GetEventLogsAsync(int limit = 100);
Task LogEventAsync(EventLog log);
```

### Web Server

```csharp
Task<WebServerSettings> GetWebServerSettingsAsync();
Task SaveWebServerSettingsAsync(WebServerSettings settings);
Task<IEnumerable<WebhookSubscription>> GetWebhookSubscriptionsAsync(string? platform = null);
Task SaveWebhookSubscriptionAsync(WebhookSubscription subscription);
Task DeleteWebhookSubscriptionAsync(string id);
Task DeleteWebhookSubscriptionsByPlatformAsync(string platform);
```

### Database Management

```csharp
Task EnsureDatabaseCreatedAsync();
```

### Chat Messages

```csharp
Task<bool> SaveChatMessageAsync(ChatMessage message);
Task<IEnumerable<ChatMessage>> GetChatMessagesAsync(string platform, string channel, int limit = 200);
Task DeleteChatMessagesAsync(string platform, string channel);
```

### Chat Event Settings

```csharp
Task<ChatEventSetting?> GetChatEventSettingAsync(string platform, string eventType);
Task<IEnumerable<ChatEventSetting>> GetAllChatEventSettingsAsync(string? platform = null);
Task SaveChatEventSettingAsync(ChatEventSetting setting);
Task DeleteChatEventSettingAsync(Guid id);
```

### Event Statistics

```csharp
Task SaveEventStatisticAsync(EventStatistic statistic);
Task<IEnumerable<EventStatistic>> GetEventStatisticsAsync(string? platform = null, string? eventType = null, int limit = 100);
Task<IEnumerable<EventStatistic>> GetEventStatisticsByUsernameAsync(string username, string? platform = null);
```

### Currency System

```csharp
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
```

### Commands

```csharp
Task<Command?> GetCommandAsync(string id);
Task<Command?> GetCommandByNameAsync(string name);
Task<IEnumerable<Command>> GetCommandsAsync(bool? enabledOnly = null, bool? builtInOnly = null);
Task SaveCommandAsync(Command command);
Task DeleteCommandAsync(string id);
Task IncrementCommandUsageAsync(string id);
```

### Command Cooldowns

```csharp
Task<CommandCooldown?> GetCommandCooldownAsync(string commandId, string? username, string platform);
Task SaveCommandCooldownAsync(CommandCooldown cooldown);
Task DeleteExpiredCooldownsAsync();
```

### Auto-Reply Rules

```csharp
Task<IEnumerable<AutoReplyRule>> GetAutoReplyRulesAsync();
Task<IEnumerable<AutoReplyRule>> GetAutoReplyRulesByPlatformAndEventAsync(string platform, string eventType);
Task<AutoReplyRule?> GetAutoReplyRuleAsync(int id);
Task<int> SaveAutoReplyRuleAsync(AutoReplyRule rule);
Task DeleteAutoReplyRuleAsync(int id);
Task IncrementRuleTriggerCountAsync(int id);
```

### Giveaways

```csharp
Task<IEnumerable<Giveaway>> GetGiveawaysAsync();
Task<Giveaway?> GetGiveawayAsync(Guid id);
Task<Giveaway?> GetActiveGiveawayAsync();
Task SaveGiveawayAsync(Giveaway giveaway);
Task DeleteGiveawayAsync(Guid id);
```

### Giveaway Entries

```csharp
Task<IEnumerable<GiveawayEntry>> GetGiveawayEntriesAsync(Guid giveawayId);
Task<GiveawayEntry?> GetGiveawayEntryAsync(Guid giveawayId, string username, string platform);
Task SaveGiveawayEntryAsync(GiveawayEntry entry);
Task<int> GetGiveawayEntryCountAsync(Guid giveawayId);
Task<int> GetGiveawayUniqueParticipantsAsync(Guid giveawayId);
```

### Polls

```csharp
Task<IEnumerable<Poll>> GetPollsAsync();
Task<Poll?> GetPollAsync(Guid id);
Task<Poll?> GetActivePollAsync();
Task SavePollAsync(Poll poll);
Task DeletePollAsync(Guid id);
```

### Poll Votes

```csharp
Task<IEnumerable<PollVote>> GetPollVotesAsync(Guid pollId);
Task<PollVote?> GetPollVoteAsync(Guid pollId, string username, string platform);
Task SavePollVoteAsync(PollVote vote);
Task<int> GetPollVoteCountAsync(Guid pollId);
Task<Dictionary<int, int>> GetPollVoteResultsAsync(Guid pollId);
```

### Watchtime

```csharp
Task<UserWatchtime?> GetUserWatchtimeAsync(string username, string platform);
Task<IEnumerable<UserWatchtime>> GetAllUserWatchtimeAsync();
Task<IEnumerable<UserWatchtime>> GetTopWatchtimeUsersAsync(int limit = 10);
Task<IEnumerable<UserWatchtime>> GetActiveViewersAsync();
Task<int> GetTotalActiveViewersCountAsync();
Task SaveUserWatchtimeAsync(UserWatchtime watchtime);
Task DeactivateInactiveViewersAsync(DateTime inactiveSince);
Task ResetStreamSessionMinutesAsync();
```

### Variables

```csharp
Task<Variable?> GetVariableAsync(string id);
Task<Variable?> GetVariableByNameAsync(string name);
Task<IEnumerable<Variable>> GetVariablesAsync();
Task<IEnumerable<Variable>> GetVariablesByCategoryAsync(string category);
Task<IEnumerable<string>> GetVariableCategoriesAsync();
Task SaveVariableAsync(Variable variable);
Task DeleteVariableAsync(string id);
Task UpdateVariableValueAsync(string name, string? value);
```

---

## DapperDataService

**File:** `src/VyralBot.Data/DapperDataService.cs` (2749 lines)

`DapperDataService` is the concrete implementation of `IDataService`. It executes all database operations against a SQLite database using Dapper.

### Constructor

```csharp
public DapperDataService(string connectionString, ILogger<DapperDataService> logger)
```

- `connectionString` — SQLite connection string (e.g., `Data Source=path/to/bot.db`)
- `logger` — Structured logger for diagnostics

### Connection Pattern

Every database operation follows a **short-lived connection** pattern. Connections are **not kept open** between calls:

```csharp
private SqliteConnection CreateConnection() => new SqliteConnection(_connectionString);
```

Each method opens a fresh `SqliteConnection` inside a `using` statement, executes the query via Dapper, and disposes the connection immediately:

```csharp
public async Task<Platform?> GetPlatformByNameAsync(string name)
{
    using var connection = CreateConnection();
    return await connection.QueryFirstOrDefaultAsync<Platform>(
        "SELECT * FROM Platforms WHERE Name = @Name", new { Name = name });
}
```

This pattern:
- Avoids connection pooling complexity with SQLite
- Ensures connections are always properly disposed
- Is safe for concurrent access from different threads
- Relies on SQLite's file-level locking for consistency

### Database Initialization

`EnsureDatabaseCreatedAsync()` creates all required tables if they do not exist using `CREATE TABLE IF NOT EXISTS` statements.

### Implementation Characteristics

- All CRUD operations use parameterized SQL via Dapper's `QueryAsync`, `QueryFirstOrDefaultAsync`, `ExecuteAsync`, etc.
- Save methods use `INSERT OR REPLACE` (upsert) semantics for most entities
- Logging via `ILogger<DapperDataService>` for errors and diagnostics
- No Entity Framework — raw SQL only through Dapper

---

## ProfileAwareDataService

**File:** `src/VyralBot.UI/Services/ProfileAwareDataService.cs` (195 lines)

`ProfileAwareDataService` is a delegating wrapper that routes all `IDataService` calls to the correct per-profile `DapperDataService` instance.

### Constructor

```csharp
public ProfileAwareDataService(IProfileService profileService, ILogger<DapperDataService> logger)
```

### Delegation Pattern

The class maintains a dictionary of `DapperDataService` instances keyed by profile name. On every call, it resolves the current profile and delegates:

```
IDataService call
  → ProfileAwareDataService
    → GetCurrentService()
      → Resolves current profile via IProfileService
      → Returns (or creates) the DapperDataService for that profile
    → Delegates the call to that DapperDataService instance
```

Each profile gets its own SQLite database file, providing complete data isolation between profiles. When the active profile changes, subsequent calls are automatically routed to the new profile's database.

### Why It Exists

- **Profile isolation:** Each bot profile (e.g., "Drip", "Production") has its own database with independent platforms, triggers, commands, currency, etc.
- **Transparent switching:** The rest of the application depends only on `IDataService` and is unaware of profile switching
- **Lazy initialization:** `DapperDataService` instances are created on first access per profile

---

## Dependency Injection Registration

In the UI project's service configuration:

```csharp
services.AddSingleton<IDataService, ProfileAwareDataService>();
```

- Registered as a **singleton** — one instance for the entire application lifetime
- All consumers inject `IDataService` and receive the `ProfileAwareDataService`
- The `ProfileAwareDataService` internally manages per-profile `DapperDataService` instances

### Dependency Chain

```
Any ViewModel / Service
  → IDataService (injected)
    → ProfileAwareDataService (singleton)
      → DapperDataService (per profile, lazily created)
        → SqliteConnection (per operation, using statement)
```

---

## Database Technology

| Component | Technology |
|-----------|-----------|
| Database engine | **SQLite** (via `Microsoft.Data.Sqlite`) |
| ORM / Data access | **Dapper** (micro-ORM) |
| Connection type | `SqliteConnection` |
| Connection lifetime | Per-operation (`using` statement) |
| Schema management | `CREATE TABLE IF NOT EXISTS` in `EnsureDatabaseCreatedAsync()` |
| Query style | Parameterized raw SQL |
| Upsert strategy | `INSERT OR REPLACE` |
