using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using System.Data;
using VyralBot.Data.Entities;

namespace VyralBot.Data;

/// <summary>
/// Type handler for SQLite GUID storage (stored as TEXT).
/// </summary>
public class GuidTypeHandler : SqlMapper.TypeHandler<Guid>
{
    public override Guid Parse(object value)
    {
        if (value is string str)
            return Guid.Parse(str);
        if (value is Guid guid)
            return guid;
        throw new InvalidCastException($"Cannot convert {value?.GetType()} to Guid");
    }

    public override void SetValue(IDbDataParameter parameter, Guid value)
    {
        parameter.Value = value.ToString();
    }
}

/// <summary>
/// Lightweight Dapper-based data service.
/// Replaces EF Core to reduce memory overhead (~40MB savings).
/// </summary>
public class DapperDataService : IDataService
{
    private readonly string _connectionString;
    private readonly ILogger<DapperDataService> _logger;

    static DapperDataService()
    {
        // Register GUID type handler for SQLite TEXT columns
        SqlMapper.AddTypeHandler(new GuidTypeHandler());
    }

    public DapperDataService(string connectionString, ILogger<DapperDataService> logger)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    private IDbConnection CreateConnection() => new SqliteConnection(_connectionString);

    #region Platform Operations

    public async Task<IEnumerable<Platform>> GetPlatformsAsync()
    {
        using var conn = CreateConnection();
        return await conn.QueryAsync<Platform>("SELECT * FROM Platforms ORDER BY Name");
    }

    public async Task<Platform?> GetPlatformByNameAsync(string name)
    {
        using var conn = CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<Platform>(
            "SELECT * FROM Platforms WHERE Name = @Name", new { Name = name });
    }

    public async Task<Platform?> GetPlatformByIdAsync(Guid id)
    {
        using var conn = CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<Platform>(
            "SELECT * FROM Platforms WHERE Id = @Id", new { Id = id });
    }

    public async Task SavePlatformAsync(Platform platform)
    {
        platform.UpdatedAt = DateTime.UtcNow;

        using var conn = CreateConnection();

        // Check if exists
        var exists = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM Platforms WHERE Id = @Id", new { platform.Id });

        if (exists > 0)
        {
            // Update
            await conn.ExecuteAsync(@"
                UPDATE Platforms SET
                    Name = @Name,
                    Enabled = @Enabled,
                    AccessToken = @AccessToken,
                    RefreshToken = @RefreshToken,
                    TokenExpiresAt = @TokenExpiresAt,
                    Username = @Username,
                    UserId = @UserId,
                    ChannelId = @ChannelId,
                    ChatroomId = @ChatroomId,
                    ClientId = @ClientId,
                    ClientSecret = @ClientSecret,
                    Status = @Status,
                    LastError = @LastError,
                    ConnectedAt = @ConnectedAt,
                    KickChatEnabled = @KickChatEnabled,
                    KickChatAutoStart = @KickChatAutoStart,
                    TwitchChatEnabled = @TwitchChatEnabled,
                    TwitchChatAutoStart = @TwitchChatAutoStart,
                    YoutubeChatEnabled = @YoutubeChatEnabled,
                    YoutubeChatAutoStart = @YoutubeChatAutoStart,
                    TiktokChatEnabled = @TiktokChatEnabled,
                    TiktokChatAutoStart = @TiktokChatAutoStart,
                    DiscordChatEnabled = @DiscordChatEnabled,
                    DiscordChatAutoStart = @DiscordChatAutoStart,
                    FacebookChatEnabled = @FacebookChatEnabled,
                    FacebookChatAutoStart = @FacebookChatAutoStart,
                    RumbleChatEnabled = @RumbleChatEnabled,
                    RumbleChatAutoStart = @RumbleChatAutoStart,
                    UpdatedAt = @UpdatedAt
                WHERE Id = @Id", platform);
        }
        else
        {
            // Insert
            await conn.ExecuteAsync(@"
                INSERT INTO Platforms (
                    Id, Name, Enabled, AccessToken, RefreshToken, TokenExpiresAt,
                    Username, UserId, ChannelId, ChatroomId, ClientId, ClientSecret, Status, LastError,
                    ConnectedAt, KickChatEnabled, KickChatAutoStart,
                    TwitchChatEnabled, TwitchChatAutoStart,
                    YoutubeChatEnabled, YoutubeChatAutoStart,
                    TiktokChatEnabled, TiktokChatAutoStart,
                    DiscordChatEnabled, DiscordChatAutoStart,
                    FacebookChatEnabled, FacebookChatAutoStart,
                    RumbleChatEnabled, RumbleChatAutoStart,
                    CreatedAt, UpdatedAt
                ) VALUES (
                    @Id, @Name, @Enabled, @AccessToken, @RefreshToken, @TokenExpiresAt,
                    @Username, @UserId, @ChannelId, @ChatroomId, @ClientId, @ClientSecret, @Status, @LastError,
                    @ConnectedAt, @KickChatEnabled, @KickChatAutoStart,
                    @TwitchChatEnabled, @TwitchChatAutoStart,
                    @YoutubeChatEnabled, @YoutubeChatAutoStart,
                    @TiktokChatEnabled, @TiktokChatAutoStart,
                    @DiscordChatEnabled, @DiscordChatAutoStart,
                    @FacebookChatEnabled, @FacebookChatAutoStart,
                    @RumbleChatEnabled, @RumbleChatAutoStart,
                    @CreatedAt, @UpdatedAt
                )", platform);
        }
    }

    public async Task DeletePlatformAsync(Guid id)
    {
        using var conn = CreateConnection();
        await conn.ExecuteAsync("DELETE FROM Platforms WHERE Id = @Id", new { Id = id });
    }

    #endregion

    #region Discord Bot Operations (multi-bot support)

    public async Task<IEnumerable<DiscordBot>> GetDiscordBotsAsync()
    {
        using var conn = CreateConnection();
        return await conn.QueryAsync<DiscordBot>("SELECT * FROM DiscordBots ORDER BY CreatedAt");
    }

    public async Task SaveDiscordBotAsync(DiscordBot bot)
    {
        bot.UpdatedAt = DateTime.UtcNow;

        using var conn = CreateConnection();

        var exists = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM DiscordBots WHERE Id = @Id", new { bot.Id });

        if (exists > 0)
        {
            await conn.ExecuteAsync(@"
                UPDATE DiscordBots SET
                    Name = @Name,
                    Token = @Token,
                    Status = @Status,
                    SelectedChannelId = @SelectedChannelId,
                    ApplicationId = @ApplicationId,
                    BotUsername = @BotUsername,
                    AvatarUrl = @AvatarUrl,
                    RoleLinking = @RoleLinking,
                    RoleGiveaway = @RoleGiveaway,
                    AutoStart = @AutoStart,
                    UpdatedAt = @UpdatedAt
                WHERE Id = @Id", bot);
        }
        else
        {
            await conn.ExecuteAsync(@"
                INSERT INTO DiscordBots (
                    Id, Name, Token, Status, SelectedChannelId,
                    ApplicationId, BotUsername, AvatarUrl, RoleLinking, RoleGiveaway,
                    AutoStart,
                    CreatedAt, UpdatedAt
                ) VALUES (
                    @Id, @Name, @Token, @Status, @SelectedChannelId,
                    @ApplicationId, @BotUsername, @AvatarUrl, @RoleLinking, @RoleGiveaway,
                    @AutoStart,
                    @CreatedAt, @UpdatedAt
                )", bot);
        }
    }

    public async Task DeleteDiscordBotAsync(string id)
    {
        using var conn = CreateConnection();
        await conn.ExecuteAsync("DELETE FROM DiscordBots WHERE Id = @Id", new { Id = id });
    }

    #endregion

    #region Chat Message Operations

    public async Task<bool> SaveChatMessageAsync(Entities.ChatMessage message)
    {
        if (message == null) throw new ArgumentNullException(nameof(message));

        using var conn = CreateConnection();

        // Deduplicate by MessageId when provided
        if (!string.IsNullOrEmpty(message.MessageId))
        {
            var exists = await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(1) FROM ChatMessages WHERE MessageId = @MessageId", new { message.MessageId });
            if (exists > 0) return false;
        }

        await conn.ExecuteAsync(@"
            INSERT INTO ChatMessages (
                Id, Platform, Channel, MessageId, UserId, Username, DisplayName, Content, Timestamp,
                Color, IsModerator, IsSubscriber, IsOwner, BadgesJson, CreatedAt
            ) VALUES (
                @Id, @Platform, @Channel, @MessageId, @UserId, @Username, @DisplayName, @Content, @Timestamp,
                @Color, @IsModerator, @IsSubscriber, @IsOwner, @BadgesJson, @CreatedAt
            )", message);
            return true;
    }

    public async Task<IEnumerable<Entities.ChatMessage>> GetChatMessagesAsync(string platform, string channel, int limit = 200)
    {
        using var conn = CreateConnection();
        // Take the NEWEST rows (DESC) so history never degrades to weeks-old data
        // once a channel exceeds the limit, then restore chronological order.
        var messages = (await conn.QueryAsync<Entities.ChatMessage>(
            "SELECT * FROM ChatMessages WHERE Platform = @Platform AND Channel = @Channel ORDER BY Timestamp DESC LIMIT @Limit",
            new { Platform = platform, Channel = channel, Limit = limit })).ToList();
        messages.Reverse();
        return messages;
    }

    public async Task DeleteChatMessagesAsync(string platform, string channel)
    {
        using var conn = CreateConnection();
        await conn.ExecuteAsync("DELETE FROM ChatMessages WHERE Platform = @Platform AND Channel = @Channel", new { Platform = platform, Channel = channel });
    }

    #endregion

    #region Chat Event Settings Operations

    public async Task<ChatEventSetting?> GetChatEventSettingAsync(string platform, string eventType)
    {
        using var conn = CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<ChatEventSetting>(
            "SELECT * FROM ChatEventSettings WHERE Platform = @Platform AND EventType = @EventType",
            new { Platform = platform, EventType = eventType });
    }

    public async Task<IEnumerable<ChatEventSetting>> GetAllChatEventSettingsAsync(string? platform = null)
    {
        using var conn = CreateConnection();

        if (string.IsNullOrEmpty(platform))
        {
            return await conn.QueryAsync<ChatEventSetting>("SELECT * FROM ChatEventSettings ORDER BY Platform, EventType");
        }

        return await conn.QueryAsync<ChatEventSetting>(
            "SELECT * FROM ChatEventSettings WHERE Platform = @Platform ORDER BY EventType",
            new { Platform = platform });
    }

    public async Task SaveChatEventSettingAsync(ChatEventSetting setting)
    {
        setting.UpdatedAt = DateTime.UtcNow;

        using var conn = CreateConnection();

        var exists = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM ChatEventSettings WHERE Id = @Id", new { setting.Id });

        if (exists > 0)
        {
            await conn.ExecuteAsync(@"
                UPDATE ChatEventSettings SET
                    Platform = @Platform,
                    EventType = @EventType,
                    ShowInChat = @ShowInChat,
                    AutoReplyEnabled = @AutoReplyEnabled,
                    AutoReplyMessage = @AutoReplyMessage,
                    SoundEnabled = @SoundEnabled,
                    SoundFile = @SoundFile,
                    SoundVolume = @SoundVolume,
                    UpdatedAt = @UpdatedAt
                WHERE Id = @Id", setting);
        }
        else
        {
            await conn.ExecuteAsync(@"
                INSERT INTO ChatEventSettings (
                    Id, Platform, EventType, ShowInChat, AutoReplyEnabled, AutoReplyMessage,
                    SoundEnabled, SoundFile, SoundVolume, CreatedAt, UpdatedAt
                ) VALUES (
                    @Id, @Platform, @EventType, @ShowInChat, @AutoReplyEnabled, @AutoReplyMessage,
                    @SoundEnabled, @SoundFile, @SoundVolume, @CreatedAt, @UpdatedAt
                )", setting);
        }
    }

    public async Task DeleteChatEventSettingAsync(Guid id)
    {
        using var conn = CreateConnection();
        await conn.ExecuteAsync("DELETE FROM ChatEventSettings WHERE Id = @Id", new { Id = id });
    }

    #endregion

    #region Event Statistics Operations

    public async Task SaveEventStatisticAsync(EventStatistic statistic)
    {
        using var conn = CreateConnection();

        await conn.ExecuteAsync(@"
            INSERT INTO EventStatistics (
                Id, Platform, EventType, Username, EventData, Timestamp, WasAutoReplied, CreatedAt
            ) VALUES (
                @Id, @Platform, @EventType, @Username, @EventData, @Timestamp, @WasAutoReplied, @CreatedAt
            )", statistic);
    }

    public async Task<IEnumerable<EventStatistic>> GetEventStatisticsAsync(string? platform = null, string? eventType = null, int limit = 100, DateTime? startDate = null, DateTime? endDate = null)
    {
        using var conn = CreateConnection();

        var conditions = new List<string>();
        var parameters = new DynamicParameters();

        if (!string.IsNullOrEmpty(platform))
        {
            conditions.Add("Platform = @Platform");
            parameters.Add("Platform", platform);
        }

        // Case-insensitive so legacy rows written with different casing still match
        if (!string.IsNullOrEmpty(eventType))
        {
            conditions.Add("UPPER(EventType) = UPPER(@EventType)");
            parameters.Add("EventType", eventType);
        }

        // Date filters applied in SQL (bound DateTime parameters store/compare in the
        // same TEXT format as the rows written by SaveEventStatisticAsync) so LIMIT
        // applies AFTER filtering instead of truncating the filtered window.
        if (startDate.HasValue)
        {
            conditions.Add("Timestamp >= @StartDate");
            parameters.Add("StartDate", startDate.Value);
        }

        if (endDate.HasValue)
        {
            conditions.Add("Timestamp <= @EndDate");
            parameters.Add("EndDate", endDate.Value);
        }

        parameters.Add("Limit", limit);

        var whereClause = conditions.Any() ? "WHERE " + string.Join(" AND ", conditions) : "";
        var query = $"SELECT * FROM EventStatistics {whereClause} ORDER BY Timestamp DESC LIMIT @Limit";

        return await conn.QueryAsync<EventStatistic>(query, parameters);
    }

    public async Task<IEnumerable<EventStatistic>> GetEventStatisticsByUsernameAsync(string username, string? platform = null)
    {
        using var conn = CreateConnection();

        if (string.IsNullOrEmpty(platform))
        {
            return await conn.QueryAsync<EventStatistic>(
                "SELECT * FROM EventStatistics WHERE Username = @Username ORDER BY Timestamp DESC LIMIT 100",
                new { Username = username });
        }

        return await conn.QueryAsync<EventStatistic>(
            "SELECT * FROM EventStatistics WHERE Username = @Username COLLATE NOCASE AND Platform = @Platform COLLATE NOCASE ORDER BY Timestamp DESC LIMIT 100",
            new { Username = username, Platform = platform });
    }

    public async Task<int> DeleteEventStatisticsAsync()
    {
        using var conn = CreateConnection();
        return await conn.ExecuteAsync("DELETE FROM EventStatistics");
    }

    #endregion

    #region Currency Operations

    public async Task<CurrencySetting?> GetCurrencySettingsAsync()
    {
        using var conn = CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<CurrencySetting>("SELECT * FROM CurrencySettings LIMIT 1");
    }

    public async Task SaveCurrencySettingsAsync(CurrencySetting settings)
    {
        using var conn = CreateConnection();
        settings.UpdatedAt = DateTime.UtcNow;

        var exists = await conn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM CurrencySettings WHERE Id = @Id", settings);

        if (exists > 0)
        {
            await conn.ExecuteAsync(@"
                UPDATE CurrencySettings SET
                    CurrencyName = @CurrencyName,
                    CurrencySymbol = @CurrencySymbol,
                    WatchTimeEnabled = @WatchTimeEnabled,
                    WatchTimeInterval = @WatchTimeInterval,
                    WatchTimeAmount = @WatchTimeAmount,
                    ChatActivityEnabled = @ChatActivityEnabled,
                    ChatActivityCooldown = @ChatActivityCooldown,
                    ChatActivityAmount = @ChatActivityAmount,
                    FollowBonusEnabled = @FollowBonusEnabled,
                    FollowBonusAmount = @FollowBonusAmount,
                    SubBonusEnabled = @SubBonusEnabled,
                    SubBonusAmount = @SubBonusAmount,
                    GiftSubBonusEnabled = @GiftSubBonusEnabled,
                    GiftSubBonusAmountPerSub = @GiftSubBonusAmountPerSub,
                    RaidBonusEnabled = @RaidBonusEnabled,
                    RaidBonusAmountPerViewer = @RaidBonusAmountPerViewer,
                    UnifiedBalanceEnabled = @UnifiedBalanceEnabled,
                    CheerBonusEnabled = @CheerBonusEnabled,
                    CheerBonusAmountPerBit = @CheerBonusAmountPerBit,
                    SubMultiplierEnabled = @SubMultiplierEnabled,
                    SubMultiplierValue = @SubMultiplierValue,
                    VipMultiplierEnabled = @VipMultiplierEnabled,
                    VipMultiplierValue = @VipMultiplierValue,
                    ModMultiplierEnabled = @ModMultiplierEnabled,
                    ModMultiplierValue = @ModMultiplierValue,
                    FirstChatBonusEnabled = @FirstChatBonusEnabled,
                    FirstChatBonusAmount = @FirstChatBonusAmount,
                    DailyBonusEnabled = @DailyBonusEnabled,
                    DailyBonusAmount = @DailyBonusAmount,
                    StreakBonusEnabled = @StreakBonusEnabled,
                    StreakBonusDay1 = @StreakBonusDay1,
                    StreakBonusDay5 = @StreakBonusDay5,
                    StreakBonusDay10 = @StreakBonusDay10,
                    HostBonusEnabled = @HostBonusEnabled,
                    HostBonusAmount = @HostBonusAmount,
                    TwitchEarnMultiplier = @TwitchEarnMultiplier,
                    KickEarnMultiplier = @KickEarnMultiplier,
                    YouTubeEarnMultiplier = @YouTubeEarnMultiplier,
                    CurrencyDecayEnabled = @CurrencyDecayEnabled,
                    CurrencyDecayPercent = @CurrencyDecayPercent,
                    CurrencyDecayDays = @CurrencyDecayDays,
                    BankEnabled = @BankEnabled,
                    BankInterestRate = @BankInterestRate,
                    BankMaxBalance = @BankMaxBalance,
                    BankWithdrawalPenalty = @BankWithdrawalPenalty,
                    BankWithdrawalCooldownMinutes = @BankWithdrawalCooldownMinutes,
                    TransferTaxPercent = @TransferTaxPercent,
                    UpdatedAt = @UpdatedAt
                WHERE Id = @Id", settings);
        }
        else
        {
            settings.CreatedAt = DateTime.UtcNow;
            await conn.ExecuteAsync(@"
                INSERT INTO CurrencySettings (
                    Id, CurrencyName, CurrencySymbol, WatchTimeEnabled, WatchTimeInterval, WatchTimeAmount,
                    ChatActivityEnabled, ChatActivityCooldown, ChatActivityAmount,
                    FollowBonusEnabled, FollowBonusAmount, SubBonusEnabled, SubBonusAmount,
                    GiftSubBonusEnabled, GiftSubBonusAmountPerSub, RaidBonusEnabled, RaidBonusAmountPerViewer,
                    UnifiedBalanceEnabled,
                    CheerBonusEnabled, CheerBonusAmountPerBit,
                    SubMultiplierEnabled, SubMultiplierValue,
                    VipMultiplierEnabled, VipMultiplierValue,
                    ModMultiplierEnabled, ModMultiplierValue,
                    FirstChatBonusEnabled, FirstChatBonusAmount,
                    DailyBonusEnabled, DailyBonusAmount,
                    StreakBonusEnabled, StreakBonusDay1, StreakBonusDay5, StreakBonusDay10,
                    HostBonusEnabled, HostBonusAmount,
                    TwitchEarnMultiplier, KickEarnMultiplier, YouTubeEarnMultiplier,
                    CurrencyDecayEnabled, CurrencyDecayPercent, CurrencyDecayDays,
                    BankEnabled, BankInterestRate, BankMaxBalance, BankWithdrawalPenalty, BankWithdrawalCooldownMinutes,
                    TransferTaxPercent,
                    CreatedAt, UpdatedAt
                ) VALUES (
                    @Id, @CurrencyName, @CurrencySymbol, @WatchTimeEnabled, @WatchTimeInterval, @WatchTimeAmount,
                    @ChatActivityEnabled, @ChatActivityCooldown, @ChatActivityAmount,
                    @FollowBonusEnabled, @FollowBonusAmount, @SubBonusEnabled, @SubBonusAmount,
                    @GiftSubBonusEnabled, @GiftSubBonusAmountPerSub, @RaidBonusEnabled, @RaidBonusAmountPerViewer,
                    @UnifiedBalanceEnabled,
                    @CheerBonusEnabled, @CheerBonusAmountPerBit,
                    @SubMultiplierEnabled, @SubMultiplierValue,
                    @VipMultiplierEnabled, @VipMultiplierValue,
                    @ModMultiplierEnabled, @ModMultiplierValue,
                    @FirstChatBonusEnabled, @FirstChatBonusAmount,
                    @DailyBonusEnabled, @DailyBonusAmount,
                    @StreakBonusEnabled, @StreakBonusDay1, @StreakBonusDay5, @StreakBonusDay10,
                    @HostBonusEnabled, @HostBonusAmount,
                    @TwitchEarnMultiplier, @KickEarnMultiplier, @YouTubeEarnMultiplier,
                    @CurrencyDecayEnabled, @CurrencyDecayPercent, @CurrencyDecayDays,
                    @BankEnabled, @BankInterestRate, @BankMaxBalance, @BankWithdrawalPenalty, @BankWithdrawalCooldownMinutes,
                    @TransferTaxPercent,
                    @CreatedAt, @UpdatedAt
                )", settings);
        }
    }

    public async Task<UserCurrency?> GetUserCurrencyAsync(string username, string platform)
    {
        using var conn = CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<UserCurrency>(
            "SELECT * FROM UserCurrency WHERE Username = @Username COLLATE NOCASE AND Platform = @Platform COLLATE NOCASE",
            new { Username = username, Platform = platform });
    }

    public async Task<IEnumerable<UserCurrency>> GetUserCurrenciesAsync(string username)
    {
        using var conn = CreateConnection();
        return await conn.QueryAsync<UserCurrency>(
            "SELECT * FROM UserCurrency WHERE Username = @Username",
            new { Username = username });
    }

    public async Task<IEnumerable<UserCurrency>> GetCurrencyLeaderboardAsync(int limit = 10, string? platform = null)
    {
        using var conn = CreateConnection();

        if (string.IsNullOrEmpty(platform))
        {
            return await conn.QueryAsync<UserCurrency>(
                "SELECT * FROM UserCurrency ORDER BY Balance DESC LIMIT @Limit",
                new { Limit = limit });
        }

        return await conn.QueryAsync<UserCurrency>(
            "SELECT * FROM UserCurrency WHERE Platform = @Platform ORDER BY Balance DESC LIMIT @Limit",
            new { Platform = platform, Limit = limit });
    }

    public async Task SaveUserCurrencyAsync(UserCurrency currency)
    {
        using var conn = CreateConnection();
        currency.UpdatedAt = DateTime.UtcNow;

        var exists = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM UserCurrency WHERE Username = @Username COLLATE NOCASE AND Platform = @Platform COLLATE NOCASE",
            currency);

        if (exists > 0)
        {
            await conn.ExecuteAsync(@"
                UPDATE UserCurrency SET
                    Balance = @Balance,
                    TotalEarned = @TotalEarned,
                    TotalSpent = @TotalSpent,
                    LastWatchTimeUpdate = @LastWatchTimeUpdate,
                    LastChatActivityUpdate = @LastChatActivityUpdate,
                    UpdatedAt = @UpdatedAt
                WHERE Username = @Username COLLATE NOCASE AND Platform = @Platform COLLATE NOCASE", currency);
        }
        else
        {
            currency.CreatedAt = DateTime.UtcNow;
            await conn.ExecuteAsync(@"
                INSERT INTO UserCurrency (
                    Id, Username, Platform, Balance, TotalEarned, TotalSpent,
                    LastWatchTimeUpdate, LastChatActivityUpdate, CreatedAt, UpdatedAt
                ) VALUES (
                    @Id, @Username, @Platform, @Balance, @TotalEarned, @TotalSpent,
                    @LastWatchTimeUpdate, @LastChatActivityUpdate, @CreatedAt, @UpdatedAt
                )", currency);
        }
    }

    public async Task SaveCurrencyTransactionAsync(CurrencyTransaction transaction)
    {
        using var conn = CreateConnection();
        transaction.CreatedAt = DateTime.UtcNow;

        await conn.ExecuteAsync(@"
            INSERT INTO CurrencyTransactions (
                Id, Username, Platform, Amount, TransactionType, Reason,
                BalanceBefore, BalanceAfter, Timestamp, CreatedAt
            ) VALUES (
                @Id, @Username, @Platform, @Amount, @TransactionType, @Reason,
                @BalanceBefore, @BalanceAfter, @Timestamp, @CreatedAt
            )", transaction);
    }

    public async Task<IEnumerable<CurrencyTransaction>> GetCurrencyTransactionsAsync(string? username = null, string? platform = null, int limit = 100)
    {
        using var conn = CreateConnection();

        if (!string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(platform))
        {
            return await conn.QueryAsync<CurrencyTransaction>(
                "SELECT * FROM CurrencyTransactions WHERE Username = @Username COLLATE NOCASE AND Platform = @Platform COLLATE NOCASE ORDER BY Timestamp DESC LIMIT @Limit",
                new { Username = username, Platform = platform, Limit = limit });
        }
        else if (!string.IsNullOrEmpty(username))
        {
            return await conn.QueryAsync<CurrencyTransaction>(
                "SELECT * FROM CurrencyTransactions WHERE Username = @Username ORDER BY Timestamp DESC LIMIT @Limit",
                new { Username = username, Limit = limit });
        }
        else if (!string.IsNullOrEmpty(platform))
        {
            return await conn.QueryAsync<CurrencyTransaction>(
                "SELECT * FROM CurrencyTransactions WHERE Platform = @Platform ORDER BY Timestamp DESC LIMIT @Limit",
                new { Platform = platform, Limit = limit });
        }

        return await conn.QueryAsync<CurrencyTransaction>(
            "SELECT * FROM CurrencyTransactions ORDER BY Timestamp DESC LIMIT @Limit",
            new { Limit = limit });
    }

    public async Task<CurrencyReward?> GetCurrencyRewardAsync(string id)
    {
        using var conn = CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<CurrencyReward>(
            "SELECT * FROM CurrencyRewards WHERE Id = @Id",
            new { Id = id });
    }

    public async Task<IEnumerable<CurrencyReward>> GetCurrencyRewardsAsync(bool? enabledOnly = null)
    {
        using var conn = CreateConnection();

        if (enabledOnly == true)
        {
            return await conn.QueryAsync<CurrencyReward>(
                "SELECT * FROM CurrencyRewards WHERE IsEnabled = 1 ORDER BY Cost ASC");
        }

        return await conn.QueryAsync<CurrencyReward>(
            "SELECT * FROM CurrencyRewards ORDER BY Cost ASC");
    }

    public async Task SaveCurrencyRewardAsync(CurrencyReward reward)
    {
        using var conn = CreateConnection();
        reward.UpdatedAt = DateTime.UtcNow;

        var exists = await conn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM CurrencyRewards WHERE Id = @Id", reward);

        if (exists > 0)
        {
            await conn.ExecuteAsync(@"
                UPDATE CurrencyRewards SET
                    Name = @Name,
                    Description = @Description,
                    Cost = @Cost,
                    IsEnabled = @IsEnabled,
                    RequiresApproval = @RequiresApproval,
                    Cooldown = @Cooldown,
                    MaxRedemptionsPerUser = @MaxRedemptionsPerUser,
                    MaxRedemptionsTotal = @MaxRedemptionsTotal,
                    CurrentRedemptions = @CurrentRedemptions,
                    Category = @Category,
                    Prompt = @Prompt,
                    ActionSequenceId = @ActionSequenceId,
                    AvailableFrom = @AvailableFrom,
                    AvailableUntil = @AvailableUntil,
                    RewardTier = @RewardTier,
                    RequiresSubscription = @RequiresSubscription,
                    RequiredLoyaltyTier = @RequiredLoyaltyTier,
                    UpdatedAt = @UpdatedAt
                WHERE Id = @Id", reward);
        }
        else
        {
            reward.CreatedAt = DateTime.UtcNow;
            await conn.ExecuteAsync(@"
                INSERT INTO CurrencyRewards (
                    Id, Name, Description, Cost, IsEnabled, RequiresApproval, Cooldown,
                    MaxRedemptionsPerUser, MaxRedemptionsTotal, CurrentRedemptions,
                    Category, Prompt,
                    ActionSequenceId, AvailableFrom, AvailableUntil, RewardTier,
                    RequiresSubscription, RequiredLoyaltyTier,
                    CreatedAt, UpdatedAt
                ) VALUES (
                    @Id, @Name, @Description, @Cost, @IsEnabled, @RequiresApproval, @Cooldown,
                    @MaxRedemptionsPerUser, @MaxRedemptionsTotal, @CurrentRedemptions,
                    @Category, @Prompt,
                    @ActionSequenceId, @AvailableFrom, @AvailableUntil, @RewardTier,
                    @RequiresSubscription, @RequiredLoyaltyTier,
                    @CreatedAt, @UpdatedAt
                )", reward);
        }
    }

    public async Task DeleteCurrencyRewardAsync(string id)
    {
        using var conn = CreateConnection();
        await conn.ExecuteAsync("DELETE FROM CurrencyRewards WHERE Id = @Id", new { Id = id });
    }

    public async Task<CurrencyRedemption?> GetCurrencyRedemptionAsync(string id)
    {
        using var conn = CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<CurrencyRedemption>(
            "SELECT * FROM CurrencyRedemptions WHERE Id = @Id",
            new { Id = id });
    }

    public async Task<IEnumerable<CurrencyRedemption>> GetCurrencyRedemptionsAsync(string? status = null, string? username = null)
    {
        using var conn = CreateConnection();

        if (!string.IsNullOrEmpty(status) && !string.IsNullOrEmpty(username))
        {
            return await conn.QueryAsync<CurrencyRedemption>(
                "SELECT * FROM CurrencyRedemptions WHERE Status = @Status AND Username = @Username ORDER BY RedeemedAt DESC",
                new { Status = status, Username = username });
        }
        else if (!string.IsNullOrEmpty(status))
        {
            return await conn.QueryAsync<CurrencyRedemption>(
                "SELECT * FROM CurrencyRedemptions WHERE Status = @Status ORDER BY RedeemedAt DESC",
                new { Status = status });
        }
        else if (!string.IsNullOrEmpty(username))
        {
            return await conn.QueryAsync<CurrencyRedemption>(
                "SELECT * FROM CurrencyRedemptions WHERE Username = @Username ORDER BY RedeemedAt DESC",
                new { Username = username });
        }

        return await conn.QueryAsync<CurrencyRedemption>(
            "SELECT * FROM CurrencyRedemptions ORDER BY RedeemedAt DESC");
    }

    public async Task SaveCurrencyRedemptionAsync(CurrencyRedemption redemption)
    {
        using var conn = CreateConnection();

        var exists = await conn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM CurrencyRedemptions WHERE Id = @Id", redemption);

        if (exists > 0)
        {
            await conn.ExecuteAsync(@"
                UPDATE CurrencyRedemptions SET
                    Status = @Status,
                    ProcessedAt = @ProcessedAt,
                    ProcessedBy = @ProcessedBy,
                    Notes = @Notes
                WHERE Id = @Id", redemption);
        }
        else
        {
            redemption.CreatedAt = DateTime.UtcNow;
            await conn.ExecuteAsync(@"
                INSERT INTO CurrencyRedemptions (
                    Id, RewardId, RewardName, Username, Platform, Cost, UserInput,
                    Status, RedeemedAt, ProcessedAt, ProcessedBy, Notes, CreatedAt
                ) VALUES (
                    @Id, @RewardId, @RewardName, @Username, @Platform, @Cost, @UserInput,
                    @Status, @RedeemedAt, @ProcessedAt, @ProcessedBy, @Notes, @CreatedAt
                )", redemption);
        }
    }

    public async Task DeleteCurrencyRedemptionAsync(string id)
    {
        using var conn = CreateConnection();
        await conn.ExecuteAsync("DELETE FROM CurrencyRedemptions WHERE Id = @Id", new { Id = id });
    }

    #endregion

    #region Linked Account Operations

    public async Task<LinkedAccount?> GetLinkedAccountAsync(string username, string platform)
    {
        using var conn = CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<LinkedAccount>(
            "SELECT * FROM LinkedAccounts WHERE Username = @Username COLLATE NOCASE AND Platform = @Platform COLLATE NOCASE",
            new { Username = username, Platform = platform });
    }

    public async Task<IEnumerable<LinkedAccount>> GetLinkedAccountsByViewerIdAsync(string viewerId)
    {
        using var conn = CreateConnection();
        return await conn.QueryAsync<LinkedAccount>(
            "SELECT * FROM LinkedAccounts WHERE ViewerId = @ViewerId",
            new { ViewerId = viewerId });
    }

    public async Task<string?> GetViewerIdAsync(string username, string platform)
    {
        using var conn = CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<string>(
            "SELECT ViewerId FROM LinkedAccounts WHERE Username = @Username COLLATE NOCASE AND Platform = @Platform COLLATE NOCASE",
            new { Username = username, Platform = platform });
    }

    public async Task SaveLinkedAccountAsync(LinkedAccount account)
    {
        using var conn = CreateConnection();

        var exists = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM LinkedAccounts WHERE Id = @Id", account);

        if (exists > 0)
        {
            await conn.ExecuteAsync(@"
                UPDATE LinkedAccounts SET
                    ViewerId = @ViewerId,
                    Username = @Username,
                    Platform = @Platform,
                    IsVerified = @IsVerified,
                    VerifiedAt = @VerifiedAt
                WHERE Id = @Id", account);
        }
        else
        {
            account.CreatedAt = DateTime.UtcNow;
            await conn.ExecuteAsync(@"
                INSERT INTO LinkedAccounts (Id, ViewerId, Username, Platform, IsVerified, VerifiedAt, CreatedAt)
                VALUES (@Id, @ViewerId, @Username, @Platform, @IsVerified, @VerifiedAt, @CreatedAt)", account);
        }
    }

    public async Task DeleteLinkedAccountAsync(string id)
    {
        using var conn = CreateConnection();
        await conn.ExecuteAsync("DELETE FROM LinkedAccounts WHERE Id = @Id", new { Id = id });
    }

    #endregion

    #region Viewer Identity Operations (Link Codes, Platform Status, Preferences, Social Goals)

    public async Task<LinkCode?> GetLinkCodeAsync(string code)
    {
        using var conn = CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<LinkCode>(
            "SELECT * FROM LinkCodes WHERE Code = @Code",
            new { Code = code });
    }

    public async Task SaveLinkCodeAsync(LinkCode code)
    {
        using var conn = CreateConnection();

        var exists = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM LinkCodes WHERE Id = @Id", code);

        if (exists > 0)
        {
            await conn.ExecuteAsync(@"
                UPDATE LinkCodes SET
                    Code = @Code,
                    ViewerId = @ViewerId,
                    CreatedByUsername = @CreatedByUsername,
                    CreatedByPlatform = @CreatedByPlatform,
                    Purpose = @Purpose,
                    TargetPlatform = @TargetPlatform,
                    ExpiresAt = @ExpiresAt,
                    RedeemedByUsername = @RedeemedByUsername,
                    RedeemedByPlatform = @RedeemedByPlatform,
                    RedeemedAt = @RedeemedAt
                WHERE Id = @Id", code);
        }
        else
        {
            code.CreatedAt = DateTime.UtcNow;
            await conn.ExecuteAsync(@"
                INSERT INTO LinkCodes (Id, Code, ViewerId, CreatedByUsername, CreatedByPlatform, Purpose, TargetPlatform, CreatedAt, ExpiresAt,
                    RedeemedByUsername, RedeemedByPlatform, RedeemedAt)
                VALUES (@Id, @Code, @ViewerId, @CreatedByUsername, @CreatedByPlatform, @Purpose, @TargetPlatform, @CreatedAt, @ExpiresAt,
                    @RedeemedByUsername, @RedeemedByPlatform, @RedeemedAt)", code);
        }
    }

    public async Task InvalidateLinkCodesForViewerAsync(string viewerId)
    {
        using var conn = CreateConnection();
        await conn.ExecuteAsync(
            "UPDATE LinkCodes SET ExpiresAt = @Now WHERE ViewerId = @ViewerId AND RedeemedAt IS NULL",
            new { Now = DateTime.UtcNow, ViewerId = viewerId });
    }

    public async Task<IEnumerable<LinkCode>> GetActiveLinkCodesAsync()
    {
        using var conn = CreateConnection();
        return await conn.QueryAsync<LinkCode>(
            "SELECT * FROM LinkCodes WHERE RedeemedAt IS NULL AND ExpiresAt > @Now",
            new { Now = DateTime.UtcNow });
    }

    public async Task<IEnumerable<LinkCode>> GetRecentLinkCodesAsync(int limit = 50)
    {
        using var conn = CreateConnection();
        return await conn.QueryAsync<LinkCode>(
            "SELECT * FROM LinkCodes ORDER BY CreatedAt DESC LIMIT @Limit",
            new { Limit = limit });
    }

    public async Task SaveDiscordDmMessageAsync(DiscordDmMessage message)
    {
        using var conn = CreateConnection();
        await conn.ExecuteAsync("INSERT OR REPLACE INTO DiscordDmMessages (Id, BotId, BotUsername, ChannelId, Username, Direction, Content, Timestamp) VALUES (@Id, @BotId, @BotUsername, @ChannelId, @Username, @Direction, @Content, @Timestamp)", message);
    }

    public async Task<IEnumerable<DiscordDmMessage>> GetDiscordDmHistoryAsync(string username)
    {
        using var conn = CreateConnection();
        return await conn.QueryAsync<DiscordDmMessage>(
            "SELECT * FROM DiscordDmMessages WHERE Username = @Username COLLATE NOCASE ORDER BY Timestamp",
            new { Username = username });
    }

    public async Task<IEnumerable<(string Username, string? BotUsername, DateTime LastAt, int Total)>> GetDiscordDmConversationsAsync()
    {
        using var conn = CreateConnection();
        var rows = await conn.QueryAsync<(string Username, string BotUsername, string LastAt, int Total)>(
            "SELECT Username, MAX(BotUsername) AS BotUsername, MAX(Timestamp) AS LastAt, COUNT(1) AS Total FROM DiscordDmMessages GROUP BY Username COLLATE NOCASE ORDER BY MAX(Timestamp) DESC");
        return rows.Select(r => (r.Username, (string?)r.BotUsername, DateTime.TryParse(r.LastAt, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out var t) ? t : DateTime.MinValue, r.Total));
    }

        public async Task ClearDiscordDmHistoryAsync(string? username)
    {
        using var conn = CreateConnection();
        if (string.IsNullOrEmpty(username))
        {
            await conn.ExecuteAsync("DELETE FROM DiscordDmMessages");
        }
        else
        {
            await conn.ExecuteAsync("DELETE FROM DiscordDmMessages WHERE Username = @Username COLLATE NOCASE", new { Username = username });
        }
    }

    public async Task DeleteLinkCodeAsync(string id)
    {
        using var conn = CreateConnection();
        await conn.ExecuteAsync("DELETE FROM LinkCodes WHERE Id = @Id", new { Id = id });
    }

    public async Task<IEnumerable<LinkedAccount>> GetAllLinkedAccountsAsync()
    {
        using var conn = CreateConnection();
        return await conn.QueryAsync<LinkedAccount>("SELECT * FROM LinkedAccounts");
    }

    public async Task UpdateLinkedAccountViewerAsync(string accountId, string newViewerId)
    {
        using var conn = CreateConnection();
        await conn.ExecuteAsync(
            "UPDATE LinkedAccounts SET ViewerId = @ViewerId WHERE Id = @Id",
            new { ViewerId = newViewerId, Id = accountId });
    }

    public async Task DeleteLinkedAccountsByViewerIdAsync(string viewerId)
    {
        using var conn = CreateConnection();
        await conn.ExecuteAsync("DELETE FROM LinkedAccounts WHERE ViewerId = @ViewerId", new { ViewerId = viewerId });
    }

    public async Task<ViewerPlatformStatus?> GetViewerPlatformStatusAsync(string username, string platform)
    {
        using var conn = CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<ViewerPlatformStatus>(
            "SELECT * FROM ViewerPlatformStatus WHERE Username = @Username COLLATE NOCASE AND Platform = @Platform COLLATE NOCASE",
            new { Username = username, Platform = platform });
    }

    public async Task SaveViewerPlatformStatusAsync(ViewerPlatformStatus status)
    {
        using var conn = CreateConnection();
        status.LastUpdatedAt = DateTime.UtcNow;

        var exists = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM ViewerPlatformStatus WHERE Username = @Username COLLATE NOCASE AND Platform = @Platform COLLATE NOCASE",
            status);

        if (exists > 0)
        {
            await conn.ExecuteAsync(@"
                UPDATE ViewerPlatformStatus SET
                    ViewerId = @ViewerId,
                    IsSubscriber = @IsSubscriber,
                    SubTier = @SubTier,
                    SubscriberMonths = @SubscriberMonths,
                    IsFollower = @IsFollower,
                    LastUpdatedAt = @LastUpdatedAt
                WHERE Username = @Username COLLATE NOCASE AND Platform = @Platform COLLATE NOCASE", status);
        }
        else
        {
            await conn.ExecuteAsync(@"
                INSERT INTO ViewerPlatformStatus (Id, Username, Platform, ViewerId, IsSubscriber, SubTier,
                    SubscriberMonths, IsFollower, LastUpdatedAt)
                VALUES (@Id, @Username, @Platform, @ViewerId, @IsSubscriber, @SubTier,
                    @SubscriberMonths, @IsFollower, @LastUpdatedAt)", status);
        }
    }

    public async Task<IEnumerable<ViewerPlatformStatus>> GetViewerStatusesByViewerIdAsync(string viewerId)
    {
        using var conn = CreateConnection();
        return await conn.QueryAsync<ViewerPlatformStatus>(
            "SELECT * FROM ViewerPlatformStatus WHERE ViewerId = @ViewerId",
            new { ViewerId = viewerId });
    }

    public async Task<ViewerPreferences?> GetViewerPreferencesAsync(string viewerId)
    {
        using var conn = CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<ViewerPreferences>(
            "SELECT * FROM ViewerPreferences WHERE ViewerId = @ViewerId",
            new { ViewerId = viewerId });
    }

    public async Task SaveViewerPreferencesAsync(ViewerPreferences preferences)
    {
        using var conn = CreateConnection();
        preferences.UpdatedAt = DateTime.UtcNow;

        var exists = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM ViewerPreferences WHERE ViewerId = @ViewerId",
            preferences);

        if (exists > 0)
        {
            await conn.ExecuteAsync(@"
                UPDATE ViewerPreferences SET
                    MultiPlatformBonusOptIn = @MultiPlatformBonusOptIn,
                    UpdatedAt = @UpdatedAt
                WHERE ViewerId = @ViewerId", preferences);
        }
        else
        {
            await conn.ExecuteAsync(@"
                INSERT INTO ViewerPreferences (Id, ViewerId, MultiPlatformBonusOptIn, UpdatedAt)
                VALUES (@Id, @ViewerId, @MultiPlatformBonusOptIn, @UpdatedAt)", preferences);
        }
    }

    public async Task<IEnumerable<SocialGoal>> GetSocialGoalsAsync()
    {
        using var conn = CreateConnection();
        return await conn.QueryAsync<SocialGoal>(
            "SELECT * FROM SocialGoals ORDER BY SortOrder ASC");
    }

    public async Task<SocialGoal?> GetSocialGoalAsync(Guid id)
    {
        using var conn = CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<SocialGoal>(
            "SELECT * FROM SocialGoals WHERE Id = @Id", new { Id = id.ToString() });
    }

    public async Task SaveSocialGoalAsync(SocialGoal goal)
    {
        using var conn = CreateConnection();
        goal.UpdatedAt = DateTime.UtcNow;

        var exists = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM SocialGoals WHERE Id = @Id", goal);

        if (exists > 0)
        {
            await conn.ExecuteAsync(@"
                UPDATE SocialGoals SET
                    Name = @Name, Description = @Description, GoalType = @GoalType,
                    TargetCount = @TargetCount, RequiredPlatforms = @RequiredPlatforms,
                    RewardType = @RewardType, RewardValue = @RewardValue,
                    IsEnabled = @IsEnabled, SortOrder = @SortOrder, UpdatedAt = @UpdatedAt
                WHERE Id = @Id", goal);
        }
        else
        {
            goal.CreatedAt = DateTime.UtcNow;
            await conn.ExecuteAsync(@"
                INSERT INTO SocialGoals (Id, Name, Description, GoalType, TargetCount, RequiredPlatforms,
                    RewardType, RewardValue, IsEnabled, SortOrder, CreatedAt, UpdatedAt)
                VALUES (@Id, @Name, @Description, @GoalType, @TargetCount, @RequiredPlatforms,
                    @RewardType, @RewardValue, @IsEnabled, @SortOrder, @CreatedAt, @UpdatedAt)", goal);
        }
    }

    public async Task DeleteSocialGoalAsync(Guid id)
    {
        using var conn = CreateConnection();
        await conn.ExecuteAsync("DELETE FROM SocialGoals WHERE Id = @Id", new { Id = id.ToString() });
    }

    public async Task<ViewerGoalCompletion?> GetViewerGoalCompletionAsync(Guid goalId, string viewerId)
    {
        using var conn = CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<ViewerGoalCompletion>(
            "SELECT * FROM ViewerGoalCompletions WHERE GoalId = @GoalId AND ViewerId = @ViewerId",
            new { GoalId = goalId.ToString(), ViewerId = viewerId });
    }

    public async Task SaveViewerGoalCompletionAsync(ViewerGoalCompletion completion)
    {
        using var conn = CreateConnection();

        var exists = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM ViewerGoalCompletions WHERE GoalId = @GoalId AND ViewerId = @ViewerId",
            completion);

        if (exists > 0)
        {
            await conn.ExecuteAsync(@"
                UPDATE ViewerGoalCompletions SET
                    Username = @Username,
                    Platform = @Platform,
                    CompletedAt = @CompletedAt,
                    RewardApplied = @RewardApplied
                WHERE GoalId = @GoalId AND ViewerId = @ViewerId", completion);
        }
        else
        {
            completion.CompletedAt = DateTime.UtcNow;
            await conn.ExecuteAsync(@"
                INSERT INTO ViewerGoalCompletions (Id, GoalId, ViewerId, Username, Platform, CompletedAt, RewardApplied)
                VALUES (@Id, @GoalId, @ViewerId, @Username, @Platform, @CompletedAt, @RewardApplied)", completion);
        }
    }

    public async Task<int> GetGoalCompletionCountAsync(Guid goalId)
    {
        using var conn = CreateConnection();
        return await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM ViewerGoalCompletions WHERE GoalId = @GoalId",
            new { GoalId = goalId.ToString() });
    }

    public async Task<IEnumerable<ViewerGoalCompletion>> GetViewerGoalCompletionsByViewerIdAsync(string viewerId)
    {
        using var conn = CreateConnection();
        return await conn.QueryAsync<ViewerGoalCompletion>(
            "SELECT * FROM ViewerGoalCompletions WHERE ViewerId = @ViewerId ORDER BY CompletedAt DESC",
            new { ViewerId = viewerId });
    }

    #endregion

    #region Viewer Streak Operations

    public async Task<ViewerStreak?> GetViewerStreakAsync(string username, string platform)
    {
        using var conn = CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<ViewerStreak>(
            "SELECT * FROM ViewerStreaks WHERE Username = @Username COLLATE NOCASE AND Platform = @Platform COLLATE NOCASE",
            new { Username = username, Platform = platform });
    }

    public async Task SaveViewerStreakAsync(ViewerStreak streak)
    {
        using var conn = CreateConnection();
        streak.UpdatedAt = DateTime.UtcNow;

        var exists = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM ViewerStreaks WHERE Username = @Username COLLATE NOCASE AND Platform = @Platform COLLATE NOCASE",
            streak);

        if (exists > 0)
        {
            await conn.ExecuteAsync(@"
                UPDATE ViewerStreaks SET
                    CurrentStreak = @CurrentStreak,
                    LongestStreak = @LongestStreak,
                    LastStreamDate = @LastStreamDate,
                    LastDailyClaimDate = @LastDailyClaimDate,
                    HasChattedThisStream = @HasChattedThisStream,
                    UpdatedAt = @UpdatedAt
                WHERE Username = @Username COLLATE NOCASE AND Platform = @Platform COLLATE NOCASE", streak);
        }
        else
        {
            streak.CreatedAt = DateTime.UtcNow;
            await conn.ExecuteAsync(@"
                INSERT INTO ViewerStreaks (Id, Username, Platform, CurrentStreak, LongestStreak,
                    LastStreamDate, LastDailyClaimDate, HasChattedThisStream, CreatedAt, UpdatedAt)
                VALUES (@Id, @Username, @Platform, @CurrentStreak, @LongestStreak,
                    @LastStreamDate, @LastDailyClaimDate, @HasChattedThisStream, @CreatedAt, @UpdatedAt)", streak);
        }
    }

    #endregion

    #region Loyalty Tier Operations

    public async Task<IEnumerable<LoyaltyTier>> GetLoyaltyTiersAsync()
    {
        using var conn = CreateConnection();
        return await conn.QueryAsync<LoyaltyTier>(
            "SELECT * FROM LoyaltyTiers ORDER BY MinPoints ASC");
    }

    public async Task<LoyaltyTier?> GetLoyaltyTierAsync(string id)
    {
        using var conn = CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<LoyaltyTier>(
            "SELECT * FROM LoyaltyTiers WHERE Id = @Id", new { Id = id });
    }

    public async Task SaveLoyaltyTierAsync(LoyaltyTier tier)
    {
        using var conn = CreateConnection();
        tier.UpdatedAt = DateTime.UtcNow;

        var exists = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM LoyaltyTiers WHERE Id = @Id", tier);

        if (exists > 0)
        {
            await conn.ExecuteAsync(@"
                UPDATE LoyaltyTiers SET
                    Name = @Name, MinPoints = @MinPoints, EarnMultiplier = @EarnMultiplier,
                    Icon = @Icon, Color = @Color, Perks = @Perks, SortOrder = @SortOrder, UpdatedAt = @UpdatedAt
                WHERE Id = @Id", tier);
        }
        else
        {
            tier.CreatedAt = DateTime.UtcNow;
            await conn.ExecuteAsync(@"
                INSERT INTO LoyaltyTiers (Id, Name, MinPoints, EarnMultiplier, Icon, Color, Perks, SortOrder, CreatedAt, UpdatedAt)
                VALUES (@Id, @Name, @MinPoints, @EarnMultiplier, @Icon, @Color, @Perks, @SortOrder, @CreatedAt, @UpdatedAt)", tier);
        }
    }

    public async Task DeleteLoyaltyTierAsync(string id)
    {
        using var conn = CreateConnection();
        await conn.ExecuteAsync("DELETE FROM LoyaltyTiers WHERE Id = @Id", new { Id = id });
    }

    #endregion

    #region Currency Game Setting Operations

    public async Task<CurrencyGameSetting?> GetCurrencyGameSettingAsync(string gameType)
    {
        using var conn = CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<CurrencyGameSetting>(
            "SELECT * FROM CurrencyGameSettings WHERE GameType = @GameType",
            new { GameType = gameType });
    }

    public async Task<IEnumerable<CurrencyGameSetting>> GetCurrencyGameSettingsAsync()
    {
        using var conn = CreateConnection();
        return await conn.QueryAsync<CurrencyGameSetting>(
            "SELECT * FROM CurrencyGameSettings ORDER BY GameType ASC");
    }

    public async Task SaveCurrencyGameSettingAsync(CurrencyGameSetting setting)
    {
        using var conn = CreateConnection();
        setting.UpdatedAt = DateTime.UtcNow;

        var exists = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM CurrencyGameSettings WHERE Id = @Id", setting);

        if (exists > 0)
        {
            await conn.ExecuteAsync(@"
                UPDATE CurrencyGameSettings SET
                    GameType = @GameType, IsEnabled = @IsEnabled, MinBet = @MinBet, MaxBet = @MaxBet,
                    CooldownSeconds = @CooldownSeconds, HouseEdgePercent = @HouseEdgePercent,
                    CustomConfig = @CustomConfig, UpdatedAt = @UpdatedAt
                WHERE Id = @Id", setting);
        }
        else
        {
            setting.CreatedAt = DateTime.UtcNow;
            await conn.ExecuteAsync(@"
                INSERT INTO CurrencyGameSettings (Id, GameType, IsEnabled, MinBet, MaxBet, CooldownSeconds,
                    HouseEdgePercent, CustomConfig, CreatedAt, UpdatedAt)
                VALUES (@Id, @GameType, @IsEnabled, @MinBet, @MaxBet, @CooldownSeconds,
                    @HouseEdgePercent, @CustomConfig, @CreatedAt, @UpdatedAt)", setting);
        }
    }

    #endregion

    #region Viewer Perk Operations

    public async Task<IEnumerable<ViewerPerk>> GetActiveViewerPerksAsync(string username, string platform)
    {
        using var conn = CreateConnection();
        return await conn.QueryAsync<ViewerPerk>(
            "SELECT * FROM ViewerPerks WHERE Username = @Username COLLATE NOCASE AND Platform = @Platform COLLATE NOCASE AND IsActive = 1 AND ExpiresAt > @Now",
            new { Username = username, Platform = platform, Now = DateTime.UtcNow });
    }

    public async Task<IEnumerable<ViewerPerk>> GetViewerPerksByTypeAsync(string username, string platform, string perkType)
    {
        using var conn = CreateConnection();
        return await conn.QueryAsync<ViewerPerk>(
            "SELECT * FROM ViewerPerks WHERE Username = @Username COLLATE NOCASE AND Platform = @Platform COLLATE NOCASE AND PerkType = @PerkType AND IsActive = 1 AND ExpiresAt > @Now",
            new { Username = username, Platform = platform, PerkType = perkType, Now = DateTime.UtcNow });
    }

    public async Task SaveViewerPerkAsync(ViewerPerk perk)
    {
        using var conn = CreateConnection();

        var exists = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM ViewerPerks WHERE Id = @Id", perk);

        if (exists > 0)
        {
            await conn.ExecuteAsync(@"
                UPDATE ViewerPerks SET
                    IsActive = @IsActive, ExpiresAt = @ExpiresAt
                WHERE Id = @Id", perk);
        }
        else
        {
            perk.CreatedAt = DateTime.UtcNow;
            await conn.ExecuteAsync(@"
                INSERT INTO ViewerPerks (Id, Username, Platform, PerkType, PerkData, PurchasedAt, ExpiresAt, IsActive, Cost, CreatedAt)
                VALUES (@Id, @Username, @Platform, @PerkType, @PerkData, @PurchasedAt, @ExpiresAt, @IsActive, @Cost, @CreatedAt)", perk);
        }
    }

    public async Task ExpireViewerPerksAsync()
    {
        using var conn = CreateConnection();
        await conn.ExecuteAsync(
            "UPDATE ViewerPerks SET IsActive = 0 WHERE IsActive = 1 AND ExpiresAt <= @Now",
            new { Now = DateTime.UtcNow });
    }

    #endregion

    #region Currency Boost Operations

    public async Task<CurrencyBoost?> GetCurrencyBoostAsync(string id)
    {
        using var conn = CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<CurrencyBoost>(
            "SELECT * FROM CurrencyBoosts WHERE Id = @Id", new { Id = id });
    }

    public async Task<IEnumerable<CurrencyBoost>> GetActiveBoostsAsync(string? platform = null)
    {
        using var conn = CreateConnection();
        if (platform != null)
        {
            return await conn.QueryAsync<CurrencyBoost>(
                "SELECT * FROM CurrencyBoosts WHERE IsActive = 1 AND (Platform IS NULL OR Platform = @Platform) ORDER BY StartedAt DESC",
                new { Platform = platform });
        }
        return await conn.QueryAsync<CurrencyBoost>(
            "SELECT * FROM CurrencyBoosts WHERE IsActive = 1 ORDER BY StartedAt DESC");
    }

    public async Task<IEnumerable<CurrencyBoost>> GetBoostHistoryAsync(int limit = 50)
    {
        using var conn = CreateConnection();
        return await conn.QueryAsync<CurrencyBoost>(
            "SELECT * FROM CurrencyBoosts ORDER BY StartedAt DESC LIMIT @Limit",
            new { Limit = limit });
    }

    public async Task SaveCurrencyBoostAsync(CurrencyBoost boost)
    {
        using var conn = CreateConnection();
        var exists = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM CurrencyBoosts WHERE Id = @Id", new { boost.Id });

        if (exists > 0)
        {
            await conn.ExecuteAsync(@"
                UPDATE CurrencyBoosts SET
                    Multiplier = @Multiplier, DurationMinutes = @DurationMinutes,
                    StartedAt = @StartedAt, EndsAt = @EndsAt, Reason = @Reason,
                    Source = @Source, IsActive = @IsActive, Platform = @Platform
                WHERE Id = @Id", boost);
        }
        else
        {
            boost.CreatedAt = DateTime.UtcNow;
            await conn.ExecuteAsync(@"
                INSERT INTO CurrencyBoosts (Id, Multiplier, DurationMinutes, StartedAt, EndsAt, Reason, Source, IsActive, Platform, CreatedAt)
                VALUES (@Id, @Multiplier, @DurationMinutes, @StartedAt, @EndsAt, @Reason, @Source, @IsActive, @Platform, @CreatedAt)", boost);
        }
    }

    public async Task ExpireBoostsAsync()
    {
        using var conn = CreateConnection();
        await conn.ExecuteAsync(
            "UPDATE CurrencyBoosts SET IsActive = 0 WHERE IsActive = 1 AND EndsAt <= @Now",
            new { Now = DateTime.UtcNow });
    }

    #endregion

    #region Currency Auction Operations

    public async Task<CurrencyAuction?> GetCurrencyAuctionAsync(string id)
    {
        using var conn = CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<CurrencyAuction>(
            "SELECT * FROM CurrencyAuctions WHERE Id = @Id", new { Id = id });
    }

    public async Task<CurrencyAuction?> GetActiveAuctionAsync()
    {
        using var conn = CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<CurrencyAuction>(
            "SELECT * FROM CurrencyAuctions WHERE Status = 'Active' ORDER BY StartedAt DESC LIMIT 1");
    }

    public async Task<IEnumerable<CurrencyAuction>> GetAuctionHistoryAsync(int limit = 20)
    {
        using var conn = CreateConnection();
        return await conn.QueryAsync<CurrencyAuction>(
            "SELECT * FROM CurrencyAuctions ORDER BY StartedAt DESC LIMIT @Limit",
            new { Limit = limit });
    }

    public async Task SaveCurrencyAuctionAsync(CurrencyAuction auction)
    {
        using var conn = CreateConnection();
        var exists = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM CurrencyAuctions WHERE Id = @Id", new { auction.Id });

        if (exists > 0)
        {
            auction.UpdatedAt = DateTime.UtcNow;
            await conn.ExecuteAsync(@"
                UPDATE CurrencyAuctions SET
                    ItemName = @ItemName, Description = @Description, StartingBid = @StartingBid,
                    MinBidIncrement = @MinBidIncrement, CurrentBid = @CurrentBid,
                    CurrentBidder = @CurrentBidder, CurrentBidderPlatform = @CurrentBidderPlatform,
                    DurationMinutes = @DurationMinutes, EndsAt = @EndsAt, Status = @Status,
                    AntiSnipeEnabled = @AntiSnipeEnabled, AntiSnipeSeconds = @AntiSnipeSeconds,
                    UpdatedAt = @UpdatedAt
                WHERE Id = @Id", auction);
        }
        else
        {
            auction.CreatedAt = DateTime.UtcNow;
            auction.UpdatedAt = DateTime.UtcNow;
            await conn.ExecuteAsync(@"
                INSERT INTO CurrencyAuctions (Id, ItemName, Description, StartingBid, MinBidIncrement, CurrentBid,
                    CurrentBidder, CurrentBidderPlatform, DurationMinutes, StartedAt, EndsAt, Status,
                    AntiSnipeEnabled, AntiSnipeSeconds, CreatedAt, UpdatedAt)
                VALUES (@Id, @ItemName, @Description, @StartingBid, @MinBidIncrement, @CurrentBid,
                    @CurrentBidder, @CurrentBidderPlatform, @DurationMinutes, @StartedAt, @EndsAt, @Status,
                    @AntiSnipeEnabled, @AntiSnipeSeconds, @CreatedAt, @UpdatedAt)", auction);
        }
    }

    public async Task SaveCurrencyAuctionBidAsync(CurrencyAuctionBid bid)
    {
        using var conn = CreateConnection();
        await conn.ExecuteAsync(@"
            INSERT INTO CurrencyAuctionBids (Id, AuctionId, Username, Platform, Amount, BidAt)
            VALUES (@Id, @AuctionId, @Username, @Platform, @Amount, @BidAt)", bid);
    }

    public async Task<IEnumerable<CurrencyAuctionBid>> GetAuctionBidsAsync(string auctionId)
    {
        using var conn = CreateConnection();
        return await conn.QueryAsync<CurrencyAuctionBid>(
            "SELECT * FROM CurrencyAuctionBids WHERE AuctionId = @AuctionId ORDER BY BidAt DESC",
            new { AuctionId = auctionId });
    }

    #endregion

    #region Bank Account Operations

    public async Task<UserBankAccount?> GetUserBankAccountAsync(string username, string platform)
    {
        using var conn = CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<UserBankAccount>(
            "SELECT * FROM UserBankAccounts WHERE Username = @Username COLLATE NOCASE AND Platform = @Platform COLLATE NOCASE",
            new { Username = username, Platform = platform });
    }

    public async Task<IEnumerable<UserBankAccount>> GetAllBankAccountsAsync()
    {
        using var conn = CreateConnection();
        return await conn.QueryAsync<UserBankAccount>(
            "SELECT * FROM UserBankAccounts WHERE BankBalance > 0");
    }

    public async Task SaveUserBankAccountAsync(UserBankAccount account)
    {
        using var conn = CreateConnection();
        var exists = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM UserBankAccounts WHERE Username = @Username COLLATE NOCASE AND Platform = @Platform COLLATE NOCASE",
            new { account.Username, account.Platform });

        if (exists > 0)
        {
            account.UpdatedAt = DateTime.UtcNow;
            await conn.ExecuteAsync(@"
                UPDATE UserBankAccounts SET
                    BankBalance = @BankBalance, TotalDeposited = @TotalDeposited,
                    TotalWithdrawn = @TotalWithdrawn, TotalInterestEarned = @TotalInterestEarned,
                    LastInterestPayout = @LastInterestPayout, UpdatedAt = @UpdatedAt
                WHERE Username = @Username COLLATE NOCASE AND Platform = @Platform COLLATE NOCASE", account);
        }
        else
        {
            account.CreatedAt = DateTime.UtcNow;
            account.UpdatedAt = DateTime.UtcNow;
            await conn.ExecuteAsync(@"
                INSERT INTO UserBankAccounts (Id, Username, Platform, BankBalance, TotalDeposited,
                    TotalWithdrawn, TotalInterestEarned, LastInterestPayout, CreatedAt, UpdatedAt)
                VALUES (@Id, @Username, @Platform, @BankBalance, @TotalDeposited,
                    @TotalWithdrawn, @TotalInterestEarned, @LastInterestPayout, @CreatedAt, @UpdatedAt)", account);
        }
    }

    #endregion

    #region Currency Decay Operations

    public async Task<IEnumerable<UserCurrency>> GetInactiveUsersForDecayAsync(DateTime cutoffDate)
    {
        using var conn = CreateConnection();
        return await conn.QueryAsync<UserCurrency>(
            "SELECT * FROM UserCurrency WHERE Balance > 0 AND UpdatedAt < @CutoffDate",
            // Bind DateTime directly: UserCurrency.UpdatedAt is stored via bound
            // parameters ("yyyy-MM-dd HH:mm:ss" TEXT); an "o"-format string ("T" +
            // "Z") compares wrongly against it for same-day rows.
            new { CutoffDate = cutoffDate });
    }

    #endregion

    #region Command Operations

    public async Task<Command?> GetCommandAsync(string id)
    {
        using var conn = CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<Command>(
            "SELECT * FROM Commands WHERE Id = @Id", new { Id = id });
    }

    public async Task<Command?> GetCommandByNameAsync(string name)
    {
        using var conn = CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<Command>(
            "SELECT * FROM Commands WHERE Name = @Name COLLATE NOCASE", new { Name = name });
    }

    public async Task<IEnumerable<Command>> GetCommandsAsync(bool? enabledOnly = null, bool? builtInOnly = null)
    {
        using var conn = CreateConnection();

        var sql = "SELECT * FROM Commands WHERE 1=1";
        if (enabledOnly.HasValue)
            sql += " AND IsEnabled = " + (enabledOnly.Value ? 1 : 0);
        if (builtInOnly.HasValue)
            sql += " AND IsBuiltIn = " + (builtInOnly.Value ? 1 : 0);
        sql += " ORDER BY Name";

        return await conn.QueryAsync<Command>(sql);
    }

    /// <summary>
    /// Commands visible to a specific Discord bot: rows scoped to that bot via
    /// the BotId column plus all unscoped (BotId IS NULL) commands.
    /// </summary>
    public async Task<IEnumerable<Command>> GetCommandsByBotAsync(string botId)
    {
        using var conn = CreateConnection();
        return await conn.QueryAsync<Command>(
            "SELECT * FROM Commands WHERE BotId IS NULL OR BotId = @BotId ORDER BY Name",
            new { BotId = botId });
    }

    public async Task SaveCommandAsync(Command command)
    {
        using var conn = CreateConnection();

        var existing = await conn.QueryFirstOrDefaultAsync<Command>(
            "SELECT * FROM Commands WHERE Id = @Id", new { command.Id });

        if (existing == null)
        {
            await conn.ExecuteAsync(@"
                INSERT INTO Commands (
                    Id, Name, Response, Permission, GlobalCooldown, UserCooldown,
                    IsEnabled, IsBuiltIn, EnabledOnKick, EnabledOnTwitch,
                    UsageCount, LastUsedAt, CreatedAt, UpdatedAt, BotId,
                    ResponsesJson, ActionsJson
                ) VALUES (
                    @Id, @Name, @Response, @Permission, @GlobalCooldown, @UserCooldown,
                    @IsEnabled, @IsBuiltIn, @EnabledOnKick, @EnabledOnTwitch,
                    @UsageCount, @LastUsedAt, @CreatedAt, @UpdatedAt, @BotId,
                    @ResponsesJson, @ActionsJson
                )", command);
        }
        else
        {
            await conn.ExecuteAsync(@"
                UPDATE Commands SET
                    Name = @Name,
                    Response = @Response,
                    Permission = @Permission,
                    GlobalCooldown = @GlobalCooldown,
                    UserCooldown = @UserCooldown,
                    IsEnabled = @IsEnabled,
                    EnabledOnKick = @EnabledOnKick,
                    EnabledOnTwitch = @EnabledOnTwitch,
                    BotId = @BotId,
                    ResponsesJson = @ResponsesJson,
                    ActionsJson = @ActionsJson,
                    UpdatedAt = @UpdatedAt
                WHERE Id = @Id", command);
        }
    }

    public async Task DeleteCommandAsync(string id)
    {
        using var conn = CreateConnection();
        await conn.ExecuteAsync("DELETE FROM Commands WHERE Id = @Id AND IsBuiltIn = 0", new { Id = id });
    }

    public async Task IncrementCommandUsageAsync(string id)
    {
        using var conn = CreateConnection();
        await conn.ExecuteAsync(@"
            UPDATE Commands
            SET UsageCount = UsageCount + 1,
                LastUsedAt = @Now
            WHERE Id = @Id",
            new { Id = id, Now = DateTime.UtcNow });
    }

    public async Task<CommandCooldown?> GetCommandCooldownAsync(string commandId, string? username, string platform)
    {
        using var conn = CreateConnection();

        var sql = @"
            SELECT * FROM CommandCooldowns
            WHERE CommandId = @CommandId
              AND Platform = @Platform
              AND ExpiresAt > @Now";

        if (username == null)
            sql += " AND Username IS NULL";
        else
            sql += " AND Username = @Username";

        return await conn.QueryFirstOrDefaultAsync<CommandCooldown>(sql,
            new { CommandId = commandId, Username = username, Platform = platform, Now = DateTime.UtcNow });
    }

    public async Task SaveCommandCooldownAsync(CommandCooldown cooldown)
    {
        using var conn = CreateConnection();
        await conn.ExecuteAsync(@"
            INSERT INTO CommandCooldowns (
                Id, CommandId, CommandName, Username, Platform, ExpiresAt, CreatedAt
            ) VALUES (
                @Id, @CommandId, @CommandName, @Username, @Platform, @ExpiresAt, @CreatedAt
            )", cooldown);
    }

    public async Task DeleteExpiredCooldownsAsync()
    {
        using var conn = CreateConnection();
        await conn.ExecuteAsync(
            "DELETE FROM CommandCooldowns WHERE ExpiresAt <= @Now",
            new { Now = DateTime.UtcNow });
    }

    // Command Timers (scheduled command firing)
    public async Task<IEnumerable<CommandTimer>> GetCommandTimersAsync()
    {
        using var conn = CreateConnection();
        return await conn.QueryAsync<CommandTimer>(
            "SELECT * FROM CommandTimers ORDER BY Name");
    }

    public async Task SaveCommandTimerAsync(CommandTimer timer)
    {
        using var conn = CreateConnection();

        var existing = await conn.QueryFirstOrDefaultAsync<CommandTimer>(
            "SELECT * FROM CommandTimers WHERE Id = @Id", new { timer.Id });

        if (existing == null)
        {
            await conn.ExecuteAsync(@"
                INSERT INTO CommandTimers (
                    Id, Name, CommandName, IntervalMinutes, TargetPlatform,
                    BotId, IsEnabled, LastRunAt, CreatedAt, UpdatedAt
                ) VALUES (
                    @Id, @Name, @CommandName, @IntervalMinutes, @TargetPlatform,
                    @BotId, @IsEnabled, @LastRunAt, @CreatedAt, @UpdatedAt
                )", timer);
        }
        else
        {
            await conn.ExecuteAsync(@"
                UPDATE CommandTimers SET
                    Name = @Name,
                    CommandName = @CommandName,
                    IntervalMinutes = @IntervalMinutes,
                    TargetPlatform = @TargetPlatform,
                    BotId = @BotId,
                    IsEnabled = @IsEnabled,
                    LastRunAt = @LastRunAt,
                    UpdatedAt = @UpdatedAt
                WHERE Id = @Id", timer);
        }
    }

    public async Task DeleteCommandTimerAsync(string id)
    {
        using var conn = CreateConnection();
        await conn.ExecuteAsync("DELETE FROM CommandTimers WHERE Id = @Id", new { Id = id });
    }

    // Auto-Reply Rules
    public async Task<IEnumerable<AutoReplyRule>> GetAutoReplyRulesAsync()
    {
        using var conn = CreateConnection();
        return await conn.QueryAsync<AutoReplyRule>(
            "SELECT * FROM AutoReplyRules ORDER BY Priority ASC, Id ASC");
    }

    public async Task<IEnumerable<AutoReplyRule>> GetAutoReplyRulesByPlatformAndEventAsync(string platform, string eventType)
    {
        using var conn = CreateConnection();
        return await conn.QueryAsync<AutoReplyRule>(@"
            SELECT * FROM AutoReplyRules
            WHERE IsEnabled = 1
              AND (Platform = @Platform OR Platform = 'All')
              AND EventType = @EventType
            ORDER BY Priority ASC, Id ASC",
            new { Platform = platform, EventType = eventType });
    }

    public async Task<AutoReplyRule?> GetAutoReplyRuleAsync(int id)
    {
        using var conn = CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<AutoReplyRule>(
            "SELECT * FROM AutoReplyRules WHERE Id = @Id",
            new { Id = id });
    }

    public async Task<int> SaveAutoReplyRuleAsync(AutoReplyRule rule)
    {
        using var conn = CreateConnection();

        if (rule.Id == 0)
        {
            // Insert new rule
            rule.CreatedAt = DateTime.UtcNow;
            rule.UpdatedAt = DateTime.UtcNow;

            await conn.ExecuteAsync(@"
                INSERT INTO AutoReplyRules (
                    Name, Platform, EventType, IsEnabled, Priority,
                    ConditionJson, ReplyTemplate, CreatedAt, UpdatedAt,
                    TriggerCount, LastTriggeredAt
                ) VALUES (
                    @Name, @Platform, @EventType, @IsEnabled, @Priority,
                    @ConditionJson, @ReplyTemplate, @CreatedAt, @UpdatedAt,
                    @TriggerCount, @LastTriggeredAt
                )", rule);

            return (int)conn.QuerySingle<long>("SELECT last_insert_rowid()");
        }
        else
        {
            // Update existing rule
            rule.UpdatedAt = DateTime.UtcNow;

            await conn.ExecuteAsync(@"
                UPDATE AutoReplyRules SET
                    Name = @Name,
                    Platform = @Platform,
                    EventType = @EventType,
                    IsEnabled = @IsEnabled,
                    Priority = @Priority,
                    ConditionJson = @ConditionJson,
                    ReplyTemplate = @ReplyTemplate,
                    UpdatedAt = @UpdatedAt,
                    TriggerCount = @TriggerCount,
                    LastTriggeredAt = @LastTriggeredAt
                WHERE Id = @Id", rule);

            return rule.Id;
        }
    }

    public async Task DeleteAutoReplyRuleAsync(int id)
    {
        using var conn = CreateConnection();
        await conn.ExecuteAsync(
            "DELETE FROM AutoReplyRules WHERE Id = @Id",
            new { Id = id });
    }

    public async Task IncrementRuleTriggerCountAsync(int id)
    {
        using var conn = CreateConnection();
        await conn.ExecuteAsync(@"
            UPDATE AutoReplyRules SET
                TriggerCount = TriggerCount + 1,
                LastTriggeredAt = @Now
            WHERE Id = @Id",
            new { Id = id, Now = DateTime.UtcNow });
    }

    // Giveaways
    public async Task<IEnumerable<Giveaway>> GetGiveawaysAsync()
    {
        using var conn = CreateConnection();
        return await conn.QueryAsync<Giveaway>(
            "SELECT * FROM Giveaways ORDER BY CreatedAt DESC");
    }

    public async Task<Giveaway?> GetGiveawayAsync(Guid id)
    {
        using var conn = CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<Giveaway>(
            "SELECT * FROM Giveaways WHERE Id = @Id",
            new { Id = id.ToString() });
    }

    public async Task<Giveaway?> GetActiveGiveawayAsync()
    {
        using var conn = CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<Giveaway>(
            "SELECT * FROM Giveaways WHERE Status = @Status ORDER BY StartedAt DESC LIMIT 1",
            new { Status = (int)GiveawayStatus.Active });
    }

    public async Task SaveGiveawayAsync(Giveaway giveaway)
    {
        using var conn = CreateConnection();

        giveaway.UpdatedAt = DateTime.UtcNow;

        await conn.ExecuteAsync(@"
            INSERT OR REPLACE INTO Giveaways (
                Id, Name, Description, EntryKeyword, Status, CreatedAt, StartedAt, EndedAt,
                ScheduledEndTime, MaxEntries, WinnerCount, SubscriberBonusEnabled,
                SubscriberBonusEntries, MinWatchtimeMinutes, AllowedPlatforms, OddsBoostEnabled,
                FollowersOnly, SubscribersOnly, Winners, TotalEntries,
                UniqueParticipants, Metadata, UpdatedAt
            ) VALUES (
                @Id, @Name, @Description, @EntryKeyword, @Status, @CreatedAt, @StartedAt, @EndedAt,
                @ScheduledEndTime, @MaxEntries, @WinnerCount, @SubscriberBonusEnabled,
                @SubscriberBonusEntries, @MinWatchtimeMinutes, @AllowedPlatforms, @OddsBoostEnabled,
                @FollowersOnly, @SubscribersOnly, @Winners, @TotalEntries,
                @UniqueParticipants, @Metadata, @UpdatedAt
            )",
            new
            {
                Id = giveaway.Id.ToString(),
                giveaway.Name,
                giveaway.Description,
                giveaway.EntryKeyword,
                Status = (int)giveaway.Status,
                CreatedAt = giveaway.CreatedAt.ToString("o"),
                StartedAt = giveaway.StartedAt?.ToString("o"),
                EndedAt = giveaway.EndedAt?.ToString("o"),
                ScheduledEndTime = giveaway.ScheduledEndTime?.ToString("o"),
                giveaway.MaxEntries,
                giveaway.WinnerCount,
                SubscriberBonusEnabled = giveaway.SubscriberBonusEnabled ? 1 : 0,
                giveaway.SubscriberBonusEntries,
                giveaway.MinWatchtimeMinutes,
                giveaway.AllowedPlatforms,
                OddsBoostEnabled = giveaway.OddsBoostEnabled ? 1 : 0,
                FollowersOnly = giveaway.FollowersOnly ? 1 : 0,
                SubscribersOnly = giveaway.SubscribersOnly ? 1 : 0,
                giveaway.Winners,
                giveaway.TotalEntries,
                giveaway.UniqueParticipants,
                giveaway.Metadata,
                UpdatedAt = giveaway.UpdatedAt.ToString("o")
            });
    }

    public async Task DeleteGiveawayAsync(Guid id)
    {
        using var conn = CreateConnection();
        // Delete entries first (foreign key constraint)
        await conn.ExecuteAsync(
            "DELETE FROM GiveawayEntries WHERE GiveawayId = @Id",
            new { Id = id.ToString() });
        // Then delete giveaway
        await conn.ExecuteAsync(
            "DELETE FROM Giveaways WHERE Id = @Id",
            new { Id = id.ToString() });
    }

    // Giveaway Entries
    public async Task<IEnumerable<GiveawayEntry>> GetGiveawayEntriesAsync(Guid giveawayId)
    {
        using var conn = CreateConnection();
        return await conn.QueryAsync<GiveawayEntry>(
            "SELECT * FROM GiveawayEntries WHERE GiveawayId = @GiveawayId ORDER BY EnteredAt ASC",
            new { GiveawayId = giveawayId.ToString() });
    }

    public async Task<GiveawayEntry?> GetGiveawayEntryAsync(Guid giveawayId, string username, string platform)
    {
        using var conn = CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<GiveawayEntry>(@"
            SELECT * FROM GiveawayEntries
            WHERE GiveawayId = @GiveawayId
              AND Username = @Username
              AND Platform = @Platform",
            new { GiveawayId = giveawayId.ToString(), Username = username, Platform = platform });
    }

    public async Task SaveGiveawayEntryAsync(GiveawayEntry entry)
    {
        using var conn = CreateConnection();

        await conn.ExecuteAsync(@"
            INSERT OR REPLACE INTO GiveawayEntries (
                Id, GiveawayId, Username, Platform, PlatformUserId, EnteredAt,
                EntryCount, IsSubscriber, IsFollower, WatchtimeMinutes,
                Message, IsWinner, WonAt
            ) VALUES (
                @Id, @GiveawayId, @Username, @Platform, @PlatformUserId, @EnteredAt,
                @EntryCount, @IsSubscriber, @IsFollower, @WatchtimeMinutes,
                @Message, @IsWinner, @WonAt
            )",
            new
            {
                Id = entry.Id.ToString(),
                GiveawayId = entry.GiveawayId.ToString(),
                entry.Username,
                entry.Platform,
                entry.PlatformUserId,
                EnteredAt = entry.EnteredAt.ToString("o"),
                entry.EntryCount,
                IsSubscriber = entry.IsSubscriber ? 1 : 0,
                IsFollower = entry.IsFollower ? 1 : 0,
                entry.WatchtimeMinutes,
                entry.Message,
                IsWinner = entry.IsWinner ? 1 : 0,
                WonAt = entry.WonAt?.ToString("o")
            });
    }

    public async Task<int> GetGiveawayEntryCountAsync(Guid giveawayId)
    {
        using var conn = CreateConnection();
        return await conn.QuerySingleAsync<int>(
            "SELECT COALESCE(SUM(EntryCount), 0) FROM GiveawayEntries WHERE GiveawayId = @GiveawayId",
            new { GiveawayId = giveawayId.ToString() });
    }

    public async Task<int> GetGiveawayUniqueParticipantsAsync(Guid giveawayId)
    {
        using var conn = CreateConnection();
        return await conn.QuerySingleAsync<int>(
            "SELECT COUNT(DISTINCT Username || Platform) FROM GiveawayEntries WHERE GiveawayId = @GiveawayId",
            new { GiveawayId = giveawayId.ToString() });
    }

    public async Task<IEnumerable<GiveawayEntry>> GetGiveawayEntriesForUserAsync(string username, string platform)
    {
        using var conn = CreateConnection();
        return await conn.QueryAsync<GiveawayEntry>(@"
            SELECT * FROM GiveawayEntries
            WHERE Username = @Username
              AND Platform = @Platform
            ORDER BY EnteredAt DESC LIMIT 100",
            new { Username = username, Platform = platform });
    }

    // Polls
    public async Task<IEnumerable<Poll>> GetPollsAsync()
    {
        using var conn = CreateConnection();
        return await conn.QueryAsync<Poll>(
            "SELECT * FROM Polls ORDER BY CreatedAt DESC");
    }

    public async Task<Poll?> GetPollAsync(Guid id)
    {
        using var conn = CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<Poll>(
            "SELECT * FROM Polls WHERE Id = @Id",
            new { Id = id.ToString() });
    }

    public async Task<Poll?> GetActivePollAsync()
    {
        using var conn = CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<Poll>(
            "SELECT * FROM Polls WHERE Status = @Status ORDER BY StartedAt DESC LIMIT 1",
            new { Status = (int)PollStatus.Active });
    }

    public async Task SavePollAsync(Poll poll)
    {
        using var conn = CreateConnection();

        poll.UpdatedAt = DateTime.UtcNow;

        await conn.ExecuteAsync(@"
            INSERT OR REPLACE INTO Polls (
                Id, Question, Options, Status, CreatedAt, StartedAt, EndedAt,
                ScheduledEndTime, CreatorUsername, TotalVotes, AllowMultipleVotes,
                AllowedPlatforms, FollowersOnly, SubscribersOnly, MinWatchtimeMinutes,
                VoteResults, Metadata, UpdatedAt
            ) VALUES (
                @Id, @Question, @Options, @Status, @CreatedAt, @StartedAt, @EndedAt,
                @ScheduledEndTime, @CreatorUsername, @TotalVotes, @AllowMultipleVotes,
                @AllowedPlatforms, @FollowersOnly, @SubscribersOnly, @MinWatchtimeMinutes,
                @VoteResults, @Metadata, @UpdatedAt
            )",
            new
            {
                Id = poll.Id.ToString(),
                poll.Question,
                poll.Options,
                Status = (int)poll.Status,
                CreatedAt = poll.CreatedAt.ToString("o"),
                StartedAt = poll.StartedAt?.ToString("o"),
                EndedAt = poll.EndedAt?.ToString("o"),
                ScheduledEndTime = poll.ScheduledEndTime?.ToString("o"),
                poll.CreatorUsername,
                poll.TotalVotes,
                AllowMultipleVotes = poll.AllowMultipleVotes ? 1 : 0,
                poll.AllowedPlatforms,
                FollowersOnly = poll.FollowersOnly ? 1 : 0,
                SubscribersOnly = poll.SubscribersOnly ? 1 : 0,
                poll.MinWatchtimeMinutes,
                poll.VoteResults,
                poll.Metadata,
                UpdatedAt = poll.UpdatedAt.ToString("o")
            });
    }

    public async Task DeletePollAsync(Guid id)
    {
        using var conn = CreateConnection();
        // Delete votes first (foreign key constraint)
        await conn.ExecuteAsync(
            "DELETE FROM PollVotes WHERE PollId = @Id",
            new { Id = id.ToString() });
        // Then delete poll
        await conn.ExecuteAsync(
            "DELETE FROM Polls WHERE Id = @Id",
            new { Id = id.ToString() });
    }

    // Poll Votes
    public async Task<IEnumerable<PollVote>> GetPollVotesAsync(Guid pollId)
    {
        using var conn = CreateConnection();
        return await conn.QueryAsync<PollVote>(
            "SELECT * FROM PollVotes WHERE PollId = @PollId ORDER BY VotedAt ASC",
            new { PollId = pollId.ToString() });
    }

    public async Task<PollVote?> GetPollVoteAsync(Guid pollId, string username, string platform)
    {
        using var conn = CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<PollVote>(@"
            SELECT * FROM PollVotes
            WHERE PollId = @PollId
              AND Username = @Username
              AND Platform = @Platform",
            new { PollId = pollId.ToString(), Username = username, Platform = platform });
    }

    public async Task SavePollVoteAsync(PollVote vote)
    {
        using var conn = CreateConnection();

        await conn.ExecuteAsync(@"
            INSERT OR REPLACE INTO PollVotes (
                Id, PollId, Username, Platform, PlatformUserId, OptionIndex,
                VotedAt, IsSubscriber, IsFollower, WatchtimeMinutes
            ) VALUES (
                @Id, @PollId, @Username, @Platform, @PlatformUserId, @OptionIndex,
                @VotedAt, @IsSubscriber, @IsFollower, @WatchtimeMinutes
            )",
            new
            {
                Id = vote.Id.ToString(),
                PollId = vote.PollId.ToString(),
                vote.Username,
                vote.Platform,
                vote.PlatformUserId,
                vote.OptionIndex,
                VotedAt = vote.VotedAt.ToString("o"),
                IsSubscriber = vote.IsSubscriber ? 1 : 0,
                IsFollower = vote.IsFollower ? 1 : 0,
                vote.WatchtimeMinutes
            });
    }

    public async Task<int> GetPollVoteCountAsync(Guid pollId)
    {
        using var conn = CreateConnection();
        return await conn.QuerySingleAsync<int>(
            "SELECT COUNT(*) FROM PollVotes WHERE PollId = @PollId",
            new { PollId = pollId.ToString() });
    }

    public async Task<Dictionary<int, int>> GetPollVoteResultsAsync(Guid pollId)
    {
        using var conn = CreateConnection();
        var results = await conn.QueryAsync<(int OptionIndex, int Count)>(
            "SELECT OptionIndex, COUNT(*) as Count FROM PollVotes WHERE PollId = @PollId GROUP BY OptionIndex",
            new { PollId = pollId.ToString() });
        return results.ToDictionary(r => r.OptionIndex, r => r.Count);
    }

    // User Watchtime
    public async Task<UserWatchtime?> GetUserWatchtimeAsync(string username, string platform)
    {
        using var conn = CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<UserWatchtime>(
            "SELECT * FROM UserWatchtime WHERE Username = @Username COLLATE NOCASE AND Platform = @Platform COLLATE NOCASE",
            new { Username = username, Platform = platform });
    }

    public async Task<IEnumerable<UserWatchtime>> GetAllUserWatchtimeAsync()
    {
        using var conn = CreateConnection();
        return await conn.QueryAsync<UserWatchtime>(
            "SELECT * FROM UserWatchtime ORDER BY TotalMinutes DESC");
    }

    public async Task<IEnumerable<UserWatchtime>> GetTopWatchtimeUsersAsync(int limit = 10)
    {
        using var conn = CreateConnection();
        return await conn.QueryAsync<UserWatchtime>(
            "SELECT * FROM UserWatchtime ORDER BY TotalMinutes DESC LIMIT @Limit",
            new { Limit = limit });
    }

    public async Task<IEnumerable<UserWatchtime>> GetActiveViewersAsync()
    {
        using var conn = CreateConnection();
        return await conn.QueryAsync<UserWatchtime>(
            "SELECT * FROM UserWatchtime WHERE IsActive = 1 ORDER BY LastSeenAt DESC");
    }

    public async Task SaveUserWatchtimeAsync(UserWatchtime watchtime)
    {
        using var conn = CreateConnection();

        watchtime.UpdatedAt = DateTime.UtcNow;

        await conn.ExecuteAsync(@"
            INSERT OR REPLACE INTO UserWatchtime (
                Id, Username, Platform, TotalMinutes, LastSeenAt, FirstSeenAt,
                StreamSessionMinutes, LastStreamId, IsActive, UpdatedAt
            ) VALUES (
                @Id, @Username, @Platform, @TotalMinutes, @LastSeenAt, @FirstSeenAt,
                @StreamSessionMinutes, @LastStreamId, @IsActive, @UpdatedAt
            )",
            new
            {
                Id = watchtime.Id.ToString(),
                watchtime.Username,
                watchtime.Platform,
                watchtime.TotalMinutes,
                // Bind DateTime directly (same TEXT format the date comparisons in
                // DeactivateInactiveViewersAsync use) instead of "o"-format strings.
                watchtime.LastSeenAt,
                watchtime.FirstSeenAt,
                watchtime.StreamSessionMinutes,
                watchtime.LastStreamId,
                IsActive = watchtime.IsActive ? 1 : 0,
                watchtime.UpdatedAt
            });
    }

    public async Task<int> GetTotalActiveViewersCountAsync()
    {
        using var conn = CreateConnection();
        return await conn.QuerySingleAsync<int>(
            "SELECT COUNT(*) FROM UserWatchtime WHERE IsActive = 1");
    }

    public async Task ResetStreamSessionMinutesAsync()
    {
        using var conn = CreateConnection();
        await conn.ExecuteAsync(
            "UPDATE UserWatchtime SET StreamSessionMinutes = 0, UpdatedAt = @Now",
            new { Now = DateTime.UtcNow });
    }

    public async Task DeactivateInactiveViewersAsync(DateTime inactiveSince)
    {
        using var conn = CreateConnection();
        await conn.ExecuteAsync(
            "UPDATE UserWatchtime SET IsActive = 0, UpdatedAt = @Now WHERE LastSeenAt < @InactiveSince",
            new { InactiveSince = inactiveSince, Now = DateTime.UtcNow });
    }

    #endregion

    #region Variable Operations

    public async Task<Variable?> GetVariableAsync(string id)
    {
        using var conn = CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<Variable>(
            "SELECT * FROM Variables WHERE Id = @Id", new { Id = id });
    }

    public async Task<Variable?> GetVariableByNameAsync(string name)
    {
        using var conn = CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<Variable>(
            "SELECT * FROM Variables WHERE Name = @Name COLLATE NOCASE", new { Name = name });
    }

    public async Task<IEnumerable<Variable>> GetVariablesAsync()
    {
        using var conn = CreateConnection();
        return await conn.QueryAsync<Variable>("SELECT * FROM Variables ORDER BY Category, Name");
    }

    public async Task<IEnumerable<Variable>> GetVariablesByCategoryAsync(string category)
    {
        using var conn = CreateConnection();
        return await conn.QueryAsync<Variable>(
            "SELECT * FROM Variables WHERE Category = @Category ORDER BY Name",
            new { Category = category });
    }

    public async Task<IEnumerable<string>> GetVariableCategoriesAsync()
    {
        using var conn = CreateConnection();
        return await conn.QueryAsync<string>(
            "SELECT DISTINCT Category FROM Variables ORDER BY Category");
    }

    public async Task SaveVariableAsync(Variable variable)
    {
        variable.UpdatedAt = DateTime.UtcNow;

        using var conn = CreateConnection();

        var existing = await conn.QueryFirstOrDefaultAsync<Variable>(
            "SELECT * FROM Variables WHERE Id = @Id", new { variable.Id });

        if (existing == null)
        {
            await conn.ExecuteAsync(@"
                INSERT INTO Variables (
                    Id, Name, DisplayName, Category, Prefix, Value, MaxLength,
                    IsEditable, IsSystem, IsAutoUpdating, ContainsNestedVariables,
                    Platforms, Description, CreatedAt, UpdatedAt, LastSyncedAt
                ) VALUES (
                    @Id, @Name, @DisplayName, @Category, @Prefix, @Value, @MaxLength,
                    @IsEditable, @IsSystem, @IsAutoUpdating, @ContainsNestedVariables,
                    @Platforms, @Description, @CreatedAt, @UpdatedAt, @LastSyncedAt
                )", variable);
        }
        else
        {
            await conn.ExecuteAsync(@"
                UPDATE Variables SET
                    Name = @Name,
                    DisplayName = @DisplayName,
                    Category = @Category,
                    Prefix = @Prefix,
                    Value = @Value,
                    MaxLength = @MaxLength,
                    IsEditable = @IsEditable,
                    IsSystem = @IsSystem,
                    IsAutoUpdating = @IsAutoUpdating,
                    ContainsNestedVariables = @ContainsNestedVariables,
                    Platforms = @Platforms,
                    Description = @Description,
                    UpdatedAt = @UpdatedAt,
                    LastSyncedAt = @LastSyncedAt
                WHERE Id = @Id", variable);
        }
    }

    public async Task DeleteVariableAsync(string id)
    {
        using var conn = CreateConnection();
        // Only allow deleting custom (editable, non-system) variables
        await conn.ExecuteAsync(
            "DELETE FROM Variables WHERE Id = @Id AND IsSystem = 0 AND IsEditable = 1",
            new { Id = id });
    }

    public async Task UpdateVariableValueAsync(string name, string? value)
    {
        using var conn = CreateConnection();
        await conn.ExecuteAsync(@"
            UPDATE Variables
            SET Value = @Value, UpdatedAt = @Now, LastSyncedAt = @Now
            WHERE Name = @Name COLLATE NOCASE",
            new { Name = name, Value = value, Now = DateTime.UtcNow });
    }

    #endregion

    #region Config Operations

    public async Task<string?> GetConfigValueAsync(string key)
    {
        using var conn = CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<string>(
            "SELECT Value FROM Configs WHERE Key = @Key", new { Key = key });
    }

    public async Task SetConfigValueAsync(string key, string value, string? category = null)
    {
        using var conn = CreateConnection();

        var exists = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM Configs WHERE Key = @Key", new { Key = key });

        if (exists > 0)
        {
            await conn.ExecuteAsync(@"
                UPDATE Configs SET Value = @Value, Category = @Category, UpdatedAt = @UpdatedAt
                WHERE Key = @Key",
                new { Key = key, Value = value, Category = category, UpdatedAt = DateTime.UtcNow });
        }
        else
        {
            await conn.ExecuteAsync(@"
                INSERT INTO Configs (Key, Value, Category, IsEncrypted, UpdatedAt)
                VALUES (@Key, @Value, @Category, 0, @UpdatedAt)",
                new { Key = key, Value = value, Category = category, UpdatedAt = DateTime.UtcNow });
        }
    }

    public async Task<IEnumerable<Config>> GetConfigsByCategoryAsync(string category)
    {
        using var conn = CreateConnection();
        return await conn.QueryAsync<Config>(
            "SELECT * FROM Configs WHERE Category = @Category", new { Category = category });
    }

    #endregion

    #region Trigger Operations

    public async Task<IEnumerable<Trigger>> GetTriggersAsync(bool? enabledOnly = null)
    {
        using var conn = CreateConnection();

        if (enabledOnly == true)
            return await conn.QueryAsync<Trigger>("SELECT * FROM Triggers WHERE Enabled = 1");

        return await conn.QueryAsync<Trigger>("SELECT * FROM Triggers");
    }

    public async Task<Trigger?> GetTriggerByIdAsync(Guid id)
    {
        using var conn = CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<Trigger>(
            "SELECT * FROM Triggers WHERE Id = @Id", new { Id = id });
    }

    public async Task SaveTriggerAsync(Trigger trigger)
    {
        trigger.UpdatedAt = DateTime.UtcNow;

        using var conn = CreateConnection();

        var exists = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM Triggers WHERE Id = @Id", new { trigger.Id });

        if (exists > 0)
        {
            await conn.ExecuteAsync(@"
                UPDATE Triggers SET
                    Name = @Name,
                    Description = @Description,
                    Enabled = @Enabled,
                    EventType = @EventType,
                    Platform = @Platform,
                    Condition = @Condition,
                    CooldownSeconds = @CooldownSeconds,
                    LastFiredAt = @LastFiredAt,
                    FireCount = @FireCount,
                    Permission = @Permission,
                    ActionSequence = @ActionSequence,
                    UpdatedAt = @UpdatedAt
                WHERE Id = @Id", trigger);
        }
        else
        {
            await conn.ExecuteAsync(@"
                INSERT INTO Triggers (
                    Id, Name, Description, Enabled, EventType, Platform, Condition,
                    CooldownSeconds, LastFiredAt, FireCount, Permission, ActionSequence,
                    CreatedAt, UpdatedAt
                ) VALUES (
                    @Id, @Name, @Description, @Enabled, @EventType, @Platform, @Condition,
                    @CooldownSeconds, @LastFiredAt, @FireCount, @Permission, @ActionSequence,
                    @CreatedAt, @UpdatedAt
                )", trigger);
        }
    }

    public async Task DeleteTriggerAsync(Guid id)
    {
        using var conn = CreateConnection();
        await conn.ExecuteAsync("DELETE FROM Triggers WHERE Id = @Id", new { Id = id });
    }

    #endregion

    #region Action Operations

    public async Task<IEnumerable<BotAction>> GetActionsAsync()
    {
        using var conn = CreateConnection();
        return await conn.QueryAsync<BotAction>("SELECT * FROM Actions");
    }

    public async Task<BotAction?> GetActionByIdAsync(Guid id)
    {
        using var conn = CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<BotAction>(
            "SELECT * FROM Actions WHERE Id = @Id", new { Id = id });
    }

    public async Task SaveActionAsync(BotAction action)
    {
        action.UpdatedAt = DateTime.UtcNow;

        using var conn = CreateConnection();

        var exists = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM Actions WHERE Id = @Id", new { action.Id });

        if (exists > 0)
        {
            await conn.ExecuteAsync(@"
                UPDATE Actions SET
                    Name = @Name,
                    Description = @Description,
                    ActionType = @ActionType,
                    Configuration = @Configuration,
                    UpdatedAt = @UpdatedAt
                WHERE Id = @Id", action);
        }
        else
        {
            await conn.ExecuteAsync(@"
                INSERT INTO Actions (
                    Id, Name, Description, ActionType, Configuration, CreatedAt, UpdatedAt
                ) VALUES (
                    @Id, @Name, @Description, @ActionType, @Configuration, @CreatedAt, @UpdatedAt
                )", action);
        }
    }

    public async Task DeleteActionAsync(Guid id)
    {
        using var conn = CreateConnection();
        await conn.ExecuteAsync("DELETE FROM Actions WHERE Id = @Id", new { Id = id });
    }

    #endregion

    #region EventLog Operations

    public async Task<IEnumerable<EventLog>> GetEventLogsAsync(int limit = 100)
    {
        using var conn = CreateConnection();
        return await conn.QueryAsync<EventLog>(
            "SELECT * FROM EventLogs ORDER BY Timestamp DESC LIMIT @Limit",
            new { Limit = limit });
    }

    public async Task LogEventAsync(EventLog log)
    {
        using var conn = CreateConnection();
        await conn.ExecuteAsync(@"
            INSERT INTO EventLogs (
                Id, Platform, EventType, Data, TriggerId, Timestamp
            ) VALUES (
                @Id, @Platform, @EventType, @Data, @TriggerId, @Timestamp
            )", log);
    }

    #endregion

    #region WebServer Operations

    public async Task<WebServerSettings> GetWebServerSettingsAsync()
    {
        using var conn = CreateConnection();
        var settings = await conn.QueryFirstOrDefaultAsync<WebServerSettings>(
            "SELECT * FROM WebServerSettings WHERE Id = 1");

        if (settings == null)
        {
            _logger.LogWarning("No WebServerSettings found in database, creating default row");
            settings = new WebServerSettings { Id = 1, AutoStartOnLaunch = true };
            await SaveWebServerSettingsAsync(settings);
        }
        else
        {
            _logger.LogInformation("Loaded WebServerSettings from DB - AutoStartOnLaunch: {Auto}, Port: {Port}",
                settings.AutoStartOnLaunch, settings.Port);
        }

        return settings;
    }

    public async Task SaveWebServerSettingsAsync(WebServerSettings settings)
    {
        using var conn = CreateConnection();

        var exists = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM WebServerSettings WHERE Id = 1");

        _logger.LogInformation("Saving WebServerSettings to DB - Exists: {Exists}, AutoStartOnLaunch: {Auto}",
            exists > 0, settings.AutoStartOnLaunch);

        if (exists > 0)
        {
            var rows = await conn.ExecuteAsync(@"
                UPDATE WebServerSettings SET
                    IsEnabled = @IsEnabled,
                    Port = @Port,
                    NgrokRegion = @NgrokRegion,
                    CurrentTunnelUrl = @CurrentTunnelUrl,
                    AutoStartOnLaunch = @AutoStartOnLaunch,
                    LastStarted = @LastStarted,
                    LastStopped = @LastStopped
                WHERE Id = 1", settings);
            _logger.LogInformation("Updated {Rows} row(s) in WebServerSettings table", rows);
        }
        else
        {
            await conn.ExecuteAsync(@"
                INSERT INTO WebServerSettings (
                    Id, IsEnabled, Port, NgrokRegion,
                    CurrentTunnelUrl, AutoStartOnLaunch, LastStarted, LastStopped
                ) VALUES (
                    1, @IsEnabled, @Port, @NgrokRegion,
                    @CurrentTunnelUrl, @AutoStartOnLaunch, @LastStarted, @LastStopped
                )", settings);
            _logger.LogInformation("Inserted new row into WebServerSettings table");
        }
    }

    public async Task<IEnumerable<WebhookSubscription>> GetWebhookSubscriptionsAsync(string? platform = null)
    {
        using var conn = CreateConnection();

        if (string.IsNullOrEmpty(platform))
        {
            return await conn.QueryAsync<WebhookSubscription>(
                "SELECT * FROM WebhookSubscriptions ORDER BY CreatedAt DESC");
        }

        return await conn.QueryAsync<WebhookSubscription>(
            "SELECT * FROM WebhookSubscriptions WHERE Platform = @Platform ORDER BY CreatedAt DESC",
            new { Platform = platform });
    }

    public async Task SaveWebhookSubscriptionAsync(WebhookSubscription subscription)
    {
        using var conn = CreateConnection();

        var exists = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM WebhookSubscriptions WHERE Id = @Id",
            new { subscription.Id });

        if (exists > 0)
        {
            await conn.ExecuteAsync(@"
                UPDATE WebhookSubscriptions SET
                    Platform = @Platform,
                    EventType = @EventType,
                    EventVersion = @EventVersion,
                    BroadcasterUserId = @BroadcasterUserId,
                    WebhookUrl = @WebhookUrl,
                    IsActive = @IsActive,
                    LastEventReceived = @LastEventReceived
                WHERE Id = @Id", subscription);
        }
        else
        {
            await conn.ExecuteAsync(@"
                INSERT INTO WebhookSubscriptions (
                    Id, Platform, EventType, EventVersion, BroadcasterUserId,
                    WebhookUrl, IsActive, LastEventReceived, CreatedAt
                ) VALUES (
                    @Id, @Platform, @EventType, @EventVersion, @BroadcasterUserId,
                    @WebhookUrl, @IsActive, @LastEventReceived, @CreatedAt
                )", subscription);
        }
    }

    public async Task DeleteWebhookSubscriptionAsync(string id)
    {
        using var conn = CreateConnection();
        await conn.ExecuteAsync("DELETE FROM WebhookSubscriptions WHERE Id = @Id", new { Id = id });
    }

    public async Task DeleteWebhookSubscriptionsByPlatformAsync(string platform)
    {
        using var conn = CreateConnection();
        await conn.ExecuteAsync("DELETE FROM WebhookSubscriptions WHERE Platform = @Platform",
            new { Platform = platform });
    }

    #endregion

    #region Workflow Operations (visual automation engine)

    public async Task<IEnumerable<WorkflowEntity>> GetWorkflowsAsync()
    {
        using var conn = CreateConnection();
        return await conn.QueryAsync<WorkflowEntity>("SELECT * FROM Workflows ORDER BY Name");
    }

    public async Task SaveWorkflowAsync(WorkflowEntity workflow)
    {
        workflow.UpdatedAt = DateTime.UtcNow;

        using var conn = CreateConnection();

        var exists = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM Workflows WHERE Id = @Id", new { workflow.Id });

        if (exists > 0)
        {
            await conn.ExecuteAsync(@"
                UPDATE Workflows SET
                    Name = @Name,
                    Description = @Description,
                    Enabled = @Enabled,
                    TriggerJson = @TriggerJson,
                    GraphJson = @GraphJson,
                    Category = @Category,
                    UpdatedAt = @UpdatedAt
                WHERE Id = @Id", workflow);
        }
        else
        {
            await conn.ExecuteAsync(@"
                INSERT INTO Workflows (
                    Id, Name, Description, Enabled, TriggerJson, GraphJson, Category, CreatedAt, UpdatedAt
                ) VALUES (
                    @Id, @Name, @Description, @Enabled, @TriggerJson, @GraphJson, @Category, @CreatedAt, @UpdatedAt
                )", workflow);
        }
    }

    public async Task DeleteWorkflowAsync(string id)
    {
        using var conn = CreateConnection();
        await conn.ExecuteAsync("DELETE FROM Workflows WHERE Id = @Id", new { Id = id });
    }

    public async Task<IEnumerable<WorkflowExecution>> GetWorkflowExecutionsAsync(string? workflowId = null, int limit = 50)
    {
        using var conn = CreateConnection();

        if (!string.IsNullOrEmpty(workflowId))
        {
            return await conn.QueryAsync<WorkflowExecution>(
                "SELECT * FROM WorkflowExecutions WHERE WorkflowId = @WorkflowId ORDER BY StartedAt DESC LIMIT @Limit",
                new { WorkflowId = workflowId, Limit = limit });
        }

        return await conn.QueryAsync<WorkflowExecution>(
            "SELECT * FROM WorkflowExecutions ORDER BY StartedAt DESC LIMIT @Limit",
            new { Limit = limit });
    }

    public async Task SaveWorkflowExecutionAsync(WorkflowExecution execution)
    {
        using var conn = CreateConnection();
        await conn.ExecuteAsync(@"
            INSERT INTO WorkflowExecutions (
                Id, WorkflowId, WorkflowName, TriggerSummary, Status,
                StartedAt, CompletedAt, DurationMs, NodePath, Error
            ) VALUES (
                @Id, @WorkflowId, @WorkflowName, @TriggerSummary, @Status,
                @StartedAt, @CompletedAt, @DurationMs, @NodePath, @Error
            )", execution);
    }

    #endregion

    #region Data Management Operations (Settings → Data Management)

    // Whitelisted user-data tables surfaced in the Settings → Data Management inventory.
    private static readonly (string Table, string Label)[] InventoryTables =
    {
        ("Commands", "Commands"),
        ("CommandTimers", "Command timers"),
        ("Workflows", "Workflows"),
        ("WorkflowExecutions", "Workflow executions"),
        ("EventLogs", "Event history"),
        ("ChatMessages", "Chat messages"),
        ("AutoReplyRules", "Auto-reply rules"),
        ("Variables", "Variables"),
        ("UserCurrency", "Currency users"),
        ("UserWatchtime", "Watchtime users"),
        ("Giveaways", "Giveaways"),
        ("Polls", "Polls"),
        ("DiscordBots", "Discord bots"),
        ("WebhookSubscriptions", "Webhook subscriptions"),
        ("LinkCodes", "Link codes"),
        ("ViewerPlatformStatus", "Viewer platform status"),
        ("ViewerPreferences", "Viewer preferences"),
        ("SocialGoals", "Social goals"),
        ("ViewerGoalCompletions", "Goal completions"),
    };

    public async Task<IReadOnlyDictionary<string, int>> GetDataInventoryAsync()
    {
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        using var conn = CreateConnection();
        conn.Open();

        foreach (var (table, label) in InventoryTables)
        {
            try
            {
                var count = await conn.ExecuteScalarAsync<long>($"SELECT COUNT(*) FROM {table}");
                result[label] = (int)count;
            }
            catch
            {
                // Table may not exist yet (fresh database) — report zero instead of failing
                result[label] = 0;
            }
        }

        return result;
    }

    public async Task DeleteAllChatMessagesAsync()
    {
        using var conn = CreateConnection();
        await conn.ExecuteAsync("DELETE FROM ChatMessages");
    }

    public async Task DeleteEventLogsAsync()
    {
        using var conn = CreateConnection();
        await conn.ExecuteAsync("DELETE FROM EventLogs");
    }

    public async Task DeleteWorkflowExecutionsAsync(string? workflowId = null, DateTime? olderThan = null)
    {
        using var conn = CreateConnection();

        var sql = "DELETE FROM WorkflowExecutions WHERE 1=1";
        var args = new DynamicParameters();

        if (!string.IsNullOrEmpty(workflowId))
        {
            sql += " AND WorkflowId = @WorkflowId";
            args.Add("WorkflowId", workflowId);
        }

        if (olderThan.HasValue)
        {
            sql += " AND StartedAt < @Cutoff";
            args.Add("Cutoff", olderThan.Value); // bound DateTime matches how SaveWorkflowExecutionAsync stores StartedAt
        }

        await conn.ExecuteAsync(sql, args);
    }

    public async Task<bool> CompactDatabaseAsync()
    {
        using var conn = CreateConnection();
        conn.Open();
        await conn.ExecuteAsync("VACUUM");
        return true;
    }

    #endregion

    #region Database Management

    public async Task EnsureDatabaseCreatedAsync()
    {
        using var conn = CreateConnection();
        conn.Open();

        // Create Platforms table
        await conn.ExecuteAsync(@"
            CREATE TABLE IF NOT EXISTS Platforms (
                Id TEXT PRIMARY KEY,
                Name TEXT NOT NULL,
                Enabled INTEGER NOT NULL DEFAULT 1,
                AccessToken TEXT,
                RefreshToken TEXT,
                TokenExpiresAt TEXT,
                Username TEXT,
                UserId TEXT,
                ChannelId TEXT,
                ChatroomId TEXT,
                ClientId TEXT,
                ClientSecret TEXT,
                Status TEXT NOT NULL DEFAULT 'Disconnected',
                LastError TEXT,
                ConnectedAt TEXT,
                KickChatEnabled INTEGER NOT NULL DEFAULT 0,
                KickChatAutoStart INTEGER NOT NULL DEFAULT 0,
                TwitchChatEnabled INTEGER NOT NULL DEFAULT 0,
                TwitchChatAutoStart INTEGER NOT NULL DEFAULT 0,
                YoutubeChatEnabled INTEGER NOT NULL DEFAULT 0,
                YoutubeChatAutoStart INTEGER NOT NULL DEFAULT 0,
                TiktokChatEnabled INTEGER NOT NULL DEFAULT 0,
                TiktokChatAutoStart INTEGER NOT NULL DEFAULT 0,
                DiscordChatEnabled INTEGER NOT NULL DEFAULT 0,
                DiscordChatAutoStart INTEGER NOT NULL DEFAULT 0,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS IX_Platforms_Name ON Platforms(Name);
            CREATE INDEX IF NOT EXISTS IX_Platforms_Enabled ON Platforms(Enabled);
        ");

        // Migration: Add chat columns for supported platforms if they don't exist

        // Kick
        var kickChatEnabledExists = await conn.ExecuteScalarAsync<long>(@"
            SELECT COUNT(*) FROM pragma_table_info('Platforms') WHERE name='KickChatEnabled';
        ").ConfigureAwait(false);
        if (kickChatEnabledExists == 0)
        {
            await conn.ExecuteAsync(@"ALTER TABLE Platforms ADD COLUMN KickChatEnabled INTEGER NOT NULL DEFAULT 0;").ConfigureAwait(false);
        }

        var kickChatAutoStartExists = await conn.ExecuteScalarAsync<long>(@"
            SELECT COUNT(*) FROM pragma_table_info('Platforms') WHERE name='KickChatAutoStart';
        ").ConfigureAwait(false);
        if (kickChatAutoStartExists == 0)
        {
            await conn.ExecuteAsync(@"ALTER TABLE Platforms ADD COLUMN KickChatAutoStart INTEGER NOT NULL DEFAULT 0;").ConfigureAwait(false);
        }

        // Twitch
        var twitchChatEnabledExists = await conn.ExecuteScalarAsync<long>(@"
            SELECT COUNT(*) FROM pragma_table_info('Platforms') WHERE name='TwitchChatEnabled';
        ").ConfigureAwait(false);
        if (twitchChatEnabledExists == 0)
        {
            await conn.ExecuteAsync(@"ALTER TABLE Platforms ADD COLUMN TwitchChatEnabled INTEGER NOT NULL DEFAULT 0;").ConfigureAwait(false);
        }

        var twitchChatAutoStartExists = await conn.ExecuteScalarAsync<long>(@"
            SELECT COUNT(*) FROM pragma_table_info('Platforms') WHERE name='TwitchChatAutoStart';
        ").ConfigureAwait(false);
        if (twitchChatAutoStartExists == 0)
        {
            await conn.ExecuteAsync(@"ALTER TABLE Platforms ADD COLUMN TwitchChatAutoStart INTEGER NOT NULL DEFAULT 0;").ConfigureAwait(false);
        }

        // YouTube
        var youtubeChatEnabledExists = await conn.ExecuteScalarAsync<long>(@"
            SELECT COUNT(*) FROM pragma_table_info('Platforms') WHERE name='YoutubeChatEnabled';
        ").ConfigureAwait(false);
        if (youtubeChatEnabledExists == 0)
        {
            await conn.ExecuteAsync(@"ALTER TABLE Platforms ADD COLUMN YoutubeChatEnabled INTEGER NOT NULL DEFAULT 0;").ConfigureAwait(false);
        }

        var youtubeChatAutoStartExists = await conn.ExecuteScalarAsync<long>(@"
            SELECT COUNT(*) FROM pragma_table_info('Platforms') WHERE name='YoutubeChatAutoStart';
        ").ConfigureAwait(false);
        if (youtubeChatAutoStartExists == 0)
        {
            await conn.ExecuteAsync(@"ALTER TABLE Platforms ADD COLUMN YoutubeChatAutoStart INTEGER NOT NULL DEFAULT 0;").ConfigureAwait(false);
        }

        // TikTok
        var tiktokChatEnabledExists = await conn.ExecuteScalarAsync<long>(@"
            SELECT COUNT(*) FROM pragma_table_info('Platforms') WHERE name='TiktokChatEnabled';
        ").ConfigureAwait(false);
        if (tiktokChatEnabledExists == 0)
        {
            await conn.ExecuteAsync(@"ALTER TABLE Platforms ADD COLUMN TiktokChatEnabled INTEGER NOT NULL DEFAULT 0;").ConfigureAwait(false);
        }

        var tiktokChatAutoStartExists = await conn.ExecuteScalarAsync<long>(@"
            SELECT COUNT(*) FROM pragma_table_info('Platforms') WHERE name='TiktokChatAutoStart';
        ").ConfigureAwait(false);
        if (tiktokChatAutoStartExists == 0)
        {
            await conn.ExecuteAsync(@"ALTER TABLE Platforms ADD COLUMN TiktokChatAutoStart INTEGER NOT NULL DEFAULT 0;").ConfigureAwait(false);
        }

        // Discord
        var discordChatEnabledExists = await conn.ExecuteScalarAsync<long>(@"
            SELECT COUNT(*) FROM pragma_table_info('Platforms') WHERE name='DiscordChatEnabled';
        ").ConfigureAwait(false);
        if (discordChatEnabledExists == 0)
        {
            await conn.ExecuteAsync(@"ALTER TABLE Platforms ADD COLUMN DiscordChatEnabled INTEGER NOT NULL DEFAULT 0;").ConfigureAwait(false);
        }

        var discordChatAutoStartExists = await conn.ExecuteScalarAsync<long>(@"
            SELECT COUNT(*) FROM pragma_table_info('Platforms') WHERE name='DiscordChatAutoStart';
        ").ConfigureAwait(false);
        if (discordChatAutoStartExists == 0)
        {
            await conn.ExecuteAsync(@"ALTER TABLE Platforms ADD COLUMN DiscordChatAutoStart INTEGER NOT NULL DEFAULT 0;").ConfigureAwait(false);
        }

        // Migration: Add ChannelId and ChatroomId for Kick platform
        var channelIdExists = await conn.ExecuteScalarAsync<long>(@"
            SELECT COUNT(*) FROM pragma_table_info('Platforms') WHERE name='ChannelId';
        ").ConfigureAwait(false);
        if (channelIdExists == 0)
        {
            await conn.ExecuteAsync(@"ALTER TABLE Platforms ADD COLUMN ChannelId TEXT;").ConfigureAwait(false);
        }

        var chatroomIdExists = await conn.ExecuteScalarAsync<long>(@"
            SELECT COUNT(*) FROM pragma_table_info('Platforms') WHERE name='ChatroomId';
        ").ConfigureAwait(false);
        if (chatroomIdExists == 0)
        {
            await conn.ExecuteAsync(@"ALTER TABLE Platforms ADD COLUMN ChatroomId TEXT;").ConfigureAwait(false);
        }

        // Create Configs table
        await conn.ExecuteAsync(@"
            CREATE TABLE IF NOT EXISTS Configs (
                Key TEXT PRIMARY KEY,
                Value TEXT NOT NULL,
                Category TEXT,
                IsEncrypted INTEGER NOT NULL DEFAULT 0,
                UpdatedAt TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_Configs_Category ON Configs(Category);
        ");

        // Create Triggers table
        await conn.ExecuteAsync(@"
            CREATE TABLE IF NOT EXISTS Triggers (
                Id TEXT PRIMARY KEY,
                Name TEXT NOT NULL,
                Description TEXT,
                Enabled INTEGER NOT NULL DEFAULT 1,
                EventType TEXT NOT NULL,
                Platform TEXT,
                Condition TEXT,
                CooldownSeconds INTEGER NOT NULL DEFAULT 0,
                LastFiredAt TEXT,
                FireCount INTEGER NOT NULL DEFAULT 0,
                Permission TEXT NOT NULL DEFAULT 'Everyone',
                ActionSequence TEXT NOT NULL DEFAULT '[]',
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_Triggers_EventType ON Triggers(EventType);
            CREATE INDEX IF NOT EXISTS IX_Triggers_Enabled ON Triggers(Enabled);
        ");

        // Create Actions table
        await conn.ExecuteAsync(@"
            CREATE TABLE IF NOT EXISTS Actions (
                Id TEXT PRIMARY KEY,
                Name TEXT NOT NULL,
                Description TEXT,
                ActionType TEXT NOT NULL,
                Configuration TEXT NOT NULL,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_Actions_ActionType ON Actions(ActionType);
        ");

        // Create EventLogs table
        await conn.ExecuteAsync(@"
            CREATE TABLE IF NOT EXISTS EventLogs (
                Id TEXT PRIMARY KEY,
                Platform TEXT NOT NULL,
                EventType TEXT NOT NULL,
                Data TEXT,
                TriggerId TEXT,
                Timestamp TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_EventLogs_Platform ON EventLogs(Platform);
            CREATE INDEX IF NOT EXISTS IX_EventLogs_EventType ON EventLogs(EventType);
            CREATE INDEX IF NOT EXISTS IX_EventLogs_Timestamp ON EventLogs(Timestamp);
            CREATE INDEX IF NOT EXISTS IX_EventLogs_TriggerId ON EventLogs(TriggerId);
        ");

        // Create WebServerSettings table
        await conn.ExecuteAsync(@"
            CREATE TABLE IF NOT EXISTS WebServerSettings (
                Id INTEGER PRIMARY KEY,
                IsEnabled INTEGER NOT NULL DEFAULT 0,
                Port INTEGER NOT NULL DEFAULT 5000,
                NgrokRegion TEXT NOT NULL DEFAULT 'us',
                CurrentTunnelUrl TEXT,
                AutoStartOnLaunch INTEGER NOT NULL DEFAULT 1,
                LastStarted TEXT,
                LastStopped TEXT
            );
        ");

        // Create WebhookSubscriptions table
        await conn.ExecuteAsync(@"
            CREATE TABLE IF NOT EXISTS WebhookSubscriptions (
                Id TEXT PRIMARY KEY,
                Platform TEXT NOT NULL,
                EventType TEXT NOT NULL,
                EventVersion INTEGER NOT NULL DEFAULT 1,
                BroadcasterUserId TEXT,
                WebhookUrl TEXT NOT NULL,
                IsActive INTEGER NOT NULL DEFAULT 1,
                LastEventReceived TEXT,
                CreatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
            );
            CREATE INDEX IF NOT EXISTS IX_WebhookSubscriptions_Platform ON WebhookSubscriptions(Platform);
            CREATE INDEX IF NOT EXISTS IX_WebhookSubscriptions_IsActive ON WebhookSubscriptions(IsActive);
        ");

        // Create ChatMessages table for persisted chat history
        await conn.ExecuteAsync(@"
            CREATE TABLE IF NOT EXISTS ChatMessages (
                Id TEXT PRIMARY KEY,
                Platform TEXT NOT NULL,
                Channel TEXT NOT NULL,
                MessageId TEXT,
                UserId TEXT,
                Username TEXT,
                DisplayName TEXT,
                Content TEXT,
                Timestamp TEXT NOT NULL,
                Color TEXT,
                IsModerator INTEGER NOT NULL DEFAULT 0,
                IsSubscriber INTEGER NOT NULL DEFAULT 0,
                IsOwner INTEGER NOT NULL DEFAULT 0,
                BadgesJson TEXT,
                IsSystemMessage INTEGER NOT NULL DEFAULT 0,
                CreatedAt TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_ChatMessages_PlatformChannel ON ChatMessages(Platform, Channel);
            CREATE INDEX IF NOT EXISTS IX_ChatMessages_MessageId ON ChatMessages(MessageId);
        ");

        // Create ChatEventSettings table for event visibility and auto-reply settings
        await conn.ExecuteAsync(@"
            CREATE TABLE IF NOT EXISTS ChatEventSettings (
                Id TEXT PRIMARY KEY,
                Platform TEXT NOT NULL,
                EventType TEXT NOT NULL,
                ShowInChat INTEGER NOT NULL DEFAULT 1,
                AutoReplyEnabled INTEGER NOT NULL DEFAULT 0,
                AutoReplyMessage TEXT,
                SoundEnabled INTEGER NOT NULL DEFAULT 1,
                SoundFile TEXT,
                SoundVolume INTEGER NOT NULL DEFAULT 100,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL,
                UNIQUE(Platform, EventType)
            );
            CREATE INDEX IF NOT EXISTS IX_ChatEventSettings_Platform ON ChatEventSettings(Platform);
        ");

        // Create AutoReplyRules table for conditional auto-replies
        await conn.ExecuteAsync(@"
            CREATE TABLE IF NOT EXISTS AutoReplyRules (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                Platform TEXT NOT NULL,
                EventType TEXT NOT NULL,
                IsEnabled INTEGER NOT NULL DEFAULT 1,
                Priority INTEGER NOT NULL DEFAULT 0,
                ConditionJson TEXT NOT NULL DEFAULT '{}',
                ReplyTemplate TEXT NOT NULL,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL,
                TriggerCount INTEGER NOT NULL DEFAULT 0,
                LastTriggeredAt TEXT
            );
            CREATE INDEX IF NOT EXISTS IX_AutoReplyRules_Platform_EventType ON AutoReplyRules(Platform, EventType);
            CREATE INDEX IF NOT EXISTS IX_AutoReplyRules_IsEnabled ON AutoReplyRules(IsEnabled);
            CREATE INDEX IF NOT EXISTS IX_AutoReplyRules_Priority ON AutoReplyRules(Priority);
        ");

        // Create EventStatistics table for event tracking and history
        await conn.ExecuteAsync(@"
            CREATE TABLE IF NOT EXISTS EventStatistics (
                Id TEXT PRIMARY KEY,
                Platform TEXT NOT NULL,
                EventType TEXT NOT NULL,
                Username TEXT NOT NULL,
                EventData TEXT,
                Timestamp TEXT NOT NULL,
                WasAutoReplied INTEGER NOT NULL DEFAULT 0,
                CreatedAt TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_EventStatistics_Platform ON EventStatistics(Platform);
            CREATE INDEX IF NOT EXISTS IX_EventStatistics_EventType ON EventStatistics(EventType);
            CREATE INDEX IF NOT EXISTS IX_EventStatistics_Username ON EventStatistics(Username);
            CREATE INDEX IF NOT EXISTS IX_EventStatistics_Timestamp ON EventStatistics(Timestamp);
        ");

        // Create CurrencySettings table for currency configuration
        await conn.ExecuteAsync(@"
            CREATE TABLE IF NOT EXISTS CurrencySettings (
                Id TEXT PRIMARY KEY,
                CurrencyName TEXT NOT NULL DEFAULT 'Points',
                CurrencySymbol TEXT NOT NULL DEFAULT '⭐',
                WatchTimeEnabled INTEGER NOT NULL DEFAULT 1,
                WatchTimeInterval INTEGER NOT NULL DEFAULT 60,
                WatchTimeAmount INTEGER NOT NULL DEFAULT 10,
                ChatActivityEnabled INTEGER NOT NULL DEFAULT 1,
                ChatActivityCooldown INTEGER NOT NULL DEFAULT 120,
                ChatActivityAmount INTEGER NOT NULL DEFAULT 5,
                FollowBonusEnabled INTEGER NOT NULL DEFAULT 1,
                FollowBonusAmount INTEGER NOT NULL DEFAULT 100,
                SubBonusEnabled INTEGER NOT NULL DEFAULT 1,
                SubBonusAmount INTEGER NOT NULL DEFAULT 500,
                GiftSubBonusEnabled INTEGER NOT NULL DEFAULT 1,
                GiftSubBonusAmountPerSub INTEGER NOT NULL DEFAULT 50,
                RaidBonusEnabled INTEGER NOT NULL DEFAULT 1,
                RaidBonusAmountPerViewer INTEGER NOT NULL DEFAULT 10,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL
            );
        ");

        // Create UserCurrency table for user balances (cross-platform)
        await conn.ExecuteAsync(@"
            CREATE TABLE IF NOT EXISTS UserCurrency (
                Id TEXT PRIMARY KEY,
                Username TEXT NOT NULL,
                Platform TEXT NOT NULL,
                Balance INTEGER NOT NULL DEFAULT 0,
                TotalEarned INTEGER NOT NULL DEFAULT 0,
                TotalSpent INTEGER NOT NULL DEFAULT 0,
                LastWatchTimeUpdate TEXT,
                LastChatActivityUpdate TEXT,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL,
                UNIQUE(Username, Platform)
            );
            CREATE INDEX IF NOT EXISTS IX_UserCurrency_Username ON UserCurrency(Username);
            CREATE INDEX IF NOT EXISTS IX_UserCurrency_Balance ON UserCurrency(Balance DESC);
            CREATE INDEX IF NOT EXISTS IX_UserCurrency_Platform ON UserCurrency(Platform);
        ");

        // Create CurrencyTransactions table for audit log
        await conn.ExecuteAsync(@"
            CREATE TABLE IF NOT EXISTS CurrencyTransactions (
                Id TEXT PRIMARY KEY,
                Username TEXT NOT NULL,
                Platform TEXT NOT NULL,
                Amount INTEGER NOT NULL,
                TransactionType TEXT NOT NULL,
                Reason TEXT,
                BalanceBefore INTEGER NOT NULL,
                BalanceAfter INTEGER NOT NULL,
                Timestamp TEXT NOT NULL,
                CreatedAt TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_CurrencyTransactions_Username ON CurrencyTransactions(Username);
            CREATE INDEX IF NOT EXISTS IX_CurrencyTransactions_Platform ON CurrencyTransactions(Platform);
            CREATE INDEX IF NOT EXISTS IX_CurrencyTransactions_Timestamp ON CurrencyTransactions(Timestamp DESC);
        ");

        // Create CurrencyRewards table for redeemable rewards
        await conn.ExecuteAsync(@"
            CREATE TABLE IF NOT EXISTS CurrencyRewards (
                Id TEXT PRIMARY KEY,
                Name TEXT NOT NULL,
                Description TEXT,
                Cost INTEGER NOT NULL,
                IsEnabled INTEGER NOT NULL DEFAULT 1,
                RequiresApproval INTEGER NOT NULL DEFAULT 0,
                Cooldown INTEGER NOT NULL DEFAULT 0,
                MaxRedemptionsPerUser INTEGER NOT NULL DEFAULT 0,
                MaxRedemptionsTotal INTEGER NOT NULL DEFAULT 0,
                CurrentRedemptions INTEGER NOT NULL DEFAULT 0,
                Category TEXT,
                Prompt TEXT,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_CurrencyRewards_IsEnabled ON CurrencyRewards(IsEnabled);
            CREATE INDEX IF NOT EXISTS IX_CurrencyRewards_Cost ON CurrencyRewards(Cost);
        ");

        // Create CurrencyRedemptions table for redemption queue
        await conn.ExecuteAsync(@"
            CREATE TABLE IF NOT EXISTS CurrencyRedemptions (
                Id TEXT PRIMARY KEY,
                RewardId TEXT NOT NULL,
                RewardName TEXT NOT NULL,
                Username TEXT NOT NULL,
                Platform TEXT NOT NULL,
                Cost INTEGER NOT NULL,
                UserInput TEXT,
                Status TEXT NOT NULL DEFAULT 'Pending',
                RedeemedAt TEXT NOT NULL,
                ProcessedAt TEXT,
                ProcessedBy TEXT,
                Notes TEXT,
                CreatedAt TEXT NOT NULL,
                FOREIGN KEY (RewardId) REFERENCES CurrencyRewards(Id)
            );
            CREATE INDEX IF NOT EXISTS IX_CurrencyRedemptions_Status ON CurrencyRedemptions(Status);
            CREATE INDEX IF NOT EXISTS IX_CurrencyRedemptions_Username ON CurrencyRedemptions(Username);
            CREATE INDEX IF NOT EXISTS IX_CurrencyRedemptions_RedeemedAt ON CurrencyRedemptions(RedeemedAt DESC);
        ");

        // Create LinkedAccounts table for cross-platform identity
        await conn.ExecuteAsync(@"
            CREATE TABLE IF NOT EXISTS LinkedAccounts (
                Id TEXT PRIMARY KEY,
                ViewerId TEXT NOT NULL,
                Username TEXT NOT NULL,
                Platform TEXT NOT NULL,
                IsVerified INTEGER NOT NULL DEFAULT 0,
                VerifiedAt TEXT,
                CreatedAt TEXT NOT NULL,
                UNIQUE(Username, Platform)
            );
            CREATE INDEX IF NOT EXISTS IX_LinkedAccounts_ViewerId ON LinkedAccounts(ViewerId);
            CREATE INDEX IF NOT EXISTS IX_LinkedAccounts_Username_Platform ON LinkedAccounts(Username, Platform);
        ");

        // Create LinkCodes table for viewer account linking codes
        await conn.ExecuteAsync(@"
            CREATE TABLE IF NOT EXISTS LinkCodes (
                Id TEXT PRIMARY KEY,
                Code TEXT NOT NULL UNIQUE,
                ViewerId TEXT NOT NULL,
                CreatedByUsername TEXT NOT NULL,
                CreatedByPlatform TEXT NOT NULL,
                CreatedAt TEXT NOT NULL,
                ExpiresAt TEXT NOT NULL,
                RedeemedByUsername TEXT,
                RedeemedByPlatform TEXT,
                RedeemedAt TEXT
            );
            CREATE INDEX IF NOT EXISTS IX_LinkCodes_ViewerId ON LinkCodes(ViewerId);
        ");

        // Create ViewerPlatformStatus table for cached per-platform sub/follower state
        await conn.ExecuteAsync(@"
            CREATE TABLE IF NOT EXISTS ViewerPlatformStatus (
                Id TEXT PRIMARY KEY,
                Username TEXT NOT NULL,
                Platform TEXT NOT NULL,
                ViewerId TEXT,
                IsSubscriber INTEGER NOT NULL DEFAULT 0,
                SubTier INTEGER NOT NULL DEFAULT 0,
                SubscriberMonths INTEGER NOT NULL DEFAULT 0,
                IsFollower INTEGER NOT NULL DEFAULT 0,
                LastUpdatedAt TEXT NOT NULL,
                UNIQUE(Username, Platform)
            );
            CREATE INDEX IF NOT EXISTS IX_ViewerPlatformStatus_ViewerId ON ViewerPlatformStatus(ViewerId);
        ");

        // Create ViewerPreferences table for per-viewer opt-in settings
        await conn.ExecuteAsync(@"
            CREATE TABLE IF NOT EXISTS ViewerPreferences (
                Id TEXT PRIMARY KEY,
                ViewerId TEXT NOT NULL UNIQUE,
                MultiPlatformBonusOptIn INTEGER NOT NULL DEFAULT 0,
                UpdatedAt TEXT NOT NULL
            );
        ");

        // Create SocialGoals table for broadcaster-defined social goals
        await conn.ExecuteAsync(@"
            CREATE TABLE IF NOT EXISTS SocialGoals (
                Id TEXT PRIMARY KEY,
                Name TEXT NOT NULL,
                Description TEXT NOT NULL,
                GoalType TEXT NOT NULL,
                TargetCount INTEGER NOT NULL DEFAULT 1,
                RequiredPlatforms TEXT NOT NULL DEFAULT 'All',
                RewardType TEXT NOT NULL DEFAULT 'Currency',
                RewardValue REAL NOT NULL DEFAULT 0,
                IsEnabled INTEGER NOT NULL DEFAULT 1,
                SortOrder INTEGER NOT NULL DEFAULT 0,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL
            );
        ");

        // Create ViewerGoalCompletions table for goal completion tracking
        await conn.ExecuteAsync(@"
            CREATE TABLE IF NOT EXISTS ViewerGoalCompletions (
                Id TEXT PRIMARY KEY,
                GoalId TEXT NOT NULL,
                ViewerId TEXT NOT NULL,
                Username TEXT NOT NULL,
                Platform TEXT NOT NULL,
                CompletedAt TEXT NOT NULL,
                RewardApplied INTEGER NOT NULL DEFAULT 0,
                UNIQUE(GoalId, ViewerId)
            );
            CREATE INDEX IF NOT EXISTS IX_ViewerGoalCompletions_GoalId ON ViewerGoalCompletions(GoalId);
        ");

        // Create ViewerStreaks table for attendance tracking
        await conn.ExecuteAsync(@"
            CREATE TABLE IF NOT EXISTS ViewerStreaks (
                Id TEXT PRIMARY KEY,
                Username TEXT NOT NULL,
                Platform TEXT NOT NULL,
                CurrentStreak INTEGER NOT NULL DEFAULT 0,
                LongestStreak INTEGER NOT NULL DEFAULT 0,
                LastStreamDate TEXT,
                LastDailyClaimDate TEXT,
                HasChattedThisStream INTEGER NOT NULL DEFAULT 0,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL,
                UNIQUE(Username, Platform)
            );
            CREATE INDEX IF NOT EXISTS IX_ViewerStreaks_Username ON ViewerStreaks(Username);
        ");

        // Create LoyaltyTiers table for rank definitions
        await conn.ExecuteAsync(@"
            CREATE TABLE IF NOT EXISTS LoyaltyTiers (
                Id TEXT PRIMARY KEY,
                Name TEXT NOT NULL,
                MinPoints INTEGER NOT NULL DEFAULT 0,
                EarnMultiplier REAL NOT NULL DEFAULT 1.0,
                Icon TEXT,
                Color TEXT,
                Perks TEXT,
                SortOrder INTEGER NOT NULL DEFAULT 0,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_LoyaltyTiers_MinPoints ON LoyaltyTiers(MinPoints ASC);
        ");

        // Create CurrencyGameSettings table for mini-game configuration
        await conn.ExecuteAsync(@"
            CREATE TABLE IF NOT EXISTS CurrencyGameSettings (
                Id TEXT PRIMARY KEY,
                GameType TEXT NOT NULL UNIQUE,
                IsEnabled INTEGER NOT NULL DEFAULT 1,
                MinBet INTEGER NOT NULL DEFAULT 10,
                MaxBet INTEGER NOT NULL DEFAULT 10000,
                CooldownSeconds INTEGER NOT NULL DEFAULT 30,
                HouseEdgePercent REAL NOT NULL DEFAULT 5.0,
                CustomConfig TEXT,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL
            );
        ");

        // Create ViewerPerks table for temporary purchased perks
        await conn.ExecuteAsync(@"
            CREATE TABLE IF NOT EXISTS ViewerPerks (
                Id TEXT PRIMARY KEY,
                Username TEXT NOT NULL,
                Platform TEXT NOT NULL,
                PerkType TEXT NOT NULL,
                PerkData TEXT,
                PurchasedAt TEXT NOT NULL,
                ExpiresAt TEXT NOT NULL,
                IsActive INTEGER NOT NULL DEFAULT 1,
                Cost INTEGER NOT NULL DEFAULT 0,
                CreatedAt TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_ViewerPerks_Username_Platform ON ViewerPerks(Username, Platform);
            CREATE INDEX IF NOT EXISTS IX_ViewerPerks_IsActive ON ViewerPerks(IsActive);
            CREATE INDEX IF NOT EXISTS IX_ViewerPerks_ExpiresAt ON ViewerPerks(ExpiresAt);
        ");

        // Create CurrencyBoosts table for temporary earning multipliers
        await conn.ExecuteAsync(@"
            CREATE TABLE IF NOT EXISTS CurrencyBoosts (
                Id TEXT PRIMARY KEY,
                Multiplier REAL NOT NULL DEFAULT 2.0,
                DurationMinutes INTEGER NOT NULL DEFAULT 30,
                StartedAt TEXT NOT NULL,
                EndsAt TEXT NOT NULL,
                Reason TEXT NOT NULL DEFAULT '',
                Source TEXT NOT NULL DEFAULT 'Manual',
                IsActive INTEGER NOT NULL DEFAULT 1,
                Platform TEXT,
                CreatedAt TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_CurrencyBoosts_IsActive ON CurrencyBoosts(IsActive);
            CREATE INDEX IF NOT EXISTS IX_CurrencyBoosts_EndsAt ON CurrencyBoosts(EndsAt);
        ");

        // Create CurrencyAuctions table for item bidding
        await conn.ExecuteAsync(@"
            CREATE TABLE IF NOT EXISTS CurrencyAuctions (
                Id TEXT PRIMARY KEY,
                ItemName TEXT NOT NULL,
                Description TEXT,
                StartingBid INTEGER NOT NULL DEFAULT 100,
                MinBidIncrement INTEGER NOT NULL DEFAULT 10,
                CurrentBid INTEGER NOT NULL DEFAULT 0,
                CurrentBidder TEXT,
                CurrentBidderPlatform TEXT,
                DurationMinutes INTEGER NOT NULL DEFAULT 5,
                StartedAt TEXT NOT NULL,
                EndsAt TEXT NOT NULL,
                Status TEXT NOT NULL DEFAULT 'Active',
                AntiSnipeEnabled INTEGER NOT NULL DEFAULT 1,
                AntiSnipeSeconds INTEGER NOT NULL DEFAULT 30,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_CurrencyAuctions_Status ON CurrencyAuctions(Status);
            CREATE INDEX IF NOT EXISTS IX_CurrencyAuctions_EndsAt ON CurrencyAuctions(EndsAt);
        ");

        // Create CurrencyAuctionBids table for bid history
        await conn.ExecuteAsync(@"
            CREATE TABLE IF NOT EXISTS CurrencyAuctionBids (
                Id TEXT PRIMARY KEY,
                AuctionId TEXT NOT NULL,
                Username TEXT NOT NULL,
                Platform TEXT NOT NULL,
                Amount INTEGER NOT NULL,
                BidAt TEXT NOT NULL,
                FOREIGN KEY (AuctionId) REFERENCES CurrencyAuctions(Id)
            );
            CREATE INDEX IF NOT EXISTS IX_CurrencyAuctionBids_AuctionId ON CurrencyAuctionBids(AuctionId);
        ");

        // Create UserBankAccounts table for savings with interest
        await conn.ExecuteAsync(@"
            CREATE TABLE IF NOT EXISTS UserBankAccounts (
                Id TEXT PRIMARY KEY,
                Username TEXT NOT NULL,
                Platform TEXT NOT NULL,
                BankBalance INTEGER NOT NULL DEFAULT 0,
                TotalDeposited INTEGER NOT NULL DEFAULT 0,
                TotalWithdrawn INTEGER NOT NULL DEFAULT 0,
                TotalInterestEarned INTEGER NOT NULL DEFAULT 0,
                LastInterestPayout TEXT,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL,
                UNIQUE(Username, Platform)
            );
            CREATE INDEX IF NOT EXISTS IX_UserBankAccounts_Username_Platform ON UserBankAccounts(Username, Platform);
            CREATE INDEX IF NOT EXISTS IX_UserBankAccounts_BankBalance ON UserBankAccounts(BankBalance DESC);
        ");

        // Add new columns to CurrencySettings if not present (Phase 1-2 upgrades)
        await AddColumnIfNotExistsAsync(conn, "CurrencySettings", "UnifiedBalanceEnabled", "INTEGER NOT NULL DEFAULT 0");
        await AddColumnIfNotExistsAsync(conn, "CurrencySettings", "CheerBonusEnabled", "INTEGER NOT NULL DEFAULT 1");
        await AddColumnIfNotExistsAsync(conn, "CurrencySettings", "CheerBonusAmountPerBit", "INTEGER NOT NULL DEFAULT 1");
        await AddColumnIfNotExistsAsync(conn, "CurrencySettings", "SubMultiplierEnabled", "INTEGER NOT NULL DEFAULT 0");
        await AddColumnIfNotExistsAsync(conn, "CurrencySettings", "SubMultiplierValue", "REAL NOT NULL DEFAULT 2.0");
        await AddColumnIfNotExistsAsync(conn, "CurrencySettings", "VipMultiplierEnabled", "INTEGER NOT NULL DEFAULT 0");
        await AddColumnIfNotExistsAsync(conn, "CurrencySettings", "VipMultiplierValue", "REAL NOT NULL DEFAULT 1.5");
        await AddColumnIfNotExistsAsync(conn, "CurrencySettings", "ModMultiplierEnabled", "INTEGER NOT NULL DEFAULT 0");
        await AddColumnIfNotExistsAsync(conn, "CurrencySettings", "ModMultiplierValue", "REAL NOT NULL DEFAULT 1.5");
        await AddColumnIfNotExistsAsync(conn, "CurrencySettings", "FirstChatBonusEnabled", "INTEGER NOT NULL DEFAULT 1");
        await AddColumnIfNotExistsAsync(conn, "CurrencySettings", "FirstChatBonusAmount", "INTEGER NOT NULL DEFAULT 50");
        await AddColumnIfNotExistsAsync(conn, "CurrencySettings", "DailyBonusEnabled", "INTEGER NOT NULL DEFAULT 1");
        await AddColumnIfNotExistsAsync(conn, "CurrencySettings", "DailyBonusAmount", "INTEGER NOT NULL DEFAULT 100");
        await AddColumnIfNotExistsAsync(conn, "CurrencySettings", "StreakBonusEnabled", "INTEGER NOT NULL DEFAULT 1");
        await AddColumnIfNotExistsAsync(conn, "CurrencySettings", "StreakBonusDay1", "INTEGER NOT NULL DEFAULT 50");
        await AddColumnIfNotExistsAsync(conn, "CurrencySettings", "StreakBonusDay5", "INTEGER NOT NULL DEFAULT 100");
        await AddColumnIfNotExistsAsync(conn, "CurrencySettings", "StreakBonusDay10", "INTEGER NOT NULL DEFAULT 250");
        await AddColumnIfNotExistsAsync(conn, "CurrencySettings", "HostBonusEnabled", "INTEGER NOT NULL DEFAULT 1");
        await AddColumnIfNotExistsAsync(conn, "CurrencySettings", "HostBonusAmount", "INTEGER NOT NULL DEFAULT 200");
        await AddColumnIfNotExistsAsync(conn, "CurrencySettings", "TwitchEarnMultiplier", "REAL NOT NULL DEFAULT 1.0");
        await AddColumnIfNotExistsAsync(conn, "CurrencySettings", "KickEarnMultiplier", "REAL NOT NULL DEFAULT 1.0");
        await AddColumnIfNotExistsAsync(conn, "CurrencySettings", "YouTubeEarnMultiplier", "REAL NOT NULL DEFAULT 1.0");
        await AddColumnIfNotExistsAsync(conn, "CurrencySettings", "CurrencyDecayEnabled", "INTEGER NOT NULL DEFAULT 0");
        await AddColumnIfNotExistsAsync(conn, "CurrencySettings", "CurrencyDecayPercent", "REAL NOT NULL DEFAULT 5.0");
        await AddColumnIfNotExistsAsync(conn, "CurrencySettings", "CurrencyDecayDays", "INTEGER NOT NULL DEFAULT 30");

        // Phase 6: Banking system columns
        await AddColumnIfNotExistsAsync(conn, "CurrencySettings", "BankEnabled", "INTEGER NOT NULL DEFAULT 0");
        await AddColumnIfNotExistsAsync(conn, "CurrencySettings", "BankInterestRate", "REAL NOT NULL DEFAULT 1.0");
        await AddColumnIfNotExistsAsync(conn, "CurrencySettings", "BankMaxBalance", "INTEGER NOT NULL DEFAULT 1000000");
        await AddColumnIfNotExistsAsync(conn, "CurrencySettings", "BankWithdrawalPenalty", "REAL NOT NULL DEFAULT 0.0");
        await AddColumnIfNotExistsAsync(conn, "CurrencySettings", "BankWithdrawalCooldownMinutes", "INTEGER NOT NULL DEFAULT 0");

        // Phase 7: Transfer tax
        await AddColumnIfNotExistsAsync(conn, "CurrencySettings", "TransferTaxPercent", "REAL NOT NULL DEFAULT 0.0");

        // Add new columns to CurrencyRewards (Phase 5 upgrades)
        await AddColumnIfNotExistsAsync(conn, "CurrencyRewards", "ActionSequenceId", "TEXT");
        await AddColumnIfNotExistsAsync(conn, "CurrencyRewards", "AvailableFrom", "TEXT");
        await AddColumnIfNotExistsAsync(conn, "CurrencyRewards", "AvailableUntil", "TEXT");
        await AddColumnIfNotExistsAsync(conn, "CurrencyRewards", "RewardTier", "TEXT");
        await AddColumnIfNotExistsAsync(conn, "CurrencyRewards", "RequiresSubscription", "INTEGER NOT NULL DEFAULT 0");
        await AddColumnIfNotExistsAsync(conn, "CurrencyRewards", "RequiredLoyaltyTier", "TEXT");

        // Seed default loyalty tiers if none exist
        var tierCount = await conn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM LoyaltyTiers");
        if (tierCount == 0)
        {
            var now = DateTime.UtcNow.ToString("o");
            await conn.ExecuteAsync(@"
                INSERT INTO LoyaltyTiers (Id, Name, MinPoints, EarnMultiplier, Icon, Color, Perks, SortOrder, CreatedAt, UpdatedAt) VALUES
                (@Id1, 'Newcomer', 0, 1.0, '🌱', '#9E9E9E', NULL, 0, @Now, @Now),
                (@Id2, 'Regular', 1000, 1.1, '⭐', '#4CAF50', NULL, 1, @Now, @Now),
                (@Id3, 'Loyal', 5000, 1.25, '💎', '#2196F3', NULL, 2, @Now, @Now),
                (@Id4, 'Veteran', 25000, 1.5, '🏆', '#9C27B0', NULL, 3, @Now, @Now),
                (@Id5, 'Elite', 100000, 1.75, '👑', '#FF9800', NULL, 4, @Now, @Now),
                (@Id6, 'Legend', 500000, 2.0, '🔥', '#F44336', NULL, 5, @Now, @Now)",
                new {
                    Id1 = Guid.NewGuid().ToString(), Id2 = Guid.NewGuid().ToString(),
                    Id3 = Guid.NewGuid().ToString(), Id4 = Guid.NewGuid().ToString(),
                    Id5 = Guid.NewGuid().ToString(), Id6 = Guid.NewGuid().ToString(),
                    Now = now
                });
        }

        // Seed default game settings if none exist
        var gameCount = await conn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM CurrencyGameSettings");
        if (gameCount == 0)
        {
            var now = DateTime.UtcNow.ToString("o");
            await conn.ExecuteAsync(@"
                INSERT INTO CurrencyGameSettings (Id, GameType, IsEnabled, MinBet, MaxBet, CooldownSeconds, HouseEdgePercent, CustomConfig, CreatedAt, UpdatedAt) VALUES
                (@Id1, 'Slots', 1, 10, 10000, 30, 5.0, NULL, @Now, @Now),
                (@Id2, 'Duel', 1, 10, 5000, 60, 0.0, NULL, @Now, @Now),
                (@Id3, 'Heist', 1, 10, 5000, 300, 10.0, NULL, @Now, @Now),
                (@Id4, 'Roulette', 1, 10, 10000, 30, 2.7, NULL, @Now, @Now)",
                new {
                    Id1 = Guid.NewGuid().ToString(), Id2 = Guid.NewGuid().ToString(),
                    Id3 = Guid.NewGuid().ToString(), Id4 = Guid.NewGuid().ToString(),
                    Now = now
                });
        }

        // Create Giveaways table for giveaway management
        await conn.ExecuteAsync(@"
            CREATE TABLE IF NOT EXISTS Giveaways (
                Id TEXT PRIMARY KEY,
                Name TEXT NOT NULL,
                Description TEXT,
                EntryKeyword TEXT NOT NULL,
                Status INTEGER NOT NULL DEFAULT 0,
                CreatedAt TEXT NOT NULL,
                StartedAt TEXT,
                EndedAt TEXT,
                ScheduledEndTime TEXT,
                MaxEntries INTEGER,
                WinnerCount INTEGER NOT NULL DEFAULT 1,
                SubscriberBonusEnabled INTEGER NOT NULL DEFAULT 0,
                SubscriberBonusEntries INTEGER NOT NULL DEFAULT 1,
                MinWatchtimeMinutes INTEGER NOT NULL DEFAULT 0,
                AllowedPlatforms TEXT NOT NULL DEFAULT 'All',
                FollowersOnly INTEGER NOT NULL DEFAULT 0,
                SubscribersOnly INTEGER NOT NULL DEFAULT 0,
                Winners TEXT,
                TotalEntries INTEGER NOT NULL DEFAULT 0,
                UniqueParticipants INTEGER NOT NULL DEFAULT 0,
                Metadata TEXT,
                UpdatedAt TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_Giveaways_Status ON Giveaways(Status);
            CREATE INDEX IF NOT EXISTS IX_Giveaways_CreatedAt ON Giveaways(CreatedAt DESC);
        ");

        // Viewer identity feature: migrate existing tables (Platforms and Giveaways both exist by this point)
        await AddColumnIfNotExistsAsync(conn, "Giveaways", "OddsBoostEnabled", "INTEGER NOT NULL DEFAULT 0");
        await AddColumnIfNotExistsAsync(conn, "Platforms", "FacebookChatEnabled", "INTEGER NOT NULL DEFAULT 0");
        await AddColumnIfNotExistsAsync(conn, "Platforms", "FacebookChatAutoStart", "INTEGER NOT NULL DEFAULT 0");
        await AddColumnIfNotExistsAsync(conn, "Platforms", "RumbleChatEnabled", "INTEGER NOT NULL DEFAULT 0");
        await AddColumnIfNotExistsAsync(conn, "Platforms", "RumbleChatAutoStart", "INTEGER NOT NULL DEFAULT 0");

        // Delink feature: LinkCodes purpose + target platform (LinkCodes table created earlier in this method)
        await AddColumnIfNotExistsAsync(conn, "LinkCodes", "Purpose", "TEXT NOT NULL DEFAULT 'Link'");
        await AddColumnIfNotExistsAsync(conn, "LinkCodes", "TargetPlatform", "TEXT");

        // Create GiveawayEntries table for participant tracking
        await conn.ExecuteAsync(@"
            CREATE TABLE IF NOT EXISTS GiveawayEntries (
                Id TEXT PRIMARY KEY,
                GiveawayId TEXT NOT NULL,
                Username TEXT NOT NULL,
                Platform TEXT NOT NULL,
                PlatformUserId TEXT,
                EnteredAt TEXT NOT NULL,
                EntryCount INTEGER NOT NULL DEFAULT 1,
                IsSubscriber INTEGER NOT NULL DEFAULT 0,
                IsFollower INTEGER NOT NULL DEFAULT 0,
                WatchtimeMinutes INTEGER NOT NULL DEFAULT 0,
                Message TEXT,
                IsWinner INTEGER NOT NULL DEFAULT 0,
                WonAt TEXT,
                FOREIGN KEY (GiveawayId) REFERENCES Giveaways(Id),
                UNIQUE(GiveawayId, Username, Platform)
            );
            CREATE INDEX IF NOT EXISTS IX_GiveawayEntries_GiveawayId ON GiveawayEntries(GiveawayId);
            CREATE INDEX IF NOT EXISTS IX_GiveawayEntries_Username ON GiveawayEntries(Username);
            CREATE INDEX IF NOT EXISTS IX_GiveawayEntries_IsWinner ON GiveawayEntries(IsWinner);
        ");

        // Create Polls table for poll management
        await conn.ExecuteAsync(@"
            CREATE TABLE IF NOT EXISTS Polls (
                Id TEXT PRIMARY KEY,
                Question TEXT NOT NULL,
                Options TEXT NOT NULL,
                Status INTEGER NOT NULL DEFAULT 0,
                CreatedAt TEXT NOT NULL,
                StartedAt TEXT,
                EndedAt TEXT,
                ScheduledEndTime TEXT,
                CreatorUsername TEXT NOT NULL,
                TotalVotes INTEGER NOT NULL DEFAULT 0,
                AllowMultipleVotes INTEGER NOT NULL DEFAULT 0,
                AllowedPlatforms TEXT NOT NULL DEFAULT 'All',
                FollowersOnly INTEGER NOT NULL DEFAULT 0,
                SubscribersOnly INTEGER NOT NULL DEFAULT 0,
                MinWatchtimeMinutes INTEGER NOT NULL DEFAULT 0,
                VoteResults TEXT,
                Metadata TEXT,
                UpdatedAt TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_Polls_Status ON Polls(Status);
            CREATE INDEX IF NOT EXISTS IX_Polls_CreatedAt ON Polls(CreatedAt DESC);
        ");

        // Create PollVotes table for vote tracking
        await conn.ExecuteAsync(@"
            CREATE TABLE IF NOT EXISTS PollVotes (
                Id TEXT PRIMARY KEY,
                PollId TEXT NOT NULL,
                Username TEXT NOT NULL,
                Platform TEXT NOT NULL,
                PlatformUserId TEXT,
                OptionIndex INTEGER NOT NULL,
                VotedAt TEXT NOT NULL,
                IsSubscriber INTEGER NOT NULL DEFAULT 0,
                IsFollower INTEGER NOT NULL DEFAULT 0,
                WatchtimeMinutes INTEGER NOT NULL DEFAULT 0,
                FOREIGN KEY (PollId) REFERENCES Polls(Id),
                UNIQUE(PollId, Username, Platform)
            );
            CREATE INDEX IF NOT EXISTS IX_PollVotes_PollId ON PollVotes(PollId);
            CREATE INDEX IF NOT EXISTS IX_PollVotes_Username ON PollVotes(Username);
            CREATE INDEX IF NOT EXISTS IX_PollVotes_OptionIndex ON PollVotes(OptionIndex);
        ");

        // Create UserWatchtime table for tracking viewer watchtime
        await conn.ExecuteAsync(@"
            CREATE TABLE IF NOT EXISTS UserWatchtime (
                Id TEXT PRIMARY KEY,
                Username TEXT NOT NULL,
                Platform TEXT NOT NULL,
                TotalMinutes INTEGER NOT NULL DEFAULT 0,
                LastSeenAt TEXT NOT NULL,
                FirstSeenAt TEXT NOT NULL,
                StreamSessionMinutes INTEGER NOT NULL DEFAULT 0,
                LastStreamId TEXT,
                IsActive INTEGER NOT NULL DEFAULT 0,
                UpdatedAt TEXT NOT NULL,
                UNIQUE(Username, Platform)
            );
            CREATE INDEX IF NOT EXISTS IX_UserWatchtime_Username ON UserWatchtime(Username);
            CREATE INDEX IF NOT EXISTS IX_UserWatchtime_Platform ON UserWatchtime(Platform);
            CREATE INDEX IF NOT EXISTS IX_UserWatchtime_TotalMinutes ON UserWatchtime(TotalMinutes DESC);
            CREATE INDEX IF NOT EXISTS IX_UserWatchtime_IsActive ON UserWatchtime(IsActive);
            CREATE INDEX IF NOT EXISTS IX_UserWatchtime_LastSeenAt ON UserWatchtime(LastSeenAt DESC);
        ");

        // Create Variables table
        await conn.ExecuteAsync(@"
            CREATE TABLE IF NOT EXISTS Variables (
                Id TEXT PRIMARY KEY,
                Name TEXT NOT NULL UNIQUE,
                DisplayName TEXT NOT NULL,
                Category TEXT NOT NULL,
                Prefix TEXT,
                Value TEXT,
                MaxLength INTEGER DEFAULT 2000,
                IsEditable INTEGER DEFAULT 0,
                IsSystem INTEGER DEFAULT 0,
                IsAutoUpdating INTEGER DEFAULT 0,
                ContainsNestedVariables INTEGER DEFAULT 0,
                Platforms TEXT,
                Description TEXT,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL,
                LastSyncedAt TEXT
            );
            CREATE INDEX IF NOT EXISTS IX_Variables_Category ON Variables(Category);
            CREATE INDEX IF NOT EXISTS IX_Variables_Name ON Variables(Name);
        ");

        // Create Commands table
        await conn.ExecuteAsync(@"
            CREATE TABLE IF NOT EXISTS Commands (
                Id TEXT PRIMARY KEY,
                Name TEXT NOT NULL,
                Response TEXT NOT NULL,
                Permission TEXT NOT NULL DEFAULT 'Everyone',
                GlobalCooldown INTEGER NOT NULL DEFAULT 0,
                UserCooldown INTEGER NOT NULL DEFAULT 0,
                IsEnabled INTEGER NOT NULL DEFAULT 1,
                IsBuiltIn INTEGER NOT NULL DEFAULT 0,
                EnabledOnKick INTEGER NOT NULL DEFAULT 1,
                EnabledOnTwitch INTEGER NOT NULL DEFAULT 1,
                UsageCount INTEGER NOT NULL DEFAULT 0,
                LastUsedAt TEXT,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS IX_Commands_Name ON Commands(Name COLLATE NOCASE);
            CREATE INDEX IF NOT EXISTS IX_Commands_IsEnabled ON Commands(IsEnabled);

            CREATE TABLE IF NOT EXISTS CommandCooldowns (
                Id TEXT PRIMARY KEY,
                CommandId TEXT NOT NULL,
                CommandName TEXT NOT NULL,
                Username TEXT,
                Platform TEXT NOT NULL,
                ExpiresAt TEXT NOT NULL,
                CreatedAt TEXT NOT NULL,
                FOREIGN KEY (CommandId) REFERENCES Commands(Id)
            );
            CREATE INDEX IF NOT EXISTS IX_CommandCooldowns_CommandId ON CommandCooldowns(CommandId);
            CREATE INDEX IF NOT EXISTS IX_CommandCooldowns_ExpiresAt ON CommandCooldowns(ExpiresAt);
            CREATE INDEX IF NOT EXISTS IX_CommandCooldowns_Username_Platform ON CommandCooldowns(Username, Platform);
        ");

        // Multi-bot Discord support: per-bot tokens/status/selected channel
        await conn.ExecuteAsync(@"
            CREATE TABLE IF NOT EXISTS DiscordBots (
                Id TEXT PRIMARY KEY,
                Name TEXT NOT NULL,
                Token TEXT,
                Status TEXT,
                SelectedChannelId TEXT,
                ApplicationId TEXT,
                BotUsername TEXT,
                AvatarUrl TEXT,
                RoleLinking INTEGER NOT NULL DEFAULT 0,
                RoleGiveaway INTEGER NOT NULL DEFAULT 0,
                AutoStart INTEGER NOT NULL DEFAULT 0,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_DiscordBots_Name ON DiscordBots(Name);
        ");

        // V3: auto-detected bot identity + single-assignment bot roles
        // (upgrades DiscordBots tables created before these columns existed)
        await AddColumnIfNotExistsAsync(conn, "DiscordBots", "ApplicationId", "TEXT");
        await AddColumnIfNotExistsAsync(conn, "DiscordBots", "BotUsername", "TEXT");
        await AddColumnIfNotExistsAsync(conn, "DiscordBots", "AvatarUrl", "TEXT");
        await AddColumnIfNotExistsAsync(conn, "DiscordBots", "RoleLinking", "INTEGER NOT NULL DEFAULT 0");
        await AddColumnIfNotExistsAsync(conn, "DiscordBots", "RoleGiveaway", "INTEGER NOT NULL DEFAULT 0");
        // V4: per-bot "start at app launch" flag. Defaults to 0 (opt-in per extra
        // bot); the PRIMARY bot is NOT seeded to 1 — when the Platform row's
        // DiscordChatAutoStart is on, the startup path alone starts the primary bot.
        await AddColumnIfNotExistsAsync(conn, "DiscordBots", "AutoStart", "INTEGER NOT NULL DEFAULT 0");

        // Command timers: scheduled firing of commands (Commands page → Timers tab)
        await conn.ExecuteAsync(@"
            CREATE TABLE IF NOT EXISTS CommandTimers (
                Id TEXT PRIMARY KEY,
                Name TEXT NOT NULL,
                CommandName TEXT NOT NULL,
                IntervalMinutes INTEGER NOT NULL DEFAULT 30,
                TargetPlatform TEXT NOT NULL DEFAULT 'Twitch',
                BotId TEXT,
                IsEnabled INTEGER NOT NULL DEFAULT 1,
                LastRunAt TEXT,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_CommandTimers_IsEnabled ON CommandTimers(IsEnabled);
            CREATE INDEX IF NOT EXISTS IX_CommandTimers_CommandName ON CommandTimers(CommandName);
        ");

        // Visual automation engine (VyralBot.Workflows): workflow definitions.
        // Trigger + node graph are stored as JSON (see VyralBot.Workflows.Persistence.WorkflowJson).
        await conn.ExecuteAsync(@"
            CREATE TABLE IF NOT EXISTS Workflows (
                Id TEXT PRIMARY KEY,
                Name TEXT NOT NULL,
                Description TEXT,
                Enabled INTEGER NOT NULL DEFAULT 1,
                TriggerJson TEXT,
                GraphJson TEXT,
                Category TEXT,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_Workflows_Category ON Workflows(Category);
            CREATE INDEX IF NOT EXISTS IX_Workflows_Enabled ON Workflows(Enabled);
        ");

        // Visual automation engine: execution history (one row per workflow run).
        await conn.ExecuteAsync(@"
            CREATE TABLE IF NOT EXISTS WorkflowExecutions (
                Id TEXT PRIMARY KEY,
                WorkflowId TEXT NOT NULL,
                WorkflowName TEXT,
                TriggerSummary TEXT,
                Status TEXT NOT NULL DEFAULT 'Failed',
                StartedAt TEXT NOT NULL,
                CompletedAt TEXT,
                DurationMs INTEGER NOT NULL DEFAULT 0,
                NodePath TEXT,
                Error TEXT
            );
            CREATE INDEX IF NOT EXISTS IX_WorkflowExecutions_WorkflowId ON WorkflowExecutions(WorkflowId);
            CREATE INDEX IF NOT EXISTS IX_WorkflowExecutions_StartedAt ON WorkflowExecutions(StartedAt);
        ");

        // Migration: per-bot command scoping (NULL = visible to every bot)
        var commandBotIdExists = await conn.ExecuteScalarAsync<long>(@"
            SELECT COUNT(*) FROM pragma_table_info('Commands') WHERE name='BotId';
        ").ConfigureAwait(false);
        if (commandBotIdExists == 0)
        {
            await conn.ExecuteAsync(@"ALTER TABLE Commands ADD COLUMN BotId TEXT;").ConfigureAwait(false);
        }

        // Migration: custom-command upgrades — random replies (ResponsesJson)
        // and ordered action sequences (ActionsJson). NULL = single Response.
        var commandResponsesJsonExists = await conn.ExecuteScalarAsync<long>(@"
            SELECT COUNT(*) FROM pragma_table_info('Commands') WHERE name='ResponsesJson';
        ").ConfigureAwait(false);
        if (commandResponsesJsonExists == 0)
        {
            await conn.ExecuteAsync(@"ALTER TABLE Commands ADD COLUMN ResponsesJson TEXT;").ConfigureAwait(false);
        }

        var commandActionsJsonExists = await conn.ExecuteScalarAsync<long>(@"
            SELECT COUNT(*) FROM pragma_table_info('Commands') WHERE name='ActionsJson';
        ").ConfigureAwait(false);
        if (commandActionsJsonExists == 0)
        {
            await conn.ExecuteAsync(@"ALTER TABLE Commands ADD COLUMN ActionsJson TEXT;").ConfigureAwait(false);
        }

        // Add webhook columns to Platforms table if they don't exist
        var platformColumns = await conn.QueryAsync<dynamic>("PRAGMA table_info(Platforms)");
        var platformColumnNames = platformColumns.Select(c => ((string)c.name).ToLower()).ToList();

        if (!platformColumnNames.Contains("webhookenabled"))
        {
            await conn.ExecuteAsync("ALTER TABLE Platforms ADD COLUMN WebhookEnabled INTEGER DEFAULT 0");
        }

        if (!platformColumnNames.Contains("webhookeventtypes"))
        {
            await conn.ExecuteAsync("ALTER TABLE Platforms ADD COLUMN WebhookEventTypes TEXT");
        }

        // Add IsSystemMessage column to ChatMessages if it doesn't exist
        var chatMessageColumns = await conn.QueryAsync<dynamic>("PRAGMA table_info(ChatMessages)");
        var chatMessageColumnNames = chatMessageColumns.Select(c => ((string)c.name).ToLower()).ToList();

        if (!chatMessageColumnNames.Contains("issystemmessage"))
        {
            await conn.ExecuteAsync("ALTER TABLE ChatMessages ADD COLUMN IsSystemMessage INTEGER NOT NULL DEFAULT 0");
        }

        // Seed default ChatEventSettings if none exist
        var eventSettingsCount = await conn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM ChatEventSettings");
        if (eventSettingsCount == 0)
        {
            _logger.LogInformation("Seeding default ChatEventSettings...");

            var defaultSettings = new List<ChatEventSetting>
            {
                // Kick Events
                new() { Platform = "Kick", EventType = "Follow", ShowInChat = true, AutoReplyEnabled = false, AutoReplyMessage = "Thanks for the follow {username}!", SoundEnabled = true, SoundFile = "follow.wav" },
                new() { Platform = "Kick", EventType = "Subscribe", ShowInChat = true, AutoReplyEnabled = false, AutoReplyMessage = "Thanks for subscribing {username}! 🎉", SoundEnabled = true, SoundFile = "sub.wav" },
                new() { Platform = "Kick", EventType = "GiftSub", ShowInChat = true, AutoReplyEnabled = false, AutoReplyMessage = "Thank you {gifter} for gifting {count} subs! 🎁", SoundEnabled = true, SoundFile = "giftsub.wav" },
                new() { Platform = "Kick", EventType = "Host", ShowInChat = true, AutoReplyEnabled = false, AutoReplyMessage = "Thanks for the host {username}!", SoundEnabled = true, SoundFile = "raid.wav" },
                new() { Platform = "Kick", EventType = "KicksGifted", ShowInChat = true, AutoReplyEnabled = false, AutoReplyMessage = "Thanks {username} for the {amount} kicks!", SoundEnabled = true, SoundFile = "cheer.wav" },
                new() { Platform = "Kick", EventType = "RewardRedeemed", ShowInChat = true, AutoReplyEnabled = false, AutoReplyMessage = "{username} redeemed {reward}!", SoundEnabled = true, SoundFile = "reward.wav" },
                new() { Platform = "Kick", EventType = "StreamStatus", ShowInChat = true, AutoReplyEnabled = false, AutoReplyMessage = null, SoundEnabled = false },

                // Twitch Events
                new() { Platform = "Twitch", EventType = "Follow", ShowInChat = true, AutoReplyEnabled = false, AutoReplyMessage = "Thanks for the follow {username}!", SoundEnabled = true, SoundFile = "follow.wav" },
                new() { Platform = "Twitch", EventType = "Subscribe", ShowInChat = true, AutoReplyEnabled = false, AutoReplyMessage = "Thanks for subscribing {username}! 🎉", SoundEnabled = true, SoundFile = "sub.wav" },
                new() { Platform = "Twitch", EventType = "Resub", ShowInChat = true, AutoReplyEnabled = false, AutoReplyMessage = "Thanks for the {months} month resub {username}!", SoundEnabled = true, SoundFile = "sub.wav" },
                new() { Platform = "Twitch", EventType = "GiftSub", ShowInChat = true, AutoReplyEnabled = false, AutoReplyMessage = "Thank you {gifter} for gifting {count} subs! 🎁", SoundEnabled = true, SoundFile = "giftsub.wav" },
                new() { Platform = "Twitch", EventType = "Raid", ShowInChat = true, AutoReplyEnabled = false, AutoReplyMessage = "Thanks for the raid {raider} with {viewers} viewers! 🎉", SoundEnabled = true, SoundFile = "raid.wav" },
                new() { Platform = "Twitch", EventType = "Cheer", ShowInChat = true, AutoReplyEnabled = false, AutoReplyMessage = "Thanks for the {bits} bits {username}!", SoundEnabled = true, SoundFile = "cheer.wav" },
                new() { Platform = "Twitch", EventType = "ChannelPoints", ShowInChat = true, AutoReplyEnabled = false, AutoReplyMessage = "{username} redeemed {reward}!", SoundEnabled = true, SoundFile = "reward.wav" }
            };

            foreach (var setting in defaultSettings)
            {
                await conn.ExecuteAsync(@"
                    INSERT INTO ChatEventSettings (
                        Id, Platform, EventType, ShowInChat, AutoReplyEnabled, AutoReplyMessage,
                        SoundEnabled, SoundFile, SoundVolume, CreatedAt, UpdatedAt
                    ) VALUES (
                        @Id, @Platform, @EventType, @ShowInChat, @AutoReplyEnabled, @AutoReplyMessage,
                        @SoundEnabled, @SoundFile, @SoundVolume, @CreatedAt, @UpdatedAt
                    )", setting);
            }

            _logger.LogInformation("Seeded {Count} default ChatEventSettings", defaultSettings.Count);
        }

        // Seed default CurrencySettings if none exist
        var currencySettingsCount = await conn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM CurrencySettings");
        if (currencySettingsCount == 0)
        {
            _logger.LogInformation("Seeding default CurrencySettings...");

            var defaultCurrencySettings = new
            {
                Id = Guid.NewGuid().ToString(),
                CurrencyName = "Points",
                CurrencySymbol = "⭐",
                WatchTimeEnabled = 1,
                WatchTimeInterval = 60, // 1 minute
                WatchTimeAmount = 10,
                ChatActivityEnabled = 1,
                ChatActivityCooldown = 120, // 2 minutes
                ChatActivityAmount = 5,
                FollowBonusEnabled = 1,
                FollowBonusAmount = 100,
                SubBonusEnabled = 1,
                SubBonusAmount = 500,
                GiftSubBonusEnabled = 1,
                GiftSubBonusAmountPerSub = 50,
                RaidBonusEnabled = 1,
                RaidBonusAmountPerViewer = 10,
                CreatedAt = DateTime.UtcNow.ToString("o"),
                UpdatedAt = DateTime.UtcNow.ToString("o")
            };

            await conn.ExecuteAsync(@"
                INSERT INTO CurrencySettings (
                    Id, CurrencyName, CurrencySymbol, WatchTimeEnabled, WatchTimeInterval, WatchTimeAmount,
                    ChatActivityEnabled, ChatActivityCooldown, ChatActivityAmount, FollowBonusEnabled, FollowBonusAmount,
                    SubBonusEnabled, SubBonusAmount, GiftSubBonusEnabled, GiftSubBonusAmountPerSub,
                    RaidBonusEnabled, RaidBonusAmountPerViewer, CreatedAt, UpdatedAt
                ) VALUES (
                    @Id, @CurrencyName, @CurrencySymbol, @WatchTimeEnabled, @WatchTimeInterval, @WatchTimeAmount,
                    @ChatActivityEnabled, @ChatActivityCooldown, @ChatActivityAmount, @FollowBonusEnabled, @FollowBonusAmount,
                    @SubBonusEnabled, @SubBonusAmount, @GiftSubBonusEnabled, @GiftSubBonusAmountPerSub,
                    @RaidBonusEnabled, @RaidBonusAmountPerViewer, @CreatedAt, @UpdatedAt
                )", defaultCurrencySettings);

            _logger.LogInformation("Seeded default CurrencySettings");
        }

        // Seed default CurrencyRewards if none exist
        var rewardsCount = await conn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM CurrencyRewards");
        if (rewardsCount == 0)
        {
            _logger.LogInformation("Seeding default CurrencyRewards...");

            var defaultRewards = new[]
            {
                new
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = "Song Request",
                    Description = "Request a song to be played on stream",
                    Cost = 100,
                    IsEnabled = 1,
                    RequiresApproval = 1,
                    Cooldown = 300, // 5 minutes
                    MaxRedemptionsPerUser = 3,
                    MaxRedemptionsTotal = 0,
                    CurrentRedemptions = 0,
                    Category = "Entertainment",
                    Prompt = "Enter the song name or URL:",
                    CreatedAt = DateTime.UtcNow.ToString("o"),
                    UpdatedAt = DateTime.UtcNow.ToString("o")
                },
                new
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = "Timeout User",
                    Description = "Timeout another user for 60 seconds",
                    Cost = 500,
                    IsEnabled = 1,
                    RequiresApproval = 1,
                    Cooldown = 600, // 10 minutes
                    MaxRedemptionsPerUser = 2,
                    MaxRedemptionsTotal = 0,
                    CurrentRedemptions = 0,
                    Category = "Chaos",
                    Prompt = "Enter the username to timeout:",
                    CreatedAt = DateTime.UtcNow.ToString("o"),
                    UpdatedAt = DateTime.UtcNow.ToString("o")
                },
                new
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = "Hydration Reminder",
                    Description = "Remind the streamer to drink water",
                    Cost = 50,
                    IsEnabled = 1,
                    RequiresApproval = 0,
                    Cooldown = 600, // 10 minutes
                    MaxRedemptionsPerUser = 0,
                    MaxRedemptionsTotal = 0,
                    CurrentRedemptions = 0,
                    Category = "Health",
                    Prompt = string.Empty,
                    CreatedAt = DateTime.UtcNow.ToString("o"),
                    UpdatedAt = DateTime.UtcNow.ToString("o")
                },
                new
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = "Highlight Clip",
                    Description = "Create a highlight clip of this moment",
                    Cost = 200,
                    IsEnabled = 1,
                    RequiresApproval = 0,
                    Cooldown = 120, // 2 minutes
                    MaxRedemptionsPerUser = 0,
                    MaxRedemptionsTotal = 0,
                    CurrentRedemptions = 0,
                    Category = "Stream",
                    Prompt = string.Empty,
                    CreatedAt = DateTime.UtcNow.ToString("o"),
                    UpdatedAt = DateTime.UtcNow.ToString("o")
                },
                new
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = "Change Game",
                    Description = "Request a different game for 10 minutes",
                    Cost = 1000,
                    IsEnabled = 1,
                    RequiresApproval = 1,
                    Cooldown = 1800, // 30 minutes
                    MaxRedemptionsPerUser = 1,
                    MaxRedemptionsTotal = 0,
                    CurrentRedemptions = 0,
                    Category = "Stream",
                    Prompt = "Enter the game name:",
                    CreatedAt = DateTime.UtcNow.ToString("o"),
                    UpdatedAt = DateTime.UtcNow.ToString("o")
                }
            };

            foreach (var reward in defaultRewards)
            {
                await conn.ExecuteAsync(@"
                    INSERT INTO CurrencyRewards (
                        Id, Name, Description, Cost, IsEnabled, RequiresApproval, Cooldown,
                        MaxRedemptionsPerUser, MaxRedemptionsTotal, CurrentRedemptions,
                        Category, Prompt, CreatedAt, UpdatedAt
                    ) VALUES (
                        @Id, @Name, @Description, @Cost, @IsEnabled, @RequiresApproval, @Cooldown,
                        @MaxRedemptionsPerUser, @MaxRedemptionsTotal, @CurrentRedemptions,
                        @Category, @Prompt, @CreatedAt, @UpdatedAt
                    )", reward);
            }

            _logger.LogInformation("Seeded {Count} default CurrencyRewards", defaultRewards.Length);
        }

        // Seed built-in commands if none exist
        var commandsCount = await conn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM Commands");
        if (commandsCount == 0)
        {
            _logger.LogInformation("Seeding built-in commands...");

            var builtInCommands = new[]
            {
                new
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = "!commands",
                    Response = "Available commands: {commandList} | Use !help <command> for details",
                    Permission = "Everyone",
                    GlobalCooldown = 5,
                    UserCooldown = 30,
                    IsEnabled = 1,
                    IsBuiltIn = 1,
                    EnabledOnKick = 1,
                    EnabledOnTwitch = 1,
                    UsageCount = 0,
                    LastUsedAt = (string?)null,
                    CreatedAt = DateTime.UtcNow.ToString("o"),
                    UpdatedAt = DateTime.UtcNow.ToString("o")
                },
                new
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = "!uptime",
                    Response = "Stream has been live for {uptime}",
                    Permission = "Everyone",
                    GlobalCooldown = 5,
                    UserCooldown = 15,
                    IsEnabled = 1,
                    IsBuiltIn = 1,
                    EnabledOnKick = 1,
                    EnabledOnTwitch = 1,
                    UsageCount = 0,
                    LastUsedAt = (string?)null,
                    CreatedAt = DateTime.UtcNow.ToString("o"),
                    UpdatedAt = DateTime.UtcNow.ToString("o")
                },
                new
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = "!socials",
                    Response = "Follow me: Twitter @{twitter} | Discord: {discord} | YouTube: {youtube}",
                    Permission = "Everyone",
                    GlobalCooldown = 10,
                    UserCooldown = 60,
                    IsEnabled = 1,
                    IsBuiltIn = 1,
                    EnabledOnKick = 1,
                    EnabledOnTwitch = 1,
                    UsageCount = 0,
                    LastUsedAt = (string?)null,
                    CreatedAt = DateTime.UtcNow.ToString("o"),
                    UpdatedAt = DateTime.UtcNow.ToString("o")
                },
                new
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = "!discord",
                    Response = "Join the community: {discord}",
                    Permission = "Everyone",
                    GlobalCooldown = 10,
                    UserCooldown = 60,
                    IsEnabled = 1,
                    IsBuiltIn = 1,
                    EnabledOnKick = 1,
                    EnabledOnTwitch = 1,
                    UsageCount = 0,
                    LastUsedAt = (string?)null,
                    CreatedAt = DateTime.UtcNow.ToString("o"),
                    UpdatedAt = DateTime.UtcNow.ToString("o")
                },
                new
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = "!watchtime",
                    Response = "{username}, you've watched for {watchtime}",
                    Permission = "Everyone",
                    GlobalCooldown = 5,
                    UserCooldown = 60,
                    IsEnabled = 1,
                    IsBuiltIn = 1,
                    EnabledOnKick = 1,
                    EnabledOnTwitch = 1,
                    UsageCount = 0,
                    LastUsedAt = (string?)null,
                    CreatedAt = DateTime.UtcNow.ToString("o"),
                    UpdatedAt = DateTime.UtcNow.ToString("o")
                },
                new
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = "!points",
                    Response = "Check your balance or someone else's: !points [@user]",
                    Permission = "Everyone",
                    GlobalCooldown = 3,
                    UserCooldown = 10,
                    IsEnabled = 1,
                    IsBuiltIn = 1,
                    EnabledOnKick = 1,
                    EnabledOnTwitch = 1,
                    UsageCount = 0,
                    LastUsedAt = (string?)null,
                    CreatedAt = DateTime.UtcNow.ToString("o"),
                    UpdatedAt = DateTime.UtcNow.ToString("o")
                },
                new
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = "!leaderboard",
                    Response = "Top currency holders: !leaderboard [count]",
                    Permission = "Everyone",
                    GlobalCooldown = 10,
                    UserCooldown = 30,
                    IsEnabled = 1,
                    IsBuiltIn = 1,
                    EnabledOnKick = 1,
                    EnabledOnTwitch = 1,
                    UsageCount = 0,
                    LastUsedAt = (string?)null,
                    CreatedAt = DateTime.UtcNow.ToString("o"),
                    UpdatedAt = DateTime.UtcNow.ToString("o")
                },
                new
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = "!gamble",
                    Response = "Gamble your currency: !gamble <amount|all>",
                    Permission = "Everyone",
                    GlobalCooldown = 3,
                    UserCooldown = 10,
                    IsEnabled = 1,
                    IsBuiltIn = 1,
                    EnabledOnKick = 1,
                    EnabledOnTwitch = 1,
                    UsageCount = 0,
                    LastUsedAt = (string?)null,
                    CreatedAt = DateTime.UtcNow.ToString("o"),
                    UpdatedAt = DateTime.UtcNow.ToString("o")
                },
                new
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = "!redeem",
                    Response = "Redeem a reward: !redeem <reward name> [message]",
                    Permission = "Everyone",
                    GlobalCooldown = 3,
                    UserCooldown = 10,
                    IsEnabled = 1,
                    IsBuiltIn = 1,
                    EnabledOnKick = 1,
                    EnabledOnTwitch = 1,
                    UsageCount = 0,
                    LastUsedAt = (string?)null,
                    CreatedAt = DateTime.UtcNow.ToString("o"),
                    UpdatedAt = DateTime.UtcNow.ToString("o")
                },
                new
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = "!rewards",
                    Response = "View available rewards",
                    Permission = "Everyone",
                    GlobalCooldown = 10,
                    UserCooldown = 30,
                    IsEnabled = 1,
                    IsBuiltIn = 1,
                    EnabledOnKick = 1,
                    EnabledOnTwitch = 1,
                    UsageCount = 0,
                    LastUsedAt = (string?)null,
                    CreatedAt = DateTime.UtcNow.ToString("o"),
                    UpdatedAt = DateTime.UtcNow.ToString("o")
                },
                new
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = "!daily",
                    Response = "Claim your daily bonus with streak multiplier",
                    Permission = "Everyone",
                    GlobalCooldown = 3,
                    UserCooldown = 5,
                    IsEnabled = 1,
                    IsBuiltIn = 1,
                    EnabledOnKick = 1,
                    EnabledOnTwitch = 1,
                    UsageCount = 0,
                    LastUsedAt = (string?)null,
                    CreatedAt = DateTime.UtcNow.ToString("o"),
                    UpdatedAt = DateTime.UtcNow.ToString("o")
                },
                new
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = "!rank",
                    Response = "View your loyalty tier and progress",
                    Permission = "Everyone",
                    GlobalCooldown = 5,
                    UserCooldown = 15,
                    IsEnabled = 1,
                    IsBuiltIn = 1,
                    EnabledOnKick = 1,
                    EnabledOnTwitch = 1,
                    UsageCount = 0,
                    LastUsedAt = (string?)null,
                    CreatedAt = DateTime.UtcNow.ToString("o"),
                    UpdatedAt = DateTime.UtcNow.ToString("o")
                },
                new
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = "!store",
                    Response = "Browse the reward store: !store [page|category]",
                    Permission = "Everyone",
                    GlobalCooldown = 5,
                    UserCooldown = 15,
                    IsEnabled = 1,
                    IsBuiltIn = 1,
                    EnabledOnKick = 1,
                    EnabledOnTwitch = 1,
                    UsageCount = 0,
                    LastUsedAt = (string?)null,
                    CreatedAt = DateTime.UtcNow.ToString("o"),
                    UpdatedAt = DateTime.UtcNow.ToString("o")
                },
                new
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = "!slots",
                    Response = "Play slots: !slots <amount>",
                    Permission = "Everyone",
                    GlobalCooldown = 3,
                    UserCooldown = 10,
                    IsEnabled = 1,
                    IsBuiltIn = 1,
                    EnabledOnKick = 1,
                    EnabledOnTwitch = 1,
                    UsageCount = 0,
                    LastUsedAt = (string?)null,
                    CreatedAt = DateTime.UtcNow.ToString("o"),
                    UpdatedAt = DateTime.UtcNow.ToString("o")
                },
                new
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = "!duel",
                    Response = "Challenge someone: !duel @user <amount>",
                    Permission = "Everyone",
                    GlobalCooldown = 3,
                    UserCooldown = 15,
                    IsEnabled = 1,
                    IsBuiltIn = 1,
                    EnabledOnKick = 1,
                    EnabledOnTwitch = 1,
                    UsageCount = 0,
                    LastUsedAt = (string?)null,
                    CreatedAt = DateTime.UtcNow.ToString("o"),
                    UpdatedAt = DateTime.UtcNow.ToString("o")
                },
                new
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = "!accept",
                    Response = "Accept a pending duel challenge",
                    Permission = "Everyone",
                    GlobalCooldown = 3,
                    UserCooldown = 5,
                    IsEnabled = 1,
                    IsBuiltIn = 1,
                    EnabledOnKick = 1,
                    EnabledOnTwitch = 1,
                    UsageCount = 0,
                    LastUsedAt = (string?)null,
                    CreatedAt = DateTime.UtcNow.ToString("o"),
                    UpdatedAt = DateTime.UtcNow.ToString("o")
                },
                new
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = "!heist",
                    Response = "Start or join a heist: !heist <amount>",
                    Permission = "Everyone",
                    GlobalCooldown = 3,
                    UserCooldown = 15,
                    IsEnabled = 1,
                    IsBuiltIn = 1,
                    EnabledOnKick = 1,
                    EnabledOnTwitch = 1,
                    UsageCount = 0,
                    LastUsedAt = (string?)null,
                    CreatedAt = DateTime.UtcNow.ToString("o"),
                    UpdatedAt = DateTime.UtcNow.ToString("o")
                },
                new
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = "!roulette",
                    Response = "Play roulette: !roulette <amount> <red|black|number>",
                    Permission = "Everyone",
                    GlobalCooldown = 3,
                    UserCooldown = 10,
                    IsEnabled = 1,
                    IsBuiltIn = 1,
                    EnabledOnKick = 1,
                    EnabledOnTwitch = 1,
                    UsageCount = 0,
                    LastUsedAt = (string?)null,
                    CreatedAt = DateTime.UtcNow.ToString("o"),
                    UpdatedAt = DateTime.UtcNow.ToString("o")
                },
                new
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = "!link",
                    Response = "Link cross-platform accounts: !link <platform> [code]",
                    Permission = "Everyone",
                    GlobalCooldown = 5,
                    UserCooldown = 30,
                    IsEnabled = 1,
                    IsBuiltIn = 1,
                    EnabledOnKick = 1,
                    EnabledOnTwitch = 1,
                    UsageCount = 0,
                    LastUsedAt = (string?)null,
                    CreatedAt = DateTime.UtcNow.ToString("o"),
                    UpdatedAt = DateTime.UtcNow.ToString("o")
                },
                new
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = "!give",
                    Response = "Give currency (mod only): !give @user <amount>",
                    Permission = "Moderator",
                    GlobalCooldown = 3,
                    UserCooldown = 5,
                    IsEnabled = 1,
                    IsBuiltIn = 1,
                    EnabledOnKick = 1,
                    EnabledOnTwitch = 1,
                    UsageCount = 0,
                    LastUsedAt = (string?)null,
                    CreatedAt = DateTime.UtcNow.ToString("o"),
                    UpdatedAt = DateTime.UtcNow.ToString("o")
                },
                new
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = "!take",
                    Response = "Remove currency (mod only): !take @user <amount>",
                    Permission = "Moderator",
                    GlobalCooldown = 3,
                    UserCooldown = 5,
                    IsEnabled = 1,
                    IsBuiltIn = 1,
                    EnabledOnKick = 1,
                    EnabledOnTwitch = 1,
                    UsageCount = 0,
                    LastUsedAt = (string?)null,
                    CreatedAt = DateTime.UtcNow.ToString("o"),
                    UpdatedAt = DateTime.UtcNow.ToString("o")
                }
            };

            foreach (var command in builtInCommands)
            {
                await conn.ExecuteAsync(@"
                    INSERT INTO Commands (
                        Id, Name, Response, Permission, GlobalCooldown, UserCooldown,
                        IsEnabled, IsBuiltIn, EnabledOnKick, EnabledOnTwitch,
                        UsageCount, LastUsedAt, CreatedAt, UpdatedAt
                    ) VALUES (
                        @Id, @Name, @Response, @Permission, @GlobalCooldown, @UserCooldown,
                        @IsEnabled, @IsBuiltIn, @EnabledOnKick, @EnabledOnTwitch,
                        @UsageCount, @LastUsedAt, @CreatedAt, @UpdatedAt
                    )", command);
            }

            _logger.LogInformation("Seeded {Count} built-in commands", builtInCommands.Length);
        }

        // Discord DM history: unlimited persistence for the chat inbox.
        conn.Execute(@"
            CREATE TABLE IF NOT EXISTS DiscordDmMessages (
                Id TEXT PRIMARY KEY,
                BotId TEXT NOT NULL,
                BotUsername TEXT,
                ChannelId TEXT NOT NULL,
                Username TEXT NOT NULL,
                Direction TEXT NOT NULL,
                Content TEXT NOT NULL,
                Timestamp TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_DiscordDmMessages_Username_Timestamp
                ON DiscordDmMessages(Username, Timestamp);"
);

        // Ensure all currency built-in commands exist (migration for existing databases)
        var currencyCommandNames = new[] {
            "!points", "!leaderboard", "!gamble", "!redeem", "!rewards",
            "!daily", "!rank", "!store", "!slots", "!duel", "!accept",
            "!heist", "!roulette", "!give", "!take"
        };
        foreach (var cmdName in currencyCommandNames)
        {
            var exists = await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(1) FROM Commands WHERE Name = @Name COLLATE NOCASE", new { Name = cmdName });
            if (exists == 0)
            {
                await conn.ExecuteAsync(@"
                    INSERT INTO Commands (Id, Name, Response, Permission, GlobalCooldown, UserCooldown,
                        IsEnabled, IsBuiltIn, EnabledOnKick, EnabledOnTwitch, UsageCount, CreatedAt, UpdatedAt)
                    VALUES (@Id, @Name, @Response, @Permission, 3, 10, 1, 1, 1, 1, 0, @Now, @Now)",
                    new {
                        Id = Guid.NewGuid().ToString(),
                        Name = cmdName,
                        Response = $"Currency command: {cmdName}",
                        Permission = (cmdName is "!give" or "!take") ? "Moderator" : "Everyone",
                        Now = DateTime.UtcNow.ToString("o")
                    });
                _logger.LogInformation("Added missing built-in command: {CommandName}", cmdName);
            }
        }

        // Ensure the built-in !help command exists (migration for existing
        // databases): CommandService replies with its static HelpText for this
        // name, so the row only carries the enable/permission/cooldown settings
        // and routes the name into the built-in switch — without a row the
        // command lookup returns null and !help would never answer.
        var helpExists = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM Commands WHERE Name = '!help' COLLATE NOCASE");
        if (helpExists == 0)
        {
            await conn.ExecuteAsync(@"
                INSERT INTO Commands (Id, Name, Response, Permission, GlobalCooldown, UserCooldown,
                    IsEnabled, IsBuiltIn, EnabledOnKick, EnabledOnTwitch, UsageCount, CreatedAt, UpdatedAt)
                VALUES (@Id, @Name, @Response, @Permission, 3, 5, 1, 1, 1, 1, 0, @Now, @Now)",
                new
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = "!help",
                    Response = "Command list (handled in code)",
                    Permission = "Everyone",
                    Now = DateTime.UtcNow.ToString("o")
                });
            _logger.LogInformation("Added missing built-in command: !help");
        }

        // Seed default variables if none exist
        var variablesCount = await conn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM Variables");
        if (variablesCount == 0)
        {
            _logger.LogInformation("Seeding default variables...");

            var now = DateTime.UtcNow.ToString("o");

            // Global System Variables (non-editable, context-only)
            var systemVariables = new[]
            {
                new { Name = "username", DisplayName = "Username", Description = "Display name of the user who triggered the event" },
                new { Name = "message", DisplayName = "Message", Description = "The user's chat message content" },
                new { Name = "months", DisplayName = "Subscription Months", Description = "Number of months subscribed" },
                new { Name = "number", DisplayName = "Number", Description = "Generic number (followers, gift subs, etc.)" },
                new { Name = "platform", DisplayName = "Platform", Description = "Platform name (Kick, Twitch, etc.)" },
                new { Name = "viewers", DisplayName = "Viewer Count", Description = "Number of viewers (raids, hosts)" },
                new { Name = "bits", DisplayName = "Bits", Description = "Twitch bits amount cheered" },
                new { Name = "reward", DisplayName = "Reward Name", Description = "Channel point reward name" },
                new { Name = "tier", DisplayName = "Subscription Tier", Description = "Sub tier (1000, 2000, 3000)" },
                new { Name = "gifter", DisplayName = "Gifter", Description = "Username of gift sub sender" },
                new { Name = "raider", DisplayName = "Raider", Description = "Username of the raider" },
                new { Name = "hoster", DisplayName = "Hoster", Description = "Username of the hoster" },
                new { Name = "sender", DisplayName = "Sender", Description = "Username of the sender (donations, etc.)" },
                new { Name = "amount", DisplayName = "Amount", Description = "Monetary amount (donations, tips)" },
                new { Name = "balance", DisplayName = "Currency Balance", Description = "User's current currency balance" },
                new { Name = "role", DisplayName = "User Role", Description = "User's role (Broadcaster, Moderator, VIP, Subscriber, Viewer)" },
                new { Name = "channel", DisplayName = "Channel Name", Description = "Name of the channel" },
                new { Name = "uptime", DisplayName = "Stream Uptime", Description = "How long the stream has been live" },
                new { Name = "watchtime", DisplayName = "User Watchtime", Description = "How long the user has watched" }
            };

            foreach (var v in systemVariables)
            {
                await conn.ExecuteAsync(@"
                    INSERT INTO Variables (
                        Id, Name, DisplayName, Category, Prefix, Value, MaxLength,
                        IsEditable, IsSystem, IsAutoUpdating, ContainsNestedVariables,
                        Platforms, Description, CreatedAt, UpdatedAt, LastSyncedAt
                    ) VALUES (
                        @Id, @Name, @DisplayName, @Category, @Prefix, @Value, @MaxLength,
                        @IsEditable, @IsSystem, @IsAutoUpdating, @ContainsNestedVariables,
                        @Platforms, @Description, @CreatedAt, @UpdatedAt, @LastSyncedAt
                    )", new
                {
                    Id = Guid.NewGuid().ToString(),
                    v.Name,
                    v.DisplayName,
                    Category = "Global",
                    Prefix = (string?)null,
                    Value = (string?)null,
                    MaxLength = 2000,
                    IsEditable = 0,
                    IsSystem = 1,
                    IsAutoUpdating = 0,
                    ContainsNestedVariables = 0,
                    Platforms = (string?)null,
                    v.Description,
                    CreatedAt = now,
                    UpdatedAt = now,
                    LastSyncedAt = (string?)null
                });
            }

            // Platform-specific auto-updating variables (non-editable)
            var platformVariables = new[]
            {
                // Kick
                new { Name = "kick-followercount", DisplayName = "Kick Follower Count", Prefix = "kick-", Platforms = "Kick", Description = "Current Kick follower count" },
                new { Name = "kick-subscribercount", DisplayName = "Kick Subscriber Count", Prefix = "kick-", Platforms = "Kick", Description = "Current Kick subscriber count" },
                new { Name = "kick-viewcount", DisplayName = "Kick View Count", Prefix = "kick-", Platforms = "Kick", Description = "Current Kick viewer count" },
                // Twitch
                new { Name = "twitch-followercount", DisplayName = "Twitch Follower Count", Prefix = "twitch-", Platforms = "Twitch", Description = "Current Twitch follower count" },
                new { Name = "twitch-subscribercount", DisplayName = "Twitch Subscriber Count", Prefix = "twitch-", Platforms = "Twitch", Description = "Current Twitch subscriber count" },
                new { Name = "twitch-viewcount", DisplayName = "Twitch View Count", Prefix = "twitch-", Platforms = "Twitch", Description = "Current Twitch viewer count" }
            };

            foreach (var v in platformVariables)
            {
                await conn.ExecuteAsync(@"
                    INSERT INTO Variables (
                        Id, Name, DisplayName, Category, Prefix, Value, MaxLength,
                        IsEditable, IsSystem, IsAutoUpdating, ContainsNestedVariables,
                        Platforms, Description, CreatedAt, UpdatedAt, LastSyncedAt
                    ) VALUES (
                        @Id, @Name, @DisplayName, @Category, @Prefix, @Value, @MaxLength,
                        @IsEditable, @IsSystem, @IsAutoUpdating, @ContainsNestedVariables,
                        @Platforms, @Description, @CreatedAt, @UpdatedAt, @LastSyncedAt
                    )", new
                {
                    Id = Guid.NewGuid().ToString(),
                    v.Name,
                    v.DisplayName,
                    Category = v.Platforms, // Use platform name as category
                    v.Prefix,
                    Value = "0", // Default to 0
                    MaxLength = 100,
                    IsEditable = 0,
                    IsSystem = 0,
                    IsAutoUpdating = 1,
                    ContainsNestedVariables = 0,
                    v.Platforms,
                    v.Description,
                    CreatedAt = now,
                    UpdatedAt = now,
                    LastSyncedAt = (string?)null
                });
            }

            // Editable Social Variables
            var socialVariables = new[]
            {
                new { Name = "twitter", DisplayName = "Twitter/X", Description = "Your Twitter/X handle or URL", Value = "" },
                new { Name = "discord", DisplayName = "Discord", Description = "Your Discord invite link", Value = "" },
                new { Name = "youtube", DisplayName = "YouTube", Description = "Your YouTube channel URL", Value = "" },
                new { Name = "instagram", DisplayName = "Instagram", Description = "Your Instagram handle or URL", Value = "" },
                new { Name = "tiktok", DisplayName = "TikTok", Description = "Your TikTok handle or URL", Value = "" },
                new { Name = "full-socials", DisplayName = "Full Socials", Description = "Combined social links message (can use nested variables)", Value = "Twitter: {twitter} | Discord: {discord} | YouTube: {youtube}" }
            };

            foreach (var v in socialVariables)
            {
                await conn.ExecuteAsync(@"
                    INSERT INTO Variables (
                        Id, Name, DisplayName, Category, Prefix, Value, MaxLength,
                        IsEditable, IsSystem, IsAutoUpdating, ContainsNestedVariables,
                        Platforms, Description, CreatedAt, UpdatedAt, LastSyncedAt
                    ) VALUES (
                        @Id, @Name, @DisplayName, @Category, @Prefix, @Value, @MaxLength,
                        @IsEditable, @IsSystem, @IsAutoUpdating, @ContainsNestedVariables,
                        @Platforms, @Description, @CreatedAt, @UpdatedAt, @LastSyncedAt
                    )", new
                {
                    Id = Guid.NewGuid().ToString(),
                    v.Name,
                    v.DisplayName,
                    Category = "Social",
                    Prefix = (string?)null,
                    v.Value,
                    MaxLength = 500,
                    IsEditable = 1,
                    IsSystem = 0,
                    IsAutoUpdating = 0,
                    ContainsNestedVariables = v.Name == "full-socials" ? 1 : 0,
                    Platforms = (string?)null,
                    v.Description,
                    CreatedAt = now,
                    UpdatedAt = now,
                    LastSyncedAt = (string?)null
                });
            }

            _logger.LogInformation("Seeded {SystemCount} system variables, {PlatformCount} platform variables, {SocialCount} social variables",
                systemVariables.Length, platformVariables.Length, socialVariables.Length);
        }

        _logger.LogInformation("Database schema created/verified");
    }

    private static async Task AddColumnIfNotExistsAsync(System.Data.IDbConnection conn, string table, string column, string type)
    {
        var columns = await conn.QueryAsync<dynamic>($"PRAGMA table_info({table})");
        var columnExists = columns.Any(c => string.Equals((string)c.name, column, StringComparison.OrdinalIgnoreCase));
        if (!columnExists)
        {
            await conn.ExecuteAsync($"ALTER TABLE {table} ADD COLUMN {column} {type}");
        }
    }

    #endregion
}
