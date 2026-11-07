using VyralBot.Core.Services.Currency;
using VyralBot.Data;
using VyralBot.Data.Entities;

namespace VyralBot.UI.Tests;

// ======================================================================
// Shared test doubles for the marathon wave-2 regression tests
// (ChatModerationEngine / CurrencyBankService cooldown /
// ModerationRulesRepository / AlertService settings).
//
// The skeleton of Wave2TestDataService below is deliberately a verbatim
// copy of FakeDataService in ModCommandEngineTests.cs (same throw-
// everything-else contract) with REAL in-memory behavior for the members
// the wave-2 code paths touch: config key/value storage, bank accounts
// and the currency-transaction audit trail.
// ======================================================================

/// <summary>
/// In-memory IDataService: real behavior for the config table, bank
/// accounts and currency transactions; every other member throws (a test
/// should never depend on it). Transactions are returned most-recent-first
/// (the order the Dapper data layer uses for GetCurrencyTransactionsAsync),
/// so the "first BankWithdrawal row" is the latest withdrawal like in
/// production.
/// </summary>
public class Wave2TestDataService : IDataService
{
    public List<ModAction> ModActions { get; } = new();
    public Dictionary<string, ModRoleAssignment> ModRoleAssignments { get; } = new(StringComparer.Ordinal);
    public Dictionary<Guid, Giveaway> Giveaways { get; } = new();
    public Dictionary<Guid, Poll> Polls { get; } = new();

    private const string NotImplemented = "Not needed for wave-2 regression tests.";

    // ----- Configs (real behavior: key/value with optional category) -----

    /// <summary>Stored config values (key → JSON or plain string), in save order.</summary>
    public Dictionary<string, string> ConfigValues { get; } = new(StringComparer.Ordinal);

    /// <summary>(key, value, category) tuples in the order SetConfigValueAsync wrote them.</summary>
    public List<(string Key, string Value, string? Category)> ConfigWrites { get; } = new();

    public virtual Task<string?> GetConfigValueAsync(string key)
        => Task.FromResult(ConfigValues.TryGetValue(key, out var value) ? value : null);

    public Task SetConfigValueAsync(string key, string value, string? category = null)
    {
        ConfigValues[key] = value;
        ConfigWrites.Add((key, value, category));
        return Task.CompletedTask;
    }

    public Task<bool> DeleteConfigValueAsync(string key, string? category = null)
        => Task.FromResult(ConfigValues.Remove(key));

    // ----- Mod system (real behavior) -----

    public Task SaveModActionAsync(ModAction action)
    {
        ModActions.Add(action);
        return Task.CompletedTask;
    }

    public Task<IEnumerable<ModAction>> GetModActionsAsync(int limit = 200)
        => Task.FromResult<IEnumerable<ModAction>>(ModActions.Take(limit).ToList());

    public Task<IEnumerable<ModAction>> GetModActionsForViewerAsync(string username, string userId, int limit = 100)
        => Task.FromResult<IEnumerable<ModAction>>(ModActions.Take(limit).ToList());

    public Task SaveModRoleAssignmentAsync(ModRoleAssignment assignment)
    {
        ModRoleAssignments[assignment.DiscordUserId] = assignment;
        return Task.CompletedTask;
    }

    public Task DeleteModRoleAssignmentAsync(string discordUserId)
    {
        ModRoleAssignments.Remove(discordUserId);
        return Task.CompletedTask;
    }

    public Task<ModRoleAssignment?> GetModRoleAssignmentAsync(string discordUserId)
        => Task.FromResult(ModRoleAssignments.TryGetValue(discordUserId, out var a) ? a : null);

    public Task<IEnumerable<ModRoleAssignment>> GetModRoleAssignmentsAsync()
        => Task.FromResult<IEnumerable<ModRoleAssignment>>(ModRoleAssignments.Values.ToList());

    // ----- Connected channels / role tiers / OBS scene collections (not needed here) -----

    public Task<List<ConnectedChannel>> GetConnectedChannelsAsync(string botId) => throw new NotImplementedException(NotImplemented);
    public Task<ConnectedChannel> UpsertConnectedChannelAsync(ConnectedChannel channel) => throw new NotImplementedException(NotImplemented);
    public Task DeleteConnectedChannelAsync(string botId, string channelId) => throw new NotImplementedException(NotImplemented);
    public Task<int> CountConnectedChannelsAsync(string botId) => throw new NotImplementedException(NotImplemented);
    public Task<List<DiscordRoleTierAssignment>> GetRoleTierAssignmentsAsync(string botId) => throw new NotImplementedException(NotImplemented);
    public Task UpsertRoleTierAssignmentAsync(DiscordRoleTierAssignment assignment) => throw new NotImplementedException(NotImplemented);
    public Task DeleteRoleTierAssignmentAsync(string botId, string discordRoleId) => throw new NotImplementedException(NotImplemented);
    public Task<List<ObsSceneCollection>> GetObsSceneCollectionsAsync() => throw new NotImplementedException(NotImplemented);
    public Task<ObsSceneCollection?> GetObsSceneCollectionAsync(int id) => throw new NotImplementedException(NotImplemented);
    public Task<ObsSceneCollection> SaveObsSceneCollectionAsync(ObsSceneCollection collection) => throw new NotImplementedException(NotImplemented);
    public Task DeleteObsSceneCollectionAsync(int id) => throw new NotImplementedException(NotImplemented);

    // ----- Giveaways / polls (real behavior; real GiveawayService/PollService run on top) -----

    public Task<Giveaway?> GetGiveawayAsync(Guid id)
        => Task.FromResult(Giveaways.TryGetValue(id, out var g) ? g : null);

    public Task<Giveaway?> GetActiveGiveawayAsync()
        => Task.FromResult(Giveaways.Values.FirstOrDefault(g => g.Status == GiveawayStatus.Active));

    public Task SaveGiveawayAsync(Giveaway giveaway)
    {
        Giveaways[giveaway.Id] = giveaway;
        return Task.CompletedTask;
    }

    public Task<Poll?> GetPollAsync(Guid id)
        => Task.FromResult(Polls.TryGetValue(id, out var p) ? p : null);

    public Task<Poll?> GetActivePollAsync()
        => Task.FromResult(Polls.Values.FirstOrDefault(p => p.Status == PollStatus.Active));

    public Task SavePollAsync(Poll poll)
    {
        Polls[poll.Id] = poll;
        return Task.CompletedTask;
    }

    public Task<Dictionary<int, int>> GetPollVoteResultsAsync(Guid pollId)
        => Task.FromResult(new Dictionary<int, int>());

    // ----- Everything else (out of scope) -----

    public Task<IEnumerable<Platform>> GetPlatformsAsync() => throw new NotImplementedException(NotImplemented);
    public Task<Platform?> GetPlatformByNameAsync(string name) => throw new NotImplementedException(NotImplemented);
    public Task<Platform?> GetPlatformByIdAsync(Guid id) => throw new NotImplementedException(NotImplemented);
    public Task SavePlatformAsync(Platform platform) => throw new NotImplementedException(NotImplemented);
    public Task DeletePlatformAsync(Guid id) => throw new NotImplementedException(NotImplemented);
    public Task<IEnumerable<DiscordBot>> GetDiscordBotsAsync() => throw new NotImplementedException(NotImplemented);
    public Task SaveDiscordBotAsync(DiscordBot bot) => throw new NotImplementedException(NotImplemented);
    public Task DeleteDiscordBotAsync(string id) => throw new NotImplementedException(NotImplemented);
    public Task<string?> GetAssignedChannelIdsAsync(string botId) => throw new NotImplementedException(NotImplemented);
    public Task SetAssignedChannelIdsAsync(string botId, string channelIds) => throw new NotImplementedException(NotImplemented);
    public Task<IEnumerable<WorkflowEntity>> GetWorkflowsAsync() => throw new NotImplementedException(NotImplemented);
    public Task SaveWorkflowAsync(WorkflowEntity workflow) => throw new NotImplementedException(NotImplemented);
    public Task DeleteWorkflowAsync(string id) => throw new NotImplementedException(NotImplemented);
    public Task<IEnumerable<WorkflowExecution>> GetWorkflowExecutionsAsync(string? workflowId = null, int limit = 50) => throw new NotImplementedException(NotImplemented);
    public Task SaveWorkflowExecutionAsync(WorkflowExecution execution) => throw new NotImplementedException(NotImplemented);
    public Task<IEnumerable<Config>> GetConfigsByCategoryAsync(string category) => throw new NotImplementedException(NotImplemented);
    public Task<IEnumerable<Trigger>> GetTriggersAsync(bool? enabledOnly = null) => throw new NotImplementedException(NotImplemented);
    public Task<Trigger?> GetTriggerByIdAsync(Guid id) => throw new NotImplementedException(NotImplemented);
    public Task SaveTriggerAsync(Trigger trigger) => throw new NotImplementedException(NotImplemented);
    public Task DeleteTriggerAsync(Guid id) => throw new NotImplementedException(NotImplemented);
    public Task<IEnumerable<BotAction>> GetActionsAsync() => throw new NotImplementedException(NotImplemented);
    public Task<BotAction?> GetActionByIdAsync(Guid id) => throw new NotImplementedException(NotImplemented);
    public Task SaveActionAsync(BotAction action) => throw new NotImplementedException(NotImplemented);
    public Task DeleteActionAsync(Guid id) => throw new NotImplementedException(NotImplemented);
    public Task<IEnumerable<EventLog>> GetEventLogsAsync(int limit = 100) => throw new NotImplementedException(NotImplemented);
    public Task LogEventAsync(EventLog log) => throw new NotImplementedException(NotImplemented);
    public Task<WebServerSettings> GetWebServerSettingsAsync() => throw new NotImplementedException(NotImplemented);
    public Task SaveWebServerSettingsAsync(WebServerSettings settings) => throw new NotImplementedException(NotImplemented);
    public Task<IEnumerable<WebhookSubscription>> GetWebhookSubscriptionsAsync(string? platform = null) => throw new NotImplementedException(NotImplemented);
    public Task SaveWebhookSubscriptionAsync(WebhookSubscription subscription) => throw new NotImplementedException(NotImplemented);
    public Task DeleteWebhookSubscriptionAsync(string id) => throw new NotImplementedException(NotImplemented);
    public Task DeleteWebhookSubscriptionsByPlatformAsync(string platform) => throw new NotImplementedException(NotImplemented);
    public Task EnsureDatabaseCreatedAsync() => throw new NotImplementedException(NotImplemented);
    public Task<IReadOnlyDictionary<string, int>> GetDataInventoryAsync() => throw new NotImplementedException(NotImplemented);
    public Task DeleteAllChatMessagesAsync() => throw new NotImplementedException(NotImplemented);
    public Task DeleteEventLogsAsync() => throw new NotImplementedException(NotImplemented);
    public Task DeleteWorkflowExecutionsAsync(string? workflowId = null, DateTime? olderThan = null) => throw new NotImplementedException(NotImplemented);
    public Task<bool> CompactDatabaseAsync() => throw new NotImplementedException(NotImplemented);
    public Task<bool> SaveChatMessageAsync(ChatMessage message) => throw new NotImplementedException(NotImplemented);
    public Task<IEnumerable<ChatMessage>> GetChatMessagesAsync(string platform, string channel, int limit = 200) => throw new NotImplementedException(NotImplemented);
    public Task DeleteChatMessagesAsync(string platform, string channel) => throw new NotImplementedException(NotImplemented);
    public Task<ChatEventSetting?> GetChatEventSettingAsync(string platform, string eventType) => throw new NotImplementedException(NotImplemented);
    public Task<IEnumerable<ChatEventSetting>> GetAllChatEventSettingsAsync(string? platform = null) => throw new NotImplementedException(NotImplemented);
    public Task SaveChatEventSettingAsync(ChatEventSetting setting) => throw new NotImplementedException(NotImplemented);
    public Task DeleteChatEventSettingAsync(Guid id) => throw new NotImplementedException(NotImplemented);
    public Task SaveEventStatisticAsync(EventStatistic statistic) => throw new NotImplementedException(NotImplemented);
    public Task<IEnumerable<EventStatistic>> GetEventStatisticsAsync(string? platform = null, string? eventType = null, int limit = 100, DateTime? startDate = null, DateTime? endDate = null) => throw new NotImplementedException(NotImplemented);
    public Task<IEnumerable<EventStatistic>> GetEventStatisticsByUsernameAsync(string username, string? platform = null) => throw new NotImplementedException(NotImplemented);
    public Task<int> DeleteEventStatisticsAsync() => throw new NotImplementedException(NotImplemented);
    public Task<CurrencySetting?> GetCurrencySettingsAsync() => throw new NotImplementedException(NotImplemented);
    public Task SaveCurrencySettingsAsync(CurrencySetting settings) => throw new NotImplementedException(NotImplemented);
    public Task<UserCurrency?> GetUserCurrencyAsync(string username, string platform) => throw new NotImplementedException(NotImplemented);
    public Task<IEnumerable<UserCurrency>> GetUserCurrenciesAsync(string username) => throw new NotImplementedException(NotImplemented);
    public Task<IEnumerable<UserCurrency>> GetCurrencyLeaderboardAsync(int limit = 10, string? platform = null) => throw new NotImplementedException(NotImplemented);
    public Task<IEnumerable<UserCurrency>> GetCurrencyUsersPagedAsync(int limit, int offset, string? platform = null) => throw new NotImplementedException(NotImplemented);
    public Task SaveUserCurrencyAsync(UserCurrency currency) => throw new NotImplementedException(NotImplemented);
    // ----- Currency audit trail (real behavior) -----

    /// <summary>Rows in insertion order; GetCurrencyTransactionsAsync returns them reversed (newest first, like the Dapper layer).</summary>
    public List<CurrencyTransaction> Transactions { get; } = new();

    public Task SaveCurrencyTransactionAsync(CurrencyTransaction transaction)
    {
        Transactions.Add(transaction);
        return Task.CompletedTask;
    }

    public Task<IEnumerable<CurrencyTransaction>> GetCurrencyTransactionsAsync(string? username = null, string? platform = null, int limit = 100)
        => Task.FromResult<IEnumerable<CurrencyTransaction>>(
            Transactions
                .Where(t => username == null || t.Username.Equals(username, StringComparison.OrdinalIgnoreCase))
                .Where(t => platform == null || t.Platform.Equals(platform, StringComparison.OrdinalIgnoreCase))
                .Reverse()
                .Take(limit)
                .ToList());

    // ----- Bank accounts (real behavior) -----

    /// <summary>Bank rows keyed "username|platform" (ordinal-ignore-case would need a custom comparer; tests use exact casing).</summary>
    public Dictionary<string, UserBankAccount> BankAccounts { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Task<UserBankAccount?> GetUserBankAccountAsync(string username, string platform)
        => Task.FromResult(BankAccounts.TryGetValue(username + "|" + platform, out var account) ? account : null);

    public Task<IEnumerable<UserBankAccount>> GetAllBankAccountsAsync()
        => Task.FromResult<IEnumerable<UserBankAccount>>(BankAccounts.Values.ToList());

    public Task SaveUserBankAccountAsync(UserBankAccount account)
    {
        BankAccounts[account.Username + "|" + account.Platform] = account;
        return Task.CompletedTask;
    }
    public Task<(long UserCount, long TotalBalance)> GetCurrencyEconomyTotalsAsync() => throw new NotImplementedException(NotImplemented);
    public Task<(long Earned, long Spent)> GetCurrencyTransactionTotalsSinceAsync(DateTime since) => throw new NotImplementedException(NotImplemented);
    public Task<CurrencyReward?> GetCurrencyRewardAsync(string id) => throw new NotImplementedException(NotImplemented);
    public Task<IEnumerable<CurrencyReward>> GetCurrencyRewardsAsync(bool? enabledOnly = null) => throw new NotImplementedException(NotImplemented);
    public Task SaveCurrencyRewardAsync(CurrencyReward reward) => throw new NotImplementedException(NotImplemented);
    public Task DeleteCurrencyRewardAsync(string id) => throw new NotImplementedException(NotImplemented);
    public Task<CurrencyRedemption?> GetCurrencyRedemptionAsync(string id) => throw new NotImplementedException(NotImplemented);
    public Task<IEnumerable<CurrencyRedemption>> GetCurrencyRedemptionsAsync(string? status = null, string? username = null) => throw new NotImplementedException(NotImplemented);
    public Task SaveCurrencyRedemptionAsync(CurrencyRedemption redemption) => throw new NotImplementedException(NotImplemented);
    public Task DeleteCurrencyRedemptionAsync(string id) => throw new NotImplementedException(NotImplemented);
    public Task<LinkedAccount?> GetLinkedAccountAsync(string username, string platform) => throw new NotImplementedException(NotImplemented);
    public Task<IEnumerable<LinkedAccount>> GetLinkedAccountsByViewerIdAsync(string viewerId) => throw new NotImplementedException(NotImplemented);
    public Task<string?> GetViewerIdAsync(string username, string platform) => throw new NotImplementedException(NotImplemented);
    public Task SaveLinkedAccountAsync(LinkedAccount account) => throw new NotImplementedException(NotImplemented);
    public Task DeleteLinkedAccountAsync(string id) => throw new NotImplementedException(NotImplemented);
    public Task<LinkCode?> GetLinkCodeAsync(string code) => throw new NotImplementedException(NotImplemented);
    public Task SaveDiscordDmMessageAsync(DiscordDmMessage message) => throw new NotImplementedException(NotImplemented);
    public Task<IEnumerable<DiscordDmMessage>> GetDiscordDmHistoryAsync(string username) => throw new NotImplementedException(NotImplemented);
    public Task<IEnumerable<(string Username, string? BotUsername, DateTime LastAt, int Total)>> GetDiscordDmConversationsAsync() => throw new NotImplementedException(NotImplemented);
    public Task<IEnumerable<DiscordDmMessage>> SearchDiscordDmMessagesAsync(string query) => throw new NotImplementedException(NotImplemented);
    public Task ClearDiscordDmHistoryAsync(string? username) => throw new NotImplementedException(NotImplemented);
    public Task SaveLinkCodeAsync(LinkCode code) => throw new NotImplementedException(NotImplemented);
    public Task InvalidateLinkCodesForViewerAsync(string viewerId) => throw new NotImplementedException(NotImplemented);
    public Task<IEnumerable<LinkCode>> GetActiveLinkCodesAsync() => throw new NotImplementedException(NotImplemented);
    public Task<IEnumerable<LinkCode>> GetRecentLinkCodesAsync(int limit = 50) => throw new NotImplementedException(NotImplemented);
    public Task<IEnumerable<LinkCode>> GetUnredeemedLinkCodesByViewerAsync(string viewerId) => throw new NotImplementedException(NotImplemented);
    public Task DeleteLinkCodeAsync(string id) => throw new NotImplementedException(NotImplemented);
    public Task<IEnumerable<LinkedAccount>> GetAllLinkedAccountsAsync() => throw new NotImplementedException(NotImplemented);
    public Task UpdateLinkedAccountViewerAsync(string accountId, string newViewerId) => throw new NotImplementedException(NotImplemented);
    public Task DeleteLinkedAccountsByViewerIdAsync(string viewerId) => throw new NotImplementedException(NotImplemented);
    public Task<ViewerPlatformStatus?> GetViewerPlatformStatusAsync(string username, string platform) => throw new NotImplementedException(NotImplemented);
    public Task SaveViewerPlatformStatusAsync(ViewerPlatformStatus status) => throw new NotImplementedException(NotImplemented);
    public Task<IEnumerable<ViewerPlatformStatus>> GetViewerStatusesByViewerIdAsync(string viewerId) => throw new NotImplementedException(NotImplemented);
    public Task<ViewerPreferences?> GetViewerPreferencesAsync(string viewerId) => throw new NotImplementedException(NotImplemented);
    public Task SaveViewerPreferencesAsync(ViewerPreferences preferences) => throw new NotImplementedException(NotImplemented);
    public Task<IEnumerable<SocialGoal>> GetSocialGoalsAsync() => throw new NotImplementedException(NotImplemented);
    public Task<SocialGoal?> GetSocialGoalAsync(Guid id) => throw new NotImplementedException(NotImplemented);
    public Task SaveSocialGoalAsync(SocialGoal goal) => throw new NotImplementedException(NotImplemented);
    public Task DeleteSocialGoalAsync(Guid id) => throw new NotImplementedException(NotImplemented);
    public Task<ViewerGoalCompletion?> GetViewerGoalCompletionAsync(Guid goalId, string viewerId) => throw new NotImplementedException(NotImplemented);
    public Task SaveViewerGoalCompletionAsync(ViewerGoalCompletion completion) => throw new NotImplementedException(NotImplemented);
    public Task<int> GetGoalCompletionCountAsync(Guid goalId) => throw new NotImplementedException(NotImplemented);
    public Task<IEnumerable<ViewerGoalCompletion>> GetViewerGoalCompletionsByViewerIdAsync(string viewerId) => throw new NotImplementedException(NotImplemented);
    public Task<ViewerStreak?> GetViewerStreakAsync(string username, string platform) => throw new NotImplementedException(NotImplemented);
    public Task SaveViewerStreakAsync(ViewerStreak streak) => throw new NotImplementedException(NotImplemented);
    public Task<IEnumerable<LoyaltyTier>> GetLoyaltyTiersAsync() => throw new NotImplementedException(NotImplemented);
    public Task<LoyaltyTier?> GetLoyaltyTierAsync(string id) => throw new NotImplementedException(NotImplemented);
    public Task SaveLoyaltyTierAsync(LoyaltyTier tier) => throw new NotImplementedException(NotImplemented);
    public Task DeleteLoyaltyTierAsync(string id) => throw new NotImplementedException(NotImplemented);
    public Task<CurrencyGameSetting?> GetCurrencyGameSettingAsync(string gameType) => throw new NotImplementedException(NotImplemented);
    public Task<IEnumerable<CurrencyGameSetting>> GetCurrencyGameSettingsAsync() => throw new NotImplementedException(NotImplemented);
    public Task SaveCurrencyGameSettingAsync(CurrencyGameSetting setting) => throw new NotImplementedException(NotImplemented);
    public Task<IEnumerable<ViewerPerk>> GetActiveViewerPerksAsync(string username, string platform) => throw new NotImplementedException(NotImplemented);
    public Task<IEnumerable<ViewerPerk>> GetViewerPerksByTypeAsync(string username, string platform, string perkType) => throw new NotImplementedException(NotImplemented);
    public Task SaveViewerPerkAsync(ViewerPerk perk) => throw new NotImplementedException(NotImplemented);
    public Task ExpireViewerPerksAsync() => throw new NotImplementedException(NotImplemented);
    public Task<CurrencyBoost?> GetCurrencyBoostAsync(string id) => throw new NotImplementedException(NotImplemented);
    public Task<IEnumerable<CurrencyBoost>> GetActiveBoostsAsync(string? platform = null) => throw new NotImplementedException(NotImplemented);
    public Task<IEnumerable<CurrencyBoost>> GetBoostHistoryAsync(int limit = 50) => throw new NotImplementedException(NotImplemented);
    public Task SaveCurrencyBoostAsync(CurrencyBoost boost) => throw new NotImplementedException(NotImplemented);
    public Task ExpireBoostsAsync() => throw new NotImplementedException(NotImplemented);
    public Task<CurrencyAuction?> GetCurrencyAuctionAsync(string id) => throw new NotImplementedException(NotImplemented);
    public Task<CurrencyAuction?> GetActiveAuctionAsync() => throw new NotImplementedException(NotImplemented);
    public Task<IEnumerable<CurrencyAuction>> GetAuctionHistoryAsync(int limit = 20) => throw new NotImplementedException(NotImplemented);
    public Task SaveCurrencyAuctionAsync(CurrencyAuction auction) => throw new NotImplementedException(NotImplemented);
    public Task SaveCurrencyAuctionBidAsync(CurrencyAuctionBid bid) => throw new NotImplementedException(NotImplemented);
    public Task<IEnumerable<CurrencyAuctionBid>> GetAuctionBidsAsync(string auctionId) => throw new NotImplementedException(NotImplemented);
    public Task<IEnumerable<UserCurrency>> GetInactiveUsersForDecayAsync(DateTime cutoffDate) => throw new NotImplementedException(NotImplemented);
    public Task<Command?> GetCommandAsync(string id) => throw new NotImplementedException(NotImplemented);
    public Task<Command?> GetCommandByNameAsync(string name) => throw new NotImplementedException(NotImplemented);
    public Task<IEnumerable<Command>> GetCommandsAsync(bool? enabledOnly = null, bool? builtInOnly = null) => throw new NotImplementedException(NotImplemented);
    public Task<IEnumerable<Command>> GetCommandsByBotAsync(string botId) => throw new NotImplementedException(NotImplemented);
    public Task SaveCommandAsync(Command command) => throw new NotImplementedException(NotImplemented);
    public Task DeleteCommandAsync(string id) => throw new NotImplementedException(NotImplemented);
    public Task IncrementCommandUsageAsync(string id) => throw new NotImplementedException(NotImplemented);
    public Task<CommandCooldown?> GetCommandCooldownAsync(string commandId, string? username, string platform) => throw new NotImplementedException(NotImplemented);
    public Task SaveCommandCooldownAsync(CommandCooldown cooldown) => throw new NotImplementedException(NotImplemented);
    public Task DeleteExpiredCooldownsAsync() => throw new NotImplementedException(NotImplemented);
    public Task<IEnumerable<CommandTimer>> GetCommandTimersAsync() => throw new NotImplementedException(NotImplemented);
    public Task SaveCommandTimerAsync(CommandTimer timer) => throw new NotImplementedException(NotImplemented);
    public Task DeleteCommandTimerAsync(string id) => throw new NotImplementedException(NotImplemented);
    public Task<IEnumerable<AutoReplyRule>> GetAutoReplyRulesAsync() => throw new NotImplementedException(NotImplemented);
    public Task<IEnumerable<AutoReplyRule>> GetAutoReplyRulesByPlatformAndEventAsync(string platform, string eventType) => throw new NotImplementedException(NotImplemented);
    public Task<AutoReplyRule?> GetAutoReplyRuleAsync(int id) => throw new NotImplementedException(NotImplemented);
    public Task<int> SaveAutoReplyRuleAsync(AutoReplyRule rule) => throw new NotImplementedException(NotImplemented);
    public Task DeleteAutoReplyRuleAsync(int id) => throw new NotImplementedException(NotImplemented);
    public Task IncrementRuleTriggerCountAsync(int id) => throw new NotImplementedException(NotImplemented);
    public Task<IEnumerable<Giveaway>> GetGiveawaysAsync() => throw new NotImplementedException(NotImplemented);
    public Task DeleteGiveawayAsync(Guid id) => throw new NotImplementedException(NotImplemented);
    public Task<IEnumerable<GiveawayEntry>> GetGiveawayEntriesAsync(Guid giveawayId) => throw new NotImplementedException(NotImplemented);
    public Task<GiveawayEntry?> GetGiveawayEntryAsync(Guid giveawayId, string username, string platform) => throw new NotImplementedException(NotImplemented);
    public Task SaveGiveawayEntryAsync(GiveawayEntry entry) => throw new NotImplementedException(NotImplemented);
    public Task<int> GetGiveawayEntryCountAsync(Guid giveawayId) => throw new NotImplementedException(NotImplemented);
    public Task<int> GetGiveawayUniqueParticipantsAsync(Guid giveawayId) => throw new NotImplementedException(NotImplemented);
    public Task<IEnumerable<GiveawayEntry>> GetGiveawayEntriesForUserAsync(string username, string platform) => throw new NotImplementedException(NotImplemented);
    public Task<IEnumerable<Poll>> GetPollsAsync() => throw new NotImplementedException(NotImplemented);
    public Task DeletePollAsync(Guid id) => throw new NotImplementedException(NotImplemented);
    public Task<IEnumerable<PollVote>> GetPollVotesAsync(Guid pollId) => throw new NotImplementedException(NotImplemented);
    public Task<PollVote?> GetPollVoteAsync(Guid pollId, string username, string platform) => throw new NotImplementedException(NotImplemented);
    public Task SavePollVoteAsync(PollVote vote) => throw new NotImplementedException(NotImplemented);
    public Task<int> GetPollVoteCountAsync(Guid pollId) => throw new NotImplementedException(NotImplemented);
    public Task<UserWatchtime?> GetUserWatchtimeAsync(string username, string platform) => throw new NotImplementedException(NotImplemented);
    public Task<IEnumerable<UserWatchtime>> GetAllUserWatchtimeAsync() => throw new NotImplementedException(NotImplemented);
    public Task<IEnumerable<UserWatchtime>> GetTopWatchtimeUsersAsync(int limit = 10) => throw new NotImplementedException(NotImplemented);
    public Task<IEnumerable<UserWatchtime>> GetActiveViewersAsync() => throw new NotImplementedException(NotImplemented);
    public Task<int> GetTotalActiveViewersCountAsync() => throw new NotImplementedException(NotImplemented);
    public Task SaveUserWatchtimeAsync(UserWatchtime watchtime) => throw new NotImplementedException(NotImplemented);
    public Task DeactivateInactiveViewersAsync(DateTime inactiveSince) => throw new NotImplementedException(NotImplemented);
    public Task<int> DeactivateAllViewersAsync() => throw new NotImplementedException(NotImplemented);
    public Task ResetStreamSessionMinutesAsync() => throw new NotImplementedException(NotImplemented);
    public Task SavePlatformEarningsAsync(PlatformEarnings earnings) => throw new NotImplementedException(NotImplemented);
    public Task<IEnumerable<PlatformEarnings>> GetPlatformEarningsAsync(string viewerId) => throw new NotImplementedException(NotImplemented);
    public Task AddPlatformEarningsAsync(string viewerId, string platform, long earned, long spent) => throw new NotImplementedException(NotImplemented);
    public Task<Variable?> GetVariableAsync(string id) => throw new NotImplementedException(NotImplemented);
    public Task<Variable?> GetVariableByNameAsync(string name) => throw new NotImplementedException(NotImplemented);
    public Task<IEnumerable<Variable>> GetVariablesAsync() => throw new NotImplementedException(NotImplemented);
    public Task<IEnumerable<Variable>> GetVariablesByCategoryAsync(string category) => throw new NotImplementedException(NotImplemented);
    public Task<IEnumerable<string>> GetVariableCategoriesAsync() => throw new NotImplementedException(NotImplemented);
    public Task SaveVariableAsync(Variable variable) => throw new NotImplementedException(NotImplemented);
    public Task DeleteVariableAsync(string id) => throw new NotImplementedException(NotImplemented);
    public Task UpdateVariableValueAsync(string name, string? value) => throw new NotImplementedException(NotImplemented);
}

/// <summary>
/// ICurrencyService fake for the bank tests: balances + add/remove run
/// in-memory, GetSettingsAsync returns a settable CurrencySetting (the bank
/// settings are derived from it), and every add/remove mirrors a
/// CurrencyTransaction audit row into the backing Wave2TestDataService the
/// way the real CurrencyService does — the "BankWithdrawal" rows the
/// cooldown's cross-restart fallback reads.
/// </summary>
public class Wave2TestCurrencyService : ICurrencyService
{
    private readonly Wave2TestDataService _data;

    public Wave2TestCurrencyService(Wave2TestDataService data)
    {
        _data = data;
    }

    /// <summary>Currency settings the service reports; tests set the bank fields here.</summary>
    public CurrencySetting Settings { get; set; } = new() { BankEnabled = true };

    public Dictionary<string, int> Balances { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<(string Username, string Platform, int Amount, string TransactionType)> Adds { get; } = new();
    public List<(string Username, string Platform, int Amount, string TransactionType)> Removes { get; } = new();

    private const string NotImplemented = "Not needed for wave-2 regression tests.";

    private static string Key(string username, string platform) => $"{username}|{platform}";

    private int GetSync(string username, string platform)
        => Balances.TryGetValue(Key(username, platform), out var balance) ? balance : 0;

    public Task<int> GetBalanceAsync(string username, string platform)
        => Task.FromResult(GetSync(username, platform));

    public Task<CurrencySetting?> GetSettingsAsync() => Task.FromResult<CurrencySetting?>(Settings);

    public Task<bool> AddCurrencyAsync(string username, string platform, int amount, string reason, string transactionType = "Earn")
    {
        Adds.Add((username, platform, amount, transactionType));
        var before = GetSync(username, platform);
        Balances[Key(username, platform)] = before + amount;
        _data.Transactions.Add(new CurrencyTransaction
        {
            Username = username,
            Platform = platform,
            Amount = amount,
            TransactionType = transactionType,
            Reason = reason,
            BalanceBefore = before,
            BalanceAfter = before + amount,
            Timestamp = DateTime.UtcNow
        });
        return Task.FromResult(true);
    }

    public Task<bool> RemoveCurrencyAsync(string username, string platform, int amount, string reason, string transactionType = "Spend")
    {
        if (GetSync(username, platform) < amount)
        {
            return Task.FromResult(false);
        }

        Removes.Add((username, platform, amount, transactionType));
        var before = GetSync(username, platform);
        Balances[Key(username, platform)] = before - amount;
        _data.Transactions.Add(new CurrencyTransaction
        {
            Username = username,
            Platform = platform,
            Amount = -amount,
            TransactionType = transactionType,
            Reason = reason,
            BalanceBefore = before,
            BalanceAfter = before - amount,
            Timestamp = DateTime.UtcNow
        });
        return Task.FromResult(true);
    }

    public Task SaveSettingsAsync(CurrencySetting settings)
    {
        Settings = settings;
        return Task.CompletedTask;
    }

    public Task<IEnumerable<UserCurrency>> GetAllBalancesAsync(string username) => throw new NotImplementedException(NotImplemented);
    public Task<bool> TransferCurrencyAsync(string fromUsername, string fromPlatform, string toUsername, string toPlatform, int amount) => throw new NotImplementedException(NotImplemented);
    public Task<IEnumerable<UserCurrency>> GetLeaderboardAsync(int limit = 10, string? platform = null) => throw new NotImplementedException(NotImplemented);
    public Task<IEnumerable<CurrencyTransaction>> GetTransactionHistoryAsync(string username, string? platform = null, int limit = 50) => throw new NotImplementedException(NotImplemented);
    public Task AwardChatActivityAsync(string username, string platform, string? channelId = null) => throw new NotImplementedException(NotImplemented);
    public Task AwardWatchTimeAsync(string username, string platform) => throw new NotImplementedException(NotImplemented);
    public Task AwardEventBonusAsync(string username, string platform, string eventType, int multiplier = 1) => throw new NotImplementedException(NotImplemented);
    public Task<(bool Success, int Amount, int Streak, string Message)> ClaimDailyBonusAsync(string username, string platform) => throw new NotImplementedException(NotImplemented);
    public Task AwardFirstChatBonusAsync(string username, string platform) => throw new NotImplementedException(NotImplemented);
    public Task<double> GetEffectiveMultiplierAsync(string username, string platform, bool isSubscriber = false, bool isVip = false, bool isModerator = false) => throw new NotImplementedException(NotImplemented);
    public Task<int> GiveCurrencyToAllAsync(string platform, int amount, string reason) => throw new NotImplementedException(NotImplemented);
    public Task<EconomyStats> GetEconomyStatsAsync() => throw new NotImplementedException(NotImplemented);
    public Task ConsolidateViewerBalancesAsync(string viewerId) => throw new NotImplementedException(NotImplemented);
    public Task<IReadOnlyList<(string Platform, long Earned, long Spent)>> GetPerPlatformEarningsAsync(string viewerId) => throw new NotImplementedException(NotImplemented);
}
