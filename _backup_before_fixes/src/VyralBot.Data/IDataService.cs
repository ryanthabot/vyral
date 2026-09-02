using VyralBot.Data.Entities;

namespace VyralBot.Data;

/// <summary>
/// Lightweight data access service using Dapper.
/// Replaces EF Core DbContext to reduce memory footprint.
/// </summary>
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

    // Chat messages (per-profile)
    Task<bool> SaveChatMessageAsync(Entities.ChatMessage message);
    Task<IEnumerable<Entities.ChatMessage>> GetChatMessagesAsync(string platform, string channel, int limit = 200);
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
    Task<IEnumerable<UserCurrency>> GetUserCurrenciesAsync(string username); // All platforms for user
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

    // Linked Accounts (Cross-Platform Identity)
    Task<LinkedAccount?> GetLinkedAccountAsync(string username, string platform);
    Task<IEnumerable<LinkedAccount>> GetLinkedAccountsByViewerIdAsync(string viewerId);
    Task<string?> GetViewerIdAsync(string username, string platform);
    Task SaveLinkedAccountAsync(LinkedAccount account);
    Task DeleteLinkedAccountAsync(string id);

    // Viewer Streaks
    Task<ViewerStreak?> GetViewerStreakAsync(string username, string platform);
    Task SaveViewerStreakAsync(ViewerStreak streak);

    // Loyalty Tiers
    Task<IEnumerable<LoyaltyTier>> GetLoyaltyTiersAsync();
    Task<LoyaltyTier?> GetLoyaltyTierAsync(string id);
    Task SaveLoyaltyTierAsync(LoyaltyTier tier);
    Task DeleteLoyaltyTierAsync(string id);

    // Currency Game Settings
    Task<CurrencyGameSetting?> GetCurrencyGameSettingAsync(string gameType);
    Task<IEnumerable<CurrencyGameSetting>> GetCurrencyGameSettingsAsync();
    Task SaveCurrencyGameSettingAsync(CurrencyGameSetting setting);

    // Viewer Perks
    Task<IEnumerable<ViewerPerk>> GetActiveViewerPerksAsync(string username, string platform);
    Task<IEnumerable<ViewerPerk>> GetViewerPerksByTypeAsync(string username, string platform, string perkType);
    Task SaveViewerPerkAsync(ViewerPerk perk);
    Task ExpireViewerPerksAsync();

    // Currency Boosts
    Task<CurrencyBoost?> GetCurrencyBoostAsync(string id);
    Task<IEnumerable<CurrencyBoost>> GetActiveBoostsAsync(string? platform = null);
    Task<IEnumerable<CurrencyBoost>> GetBoostHistoryAsync(int limit = 50);
    Task SaveCurrencyBoostAsync(CurrencyBoost boost);
    Task ExpireBoostsAsync();

    // Currency Auctions
    Task<CurrencyAuction?> GetCurrencyAuctionAsync(string id);
    Task<CurrencyAuction?> GetActiveAuctionAsync();
    Task<IEnumerable<CurrencyAuction>> GetAuctionHistoryAsync(int limit = 20);
    Task SaveCurrencyAuctionAsync(CurrencyAuction auction);
    Task SaveCurrencyAuctionBidAsync(CurrencyAuctionBid bid);
    Task<IEnumerable<CurrencyAuctionBid>> GetAuctionBidsAsync(string auctionId);

    // Bank Accounts
    Task<UserBankAccount?> GetUserBankAccountAsync(string username, string platform);
    Task<IEnumerable<UserBankAccount>> GetAllBankAccountsAsync();
    Task SaveUserBankAccountAsync(UserBankAccount account);

    // Currency Decay
    Task<IEnumerable<UserCurrency>> GetInactiveUsersForDecayAsync(DateTime cutoffDate);

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
    Task<int> SaveAutoReplyRuleAsync(AutoReplyRule rule); // Returns the rule ID
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
