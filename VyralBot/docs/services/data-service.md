# Data Service

## Overview
SQLite-based data access layer using Dapper ORM. `IDataService` defines 80+ methods across 17+ categories. `DapperDataService` implements all methods using raw SQL via Dapper. `ProfileAwareDataService` is a delegation wrapper that routes to per-profile database instances.

## Source Files

### IDataService Interface
- **File:** `src/VyralBot.Data/IDataService.cs` (~157 lines)
- **Namespace:** `VyralBot.Data`
- **Method categories (80+ methods):**
  - **Platforms:** `GetPlatformAsync(string)`, `GetPlatformsAsync()`, `SavePlatformAsync(Platform)`
  - **Configs:** `GetConfigAsync(string)`, `GetConfigsAsync()`, `GetConfigsByCategoryAsync(string)`, `SaveConfigAsync(Config)`, `DeleteConfigAsync(string)`
  - **Triggers:** `GetTriggersAsync()`, `GetTriggerAsync(Guid)`, `SaveTriggerAsync(Trigger)`, `DeleteTriggerAsync(Guid)`
  - **Actions:** `GetBotActionsAsync()`, `GetBotActionAsync(Guid)`, `SaveBotActionAsync(BotAction)`, `DeleteBotActionAsync(Guid)`
  - **EventLogs:** `GetEventLogsAsync(int,int)`, `GetEventLogsByTypeAsync(string,int,int)`, `SaveEventLogAsync(EventLog)`, `GetEventLogCountAsync()`
  - **WebServer:** `GetWebServerSettingsAsync()`, `SaveWebServerSettingsAsync(WebServerSettings)`, `GetWebhookSubscriptionsAsync()`, `SaveWebhookSubscriptionAsync(WebhookSubscription)`, `DeleteWebhookSubscriptionAsync(string)`
  - **ChatMessages:** `SaveChatMessageAsync(ChatMessage)`, `GetChatMessagesAsync(string,string,int,int)`, `GetChatMessageCountAsync(string,string)`
  - **ChatEventSettings:** `GetChatEventSettingsAsync()`, `GetChatEventSettingAsync(string,string)`, `SaveChatEventSettingAsync(ChatEventSetting)`, `DeleteChatEventSettingAsync(Guid)`
  - **EventStatistics:** `SaveEventStatisticAsync(EventStatistic)`, `GetEventStatisticsAsync(int,int)`, `GetEventStatisticsByTypeAsync(string,int,int)`, `GetEventStatisticsCountAsync()`
  - **Currency:** Settings, UserCurrency, CurrencyTransaction, CurrencyReward, CurrencyRedemption methods. Includes SQL-aggregation stats queries (`GetCurrencyEconomyTotalsAsync` COUNT/SUM over UserCurrency, `GetCurrencyTransactionTotalsSinceAsync` SUM by sign with a date filter) and `GetCurrencyUsersPagedAsync` (LIMIT/OFFSET over the stable Username+Platform key, used by GiveCurrencyToAll to walk every user). Leaderboard ordering is `Balance DESC, Username COLLATE NOCASE, Platform` (deterministic ties).
  - **Commands:** `GetCommandsAsync()`, `GetCommandAsync(string)`, `SaveCommandAsync(Command)`, `DeleteCommandAsync(string)`
  - **CommandCooldowns:** Cooldown tracking methods
  - **AutoReplyRules:** Rule CRUD methods
  - **Giveaways/Entries:** Giveaway and entry CRUD methods
  - **Polls/Votes:** Poll and vote CRUD methods
  - **Watchtime:** User watchtime CRUD methods, plus `DeactivateAllViewersAsync` (clears every IsActive flag at app startup so viewers persisted as active by the previous run cannot accrue watchtime/points until they chat again)
  - **Variables:** Variable CRUD methods

### DapperDataService Implementation
- **File:** `src/VyralBot.Data/DapperDataService.cs` (~2749 lines)
- **Namespace:** `VyralBot.Data`
- **Class:** `DapperDataService : IDataService`
- **Constructor:** `string connectionString`, `ILogger<DapperDataService>`
- **Uses:** Dapper ORM, `Microsoft.Data.Sqlite`
- **Connection:** `new SqliteConnection(_connectionString)` per query
- **Custom type handler:** `GuidTypeHandler` for SQLite TEXT ↔ Guid mapping
- **Database initialization:** Creates all tables on first use

### ProfileAwareDataService
- **File:** `src/VyralBot.UI/Services/ProfileAwareDataService.cs` (~195 lines)
- **Namespace:** `VyralBot.UI.Services`
- **Class:** `ProfileAwareDataService : IDataService`
- **Constructor:** `IProfileService`, `ILogger<DapperDataService>`
- **Pattern:** All IDataService methods delegate to `GetCurrentService()` which returns a DapperDataService pointed at `profiles/{currentProfile}/data/vyralbot.db`
- **`ClearConnection()`** — disposes current service, clears SQLite pool, forces GC

## Architecture
```
IDataService (interface, 80+ methods)
  │
  ├── DapperDataService (raw implementation)
  │   └── SQLite via Dapper
  │       └── profiles/{name}/data/vyralbot.db
  │
  └── ProfileAwareDataService (delegation wrapper)
      └── Routes to correct DapperDataService per profile
          └── GetCurrentService() checks current profile
```

## All Files
| File | Type | Description |
|------|------|-------------|
| `src/VyralBot.Data/IDataService.cs` | Interface | 80+ data access methods |
| `src/VyralBot.Data/DapperDataService.cs` | Class | Dapper/SQLite implementation (2749 lines) |
| `src/VyralBot.UI/Services/ProfileAwareDataService.cs` | Class | Profile-aware delegation wrapper |
