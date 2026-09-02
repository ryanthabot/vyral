using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using VyralBot.Data;
using VyralBot.UI.Services;

namespace VyralBot.UI.ViewModels;

public partial class ChatHubViewModel : ViewModelBase
{
    private readonly ILogger<ChatHubViewModel> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly IDataService _dataService;
    private readonly PlatformConnectionService _platformConnectionService;

    [ObservableProperty]
    private string _selectedPlatform = "Twitch";

    [ObservableProperty]
    private bool _isTwitchConnected;

    [ObservableProperty]
    private bool _isKickConnected;

    [ObservableProperty]
    private bool _isYouTubeConnected;

    [ObservableProperty]
    private bool _isTikTokConnected;

    [ObservableProperty]
    private bool _isDiscordConnected;

    [ObservableProperty]
    private TwitchChatSettingsViewModel? _twitchSettings;

    [ObservableProperty]
    private KickChatSettingsViewModel? _kickSettings;

    [ObservableProperty]
    private YoutubeChatSettingsViewModel? _youtubeSettings;

    [ObservableProperty]
    private TiktokChatSettingsViewModel? _tiktokSettings;

    [ObservableProperty]
    private DiscordChatSettingsViewModel? _discordSettings;

    [ObservableProperty]
    private bool _isCurrentPlatformConnected;

    [ObservableProperty]
    private ViewModelBase? _currentPlatformSettings;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string? _connectionActionMessage;

    public ChatHubViewModel(
        ILogger<ChatHubViewModel> logger,
        IServiceProvider serviceProvider,
        IDataService dataService,
        PlatformConnectionService platformConnectionService)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _dataService = dataService;
        _platformConnectionService = platformConnectionService;
    }

    public async Task InitializeAsync()
    {
        try
        {
            _logger.LogInformation("Initializing ChatHub...");

            await LoadPlatformConnectionsAsync();

            TwitchSettings = _serviceProvider.GetRequiredService<TwitchChatSettingsViewModel>();
            KickSettings = _serviceProvider.GetRequiredService<KickChatSettingsViewModel>();
            YoutubeSettings = _serviceProvider.GetRequiredService<YoutubeChatSettingsViewModel>();
            TiktokSettings = _serviceProvider.GetRequiredService<TiktokChatSettingsViewModel>();
            DiscordSettings = _serviceProvider.GetRequiredService<DiscordChatSettingsViewModel>();

            await TwitchSettings.InitializeAsync();
            await KickSettings.InitializeAsync();
            await YoutubeSettings.InitializeAsync();
            await TiktokSettings.InitializeAsync();
            await DiscordSettings.InitializeAsync();

            UpdateCurrentPlatform();

            _logger.LogInformation("ChatHub initialized successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initializing ChatHub");
        }
    }

    private async Task LoadPlatformConnectionsAsync()
    {
        try
        {
            var twitch = await _dataService.GetPlatformByNameAsync("Twitch");
            IsTwitchConnected = twitch?.Status == "Connected";

            var kick = await _dataService.GetPlatformByNameAsync("Kick");
            IsKickConnected = kick?.Status == "Connected";

            var youtube = await _dataService.GetPlatformByNameAsync("YouTube");
            IsYouTubeConnected = youtube?.Status == "Connected";

            var tiktok = await _dataService.GetPlatformByNameAsync("TikTok");
            IsTikTokConnected = tiktok?.Status == "Connected";

            var discord = await _dataService.GetPlatformByNameAsync("Discord");
            IsDiscordConnected = discord?.Status == "Connected";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading platform connections");
        }
    }

    private void UpdateCurrentPlatform()
    {
        switch (SelectedPlatform)
        {
            case "Twitch":
                CurrentPlatformSettings = TwitchSettings;
                IsCurrentPlatformConnected = IsTwitchConnected;
                break;
            case "Kick":
                CurrentPlatformSettings = KickSettings;
                IsCurrentPlatformConnected = IsKickConnected;
                break;
            case "YouTube":
                CurrentPlatformSettings = YoutubeSettings;
                IsCurrentPlatformConnected = IsYouTubeConnected;
                break;
            case "TikTok":
                CurrentPlatformSettings = TiktokSettings;
                IsCurrentPlatformConnected = IsTikTokConnected;
                break;
            case "Discord":
                CurrentPlatformSettings = DiscordSettings;
                IsCurrentPlatformConnected = IsDiscordConnected;
                break;
        }
    }

    [RelayCommand]
    private void SelectPlatform(string platform)
    {
        SelectedPlatform = platform;
    }

    /// <summary>True when the selected platform supports connect/disconnect from here (TikTok is not implemented yet).</summary>
    public bool IsCurrentPlatformManageable => SelectedPlatform is "Twitch" or "Kick" or "YouTube" or "Discord";

    /// <summary>True when the selected platform supports manual OAuth token refresh (YouTube refreshes on connect; Discord bot tokens don't expire).</summary>
    public bool CanRefreshCurrentPlatformTokens => SelectedPlatform is "Twitch" or "Kick";

    [RelayCommand]
    private async Task ConnectCurrentPlatformAsync()
    {
        await RunConnectionActionAsync(() => _platformConnectionService.ConnectAsync(SelectedPlatform, refreshTokensFirst: true));
    }

    [RelayCommand]
    private async Task DisconnectCurrentPlatformAsync()
    {
        await RunConnectionActionAsync(() => _platformConnectionService.DisconnectAsync(SelectedPlatform));
    }

    [RelayCommand]
    private async Task RefreshCurrentPlatformTokensAsync()
    {
        await RunConnectionActionAsync(() => _platformConnectionService.RefreshTokensAsync(SelectedPlatform));
    }

    private async Task RunConnectionActionAsync(Func<Task<(bool Success, string Message)>> action)
    {
        if (IsBusy || !IsCurrentPlatformManageable)
        {
            return;
        }

        try
        {
            IsBusy = true;
            ConnectionActionMessage = null;

            var (success, message) = await action();
            ConnectionActionMessage = message;
            _logger.LogInformation("ChatHub {Platform} action: {Message}", SelectedPlatform, message);

            await LoadPlatformConnectionsAsync();
            UpdateCurrentPlatform();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ChatHub connection action failed for {Platform}", SelectedPlatform);
            ConnectionActionMessage = $"Action failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    partial void OnSelectedPlatformChanged(string value)
    {
        UpdateCurrentPlatform();
        OnPropertyChanged(nameof(IsCurrentPlatformManageable));
        OnPropertyChanged(nameof(CanRefreshCurrentPlatformTokens));
        ConnectionActionMessage = null;
    }
}
