using VyralBot.Core.Services.Variables;
using VyralBot.Data;
using VyralBot.Data.Entities;
using VyralBot.Integrations.Discord;
using VyralBot.Integrations.Kick;
using VyralBot.Integrations.Twitch;
using VyralBot.Integrations.YouTube;

namespace VyralBot.UI.Tests;

// ======================================================================
// Shared test doubles for the WAVE-8 integration tests (cross-subsystem
// flows the unit waves could not cover: chat→workflow command pipeline,
// auto-mod engine→action service, command-timer chat gating, quotes,
// SecretProtector at-rest round-trip).
//
// The IDataService skeleton is a deliberate sibling of Wave2TestDataService
// (same throw-everything-else contract) with REAL in-memory behavior for the
// members the wave-8 flows touch: configs, workflows + executions, command
// timers, commands, quotes, platforms (with the DapperDataService
// SecretProtector at-rest convention) and the mod-action audit log.
//
// The platform-service fakes implement the small surfaces the workflows /
// timer pipelines route messages through (ITwitchService, IKickService,
// IYouTubeService) plus the two big Discord seams WorkflowMessageSender's
// constructor demands (IDiscordBotManager, IDiscordService) — every member
// the flows never touch throws, mirroring the Wave2 fake style.
// ======================================================================

/// <summary>
/// In-memory IDataService: real behavior for the config table, workflows +
/// execution history, command timers, commands, quotes, platforms (secrets
/// stored wrapped exactly like DapperDataService) and mod actions; every
/// other member throws (a wave-8 test should never depend on it).
/// </summary>
public class IntegrationWave8DataService : IDataService
{
    private const string NotImplemented = "Not needed for wave-8 integration tests.";

    // ----- Configs (real behavior: key/value with optional category) -----

    public Dictionary<string, string> ConfigValues { get; } = new(StringComparer.Ordinal);

    public Task<string?> GetConfigValueAsync(string key)
        => Task.FromResult(ConfigValues.TryGetValue(key, out var value) ? value : null);

    public Task SetConfigValueAsync(string key, string value, string? category = null)
    {
        ConfigValues[key] = value;
        return Task.CompletedTask;
    }

    public Task<bool> DeleteConfigValueAsync(string key, string? category = null)
        => Task.FromResult(ConfigValues.Remove(key));

    // ----- Workflows + execution history (real behavior) -----

    /// <summary>Workflow rows in save order (tests seed via WorkflowJson.ToRow).</summary>
    public List<WorkflowEntity> WorkflowRows { get; } = new();

    /// <summary>Execution-history rows the WorkflowRuntime persisted, in save order.</summary>
    public List<WorkflowExecution> WorkflowExecutions { get; } = new();

    public Task<IEnumerable<WorkflowEntity>> GetWorkflowsAsync()
        => Task.FromResult<IEnumerable<WorkflowEntity>>(WorkflowRows.ToList());

    public Task SaveWorkflowAsync(WorkflowEntity workflow)
    {
        var existing = WorkflowRows.FirstOrDefault(r => r.Id == workflow.Id);
        if (existing != null)
        {
            WorkflowRows.Remove(existing);
        }
        WorkflowRows.Add(workflow);
        return Task.CompletedTask;
    }

    public Task DeleteWorkflowAsync(string id)
    {
        WorkflowRows.RemoveAll(r => r.Id == id);
        return Task.CompletedTask;
    }

    public Task<IEnumerable<WorkflowExecution>> GetWorkflowExecutionsAsync(string? workflowId = null, int limit = 50)
        => Task.FromResult<IEnumerable<WorkflowExecution>>(
            WorkflowExecutions
                .Where(e => workflowId == null || e.WorkflowId == workflowId)
                .Reverse()
                .Take(limit)
                .ToList());

    public Task SaveWorkflowExecutionAsync(WorkflowExecution execution)
    {
        WorkflowExecutions.Add(execution);
        return Task.CompletedTask;
    }

    public Task DeleteWorkflowExecutionsAsync(string? workflowId = null, DateTime? olderThan = null)
    {
        WorkflowExecutions.RemoveAll(e =>
            (workflowId == null || e.WorkflowId == workflowId)
            && (olderThan == null || e.StartedAt < olderThan.Value));
        return Task.CompletedTask;
    }

    // ----- Command timers (real behavior) -----

    /// <summary>CommandTimer rows keyed by Id; the tick mutates LastRunAt on these instances.</summary>
    public Dictionary<string, CommandTimer> CommandTimers { get; } = new(StringComparer.Ordinal);

    public Task<IEnumerable<CommandTimer>> GetCommandTimersAsync()
        => Task.FromResult<IEnumerable<CommandTimer>>(CommandTimers.Values.ToList());

    public Task SaveCommandTimerAsync(CommandTimer timer)
    {
        CommandTimers[timer.Id] = timer;
        return Task.CompletedTask;
    }

    public Task DeleteCommandTimerAsync(string id)
    {
        CommandTimers.Remove(id);
        return Task.CompletedTask;
    }

    // ----- Commands (real behavior: what FireTimerAsync resolves by name) -----

    /// <summary>Command rows keyed by name (ordinal-ignore-case, like the command lookups).</summary>
    public Dictionary<string, Command> CommandsByName { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Task<Command?> GetCommandAsync(string id)
        => Task.FromResult(CommandsByName.Values.FirstOrDefault(c => c.Id == id));

    public Task<Command?> GetCommandByNameAsync(string name)
        => Task.FromResult(CommandsByName.TryGetValue(name, out var command) ? command : null);

    public Task<IEnumerable<Command>> GetCommandsAsync(bool? enabledOnly = null, bool? builtInOnly = null)
        => Task.FromResult<IEnumerable<Command>>(
            CommandsByName.Values
                .Where(c => enabledOnly != true || c.IsEnabled)
                .Where(c => builtInOnly != true || c.IsBuiltIn)
                .ToList());

    public Task<IEnumerable<Command>> GetCommandsByBotAsync(string botId) => throw new NotImplementedException(NotImplemented);

    public Task SaveCommandAsync(Command command)
    {
        CommandsByName[command.Name] = command;
        return Task.CompletedTask;
    }

    public Task DeleteCommandAsync(string id)
    {
        var match = CommandsByName.Values.FirstOrDefault(c => c.Id == id);
        if (match != null)
        {
            CommandsByName.Remove(match.Name);
        }
        return Task.CompletedTask;
    }

    public Task IncrementCommandUsageAsync(string id)
    {
        var command = CommandsByName.Values.FirstOrDefault(c => c.Id == id);
        if (command != null)
        {
            command.UsageCount++;
            command.LastUsedAt = DateTime.UtcNow;
        }
        return Task.CompletedTask;
    }

    public Task<CommandCooldown?> GetCommandCooldownAsync(string commandId, string? username, string platform) => throw new NotImplementedException(NotImplemented);
    public Task SaveCommandCooldownAsync(CommandCooldown cooldown) => throw new NotImplementedException(NotImplemented);
    public Task DeleteExpiredCooldownsAsync() => throw new NotImplementedException(NotImplemented);

    // ----- Quotes (real behavior: sequential numbers like the Dapper layer) -----

    /// <summary>Quote rows in insertion order; Number is assigned on AddQuoteAsync.</summary>
    public List<Quote> Quotes { get; } = new();

    public Task<Quote> AddQuoteAsync(Quote quote)
    {
        var stored = new Quote
        {
            Number = Quotes.Count == 0 ? 1 : Quotes.Max(q => q.Number) + 1,
            Text = quote.Text,
            AddedBy = quote.AddedBy,
            Platform = quote.Platform,
            CreatedAt = quote.CreatedAt
        };
        Quotes.Add(stored);
        return Task.FromResult(stored);
    }

    public Task<Quote?> GetQuoteByNumberAsync(int number)
        => Task.FromResult(Quotes.FirstOrDefault(q => q.Number == number));

    public Task<Quote?> GetRandomQuoteAsync()
        => Task.FromResult(Quotes.Count == 0 ? null : Quotes[Random.Shared.Next(Quotes.Count)]);

    public Task<IEnumerable<Quote>> SearchQuotesAsync(string searchText, int limit = 20)
        => Task.FromResult<IEnumerable<Quote>>(
            Quotes
                .Where(q => q.Text.Contains(searchText, StringComparison.OrdinalIgnoreCase)
                            || q.AddedBy.Contains(searchText, StringComparison.OrdinalIgnoreCase))
                .Take(limit)
                .ToList());

    public Task<IEnumerable<Quote>> GetQuotesAsync()
        => Task.FromResult<IEnumerable<Quote>>(Quotes.ToList());

    public Task<bool> DeleteQuoteAsync(int number)
        => Task.FromResult(Quotes.RemoveAll(q => q.Number == number) > 0);

    public Task<int> GetQuoteCountAsync() => Task.FromResult(Quotes.Count);

    // ----- Song requests (chat-driven song queue; not used by wave 8 tests) -----
    public Task<VyralBot.Data.Entities.SongRequest> AddSongRequestAsync(VyralBot.Data.Entities.SongRequest request) => throw new NotImplementedException(NotImplemented);
    public Task<IEnumerable<VyralBot.Data.Entities.SongRequest>> GetSongQueueAsync() => throw new NotImplementedException(NotImplemented);
    public Task<VyralBot.Data.Entities.SongRequest?> GetCurrentSongRequestAsync() => throw new NotImplementedException(NotImplemented);
    public Task<bool> SetSongRequestStatusAsync(int id, string status) => throw new NotImplementedException(NotImplemented);
    public Task<int> GetQueuedSongRequestCountAsync() => throw new NotImplementedException(NotImplemented);
    public Task<int> GetQueuedSongRequestCountByUserAsync(string requestedBy) => throw new NotImplementedException(NotImplemented);
    public Task<int> ClearSongRequestsAsync() => throw new NotImplementedException(NotImplemented);
    public Task<int> UpdateSongRequestTitleAsync(string youtubeId, string title, int? durationSeconds) => throw new NotImplementedException(NotImplemented);

    // ----- Platforms (real behavior: the DapperDataService at-rest secret convention) -----

    /// <summary>
    /// Rows AS STORED (ClientSecret already DPAPI-wrapped, like the SQLite
    /// table). Tests assert the envelope directly against these instances.
    /// </summary>
    public Dictionary<Guid, Platform> PlatformRowsAtRest { get; } = new();

    public Task<IEnumerable<Platform>> GetPlatformsAsync()
        => Task.FromResult<IEnumerable<Platform>>(
            PlatformRowsAtRest.Values
                .OrderBy(p => p.Name, StringComparer.Ordinal)
                .Select(UnprotectCopy)
                .ToList());

    public Task<Platform?> GetPlatformByNameAsync(string name)
    {
        var row = PlatformRowsAtRest.Values.FirstOrDefault(p =>
            string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(row == null ? null : UnprotectCopy(row));
    }

    public Task<Platform?> GetPlatformByIdAsync(Guid id)
        => Task.FromResult(PlatformRowsAtRest.TryGetValue(id, out var row) ? UnprotectCopy(row) : null);

    public Task SavePlatformAsync(Platform platform)
    {
        // Mirrors DapperDataService.SavePlatformAsync: the ROW carries the
        // "v1:" DPAPI envelope while the caller's in-memory entity keeps the
        // plaintext secret (its finally-restore contract).
        platform.UpdatedAt = DateTime.UtcNow;
        var row = ClonePlatform(platform);
        row.ClientSecret = SecretProtector.Protect(row.ClientSecret);
        PlatformRowsAtRest[platform.Id] = row;
        return Task.CompletedTask;
    }

    public Task DeletePlatformAsync(Guid id)
    {
        PlatformRowsAtRest.Remove(id);
        return Task.CompletedTask;
    }

    /// <summary>Fresh entity with the stored secret unwrapped — what every read path hands callers.</summary>
    private static Platform UnprotectCopy(Platform row)
    {
        var copy = ClonePlatform(row);
        copy.ClientSecret = SecretProtector.Unprotect(copy.ClientSecret);
        return copy;
    }

    private static Platform ClonePlatform(Platform platform) => new()
    {
        Id = platform.Id,
        Name = platform.Name,
        Enabled = platform.Enabled,
        AccessToken = platform.AccessToken,
        RefreshToken = platform.RefreshToken,
        TokenExpiresAt = platform.TokenExpiresAt,
        Username = platform.Username,
        UserId = platform.UserId,
        ChannelId = platform.ChannelId,
        ChatroomId = platform.ChatroomId,
        ClientId = platform.ClientId,
        ClientSecret = platform.ClientSecret,
        Status = platform.Status,
        LastError = platform.LastError,
        ConnectedAt = platform.ConnectedAt,
        KickChatEnabled = platform.KickChatEnabled,
        KickChatAutoStart = platform.KickChatAutoStart,
        TwitchChatEnabled = platform.TwitchChatEnabled,
        TwitchChatAutoStart = platform.TwitchChatAutoStart,
        YoutubeChatEnabled = platform.YoutubeChatEnabled,
        YoutubeChatAutoStart = platform.YoutubeChatAutoStart,
        TiktokChatEnabled = platform.TiktokChatEnabled,
        TiktokChatAutoStart = platform.TiktokChatAutoStart,
        DiscordChatEnabled = platform.DiscordChatEnabled,
        DiscordChatAutoStart = platform.DiscordChatAutoStart,
        FacebookChatEnabled = platform.FacebookChatEnabled,
        FacebookChatAutoStart = platform.FacebookChatAutoStart,
        RumbleChatEnabled = platform.RumbleChatEnabled,
        RumbleChatAutoStart = platform.RumbleChatAutoStart,
        CreatedAt = platform.CreatedAt,
        UpdatedAt = platform.UpdatedAt
    };

    // ----- Mod system (real behavior: the auto-mod audit log) -----

    public List<ModAction> ModActions { get; } = new();

    public Task SaveModActionAsync(ModAction action)
    {
        ModActions.Add(action);
        return Task.CompletedTask;
    }

    public Task<IEnumerable<ModAction>> GetModActionsAsync(int limit = 200)
        => Task.FromResult<IEnumerable<ModAction>>(ModActions.Take(limit).ToList());

    public Task<IEnumerable<ModAction>> GetModActionsForViewerAsync(string username, string userId, int limit = 100)
        => Task.FromResult<IEnumerable<ModAction>>(ModActions.Take(limit).ToList());

    public Dictionary<string, ModRoleAssignment> ModRoleAssignments { get; } = new(StringComparer.Ordinal);

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

    // ----- Everything else (out of scope for wave 8) -----

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
    public Task<Giveaway?> GetGiveawayAsync(Guid id) => throw new NotImplementedException(NotImplemented);
    public Task<Giveaway?> GetActiveGiveawayAsync() => throw new NotImplementedException(NotImplemented);
    public Task SaveGiveawayAsync(Giveaway giveaway) => throw new NotImplementedException(NotImplemented);
    public Task<Poll?> GetPollAsync(Guid id) => throw new NotImplementedException(NotImplemented);
    public Task<Poll?> GetActivePollAsync() => throw new NotImplementedException(NotImplemented);
    public Task SavePollAsync(Poll poll) => throw new NotImplementedException(NotImplemented);
    public Task<Dictionary<int, int>> GetPollVoteResultsAsync(Guid pollId) => throw new NotImplementedException(NotImplemented);
    public Task<IEnumerable<DiscordBot>> GetDiscordBotsAsync() => throw new NotImplementedException(NotImplemented);
    public Task SaveDiscordBotAsync(DiscordBot bot) => throw new NotImplementedException(NotImplemented);
    public Task DeleteDiscordBotAsync(string id) => throw new NotImplementedException(NotImplemented);
    public Task<string?> GetAssignedChannelIdsAsync(string botId) => throw new NotImplementedException(NotImplemented);
    public Task SetAssignedChannelIdsAsync(string botId, string channelIds) => throw new NotImplementedException(NotImplemented);
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
    public Task SaveCurrencyTransactionAsync(CurrencyTransaction transaction) => throw new NotImplementedException(NotImplemented);
    public Task<IEnumerable<CurrencyTransaction>> GetCurrencyTransactionsAsync(string? username = null, string? platform = null, int limit = 100) => throw new NotImplementedException(NotImplemented);
    public Task<UserBankAccount?> GetUserBankAccountAsync(string username, string platform) => throw new NotImplementedException(NotImplemented);
    public Task<IEnumerable<UserBankAccount>> GetAllBankAccountsAsync() => throw new NotImplementedException(NotImplemented);
    public Task SaveUserBankAccountAsync(UserBankAccount account) => throw new NotImplementedException(NotImplemented);
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
    public Task ClearDiscordDmHistoryAsync(string? username = null) => throw new NotImplementedException(NotImplemented);
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
/// Recording ITwitchService: connected to "vyralhq"; every SendMessageAsync
/// lands in <see cref="SentMessages"/> and completes the current
/// <see cref="NextSend"/> gate so tests can await the async workflow run
/// without sleeps. Unused management actions throw.
/// </summary>
public sealed class IntegrationWave8TwitchService : ITwitchService
{
    private const string NotImplemented = "Not needed for wave-8 integration tests.";

    public bool IsConnected { get; set; } = true;
    public string? Channel { get; set; } = "vyralhq";
    public string? BotUsername { get; set; } = "vyralbot";

    public List<string> SentMessages { get; } = new();
    private TaskCompletionSource<string> _nextSend =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Completes with the next message handed to SendMessageAsync.</summary>
    public Task<string> NextSend => _nextSend.Task;

    public Task SendMessageAsync(string message)
    {
        SentMessages.Add(message);
        _nextSend.TrySetResult(message);
        return Task.CompletedTask;
    }

    public Task ConnectAsync(string username, string accessToken, string channel) => throw new NotImplementedException(NotImplemented);
    public Task ConnectAsync(string username, string accessToken, string? refreshToken, string channel) => throw new NotImplementedException(NotImplemented);
    public Task DisconnectAsync() => throw new NotImplementedException(NotImplemented);
    public Task<TwitchActionResponse> EditChannelInfoAsync(string? title, string? category) => throw new NotImplementedException(NotImplemented);
    public Task<TwitchActionResponse> CreateStreamMarkerAsync(string? description) => throw new NotImplementedException(NotImplemented);
    public Task<TwitchActionResponse> CreateClipAsync() => throw new NotImplementedException(NotImplemented);
    public Task<TwitchActionResponse> RaidChannelAsync(string targetChannel) => throw new NotImplementedException(NotImplemented);
    public Task<TwitchActionResponse> StopRaidAsync() => throw new NotImplementedException(NotImplemented);
    public Task<TwitchActionResponse> ClearChatAsync() => throw new NotImplementedException(NotImplemented);
    public Task<bool> TimeoutUserAsync(string username, TimeSpan duration, string reason) => throw new NotImplementedException(NotImplemented);
    public Task<bool> BanUserAsync(string username, string reason) => throw new NotImplementedException(NotImplemented);
    public Task<bool> DeleteMessageAsync(string messageId) => throw new NotImplementedException(NotImplemented);
    public Task<TwitchActionResponse> SetChatModeAsync(bool? emoteOnly, bool? followerOnly) => throw new NotImplementedException(NotImplemented);
    public Task<TwitchActionResponse> SetShieldModeAsync(bool enabled) => throw new NotImplementedException(NotImplemented);
    public Task<TwitchChatModeState?> GetChatModeStateAsync() => throw new NotImplementedException(NotImplemented);

    // Engine-facing Helix lookups/actions (interface wave 14): no wave-8 flow
    // touches them, and their contract is "never throw", so they return
    // failure defaults instead of NotImplementedException.
    public Task<string?> CreateClipUrlAsync() => Task.FromResult<string?>(null);
    public Task<bool> StartRaidAsync(string targetLogin) => Task.FromResult(false);
    public Task<bool> CancelRaidAsync() => Task.FromResult(false);
    public Task<bool> UpdateChannelInfoAsync(string? title, string? gameId) => Task.FromResult(false);
    public Task<(string Title, string GameName)?> GetChannelInfoAsync() => Task.FromResult<(string Title, string GameName)?>(null);
    public Task<string?> GetTopGamesAsync(string? query = null, int limit = 20) => Task.FromResult<string?>(null);
    public Task<bool> StartCommercialAsync(int seconds) => Task.FromResult(false);
    public Task<TimeSpan?> GetStreamUptimeAsync() => Task.FromResult<TimeSpan?>(null);
    public Task<TimeSpan?> GetFollowAgeAsync(string login) => Task.FromResult<TimeSpan?>(null);
}

/// <summary>
/// Recording IKickService: connected (so a platform-filter leak would be
/// observable), every send recorded. Unused members throw.
/// </summary>
public sealed class IntegrationWave8KickService : IKickService
{
    private const string NotImplemented = "Not needed for wave-8 integration tests.";

    public bool IsConnected { get; set; } = true;
    public string? Channel { get; set; } = "vyralhq";
    public string? BotUsername { get; set; } = "vyralbot";

    public List<string> SentMessages { get; } = new();

    private TaskCompletionSource<string> _nextSend =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Completes with the next message handed to SendMessageAsync.</summary>
    public Task<string> NextSend => _nextSend.Task;

    public Task SendMessageAsync(string message)
    {
        SentMessages.Add(message);
        _nextSend.TrySetResult(message);
        return Task.CompletedTask;
    }

    public Task ConnectAsync(string username, string accessToken, string channel, int broadcasterUserId) => throw new NotImplementedException(NotImplemented);
    public Task ConnectAsync(string username, string accessToken, string? refreshToken, string channel, int broadcasterUserId) => throw new NotImplementedException(NotImplemented);
    public Task DisconnectAsync() => throw new NotImplementedException(NotImplemented);
    public Task<bool> TimeoutUserAsync(string userId, TimeSpan duration, string reason) => throw new NotImplementedException(NotImplemented);
    public Task<bool> BanUserAsync(string userId, string reason) => throw new NotImplementedException(NotImplemented);
    public Task<bool> DeleteMessageAsync(string messageId) => throw new NotImplementedException(NotImplemented);
}

/// <summary>Minimal IYouTubeService: disconnected, no channel; only satisfies the timer service's constructor.</summary>
public sealed class IntegrationWave8YouTubeService : IYouTubeService
{
    private const string NotImplemented = "Not needed for wave-8 integration tests.";

    public bool IsConnected => false;
    public bool RequiresReauth => false;
    public string? ReauthReason => null;
    public string LiveChatState => "NotConnected";
    public string? Channel => null;
    public string? BotUsername => null;

    public Task SendMessageAsync(string message) => throw new NotImplementedException(NotImplemented);
    public Task ConnectAsync(string username, string accessToken, string channel) => throw new NotImplementedException(NotImplemented);
    public Task ConnectAsync(string username, string accessToken, string? refreshToken, string channel) => throw new NotImplementedException(NotImplemented);
    public Task DisconnectAsync() => throw new NotImplementedException(NotImplemented);
    public Task<YouTubeActionResponse> UpdateStreamInfoAsync(string? title, string? description) => throw new NotImplementedException(NotImplemented);
    public Task<bool> DeleteMessageAsync(string messageId) => throw new NotImplementedException(NotImplemented);
}

/// <summary>
/// Minimal IDiscordBotManager: nothing connected (workflow slash sync skips,
/// Discord sends fall through to their no-target path). Every other member
/// throws. Exists because WorkflowMessageSender's constructor demands the
/// seam even for Twitch/Kick-only flows.
/// </summary>
public sealed class IntegrationWave8DiscordBotManager : IDiscordBotManager
{
    private const string NotImplemented = "Not needed for wave-8 integration tests.";

#pragma warning disable CS0067 // never raised by the fake
    public event Action? BotsChanged;
#pragma warning restore CS0067

    public IReadOnlyList<IDiscordService> Connected => Array.Empty<IDiscordService>();
    public IDiscordService? Primary => null;
    public string? PlatformAdoptedBotId => null;

    public IDiscordService? Get(string botId) => null;

    public Task<string?> ResolveBotIdForRoleAsync(string role) => Task.FromResult<string?>(null);

    public Task<IReadOnlyList<DiscordBotConfig>> LoadBotsAsync() => throw new NotImplementedException(NotImplemented);
    public Task<bool> AddBotAsync(DiscordBotConfig bot) => throw new NotImplementedException(NotImplemented);
    public Task<bool> UpdateBotAsync(DiscordBotConfig bot) => throw new NotImplementedException(NotImplemented);
    public Task RemoveBotAsync(string botId) => throw new NotImplementedException(NotImplemented);
    public Task<bool> StartBotAsync(string botId) => throw new NotImplementedException(NotImplemented);
    public Task StopBotAsync(string botId) => throw new NotImplementedException(NotImplemented);
    public Task StopAllAsync() => throw new NotImplementedException(NotImplemented);
    public Task StartAllSavedAsync() => throw new NotImplementedException(NotImplemented);
    public Task<DiscordAdoptionResult> AdoptPlatformTokenAsync(string platformToken, string? preferredName = null) => throw new NotImplementedException(NotImplemented);
    public Task<string?> GetBotIdForRoleAsync(string role) => throw new NotImplementedException(NotImplemented);
    public Task AssignRoleAsync(string botId, string role, bool assigned) => throw new NotImplementedException(NotImplemented);
    public Task<DiscordBotWelcomeSettings> GetWelcomeSettingsAsync(string botId) => throw new NotImplementedException(NotImplemented);
    public Task SaveWelcomeSettingsAsync(string botId, DiscordBotWelcomeSettings settings) => throw new NotImplementedException(NotImplemented);
    public Task<DiscordBotAllowedChannels> GetAllowedChannelsAsync(string botId) => throw new NotImplementedException(NotImplemented);
    public Task SaveAllowedChannelsAsync(string botId, DiscordBotAllowedChannels allowed) => throw new NotImplementedException(NotImplemented);
    public Task<IReadOnlyList<DiscordBotScheduledMessage>> GetScheduledMessagesAsync(string botId) => throw new NotImplementedException(NotImplemented);
    public Task SaveScheduledMessagesAsync(string botId, IReadOnlyList<DiscordBotScheduledMessage> messages) => throw new NotImplementedException(NotImplemented);
    public Task ClearRoleAsync(string role) => throw new NotImplementedException(NotImplemented);
}

/// <summary>
/// Minimal disconnected IDiscordService: satisfies WorkflowMessageSender's
/// constructor; every method throws because no wave-8 flow sends to Discord.
/// </summary>
public sealed class IntegrationWave8DiscordService : IDiscordService
{
    private const string NotImplemented = "Not needed for wave-8 integration tests.";

    public string BotId => "wave8-fake";
    public bool IsConnected => false;
    public string? BotUsername => null;
    public string? DetectedBotUsername => null;
    public string? DetectedApplicationId => null;
    public string? DetectedAvatarUrl => null;
    public string? SelectedChannelId => null;
    public IReadOnlyList<DiscordGuildInfo> ConnectedGuilds => Array.Empty<DiscordGuildInfo>();
    public IReadOnlyList<DiscordSeenBot> SeenBots => Array.Empty<DiscordSeenBot>();
    public string? ModChannelId => null;

#pragma warning disable CS0067 // never raised by the fake
    public event Action<string>? BotIdentityDetected;
    public event EventHandler<DiscordSlashInvocation>? SlashCommandInvoked;
    public event EventHandler<DiscordComponentInteraction>? ComponentInteractionInvoked;
#pragma warning restore CS0067

    public Task<bool> SendMessageAsync(string message) => throw new NotImplementedException(NotImplemented);
    public Task<bool> SendToChannelAsync(string channelNameOrId, string content, string? embedJson, string? guildName = null) => throw new NotImplementedException(NotImplemented);
    public Task<bool> SendToChannelWithComponentsAsync(string channelNameOrId, string content, string? embedJson, string? buttonsJson, string? menuJson, string? componentPrefix, string? guildName = null) => throw new NotImplementedException(NotImplemented);
    public Task<bool> ValidateTokenAsync(string botToken) => throw new NotImplementedException(NotImplemented);
    public Task<bool> ConnectAsync(string botToken) => throw new NotImplementedException(NotImplemented);
    public Task DisconnectAsync() => throw new NotImplementedException(NotImplemented);
    public Task<bool> ReconnectAsync() => throw new NotImplementedException(NotImplemented);
    public Task SetSelectedChannelAsync(string channelId) => throw new NotImplementedException(NotImplemented);
    public Task<string> SendOwnerTestMessageAsync() => throw new NotImplementedException(NotImplemented);
    public Task<string> KickMemberAsync(string guildId, string userId, string? reason) => throw new NotImplementedException(NotImplemented);
    public Task<string> BanMemberAsync(string guildId, string userId, string? reason, int deleteMessageDays = 0) => throw new NotImplementedException(NotImplemented);
    public Task<string> UnbanMemberAsync(string guildId, string userId) => throw new NotImplementedException(NotImplemented);
    public Task<string> GrantRoleAsync(string guildId, string userId, string roleId) => throw new NotImplementedException(NotImplemented);
    public Task<string> RemoveRoleAsync(string guildId, string userId, string roleId) => throw new NotImplementedException(NotImplemented);
    public Task<string> TimeoutMemberAsync(string guildId, string userId, int minutes, string? reason) => throw new NotImplementedException(NotImplemented);
    public Task<string> RemoveTimeoutAsync(string guildId, string userId) => throw new NotImplementedException(NotImplemented);
    public Task<string> SetSlowmodeAsync(string channelId, int seconds) => throw new NotImplementedException(NotImplemented);
    public Task<string> PurgeMessagesAsync(string channelId, int count) => throw new NotImplementedException(NotImplemented);
    public Task<string?> SendAndCaptureReplyAsync(string channelId, string message, string authorBotId, TimeSpan timeout) => throw new NotImplementedException(NotImplemented);
    public Task<bool> SendDmAsync(string username, string content, string? embedJson, string? guildName = null) => throw new NotImplementedException(NotImplemented);
    public Task<bool> SendDmToChannelAsync(string channelId, string content, string? embedJson = null) => throw new NotImplementedException(NotImplemented);
    public Task<(bool Success, string? Error)> SendDmToUserAsync(string recipientUserId, string content) => throw new NotImplementedException(NotImplemented);
    public Task<(bool Success, string? ChannelId, string? Username, string? Error)> OpenDmByUserIdAsync(string userId) => throw new NotImplementedException(NotImplemented);
    public Task<bool> AddRoleToUserAsync(string username, string roleIdOrName, string? reason, string? guildName = null) => throw new NotImplementedException(NotImplemented);
    public Task<bool> RemoveRoleFromUserAsync(string username, string roleIdOrName, string? reason, string? guildName = null) => throw new NotImplementedException(NotImplemented);
    public Task<bool> CreateWorkflowChannelAsync(string name, string channelType, string? category, string? guildName = null) => throw new NotImplementedException(NotImplemented);
    public Task<bool> DeleteMessageByScopeAsync(string scope, string? guildName = null) => throw new NotImplementedException(NotImplemented);
    public Task<bool> EditLastBotMessageAsync(string newContent, string? embedJson, string? guildName = null) => throw new NotImplementedException(NotImplemented);
    public Task<bool> ReactToMessageAsync(string emoji, string scope, string? guildName = null) => throw new NotImplementedException(NotImplemented);
    public Task<bool> CreateThreadFromLastAsync(string threadName, int autoArchiveMinutes, string? guildName = null) => throw new NotImplementedException(NotImplemented);
    public Task<bool> SetChannelLockAsync(bool locked, string? reason, string? guildName = null) => throw new NotImplementedException(NotImplemented);
    public Task<bool> UserHasRoleAsync(string username, string roleIdOrName, string? guildName = null) => throw new NotImplementedException(NotImplemented);
    public Task<List<DiscordRoleInfo>> GetGuildRolesAsync(string guildId) => throw new NotImplementedException(NotImplemented);
    public Task<List<string>> GetMemberRoleIdsAsync(string guildId, string userId) => throw new NotImplementedException(NotImplemented);
    public Task<bool> UserHasPermissionAsync(string username, string permission, string? guildName = null) => throw new NotImplementedException(NotImplemented);
    public Task<IReadOnlyList<string>> GetWorkflowRoleNamesAsync(string? guildName = null) => throw new NotImplementedException(NotImplemented);
    public Task<IReadOnlyList<string>> GetWorkflowChannelNamesAsync(string? guildName = null) => throw new NotImplementedException(NotImplemented);
    public Task<string?> RegisterWorkflowSlashCommandAsync(string name, string description, IReadOnlyList<VyralBot.Workflows.Models.WorkflowParameter>? parameters = null) => throw new NotImplementedException(NotImplemented);
    public Task<string?> UnregisterWorkflowSlashCommandAsync(string name) => throw new NotImplementedException(NotImplemented);
    public Task<bool> KickMemberByNameAsync(string username, string? reason, string? guildName = null) => throw new NotImplementedException(NotImplemented);
    public Task<bool> BanMemberByNameAsync(string username, string? reason, string? guildName = null) => throw new NotImplementedException(NotImplemented);
    public Task<bool> UnbanMemberByNameAsync(string username, string? guildName = null) => throw new NotImplementedException(NotImplemented);
    public Task<bool> TimeoutMemberByNameAsync(string username, int minutes, string? reason, string? guildName = null) => throw new NotImplementedException(NotImplemented);
    public Task<bool> RemoveTimeoutMemberByNameAsync(string username, string? guildName = null) => throw new NotImplementedException(NotImplemented);
    public Task<bool> PurgeOriginChannelAsync(int count, string? guildName = null) => throw new NotImplementedException(NotImplemented);
    public Task<bool> SetSlowmodeOnOriginChannelAsync(int seconds, string? guildName = null) => throw new NotImplementedException(NotImplemented);
    public Task<CreateModChannelResult> CreateModChannelAsync(string modRoleName) => throw new NotImplementedException(NotImplemented);
    public Task<bool> CreateRoleAsync(string roleName, DiscordColor? color = null) => throw new NotImplementedException(NotImplemented);
    public Task SetModChannelAsync(string channelId) => throw new NotImplementedException(NotImplemented);
}

/// <summary>
/// Pass-through IVariableReplacementService: templates come back unchanged
/// (the timer pipeline's variable context is not under test in wave 8).
/// </summary>
public sealed class IntegrationWave8VariableReplacement : IVariableReplacementService
{
    public Task<string> ReplaceVariablesAsync(
        string template,
        Dictionary<string, string>? contextVariables = null,
        string? platform = null)
        => Task.FromResult(template ?? string.Empty);

    public Task<Dictionary<string, string>> GetAvailableVariablesAsync(string? platform = null)
        => Task.FromResult(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

    public Task<string?> GetVariableValueAsync(string variableName) => Task.FromResult<string?>(null);

    public bool ValidateVariableName(string name, out string? errorMessage)
    {
        errorMessage = null;
        return true;
    }

    public Task InvalidateCacheAsync() => Task.CompletedTask;
}
