using Xunit;
using VyralBot.Data;
using VyralBot.Core.Services.Counters;
using VyralBot.Core.Services.Currency;
using VyralBot.Core.Services.StreamStatus;
using VyralBot.Data.Entities;
using VyralBot.Integrations.Twitch;
using VyralBot.Workflows.Abstractions;

namespace VyralBot.UI.Tests;

// ======================================================================
// Shared test doubles for the WAVE-14 tests (StreamTools chat commands,
// death counter, !tts command gates, stream-control workflow nodes,
// leaderboard/counters loopback controllers).
//
// Mirrors the Wave-8 fake style: real, configurable behavior for the
// members the wave-14 surfaces touch; every member a test should never
// depend on throws so an accidental coupling fails loudly.
// ======================================================================

/// <summary>
/// Configurable ITwitchService for <c>StreamToolsCommandService</c>: the
/// engine-facing Helix members (CreateClipUrl/StartRaid/CancelRaid/
/// UpdateChannelInfo/GetChannelInfo/GetTopGames/StartCommercial/
/// GetStreamUptime/GetFollowAge) return settable results and record their
/// arguments. UI quick-action members and the connect/send surface throw
/// (the command service never touches them).
/// </summary>
public sealed class Wave14TwitchService : ITwitchService
{
    private const string NotImplemented = "Not needed for wave-14 tests.";

    public bool IsConnected { get; set; } = true;
    public string? Channel { get; set; } = "vyralhq";
    public string? BotUsername { get; set; } = "vyralbot";

    // ----- Configurable results -----
    public string? ClipUrlResult { get; set; }
    public bool StartRaidResult { get; set; } = true;
    public bool CancelRaidResult { get; set; } = true;
    public bool UpdateChannelInfoResult { get; set; } = true;
    public (string Title, string GameName)? ChannelInfoResult { get; set; }
    public string? TopGamesResult { get; set; }
    public bool StartCommercialResult { get; set; } = true;
    public TimeSpan? StreamUptimeResult { get; set; }
    public TimeSpan? FollowAgeResult { get; set; }

    // ----- Recorded calls -----
    public List<string> RaidTargets { get; } = new();
    public int CancelRaidCalls { get; private set; }
    public List<(string? Title, string? GameId)> ChannelUpdates { get; } = new();
    public List<string?> TopGamesQueries { get; } = new();
    public List<int> CommercialLengths { get; } = new();
    public List<string> FollowAgeLookups { get; } = new();

    // ----- Chat surface (unused by StreamToolsCommandService) -----
    public Task ConnectAsync(string username, string accessToken, string channel) => throw new NotImplementedException(NotImplemented);
    public Task ConnectAsync(string username, string accessToken, string? refreshToken, string channel) => throw new NotImplementedException(NotImplemented);
    public Task DisconnectAsync() => throw new NotImplementedException(NotImplemented);
    public Task SendMessageAsync(string message) => throw new NotImplementedException(NotImplemented);

    // ----- UI quick actions (unused by StreamToolsCommandService) -----
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

    // ----- Engine-facing Helix actions/lookups (the !so/!raid/!title/!game/
    // !commercial/!uptime/!followage seams) -----
    public Task<string?> CreateClipUrlAsync() => Task.FromResult(ClipUrlResult);

    public Task<bool> StartRaidAsync(string targetLogin)
    {
        RaidTargets.Add(targetLogin);
        return Task.FromResult(StartRaidResult);
    }

    public Task<bool> CancelRaidAsync()
    {
        CancelRaidCalls++;
        return Task.FromResult(CancelRaidResult);
    }

    public Task<bool> UpdateChannelInfoAsync(string? title, string? gameId)
    {
        ChannelUpdates.Add((title, gameId));
        return Task.FromResult(UpdateChannelInfoResult);
    }

    public Task<(string Title, string GameName)?> GetChannelInfoAsync()
        => Task.FromResult(ChannelInfoResult);

    public Task<string?> GetTopGamesAsync(string? query = null, int limit = 20)
    {
        TopGamesQueries.Add(query);
        return Task.FromResult(TopGamesResult);
    }

    public Task<bool> StartCommercialAsync(int seconds)
    {
        CommercialLengths.Add(seconds);
        return Task.FromResult(StartCommercialResult);
    }

    public Task<TimeSpan?> GetStreamUptimeAsync() => Task.FromResult(StreamUptimeResult);

    public Task<TimeSpan?> GetFollowAgeAsync(string login)
    {
        FollowAgeLookups.Add(login);
        return Task.FromResult(FollowAgeResult);
    }
}

/// <summary>
/// In-memory IStreamLiveRegistry for the non-Twitch !uptime degradation path
/// (the service itself takes the interface nullable, so "no registry" is tested
/// by passing null rather than a fake).
/// </summary>
public sealed class Wave14LiveRegistry : IStreamLiveRegistry
{
    private readonly Dictionary<string, bool> _live = new(StringComparer.OrdinalIgnoreCase);

    public event EventHandler<string>? StreamLiveChanged;

    public bool IsLive(string platform) =>
        _live.TryGetValue(platform, out var live) && live;

    public void SetLive(string platform, bool isLive)
    {
        var was = IsLive(platform);
        _live[platform] = isLive;
        if (was != isLive)
        {
            StreamLiveChanged?.Invoke(this, platform);
        }
    }
}

/// <summary>
/// IDataService for the !tts command tests, layered over the wave-8 in-memory
/// config fake. The DoNotDisturb key answers by READ PARITY: the 1st, 3rd, 5th
/// ... reads say "False" and the 2nd, 4th, ... say "True". TextToSpeechService
/// reads the key exactly once per command-level gate and once more inside the
/// SpeakAsync of an accepted command, so for sequential commands the odd reads
/// are the command gates (they pass — cooldown acquired, truncation applied,
/// reply built) and the even reads are the speak gates (they trip DND, so
/// SpeakAsync returns BEFORE enqueueing). The tests therefore exercise the
/// parse/gate/cooldown seams deterministically without ever reaching the real
/// SpeechSynthesizer (voiceless-environment safe). <see cref="DndReads"/> lets
/// a test prove the speak path was entered and gated rather than skipped.
/// </summary>
public sealed class Wave14TtsDataService : IntegrationWave8DataService, IDataService
{
    private int _dndReads;

    /// <summary>How often the Notifications.DoNotDisturb key has been read.</summary>
    public int DndReads => _dndReads;

    public new Task<string?> GetConfigValueAsync(string key)
    {
        if (key == "Notifications.DoNotDisturb")
        {
            _dndReads++;
            return Task.FromResult<string?>(_dndReads % 2 == 1 ? "False" : "True");
        }

        return base.GetConfigValueAsync(key);
    }
}

/// <summary>
/// Recording IStreamControlWorkflowBridge for the stream-control workflow
/// nodes: every method appends "<c>method[:arg]</c>" to <see cref="Calls"/>
/// and returns the single configurable <see cref="Outcome"/> — so node tests
/// can flip Ok true/false and still see exactly which bridge action ran.
/// </summary>
public sealed class Wave14StreamControlBridge : IStreamControlWorkflowBridge
{
    public StreamControlOutcome Outcome { get; set; } = new(true, "bridge did the thing");

    /// <summary>Which bridge methods ran, with the scene name when applicable.</summary>
    public List<string> Calls { get; } = new();

    public Task<StreamControlOutcome> SetSceneAsync(string sceneName)
    {
        Calls.Add($"scene:{sceneName}");
        return Task.FromResult(Outcome);
    }

    public Task<StreamControlOutcome> StartStreamAsync()
    {
        Calls.Add("stream:start");
        return Task.FromResult(Outcome);
    }

    public Task<StreamControlOutcome> StopStreamAsync()
    {
        Calls.Add("stream:stop");
        return Task.FromResult(Outcome);
    }

    public Task<StreamControlOutcome> SaveReplayBufferAsync()
    {
        Calls.Add("replay");
        return Task.FromResult(Outcome);
    }

    public Task<StreamControlOutcome> CreateTwitchClipAsync()
    {
        Calls.Add("clip");
        return Task.FromResult(Outcome);
    }
}

/// <summary>
/// Recording ICurrencyService for the LeaderboardController tests: real
/// behavior for <see cref="ICurrencyService.GetLeaderboardAsync"/> (records
/// the effective limit/platform and returns one entry) and
/// <see cref="ICurrencyService.GetSettingsAsync"/>; every other member throws
/// (the read-only controller never touches them).
/// </summary>
public sealed class Wave14CurrencyService : ICurrencyService
{
    private const string NotImplemented = "Not needed for wave-14 tests.";

    public int Count { get; set; } = 3;

    public List<(int Limit, string? Platform)> LeaderboardCalls { get; } = new();

    public Task<IEnumerable<UserCurrency>> GetLeaderboardAsync(int limit = 10, string? platform = null)
    {
        LeaderboardCalls.Add((limit, platform));
        var rows = Enumerable.Range(1, Math.Min(limit, Count))
            .Select(i => new UserCurrency { Username = $"viewer{i}", Platform = platform ?? "Twitch", Balance = 100 * (Count - i + 1) });
        return Task.FromResult<IEnumerable<UserCurrency>>(rows);
    }

    public Task<CurrencySetting?> GetSettingsAsync()
        => Task.FromResult<CurrencySetting?>(new CurrencySetting { CurrencyName = "Vibes", CurrencySymbol = "✨" });

    public Task SaveSettingsAsync(CurrencySetting settings) => throw new NotImplementedException(NotImplemented);
    public Task<int> GetBalanceAsync(string username, string platform) => throw new NotImplementedException(NotImplemented);
    public Task<IEnumerable<UserCurrency>> GetAllBalancesAsync(string username) => throw new NotImplementedException(NotImplemented);
    public Task<bool> AddCurrencyAsync(string username, string platform, int amount, string reason, string transactionType = "Earn") => throw new NotImplementedException(NotImplemented);
    public Task<bool> RemoveCurrencyAsync(string username, string platform, int amount, string reason, string transactionType = "Spend") => throw new NotImplementedException(NotImplemented);
    public Task<bool> TransferCurrencyAsync(string fromUsername, string fromPlatform, string toUsername, string toPlatform, int amount) => throw new NotImplementedException(NotImplemented);
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

/// <summary>
/// Minimal IDeathCounterService for the CountersController tests: the read
/// the /counters/death widget polls; the mutation members throw (the
/// controller is read-only).
/// </summary>
public sealed class Wave14CountersService : IDeathCounterService
{
    private const string NotImplemented = "Not needed for wave-14 tests.";

    public int Count { get; set; }

    public int GetCountCalls { get; private set; }

    public event EventHandler<int>? CountChanged;

    public Task<int> GetCountAsync()
    {
        GetCountCalls++;
        return Task.FromResult(Count);
    }

    public Task<string?> ProcessCommandAsync(string username, string platform, bool isModerator, string message)
        => throw new NotImplementedException(NotImplemented);

    public Task<int> AddAsync(int delta, string by) => throw new NotImplementedException(NotImplemented);
    public Task<int> SubtractAsync(int delta, string by) => throw new NotImplementedException(NotImplemented);
    public Task<int> SetAsync(int value, string by) => throw new NotImplementedException(NotImplemented);
    public Task<int> ResetAsync(string by) => throw new NotImplementedException(NotImplemented);
    public Task<IReadOnlyList<DeathCounterEvent>> GetHistoryAsync(int limit = 10) => throw new NotImplementedException(NotImplemented);
}
