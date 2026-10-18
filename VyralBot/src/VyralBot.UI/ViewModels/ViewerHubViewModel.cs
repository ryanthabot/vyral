using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using VyralBot.Core.Services.Linking;
using VyralBot.Data;
using VyralBot.Data.Entities;
using VyralBot.Integrations.Discord;

namespace VyralBot.UI.ViewModels;

/// <summary>
/// Settings → Viewer Hub panel: link-code settings + pending codes monitor,
/// multi-platform bonus ladder, giveaway odds bonuses, Vyral goal editor and
/// the linked-viewers browser.
///
/// Config-backed toggles auto-save on change (guarded by <see cref="_isLoaded"/>
/// so population does not persist). Pending link codes refresh on a 30s
/// DispatcherTimer; the linked-viewers list only loads on Refresh-button
/// demand to avoid heavy queries when the panel is merely constructed.
/// </summary>
public partial class ViewerHubViewModel : ViewModelBase
{
    private readonly ILogger<ViewerHubViewModel> _logger;
    private readonly IDataService _dataService;
    private readonly ILinkCodeService _linkCodeService;
    private readonly ISocialGoalService _socialGoalService;
    private readonly IDiscordBotManager _discordBotManager;

    private bool _isLoaded;
    private DispatcherTimer? _codeTimer;

    /// <summary>Suppresses the selection handler while populating the ComboBox.</summary>
    private bool _suppressLinkingBotSelection;

    [ObservableProperty]
    private string? _statusMessage;

    // ===== Linking =====

    [ObservableProperty]
    private bool _linkingEnabled = true;

    [ObservableProperty]
    private int _codeExpiryMinutes = 5;

    [ObservableProperty]
    private bool _twitchLinkingEnabled = true;

    [ObservableProperty]
    private bool _kickLinkingEnabled = true;

    [ObservableProperty]
    private bool _youTubeLinkingEnabled = true;

    [ObservableProperty]
    private bool _discordLinkingEnabled = true;

    [ObservableProperty]
    private bool _tikTokLinkingEnabled = true;

    [ObservableProperty]
    private bool _facebookLinkingEnabled = true;

    [ObservableProperty]
    private bool _rumbleLinkingEnabled = true;

    public ObservableCollection<LinkCodeRow> PendingCodes { get; } = new();

    // ===== Linking bot picker =====

    /// <summary>
    /// ComboBox choices for "Account linking Discord bot": a first
    /// "Primary bot (default)" item (BotId = null) plus one item per bot row.
    /// Rebuilt from the manager's bot rows on every InitializeAsync.
    /// </summary>
    public ObservableCollection<LinkingBotOption> LinkingBotOptions { get; } = new();

    /// <summary>Currently selected linking-bot option (null until populated).</summary>
    [ObservableProperty]
    private LinkingBotOption? _selectedLinkingBot;

    // ===== Multi-platform bonus =====

    /// <summary>"Off" | "OptIn" | "Auto" (matches MultiPlatformBonusService).</summary>
    [ObservableProperty]
    private string _mpMode = "Off";

    [ObservableProperty]
    private double _bonus2 = 1.25;

    [ObservableProperty]
    private double _bonus3 = 1.5;

    [ObservableProperty]
    private double _bonus4 = 2.0;

    [ObservableProperty]
    private double _bonus5 = 2.5;

    [ObservableProperty]
    private double _activeWatchingBonus = 1.25;

    /// <summary>Two-way wrapper so a RadioButton can drive the string MpMode.</summary>
    public bool MpModeIsOff
    {
        get => MpMode == "Off";
        set { if (value) MpMode = "Off"; }
    }

    public bool MpModeIsOptIn
    {
        get => MpMode == "OptIn";
        set { if (value) MpMode = "OptIn"; }
    }

    public bool MpModeIsAuto
    {
        get => MpMode == "Auto";
        set { if (value) MpMode = "Auto"; }
    }

    partial void OnMpModeChanged(string value)
    {
        OnPropertyChanged(nameof(MpModeIsOff));
        OnPropertyChanged(nameof(MpModeIsOptIn));
        OnPropertyChanged(nameof(MpModeIsAuto));

        if (_isLoaded)
        {
            _ = PersistConfigAsync("MultiPlatform.Mode", value, "MultiPlatform");
        }
    }

    // ===== Giveaway odds =====

    [ObservableProperty]
    private int _tier1Bonus = 1;

    [ObservableProperty]
    private int _tier2Bonus = 2;

    [ObservableProperty]
    private int _tier3Bonus = 3;

    [ObservableProperty]
    private int _followerBonus = 1;

    [ObservableProperty]
    private int _perPlatformBonus = 1;

    [ObservableProperty]
    private int _platformCap = 3;

    // ===== Vyral goals =====

    [ObservableProperty]
    private bool _goalsEnabled = true;

    /// <summary>Exact GoalType strings consumed by GoalEvaluator.</summary>
    public static IReadOnlyList<string> GoalTypeOptions { get; } = new[]
    {
        "LinkedPlatforms", "FollowPlatforms", "SubAnyPlatform", "SubTier",
    };

    /// <summary>Exact RewardType strings consumed by SocialGoalService.</summary>
    public static IReadOnlyList<string> RewardTypeOptions { get; } = new[]
    {
        "Currency", "Multiplier", "Badge",
    };

    public ObservableCollection<SocialGoalRow> Goals { get; } = new();

    // ===== Linked viewers =====

    [ObservableProperty]
    private string _viewerSearchText = string.Empty;

    public ObservableCollection<LinkedAccountRow> LinkedViewers { get; } = new();

    private List<LinkedAccountRow> _allAccountRows = new();

    public ViewerHubViewModel(
        ILogger<ViewerHubViewModel> logger,
        IDataService dataService,
        ILinkCodeService linkCodeService,
        ISocialGoalService socialGoalService,
        IDiscordBotManager discordBotManager)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
        _linkCodeService = linkCodeService ?? throw new ArgumentNullException(nameof(linkCodeService));
        _socialGoalService = socialGoalService ?? throw new ArgumentNullException(nameof(socialGoalService));
        _discordBotManager = discordBotManager ?? throw new ArgumentNullException(nameof(discordBotManager));
    }

    public async Task InitializeAsync()
    {
        try
        {
            LinkingEnabled = await GetBoolAsync("Linking.Enabled", true);
            CodeExpiryMinutes = await GetIntAsync("Linking.CodeExpiryMinutes", 5);
            TwitchLinkingEnabled = await GetBoolAsync("Linking.Platforms.Twitch", true);
            KickLinkingEnabled = await GetBoolAsync("Linking.Platforms.Kick", true);
            YouTubeLinkingEnabled = await GetBoolAsync("Linking.Platforms.YouTube", true);
            DiscordLinkingEnabled = await GetBoolAsync("Linking.Platforms.Discord", true);
            TikTokLinkingEnabled = await GetBoolAsync("Linking.Platforms.TikTok", true);
            FacebookLinkingEnabled = await GetBoolAsync("Linking.Platforms.Facebook", true);
            RumbleLinkingEnabled = await GetBoolAsync("Linking.Platforms.Rumble", true);
            UnifiedBalances = await GetBoolAsync("Linking.UnifiedBalances", true);

            var mode = await _dataService.GetConfigValueAsync("MultiPlatform.Mode");
            MpMode = mode is "OptIn" or "Auto" ? mode : "Off";
            Bonus2 = await GetDoubleAsync("MultiPlatform.Bonus2", 1.25);
            Bonus3 = await GetDoubleAsync("MultiPlatform.Bonus3", 1.5);
            Bonus4 = await GetDoubleAsync("MultiPlatform.Bonus4", 2.0);
            Bonus5 = await GetDoubleAsync("MultiPlatform.Bonus5", 2.5);
            ActiveWatchingBonus = await GetDoubleAsync("MultiPlatform.ActiveWatchingBonus", 1.25);

            Tier1Bonus = await GetIntAsync("Odds.Tier1BonusEntries", 1);
            Tier2Bonus = await GetIntAsync("Odds.Tier2BonusEntries", 2);
            Tier3Bonus = await GetIntAsync("Odds.Tier3BonusEntries", 3);
            FollowerBonus = await GetIntAsync("Odds.FollowerBonusEntries", 1);
            PerPlatformBonus = await GetIntAsync("Odds.LinkedPlatformBonusEntries", 1);
            PlatformCap = await GetIntAsync("Odds.LinkedPlatformBonusCap", 3);

            GoalsEnabled = await GetBoolAsync("Goals.Enabled", true);

            _isLoaded = true;

            await LoadLinkingBotOptionsAsync();
            await LoadGoalsAsync();
            await RefreshPendingCodesAsync();

            StartCodeRefresh();

            _logger.LogInformation("Viewer Hub settings initialized");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initializing Viewer Hub settings");
            StatusMessage = $"Error loading Viewer Hub settings: {ex.Message}";
        }
    }

    /// <summary>
    /// Stops the pending-codes refresh timer. Idempotent (safe to call repeatedly);
    /// invoked by the host when the ViewerHub section is hidden or on teardown.
    /// The timer restarts via <see cref="InitializeAsync"/> or
    /// <see cref="StartCodeRefresh"/> when the section becomes visible again.
    /// </summary>
    public void Cleanup() => StopCodeRefresh();

    // ======================================================================
    // Pending link codes (30s DispatcherTimer)
    // ======================================================================

    /// <summary>Begins the 30s pending-codes refresh loop. Safe to call repeatedly.</summary>
    public void StartCodeRefresh()
    {
        if (_codeTimer is null)
        {
            _codeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
            _codeTimer.Tick += async (_, _) => await RefreshPendingCodesAsync();
        }

        _codeTimer.Start();
    }

    /// <summary>Stops the 30s pending-codes refresh loop.</summary>
    public void StopCodeRefresh() => _codeTimer?.Stop();

    [RelayCommand]
    private async Task RefreshPendingCodesAsync()
    {
        try
        {
            var codes = await _dataService.GetActiveLinkCodesAsync();
            var nowUtc = DateTime.UtcNow;

            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                PendingCodes.Clear();
                foreach (var code in codes.OrderBy(c => c.ExpiresAt))
                {
                    PendingCodes.Add(new LinkCodeRow(this, code, nowUtc));
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error refreshing pending link codes");
        }
    }

    internal async Task DeleteCodeAsync(LinkCodeRow row)
    {
        try
        {
            await _dataService.DeleteLinkCodeAsync(row.Id);
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => PendingCodes.Remove(row));
            StatusMessage = $"Link code {row.Code} deleted";
            _logger.LogInformation("Link code {Code} deleted", row.Code);
            await ClearStatusAfterDelayAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting link code {Code}", row.Code);
            StatusMessage = "Failed to delete link code";
        }
    }

    // ======================================================================
    // Account linking Discord bot (role on the manager's bot rows)
    // ======================================================================

    /// <summary>
    /// Rebuilds the "Account linking Discord bot" ComboBox from the manager's
    /// bot rows and preselects the bot currently holding the linking role
    /// (falls back to "Primary bot (default)" when none is assigned).
    /// Nothing is persisted here — the role flags on the bot rows are the store.
    /// </summary>
    private async Task LoadLinkingBotOptionsAsync()
    {
        try
        {
            var bots = await _discordBotManager.LoadBotsAsync();
            var assignedBotId = await _discordBotManager.GetBotIdForRoleAsync(DiscordBotRoles.Linking);

            var options = new List<LinkingBotOption> { new(null, "Primary bot (default)") };
            foreach (var bot in bots)
            {
                var username = string.IsNullOrWhiteSpace(bot.BotUsername) ? "unknown" : bot.BotUsername!;
                options.Add(new LinkingBotOption(bot.Id, $"{bot.Name} ({username})"));
            }

            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                _suppressLinkingBotSelection = true;
                try
                {
                    LinkingBotOptions.Clear();
                    foreach (var option in options)
                    {
                        LinkingBotOptions.Add(option);
                    }

                    SelectedLinkingBot = LinkingBotOptions.FirstOrDefault(o =>
                        o.BotId != null
                        && string.Equals(o.BotId, assignedBotId, StringComparison.OrdinalIgnoreCase))
                        ?? LinkingBotOptions[0];
                }
                finally
                {
                    _suppressLinkingBotSelection = false;
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading Discord bot options for Viewer Hub linking");
        }
    }

    partial void OnSelectedLinkingBotChanged(LinkingBotOption? value)
    {
        if (_suppressLinkingBotSelection || value is null)
            return;

        _ = ApplyLinkingBotSelectionAsync(value);
    }

    /// <summary>
    /// Selection change: the Primary item clears the linking role (falling back
    /// to Primary routing); any other item assigns the role to that bot — the
    /// manager clears the role from every other bot as part of the assignment.
    /// </summary>
    private async Task ApplyLinkingBotSelectionAsync(LinkingBotOption option)
    {
        try
        {
            if (string.IsNullOrEmpty(option.BotId))
            {
                await _discordBotManager.ClearRoleAsync(DiscordBotRoles.Linking);
                StatusMessage = "Link and delink DMs go to your Primary bot";
            }
            else
            {
                await _discordBotManager.AssignRoleAsync(option.BotId, DiscordBotRoles.Linking, true);
                StatusMessage = "Link and delink DMs now go to this bot";
            }

            _logger.LogInformation("Viewer Hub linking bot set to {BotId}", option.BotId ?? "(primary)");
            await ClearStatusAfterDelayAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting the Viewer Hub linking bot to {BotId}", option.BotId);
            StatusMessage = "Error setting the linking bot";
        }
    }

    // ======================================================================
    // Vyral goals
    // ======================================================================

    private async Task LoadGoalsAsync()
    {
        try
        {
            await _socialGoalService.SeedDefaultsAsync();

            var goals = await _socialGoalService.GetGoalsAsync();
            var rows = new List<SocialGoalRow>();
            foreach (var goal in goals)
            {
                var row = new SocialGoalRow(this, goal);
                row.CompletionCount = await _socialGoalService.GetCompletionCountAsync(goal.Id);
                rows.Add(row);
            }

            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                Goals.Clear();
                foreach (var row in rows)
                {
                    Goals.Add(row);
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading Vyral goals");
            StatusMessage = "Error loading goals";
        }
    }

    [RelayCommand]
    private void AddGoal()
    {
        var row = new SocialGoalRow(this, new SocialGoal
        {
            Name = "New Goal",
            GoalType = "LinkedPlatforms",
            TargetCount = 2,
            RequiredPlatforms = "All",
            RewardType = "Currency",
            RewardValue = 100,
            IsEnabled = true,
            SortOrder = Goals.Count + 1,
        });
        Goals.Add(row);
        StatusMessage = "New goal added — edit the row, then press Save";
    }

    internal async Task SaveGoalAsync(SocialGoalRow row)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(row.Name))
            {
                StatusMessage = "Goal name is required";
                return;
            }

            // Persist a fresh entity built from the row copy — the entity the
            // service tracked elsewhere is never mutated in place.
            await _socialGoalService.SaveGoalAsync(row.ToEntity());
            StatusMessage = $"Goal '{row.Name}' saved";
            _logger.LogInformation("Vyral goal {GoalName} saved (Id={GoalId})", row.Name, row.Id);
            await ClearStatusAfterDelayAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving Vyral goal {GoalName}", row.Name);
            StatusMessage = "Failed to save goal";
        }
    }

    internal async Task DeleteGoalAsync(SocialGoalRow row)
    {
        try
        {
            await _socialGoalService.DeleteGoalAsync(row.Id);
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => Goals.Remove(row));
            StatusMessage = $"Goal '{row.Name}' deleted";
            _logger.LogInformation("Vyral goal {GoalName} deleted", row.Name);
            await ClearStatusAfterDelayAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting Vyral goal {GoalName}", row.Name);
            StatusMessage = "Failed to delete goal";
        }
    }

    [RelayCommand]
    private async Task RefreshGoalsAsync() => await LoadGoalsAsync();

    // ======================================================================
    // Linked viewers (demand-load only)
    // ======================================================================

    partial void OnViewerSearchTextChanged(string value) => ApplyViewerFilter();

    [RelayCommand]
    private async Task RefreshViewersAsync()
    {
        try
        {
            var accounts = await _dataService.GetAllLinkedAccountsAsync();

            // One row per linked account (platform+username) so each account can
            // be unlinked individually; rows stay grouped under their ViewerId.
            var rows = accounts
                .Select(a => new LinkedAccountRow(this, a.ViewerId, a.Username, a.Platform))
                .OrderBy(r => r.ViewerId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(r => r.Platform, StringComparer.OrdinalIgnoreCase)
                .ThenBy(r => r.Username, StringComparer.OrdinalIgnoreCase)
                .ToList();

            _allAccountRows = rows;
            ApplyViewerFilter();
            StatusMessage = $"{rows.Select(r => r.ViewerId).Distinct(StringComparer.OrdinalIgnoreCase).Count()} linked viewer(s), {rows.Count} account(s) loaded";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading linked viewers");
            StatusMessage = "Error loading linked viewers";
        }
    }

    /// <summary>Filters the in-memory list — the search box never hits the database.</summary>
    private void ApplyViewerFilter()
    {
        var query = ViewerSearchText?.Trim();
        IEnumerable<LinkedAccountRow> filtered = _allAccountRows;
        if (!string.IsNullOrEmpty(query))
        {
            filtered = filtered.Where(r =>
                r.ViewerId.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                r.Username.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                r.Platform.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            LinkedViewers.Clear();
            foreach (var row in filtered)
            {
                LinkedViewers.Add(row);
            }
        });
    }

    /// <summary>
    /// Removes a single account (platform+username) from its link group via
    /// <see cref="ILinkCodeService.UnlinkAsync"/> (banned-user override), then
    /// refreshes the list.
    /// </summary>
    internal async Task UnlinkAccountAsync(LinkedAccountRow row)
    {
        try
        {
            var removed = await _linkCodeService.UnlinkAsync(row.Username, row.Platform);
            if (!removed)
            {
                StatusMessage = $"Could not unlink {row.Username} ({row.Platform}) — no matching linked account";
                return;
            }

            StatusMessage = $"Unlinked {row.Username} ({row.Platform}) from viewer {row.ViewerId}";
            _logger.LogInformation("Account {Username} ({Platform}) unlinked from viewer {ViewerId}", row.Username, row.Platform, row.ViewerId);
            await ClearStatusAfterDelayAsync();
            await RefreshViewersAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error unlinking account {Username} ({Platform})", row.Username, row.Platform);
            StatusMessage = $"Failed to unlink {row.Username} ({row.Platform})";
        }
    }

    /// <summary>
    /// Whole-group action: unlinks every account under the row's viewer ID one
    /// by one via <see cref="ILinkCodeService.UnlinkAsync"/> (rather than the
    /// bulk data delete), so every per-platform status row is detached the same
    /// way the single-account path does — then refreshes the list.
    /// </summary>
    internal async Task UnlinkViewerAsync(LinkedAccountRow row)
    {
        try
        {
            // Snapshot first: UnlinkAsync deletes rows as we go.
            var accounts = (await _dataService.GetLinkedAccountsByViewerIdAsync(row.ViewerId)).ToList();

            var removed = 0;
            foreach (var account in accounts)
            {
                if (await _linkCodeService.UnlinkAsync(account.Username, account.Platform))
                {
                    removed++;
                }
                else
                {
                    _logger.LogWarning("Whole-group unlink skipped missing account {Username} ({Platform}) of viewer {ViewerId}",
                        account.Username, account.Platform, row.ViewerId);
                }
            }

            StatusMessage = $"Viewer {row.ViewerId} unlinked ({removed} account(s))";
            _logger.LogInformation("Viewer {ViewerId} unlinked ({Count} account(s))", row.ViewerId, removed);
            await ClearStatusAfterDelayAsync();
            await RefreshViewersAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error unlinking viewer {ViewerId}", row.ViewerId);
            StatusMessage = "Failed to unlink viewer";
        }
    }

    // ======================================================================
    // Config persistence helpers
    // ======================================================================

    partial void OnLinkingEnabledChanged(bool value)
    {
        if (_isLoaded)
        {
            _ = PersistConfigAsync("Linking.Enabled", value.ToString(), "Linking");
        }
    }

    [ObservableProperty]
    private bool _unifiedBalances = true;

    /// <summary>Persisted toggle for the unified-balance pool (default on).</summary>
    partial void OnUnifiedBalancesChanged(bool value)
    {
        if (_isLoaded)
        {
            _ = PersistConfigAsync("Linking.UnifiedBalances", value.ToString(), "ViewerHub");
        }
    }

    partial void OnCodeExpiryMinutesChanged(int value)
    {
        if (_isLoaded)
        {
            _ = PersistConfigAsync("Linking.CodeExpiryMinutes", Math.Max(1, value).ToString(CultureInfo.InvariantCulture), "Linking");
        }
    }

    partial void OnTwitchLinkingEnabledChanged(bool value) => SaveLinkingPlatformToggle("Twitch", value);
    partial void OnKickLinkingEnabledChanged(bool value) => SaveLinkingPlatformToggle("Kick", value);
    partial void OnYouTubeLinkingEnabledChanged(bool value) => SaveLinkingPlatformToggle("YouTube", value);
    partial void OnDiscordLinkingEnabledChanged(bool value) => SaveLinkingPlatformToggle("Discord", value);
    partial void OnTikTokLinkingEnabledChanged(bool value) => SaveLinkingPlatformToggle("TikTok", value);
    partial void OnFacebookLinkingEnabledChanged(bool value) => SaveLinkingPlatformToggle("Facebook", value);
    partial void OnRumbleLinkingEnabledChanged(bool value) => SaveLinkingPlatformToggle("Rumble", value);

    private void SaveLinkingPlatformToggle(string platform, bool value)
    {
        if (_isLoaded)
        {
            _ = PersistConfigAsync($"Linking.Platforms.{platform}", value.ToString(), "Linking");
        }
    }

    partial void OnBonus2Changed(double value) => SaveBonus("MultiPlatform.Bonus2", value);
    partial void OnBonus3Changed(double value) => SaveBonus("MultiPlatform.Bonus3", value);
    partial void OnBonus4Changed(double value) => SaveBonus("MultiPlatform.Bonus4", value);
    partial void OnBonus5Changed(double value) => SaveBonus("MultiPlatform.Bonus5", value);
    partial void OnActiveWatchingBonusChanged(double value) => SaveBonus("MultiPlatform.ActiveWatchingBonus", value);

    private void SaveBonus(string key, double value)
    {
        if (_isLoaded)
        {
            _ = PersistConfigAsync(key, Math.Max(1.0, value).ToString(CultureInfo.InvariantCulture), "MultiPlatform");
        }
    }

    partial void OnTier1BonusChanged(int value) => SaveOdds("Odds.Tier1BonusEntries", value);
    partial void OnTier2BonusChanged(int value) => SaveOdds("Odds.Tier2BonusEntries", value);
    partial void OnTier3BonusChanged(int value) => SaveOdds("Odds.Tier3BonusEntries", value);
    partial void OnFollowerBonusChanged(int value) => SaveOdds("Odds.FollowerBonusEntries", value);
    partial void OnPerPlatformBonusChanged(int value) => SaveOdds("Odds.LinkedPlatformBonusEntries", value);
    partial void OnPlatformCapChanged(int value) => SaveOdds("Odds.LinkedPlatformBonusCap", value);

    private void SaveOdds(string key, int value)
    {
        if (_isLoaded)
        {
            _ = PersistConfigAsync(key, Math.Max(0, value).ToString(CultureInfo.InvariantCulture), "Giveaway");
        }
    }

    partial void OnGoalsEnabledChanged(bool value)
    {
        if (_isLoaded)
        {
            _ = PersistConfigAsync("Goals.Enabled", value.ToString(), "Goals");
        }
    }

    private async Task PersistConfigAsync(string key, string value, string category)
    {
        try
        {
            await _dataService.SetConfigValueAsync(key, value, category);
            StatusMessage = $"{key} saved";
            _logger.LogInformation("{Key} saved: {Value}", key, value);
            await ClearStatusAfterDelayAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving {Key}", key);
            StatusMessage = $"Error saving setting: {ex.Message}";
        }
    }

    private async Task<bool> GetBoolAsync(string key, bool defaultValue)
    {
        var value = await _dataService.GetConfigValueAsync(key);
        return bool.TryParse(value, out var parsed) ? parsed : defaultValue;
    }

    private async Task<int> GetIntAsync(string key, int defaultValue)
    {
        var value = await _dataService.GetConfigValueAsync(key);
        return int.TryParse(value, out var parsed) ? parsed : defaultValue;
    }

    private async Task<double> GetDoubleAsync(string key, double defaultValue)
    {
        var value = await _dataService.GetConfigValueAsync(key);
        return double.TryParse(value, CultureInfo.InvariantCulture, out var parsed) && parsed > 0 ? parsed : defaultValue;
    }

    private async Task ClearStatusAfterDelayAsync()
    {
        await Task.Delay(2000);
        StatusMessage = null;
    }
}

/// <summary>
/// One choice in the Viewer Hub "Account linking Discord bot" ComboBox:
/// the manager bot row's ID (null for the "Primary bot (default)" item)
/// plus its display text, formatted "{Name} ({BotUsername ?? "unknown"})".
/// </summary>
public sealed record LinkingBotOption(string? BotId, string DisplayName);

/// <summary>
/// One pending (active, unredeemed) link code row in Settings → Viewer Hub.
/// </summary>
public partial class LinkCodeRow : ObservableObject
{
    private readonly ViewerHubViewModel _owner;

    public LinkCodeRow(ViewerHubViewModel owner, LinkCode code, DateTime nowUtc)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        Id = code.Id;
        Code = code.Code;
        CreatedBy = string.IsNullOrEmpty(code.CreatedByUsername) ? "—" : code.CreatedByUsername;
        Platform = string.IsNullOrEmpty(code.CreatedByPlatform) ? "—" : code.CreatedByPlatform;
        ExpiresIn = DescribeRemaining(code.ExpiresAt, nowUtc);
    }

    public string Id { get; }
    public string Code { get; }
    public string CreatedBy { get; }
    public string Platform { get; }

    [ObservableProperty]
    private string _expiresIn = "—";

    private static string DescribeRemaining(DateTime expiresAtUtc, DateTime nowUtc)
    {
        var remaining = expiresAtUtc - nowUtc;
        if (remaining <= TimeSpan.Zero)
        {
            return "expired";
        }

        return $"{(int)remaining.TotalMinutes}m {remaining.Seconds}s";
    }

    [RelayCommand]
    private Task DeleteAsync() => _owner.DeleteCodeAsync(this);
}

/// <summary>
/// One editable Vyral goal row. The row is a detached copy — editing never
/// mutates a tracked <see cref="SocialGoal"/>; Save persists a fresh entity.
/// </summary>
public partial class SocialGoalRow : ObservableObject
{
    private readonly ViewerHubViewModel _owner;

    public SocialGoalRow(ViewerHubViewModel owner, SocialGoal goal)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        Id = goal.Id;
        _name = goal.Name;
        _description = goal.Description;
        _goalType = goal.GoalType;
        _targetCount = goal.TargetCount;
        _requiredPlatforms = goal.RequiredPlatforms;
        _rewardType = goal.RewardType;
        _rewardValue = goal.RewardValue;
        _isEnabled = goal.IsEnabled;
        _sortOrder = goal.SortOrder;
    }

    public Guid Id { get; }

    [ObservableProperty] private string _name;
    [ObservableProperty] private string _description;
    [ObservableProperty] private string _goalType;
    [ObservableProperty] private int _targetCount;
    [ObservableProperty] private string _requiredPlatforms;
    [ObservableProperty] private string _rewardType;
    [ObservableProperty] private double _rewardValue;
    [ObservableProperty] private bool _isEnabled;
    [ObservableProperty] private int _sortOrder;

    /// <summary>Read-only label: how many viewers completed this goal.</summary>
    [ObservableProperty]
    private int _completionCount;

    /// <summary>Builds a fresh entity from the row values (never mutates tracked state).</summary>
    public SocialGoal ToEntity() => new()
    {
        Id = Id,
        Name = Name?.Trim() ?? string.Empty,
        Description = Description?.Trim() ?? string.Empty,
        GoalType = GoalType,
        TargetCount = Math.Max(1, TargetCount),
        RequiredPlatforms = string.IsNullOrWhiteSpace(RequiredPlatforms) ? "All" : RequiredPlatforms.Trim(),
        RewardType = RewardType,
        RewardValue = RewardValue,
        IsEnabled = IsEnabled,
        SortOrder = SortOrder,
    };

    [RelayCommand]
    private Task SaveAsync() => _owner.SaveGoalAsync(this);

    [RelayCommand]
    private Task DeleteAsync() => _owner.DeleteGoalAsync(this);
}

/// <summary>
/// One linked account (platform+username) row in the Linked Viewers browser
/// (loaded on Refresh demand only). The Unlink command removes just this
/// account from its link group; UnlinkAll removes every account under the
/// viewer ID.
/// </summary>
public partial class LinkedAccountRow : ObservableObject
{
    private readonly ViewerHubViewModel _owner;

    public LinkedAccountRow(ViewerHubViewModel owner, string viewerId, string username, string platform)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        ViewerId = viewerId;
        Username = username;
        Platform = platform;
    }

    public string ViewerId { get; }
    public string Username { get; }
    public string Platform { get; }

    [RelayCommand]
    private Task UnlinkAsync() => _owner.UnlinkAccountAsync(this);

    [RelayCommand]
    private Task UnlinkAllAsync() => _owner.UnlinkViewerAsync(this);
}
