using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using VyralBot.Core.Services.ProcessManagement;
using VyralBot.Data;
using VyralBot.Data.Entities;
using VyralBot.Integrations.Discord;

namespace VyralBot.UI.ViewModels;

/// <summary>
/// One row in the Discord dashboard servers list: a guild the bot is in,
/// its member count, and its text channels as a selectable drop-down.
/// </summary>
public partial class DiscordGuildViewModel : ObservableObject
{
    public string Name { get; }

    public string MemberCountText { get; }

    public IReadOnlyList<DiscordChannelInfo> Channels { get; }

    public bool HasChannels => Channels.Count > 0;

    [ObservableProperty]
    private string? _selectedChannelId;

    public DiscordGuildViewModel(DiscordGuildInfo guild, string? activeChannelId)
    {
        Name = guild.Name;
        MemberCountText = guild.MemberCount == 1 ? "1 member" : $"{guild.MemberCount:N0} members";
        Channels = guild.TextChannels;

        // Pre-select the channel matching the service's active channel, if this guild owns it
        _selectedChannelId = activeChannelId is not null && guild.TextChannels.Any(c => c.Id == activeChannelId)
            ? activeChannelId
            : null;
    }
}

/// <summary>
/// One card in the dashboard's bot fleet: live connection state pulled from
/// <see cref="IDiscordBotManager.Get"/>, the servers/channels the bot can see,
/// and the command inventory pinned to the bot (Commands.BotId).
/// </summary>
public partial class DiscordDashboardBotCardViewModel : ObservableObject
{
    public required string BotId { get; init; }

    public required string Name { get; init; }

    /// <summary>Live gateway username, falling back to the row's detected username.</summary>
    public required string UsernameText { get; init; }

    public bool IsConnected { get; init; }

    /// <summary>Status persisted on the DiscordBots row was "Error".</summary>
    public bool IsError { get; init; }

    public string StatusText { get; init; } = "Disconnected";

    /// <summary>Uptime label ("up 1h 12m"), refreshed by the dashboard timer. "—" when offline.</summary>
    [ObservableProperty]
    private string _uptimeText = "—";

    /// <summary>"2 servers • 14 channels • A, B +1 more" (or an offline hint).</summary>
    public string ServersLineText { get; init; } = "not in any servers";

    /// <summary>"8 commands (2 pinned) • 1,234 uses • last used 5m ago".</summary>
    public string CommandsLineText { get; init; } = "0 commands";

    /// <summary>Single-assignment role chips ("linking, giveaway"), empty when none.</summary>
    public string RoleText { get; init; } = "";

    public bool HasRoles => RoleText.Length > 0;
}

/// <summary>
/// One row of the dashboard's "Recent mod actions" list (ModActions table).
/// </summary>
public partial class DiscordModActionItemViewModel
{
    public required string Action { get; init; }

    /// <summary>"ModName → TargetName" summary line.</summary>
    public required string WhoText { get; init; }

    public required string TimeText { get; init; }

    public string? Details { get; init; }
}

/// <summary>
/// One row of the dashboard's "Workflow runs today" list (WorkflowExecutions table).
/// </summary>
public partial class DiscordWorkflowRunItemViewModel
{
    public required string WorkflowName { get; init; }

    public string TriggerSummary { get; init; } = "";

    public bool IsCompleted { get; init; }

    public bool IsFailed { get; init; }

    public string StatusText { get; init; } = "";

    public string DurationText { get; init; } = "";

    public required string TimeText { get; init; }
}

/// <summary>
/// Discord dashboard (DASHBOARD tab of the Bots page): fleet overview of every
/// configured bot (live status, servers, channels, command inventory), KPI
/// strip, recent mod actions, workflow runs today — plus the setup bot's
/// connection card, servers/active-channel picker and test-message button.
/// Data comes from <see cref="IDiscordBotManager"/> (live bot state),
/// <see cref="IDataService"/> (DiscordBots/Commands/ModActions/WorkflowExecutions
/// tables) and the legacy <see cref="IDiscordService"/> (setup bot). A 30s
/// DispatcherTimer refreshes while the tab is visible (started/stopped by the
/// view's IsVisibleChanged) and <see cref="IDiscordBotManager.BotsChanged"/>
/// nudges an immediate refresh.
/// </summary>
public partial class DiscordDashboardViewModel : ViewModelBase, IDisposable
{
    private readonly ILogger<DiscordDashboardViewModel> _logger;
    private readonly IDataService _dataService;
    private readonly IDiscordService _discordService;
    private readonly IDiscordBotManager _botManager;
    private readonly IProcessManager _processManager;

    private const int RecentModActionRows = 5;
    private const int RecentWorkflowRunRows = 5;

    // 30s lightweight refresh while the dashboard tab is visible. All queries
    // hit the local SQLite profile database (small row counts) — cheap.
    private const int AutoRefreshSeconds = 30;

    private readonly DispatcherTimer _refreshTimer;
    private int _loading;                 // re-entrancy guard (timer tick vs BotsChanged vs manual)
    private bool _initializedOnce;        // first InitializeAsync does the full legacy-connect pass
    private readonly Dictionary<string, DateTime> _connectedSince = new(StringComparer.Ordinal);
    private string? _guildSignature;      // skips no-op guild-list rebuilds (protects the channel picker)

    [ObservableProperty]
    private string _status = "Loading...";

    [ObservableProperty]
    private bool _isConnected = false;

    [ObservableProperty]
    private string _botUsername = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotBusy))]
    private bool _isBusy = false;

    /// <summary>Inverse of <see cref="IsBusy"/> for one-way IsEnabled bindings in XAML.</summary>
    public bool IsNotBusy => !IsBusy;

    [ObservableProperty]
    private bool _hasGuilds = false;

    [ObservableProperty]
    private bool _inviteUrlAvailable = false;

    [ObservableProperty]
    private string _testMessageStatus = "Click 'Send Test Message' to post a message to the active channel";

    private string? _inviteUrl;

    // ===== Fleet + KPI =====

    [ObservableProperty]
    private bool _hasBots = false;

    [ObservableProperty]
    private string _onlineBotsText = "0 / 0";

    [ObservableProperty]
    private string _guildsKpiText = "0";

    [ObservableProperty]
    private string _channelsKpiText = "0";

    [ObservableProperty]
    private string _commandsKpiText = "0";

    [ObservableProperty]
    private string _commandsUsesKpiText = "";

    [ObservableProperty]
    private string _workflowsTodayKpiText = "0";

    [ObservableProperty]
    private string _workflowsFailedKpiText = "";

    // ===== Activity lists =====

    [ObservableProperty]
    private bool _hasModActions = false;

    [ObservableProperty]
    private bool _hasWorkflowRunsToday = false;

    [ObservableProperty]
    private string _lastRefreshText = "never";

    public ObservableCollection<DiscordGuildViewModel> Guilds { get; } = new();

    public ObservableCollection<DiscordDashboardBotCardViewModel> Bots { get; } = new();

    public ObservableCollection<DiscordModActionItemViewModel> RecentModActions { get; } = new();

    public ObservableCollection<DiscordWorkflowRunItemViewModel> WorkflowRunsToday { get; } = new();

    public DiscordDashboardViewModel(
        ILogger<DiscordDashboardViewModel> logger,
        IDataService dataService,
        IDiscordService discordService,
        IDiscordBotManager botManager,
        IProcessManager processManager)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
        _discordService = discordService ?? throw new ArgumentNullException(nameof(discordService));
        _botManager = botManager ?? throw new ArgumentNullException(nameof(botManager));
        _processManager = processManager ?? throw new ArgumentNullException(nameof(processManager));

        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(AutoRefreshSeconds) };
        _refreshTimer.Tick += OnRefreshTimerTick;

        // Live status flips (start/stop/error) refresh the fleet immediately.
        _botManager.BotsChanged += OnBotsChanged;
    }

    public async Task InitializeAsync()
    {
        _logger.LogInformation("Initializing Discord dashboard");

        try
        {
            var platform = await _dataService.GetPlatformByNameAsync("Discord");
            UpdateInviteUrl(platform?.AccessToken);

            // First visit only: re-connect the shared bot client from the stored
            // token so the dashboard shows live data. Later visits (the view is
            // cached in the Bots page host) take the cheap refresh path.
            if (!_initializedOnce &&
                !_discordService.IsConnected &&
                platform?.Status == "Connected" &&
                !string.IsNullOrWhiteSpace(platform.AccessToken))
            {
                await _discordService.ConnectAsync(platform.AccessToken);
            }

            _initializedOnce = true;
            UpdateFromService();
            await LoadDashboardDataAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initializing Discord dashboard");
            Status = $"Error: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (IsBusy)
            return;

        _logger.LogInformation("Refreshing Discord dashboard");
        IsBusy = true;
        try
        {
            var platform = await _dataService.GetPlatformByNameAsync("Discord");
            UpdateInviteUrl(platform?.AccessToken);

            if (!_discordService.IsConnected &&
                platform?.Status == "Connected" &&
                !string.IsNullOrWhiteSpace(platform.AccessToken))
            {
                await _discordService.ConnectAsync(platform.AccessToken);
            }

            UpdateFromService();
            await LoadDashboardDataAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error refreshing Discord dashboard");
            Status = $"Error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task DisconnectAsync()
    {
        if (IsBusy)
            return;

        _logger.LogInformation("Disconnecting Discord bot");
        IsBusy = true;
        try
        {
            await _discordService.DisconnectAsync();

            var platform = await _dataService.GetPlatformByNameAsync("Discord");
            if (platform != null)
            {
                platform.Status = "Disconnected";
                platform.UpdatedAt = DateTime.UtcNow;
                await _dataService.SavePlatformAsync(platform);
            }

            UpdateFromService();
            TestMessageStatus = "Discord is disconnected";
            await LoadDashboardDataAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error disconnecting Discord");
            Status = $"Error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ReconnectAsync()
    {
        if (IsBusy)
            return;

        _logger.LogInformation("Reconnecting Discord bot");
        IsBusy = true;
        Status = "Reconnecting...";
        try
        {
            var success = await _discordService.ReconnectAsync();

            UpdateFromService();
            if (success && IsConnected)
            {
                TestMessageStatus = "Discord reconnected";
            }
            else
            {
                Status = "Reconnect failed — no bot token saved, or Discord refused the connection";
                TestMessageStatus = "Discord is not connected";
            }

            await LoadDashboardDataAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reconnecting Discord");
            Status = $"Error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SetChannelAsync(DiscordGuildViewModel? guild)
    {
        if (guild?.SelectedChannelId is not { } channelId)
        {
            TestMessageStatus = "Pick a channel from the drop-down first";
            return;
        }

        try
        {
            await _discordService.SetSelectedChannelAsync(channelId);
            var channelName = guild.Channels.FirstOrDefault(c => c.Id == channelId)?.Name ?? channelId;
            TestMessageStatus = $"Active channel set to #{channelName}";
            _logger.LogInformation("Discord active channel set to {ChannelId}", channelId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting Discord active channel");
            TestMessageStatus = $"Failed to set channel: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task SendTestMessageAsync()
    {
        if (!_discordService.IsConnected)
        {
            TestMessageStatus = "Discord is not connected";
            return;
        }

        TestMessageStatus = "Creating a private test channel and pinging the server owner...";
        try
        {
            TestMessageStatus = await _discordService.SendOwnerTestMessageAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending Discord owner test message");
            TestMessageStatus = $"Error: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task OpenInviteLinkAsync()
    {
        if (_inviteUrl is null)
            return;

        try
        {
            // Golden rule: external processes go through ProcessManager (tracked,
            // zero-orphans) — never a raw Process.Start.
            await _processManager.StartProcessAsync(new System.Diagnostics.ProcessStartInfo
            {
                FileName = _inviteUrl,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open Discord invite link");
            Status = "Could not open the invite link";
        }
    }

    // ==================================================================
    // Auto-refresh lifecycle (view drives this via IsVisibleChanged)
    // ==================================================================

    /// <summary>
    /// Starts the 30s refresh timer (dashboard tab shown) and refreshes
    /// immediately so a re-shown tab never displays stale data.
    /// </summary>
    public void StartAutoRefresh()
    {
        if (!_refreshTimer.IsEnabled)
            _refreshTimer.Start();

        _ = TryLoadDashboardDataAsync();
    }

    /// <summary>Stops the refresh timer (dashboard tab hidden / page unloaded).</summary>
    public void StopAutoRefresh()
    {
        if (_refreshTimer.IsEnabled)
            _refreshTimer.Stop();
    }

    private async void OnRefreshTimerTick(object? sender, EventArgs e)
    {
        await TryLoadDashboardDataAsync();
        // Keep "connected as" and the uptime labels honest between user refreshes.
        UpdateFromService();
        UpdateUptimeLabels();
    }

    private void OnBotsChanged()
    {
        // May fire from background threads — hop to the UI thread (our
        // ObservableCollections live there), same pattern as DiscordBotsViewModel.
        _ = Application.Current.Dispatcher.InvokeAsync(async () =>
        {
            await TryLoadDashboardDataAsync();
            UpdateUptimeLabels();
        });
    }

    /// <summary>Guarded wrapper: skips the load when one is already in flight.</summary>
    private async Task TryLoadDashboardDataAsync()
    {
        if (Interlocked.CompareExchange(ref _loading, 1, 0) != 0)
            return;

        try
        {
            await LoadDashboardDataAsync();
        }
        finally
        {
            Volatile.Write(ref _loading, 0);
        }
    }

    // ==================================================================
    // Dashboard data (bots fleet, KPIs, mod actions, workflow runs)
    // ==================================================================

    /// <summary>
    /// Reloads every dashboard section. Runs on the UI thread (timer tick,
    /// BotsChanged dispatcher hop, InitializeAsync/RefreshAsync from the view);
    /// the awaits inside only resume continuations there.
    /// </summary>
    private async Task LoadDashboardDataAsync()
    {
        try
        {
            var configs = await _botManager.LoadBotsAsync();
            var commands = (await _dataService.GetCommandsAsync()).ToList();
            var modActions = await _dataService.GetModActionsAsync(50);
            var executions = await _dataService.GetWorkflowExecutionsAsync(null, 200);

            BuildFleet(configs, commands);
            BuildKpis(configs, commands, executions);
            BuildModActions(modActions);
            BuildWorkflowRuns(executions);

            LastRefreshText = DateTime.Now.ToString("HH:mm:ss");
        }
        catch (Exception ex)
        {
            // A dashboard refresh must never take the page down.
            _logger.LogError(ex, "Error loading Discord dashboard data");
        }
    }

    private void BuildFleet(IReadOnlyList<DiscordBotConfig> configs, List<Command> commands)
    {
        Bots.Clear();
        foreach (var config in configs.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase))
        {
            var service = _botManager.Get(config.Id);
            var isConnected = service is not null && service.IsConnected;
            var guilds = isConnected ? service!.ConnectedGuilds : Array.Empty<DiscordGuildInfo>();

            // Session-scoped uptime: remember when we FIRST saw this bot connected.
            if (isConnected)
            {
                if (!_connectedSince.TryGetValue(config.Id, out _))
                    _connectedSince[config.Id] = DateTime.UtcNow;
            }
            else
            {
                _connectedSince.Remove(config.Id);
            }

            var pinned = commands.Count(c => c.BotId == config.Id);
            var usable = commands.Where(c => c.BotId == null || c.BotId == config.Id).ToList();
            var uses = usable.Sum(c => c.UsageCount);
            var lastUsed = usable.Where(c => c.LastUsedAt.HasValue).Select(c => c.LastUsedAt!.Value).DefaultIfEmpty().Max();
            var roles = new List<string>();
            if (config.RoleLinking) roles.Add("linking");
            if (config.RoleGiveaway) roles.Add("giveaway");

            var guildNames = guilds.Select(g => g.Name).ToList();
            var guildNamesText = guildNames.Count <= 2
                ? string.Join(", ", guildNames)
                : $"{guildNames[0]}, {guildNames[1]} +{guildNames.Count - 2} more";
            var serversLine = guilds.Count == 0
                ? (isConnected ? "connected but not in any servers" : "offline — servers unknown")
                : $"{(guilds.Count == 1 ? "1 server" : $"{guilds.Count} servers")}" +
                  $" • {guilds.Sum(g => g.TextChannels.Count)} channels" +
                  (guildNamesText.Length > 0 ? $" • {guildNamesText}" : "");
            var commandsLine = $"{usable.Count} commands" +
                               (pinned > 0 ? $" ({pinned} pinned)" : "") +
                               (uses > 0 ? $" • {uses:N0} uses" : " • never used") +
                               (lastUsed != default ? $" • last used {FormatRelative(lastUsed)}" : "");

            Bots.Add(new DiscordDashboardBotCardViewModel
            {
                BotId = config.Id,
                Name = config.Name,
                UsernameText = (isConnected ? service!.BotUsername : config.BotUsername) is { Length: > 0 } username
                    ? username
                    : "identity unknown",
                IsConnected = isConnected,
                IsError = !isConnected && config.Status == "Error",
                StatusText = isConnected
                    ? "Connected"
                    : config.Status == "Error" ? "Error" : "Disconnected",
                UptimeText = isConnected && _connectedSince.TryGetValue(config.Id, out var since)
                    ? FormatUptime(DateTime.UtcNow - since)
                    : "—",
                ServersLineText = serversLine,
                CommandsLineText = commandsLine,
                RoleText = string.Join(", ", roles),
            });
        }

        HasBots = Bots.Count > 0;
    }

    private void BuildKpis(
        IReadOnlyList<DiscordBotConfig> configs,
        List<Command> commands,
        IEnumerable<WorkflowExecution> executions)
    {
        var online = _botManager.Connected.Count;
        OnlineBotsText = $"{online} / {configs.Count}";

        var guildIds = new HashSet<string>(StringComparer.Ordinal);
        var channelCount = 0;
        foreach (var service in _botManager.Connected)
        {
            foreach (var guild in service.ConnectedGuilds)
            {
                guildIds.Add(guild.Id);
                channelCount += guild.TextChannels.Count;
            }
        }

        GuildsKpiText = guildIds.Count.ToString();
        ChannelsKpiText = channelCount.ToString();

        var totalUses = commands.Sum(c => c.UsageCount);
        CommandsKpiText = commands.Count.ToString();
        CommandsUsesKpiText = totalUses > 0 ? $"{totalUses:N0} uses" : "no uses yet";

        var today = executions.Where(x => x.StartedAt.Date == DateTime.Now.Date).ToList();
        var failed = today.Count(x => x.Status == "Failed");
        WorkflowsTodayKpiText = today.Count.ToString();
        WorkflowsFailedKpiText = failed > 0 ? $"{failed} failed" : "";
    }

    private void BuildModActions(IEnumerable<ModAction> modActions)
    {
        RecentModActions.Clear();
        foreach (var action in modActions.Take(RecentModActionRows))
        {
            RecentModActions.Add(new DiscordModActionItemViewModel
            {
                Action = action.Action,
                WhoText = $"{action.ModUsername} → {action.TargetUsername}",
                TimeText = FormatRelative(action.Timestamp),
                Details = action.Details,
            });
        }

        HasModActions = RecentModActions.Count > 0;
    }

    private void BuildWorkflowRuns(IEnumerable<WorkflowExecution> executions)
    {
        WorkflowRunsToday.Clear();
        foreach (var run in executions
                     .Where(x => x.StartedAt.Date == DateTime.Now.Date)
                     .OrderByDescending(x => x.StartedAt)
                     .Take(RecentWorkflowRunRows))
        {
            WorkflowRunsToday.Add(new DiscordWorkflowRunItemViewModel
            {
                WorkflowName = run.WorkflowName ?? run.WorkflowId ?? "workflow",
                TriggerSummary = run.TriggerSummary ?? "",
                IsCompleted = run.Status == "Completed",
                IsFailed = run.Status == "Failed",
                StatusText = run.Status,
                DurationText = run.DurationMs >= 0 ? $"{run.DurationMs:N0} ms" : "",
                TimeText = FormatRelative(run.StartedAt),
            });
        }

        HasWorkflowRunsToday = WorkflowRunsToday.Count > 0;
    }

    /// <summary>Recomputes every fleet card's uptime label (timer cadence).</summary>
    private void UpdateUptimeLabels()
    {
        foreach (var card in Bots)
        {
            if (card.IsConnected && _connectedSince.TryGetValue(card.BotId, out var since))
                card.UptimeText = FormatUptime(DateTime.UtcNow - since);
            else
                card.UptimeText = "—";
        }
    }

    private void UpdateInviteUrl(string? accessToken)
    {
        if (_inviteUrl is not null)
            return;

        _inviteUrl = DiscordWebSetupViewModel.BuildInviteUrlFromToken(accessToken);
        InviteUrlAvailable = _inviteUrl is not null;
    }

    private void UpdateFromService()
    {
        IsConnected = _discordService.IsConnected;
        BotUsername = _discordService.BotUsername ?? "";
        Status = IsConnected ? $"Connected as {BotUsername}" : "Not connected";

        // Rebuild the guild list only when something actually changed: the 30s
        // tick calls this too, and rebuilding would wipe a channel selection
        // the user made in the drop-down but has not applied yet.
        var activeChannelId = _discordService.SelectedChannelId;
        var guilds = _discordService.ConnectedGuilds;
        var signature = $"{IsConnected}|{activeChannelId}|{guilds.Count}|{string.Join(",", guilds.Select(g => $"{g.Id}:{g.TextChannels.Count}"))}";
        if (signature == _guildSignature)
            return;

        _guildSignature = signature;
        Guilds.Clear();
        foreach (var guild in guilds)
        {
            Guilds.Add(new DiscordGuildViewModel(guild, activeChannelId));
        }
        HasGuilds = Guilds.Count > 0;
    }

    private static string FormatUptime(TimeSpan up)
    {
        if (up.TotalMinutes < 1) return "up <1m";
        if (up.TotalHours < 1) return $"up {(int)up.TotalMinutes}m";
        if (up.TotalDays < 1) return $"up {(int)up.TotalHours}h {up.Minutes}m";
        return $"up {(int)up.TotalDays}d {up.Hours}h";
    }

    private static string FormatRelative(DateTime utcTimestamp)
    {
        var delta = DateTime.UtcNow - utcTimestamp;
        if (delta.TotalMinutes < 1) return "just now";
        if (delta.TotalMinutes < 60) return $"{Math.Max(1, (int)delta.TotalMinutes)}m ago";
        if (delta.TotalHours < 24) return $"{(int)delta.TotalHours}h ago";
        return $"{(int)delta.TotalDays}d ago";
    }

    public void Dispose()
    {
        _refreshTimer.Stop();
        _refreshTimer.Tick -= OnRefreshTimerTick;
        _botManager.BotsChanged -= OnBotsChanged;
    }
}
