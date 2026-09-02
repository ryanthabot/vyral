# Data Services — Detailed Reference

## DapperDataService
- **File:** `src/VyralBot.Data/DapperDataService.cs` (~2749 lines)
- **Namespace:** `VyralBot.Data`
- **Constructor:** `(string connectionString, ILogger<DapperDataService>)`
- **Technology:** Dapper ORM with Microsoft.Data.Sqlite
- **Connection pattern:** New `SqliteConnection` per operation (not pooled/shared)
- **Guid handling:** Custom `GuidTypeHandler : SqlMapper.TypeHandler<Guid>` registered in static constructor for SQLite TEXT ↔ Guid mapping

### Table Creation
DapperDataService initializes all database tables on construction. Tables include:
- `Platforms`, `Configs`, `Triggers`, `BotActions`, `EventLogs`
- `WebServerSettings`, `WebhookSubscriptions`
- `ChatMessages`, `ChatEventSettings`, `EventStatistics`
- `CurrencySettings`, `UserCurrency`, `CurrencyTransactions`, `CurrencyRewards`, `CurrencyRedemptions`
- `Commands`, `CommandCooldowns`, `AutoReplyRules`
- `Giveaways`, `GiveawayEntries`, `Polls`, `PollVotes`
- `UserWatchtime`, `Variables`

### Method Group: Platforms
```csharp
Task<Platform?> GetPlatformAsync(string name)
Task<List<Platform>> GetPlatformsAsync()
Task SavePlatformAsync(Platform platform)  // UPSERT
```

### Method Group: Config
```csharp
Task<Config?> GetConfigAsync(string key)
Task<List<Config>> GetConfigsAsync()
Task<List<Config>> GetConfigsByCategoryAsync(string category)
Task SaveConfigAsync(Config config)  // UPSERT using Key
Task DeleteConfigAsync(string key)
```

### Method Group: Currency
```csharp
// Settings
Task<CurrencySetting?> GetCurrencySettingsAsync()
Task SaveCurrencySettingsAsync(CurrencySetting settings)
// User balances
Task<UserCurrency?> GetUserCurrencyAsync(string username, string platform)
Task<List<UserCurrency>> GetAllUserCurrencyAsync()
Task SaveUserCurrencyAsync(UserCurrency userCurrency)
// Transactions
Task SaveCurrencyTransactionAsync(CurrencyTransaction transaction)
Task<List<CurrencyTransaction>> GetCurrencyTransactionsAsync(string username, string platform, int limit, int offset)
// Rewards & Redemptions
Task<List<CurrencyReward>> GetCurrencyRewardsAsync()
Task<CurrencyReward?> GetCurrencyRewardAsync(string id)
Task SaveCurrencyRewardAsync(CurrencyReward reward)
Task DeleteCurrencyRewardAsync(string id)
Task<List<CurrencyRedemption>> GetCurrencyRedemptionsAsync(string? rewardId, string? status, int limit, int offset)
Task<CurrencyRedemption?> GetCurrencyRedemptionAsync(string id)
Task SaveCurrencyRedemptionAsync(CurrencyRedemption redemption)
```

### Method Group: Commands
```csharp
Task<List<Command>> GetCommandsAsync()
Task<Command?> GetCommandAsync(string id)
Task SaveCommandAsync(Command command)
Task DeleteCommandAsync(string id)
// Cooldowns
Task<CommandCooldown?> GetCommandCooldownAsync(string commandId, string? username, string platform)
Task SaveCommandCooldownAsync(CommandCooldown cooldown)
Task CleanExpiredCooldownsAsync()
```

### Method Group: Giveaways
```csharp
Task<List<Giveaway>> GetGiveawaysAsync()
Task<Giveaway?> GetGiveawayAsync(Guid id)
Task SaveGiveawayAsync(Giveaway giveaway)
Task DeleteGiveawayAsync(Guid id)
Task<List<GiveawayEntry>> GetGiveawayEntriesAsync(Guid giveawayId)
Task SaveGiveawayEntryAsync(GiveawayEntry entry)
```

### Method Group: Polls
```csharp
Task<List<Poll>> GetPollsAsync()
Task<Poll?> GetPollAsync(Guid id)
Task SavePollAsync(Poll poll)
Task DeletePollAsync(Guid id)
Task<List<PollVote>> GetPollVotesAsync(Guid pollId)
Task SavePollVoteAsync(PollVote vote)
```

## ProfileAwareDataService — Delegation Pattern
- **File:** `src/VyralBot.UI/Services/ProfileAwareDataService.cs`
- Every method calls `GetCurrentService()` which:
  1. Reads current profile name from `IProfileService.GetCurrentProfileName()`
  2. If profile changed, creates new `DapperDataService` with connection string `Data Source={profileDataPath}/vyralbot.db`
  3. Returns the cached `DapperDataService` instance
- `ClearConnection()` is called during profile switches to release database files

## All Files
| File | Description |
|------|-------------|
| `src/VyralBot.Data/IDataService.cs` | Interface (80+ methods) |
| `src/VyralBot.Data/DapperDataService.cs` | Dapper implementation (2749 lines) |
| `src/VyralBot.UI/Services/ProfileAwareDataService.cs` | Profile routing wrapper |
