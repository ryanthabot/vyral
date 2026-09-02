using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;
using VyralBot.Data;
using VyralBot.Data.Entities;
using VyralBot.UI.Services;

namespace VyralBot.UI.ViewModels;

/// <summary>
/// One row in Settings → Platforms → "Startup & Connection": per-platform
/// auto-connect-on-launch, refresh-tokens-on-launch, chat routing and
/// on-demand connect/disconnect/token refresh.
/// </summary>
public partial class PlatformStartupItem : ObservableObject
{
    private readonly PlatformStartupSettingsViewModel _owner;
    private bool _isInitialized;

    public PlatformStartupItem(
        PlatformStartupSettingsViewModel owner,
        string platformName,
        string description,
        bool supportsTokenRefresh,
        bool isImplemented = true)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        PlatformName = platformName;
        Description = description;
        SupportsTokenRefresh = supportsTokenRefresh;
        IsImplemented = isImplemented;
    }

    public string PlatformName { get; }
    public string Description { get; }
    public bool SupportsTokenRefresh { get; }
    public bool IsImplemented { get; }

    [ObservableProperty]
    private string _statusText = "Not set up";

    [ObservableProperty]
    private bool _isConfigured;

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private bool _autoConnectOnStartup;

    [ObservableProperty]
    private bool _refreshTokensOnStartup = true;

    [ObservableProperty]
    private bool _isBusy;

    partial void OnAutoConnectOnStartupChanged(bool value)
    {
        if (_isInitialized)
        {
            _ = _owner.PersistAutoConnectAsync(PlatformName, value);
        }
    }

    partial void OnRefreshTokensOnStartupChanged(bool value)
    {
        if (_isInitialized)
        {
            _ = _owner.PersistRefreshTokensAsync(PlatformName, value);
        }
    }

    public void BeginInit() => _isInitialized = false;

    public void EndInit() => _isInitialized = true;

    [RelayCommand]
    private Task ConnectAsync() => _owner.ConnectPlatformAsync(this);

    [RelayCommand]
    private Task DisconnectAsync() => _owner.DisconnectPlatformAsync(this);

    [RelayCommand]
    private Task RefreshTokensAsync() => _owner.RefreshPlatformTokensAsync(this);
}

/// <summary>
/// Settings → Platforms panel: per-platform startup & connection behavior plus the
/// Discord bots and webhook-server startup toggles.
/// </summary>
public partial class PlatformStartupSettingsViewModel : ViewModelBase
{
    private readonly ILogger<PlatformStartupSettingsViewModel> _logger;
    private readonly IDataService _dataService;
    private readonly PlatformConnectionService _connectionService;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private bool _discordBotsAutoConnect = true;

    [ObservableProperty]
    private int _discordBotCount;

    public ObservableCollection<PlatformStartupItem> Platforms { get; } = new();

    public PlatformStartupSettingsViewModel(
        ILogger<PlatformStartupSettingsViewModel> logger,
        IDataService dataService,
        PlatformConnectionService connectionService)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
        _connectionService = connectionService ?? throw new ArgumentNullException(nameof(connectionService));

        Platforms.Add(new PlatformStartupItem(this, "Twitch", "Twitch IRC chat + events", supportsTokenRefresh: true));
        Platforms.Add(new PlatformStartupItem(this, "Kick", "Kick chat + webhooks", supportsTokenRefresh: true));
        Platforms.Add(new PlatformStartupItem(this, "YouTube", "YouTube Live Chat", supportsTokenRefresh: false));
        Platforms.Add(new PlatformStartupItem(this, "Discord", "Primary Discord bot", supportsTokenRefresh: false));
        Platforms.Add(new PlatformStartupItem(this, "TikTok", "Not available yet", supportsTokenRefresh: false, isImplemented: false));
    }

    public async Task InitializeAsync()
    {
        try
        {
            foreach (var item in Platforms)
            {
                item.BeginInit();
            }

            foreach (var item in Platforms)
            {
                await LoadItemAsync(item);
            }

            foreach (var item in Platforms)
            {
                item.EndInit();
            }

            // Discord bots master toggle + count
            var bots = await _dataService.GetDiscordBotsAsync();
            DiscordBotCount = bots.Count();
            var botsAutoStart = await _dataService.GetConfigValueAsync("Startup.DiscordBots.AutoConnect");
            if (bool.TryParse(botsAutoStart, out var autoStart))
            {
                DiscordBotsAutoConnect = autoStart;
            }

            _isLoaded = true;

            _logger.LogInformation("Platform startup settings initialized");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initializing platform startup settings");
            StatusMessage = $"Error loading platform settings: {ex.Message}";
        }
    }

    private async Task LoadItemAsync(PlatformStartupItem item)
    {
        var platform = await _dataService.GetPlatformByNameAsync(item.PlatformName);
        var isConfigured = platform != null && !string.IsNullOrEmpty(platform.AccessToken);

        item.IsConfigured = isConfigured && item.IsImplemented;
        item.IsConnected = _connectionService.IsPlatformConnected(item.PlatformName);
        item.AutoConnectOnStartup = ReadAutoStartFlag(item.PlatformName, platform);
        item.RefreshTokensOnStartup = await ReadRefreshTokensAsync(item.PlatformName);

        if (!item.IsImplemented)
        {
            item.StatusText = "Not available yet";
        }
        else if (item.IsConnected)
        {
            item.StatusText = $"Connected as {platform?.Username ?? item.PlatformName}";
        }
        else if (isConfigured)
        {
            item.StatusText = "Signed in — not connected";
        }
        else
        {
            item.StatusText = "Not set up";
        }
    }

    internal async Task PersistAutoConnectAsync(string platformName, bool value)
    {
        try
        {
            var platform = await _dataService.GetPlatformByNameAsync(platformName);
            if (platform == null)
            {
                // No row yet (never signed in): store nothing — the flag is
                // created when the platform is first connected.
                StatusMessage = $"{platformName}: sign in first to save startup settings.";
                return;
            }

            switch (platformName)
            {
                case "Twitch": platform.TwitchChatAutoStart = value; break;
                case "Kick": platform.KickChatAutoStart = value; break;
                case "YouTube": platform.YoutubeChatAutoStart = value; break;
                case "TikTok": platform.TiktokChatAutoStart = value; break;
                case "Discord": platform.DiscordChatAutoStart = value; break;
            }

            platform.UpdatedAt = DateTime.UtcNow;
            await _dataService.SavePlatformAsync(platform);
            StatusMessage = $"{platformName} will {(value ? "auto-connect" : "not auto-connect")} at startup";
            _logger.LogInformation("{Platform} auto-connect setting saved: {Value}", platformName, value);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving {Platform} auto-connect setting", platformName);
            StatusMessage = $"Error saving setting: {ex.Message}";
        }
    }

    internal async Task PersistRefreshTokensAsync(string platformName, bool value)
    {
        try
        {
            await _dataService.SetConfigValueAsync($"Startup.{platformName}.RefreshTokens", value.ToString(), "Startup");
            StatusMessage = $"{platformName} tokens will {(value ? "refresh" : "not refresh")} at startup";
            _logger.LogInformation("{Platform} refresh-tokens-at-startup saved: {Value}", platformName, value);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving {Platform} token refresh setting", platformName);
            StatusMessage = $"Error saving setting: {ex.Message}";
        }
    }

    internal async Task ConnectPlatformAsync(PlatformStartupItem item)
    {
        if (item.IsBusy) return;

        try
        {
            item.IsBusy = true;
            var (success, message) = await _connectionService.ConnectAsync(item.PlatformName, refreshTokensFirst: item.RefreshTokensOnStartup);
            StatusMessage = message;
            await RefreshItemAsync(item);
        }
        finally
        {
            item.IsBusy = false;
        }
    }

    internal async Task DisconnectPlatformAsync(PlatformStartupItem item)
    {
        if (item.IsBusy) return;

        try
        {
            item.IsBusy = true;
            var (success, message) = await _connectionService.DisconnectAsync(item.PlatformName);
            StatusMessage = message;
            await RefreshItemAsync(item);
        }
        finally
        {
            item.IsBusy = false;
        }
    }

    internal async Task RefreshPlatformTokensAsync(PlatformStartupItem item)
    {
        if (item.IsBusy) return;

        try
        {
            item.IsBusy = true;
            var (success, message) = await _connectionService.RefreshTokensAsync(item.PlatformName);
            StatusMessage = message;
        }
        finally
        {
            item.IsBusy = false;
        }
    }

    private async Task RefreshItemAsync(PlatformStartupItem item)
    {
        item.BeginInit();
        await LoadItemAsync(item);
        item.EndInit();
    }

    private static bool ReadAutoStartFlag(string platformName, Platform? platform)
    {
        return platformName switch
        {
            "Twitch" => platform?.TwitchChatAutoStart ?? false,
            "Kick" => platform?.KickChatAutoStart ?? false,
            "YouTube" => platform?.YoutubeChatAutoStart ?? false,
            "TikTok" => platform?.TiktokChatAutoStart ?? false,
            "Discord" => platform?.DiscordChatAutoStart ?? false,
            _ => false,
        };
    }

    private async Task<bool> ReadRefreshTokensAsync(string platformName)
    {
        var value = await _dataService.GetConfigValueAsync($"Startup.{platformName}.RefreshTokens");
        return !bool.TryParse(value, out var refresh) || refresh;
    }

    partial void OnDiscordBotsAutoConnectChanged(bool value)
    {
        if (_isLoaded)
        {
            _ = PersistDiscordBotsAutoConnectAsync(value);
        }
    }

    private bool _isLoaded;

    private async Task PersistDiscordBotsAutoConnectAsync(bool value)
    {
        try
        {
            await _dataService.SetConfigValueAsync("Startup.DiscordBots.AutoConnect", value.ToString(), "Startup");
            StatusMessage = $"Saved Discord bots will {(value ? "start" : "not start")} at app launch";
            _logger.LogInformation("Discord bots auto-start saved: {Value}", value);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving Discord bots auto-start setting");
            StatusMessage = $"Error saving setting: {ex.Message}";
        }
    }

    [RelayCommand]
    public async Task RefreshStatusAsync()
    {
        foreach (var item in Platforms)
        {
            await RefreshItemAsync(item);
        }
    }
}
