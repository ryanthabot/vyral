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

    public ObservableCollection<DiscordGuildViewModel> Guilds { get; } = new();

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
        IDataService dataService)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _botManager = botManager ?? throw new ArgumentNullException(nameof(botManager));
        _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));

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
        Status = "Loading...";
        await RefreshAsync();
        await LoadBotCommandsAsync();
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
                            || string.Equals(c.BotId, BotId, StringComparison.Ordinal))
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
                return;
            }

            BotName = config.Name;
            UpdateInviteUrl(config.Token);
            UpdateFromService();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading Discord bot {BotId}", BotId);
            Status = $"Error: {ex.Message}";
        }
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
    private void OpenInviteLink()
    {
        if (_inviteUrl is null)
            return;

        try
        {
            // No WebView2 here — open the invite in the default browser
            Process.Start(new ProcessStartInfo { FileName = _inviteUrl, UseShellExecute = true });
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
