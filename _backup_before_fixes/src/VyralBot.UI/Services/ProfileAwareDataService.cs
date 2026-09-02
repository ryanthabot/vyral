using Microsoft.Extensions.Logging;
using VyralBot.Core.Services.Profiles;
using VyralBot.Data;
using VyralBot.Data.Entities;
using System.IO;
using Microsoft.Data.Sqlite;

namespace VyralBot.UI.Services;

/// <summary>
/// Profile-aware data service that dynamically switches database based on current profile.
/// </summary>
public class ProfileAwareDataService : IDataService
{
    private readonly IProfileService _profileService;
    private readonly ILogger<DapperDataService> _logger;
    private DapperDataService? _currentService;
    private string? _currentProfileName;

    public ProfileAwareDataService(IProfileService profileService, ILogger<DapperDataService> logger)
    {
        _profileService = profileService ?? throw new ArgumentNullException(nameof(profileService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    private DapperDataService GetCurrentService()
    {
        var profileName = _profileService.GetCurrentProfileName() ?? "temp";

        // If profile changed, recreate the service with new database path
        if (_currentService == null || _currentProfileName != profileName)
        {
            _currentProfileName = profileName;

            var profileDataPath = _profileService.GetProfileDataPath(profileName);
            Directory.CreateDirectory(profileDataPath);

            var dbPath = Path.Combine(profileDataPath, "vyralbot.db");
            var connectionString = $"Data Source={dbPath}";

            _logger.LogInformation("Switching to database for profile: {ProfileName} at {DbPath}", profileName, dbPath);
            _currentService = new DapperDataService(connectionString, _logger);
        }

        return _currentService;
    }

    /// <summary>
    /// Clears the current database connection to allow file operations on the database.
    /// </summary>
    public void ClearConnection()
    {
        _currentService = null;
        _currentProfileName = null;

        // Force close all SQLite connection pools to release file locks
        SqliteConnection.ClearAllPools();

        // Force garbage collection to release any remaining handles
        GC.Collect();
        GC.WaitForPendingFinalizers();

        _logger.LogInformation("Cleared database connection cache and SQLite pools");
    }

    public Task EnsureDatabaseCreatedAsync() => GetCurrentService().EnsureDatabaseCreatedAsync();

    public Task<IEnumerable<Platform>> GetPlatformsAsync() => GetCurrentService().GetPlatformsAsync();
    public Task<Platform?> GetPlatformByNameAsync(string name) => GetCurrentService().GetPlatformByNameAsync(name);
    public Task<Platform?> GetPlatformByIdAsync(Guid id) => GetCurrentService().GetPlatformByIdAsync(id);
    public Task SavePlatformAsync(Platform platform) => GetCurrentService().SavePlatformAsync(platform);
    public Task DeletePlatformAsync(Guid id) => GetCurrentService().DeletePlatformAsync(id);

    public Task<string?> GetConfigValueAsync(string key) => GetCurrentService().GetConfigValueAsync(key);
    public Task SetConfigValueAsync(string key, string value, string? category = null) => GetCurrentService().SetConfigValueAsync(key, value, category);
    public Task<IEnumerable<Config>> GetConfigsByCategoryAsync(string category) => GetCurrentService().GetConfigsByCategoryAsync(category);

    public Task<IEnumerable<Trigger>> GetTriggersAsync(bool? enabledOnly = null) => GetCurrentService().GetTriggersAsync(enabledOnly);
    public Task<Trigger?> GetTriggerByIdAsync(Guid id) => GetCurrentService().GetTriggerByIdAsync(id);
    public Task SaveTriggerAsync(Trigger trigger) => GetCurrentService().SaveTriggerAsync(trigger);
    public Task DeleteTriggerAsync(Guid id) => GetCurrentService().DeleteTriggerAsync(id);

    public Task<IEnumerable<BotAction>> GetActionsAsync() => GetCurrentService().GetActionsAsync();
    public Task<BotAction?> GetActionByIdAsync(Guid id) => GetCurrentService().GetActionByIdAsync(id);
    public Task SaveActionAsync(BotAction action) => GetCurrentService().SaveActionAsync(action);
    public Task DeleteActionAsync(Guid id) => GetCurrentService().DeleteActionAsync(id);

    public Task<IEnumerable<EventLog>> GetEventLogsAsync(int limit = 100) => GetCurrentService().GetEventLogsAsync(limit);
    public Task LogEventAsync(EventLog log) => GetCurrentService().LogEventAsync(log);

    public Task<WebServerSettings> GetWebServerSettingsAsync() => GetCurrentService().GetWebServerSettingsAsync();
    public Task SaveWebServerSettingsAsync(WebServerSettings settings) => GetCurrentService().SaveWebServerSettingsAsync(settings);
    public Task<IEnumerable<WebhookSubscription>> GetWebhookSubscriptionsAsync(string? platform = null) => GetCurrentService().GetWebhookSubscriptionsAsync(platform);
    public Task SaveWebhookSubscriptionAsync(WebhookSubscription subscription) => GetCurrentService().SaveWebhookSubscriptionAsync(subscription);
    public Task DeleteWebhookSubscriptionAsync(string id) => GetCurrentService().DeleteWebhookSubscriptionAsync(id);
    public Task DeleteWebhookSubscriptionsByPlatformAsync(string platform) => GetCurrentService().DeleteWebhookSubscriptionsByPlatformAsync(platform);

    // Chat message operations
    public Task<bool> SaveChatMessageAsync(Data.Entities.ChatMessage message) => GetCurrentService().SaveChatMessageAsync(message);
    public Task<IEnumerable<Data.Entities.ChatMessage>> GetChatMessagesAsync(string platform, string channel, int limit = 200) => GetCurrentService().GetChatMessagesAsync(platform, channel, limit);
    public Task DeleteChatMessagesAsync(string platform, string channel) => GetCurrentService().DeleteChatMessagesAsync(platform, channel);

    // Chat event settings operations
    public Task<ChatEventSetting?> GetChatEventSettingAsync(string platform, string eventType) => GetCurrentService().GetChatEventSettingAsync(platform, eventType);
    public Task<IEnumerable<ChatEventSetting>> GetAllChatEventSettingsAsync(string? platform = null) => GetCurrentService().GetAllChatEventSettingsAsync(platform);
    public Task SaveChatEventSettingAsync(ChatEventSetting setting) => GetCurrentService().SaveChatEventSettingAsync(setting);
    public Task DeleteChatEventSettingAsync(Guid id) => GetCurrentService().DeleteChatEventSettingAsync(id);

    // Event statistics operations
    public Task SaveEventStatisticAsync(EventStatistic statistic) => GetCurrentService().SaveEventStatisticAsync(statistic);
    public Task<IEnumerable<EventStatistic>> GetEventStatisticsAsync(string? platform = null, string? eventType = null, int limit = 100) => GetCurrentService().GetEventStatisticsAsync(platform, eventType, limit);
    public Task<IEnumerable<EventStatistic>> GetEventStatisticsByUsernameAsync(string username, string? platform = null) => GetCurrentService().GetEventStatisticsByUsernameAsync(username, platform);

    // Currency operations
    public Task<CurrencySetting?> GetCurrencySettingsAsync() => GetCurrentService().GetCurrencySettingsAsync();
    public Task SaveCurrencySettingsAsync(CurrencySetting settings) => GetCurrentService().SaveCurrencySettingsAsync(settings);
    public Task<UserCurrency?> GetUserCurrencyAsync(string username, string platform) => GetCurrentService().GetUserCurrencyAsync(username, platform);
    public Task<IEnumerable<UserCurrency>> GetUserCurrenciesAsync(string username) => GetCurrentService().GetUserCurrenciesAsync(username);
    public Task<IEnumerable<UserCurrency>> GetCurrencyLeaderboardAsync(int limit = 10, string? platform = null) => GetCurrentService().GetCurrencyLeaderboardAsync(limit, platform);
    public Task SaveUserCurrencyAsync(UserCurrency currency) => GetCurrentService().SaveUserCurrencyAsync(currency);
    public Task SaveCurrencyTransactionAsync(CurrencyTransaction transaction) => GetCurrentService().SaveCurrencyTransactionAsync(transaction);
    public Task<IEnumerable<CurrencyTransaction>> GetCurrencyTransactionsAsync(string? username = null, string? platform = null, int limit = 100) => GetCurrentService().GetCurrencyTransactionsAsync(username, platform, limit);
    public Task<CurrencyReward?> GetCurrencyRewardAsync(string id) => GetCurrentService().GetCurrencyRewardAsync(id);
    public Task<IEnumerable<CurrencyReward>> GetCurrencyRewardsAsync(bool? enabledOnly = null) => GetCurrentService().GetCurrencyRewardsAsync(enabledOnly);
    public Task SaveCurrencyRewardAsync(CurrencyReward reward) => GetCurrentService().SaveCurrencyRewardAsync(reward);
    public Task DeleteCurrencyRewardAsync(string id) => GetCurrentService().DeleteCurrencyRewardAsync(id);
    public Task<CurrencyRedemption?> GetCurrencyRedemptionAsync(string id) => GetCurrentService().GetCurrencyRedemptionAsync(id);
    public Task<IEnumerable<CurrencyRedemption>> GetCurrencyRedemptionsAsync(string? status = null, string? username = null) => GetCurrentService().GetCurrencyRedemptionsAsync(status, username);
    public Task SaveCurrencyRedemptionAsync(CurrencyRedemption redemption) => GetCurrentService().SaveCurrencyRedemptionAsync(redemption);
    public Task DeleteCurrencyRedemptionAsync(string id) => GetCurrentService().DeleteCurrencyRedemptionAsync(id);

    // Linked account operations
    public Task<LinkedAccount?> GetLinkedAccountAsync(string username, string platform) => GetCurrentService().GetLinkedAccountAsync(username, platform);
    public Task<IEnumerable<LinkedAccount>> GetLinkedAccountsByViewerIdAsync(string viewerId) => GetCurrentService().GetLinkedAccountsByViewerIdAsync(viewerId);
    public Task<string?> GetViewerIdAsync(string username, string platform) => GetCurrentService().GetViewerIdAsync(username, platform);
    public Task SaveLinkedAccountAsync(LinkedAccount account) => GetCurrentService().SaveLinkedAccountAsync(account);
    public Task DeleteLinkedAccountAsync(string id) => GetCurrentService().DeleteLinkedAccountAsync(id);

    // Viewer streak operations
    public Task<ViewerStreak?> GetViewerStreakAsync(string username, string platform) => GetCurrentService().GetViewerStreakAsync(username, platform);
    public Task SaveViewerStreakAsync(ViewerStreak streak) => GetCurrentService().SaveViewerStreakAsync(streak);

    // Loyalty tier operations
    public Task<IEnumerable<LoyaltyTier>> GetLoyaltyTiersAsync() => GetCurrentService().GetLoyaltyTiersAsync();
    public Task<LoyaltyTier?> GetLoyaltyTierAsync(string id) => GetCurrentService().GetLoyaltyTierAsync(id);
    public Task SaveLoyaltyTierAsync(LoyaltyTier tier) => GetCurrentService().SaveLoyaltyTierAsync(tier);
    public Task DeleteLoyaltyTierAsync(string id) => GetCurrentService().DeleteLoyaltyTierAsync(id);

    // Currency game settings operations
    public Task<CurrencyGameSetting?> GetCurrencyGameSettingAsync(string gameType) => GetCurrentService().GetCurrencyGameSettingAsync(gameType);
    public Task<IEnumerable<CurrencyGameSetting>> GetCurrencyGameSettingsAsync() => GetCurrentService().GetCurrencyGameSettingsAsync();
    public Task SaveCurrencyGameSettingAsync(CurrencyGameSetting setting) => GetCurrentService().SaveCurrencyGameSettingAsync(setting);

    // Viewer perk operations
    public Task<IEnumerable<ViewerPerk>> GetActiveViewerPerksAsync(string username, string platform) => GetCurrentService().GetActiveViewerPerksAsync(username, platform);
    public Task<IEnumerable<ViewerPerk>> GetViewerPerksByTypeAsync(string username, string platform, string perkType) => GetCurrentService().GetViewerPerksByTypeAsync(username, platform, perkType);
    public Task SaveViewerPerkAsync(ViewerPerk perk) => GetCurrentService().SaveViewerPerkAsync(perk);
    public Task ExpireViewerPerksAsync() => GetCurrentService().ExpireViewerPerksAsync();

    // Currency boost operations
    public Task<CurrencyBoost?> GetCurrencyBoostAsync(string id) => GetCurrentService().GetCurrencyBoostAsync(id);
    public Task<IEnumerable<CurrencyBoost>> GetActiveBoostsAsync(string? platform = null) => GetCurrentService().GetActiveBoostsAsync(platform);
    public Task<IEnumerable<CurrencyBoost>> GetBoostHistoryAsync(int limit = 50) => GetCurrentService().GetBoostHistoryAsync(limit);
    public Task SaveCurrencyBoostAsync(CurrencyBoost boost) => GetCurrentService().SaveCurrencyBoostAsync(boost);
    public Task ExpireBoostsAsync() => GetCurrentService().ExpireBoostsAsync();

    // Currency auction operations
    public Task<CurrencyAuction?> GetCurrencyAuctionAsync(string id) => GetCurrentService().GetCurrencyAuctionAsync(id);
    public Task<CurrencyAuction?> GetActiveAuctionAsync() => GetCurrentService().GetActiveAuctionAsync();
    public Task<IEnumerable<CurrencyAuction>> GetAuctionHistoryAsync(int limit = 20) => GetCurrentService().GetAuctionHistoryAsync(limit);
    public Task SaveCurrencyAuctionAsync(CurrencyAuction auction) => GetCurrentService().SaveCurrencyAuctionAsync(auction);
    public Task SaveCurrencyAuctionBidAsync(CurrencyAuctionBid bid) => GetCurrentService().SaveCurrencyAuctionBidAsync(bid);
    public Task<IEnumerable<CurrencyAuctionBid>> GetAuctionBidsAsync(string auctionId) => GetCurrentService().GetAuctionBidsAsync(auctionId);

    // Bank account operations
    public Task<UserBankAccount?> GetUserBankAccountAsync(string username, string platform) => GetCurrentService().GetUserBankAccountAsync(username, platform);
    public Task<IEnumerable<UserBankAccount>> GetAllBankAccountsAsync() => GetCurrentService().GetAllBankAccountsAsync();
    public Task SaveUserBankAccountAsync(UserBankAccount account) => GetCurrentService().SaveUserBankAccountAsync(account);

    // Currency decay operations
    public Task<IEnumerable<UserCurrency>> GetInactiveUsersForDecayAsync(DateTime cutoffDate) => GetCurrentService().GetInactiveUsersForDecayAsync(cutoffDate);

    // Command operations
    public Task<Command?> GetCommandAsync(string id) => GetCurrentService().GetCommandAsync(id);
    public Task<Command?> GetCommandByNameAsync(string name) => GetCurrentService().GetCommandByNameAsync(name);
    public Task<IEnumerable<Command>> GetCommandsAsync(bool? enabledOnly = null, bool? builtInOnly = null) => GetCurrentService().GetCommandsAsync(enabledOnly, builtInOnly);
    public Task SaveCommandAsync(Command command) => GetCurrentService().SaveCommandAsync(command);
    public Task DeleteCommandAsync(string id) => GetCurrentService().DeleteCommandAsync(id);
    public Task IncrementCommandUsageAsync(string id) => GetCurrentService().IncrementCommandUsageAsync(id);
    public Task<CommandCooldown?> GetCommandCooldownAsync(string commandId, string? username, string platform) => GetCurrentService().GetCommandCooldownAsync(commandId, username, platform);
    public Task SaveCommandCooldownAsync(CommandCooldown cooldown) => GetCurrentService().SaveCommandCooldownAsync(cooldown);
    public Task DeleteExpiredCooldownsAsync() => GetCurrentService().DeleteExpiredCooldownsAsync();

    // AutoReplyRule operations
    public Task<IEnumerable<AutoReplyRule>> GetAutoReplyRulesAsync() => GetCurrentService().GetAutoReplyRulesAsync();
    public Task<IEnumerable<AutoReplyRule>> GetAutoReplyRulesByPlatformAndEventAsync(string platform, string eventType) => GetCurrentService().GetAutoReplyRulesByPlatformAndEventAsync(platform, eventType);
    public Task<AutoReplyRule?> GetAutoReplyRuleAsync(int id) => GetCurrentService().GetAutoReplyRuleAsync(id);
    public Task<int> SaveAutoReplyRuleAsync(AutoReplyRule rule) => GetCurrentService().SaveAutoReplyRuleAsync(rule);
    public Task DeleteAutoReplyRuleAsync(int id) => GetCurrentService().DeleteAutoReplyRuleAsync(id);
    public Task IncrementRuleTriggerCountAsync(int id) => GetCurrentService().IncrementRuleTriggerCountAsync(id);

    // Giveaway operations
    public Task<IEnumerable<Giveaway>> GetGiveawaysAsync() => GetCurrentService().GetGiveawaysAsync();
    public Task<Giveaway?> GetGiveawayAsync(Guid id) => GetCurrentService().GetGiveawayAsync(id);
    public Task<Giveaway?> GetActiveGiveawayAsync() => GetCurrentService().GetActiveGiveawayAsync();
    public Task SaveGiveawayAsync(Giveaway giveaway) => GetCurrentService().SaveGiveawayAsync(giveaway);
    public Task DeleteGiveawayAsync(Guid id) => GetCurrentService().DeleteGiveawayAsync(id);
    public Task<IEnumerable<GiveawayEntry>> GetGiveawayEntriesAsync(Guid giveawayId) => GetCurrentService().GetGiveawayEntriesAsync(giveawayId);
    public Task<GiveawayEntry?> GetGiveawayEntryAsync(Guid giveawayId, string username, string platform) => GetCurrentService().GetGiveawayEntryAsync(giveawayId, username, platform);
    public Task SaveGiveawayEntryAsync(GiveawayEntry entry) => GetCurrentService().SaveGiveawayEntryAsync(entry);
    public Task<int> GetGiveawayEntryCountAsync(Guid giveawayId) => GetCurrentService().GetGiveawayEntryCountAsync(giveawayId);
    public Task<int> GetGiveawayUniqueParticipantsAsync(Guid giveawayId) => GetCurrentService().GetGiveawayUniqueParticipantsAsync(giveawayId);

    // Poll operations
    public Task<IEnumerable<Poll>> GetPollsAsync() => GetCurrentService().GetPollsAsync();
    public Task<Poll?> GetPollAsync(Guid id) => GetCurrentService().GetPollAsync(id);
    public Task<Poll?> GetActivePollAsync() => GetCurrentService().GetActivePollAsync();
    public Task SavePollAsync(Poll poll) => GetCurrentService().SavePollAsync(poll);
    public Task DeletePollAsync(Guid id) => GetCurrentService().DeletePollAsync(id);
    public Task<IEnumerable<PollVote>> GetPollVotesAsync(Guid pollId) => GetCurrentService().GetPollVotesAsync(pollId);
    public Task<PollVote?> GetPollVoteAsync(Guid pollId, string username, string platform) => GetCurrentService().GetPollVoteAsync(pollId, username, platform);
    public Task SavePollVoteAsync(PollVote vote) => GetCurrentService().SavePollVoteAsync(vote);
    public Task<int> GetPollVoteCountAsync(Guid pollId) => GetCurrentService().GetPollVoteCountAsync(pollId);
    public Task<Dictionary<int, int>> GetPollVoteResultsAsync(Guid pollId) => GetCurrentService().GetPollVoteResultsAsync(pollId);

    // Watchtime operations
    public Task<UserWatchtime?> GetUserWatchtimeAsync(string username, string platform) => GetCurrentService().GetUserWatchtimeAsync(username, platform);
    public Task<IEnumerable<UserWatchtime>> GetAllUserWatchtimeAsync() => GetCurrentService().GetAllUserWatchtimeAsync();
    public Task<IEnumerable<UserWatchtime>> GetTopWatchtimeUsersAsync(int limit = 10) => GetCurrentService().GetTopWatchtimeUsersAsync(limit);
    public Task<IEnumerable<UserWatchtime>> GetActiveViewersAsync() => GetCurrentService().GetActiveViewersAsync();
    public Task SaveUserWatchtimeAsync(UserWatchtime watchtime) => GetCurrentService().SaveUserWatchtimeAsync(watchtime);
    public Task<int> GetTotalActiveViewersCountAsync() => GetCurrentService().GetTotalActiveViewersCountAsync();
    public Task ResetStreamSessionMinutesAsync() => GetCurrentService().ResetStreamSessionMinutesAsync();
    public Task DeactivateInactiveViewersAsync(DateTime inactiveSince) => GetCurrentService().DeactivateInactiveViewersAsync(inactiveSince);

    // Variable operations
    public Task<Variable?> GetVariableAsync(string id) => GetCurrentService().GetVariableAsync(id);
    public Task<Variable?> GetVariableByNameAsync(string name) => GetCurrentService().GetVariableByNameAsync(name);
    public Task<IEnumerable<Variable>> GetVariablesAsync() => GetCurrentService().GetVariablesAsync();
    public Task<IEnumerable<Variable>> GetVariablesByCategoryAsync(string category) => GetCurrentService().GetVariablesByCategoryAsync(category);
    public Task<IEnumerable<string>> GetVariableCategoriesAsync() => GetCurrentService().GetVariableCategoriesAsync();
    public Task SaveVariableAsync(Variable variable) => GetCurrentService().SaveVariableAsync(variable);
    public Task DeleteVariableAsync(string id) => GetCurrentService().DeleteVariableAsync(id);
    public Task UpdateVariableValueAsync(string name, string? value) => GetCurrentService().UpdateVariableValueAsync(name, value);
}
