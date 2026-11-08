using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using VyralBot.Core.Services.ProcessManagement;
using VyralBot.Data;
using VyralBot.Data.Entities;
using VyralBot.Integrations.Discord;

namespace VyralBot.UI.ViewModels;

/// <summary>
/// Per-bot Discord dashboard: the same information as DiscordDashboardViewModel
/// (status, servers, active-channel picker, test message) but bound to one bot
/// resolved from <see cref="IDiscordBotManager"/> by its ID. The bot ID is
/// passed in via <see cref="LoadAsync"/> from MainViewModel navigation.
/// </summary>
public partial class DiscordBotDetailViewModel : ViewModelBase
{
    internal const int ResponsePreviewLength = 140;

    private readonly ILogger<DiscordBotDetailViewModel> _logger;
    private readonly IDiscordBotManager _botManager;
    private readonly IDataService _dataService;
    private readonly IProcessManager _processManager;

    private string? _inviteUrl;

    [ObservableProperty]
    private string _botId = "";

    [ObservableProperty]
    private string _botName = "";

    [ObservableProperty]
    private string _status = "Loading...";

    [ObservableProperty]
    private bool _isConnected = false;

    [ObservableProperty]
    private string _botUsername = "";

    [ObservableProperty]
    private bool _isBusy = false;

    [ObservableProperty]
    private bool _hasGuilds = false;

    [ObservableProperty]
    private bool _inviteUrlAvailable = false;

    [ObservableProperty]
    private string _testMessageStatus = "Click 'Send Test Message' to post a message to the active channel";

    [ObservableProperty]
    private bool _hasCustomCommands;

    /// <summary>True once the custom-command list has loaded (gates the empty state).</summary>
    [ObservableProperty]
    private bool _commandsLoaded;

    // ===== Identity (auto-detected, read-only — populated from the config row) =====

    /// <summary>Detected Discord application ID, "—" until the bot has connected once.</summary>
    [ObservableProperty]
    private string _detectedApplicationId = "—";

    /// <summary>Detected Discord account username, "—" until the bot has connected once.</summary>
    [ObservableProperty]
    private string _detectedBotUsername = "—";

    /// <summary>Detected avatar URL, null until the bot has connected once.</summary>
    [ObservableProperty]
    private string? _detectedAvatarUrl;

    /// <summary>True when <see cref="DetectedAvatarUrl"/> is set (gates the avatar image vs. fallback icon).</summary>
    [ObservableProperty]
    private bool _hasAvatar = false;

    // ===== Bot roles (single-select per role — the manager clears the role elsewhere) =====

    /// <summary>True when THIS bot currently holds the account-linking role.</summary>
    [ObservableProperty]
    private bool _isLinkingBot = false;

    /// <summary>True when THIS bot currently holds the giveaway-announcements role.</summary>
    [ObservableProperty]
    private bool _isGiveawayBot = false;

    // ===== Startup (per-bot "start at app launch" flag persisted on the config row) =====

    /// <summary>True when THIS bot should be started automatically when the app launches.</summary>
    [ObservableProperty]
    private bool _autoStart = false;

    // ===== Welcome + auto-role on join (per-bot, persisted as DiscordBot.{botId}.WelcomeSettings) =====

    /// <summary>True when this bot welcomes new members and/or grants a role on join.</summary>
    [ObservableProperty]
    private bool _welcomeEnabled = false;

    /// <summary>Welcome message template; {user}/{username}/{server} are applied on join.</summary>
    [ObservableProperty]
    private string _welcomeMessage = DiscordBotWelcomeSettings.DefaultMessage;

    /// <summary>Selected welcome channel id; "" = deliberately cleared, null = keep the stored id.</summary>
    [ObservableProperty]
    private string? _welcomeChannelId;

    /// <summary>Selected auto-role id; "" = deliberately cleared, null = keep the stored id.</summary>
    [ObservableProperty]
    private string? _welcomeRoleId;

    /// <summary>Status line under the welcome card's Save button.</summary>
    [ObservableProperty]
    private string _welcomeStatus = "";

    /// <summary>True when the bot is connected inside a guild, so a channel and role can be picked.</summary>
    [ObservableProperty]
    private bool _hasWelcomeOptions = false;

    /// <summary>Reentrancy guard: one welcome-settings save in flight at a time.</summary>
    private bool _isSavingWelcome;

    /// <summary>Guild the pickers were built from (the bot's first guild); persisted as the settings' GuildId.</summary>
    private string? _welcomeGuildId;

    /// <summary>Reentrancy guard: one role toggle in flight at a time.</summary>
    private bool _isUpdatingRole;

    /// <summary>Reentrancy guard: one AutoStart toggle in flight at a time.</summary>
    private bool _isUpdatingAutoStart;

    // ===== Command channels + scheduled messages (per-bot, persisted as
    // DiscordBot.{botId}.AllowedChannels / .ScheduledMessages) =====

    /// <summary>True when the bot is connected inside a guild, so channels can be picked.</summary>
    [ObservableProperty]
    private bool _hasAllowedChannelOptions = false;

    /// <summary>Status line under the command-channels card's Save button.</summary>
    [ObservableProperty]
    private string _allowedChannelsStatus = "";

    /// <summary>True once the schedule list has at least one row (gates the empty state).</summary>
    [ObservableProperty]
    private bool _hasScheduledMessages = false;

    /// <summary>True while the schedule list is below the per-bot bound (gates the ADD button).</summary>
    [ObservableProperty]
    private bool _hasScheduleCapacity = true;

    /// <summary>Status line under the scheduled-messages card's Save button.</summary>
    [ObservableProperty]
    private string _scheduledMessagesStatus = "";

    /// <summary>Reentrancy guard: one allowed-channels save in flight at a time.</summary>
    private bool _isSavingAllowedChannels;

    /// <summary>Reentrancy guard: one scheduled-messages save in flight at a time.</summary>
    private bool _isSavingScheduledMessages;

    /// <summary>Shared channel options (bot's first guild) feeding both cards' pickers.</summary>
    private readonly ObservableCollection<DiscordChannelInfo> _scheduledChannelOptions = new();

    public ObservableCollection<DiscordGuildViewModel> Guilds { get; } = new();

    /// <summary>Checkbox rows for the per-bot allowed-channels card.</summary>
    public ObservableCollection<DiscordChannelPickViewModel> AllowedChannelPicks { get; } = new();

    /// <summary>Editable rows of the per-bot scheduled-messages card.</summary>
    public ObservableCollection<DiscordScheduledMessageRowViewModel> ScheduledMessages { get; } = new();

    /// <summary>Channel options for the welcome channel picker ("(none)" entry first).</summary>
    public ObservableCollection<DiscordChannelInfo> WelcomeChannels { get; } = new();

    /// <summary>Role options for the auto-role picker ("(none)" entry first; @everyone and managed roles skipped).</summary>
    public ObservableCollection<DiscordRoleInfo> WelcomeRoles { get; } = new();

    /// <summary>
    /// Section B rows: this bot's pinned custom commands plus the unscoped
    /// ("all bots") ones, rebuilt by <see cref="LoadBotCommandsAsync"/>.
    /// </summary>
    public ObservableCollection<BotCommandRowViewModel> BotCommands { get; } = new();

    /// <summary>
    /// Section A: the moderation commands every Vyral Discord bot answers to at
    /// runtime (handled by <c>DiscordModCommandHandler</c> — not stored in the
    /// Commands table, so this list is read-only reference text).
    /// </summary>
    public IReadOnlyList<DiscordModCommandInfo> BuiltInModCommands { get; } = new[]
    {
        new DiscordModCommandInfo(
            "kick",
            "kick <@user|userId> [reason]",
            "Kick a member"),
        new DiscordModCommandInfo(
            "ban",
            "ban <@user|userId> [reason]",
            "Ban a user"),
        new DiscordModCommandInfo(
            "unban",
            "unban <userId>",
            "Lift a ban"),
        new DiscordModCommandInfo(
            "mute",
            "mute <@user|userId> <minutes> [reason]",
            "Timeout a member (up to 28 days)"),
        new DiscordModCommandInfo(
            "unmute",
            "unmute <@user|userId>",
            "Remove a timeout"),
        new DiscordModCommandInfo(
            "slowmode",
            "slowmode <seconds 0-21600>",
            "Set slowmode in this channel, or slowmode <#channel|channelId> <seconds>"),
        new DiscordModCommandInfo(
            "purge / clear",
            "purge <1-100>",
            "Bulk delete recent messages"),
        new DiscordModCommandInfo(
            "help",
            "help",
            "Show the command list (everyone)")
    };

    public DiscordBotDetailViewModel(
        ILogger<DiscordBotDetailViewModel> logger,
        IDiscordBotManager botManager,
        IDataService dataService,
        IProcessManager processManager)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _botManager = botManager ?? throw new ArgumentNullException(nameof(botManager));
        _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
        _processManager = processManager ?? throw new ArgumentNullException(nameof(processManager));

        // Live state changes (start/stop elsewhere, gateway ready) refresh this page
        _botManager.BotsChanged += OnBotsChanged;
    }

    private void OnBotsChanged()
    {
        if (string.IsNullOrEmpty(BotId))
            return;

        var app = Application.Current;
        if (app == null)
            return;

        _ = app.Dispatcher.InvokeAsync(async () =>
        {
            try
            {
                await RefreshAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error refreshing Discord bot {BotId} after change", BotId);
            }
        });
    }

    /// <summary>
    /// Loads the dashboard for a specific bot. Called from MainViewModel
    /// navigation each time the detail page is opened.
    /// </summary>
    public async Task LoadAsync(string botId)
    {
        BotId = botId ?? throw new ArgumentNullException(nameof(botId));
        // This VM is a DI singleton reused for every bot — drop the previous
        // bot's cached invite URL so UpdateInviteUrl recomputes it from the
        // token of the bot being loaded.
        _inviteUrl = null;
        Status = "Loading...";
        WelcomeStatus = "";
        await RefreshAsync();
        await LoadBotCommandsAsync();
        await LoadWelcomeAsync();
        await LoadChannelFeaturesAsync();
    }

    /// <summary>
    /// Loads the custom commands this bot answers to: rows pinned to it via
    /// BotId plus the unscoped ones that run on every bot. Built-in table
    /// commands are skipped — the bot-specific moderation commands are listed
    /// separately (read-only) in <see cref="BuiltInModCommands"/>.
    /// </summary>
    [RelayCommand]
    public async Task LoadBotCommandsAsync()
    {
        if (string.IsNullOrEmpty(BotId))
            return;

        try
        {
            var allCommands = await _dataService.GetCommandsAsync();
            var forThisBot = allCommands
                .Where(c => !c.IsBuiltIn)
                .Where(c => string.IsNullOrEmpty(c.BotId)
                            || string.Equals(c.BotId, BotId, StringComparison.OrdinalIgnoreCase))
                .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            BotCommands.Clear();
            foreach (var command in forThisBot)
            {
                BotCommands.Add(new BotCommandRowViewModel(command, BotName));
            }

            HasCustomCommands = BotCommands.Count > 0;
            CommandsLoaded = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading custom commands for bot {BotId}", BotId);
            HasCustomCommands = false;
        }
    }

    /// <summary>
    /// Row action: flips a command's Enabled flag and persists it with
    /// <c>SaveCommandAsync</c>, reverting the row when the save fails.
    /// </summary>
    [RelayCommand]
    private async Task ToggleBotCommandAsync(BotCommandRowViewModel? row)
    {
        if (row?.Command == null)
            return;

        try
        {
            row.IsEnabled = !row.IsEnabled;
            row.Command.IsEnabled = row.IsEnabled;
            row.Command.UpdatedAt = DateTime.UtcNow;
            await _dataService.SaveCommandAsync(row.Command);

            _logger.LogInformation("Toggled custom command {CommandName} to {Status} for bot {BotId}",
                row.Name, row.IsEnabled ? "enabled" : "disabled", BotId);
        }
        catch (Exception ex)
        {
            // Put the row back the way it was — the save never landed.
            row.IsEnabled = !row.IsEnabled;
            row.Command.IsEnabled = row.IsEnabled;
            _logger.LogError(ex, "Error toggling custom command {CommandName} for bot {BotId}",
                row.Name, BotId);
        }
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (string.IsNullOrEmpty(BotId))
            return;

        try
        {
            var bots = await _botManager.LoadBotsAsync();
            var config = bots.FirstOrDefault(b => b.Id == BotId);
            if (config == null)
            {
                BotName = "Unknown bot";
                IsConnected = false;
                Status = "This bot is no longer configured";
                Guilds.Clear();
                HasGuilds = false;
                DetectedApplicationId = "—";
                DetectedBotUsername = "—";
                DetectedAvatarUrl = null;
                HasAvatar = false;
                IsLinkingBot = false;
                IsGiveawayBot = false;
                AutoStart = false;
                return;
            }

            BotName = config.Name;
            UpdateInviteUrl(config.Token);
            UpdateFromService();
            ApplyIdentity(config);
            ApplyRoleState(bots);
            // Startup flag lives on this bot's persisted row (the manager owns
            // the launch-time auto-start, the UI only mirrors the saved value).
            AutoStart = config.AutoStart;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading Discord bot {BotId}", BotId);
            Status = $"Error: {ex.Message}";
        }
    }

    /// <summary>
    /// Fills the read-only "Identity (auto-detected)" fields from the bot's
    /// persisted config row (the manager populates these when the gateway
    /// connects). Falls back to dashes so a never-connected bot still reads well.
    /// </summary>
    private void ApplyIdentity(DiscordBotConfig config)
    {
        DetectedApplicationId = string.IsNullOrWhiteSpace(config.ApplicationId) ? "—" : config.ApplicationId!;
        DetectedBotUsername = string.IsNullOrWhiteSpace(config.BotUsername) ? "—" : config.BotUsername!;
        DetectedAvatarUrl = config.AvatarUrl;
        HasAvatar = !string.IsNullOrWhiteSpace(config.AvatarUrl);
    }

    /// <summary>
    /// Reflects the single-select role state from the FRESH bot rows: each role
    /// belongs to at most one bot, so this page's checkboxes are only checked
    /// when this bot's row holds the flag.
    /// </summary>
    private void ApplyRoleState(IReadOnlyList<DiscordBotConfig> bots)
    {
        var linkingBot = bots.FirstOrDefault(b => b.RoleLinking);
        var giveawayBot = bots.FirstOrDefault(b => b.RoleGiveaway);

        IsLinkingBot = linkingBot != null
            && string.Equals(linkingBot.Id, BotId, StringComparison.OrdinalIgnoreCase);
        IsGiveawayBot = giveawayBot != null
            && string.Equals(giveawayBot.Id, BotId, StringComparison.OrdinalIgnoreCase);
    }

    [RelayCommand]
    private async Task ToggleLinkingRoleAsync(object? parameter)
    {
        await SetRoleAsync(DiscordBotRoles.Linking, parameter is true);
    }

    [RelayCommand]
    private async Task ToggleGiveawayRoleAsync(object? parameter)
    {
        await SetRoleAsync(DiscordBotRoles.Giveaway, parameter is true);
    }

    /// <summary>
    /// Assigns or clears one role for this bot via the manager (AssignRoleAsync
    /// clears the role on other bots and raises BotsChanged), then reloads all
    /// bot rows and re-binds BOTH checkboxes from the fresh rows so the
    /// single-select outcome is reflected immediately.
    /// </summary>
    private async Task SetRoleAsync(string role, bool isChecked)
    {
        if (_isUpdatingRole || string.IsNullOrEmpty(BotId))
            return;

        _isUpdatingRole = true;
        try
        {
            await _botManager.AssignRoleAsync(BotId, role, isChecked);
            _logger.LogInformation("Discord role {Role} {State} for bot {BotId}",
                role, isChecked ? "assigned" : "cleared", BotId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting Discord role {Role} for bot {BotId}", role, BotId);
            Status = "Could not update bot roles — try again";
        }
        finally
        {
            // Reload from the stored rows either way: on success this mirrors the
            // single-select result onto both checkboxes; on failure it reverts.
            try
            {
                var bots = await _botManager.LoadBotsAsync();
                ApplyRoleState(bots);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reloading Discord bot roles for {BotId}", BotId);
            }

            _isUpdatingRole = false;
        }
    }

    [RelayCommand]
    private async Task ToggleAutoStartAsync(object? parameter)
    {
        await SetAutoStartAsync(parameter is true);
    }

    /// <summary>
    /// Flips the "Start this bot when the app launches" flag on THIS bot's
    /// persisted config row (load the fresh row, set AutoStart, save through
    /// the manager's UpdateBotAsync — the same load/flip/save path the
    /// channel picker uses). Reloads the stored rows in the finally block so
    /// the checkbox mirrors the SAVED value: correct after success, reverted
    /// after a failed save. The manager raises BotsChanged, which refreshes
    /// the bots list (and its rocket badge) as well.
    /// </summary>
    private async Task SetAutoStartAsync(bool isEnabled)
    {
        if (_isUpdatingAutoStart || string.IsNullOrEmpty(BotId))
            return;

        _isUpdatingAutoStart = true;
        try
        {
            var bots = await _botManager.LoadBotsAsync();
            var config = bots.FirstOrDefault(b =>
                string.Equals(b.Id, BotId, StringComparison.OrdinalIgnoreCase));
            if (config == null)
            {
                Status = "This bot is no longer configured";
                return;
            }

            var saved = await _botManager.UpdateBotAsync(config with { AutoStart = isEnabled });
            if (saved)
            {
                _logger.LogInformation("AutoStart {State} for bot {BotId}",
                    isEnabled ? "enabled" : "disabled", BotId);
            }
            else
            {
                Status = "Could not save the startup setting — try again";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting AutoStart for bot {BotId}", BotId);
            Status = "Could not update the startup setting — try again";
        }
        finally
        {
            // Reload from the stored row either way: on success this mirrors the
            // saved flag onto the checkbox; on failure it reverts.
            try
            {
                var bots = await _botManager.LoadBotsAsync();
                var config = bots.FirstOrDefault(b =>
                    string.Equals(b.Id, BotId, StringComparison.OrdinalIgnoreCase));
                AutoStart = config?.AutoStart ?? false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reloading AutoStart for bot {BotId}", BotId);
            }

            _isUpdatingAutoStart = false;
        }
    }

    // ==================================================================
    // Welcome + auto-role on member join
    //
    // Settings live in the manager's per-bot store (Configs key
    // "DiscordBot.{botId}.WelcomeSettings"); the manager's join handler reads
    // them live, so a SAVE here applies to the very next member join.
    // ==================================================================

    /// <summary>
    /// Loads this bot's saved welcome/auto-role settings and rebuilds the
    /// card's channel + role pickers from the bot's FIRST guild (the same
    /// first-guild convention as the moderation role tiers). An offline bot
    /// keeps its saved ids; the pickers stay disabled until it connects.
    /// </summary>
    [RelayCommand]
    public async Task LoadWelcomeAsync()
    {
        if (string.IsNullOrEmpty(BotId))
            return;

        try
        {
            var settings = await _botManager.GetWelcomeSettingsAsync(BotId);
            WelcomeEnabled = settings.Enabled;
            WelcomeMessage = string.IsNullOrWhiteSpace(settings.Message)
                ? DiscordBotWelcomeSettings.DefaultMessage
                : settings.Message!;

            WelcomeChannels.Clear();
            WelcomeRoles.Clear();
            _welcomeGuildId = null;
            HasWelcomeOptions = false;

            var service = _botManager.Get(BotId);
            var guild = service?.ConnectedGuilds.FirstOrDefault();
            if (guild != null && service is { IsConnected: true })
            {
                _welcomeGuildId = guild.Id;

                // "" = "(none)" — lets the user deliberately clear a saved pick.
                WelcomeChannels.Add(new DiscordChannelInfo("", "(no welcome channel)"));
                foreach (var channel in guild.TextChannels)
                {
                    WelcomeChannels.Add(channel);
                }

                WelcomeRoles.Add(new DiscordRoleInfo("", "(no auto-role)", 0, 0, false));
                foreach (var role in await service.GetGuildRolesAsync(guild.Id))
                {
                    // @everyone carries the guild id as its role id, and managed
                    // (integration) roles cannot be granted by the bot — neither
                    // is a valid auto-role.
                    if (string.Equals(role.Id, guild.Id, StringComparison.Ordinal) || role.Managed)
                    {
                        continue;
                    }

                    WelcomeRoles.Add(role);
                }

                HasWelcomeOptions = true;
            }

            // Pre-select the saved picks when they are pickable; a null
            // selection keeps the stored id on save (pickers unavailable).
            WelcomeChannelId = WelcomeChannels.Any(c => c.Id == settings.ChannelId)
                ? settings.ChannelId
                : null;
            WelcomeRoleId = WelcomeRoles.Any(r => r.Id == settings.AutoRoleId)
                ? settings.AutoRoleId
                : null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading welcome settings for bot {BotId}", BotId);
            WelcomeStatus = "Could not load the welcome settings";
        }
    }

    /// <summary>
    /// Persists the card: enable toggle, message template, channel and role.
    /// An empty ("(none)") picker selection deliberately clears that id; a
    /// null selection keeps the stored one (the pickers were offline).
    /// </summary>
    [RelayCommand]
    private async Task SaveWelcomeSettingsAsync()
    {
        if (string.IsNullOrEmpty(BotId) || _isSavingWelcome)
            return;

        _isSavingWelcome = true;
        try
        {
            var existing = await _botManager.GetWelcomeSettingsAsync(BotId);

            var channelId = WelcomeChannelId == ""
                ? null
                : WelcomeChannelId ?? existing.ChannelId;
            var roleId = WelcomeRoleId == ""
                ? null
                : WelcomeRoleId ?? existing.AutoRoleId;

            var settings = new DiscordBotWelcomeSettings(
                WelcomeEnabled,
                _welcomeGuildId ?? existing.GuildId,
                channelId,
                string.IsNullOrWhiteSpace(WelcomeMessage) ? null : WelcomeMessage.Trim(),
                roleId);

            await _botManager.SaveWelcomeSettingsAsync(BotId, settings);
            WelcomeStatus = WelcomeEnabled
                ? "Saved — the next member to join gets the welcome treatment"
                : "Saved (welcome is off)";
            _logger.LogInformation("Saved welcome settings for Discord bot {BotId} (enabled: {Enabled})",
                BotId, WelcomeEnabled);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving welcome settings for bot {BotId}", BotId);
            WelcomeStatus = $"Could not save: {ex.Message}";
        }
        finally
        {
            _isSavingWelcome = false;
        }
    }

    // ==================================================================
    // Command channels (per-bot ALLOWED CHANNELS) + Scheduled messages
    //
    // Both persist in the manager's per-bot store (Configs keys
    // "DiscordBot.{botId}.AllowedChannels" / ".ScheduledMessages"). Allowed
    // channels are ENFORCED by CommandService where the bot's commands are
    // matched (mods are never restricted); scheduled messages are fired by
    // the manager's single 30-second timer while the bot is connected.
    // ==================================================================

    /// <summary>
    /// Loads this bot's allowed-channel picks and scheduled messages, and
    /// rebuilds the channel options from the bot's FIRST guild (the same
    /// first-guild convention as the welcome card). An offline bot keeps its
    /// saved schedules; the channel pickers stay disabled until it connects.
    /// </summary>
    [RelayCommand]
    public async Task LoadChannelFeaturesAsync()
    {
        if (string.IsNullOrEmpty(BotId))
            return;

        try
        {
            var allowed = await _botManager.GetAllowedChannelsAsync(BotId);

            AllowedChannelPicks.Clear();
            _scheduledChannelOptions.Clear();
            HasAllowedChannelOptions = false;

            var service = _botManager.Get(BotId);
            var guild = service?.ConnectedGuilds.FirstOrDefault();
            if (guild != null && service is { IsConnected: true })
            {
                foreach (var channel in guild.TextChannels)
                {
                    _scheduledChannelOptions.Add(channel);
                    AllowedChannelPicks.Add(new DiscordChannelPickViewModel(
                        channel.Id,
                        channel.Name,
                        allowed.IsPicked(channel.Id)));
                }

                HasAllowedChannelOptions = AllowedChannelPicks.Count > 0;
            }

            var schedules = await _botManager.GetScheduledMessagesAsync(BotId);
            ScheduledMessages.Clear();
            foreach (var schedule in schedules)
            {
                ScheduledMessages.Add(new DiscordScheduledMessageRowViewModel(
                    _scheduledChannelOptions, schedule));
            }

            UpdateScheduleCapacity();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading channel features for bot {BotId}", BotId);
            AllowedChannelsStatus = "Could not load the command-channel settings";
            ScheduledMessagesStatus = "Could not load the scheduled messages";
        }
    }

    /// <summary>
    /// Persists the checked channels as this bot's allowlist. Nothing checked
    /// = commands work in every channel (legacy behavior).
    /// </summary>
    [RelayCommand]
    private async Task SaveAllowedChannelsAsync()
    {
        if (string.IsNullOrEmpty(BotId) || _isSavingAllowedChannels)
            return;

        _isSavingAllowedChannels = true;
        try
        {
            var picked = AllowedChannelPicks
                .Where(p => p.IsAllowed)
                .Select(p => p.Id)
                .ToList();

            await _botManager.SaveAllowedChannelsAsync(BotId, new DiscordBotAllowedChannels(picked));
            AllowedChannelsStatus = picked.Count == 0
                ? "Saved — commands work in every channel"
                : $"Saved — commands only in {picked.Count} channel(s) (mods are never restricted)";
            _logger.LogInformation("Saved allowed channels for bot {BotId} ({Count} picked)", BotId, picked.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving allowed channels for bot {BotId}", BotId);
            AllowedChannelsStatus = $"Could not save: {ex.Message}";
        }
        finally
        {
            _isSavingAllowedChannels = false;
        }
    }

    /// <summary>Adds one draft row (disabled, 30-minute default interval); SAVE persists it.</summary>
    [RelayCommand]
    private void AddScheduledMessage()
    {
        if (!HasScheduleCapacity)
            return;

        ScheduledMessages.Add(new DiscordScheduledMessageRowViewModel(_scheduledChannelOptions)
        {
            Enabled = false,
            IntervalText = DiscordBotScheduledMessage.MinIntervalMinutes.ToString()
        });
        UpdateScheduleCapacity();
        ScheduledMessagesStatus = "Draft added — pick a channel, write the message, then SAVE";
    }

    /// <summary>Row action: drops the row from the editor (nothing is persisted until SAVE).</summary>
    [RelayCommand]
    private void RemoveScheduledMessage(DiscordScheduledMessageRowViewModel? row)
    {
        if (row == null)
            return;

        ScheduledMessages.Remove(row);
        UpdateScheduleCapacity();
    }

    /// <summary>
    /// Validates the rows (enabled schedules need a channel and a message;
    /// the interval must parse and is clamped to 5..1440 minutes) and
    /// persists them. Saving restarts every schedule's cycle one full
    /// interval out.
    /// </summary>
    [RelayCommand]
    private async Task SaveScheduledMessagesAsync()
    {
        if (string.IsNullOrEmpty(BotId) || _isSavingScheduledMessages)
            return;

        _isSavingScheduledMessages = true;
        try
        {
            var messages = new List<DiscordBotScheduledMessage>();
            var index = 0;
            foreach (var row in ScheduledMessages)
            {
                index++;
                var message = row.Message?.Trim();

                // Drop blank disabled drafts instead of failing the save.
                if (!row.Enabled && string.IsNullOrWhiteSpace(message))
                {
                    continue;
                }

                if (!int.TryParse(row.IntervalText?.Trim(), out var interval))
                {
                    ScheduledMessagesStatus = $"Schedule {index}: the interval must be a number of minutes";
                    return;
                }

                if (row.Enabled
                    && (string.IsNullOrWhiteSpace(message) || string.IsNullOrEmpty(row.SelectedChannelId)))
                {
                    ScheduledMessagesStatus = $"Schedule {index}: enabled schedules need a channel and a message";
                    return;
                }

                messages.Add(new DiscordBotScheduledMessage(
                    row.Id,
                    row.Enabled,
                    row.SelectedChannelId,
                    interval,
                    message));
            }

            if (messages.Count > DiscordBotScheduledMessage.MaxPerBot)
            {
                messages = messages.Take(DiscordBotScheduledMessage.MaxPerBot).ToList();
            }

            await _botManager.SaveScheduledMessagesAsync(BotId, messages);

            // Re-load so the rows mirror the persisted, bounded, clamped list.
            var saved = await _botManager.GetScheduledMessagesAsync(BotId);
            ScheduledMessages.Clear();
            foreach (var schedule in saved)
            {
                ScheduledMessages.Add(new DiscordScheduledMessageRowViewModel(
                    _scheduledChannelOptions, schedule));
            }

            UpdateScheduleCapacity();
            var enabledCount = saved.Count(s => s.Enabled);
            ScheduledMessagesStatus = enabledCount == 0
                ? $"Saved {saved.Count} schedule(s) — none enabled yet"
                : $"Saved — {enabledCount} schedule(s) run while this bot is connected (first post one interval after saving)";
            _logger.LogInformation("Saved {Count} scheduled message(s) for bot {BotId} ({Enabled} enabled)",
                saved.Count, BotId, enabledCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving scheduled messages for bot {BotId}", BotId);
            ScheduledMessagesStatus = $"Could not save: {ex.Message}";
        }
        finally
        {
            _isSavingScheduledMessages = false;
        }
    }

    /// <summary>Keeps the ADD button's enabled state in step with the per-bot row bound.</summary>
    private void UpdateScheduleCapacity()
    {
        HasScheduleCapacity = ScheduledMessages.Count < DiscordBotScheduledMessage.MaxPerBot;
        HasScheduledMessages = ScheduledMessages.Count > 0;
    }

    [RelayCommand]
    private async Task StartAsync()
    {
        if (IsBusy || string.IsNullOrEmpty(BotId))
            return;
        _logger.LogInformation("Starting Discord bot {BotId}", BotId);
        IsBusy = true;
        Status = "Starting...";
        try
        {
            var started = await _botManager.StartBotAsync(BotId);
            if (!started)
            {
                Status = "Could not start — check the bot token";
                return;
            }

            // Give the gateway a moment to report guilds, like the platforms page does
            await Task.Delay(1500);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting Discord bot {BotId}", BotId);
            Status = $"Error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            UpdateFromService();
            // The gateway now knows the guild's channels/roles — rebuild the
            // welcome card's pickers against them.
            await LoadWelcomeAsync();
            // Same for the command-channels + scheduled-messages cards.
            await LoadChannelFeaturesAsync();
        }
    }

    [RelayCommand]
    private async Task StopAsync()
    {
        if (IsBusy || string.IsNullOrEmpty(BotId))
            return;

        _logger.LogInformation("Stopping Discord bot {BotId}", BotId);
        IsBusy = true;
        Status = "Stopping...";
        try
        {
            await _botManager.StopBotAsync(BotId);
            TestMessageStatus = "This bot is stopped";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error stopping Discord bot {BotId}", BotId);
            Status = $"Error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            UpdateFromService();
        }
    }

    [RelayCommand]
    private async Task ReconnectAsync()
    {
        if (IsBusy || string.IsNullOrEmpty(BotId))
            return;

        var service = _botManager.Get(BotId);
        if (service == null)
        {
            Status = "Start the bot first, then reconnect";
            return;
        }

        _logger.LogInformation("Reconnecting Discord bot {BotId}", BotId);
        IsBusy = true;
        Status = "Reconnecting...";
        try
        {
            // Stop+start via the manager so the bot reconnects with ITS OWN token.
            // service.ReconnectAsync() reads the shared legacy Platform row, which
            // only holds the primary bot's token.
            await _botManager.StopBotAsync(BotId);
            var ok = await _botManager.StartBotAsync(BotId);
            Status = ok ? "Reconnected" : "Reconnect failed — check this bot's token";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reconnecting Discord bot {BotId}", BotId);
            Status = $"Error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            UpdateFromService();
            // Reconnect = fresh gateway session: rebuild the welcome pickers
            // so they reflect what the bot can see right now.
            await LoadWelcomeAsync();
            // Same for the command-channels + scheduled-messages cards.
            await LoadChannelFeaturesAsync();
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

        var service = _botManager.Get(BotId);
        if (service == null)
        {
            TestMessageStatus = "Start the bot first, then set the active channel";
            return;
        }

        try
        {
            await service.SetSelectedChannelAsync(channelId);

            // Persist the choice on the bot's saved configuration
            var bots = await _botManager.LoadBotsAsync();
            var config = bots.FirstOrDefault(b => b.Id == BotId);
            if (config != null)
            {
                await _botManager.UpdateBotAsync(config with { SelectedChannelId = channelId });
            }

            var channelName = guild.Channels.FirstOrDefault(c => c.Id == channelId)?.Name ?? channelId;
            TestMessageStatus = $"Active channel set to #{channelName}";
            _logger.LogInformation("Active channel for bot {BotId} set to {ChannelId}", BotId, channelId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting active channel for bot {BotId}", BotId);
            TestMessageStatus = $"Failed to set channel: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task SendTestMessageAsync()
    {
        var service = _botManager.Get(BotId);
        if (service?.IsConnected != true)
        {
            TestMessageStatus = "This bot is not connected";
            return;
        }

        TestMessageStatus = "Creating a private test channel and pinging the server owner...";
        try
        {
            TestMessageStatus = await service.SendOwnerTestMessageAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending owner test message for bot {BotId}", BotId);
            TestMessageStatus = $"Error: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task OpenInviteLink()
    {
        if (_inviteUrl is null)
            return;

        try
        {
            // No WebView2 here — open the invite in the default browser
            await _processManager.StartProcessAsync(
                new ProcessStartInfo { FileName = _inviteUrl, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open Discord invite link for bot {BotId}", BotId);
            Status = "Could not open the invite link";
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
        var service = _botManager.Get(BotId);
        IsConnected = service?.IsConnected == true;
        BotUsername = service?.BotUsername ?? "";
        Status = IsConnected
            ? (string.IsNullOrEmpty(BotUsername) ? "Connected" : $"Connected as {BotUsername}")
            : "Offline";

        Guilds.Clear();
        if (service != null)
        {
            var activeChannelId = service.SelectedChannelId;
            foreach (var guild in service.ConnectedGuilds)
            {
                Guilds.Add(new DiscordGuildViewModel(guild, activeChannelId));
            }
        }
        HasGuilds = Guilds.Count > 0;
    }
}

/// <summary>
/// One row of the "Custom commands for this bot" list on the per-bot
/// dashboard: the command's display fields plus a live Enabled flag so the
/// row's toggle button tracks saves made from this page.
/// </summary>
public partial class BotCommandRowViewModel : ObservableObject
{
    public Command Command { get; }

    public string Name => Command.Name;

    /// <summary>Response trimmed to one preview line (full text lives in the builder).</summary>
    public string ResponsePreview { get; }

    /// <summary>"all bots" for unscoped commands, otherwise the bot's own name.</summary>
    public string Scope { get; }

    [ObservableProperty]
    private bool _isEnabled;

    public BotCommandRowViewModel(Command command, string botName)
    {
        Command = command;
        _isEnabled = command.IsEnabled;
        Scope = string.IsNullOrEmpty(command.BotId) ? "all bots" : botName;

        var response = (command.Response ?? string.Empty)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Trim();
        ResponsePreview = response.Length <= DiscordBotDetailViewModel.ResponsePreviewLength
            ? response
            : response[..(DiscordBotDetailViewModel.ResponsePreviewLength - 1)] + "…";
    }
}

/// <summary>Read-only reference entry for a built-in Discord moderation command.</summary>
public sealed record DiscordModCommandInfo(string Name, string Usage, string Description);

/// <summary>
/// One channel row of the "Command channels" card: the channel's display
/// fields plus a live checked flag tracking whether it is on this bot's
/// allowed-channel list.
/// </summary>
public partial class DiscordChannelPickViewModel : ObservableObject
{
    public string Id { get; }
    public string Name { get; }

    /// <summary>True while this channel is on the bot's allowed-channel list.</summary>
    [ObservableProperty]
    private bool _isAllowed;

    public DiscordChannelPickViewModel(string id, string name, bool isAllowed)
    {
        Id = id;
        Name = name;
        _isAllowed = isAllowed;
    }
}

/// <summary>
/// One editable row of the "Scheduled messages" card. Rows are drafts until
/// SAVE persists them through the manager (which restarts every schedule's
/// cycle); channel options come from the shared per-page collection so all
/// rows stay in step when the bot reconnects.
/// </summary>
public partial class DiscordScheduledMessageRowViewModel : ObservableObject
{
    public string Id { get; }

    [ObservableProperty]
    private bool _enabled;

    [ObservableProperty]
    private string _message = "";

    /// <summary>Interval in minutes as typed (validated + clamped on save; min 5).</summary>
    [ObservableProperty]
    private string _intervalText = "30";

    /// <summary>Selected channel id; null = not picked yet.</summary>
    [ObservableProperty]
    private string? _selectedChannelId;

    /// <summary>Channel options shared with the page (bot's first guild, read-only).</summary>
    public ObservableCollection<DiscordChannelInfo> Channels { get; }

    public DiscordScheduledMessageRowViewModel(ObservableCollection<DiscordChannelInfo> channels)
    {
        Channels = channels;
        Id = Guid.NewGuid().ToString("N");
    }

    public DiscordScheduledMessageRowViewModel(
        ObservableCollection<DiscordChannelInfo> channels,
        DiscordBotScheduledMessage schedule)
    {
        Channels = channels;
        Id = schedule.Id;
        _enabled = schedule.Enabled;
        _message = schedule.Message ?? string.Empty;
        _intervalText = schedule.EffectiveIntervalMinutes.ToString();
        _selectedChannelId = schedule.ChannelId;
    }
}

/// <summary>
/// Toggle-button label for a custom command row: shows the action clicking
/// performs — "DISABLE" while enabled, "ENABLE" while disabled.
/// </summary>
public class BotCommandToggleLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is true ? "DISABLE" : "ENABLE";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
