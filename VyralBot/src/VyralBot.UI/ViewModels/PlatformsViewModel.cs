using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System.IO;
using VyralBot.Data;
using VyralBot.Data.Entities;
using VyralBot.Integrations.Twitch;
using VyralBot.Integrations.Kick;
using VyralBot.Integrations.YouTube;
using VyralBot.Integrations.Discord;
using VyralBot.Core.Services.Profiles;

namespace VyralBot.UI.ViewModels;

/// <summary>
/// ViewModel for managing platform connections (Twitch, YouTube, etc.).
/// </summary>
public partial class PlatformsViewModel : ViewModelBase
{
    private readonly ILogger<PlatformsViewModel> _logger;
    private readonly IDataService _dataService;
    private readonly ITwitchService _twitchService;
    private readonly TwitchOAuthService _oAuthService;
    private readonly TwitchWebSetupViewModel _webSetupViewModel;
    private readonly IKickService _kickService;
    private readonly KickLibOAuthService _kickOAuthService;
    private readonly KickWebSetupViewModel _kickWebSetupViewModel;
    private readonly IYouTubeService _youtubeService;
    private readonly YouTubeWebSetupViewModel _youtubeWebSetupViewModel;
    private readonly TikTokWebSetupViewModel _tiktokWebSetupViewModel;
    private readonly DiscordWebSetupViewModel _discordWebSetupViewModel;
    private readonly IDiscordService _discordService;
    private readonly IProfileService _profileService;

    private Func<Task>? _onPlatformConnectionChanged;
    private Func<Task>? _onNavigateToPlatforms;
    private Action? _onOpenDiscordSetup;

    [ObservableProperty]
    private string _twitchStatus = "Disconnected";

    [ObservableProperty]
    private bool _isTwitchConnected;

    [ObservableProperty]
    private string _twitchChannel = string.Empty;

    [ObservableProperty]
    private string _twitchUsername = string.Empty;

    [ObservableProperty]
    private string _kickStatus = "Disconnected";

    [ObservableProperty]
    private bool _isKickConnected;

    [ObservableProperty]
    private string _kickUsername = string.Empty;

    [ObservableProperty]
    private string _youtubeStatus = "Disconnected";

    [ObservableProperty]
    private bool _isYouTubeConnected;

    [ObservableProperty]
    private string _youtubeUsername = string.Empty;

    [ObservableProperty]
    private string _tiktokStatus = "Disconnected";

    [ObservableProperty]
    private bool _isTikTokConnected;

    [ObservableProperty]
    private string _tiktokUsername = string.Empty;

    [ObservableProperty]
    private string _discordStatus = "Disconnected";

    [ObservableProperty]
    private bool _isDiscordConnected;

    [ObservableProperty]
    private string _discordUsername = string.Empty;

    // View state properties - control what's currently displayed
    [ObservableProperty]
    private bool _showTwitchConnection = false;

    [ObservableProperty]
    private bool _showKickConnection = false;

    [ObservableProperty]
    private bool _showYouTubeConnection = false;

    [ObservableProperty]
    private bool _showTikTokConnection = false;

    [ObservableProperty]
    private bool _showDiscordConnection = false;

    [ObservableProperty]
    private string _statusMessage = "Configure your Twitch credentials to get started";

    [ObservableProperty]
    private bool _isConnecting;

    [ObservableProperty]
    private bool _needsSetup;

    [ObservableProperty]
    private bool _isConfigured;

    [ObservableProperty]
    private bool _showPlatformSelection = true;

    [ObservableProperty]
    private bool _showTwitchSetup;

    [ObservableProperty]
    private bool _showKickSetup;

    [ObservableProperty]
    private bool _showYouTubeSetup;

    [ObservableProperty]
    private bool _showTikTokSetup;

    [ObservableProperty]
    private bool _showDiscordSetup;

    public TwitchWebSetupViewModel WebSetupViewModel => _webSetupViewModel;
    public KickWebSetupViewModel KickWebSetupViewModel => _kickWebSetupViewModel;
    public YouTubeWebSetupViewModel YouTubeWebSetupViewModel => _youtubeWebSetupViewModel;
    public TikTokWebSetupViewModel TikTokWebSetupViewModel => _tiktokWebSetupViewModel;
    public DiscordWebSetupViewModel DiscordWebSetupViewModel => _discordWebSetupViewModel;

    public PlatformsViewModel(
        ILogger<PlatformsViewModel> logger,
        IDataService dataService,
        ITwitchService twitchService,
        TwitchOAuthService oAuthService,
        TwitchWebSetupViewModel webSetupViewModel,
        IKickService kickService,
        KickLibOAuthService kickOAuthService,
        KickWebSetupViewModel kickWebSetupViewModel,
        IYouTubeService youtubeService,
        YouTubeWebSetupViewModel youtubeWebSetupViewModel,
        TikTokWebSetupViewModel tiktokWebSetupViewModel,
        DiscordWebSetupViewModel discordWebSetupViewModel,
        IDiscordService discordService,
        IProfileService profileService)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
        _twitchService = twitchService ?? throw new ArgumentNullException(nameof(twitchService));
        _oAuthService = oAuthService ?? throw new ArgumentNullException(nameof(oAuthService));
        _webSetupViewModel = webSetupViewModel ?? throw new ArgumentNullException(nameof(webSetupViewModel));
        _kickService = kickService ?? throw new ArgumentNullException(nameof(kickService));
        _kickOAuthService = kickOAuthService ?? throw new ArgumentNullException(nameof(kickOAuthService));
        _kickWebSetupViewModel = kickWebSetupViewModel ?? throw new ArgumentNullException(nameof(kickWebSetupViewModel));
        _youtubeService = youtubeService ?? throw new ArgumentNullException(nameof(youtubeService));
        _youtubeWebSetupViewModel = youtubeWebSetupViewModel ?? throw new ArgumentNullException(nameof(youtubeWebSetupViewModel));
        _tiktokWebSetupViewModel = tiktokWebSetupViewModel ?? throw new ArgumentNullException(nameof(tiktokWebSetupViewModel));
        _discordWebSetupViewModel = discordWebSetupViewModel ?? throw new ArgumentNullException(nameof(discordWebSetupViewModel));
        _discordService = discordService ?? throw new ArgumentNullException(nameof(discordService));
        _profileService = profileService ?? throw new ArgumentNullException(nameof(profileService));

        // Subscribe to setup completion events
        _webSetupViewModel.SetupCompleted += OnSetupCompleted;
        _kickWebSetupViewModel.SetupCompleted += OnKickSetupCompleted;
        _youtubeWebSetupViewModel.SetupCompleted += OnYouTubeSetupCompleted;
        _tiktokWebSetupViewModel.SetupCompleted += OnTikTokSetupCompleted;
        _discordWebSetupViewModel.SetupCompleted += OnDiscordSetupCompleted;
    }

    /// <summary>
    /// Sets a callback to be invoked when platform connections change.
    /// </summary>
    public void SetConnectionChangedCallback(Func<Task> callback)
    {
        _onPlatformConnectionChanged = callback;
    }

    /// <summary>
    /// Sets a callback to be invoked to navigate to Platforms view.
    /// </summary>
    public void SetDiscordSetupCallback(Action openWizard)
    {
        _onOpenDiscordSetup = openWizard;
    }

    public void SetNavigationCallback(Func<Task> callback)
    {
        _onNavigateToPlatforms = callback;
    }

    private async void OnSetupCompleted(object? sender, EventArgs e)
    {
        _logger.LogInformation("Setup completed event received, refreshing platform view");

        // Hide setup
        ShowTwitchSetup = false;

        _logger.LogInformation("Loading Twitch platform data and attempting auto-connect...");

        // Load platform data from database (this includes auto-connect to IRC)
        await LoadTwitchPlatformAsync();

        // Force property change notifications for UI update
        OnPropertyChanged(nameof(IsTwitchConnected));
        OnPropertyChanged(nameof(TwitchStatus));
        OnPropertyChanged(nameof(TwitchUsername));

        // Wait a moment for IRC connection to fully establish
        await Task.Delay(500);

        // Notify that platform connection changed (updates sidebar and chat)
        if (_onPlatformConnectionChanged != null)
        {
            _logger.LogInformation("Notifying platform connection changed after IRC connect");
            await _onPlatformConnectionChanged.Invoke();
        }

        // Navigate to Platforms view to show the connected platform
        if (_onNavigateToPlatforms != null)
        {
            _logger.LogInformation("Auto-navigating to Platforms view after setup complete");
            await _onNavigateToPlatforms.Invoke();
        }

        // Refresh credentials check
        CheckTwitchCredentials();
    }

    private async void OnKickSetupCompleted(object? sender, EventArgs e)
    {
        _logger.LogInformation("Kick setup completed, refreshing platform view");

        // Hide setup
        ShowKickSetup = false;

        _logger.LogInformation("Loading Kick platform data and attempting auto-connect...");

        // Load platform data from database (includes auto-connect if needed)
        await LoadKickPlatformAsync();

        // Force property change notifications for UI update
        OnPropertyChanged(nameof(IsKickConnected));
        OnPropertyChanged(nameof(KickStatus));
        OnPropertyChanged(nameof(KickUsername));

        // Wait a moment for connection to fully establish
        await Task.Delay(500);

        // Notify that platform connection changed (updates sidebar and chat)
        if (_onPlatformConnectionChanged != null)
        {
            _logger.LogInformation("Notifying platform connection changed after Kick connect");
            await _onPlatformConnectionChanged.Invoke();
        }

        // Navigate to Platforms view to show the connected platform
        if (_onNavigateToPlatforms != null)
        {
            _logger.LogInformation("Auto-navigating to Platforms view after Kick setup complete");
            await _onNavigateToPlatforms.Invoke();
        }

        // Refresh credentials check
        CheckKickCredentials();
    }

    private async void OnYouTubeSetupCompleted(object? sender, EventArgs e)
    {
        _logger.LogInformation("YouTube setup completed, refreshing platform view");
        ShowYouTubeSetup = false;
        await LoadYouTubePlatformAsync();

        // Auto-connect YouTube chat service
        try
        {
            var platform = await _dataService.GetPlatformByNameAsync("YouTube");
            if (platform != null &&
                !string.IsNullOrEmpty(platform.AccessToken) &&
                !string.IsNullOrEmpty(platform.Username))
            {
                _logger.LogInformation("🎬 Auto-connecting YouTube chat service for {Username}", platform.Username);
                await _youtubeService.ConnectAsync(
                    platform.Username,
                    platform.AccessToken,
                    platform.RefreshToken,
                    platform.Username);
                _logger.LogInformation("✅ YouTube chat service connected successfully");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to auto-connect YouTube chat service");
        }

        OnPropertyChanged(nameof(IsYouTubeConnected));
        OnPropertyChanged(nameof(YoutubeStatus));
        OnPropertyChanged(nameof(YoutubeUsername));
        await Task.Delay(500);
        if (_onPlatformConnectionChanged != null)
            await _onPlatformConnectionChanged.Invoke();
        if (_onNavigateToPlatforms != null)
            await _onNavigateToPlatforms.Invoke();
    }

    private async void OnTikTokSetupCompleted(object? sender, EventArgs e)
    {
        _logger.LogInformation("TikTok setup completed, refreshing platform view");
        ShowTikTokSetup = false;
        await LoadTikTokPlatformAsync();
        OnPropertyChanged(nameof(IsTikTokConnected));
        OnPropertyChanged(nameof(TiktokStatus));
        OnPropertyChanged(nameof(TiktokUsername));
        await Task.Delay(500);
        if (_onPlatformConnectionChanged != null)
            await _onPlatformConnectionChanged.Invoke();
        if (_onNavigateToPlatforms != null)
            await _onNavigateToPlatforms.Invoke();
    }

    private async void OnDiscordSetupCompleted(object? sender, EventArgs e)
    {
        _logger.LogInformation("Discord setup completed, refreshing platform view");

        // Hide setup
        ShowDiscordSetup = false;

        _logger.LogInformation("Loading Discord platform data and attempting auto-connect...");

        // Load platform data from database (includes auto-connect to the bot gateway)
        await LoadDiscordPlatformAsync();

        // Force property change notifications for UI update
        OnPropertyChanged(nameof(IsDiscordConnected));
        OnPropertyChanged(nameof(DiscordStatus));
        OnPropertyChanged(nameof(DiscordUsername));

        // Wait a moment for the gateway connection to fully establish
        await Task.Delay(500);

        // Notify that platform connection changed (updates sidebar and chat)
        if (_onPlatformConnectionChanged != null)
        {
            _logger.LogInformation("Notifying platform connection changed after Discord connect");
            await _onPlatformConnectionChanged.Invoke();
        }

        // Navigate to Platforms view to show the connected platform
        if (_onNavigateToPlatforms != null)
        {
            _logger.LogInformation("Auto-navigating to Platforms view after Discord setup complete");
            await _onNavigateToPlatforms.Invoke();
        }
    }

    /// <summary>
    /// Initializes the ViewModel by loading platform configuration.
    /// Call this after the ViewModel is created.
    /// </summary>
    public async Task InitializeAsync()
    {
        try
        {
            _logger.LogInformation("Initializing Platforms view");

            // Always show platform selection on load - set BEFORE checking credentials
            ShowPlatformSelection = true;
            ShowTwitchSetup = false;
            ShowKickSetup = false;
            ShowYouTubeSetup = false;
            ShowTikTokSetup = false;
            ShowDiscordSetup = false;

            // Hide all connection views when showing platform selection
            ShowTwitchConnection = false;
            ShowKickConnection = false;
            ShowYouTubeConnection = false;
            ShowTikTokConnection = false;
            ShowDiscordConnection = false;

            _logger.LogInformation("Set ShowPlatformSelection = {ShowPlatformSelection}, ShowTwitchSetup = {ShowTwitchSetup}",
                ShowPlatformSelection, ShowTwitchSetup);

            CheckTwitchCredentials();
            CheckKickCredentials();

            // Load platform connection status on initialization
            await LoadPlatformStatusAsync();

            _logger.LogInformation("After CheckTwitchCredentials: ShowPlatformSelection = {ShowPlatformSelection}, ShowTwitchSetup = {ShowTwitchSetup}",
                ShowPlatformSelection, ShowTwitchSetup);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in InitializeAsync");
            StatusMessage = $"Error initializing: {ex.Message}";
        }
    }

    /// <summary>
    /// Loads the current platform connection status from the database.
    /// </summary>
    private async Task LoadPlatformStatusAsync()
    {
        try
        {
            // Check Twitch status
            var twitchPlatform = await _dataService.GetPlatformByNameAsync("Twitch");
            if (twitchPlatform != null)
            {
                IsTwitchConnected = twitchPlatform.Status == "Connected";
                TwitchStatus = twitchPlatform.Status ?? "Disconnected";
                TwitchUsername = twitchPlatform.Username ?? string.Empty;
                TwitchChannel = twitchPlatform.Username ?? string.Empty;
            }

            // Check Kick status
            var kickPlatform = await _dataService.GetPlatformByNameAsync("Kick");
            if (kickPlatform != null)
            {
                IsKickConnected = kickPlatform.Status == "Connected";
                KickStatus = kickPlatform.Status ?? "Disconnected";
                KickUsername = kickPlatform.Username ?? string.Empty;
            }

            // Check YouTube status — the LIVE chat service is the source of truth;
            // the database row can still claim "Connected" after a failed
            // auto-connect (e.g. an expired Google refresh token).
            var youtubePlatform = await _dataService.GetPlatformByNameAsync("YouTube");
            if (youtubePlatform != null)
            {
                if (_youtubeService.IsConnected)
                {
                    IsYouTubeConnected = true;
                    YoutubeStatus = "Connected";
                }
                else if (_youtubeService.RequiresReauth)
                {
                    IsYouTubeConnected = false;
                    YoutubeStatus = "Re-authorization required";
                    StatusMessage = "YouTube needs re-authorization — run Home → Connections → YouTube to sign in again.";
                }
                else
                {
                    IsYouTubeConnected = youtubePlatform.Status == "Connected";
                    YoutubeStatus = youtubePlatform.Status ?? "Disconnected";

                    // Row says Connected but the live service is down (e.g. the app
                    // exited before the service started): converge by reconnecting
                    // from the stored credentials in the background.
                    if (IsYouTubeConnected && !string.IsNullOrEmpty(youtubePlatform.AccessToken))
                    {
                        _logger.LogInformation("YouTube row is Connected but the chat service is down — reconnecting from stored credentials");
                        _ = ReconnectYouTubeFromStoredCredentialsAsync(youtubePlatform);
                    }
                }
                YoutubeUsername = youtubePlatform.Username ?? string.Empty;
            }

            // Check TikTok status
            var tiktokPlatform = await _dataService.GetPlatformByNameAsync("TikTok");
            if (tiktokPlatform != null)
            {
                IsTikTokConnected = tiktokPlatform.Status == "Connected";
                TiktokStatus = tiktokPlatform.Status ?? "Disconnected";
                TiktokUsername = tiktokPlatform.Username ?? string.Empty;
            }

            // Check Discord status
            var discordPlatform = await _dataService.GetPlatformByNameAsync("Discord");
            if (discordPlatform != null)
            {
                // Only treat Discord as connected when a real bot token is present
                IsDiscordConnected = discordPlatform.Status == "Connected" && !string.IsNullOrEmpty(discordPlatform.AccessToken);
                DiscordStatus = discordPlatform.Status ?? "Disconnected";
                DiscordUsername = discordPlatform.Username ?? string.Empty;
            }

            _logger.LogInformation("Platform status loaded - Twitch: {TwitchStatus}, Kick: {KickStatus}, YouTube: {YouTubeStatus}, TikTok: {TikTokStatus}, Discord: {DiscordStatus}",
                TwitchStatus, KickStatus, YoutubeStatus, TiktokStatus, DiscordStatus);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading platform status");
        }
    }

    private async Task LoadConnectedPlatformsAsync()
    {
        try
        {
            // Check for connected Twitch platform
            var twitchPlatform = await _dataService.GetPlatformByNameAsync("Twitch");
            if (twitchPlatform != null && twitchPlatform.Status != "Connected") twitchPlatform = null;
            if (twitchPlatform != null)
            {
                _logger.LogInformation("Found connected Twitch platform, loading data");
                await LoadTwitchPlatformAsync();
            }

            // Check for connected Kick platform
            var kickPlatform = await _dataService.GetPlatformByNameAsync("Kick");
            if (kickPlatform != null && kickPlatform.Status != "Connected") kickPlatform = null;
            if (kickPlatform != null)
            {
                _logger.LogInformation("Found connected Kick platform, loading data");
                await LoadKickPlatformAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading connected platforms");
        }
    }

    [RelayCommand]
    private async Task SelectTwitchAsync()
    {
        try
        {
            _logger.LogInformation("SelectTwitch called");

            // Check database first
            var platform = await _dataService.GetPlatformByNameAsync("Twitch");
            var isConnected = platform != null && platform.Status == "Connected";

            _logger.LogInformation("Platform check: Exists={Exists}, Connected={Connected}", platform != null, isConnected);

            if (!isConnected)
            {
                _logger.LogInformation("Showing Twitch setup");
                ShowPlatformSelection = false;
                NeedsSetup = true;
                ShowTwitchSetup = true;
            }
            else
            {
                // Already configured, show the connection view
                _logger.LogInformation("Showing Twitch connection view");
                await ShowTwitchConnectionAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in SelectTwitch");
            StatusMessage = $"Error: {ex.Message}";
            ShowPlatformSelection = true;
        }
    }

    /// <summary>
    /// Checks if Twitch credentials are configured via file.
    /// </summary>
    private async void CheckTwitchCredentials()
    {
        try
        {
            var profile = await _profileService.GetCurrentProfileAsync();
            var credentialsFolder = _profileService.GetProfileCredentialsPath(profile.Name);
            var credentialsPath = Path.Combine(credentialsFolder, "twitch_credentials.json");

            var hasCredentials = File.Exists(credentialsPath);

            _logger.LogInformation("Checking credentials at: {Path}, Exists: {Exists}", credentialsPath, hasCredentials);

            NeedsSetup = !hasCredentials;
            IsConfigured = hasCredentials;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking credentials");
            NeedsSetup = true;
            IsConfigured = false;
        }

        // Don't update visibility flags here - let InitializeAsync or SelectTwitch control that

        if (NeedsSetup)
        {
            StatusMessage = "Please complete Twitch setup to continue";
            _logger.LogInformation("Twitch credentials not found. Setup required.");
        }
        else
        {
            _logger.LogInformation("Twitch credentials found");
        }
    }

    /// <summary>
    /// Checks if Kick credentials are configured via file.
    /// </summary>
    private async void CheckKickCredentials()
    {
        try
        {
            var profile = await _profileService.GetCurrentProfileAsync();
            var credentialsFolder = _profileService.GetProfileCredentialsPath(profile.Name);
            var credentialsPath = Path.Combine(credentialsFolder, "kick_credentials.json");

            var hasCredentials = File.Exists(credentialsPath);

            _logger.LogInformation("Checking Kick credentials at: {Path}, Exists: {Exists}", credentialsPath, hasCredentials);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking Kick credentials");
        }
    }

    /// <summary>
    /// Loads Twitch platform data from the database.
    /// </summary>
    public async Task LoadTwitchPlatformAsync()
    {
        try
        {
            _logger.LogInformation("Loading Twitch platform data from database");

            // Ensure database exists
            await _dataService.EnsureDatabaseCreatedAsync();

            var platform = await _dataService.GetPlatformByNameAsync("Twitch");

            if (platform != null)
            {
                TwitchUsername = platform.Username ?? string.Empty;
                TwitchChannel = platform.Username ?? string.Empty;
                IsTwitchConnected = platform.Status == "Connected";
                TwitchStatus = platform.Status;

                _logger.LogInformation("Loaded Twitch account from database - Username: {Username}", TwitchUsername);

                // Auto-connect to Twitch IRC if status is Connected
                if (platform.Status == "Connected" && !_twitchService.IsConnected)
                {
                    try
                    {
                        _logger.LogInformation("Auto-connecting to Twitch IRC for {Channel}", TwitchChannel);

                        if (string.IsNullOrEmpty(platform.AccessToken))
                        {
                            _logger.LogWarning("No access token found, cannot connect to Twitch");
                            return;
                        }

                        var accessToken = TwitchOAuthService.Decrypt(platform.AccessToken);
                        var refreshToken = !string.IsNullOrEmpty(platform.RefreshToken)
                            ? TwitchOAuthService.Decrypt(platform.RefreshToken)
                            : null;
                        var botUsername = TwitchChannel.ToLower();

                        await _twitchService.ConnectAsync(botUsername, accessToken, refreshToken, TwitchChannel);

                        // Enable chat by default after successful auto-connect (like Kick does)
                        if (platform.TwitchChatEnabled == false)
                        {
                            platform.TwitchChatEnabled = true;
                            await _dataService.SavePlatformAsync(platform);
                            _logger.LogInformation("Enabled Twitch chat by default after auto-connect");
                        }

                        _logger.LogInformation("Successfully auto-connected to Twitch IRC for #{Channel}", TwitchChannel);
                        TwitchStatus = $"Connected to #{TwitchChannel}";
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error auto-connecting to Twitch IRC");
                        TwitchStatus = "Connected (Chat offline)";
                    }
                }
            }
            else
            {
                _logger.LogInformation("No Twitch account connected in database");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading Twitch platform configuration");
            StatusMessage = $"Error loading configuration: {ex.Message}";
        }
    }

    /// <summary>
    /// Starts OAuth flow to authenticate with Twitch.
    /// </summary>
    [RelayCommand]
    private async Task ConnectTwitchAsync()
    {
        _logger.LogInformation("ConnectTwitchAsync called - Starting Twitch sign-in");

        try
        {
            IsConnecting = true;
            StatusMessage = "Opening browser to sign in with Twitch...";

            var result = await _oAuthService.AuthenticateAsync();

            _logger.LogInformation("AuthenticateAsync returned, result null: {IsNull}", result == null);

            if (result == null)
            {
                StatusMessage = "Sign-in cancelled or failed";
                return;
            }

            var (accessToken, refreshToken, username, userId) = result.Value;
            TwitchUsername = username;
            TwitchChannel = username;

            // Save platform to database
            var platform = await _dataService.GetPlatformByNameAsync("Twitch");

            if (platform == null)
            {
                platform = new Platform
                {
                    Name = "Twitch",
                    Username = username,
                    UserId = userId,
                    AccessToken = TwitchOAuthService.Encrypt(accessToken),
                    RefreshToken = TwitchOAuthService.Encrypt(refreshToken),
                    TokenExpiresAt = DateTime.UtcNow.AddHours(4),
                    Status = "Connected",
                    ConnectedAt = DateTime.UtcNow
                };
            }
            else
            {
                platform.Username = username;
                // Persist the authenticated user id - EventSub cannot connect without it
                if (!string.IsNullOrEmpty(userId))
                {
                    platform.UserId = userId;
                }
                platform.AccessToken = TwitchOAuthService.Encrypt(accessToken);
                platform.RefreshToken = TwitchOAuthService.Encrypt(refreshToken);
                platform.TokenExpiresAt = DateTime.UtcNow.AddHours(4);
                platform.Status = "Connected";
                platform.ConnectedAt = DateTime.UtcNow;
            }

            await _dataService.SavePlatformAsync(platform);

            IsTwitchConnected = true;
            TwitchStatus = $"Signed in as {username}";
            StatusMessage = $"Successfully signed in as {username}!";

            _logger.LogInformation("Twitch signed in successfully as {Username}", username);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error connecting to Twitch");
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsConnecting = false;
        }
    }

    /// <summary>
    /// Connects to Twitch chat.
    /// </summary>
    [RelayCommand]
    private async Task JoinChannelAsync()
    {
        _logger.LogInformation("JoinChannelAsync called. Channel: {Channel}", TwitchChannel);

        if (string.IsNullOrEmpty(TwitchChannel))
        {
            StatusMessage = "Please enter a channel name";
            _logger.LogWarning("No channel name provided");
            return;
        }

        try
        {
            IsConnecting = true;
            StatusMessage = "Connecting to Twitch chat...";

            var platform = await _dataService.GetPlatformByNameAsync("Twitch");
            if (platform == null || string.IsNullOrEmpty(platform.AccessToken))
            {
                StatusMessage = "Please authenticate with Twitch first";
                _logger.LogWarning("No platform or access token found");
                return;
            }

            _logger.LogInformation("Decrypting access token and connecting to IRC");
            var accessToken = TwitchOAuthService.Decrypt(platform.AccessToken);
            var refreshToken = !string.IsNullOrEmpty(platform.RefreshToken)
                ? TwitchOAuthService.Decrypt(platform.RefreshToken)
                : null;
            var botUsername = TwitchChannel.ToLower(); // Use channel name as bot username for now

            await _twitchService.ConnectAsync(botUsername, accessToken, refreshToken, TwitchChannel);

            platform.Username = TwitchChannel;
            platform.Status = "Connected";
            // Enable chat by default on manual connect
            if (platform.TwitchChatEnabled == false)
            {
                platform.TwitchChatEnabled = true;
                _logger.LogInformation("Enabled Twitch chat by default on connect");
            }
            await _dataService.SavePlatformAsync(platform);

            IsTwitchConnected = true;
            TwitchStatus = $"Connected to #{TwitchChannel}";
            StatusMessage = $"Successfully joined #{TwitchChannel}!";

            _logger.LogInformation("Joined Twitch channel #{Channel}", TwitchChannel);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error joining Twitch channel");
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsConnecting = false;
        }
    }

    /// <summary>
    /// Reconnects to Twitch by initiating OAuth flow to refresh the token.
    /// </summary>
    [RelayCommand]
    private async Task ReconnectTwitchAsync()
    {
        try
        {
            _logger.LogInformation("Reconnecting to Twitch - triggering OAuth flow");
            TwitchStatus = "Re-authenticating...";
            StatusMessage = "Opening Twitch OAuth to refresh token...";

            // Disconnect existing connection cleanly
            if (_twitchService.IsConnected)
            {
                try
                {
                    await _twitchService.DisconnectAsync();
                    _logger.LogInformation("Disconnected existing Twitch connection");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error disconnecting before reconnect");
                }
            }

            // Note: We keep Client ID and Client Secret intact - those are app credentials
            // Only the database token needs to be refreshed via OAuth flow
            _logger.LogInformation("Keeping Client ID/Secret intact, refreshing user OAuth token only");

            // Show Twitch setup wizard starting at step 3 (OAuth) since credentials already exist
            ShowTwitchConnection = false;
            ShowPlatformSelection = false;
            ShowKickSetup = false;
            ShowKickConnection = false;

            // Start at step 3 for reconnect
            if (_webSetupViewModel != null)
            {
                _webSetupViewModel.CurrentStep = 3;
                _logger.LogInformation("Starting Twitch setup wizard at step 3 for reconnect");
            }

            ShowTwitchSetup = true;

            _logger.LogInformation("Twitch OAuth setup wizard displayed for token refresh");
            StatusMessage = "Please complete Twitch authentication to refresh your token";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reconnecting to Twitch");
            TwitchStatus = "Reconnect Failed";
            StatusMessage = $"Failed to reconnect: {ex.Message}";
        }
    }

    /// <summary>
    /// Disconnects from Twitch.
    /// </summary>
    [RelayCommand]
    private async Task DisconnectTwitch()
    {
        _logger.LogInformation("Disconnecting Twitch platform");

        try
        {
            // Update UI first
            ShowTwitchConnection = false;
            ShowTwitchSetup = false;

            // Disconnect service
            try
            {
                if (_twitchService.IsConnected)
                {
                    await _twitchService.DisconnectAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error disconnecting Twitch service");
            }

            // Remove from database
            try
            {
                var platform = await _dataService.GetPlatformByNameAsync("Twitch");
                if (platform != null)
                {
                    await _dataService.DeletePlatformAsync(platform.Id);
                    _logger.LogInformation("Removed Twitch platform from database");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error removing from database");
            }

            // Reset wizard and dispose WebView2
            try
            {
                _webSetupViewModel?.ResetWizard(disposeWebView: true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error resetting wizard");
            }

            // Update state
            IsTwitchConnected = false;
            TwitchStatus = "Disconnected";
            StatusMessage = "Disconnected from Twitch";
            TwitchUsername = string.Empty;
            TwitchChannel = string.Empty;
            NeedsSetup = true;
            IsConfigured = false;

            // Show platform selection
            ShowPlatformSelection = true;
            ShowKickSetup = false;
            ShowKickConnection = false;

            _logger.LogInformation("Twitch disconnect complete");

            // Notify callback to update sidebar and chat dropdown
            if (_onPlatformConnectionChanged != null)
            {
                _logger.LogInformation("Notifying platform connection changed after Twitch disconnect");
                await _onPlatformConnectionChanged.Invoke();
            }

            // Navigate to Platforms view to show the platform selection grid
            if (_onNavigateToPlatforms != null)
            {
                _logger.LogInformation("Auto-navigating to Platforms view after Twitch disconnect");
                await _onNavigateToPlatforms.Invoke();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error disconnecting from Twitch");
            StatusMessage = $"Error: {ex.Message}";
        }
    }

    /// <summary>
    /// Sends a test message to chat.
    /// </summary>
    [RelayCommand]
    private async Task SendTestMessageAsync()
    {
        if (!_twitchService.IsConnected)
        {
            StatusMessage = "Not connected to Twitch chat";
            return;
        }

        try
        {
            await _twitchService.SendMessageAsync("Vyral is online! PogChamp");
            StatusMessage = "Test message sent!";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending test message");
            StatusMessage = $"Error: {ex.Message}";
        }
    }

    // ====== KICK PLATFORM METHODS ======

    [RelayCommand]
    private async Task SelectKickAsync()
    {
        try
        {
            _logger.LogInformation("SelectKick called");

            // Check database first
            var platform = await _dataService.GetPlatformByNameAsync("Kick");
            var isConnected = platform != null && platform.Status == "Connected";

            _logger.LogInformation("Kick platform check: Exists={Exists}, Connected={Connected}", platform != null, isConnected);

            if (!isConnected)
            {
                _logger.LogInformation("Showing Kick setup");
                ShowPlatformSelection = false;
                ShowKickSetup = true;
            }
            else
            {
                // Already configured, show the connection view
                _logger.LogInformation("Showing Kick connection view");
                await ShowKickConnectionAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in SelectKick");
            StatusMessage = $"Error: {ex.Message}";
            ShowPlatformSelection = true;
        }
    }

    /// <summary>
    /// Loads Kick platform data from the database.
    /// </summary>
    public async Task LoadKickPlatformAsync()
    {
        try
        {
            _logger.LogInformation("Loading Kick platform data from database");

            await _dataService.EnsureDatabaseCreatedAsync();

            var platform = await _dataService.GetPlatformByNameAsync("Kick");

            if (platform != null)
            {
                KickUsername = platform.Username ?? string.Empty;
                IsKickConnected = platform.Status == "Connected";
                KickStatus = platform.Status;

                _logger.LogInformation("Loaded Kick account from database - Username: {Username}", KickUsername);

                // Auto-connect to Kick if status is Connected
                if (platform.Status == "Connected" && !_kickService.IsConnected)
                {
                    _logger.LogInformation("Auto-connecting to Kick");

                    try
                    {
                        if (string.IsNullOrEmpty(platform.AccessToken) || string.IsNullOrEmpty(platform.UserId))
                        {
                            _logger.LogWarning("Cannot auto-connect - missing access token or user ID");
                            KickStatus = "Setup Required";
                            return;
                        }

                        // Kick tokens are NOT encrypted (KickLib manages token security)
                        var accessToken = platform.AccessToken;
                        var refreshToken = platform.RefreshToken;
                        var broadcasterUserId = int.Parse(platform.UserId);

                        // Try to refresh token before connecting (in case it's expired)
                        if (!string.IsNullOrEmpty(refreshToken))
                        {
                            _logger.LogInformation("Refreshing Kick access token before auto-connect...");

                            // Use the injected KickLibOAuthService
                            var refreshSuccess = await _kickOAuthService.RefreshTokenAsync();
                            if (refreshSuccess)
                            {
                                // Reload platform to get updated tokens
                                var updatedPlatform = await _dataService.GetPlatformByNameAsync("Kick");
                                if (updatedPlatform != null)
                                {
                                    accessToken = updatedPlatform.AccessToken;
                                    refreshToken = updatedPlatform.RefreshToken;
                                    _logger.LogInformation("Kick access token refreshed and saved before auto-connect");
                                }
                                else
                                {
                                    _logger.LogWarning("Failed to refresh Kick token, will try with existing token");
                                }
                            }
                        }

                        _logger.LogInformation("Auto-connecting to Kick for channel {Channel} with UserId {UserId}",
                            KickUsername, broadcasterUserId);

                        await _kickService.ConnectAsync(KickUsername, accessToken!, refreshToken, KickUsername, broadcasterUserId);

                        // Enable chat by default after successful auto-connect
                        if (platform.KickChatEnabled == false)
                        {
                            platform.KickChatEnabled = true;
                            await _dataService.SavePlatformAsync(platform);
                            _logger.LogInformation("Enabled Kick chat by default after auto-connect");
                        }

                        _logger.LogInformation("Successfully auto-connected to Kick for channel {Channel}", KickUsername);
                        KickStatus = $"Connected to {KickUsername}";
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error auto-connecting to Kick");
                        KickStatus = "Error connecting";
                    }
                }

                StatusMessage = $"Kick account: {KickUsername}";
            }
            else
            {
                _logger.LogWarning("No Kick platform found in database");
                KickStatus = "Not configured";
                StatusMessage = "Kick not configured";
                ShowPlatformSelection = true;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading Kick platform");
            KickStatus = "Error";
            StatusMessage = $"Error loading Kick: {ex.Message}";
            ShowPlatformSelection = true;
        }
    }

    /// <summary>
    /// Shows the Kick connection settings view (for navigation from sidebar).
    /// </summary>
    public async Task ShowKickConnectionAsync()
    {
        try
        {
            _logger.LogInformation("ShowKickConnectionAsync called");

            // Hide platform selection and setup views
            ShowPlatformSelection = false;
            ShowTwitchSetup = false;
            ShowKickSetup = false;

            // Hide both connection views first
            ShowTwitchConnection = false;
            ShowKickConnection = false;

            // Load Kick platform data
            await LoadKickPlatformAsync();

            // Show only Kick section after data is loaded
            ShowKickConnection = true;

            _logger.LogInformation("Kick connection view displayed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error showing Kick connection");
            StatusMessage = $"Error: {ex.Message}";
        }
    }

    /// <summary>
    /// Shows the Twitch connection settings view (for navigation from sidebar).
    /// </summary>
    public async Task ShowTwitchConnectionAsync()
    {
        try
        {
            _logger.LogInformation("ShowTwitchConnectionAsync called");

            // Hide platform selection and setup views
            ShowPlatformSelection = false;
            ShowTwitchSetup = false;
            ShowKickSetup = false;

            // Hide both connection views first
            ShowTwitchConnection = false;
            ShowKickConnection = false;

            // Load Twitch platform data
            await LoadTwitchPlatformAsync();

            // Show only Twitch section after data is loaded
            ShowTwitchConnection = true;

            _logger.LogInformation("Twitch connection view displayed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error showing Twitch connection");
            StatusMessage = $"Error: {ex.Message}";
        }
    }

    /// <summary>
    /// Returns to the platform selection grid.
    /// </summary>
    [RelayCommand]
    private void BackToPlatformSelection()
    {
        _logger.LogInformation("Returning to platform selection");

        // Hide all connection views and setup wizards
        ShowTwitchConnection = false;
        ShowKickConnection = false;
        ShowYouTubeConnection = false;
        ShowTikTokConnection = false;
        ShowDiscordConnection = false;
        ShowTwitchSetup = false;
        ShowKickSetup = false;
        ShowYouTubeSetup = false;
        ShowTikTokSetup = false;
        ShowDiscordSetup = false;

        // Show platform selection grid
        ShowPlatformSelection = true;
    }

    /// <summary>
    /// Reconnects to Kick using saved credentials.
    /// </summary>
    [RelayCommand]
    private async Task ReconnectKickAsync()
    {
        try
        {
            _logger.LogInformation("Reconnecting to Kick - triggering OAuth flow");
            KickStatus = "Re-authenticating...";
            StatusMessage = "Opening Kick OAuth to refresh token...";

            // Disconnect existing connection cleanly
            if (_kickService.IsConnected)
            {
                try
                {
                    await _kickService.DisconnectAsync();
                    _logger.LogInformation("Disconnected existing Kick connection");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error disconnecting before reconnect");
                }
            }

            // Note: We keep Client ID and Client Secret intact - those are app credentials
            // Only the database token needs to be refreshed via OAuth flow
            _logger.LogInformation("Keeping Client ID/Secret intact, refreshing user OAuth token only");

            // Show Kick setup wizard starting at step 3 (OAuth) since credentials already exist
            ShowKickConnection = false;
            ShowPlatformSelection = false;
            ShowTwitchSetup = false;
            ShowTwitchConnection = false;

            // Start at step 3 for reconnect
            if (_kickWebSetupViewModel != null)
            {
                _kickWebSetupViewModel.CurrentStep = 3;
                _logger.LogInformation("Starting Kick setup wizard at step 3 for reconnect");
            }

            ShowKickSetup = true;

            _logger.LogInformation("Kick OAuth setup wizard displayed for token refresh");
            StatusMessage = "Please complete Kick authentication to refresh your token";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reconnecting to Kick");
            KickStatus = "Reconnect Failed";
            StatusMessage = $"Failed to reconnect: {ex.Message}";
        }
    }

    /// <summary>
    /// Disconnects from Kick.
    /// </summary>
    [RelayCommand]
    private async Task DisconnectKick()
    {
        try
        {
            _logger.LogInformation("Disconnecting from Kick");

            // Disconnect service
            try
            {
                await _kickService.DisconnectAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error disconnecting Kick service");
            }

            // Update database - clear all credentials
            try
            {
                var platform = await _dataService.GetPlatformByNameAsync("Kick");
                if (platform != null)
                {
                    _logger.LogInformation("Clearing Kick credentials from database");
                    platform.Status = "Disconnected";
                    platform.AccessToken = null;
                    platform.RefreshToken = null;
                    platform.TokenExpiresAt = null;
                    platform.Username = null;
                    platform.UserId = null;
                    platform.ChannelId = null;
                    platform.ChatroomId = null;
                    platform.ClientId = null;
                    platform.ClientSecret = null;
                    platform.ConnectedAt = null;
                    platform.LastError = null;
                    await _dataService.SavePlatformAsync(platform);
                    _logger.LogInformation("Kick credentials cleared from database");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating database");
            }

            // Update UI
            IsKickConnected = false;
            KickStatus = "Disconnected";
            StatusMessage = "Disconnected from Kick";
            KickUsername = string.Empty;

            // Show platform selection
            ShowPlatformSelection = true;
            ShowKickSetup = false;
            ShowKickConnection = false;
            ShowTwitchSetup = false;
            ShowTwitchConnection = false;

            _logger.LogInformation("Successfully disconnected from Kick");

            // Notify callback to update sidebar and chat dropdown
            if (_onPlatformConnectionChanged != null)
            {
                _logger.LogInformation("Notifying platform connection changed after Kick disconnect");
                await _onPlatformConnectionChanged.Invoke();
            }

            // Navigate to Platforms view to show the platform selection grid
            if (_onNavigateToPlatforms != null)
            {
                _logger.LogInformation("Auto-navigating to Platforms view after Kick disconnect");
                await _onNavigateToPlatforms.Invoke();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error disconnecting from Kick");
            StatusMessage = $"Error disconnecting: {ex.Message}";
        }
    }

    // ====== YOUTUBE PLATFORM METHODS ======

    [RelayCommand]
    private async Task SelectYouTubeAsync()
    {
        try
        {
            _logger.LogInformation("SelectYouTube called");
            var platform = await _dataService.GetPlatformByNameAsync("YouTube");
            var isConnected = platform != null && platform.Status == "Connected";

            if (!isConnected)
            {
                _logger.LogInformation("Showing YouTube setup");
                ShowPlatformSelection = false;
                ShowYouTubeSetup = true;
            }
            else
            {
                _logger.LogInformation("Showing YouTube connection view");
                await ShowYouTubeConnectionAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in SelectYouTube");
            StatusMessage = $"Error: {ex.Message}";
            ShowPlatformSelection = true;
        }
    }

    public async Task LoadYouTubePlatformAsync()
    {
        try
        {
            _logger.LogInformation("Loading YouTube platform data from database");
            await _dataService.EnsureDatabaseCreatedAsync();
            var platform = await _dataService.GetPlatformByNameAsync("YouTube");
            if (platform != null)
            {
                YoutubeUsername = platform.Username ?? string.Empty;
                if (_youtubeService.IsConnected)
                {
                    IsYouTubeConnected = true;
                    YoutubeStatus = "Connected";
                }
                else if (_youtubeService.RequiresReauth)
                {
                    IsYouTubeConnected = false;
                    YoutubeStatus = "Re-authorization required";
                    StatusMessage = "YouTube needs re-authorization — run Home → Connections → YouTube to sign in again.";
                }
                else
                {
                    IsYouTubeConnected = platform.Status == "Connected";
                    YoutubeStatus = platform.Status;
                }
                _logger.LogInformation("Loaded YouTube account from database - Username: {Username}", YoutubeUsername);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading YouTube platform");
        }
    }

    public async Task ShowYouTubeConnectionAsync()
    {
        try
        {
            ShowPlatformSelection = false;
            ShowTwitchSetup = false;
            ShowKickSetup = false;
            ShowYouTubeSetup = false;
            ShowTikTokSetup = false;
            ShowDiscordSetup = false;
            ShowTwitchConnection = false;
            ShowKickConnection = false;
            ShowYouTubeConnection = false;
            ShowTikTokConnection = false;
            ShowDiscordConnection = false;
            await LoadYouTubePlatformAsync();
            ShowYouTubeConnection = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error showing YouTube connection");
            StatusMessage = $"Error: {ex.Message}";
        }
    }

    [RelayCommand]
    /// <summary>
    /// Converges the page with reality when the database row says Connected but
    /// the live YouTube chat service is down: attempts one stored-credential
    /// reconnect. On a re-auth failure the row is marked Disconnected so the
    /// status stops lying; the user re-authorizes via the setup flow.
    /// </summary>
    private async Task ReconnectYouTubeFromStoredCredentialsAsync(Data.Entities.Platform platform)
    {
        try
        {
            if (_youtubeService.IsConnected || string.IsNullOrEmpty(platform.AccessToken))
            {
                return;
            }

            await _youtubeService.ConnectAsync(
                platform.Username ?? "",
                platform.AccessToken,
                platform.RefreshToken,
                platform.Username ?? "");
            IsYouTubeConnected = true;
            YoutubeStatus = "Connected";
            OnPropertyChanged(nameof(IsYouTubeConnected));
            OnPropertyChanged(nameof(YoutubeStatus));
        }
        catch (YouTubeReauthException ex)
        {
            _logger.LogWarning("❌ YouTube re-authorization required ({Reason}) while reconnecting from stored credentials", ex.Reason ?? ex.Message);
            try
            {
                platform.Status = "Disconnected";
                await _dataService.SavePlatformAsync(platform);
            }
            catch (Exception saveEx)
            {
                _logger.LogError(saveEx, "Failed to mark the YouTube platform Disconnected after a re-auth failure");
            }
            IsYouTubeConnected = false;
            YoutubeStatus = "Re-authorization required";
            StatusMessage = "YouTube needs re-authorization — run Home → Connections → YouTube to sign in again.";
            OnPropertyChanged(nameof(IsYouTubeConnected));
            OnPropertyChanged(nameof(YoutubeStatus));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Stored-credential YouTube reconnect failed");
            IsYouTubeConnected = false;
            YoutubeStatus = "Disconnected";
            OnPropertyChanged(nameof(IsYouTubeConnected));
            OnPropertyChanged(nameof(YoutubeStatus));
        }
    }

    private async Task ReconnectYouTubeAsync()
    {
        await Task.CompletedTask;
        try
        {
            _logger.LogInformation("Reconnecting to YouTube");
            ShowYouTubeConnection = false;
            ShowPlatformSelection = false;
            if (_youtubeWebSetupViewModel != null)
                _youtubeWebSetupViewModel.CurrentStep = 3;
            ShowYouTubeSetup = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reconnecting to YouTube");
            StatusMessage = $"Failed to reconnect: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task DisconnectYouTube()
    {
        try
        {
            _logger.LogInformation("Disconnecting from YouTube");

            // Disconnect the chat service first
            if (_youtubeService.IsConnected)
            {
                await _youtubeService.DisconnectAsync();
            }

            var platform = await _dataService.GetPlatformByNameAsync("YouTube");
            if (platform != null)
            {
                platform.Status = "Disconnected";
                platform.AccessToken = null;
                platform.RefreshToken = null;
                platform.TokenExpiresAt = null;
                platform.Username = null;
                platform.ClientId = null;
                platform.ClientSecret = null;
                platform.ConnectedAt = null;
                await _dataService.SavePlatformAsync(platform);
            }
            IsYouTubeConnected = false;
            YoutubeStatus = "Disconnected";
            YoutubeUsername = string.Empty;
            ShowPlatformSelection = true;
            ShowYouTubeSetup = false;
            ShowYouTubeConnection = false;
            if (_onPlatformConnectionChanged != null)
                await _onPlatformConnectionChanged.Invoke();
            if (_onNavigateToPlatforms != null)
                await _onNavigateToPlatforms.Invoke();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error disconnecting from YouTube");
            StatusMessage = $"Error disconnecting: {ex.Message}";
        }
    }

    // ====== TIKTOK PLATFORM METHODS ======

    [RelayCommand]
    private async Task SelectTikTokAsync()
    {
        try
        {
            _logger.LogInformation("SelectTikTok called");
            var platform = await _dataService.GetPlatformByNameAsync("TikTok");
            var isConnected = platform != null && platform.Status == "Connected";

            if (!isConnected)
            {
                ShowPlatformSelection = false;
                ShowTikTokSetup = true;
            }
            else
            {
                await ShowTikTokConnectionAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in SelectTikTok");
            StatusMessage = $"Error: {ex.Message}";
            ShowPlatformSelection = true;
        }
    }

    public async Task LoadTikTokPlatformAsync()
    {
        try
        {
            _logger.LogInformation("Loading TikTok platform data from database");
            await _dataService.EnsureDatabaseCreatedAsync();
            var platform = await _dataService.GetPlatformByNameAsync("TikTok");
            if (platform != null)
            {
                TiktokUsername = platform.Username ?? string.Empty;
                IsTikTokConnected = platform.Status == "Connected";
                TiktokStatus = platform.Status;
                _logger.LogInformation("Loaded TikTok account from database - Username: {Username}", TiktokUsername);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading TikTok platform");
        }
    }

    public async Task ShowTikTokConnectionAsync()
    {
        try
        {
            ShowPlatformSelection = false;
            ShowTwitchSetup = false;
            ShowKickSetup = false;
            ShowYouTubeSetup = false;
            ShowTikTokSetup = false;
            ShowDiscordSetup = false;
            ShowTwitchConnection = false;
            ShowKickConnection = false;
            ShowYouTubeConnection = false;
            ShowTikTokConnection = false;
            ShowDiscordConnection = false;
            await LoadTikTokPlatformAsync();
            ShowTikTokConnection = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error showing TikTok connection");
            StatusMessage = $"Error: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ReconnectTikTokAsync()
    {
        await Task.CompletedTask;
        try
        {
            _logger.LogInformation("Reconnecting to TikTok");
            ShowTikTokConnection = false;
            ShowPlatformSelection = false;
            if (_tiktokWebSetupViewModel != null)
                _tiktokWebSetupViewModel.CurrentStep = 3;
            ShowTikTokSetup = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reconnecting to TikTok");
            StatusMessage = $"Failed to reconnect: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task DisconnectTikTok()
    {
        try
        {
            _logger.LogInformation("Disconnecting from TikTok");
            var platform = await _dataService.GetPlatformByNameAsync("TikTok");
            if (platform != null)
            {
                platform.Status = "Disconnected";
                platform.AccessToken = null;
                platform.RefreshToken = null;
                platform.TokenExpiresAt = null;
                platform.Username = null;
                platform.ClientId = null;
                platform.ClientSecret = null;
                platform.ConnectedAt = null;
                await _dataService.SavePlatformAsync(platform);
            }
            IsTikTokConnected = false;
            TiktokStatus = "Disconnected";
            TiktokUsername = string.Empty;
            ShowPlatformSelection = true;
            ShowTikTokSetup = false;
            ShowTikTokConnection = false;
            if (_onPlatformConnectionChanged != null)
                await _onPlatformConnectionChanged.Invoke();
            if (_onNavigateToPlatforms != null)
                await _onNavigateToPlatforms.Invoke();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error disconnecting from TikTok");
            StatusMessage = $"Error disconnecting: {ex.Message}";
        }
    }

    // ====== DISCORD PLATFORM METHODS ======

    [RelayCommand]
    private async Task SelectDiscordAsync()
    {
        try
        {
            _logger.LogInformation("SelectDiscord called");
            var platform = await _dataService.GetPlatformByNameAsync("Discord");
            // Only treat as connected when a real bot token is saved (wizard validates before saving)
            var isConnected = platform != null && platform.Status == "Connected" && !string.IsNullOrEmpty(platform.AccessToken);

            if (!isConnected)
            {
                if (_onOpenDiscordSetup != null)
                {
                    // Open the wizard as its own full-window page (no layered containers)
                    _onOpenDiscordSetup.Invoke();
                }
                else
                {
                    ShowPlatformSelection = false;
                    ShowDiscordSetup = true;
                }
            }
            else
            {
                await ShowDiscordConnectionAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in SelectDiscord");
            StatusMessage = $"Error: {ex.Message}";
            ShowPlatformSelection = true;
        }
    }

    public async Task LoadDiscordPlatformAsync()
    {
        try
        {
            _logger.LogInformation("Loading Discord platform data from database");
            await _dataService.EnsureDatabaseCreatedAsync();
            var platform = await _dataService.GetPlatformByNameAsync("Discord");
            if (platform != null)
            {
                DiscordUsername = platform.Username ?? string.Empty;
                // The wizard only saves Status="Connected" when a real bot token was validated
                IsDiscordConnected = platform.Status == "Connected" && !string.IsNullOrEmpty(platform.AccessToken);
                DiscordStatus = IsDiscordConnected ? "Connected" : (platform.Status ?? "Disconnected");
                _logger.LogInformation("Loaded Discord account from database - Username: {Username}, Status: {Status}", DiscordUsername, platform.Status);

                // Auto-connect the bot service if the row is marked Connected with a real token
                if (platform.Status == "Connected" && !string.IsNullOrEmpty(platform.AccessToken))
                {
                    try
                    {
                        if (!_discordService.IsConnected)
                        {
                            _logger.LogInformation("Auto-connecting to Discord");

                            var connected = await _discordService.ConnectAsync(platform.AccessToken);

                            // Reflect the real service state (DiscordChatEnabled is intentionally left as-is)
                            IsDiscordConnected = _discordService.IsConnected;

                            if (connected)
                            {
                                _logger.LogInformation("Successfully auto-connected to Discord");
                            }
                            else
                            {
                                _logger.LogWarning("Discord auto-connect failed (invalid or revoked bot token?)");
                                DiscordStatus = "Error connecting";
                            }
                        }

                        if (_discordService.IsConnected)
                        {
                            // Prefer the live bot username from the Discord gateway
                            if (!string.IsNullOrEmpty(_discordService.BotUsername))
                            {
                                DiscordUsername = _discordService.BotUsername;
                            }

                            DiscordStatus = string.IsNullOrEmpty(DiscordUsername)
                                ? "Connected"
                                : $"Connected as {DiscordUsername}";
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error auto-connecting to Discord");
                        IsDiscordConnected = false;
                        DiscordStatus = "Error connecting";
                    }
                }
                else if (platform.Status == "Connected")
                {
                    _logger.LogWarning("Discord row is Connected but has no bot token - cannot connect");
                    DiscordStatus = "Setup Required";
                }
            }
            else
            {
                _logger.LogInformation("No Discord platform found in database");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading Discord platform");
        }
    }

    public async Task ShowDiscordConnectionAsync()
    {
        try
        {
            ShowPlatformSelection = false;
            ShowTwitchSetup = false;
            ShowKickSetup = false;
            ShowYouTubeSetup = false;
            ShowTikTokSetup = false;
            ShowDiscordSetup = false;
            ShowTwitchConnection = false;
            ShowKickConnection = false;
            ShowYouTubeConnection = false;
            ShowTikTokConnection = false;
            ShowDiscordConnection = false;
            await LoadDiscordPlatformAsync();
            ShowDiscordConnection = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error showing Discord connection");
            StatusMessage = $"Error: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ReconnectDiscordAsync()
    {
        await Task.CompletedTask;
        try
        {
            _logger.LogInformation("Reconnecting to Discord");
            ShowDiscordConnection = false;
            ShowPlatformSelection = false;
            if (_discordWebSetupViewModel != null)
                _discordWebSetupViewModel.CurrentStep = 3;
            ShowDiscordSetup = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reconnecting to Discord");
            StatusMessage = $"Failed to reconnect: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task DisconnectDiscord()
    {
        try
        {
            _logger.LogInformation("Disconnecting from Discord");

            // Disconnect the bot service first
            try
            {
                if (_discordService.IsConnected)
                {
                    await _discordService.DisconnectAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error disconnecting Discord service");
            }

            var platform = await _dataService.GetPlatformByNameAsync("Discord");
            if (platform != null)
            {
                platform.Status = "Disconnected";
                platform.AccessToken = null;
                platform.RefreshToken = null;
                platform.TokenExpiresAt = null;
                platform.Username = null;
                platform.ClientId = null;
                platform.ClientSecret = null;
                platform.ConnectedAt = null;
                await _dataService.SavePlatformAsync(platform);
            }
            IsDiscordConnected = false;
            DiscordStatus = "Disconnected";
            DiscordUsername = string.Empty;
            ShowPlatformSelection = true;
            ShowDiscordSetup = false;
            ShowDiscordConnection = false;
            if (_onPlatformConnectionChanged != null)
                await _onPlatformConnectionChanged.Invoke();
            if (_onNavigateToPlatforms != null)
                await _onNavigateToPlatforms.Invoke();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error disconnecting from Discord");
            StatusMessage = $"Error disconnecting: {ex.Message}";
        }
    }
}
