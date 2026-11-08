using VyralBot.Data.Entities;

namespace VyralBot.Data;

/// <summary>
/// One row of the DiscordBots table: a single Discord bot managed by the
/// multi-bot DiscordBotManager (per-bot token, status and selected channel).
/// </summary>
public class DiscordBot
{
    /// <summary>Manager-assigned stable ID (GUID "N" string).</summary>
    public required string Id { get; set; }

    /// <summary>Display name of the bot.</summary>
    public required string Name { get; set; }

    /// <summary>Bot token. Plain text in memory; persisted DPAPI-wrapped by
    /// DapperDataService (SecretProtector "v1:" envelope, CurrentUser scope).
    /// Legacy plaintext rows are unwrapped on read as-is and re-wrapped on save.</summary>
    public string? Token { get; set; }

    /// <summary>Last known status: "Connected", "Disconnected" or "Error".</summary>
    public string? Status { get; set; }

    /// <summary>Per-bot snowflake channel used for outgoing messages.</summary>
    public string? SelectedChannelId { get; set; }

    /// <summary>Discord application/user snowflake ID, auto-detected from the gateway at connect time.</summary>
    public string? ApplicationId { get; set; }

    /// <summary>Discord username, auto-detected from the gateway at connect time.</summary>
    public string? BotUsername { get; set; }

    /// <summary>Avatar URL (PNG, 128px), auto-detected from the gateway at connect time.</summary>
    public string? AvatarUrl { get; set; }

    /// <summary>This bot handles account-link DMs (single-assignment role — only one row holds it).</summary>
    public bool RoleLinking { get; set; }

    /// <summary>This bot sends giveaway announcements (single-assignment role — only one row holds it).</summary>
    public bool RoleGiveaway { get; set; }

    /// <summary>Per-bot snowflake channel used for mod-action announcements.</summary>
    public string? ModChannelId { get; set; }

    /// <summary>Whether this bot routes mod actions to its mod channel (defaults to on).</summary>
    public bool ModChannelEnabled { get; set; } = true;

    /// <summary>Comma-separated snowflake channel IDs this bot listens in for mod commands.</summary>
    public string? AssignedChannelIds { get; set; }

    /// <summary>
    /// Start this bot automatically at app launch. The PRIMARY bot does not need
    /// this flag: when the Platform row's DiscordChatAutoStart is on, the startup
    /// path alone starts the bot matching the Platform row token (or the first
    /// saved bot). AutoStart is opt-in per extra bot; defaults to false and is
    /// never seeded to true on migration.
    /// </summary>
    public bool AutoStart { get; set; } = false;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// One row of the Workflows table: a visual automation workflow (Automation page /
/// VyralBot.Workflows engine). The trigger definition is stored as JSON in TriggerJson
/// and the node graph is stored as JSON in GraphJson (see VyralBot.Workflows.Persistence.WorkflowJson).
/// </summary>
public class WorkflowEntity
{
    /// <summary>Stable ID (GUID "N" string).</summary>
    public required string Id { get; set; }

    /// <summary>Display name of the workflow.</summary>
    public required string Name { get; set; }

    /// <summary>Optional user-facing description.</summary>
    public string? Description { get; set; }

    /// <summary>Whether the workflow's trigger is active.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>JSON-serialized WorkflowTrigger.</summary>
    public string? TriggerJson { get; set; }

    /// <summary>JSON-serialized node graph (list of WorkflowNode).</summary>
    public string? GraphJson { get; set; }

    /// <summary>Optional grouping category (e.g. "Moderation", "Fun").</summary>
    public string? Category { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// One row of the WorkflowExecutions table: the execution history of a single
/// workflow run (status, duration, visited node path and error, if any).
/// </summary>
public class WorkflowExecution
{
    /// <summary>Execution ID (GUID "N" string, one per run).</summary>
    public required string Id { get; set; }

    /// <summary>ID of the workflow that ran.</summary>
    public string? WorkflowId { get; set; }

    /// <summary>Snapshot of the workflow name at execution time.</summary>
    public string? WorkflowName { get; set; }

    /// <summary>Human-readable trigger summary, e.g. "command !hello" or "timer 15m".</summary>
    public string? TriggerSummary { get; set; }

    /// <summary>"Completed", "Failed" or "Cancelled".</summary>
    public string Status { get; set; } = "Failed";

    public DateTime StartedAt { get; set; } = DateTime.UtcNow;

    public DateTime? CompletedAt { get; set; }

    /// <summary>Total execution duration in milliseconds.</summary>
    public long DurationMs { get; set; }

    /// <summary>Visited node IDs/types in execution order.</summary>
    public string? NodePath { get; set; }

    /// <summary>Error message when Status = "Failed".</summary>
    public string? Error { get; set; }
}

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

    // Discord multi-bot support (DiscordBots table)
    Task<IEnumerable<DiscordBot>> GetDiscordBotsAsync();
    Task SaveDiscordBotAsync(DiscordBot bot);
    Task DeleteDiscordBotAsync(string id);

    // ===== W1T4 watchtime/points gating: per-bot assigned Discord channels =====
    // Owned by the watchtime-gating task. The AssignedChannelIds column itself
    // (entity property, save path and DDL) is maintained elsewhere; these are
    // the read/write members used by CurrencyService.AwardChatActivityAsync.
    /// <summary>Reads the comma-separated assigned Discord channel ids for one bot (null when unset).</summary>
    Task<string?> GetAssignedChannelIdsAsync(string botId);
    /// <summary>Overwrites the comma-separated assigned Discord channel ids for one bot.</summary>
    Task SetAssignedChannelIdsAsync(string botId, string channelIds);

    // Workflows (visual automation engine, VyralBot.Workflows)
    Task<IEnumerable<WorkflowEntity>> GetWorkflowsAsync();
    Task SaveWorkflowAsync(WorkflowEntity workflow);
    Task DeleteWorkflowAsync(string id);
    Task<IEnumerable<WorkflowExecution>> GetWorkflowExecutionsAsync(string? workflowId = null, int limit = 50);
    Task SaveWorkflowExecutionAsync(WorkflowExecution execution);

    // Configs
    Task<string?> GetConfigValueAsync(string key);
    Task SetConfigValueAsync(string key, string value, string? category = null);
    /// <summary>
    /// Deletes one Configs row by key (optionally restricted to a category, like
    /// <see cref="SetConfigValueAsync"/>'s write path). Returns true when a row
    /// was removed; false when the key did not exist.
    /// </summary>
    Task<bool> DeleteConfigValueAsync(string key, string? category = null);
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

    // Data management (Settings → Data Management)
    /// <summary>Row counts for the whitelisted user-data tables (Settings → Data Management inventory).</summary>
    Task<IReadOnlyDictionary<string, int>> GetDataInventoryAsync();
    /// <summary>Deletes every stored chat message for the current profile.</summary>
    Task DeleteAllChatMessagesAsync();
    /// <summary>Deletes every event log entry for the current profile.</summary>
    Task DeleteEventLogsAsync();
    /// <summary>Deletes workflow execution history (all, one workflow, and/or older than a cutoff).</summary>
    Task DeleteWorkflowExecutionsAsync(string? workflowId = null, DateTime? olderThan = null);
    /// <summary>Runs VACUUM to reclaim unused space in the profile database.</summary>
    Task<bool> CompactDatabaseAsync();

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
    Task<IEnumerable<EventStatistic>> GetEventStatisticsAsync(string? platform = null, string? eventType = null, int limit = 100, DateTime? startDate = null, DateTime? endDate = null);
    Task<IEnumerable<EventStatistic>> GetEventStatisticsByUsernameAsync(string username, string? platform = null);
    /// <summary>Deletes every event statistic row (the live event history) and returns the number of rows removed.</summary>
    Task<int> DeleteEventStatisticsAsync();

    // Currency System
    Task<CurrencySetting?> GetCurrencySettingsAsync();
    Task SaveCurrencySettingsAsync(CurrencySetting settings);

    Task<UserCurrency?> GetUserCurrencyAsync(string username, string platform);
    Task<IEnumerable<UserCurrency>> GetUserCurrenciesAsync(string username); // All platforms for user
    Task<IEnumerable<UserCurrency>> GetCurrencyLeaderboardAsync(int limit = 10, string? platform = null);
    // Stable-key paging (Username+Platform order, balances may mutate mid-walk) for
    // full-table walks like GiveCurrencyToAll — OFFSET paging over the leaderboard's
    // Balance order would skip/duplicate rows as awards change the sort key.
    Task<IEnumerable<UserCurrency>> GetCurrencyUsersPagedAsync(int limit, int offset, string? platform = null);
    Task SaveUserCurrencyAsync(UserCurrency currency);

    Task SaveCurrencyTransactionAsync(CurrencyTransaction transaction);
    Task<IEnumerable<CurrencyTransaction>> GetCurrencyTransactionsAsync(string? username = null, string? platform = null, int limit = 100);
    // SQL-side aggregation (SUM/COUNT) for economy stats — never loads row lists.
    Task<(long UserCount, long TotalBalance)> GetCurrencyEconomyTotalsAsync();
    Task<(long Earned, long Spent)> GetCurrencyTransactionTotalsSinceAsync(DateTime since);

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

    // Link Codes
    Task<LinkCode?> GetLinkCodeAsync(string code);

    // ===== Discord DM history (unlimited persistence for the chat inbox) =====
    Task SaveDiscordDmMessageAsync(DiscordDmMessage message);
    Task<IEnumerable<DiscordDmMessage>> GetDiscordDmHistoryAsync(string username);
    Task<IEnumerable<(string Username, string? BotUsername, DateTime LastAt, int Total)>> GetDiscordDmConversationsAsync();
    Task<IEnumerable<DiscordDmMessage>> SearchDiscordDmMessagesAsync(string query); // Content/Username LIKE search, newest first, capped at 100 rows
    Task ClearDiscordDmHistoryAsync(string? username);
    Task SaveLinkCodeAsync(LinkCode code);
    Task InvalidateLinkCodesForViewerAsync(string viewerId);      // marks unredeemed codes expired
    Task<IEnumerable<LinkCode>> GetActiveLinkCodesAsync();        // RedeemedAt IS NULL AND ExpiresAt > now
    Task<IEnumerable<LinkCode>> GetRecentLinkCodesAsync(int limit = 50); // ORDER BY CreatedAt DESC
    Task<IEnumerable<LinkCode>> GetUnredeemedLinkCodesByViewerAsync(string viewerId); // live (unredeemed, unexpired) codes for one viewer — serves IX_LinkCodes_ViewerId
    Task DeleteLinkCodeAsync(string id);
    Task<IEnumerable<LinkedAccount>> GetAllLinkedAccountsAsync();
    Task UpdateLinkedAccountViewerAsync(string accountId, string newViewerId);
    Task DeleteLinkedAccountsByViewerIdAsync(string viewerId);

    // Viewer Platform Status
    Task<ViewerPlatformStatus?> GetViewerPlatformStatusAsync(string username, string platform);
    Task SaveViewerPlatformStatusAsync(ViewerPlatformStatus status);
    Task<IEnumerable<ViewerPlatformStatus>> GetViewerStatusesByViewerIdAsync(string viewerId);

    // Viewer Preferences
    Task<ViewerPreferences?> GetViewerPreferencesAsync(string viewerId);
    Task SaveViewerPreferencesAsync(ViewerPreferences preferences);

    // Social Goals
    Task<IEnumerable<SocialGoal>> GetSocialGoalsAsync();          // ORDER BY SortOrder
    Task<SocialGoal?> GetSocialGoalAsync(Guid id);
    Task SaveSocialGoalAsync(SocialGoal goal);                    // exists-check upsert
    Task DeleteSocialGoalAsync(Guid id);
    Task<ViewerGoalCompletion?> GetViewerGoalCompletionAsync(Guid goalId, string viewerId);
    Task SaveViewerGoalCompletionAsync(ViewerGoalCompletion completion);
    Task<int> GetGoalCompletionCountAsync(Guid goalId);
    Task<IEnumerable<ViewerGoalCompletion>> GetViewerGoalCompletionsByViewerIdAsync(string viewerId);

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
    Task<IEnumerable<Command>> GetCommandsByBotAsync(string botId);
    Task SaveCommandAsync(Command command);
    Task DeleteCommandAsync(string id);
    Task IncrementCommandUsageAsync(string id);

    // Command Cooldowns
    Task<CommandCooldown?> GetCommandCooldownAsync(string commandId, string? username, string platform);
    Task SaveCommandCooldownAsync(CommandCooldown cooldown);
    Task DeleteExpiredCooldownsAsync();

    // Command Timers (scheduled command firing)
    Task<IEnumerable<CommandTimer>> GetCommandTimersAsync();
    Task SaveCommandTimerAsync(CommandTimer timer);
    Task DeleteCommandTimerAsync(string id);

    // Quotes (chat quote database, "!quote" family — Commands page QUOTES tab)
    /// <summary>Inserts a quote and returns it with the autoassigned <see cref="Quote.Number"/> set.</summary>
    Task<Quote> AddQuoteAsync(Quote quote);
    /// <summary>Fetches one quote by its stable per-profile number.</summary>
    Task<Quote?> GetQuoteByNumberAsync(int number);
    /// <summary>Uniform random quote (SQL ORDER BY RANDOM() LIMIT 1).</summary>
    Task<Quote?> GetRandomQuoteAsync();
    /// <summary>Case-insensitive substring search on Text/AddedBy, best (lowest) numbers first.</summary>
    Task<IEnumerable<Quote>> SearchQuotesAsync(string searchText, int limit = 20);
    /// <summary>Every quote, lowest number first (the QUOTES tab grid).</summary>
    Task<IEnumerable<Quote>> GetQuotesAsync();
    /// <summary>Deletes one quote by number; true when a row was removed.</summary>
    Task<bool> DeleteQuoteAsync(int number);
    /// <summary>Total quote count (header + !quotecount).</summary>
    Task<int> GetQuoteCountAsync();

    // Song requests (chat-driven song queue, "!sr" family)
    /// <summary>Inserts a song request and returns it with the autoassigned <see cref="SongRequest.Id"/> set.</summary>
    Task<SongRequest> AddSongRequestAsync(SongRequest request);
    /// <summary>Queued requests in queue order (Id ASC) — the "!sr" queue.</summary>
    Task<IEnumerable<SongRequest>> GetSongQueueAsync();
    /// <summary>The oldest request with Status='Playing', or null when nothing is playing.</summary>
    Task<SongRequest?> GetCurrentSongRequestAsync();
    /// <summary>Sets one request's status (Queued/Playing/Played/Skipped); true when a row changed.</summary>
    Task<bool> SetSongRequestStatusAsync(int id, string status);
    /// <summary>Total queued count — enforces the total queue cap.</summary>
    Task<int> GetQueuedSongRequestCountAsync();
    /// <summary>Queued count for one user — enforces the per-user queue cap.</summary>
    Task<int> GetQueuedSongRequestCountByUserAsync(string requestedBy);
    /// <summary>Deletes every song request row (mod "!srclear"); returns rows removed.</summary>
    Task<int> ClearSongRequestsAsync();
    /// <summary>Updates Title/DurationSeconds for all rows with a YoutubeId (player callback); returns rows updated.</summary>
    Task<int> UpdateSongRequestTitleAsync(string youtubeId, string title, int? durationSeconds);

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
    Task<IEnumerable<GiveawayEntry>> GetGiveawayEntriesForUserAsync(string username, string platform);

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
    /// <summary>Clears every IsActive flag (app startup) so viewers persisted as
    /// active by the previous run cannot earn watchtime/points until they actually
    /// re-appear in chat. Returns the number of rows deactivated.</summary>
    Task<int> DeactivateAllViewersAsync();
    Task ResetStreamSessionMinutesAsync();

    // Mod system + platform earnings
    Task SaveModActionAsync(ModAction action);
    Task<IEnumerable<ModAction>> GetModActionsAsync(int limit = 200);                 // ORDER BY Timestamp DESC
    Task<IEnumerable<ModAction>> GetModActionsForViewerAsync(string username, string userId, int limit = 100); // TargetUsername COLLATE NOCASE OR TargetUserId
    Task SaveModRoleAssignmentAsync(ModRoleAssignment assignment);                    // exists-check upsert on DiscordUserId
    Task DeleteModRoleAssignmentAsync(string discordUserId);
    Task<IEnumerable<ModRoleAssignment>> GetModRoleAssignmentsAsync();
    Task<ModRoleAssignment?> GetModRoleAssignmentAsync(string discordUserId);
    Task SavePlatformEarningsAsync(PlatformEarnings earnings);                        // exists-check upsert on (ViewerId, Platform)
    Task<IEnumerable<PlatformEarnings>> GetPlatformEarningsAsync(string viewerId);    // one row per platform
    Task AddPlatformEarningsAsync(string viewerId, string platform, long earned, long spent); // atomic read-add-write upsert

    // ===== Discord multi-bot: monitored channels (UNIQUE(BotId, ChannelId)) =====
    /// <summary>Channels a bot monitors, tagged with purpose CSV ("Moderation,Support,Logging").</summary>
    Task<List<ConnectedChannel>> GetConnectedChannelsAsync(string botId);
    Task<ConnectedChannel> UpsertConnectedChannelAsync(ConnectedChannel channel);
    Task DeleteConnectedChannelAsync(string botId, string channelId);
    Task<int> CountConnectedChannelsAsync(string botId);

    // ===== Discord multi-bot: role → mod-tier mapping (UNIQUE(BotId, DiscordRoleId)) =====
    Task<List<DiscordRoleTierAssignment>> GetRoleTierAssignmentsAsync(string botId);
    Task UpsertRoleTierAssignmentAsync(DiscordRoleTierAssignment assignment);
    Task DeleteRoleTierAssignmentAsync(string botId, string discordRoleId);

    // ===== Imported OBS scene collections (RawJsonBytes is the verbatim canonical store) =====
    Task<List<ObsSceneCollection>> GetObsSceneCollectionsAsync();
    Task<ObsSceneCollection?> GetObsSceneCollectionAsync(int id);
    Task<ObsSceneCollection> SaveObsSceneCollectionAsync(ObsSceneCollection collection); // insert when Id==0, else update
    Task DeleteObsSceneCollectionAsync(int id);

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
