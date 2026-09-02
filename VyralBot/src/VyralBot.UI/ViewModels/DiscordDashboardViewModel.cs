using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
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
/// Discord dashboard page: shows the bot connection status, the servers the bot is in,
/// the active-channel picker, and a test-message button. Patterned on TwitchDashboardViewModel
/// (InitializeAsync + status strings) — no WebView2 involved.
/// </summary>
public partial class DiscordDashboardViewModel : ViewModelBase
{
    private readonly ILogger<DiscordDashboardViewModel> _logger;
    private readonly IDataService _dataService;
    private readonly IDiscordService _discordService;

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

    public ObservableCollection<DiscordGuildViewModel> Guilds { get; } = new();

    public DiscordDashboardViewModel(
        ILogger<DiscordDashboardViewModel> logger,
        IDataService dataService,
        IDiscordService discordService)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
        _discordService = discordService ?? throw new ArgumentNullException(nameof(discordService));
    }

    public async Task InitializeAsync()
    {
        _logger.LogInformation("Initializing Discord dashboard");

        try
        {
            var platform = await _dataService.GetPlatformByNameAsync("Discord");
            UpdateInviteUrl(platform?.AccessToken);

            // Re-connect the shared bot client from the stored token so the dashboard shows live data
            if (!_discordService.IsConnected &&
                platform?.Status == "Connected" &&
                !string.IsNullOrWhiteSpace(platform.AccessToken))
            {
                await _discordService.ConnectAsync(platform.AccessToken);
            }

            UpdateFromService();
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
    private void OpenInviteLink()
    {
        if (_inviteUrl is null)
            return;

        try
        {
            // No WebView2 on the dashboard — open the invite in the default browser
            Process.Start(new ProcessStartInfo { FileName = _inviteUrl, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open Discord invite link");
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
        IsConnected = _discordService.IsConnected;
        BotUsername = _discordService.BotUsername ?? "";
        Status = IsConnected ? $"Connected as {BotUsername}" : "Not connected";

        Guilds.Clear();
        var activeChannelId = _discordService.SelectedChannelId;
        foreach (var guild in _discordService.ConnectedGuilds)
        {
            Guilds.Add(new DiscordGuildViewModel(guild, activeChannelId));
        }
        HasGuilds = Guilds.Count > 0;
    }
}
