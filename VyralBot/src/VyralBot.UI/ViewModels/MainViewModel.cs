using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using VyralBot.Data;
using VyralBot.Data.Entities;
using VyralBot.Core.Services.Profiles;
using VyralBot.Core.Models;
using VyralBot.Core.Services.EventBus;
using VyralBot.Core.Services.EventBus.Events;
using VyralBot.Integrations.Twitch;
using VyralBot.Integrations.Kick;
using VyralBot.Integrations.YouTube;
using VyralBot.Integrations.Discord;
using VyralBot.UI.Views;
using System.Collections.ObjectModel;
using VyralBot.Api.Services;
using VyralBot.UI.Services;

namespace VyralBot.UI.ViewModels;

/// <summary>
/// Main window ViewModel.
/// </summary>
public partial class MainViewModel : ViewModelBase
{
    private readonly DashboardViewModel _dashboardViewModel;
    private readonly IServiceProvider _serviceProvider;
    private readonly IProfileService _profileService;
    private readonly ProfileSettingsLoader _settingsLoader;
    private readonly IDiscordService _discordService;
    private readonly IDiscordBotManager _discordBotManager;
    private readonly PlatformConnectionService _platformConnectionService;
    private readonly ILogger<MainViewModel> _logger;
    private readonly IEventBus _eventBus;
    private Guid? _navigateToPageSubscriptionId;
    private Guid? _platformConnectionSubscriptionId;
    private IServiceScope? _platformsScope;
    private PlatformsViewModel? _platformsViewModel;
    private IServiceScope? _chatScope;
    private ChatViewModel? _chatViewModel;
    private IServiceScope? _twitchDashboardScope;
    private TwitchDashboardViewModel? _twitchDashboardViewModel;
    private IServiceScope? _kickDashboardScope;
    private KickDashboardViewModel? _kickDashboardViewModel;
    private IServiceScope? _youTubeDashboardScope;
    private YouTubeDashboardViewModel? _youTubeDashboardViewModel;
    private IServiceScope? _discordDashboardScope;
    private DiscordDashboardViewModel? _discordDashboardViewModel;
    private IServiceScope? _discordBotsScope;
    private DiscordBotsViewModel? _discordBotsViewModel;
    private IServiceScope? _discordBotDetailScope;
    private DiscordBotDetailViewModel? _discordBotDetailViewModel;
    private IServiceScope? _settingsScope;
    private SettingsViewModel? _settingsViewModel;
    private IServiceScope? _audioSettingsScope;
    private AudioSettingsViewModel? _audioSettingsViewModel;
    private IServiceScope? _soundsScope;
    private SoundsViewModel? _soundsViewModel;
    private IServiceScope? _statisticsScope;
    private StatisticsViewModel? _statisticsViewModel;
    private IServiceScope? _eventHistoryScope;
    private EventHistoryViewModel? _eventHistoryViewModel;
    private IServiceScope? _alertSettingsScope;
    private AlertSettingsViewModel? _alertSettingsViewModel;
    private IServiceScope? _conditionalRulesScope;
    private ConditionalRulesViewModel? _conditionalRulesViewModel;
    private IServiceScope? _giveawayScope;
    private GiveawayViewModel? _giveawayViewModel;
    private IServiceScope? _pollScope;
    private PollViewModel? _pollViewModel;
    private IServiceScope? _watchtimeScope;
    private WatchtimeViewModel? _watchtimeViewModel;
    private IServiceScope? _eventTestingScope;
    private EventTestingViewModel? _eventTestingViewModel;
    private IServiceScope? _demoSetupScope;
    private GoogleDemoSetupViewModel? _demoSetupViewModel;
    private IServiceScope? _ngrokSetupWizardScope;
    private NgrokSetupWizardViewModel? _ngrokSetupWizardViewModel;
    private IServiceScope? _chatHubScope;
    private ChatHubViewModel? _chatHubViewModel;
    private IServiceScope? _toolsScope;
    private ToolsViewModel? _toolsViewModel;
    private IServiceScope? _integrationsScope;
    private IntegrationsViewModel? _integrationsViewModel;
    private IServiceScope? _documentationScope;
    private DocumentationViewModel? _documentationViewModel;
    private IServiceScope? _automationScope;
    private AutomationViewModel? _automationViewModel;
    private IServiceScope? _moderatorScope;
    private ModeratorViewModel? _moderatorViewModel;
    private IServiceScope? _obsScope;
    private ObsViewModel? _obsViewModel;
    private IServiceScope? _restreamScope;
    private RestreamViewModel? _restreamViewModel;
    private IServiceScope? _streamDeckScope;
    private StreamDeckViewModel? _streamDeckViewModel;

    // One-shot guard for Discord bot auto-start: CheckPlatformConnectionsAsync
    // also runs on every PlatformConnectionChangedEvent refresh, but the bots
    // should only be auto-started once per profile load (reset in
    // OnProfileLoadedAsync so a profile switch re-runs it).
    private bool _discordBotsAutoStartDone;

    [ObservableProperty]
    private ViewModelBase? _currentViewModel;

    [ObservableProperty]
    private string _title = "Vyral";

    [ObservableProperty]
    private bool _isSidebarExpanded = true;

    [ObservableProperty]
    private bool _isPlatformsExpanded = false;

    [ObservableProperty]
    private bool _isChatFeaturesExpanded = false;

    [ObservableProperty]
    private bool _isStreamToolsExpanded = false;

    [ObservableProperty]
    private bool _isRestreamExpanded = false;

    [ObservableProperty]
    private bool _isTwitchConnected = false;

    [ObservableProperty]
    private bool _isKickConnected = false;

    [ObservableProperty]
    private bool _isYouTubeConnected = false;

    [ObservableProperty]
    private bool _isDiscordConnected = false;

    [ObservableProperty]
    private bool _showProfileSelector = false;

    [ObservableProperty]
    private bool _showPinEntry = false;

    [ObservableProperty]
    private bool _showCreateProfile = false;

    [ObservableProperty]
    private string _newProfileName = string.Empty;

    [ObservableProperty]
    private string _newProfileDisplayName = string.Empty;

    [ObservableProperty]
    private string _newProfilePin = string.Empty;

    [ObservableProperty]
    private string _profilePin = string.Empty;

    [ObservableProperty]
    private string? _profileErrorMessage;

    [ObservableProperty]
    private ObservableCollection<Profile> _profiles = new();

    private Profile? _selectedProfile;

    public MainViewModel(
        DashboardViewModel dashboardViewModel,
        IServiceProvider serviceProvider,
        IProfileService profileService,
        ProfileSettingsLoader settingsLoader,
        IEventBus eventBus,
        IDiscordService discordService,
        IDiscordBotManager discordBotManager,
        PlatformConnectionService platformConnectionService,
        ILogger<MainViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(dashboardViewModel);
        ArgumentNullException.ThrowIfNull(serviceProvider);
        ArgumentNullException.ThrowIfNull(profileService);
        ArgumentNullException.ThrowIfNull(settingsLoader);
        ArgumentNullException.ThrowIfNull(eventBus);
        ArgumentNullException.ThrowIfNull(discordService);
        ArgumentNullException.ThrowIfNull(discordBotManager);
        ArgumentNullException.ThrowIfNull(platformConnectionService);
        ArgumentNullException.ThrowIfNull(logger);

        _dashboardViewModel = dashboardViewModel;
        _serviceProvider = serviceProvider;
        _profileService = profileService;
        _settingsLoader = settingsLoader;
        _discordService = discordService;
        _discordBotManager = discordBotManager;
        _platformConnectionService = platformConnectionService;
        _logger = logger;
        _eventBus = eventBus;
        CurrentViewModel = dashboardViewModel;

        _logger.LogInformation("MainViewModel initialized");

        // Subscribe to embedded-surface navigation requests (chat quick-actions, etc.)
        // SubscribeAsync completes synchronously, so the id can be captured for Cleanup.
        _navigateToPageSubscriptionId = eventBus.SubscribeAsync<NavigateToPageEvent>(OnNavigateToPageEventAsync).GetAwaiter().GetResult();

        // Sidebar platform-connection flags must track connects/disconnects no matter
        // which page is visible (previously they only refreshed when visiting Connections).
        _platformConnectionSubscriptionId = eventBus.SubscribeAsync<PlatformConnectionChangedEvent>(OnPlatformConnectionChangedAsync).GetAwaiter().GetResult();

        // Subscribe to profile switched events
        eventBus.SubscribeAsync<ProfileSwitchedEvent>(async e =>
        {
            _logger.LogInformation("Profile switched event received: {OldProfile} -> {NewProfile}", e.OldProfileName, e.NewProfileName);

            // Check if this is the same profile (user selected current profile from selector)
            if (e.OldProfileName == e.NewProfileName)
            {
                _logger.LogInformation("Same profile selected, initializing data without cleanup");
                // Just initialize profile data, don't disconnect or cleanup
                await OnProfileLoadedAsync();
                return;
            }

            _logger.LogInformation("Reloading all data for new profile");

            // Update title
            await UpdateTitleAsync();

            // Disconnect from all platforms
            await DisconnectAllPlatformsAsync();

            // Cleanup and clear ChatViewModel
            if (_chatViewModel != null)
            {
                _chatViewModel.Cleanup();
                _chatViewModel = null;
            }

            // Clear cached ViewModels to force reload with new profile data
            _platformsViewModel = null;
            _settingsViewModel = null;

            // Dispose scopes
            _platformsScope?.Dispose();
            _platformsScope = null;
            _chatScope?.Dispose();
            _chatScope = null;
            _settingsScope?.Dispose();
            _settingsScope = null;

            // Initialize new profile data
            await OnProfileLoadedAsync();

            // If currently viewing a profile-specific view, refresh it
            if (CurrentViewModel == _dashboardViewModel)
            {
                // Dashboard is fine, no reload needed
            }
            else
            {
                // Navigate back to home to avoid stale data
                _logger.LogInformation("Navigating to home after profile switch");
                _ = NavigateToHome(); // Fire and forget - safe in this context
            }

            _logger.LogInformation("Profile switch complete, app ready with new profile data");
        });

        // Title and platform status will be initialized after profile is selected
        // via OnProfileLoadedAsync()
    }

    /// <summary>
    /// Routes a <see cref="NavigateToPageEvent"/> (published by embedded surfaces such as
    /// the chat quick-actions popup) to the matching navigation command.
    /// </summary>
    private async Task OnNavigateToPageEventAsync(NavigateToPageEvent e)
    {
        _logger.LogInformation("Navigate to page event received: {PageKey}", e.PageKey);

        // Handlers run on the EventBus thread pool — navigation touches observable
        // state, so marshal to the UI thread (same pattern as ChatViewModel).
        // Execute() from the UI thread mirrors a button click; the async command
        // pipeline handles its own exceptions.
        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            switch (e.PageKey)
            {
                case AppPages.Giveaways:
                    NavigateToGiveawaysCommand.Execute(null);
                    break;
                case AppPages.Polls:
                    NavigateToPollsCommand.Execute(null);
                    break;
                case AppPages.EventTesting:
                    NavigateToEventTestingCommand.Execute(null);
                    break;
                case AppPages.Settings:
                    NavigateToSettingsCommand.Execute(null);
                    break;
                case AppPages.Moderator:
                    NavigateToModeratorCommand.Execute(null);
                    break;
                default:
                    _logger.LogDebug("Unknown page key '{PageKey}' in NavigateToPageEvent - ignoring", e.PageKey);
                    break;
            }
        });
    }

    /// <summary>
    /// Refreshes the sidebar platform-connection flags whenever a platform
    /// connects or disconnects (published by PlatformConnectionService and the
    /// platform chat services).
    /// </summary>
    private async Task OnPlatformConnectionChangedAsync(PlatformConnectionChangedEvent e)
    {
        _logger.LogInformation("Platform connection changed: {Platform} -> {Connected}", e.Platform, e.Connected);

        // Handlers run on the EventBus thread pool — marshal to the UI thread so
        // the Is*Connected observable properties update safely.
        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            _ = RefreshPlatformConnectionsAsync();
        });
    }

    /// <summary>
    /// Unsubscribes EventBus subscriptions. MainViewModel is a singleton with the
    /// app lifetime, so this only matters for orderly shutdown/tests.
    /// </summary>
    public void Cleanup()
    {
        if (_navigateToPageSubscriptionId is { } navigateId)
        {
            _eventBus.Unsubscribe(navigateId);
            _navigateToPageSubscriptionId = null;
        }

        if (_platformConnectionSubscriptionId is { } connectionId)
        {
            _eventBus.Unsubscribe(connectionId);
            _platformConnectionSubscriptionId = null;
        }
    }

    /// <summary>
    /// Called after a profile is loaded/selected to initialize profile data and connections
    /// </summary>
    public async Task OnProfileLoadedAsync()
    {
        _logger.LogInformation("Profile loaded, initializing ALL app data and settings...");

        // Update title with profile name
        await UpdateTitleAsync();

        // Initialize webhook coordinator now that profile is loaded
        var webhookCoordinator = _serviceProvider.GetRequiredService<VyralBot.Api.Services.WebhookCoordinatorService>();
        await webhookCoordinator.InitializeAsync();

        // Start audio WebSocket server for browser sources
        // Audio WebSocket server now handled by Kestrel (port 5000); initialize sounds only
        var soundService = _serviceProvider.GetRequiredService<Core.Services.Audio.ISoundService>();
        await soundService.InitializeDefaultSoundsAsync();

        // Global soundboard hotkeys (KI-55): re-register this profile's saved
        // hotkeys now that the profile data store is live. Runs on every profile
        // (re)load; the service unregisters the previous profile's slots first
        // and skips entries whose sound file is gone.
        try
        {
            var hotkeyService = _serviceProvider.GetRequiredService<Services.Hotkey.GlobalHotkeyService>();
            await hotkeyService.RestoreSavedHotkeysAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error restoring saved sound hotkeys");
        }

        // ========================================================================
        // CREATE ESSENTIAL VIEWMODELS ONLY (lazy-load Platforms/Settings on demand)
        // ========================================================================

        // Don't create PlatformsViewModel at startup - lazy-load when user navigates to it
        // This saves ~30MB of memory on startup

        // 1. Create KickSettingsViewModel (needed for settings loading)
        IServiceScope kickSettingsScope = _serviceProvider.CreateScope();
        var kickSettingsViewModel = kickSettingsScope.ServiceProvider.GetRequiredService<KickSettingsViewModel>();

        // 3. Create WebServerSettingsViewModel
        IServiceScope webServerScope = _serviceProvider.CreateScope();
        var webServerSettingsViewModel = webServerScope.ServiceProvider.GetRequiredService<WebServerSettingsViewModel>();

        // 4. Create ChatViewModel
        if (_chatViewModel == null)
        {
            _chatScope = _serviceProvider.CreateScope();
            _chatViewModel = _chatScope.ServiceProvider.GetRequiredService<ChatViewModel>();
        }

        // ========================================================================
        // LOAD ALL SETTINGS AND SUBSCRIBE TO EVENTS *BEFORE* AUTO-CONNECTIONS
        // ========================================================================
        // CRITICAL: ChatViewModel must subscribe to EventBus BEFORE platforms connect,
        // otherwise messages arriving between connect and subscribe are lost.
        _logger.LogInformation("🔄 Loading all profile settings and subscribing to events before auto-connections...");

        await _settingsLoader.LoadAllSettingsAsync(
            platformsViewModel: null, // Lazy-load Platforms on demand
            kickSettingsViewModel: kickSettingsViewModel,
            webServerSettingsViewModel: webServerSettingsViewModel,
            chatViewModel: _chatViewModel
        );

        _logger.LogInformation("✅ All settings loaded and EventBus subscriptions active - now connecting platforms...");

        // Check platform status from database and auto-connect
        // MUST happen AFTER ChatViewModel.InitializeAsync() so EventBus subscriptions are active
        _discordBotsAutoStartDone = false; // profile (re)load re-arms Discord bot auto-start
        await CheckPlatformConnectionsAsync();

        // Discord bot auto-start (multi-bot) now lives in
        // CheckPlatformConnectionsAsync → AutoStartDiscordBotsAsync: the
        // Platform row's DiscordChatAutoStart flag starts the primary bot and
        // each bot's own AutoStart flag opts extra bots in — all through the
        // manager, guarded against double-starts. The previous blanket
        // StartAllSavedAsync() here was removed so bots the user did NOT opt
        // in are no longer force-started on every launch.

        // ========================================================================
        // AUTO-ENABLE FEATURES BASED ON LOADED SETTINGS
        // ========================================================================

        // Auto-enable Kick webhooks if toggle is ON (from loaded settings)
        await AutoEnableKickWebhooksAsync();

        // NOTE: Auto-start is handled by WebhookCoordinatorService.InitializeAsync() above.
        // Do NOT start again here — it causes a double start/stop/start cycle.

        // Dispose temporary scopes (ViewModels already initialized)
        kickSettingsScope.Dispose();
        webServerScope.Dispose();

        // Start the command timer scheduler (Commands page → Timers tab) so
        // scheduled timers fire while this profile is active.
        try
        {
            var commandTimerService = _serviceProvider.GetRequiredService<CommandTimerService>();
            await commandTimerService.StartAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting command timers");
        }

        // Load command processing settings (prefixes + per-platform toggles from
        // Settings → Commands) before any message can trigger a command.
        try
        {
            await _serviceProvider.GetRequiredService<Core.Services.Commands.ICommandService>().InitializeAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading command settings");
        }

        // Start the workflow trigger service (Visual Automation): loads the
        // workflow definitions and activates command/event/timer triggers for
        // this profile.
        try
        {
            var workflowTriggerService = _serviceProvider.GetRequiredService<WorkflowTriggerService>();
            await workflowTriggerService.StartAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting workflow triggers");
        }

        _logger.LogInformation("✅ Profile initialization complete - all settings loaded, features ready");
    }



    private async Task AutoEnableKickWebhooksAsync()
    {
        try
        {
            _logger.LogInformation("Checking if Kick webhooks should be auto-enabled...");

            using var scope = _serviceProvider.CreateScope();
            var dataService = scope.ServiceProvider.GetRequiredService<IDataService>();
            var webhookCoordinator = scope.ServiceProvider.GetRequiredService<WebhookCoordinatorService>();

            // Check if webhook server is running
            if (!webhookCoordinator.IsRunning)
            {
                _logger.LogInformation("Webhook server not running, skipping Kick webhook auto-enable");
                return;
            }

            // Check if there are active Kick webhook subscriptions in database
            var subscriptions = await dataService.GetWebhookSubscriptionsAsync("Kick");
            var activeSubscriptions = subscriptions.Where(s => s.IsActive).ToList();

            if (!activeSubscriptions.Any())
            {
                _logger.LogInformation("No active Kick webhook subscriptions found in database");
                return;
            }

            // Get Kick platform credentials
            var platform = await dataService.GetPlatformByNameAsync("Kick");
            if (platform == null || string.IsNullOrEmpty(platform.AccessToken) || string.IsNullOrEmpty(platform.UserId))
            {
                _logger.LogWarning("Cannot auto-enable Kick webhooks - missing platform credentials");
                return;
            }

            _logger.LogInformation("🔄 Auto-enabling Kick webhooks on startup (found {Count} active subscriptions)", activeSubscriptions.Count);

            // Kick tokens are NOT encrypted (KickLib manages token security)
            var accessToken = platform.AccessToken;
            await webhookCoordinator.EnableKickWebhooksAsync(accessToken, platform.UserId);

            _logger.LogInformation("✅ Kick webhooks auto-enabled successfully on startup");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error auto-enabling Kick webhooks on startup");
        }
    }

    private async Task UpdateTitleAsync()
    {
        try
        {
            var profile = await _profileService.GetCurrentProfileAsync();
            Title = $"Vyral ({profile.DisplayName})";
        }
        catch
        {
            Title = "Vyral";
        }
    }

    private async Task CheckPlatformConnectionsAsync()
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var dataService = scope.ServiceProvider.GetRequiredService<IDataService>();

            // Ensure database is created
            await dataService.EnsureDatabaseCreatedAsync();

            // One-time migration: the per-platform auto-start columns predate the
            // Settings → Platforms startup controls and defaulted to 0. Backfill
            // them from the current connection status so this update preserves
            // each user's existing auto-connect behavior.
            await BackfillAutoStartFlagsAsync(dataService);

            var twitchPlatform = await dataService.GetPlatformByNameAsync("Twitch");

            IsTwitchConnected = twitchPlatform?.Status == "Connected";

            var kickPlatform = await dataService.GetPlatformByNameAsync("Kick");

            IsKickConnected = kickPlatform?.Status == "Connected";

            var youtubePlatform = await dataService.GetPlatformByNameAsync("YouTube");

            IsYouTubeConnected = youtubePlatform?.Status == "Connected";

            var discordPlatform = await dataService.GetPlatformByNameAsync("Discord");

            // Only treat Discord as connected when the row is marked Connected AND a real bot token exists
            IsDiscordConnected = discordPlatform?.Status == "Connected" && !string.IsNullOrEmpty(discordPlatform?.AccessToken);

            // Auto-connect each platform only when its Settings → Platforms
            // "auto-connect at startup" flag is on (backfilled above for
            // pre-settings installs, so existing users keep current behavior).
            if (twitchPlatform?.Status == "Connected" && twitchPlatform.TwitchChatAutoStart)
            {
                var refreshTokens = await ShouldRefreshTokensAtStartupAsync(dataService, "Twitch");
                var (success, message) = await _platformConnectionService.ConnectTwitchAsync(refreshTokens);
                if (!success)
                {
                    _logger.LogWarning("Twitch auto-connect skipped: {Message}", message);
                }
            }

            if (kickPlatform?.Status == "Connected" && kickPlatform.KickChatAutoStart)
            {
                var refreshTokens = await ShouldRefreshTokensAtStartupAsync(dataService, "Kick");
                var (success, message) = await _platformConnectionService.ConnectKickAsync(refreshTokens);
                if (!success)
                {
                    _logger.LogWarning("Kick auto-connect skipped: {Message}", message);
                }
            }

            if (youtubePlatform?.Status == "Connected" && youtubePlatform.YoutubeChatAutoStart)
            {
                // The YouTube connect polls Google APIs, which can stall for
                // minutes on quota errors — that starves the whole boot chain
                // (Discord bot auto-start, command timers, workflow engine).
                // Bound it; the connect keeps running in the background and its
                // PlatformConnectionChanged events update the UI on completion.
                var youtubeConnect = _platformConnectionService.ConnectYouTubeAsync();
                var finished = await Task.WhenAny(youtubeConnect, Task.Delay(TimeSpan.FromSeconds(45)));
                if (finished != youtubeConnect)
                {
                    _logger.LogWarning("⚠️ YouTube auto-connect timed out after 45s — continuing startup; the connect keeps retrying in the background");
                }
                else
                {
                    var (success, message) = await youtubeConnect;
                    if (!success)
                    {
                        _logger.LogWarning("YouTube auto-connect skipped: {Message}", message);
                    }
                }
            }

            // Discord bots auto-start (multi-bot): the Platform row's
            // DiscordChatAutoStart flag starts the primary bot and each bot's
            // own AutoStart flag opts extra bots in — both go through
            // IDiscordBotManager (the legacy ConnectDiscordAsync singleton is
            // reserved for the setup wizard; connecting it AND the manager's
            // bot with the same token made the two gateway sessions fight, so
            // the bot never reliably came up at launch). Fire-and-forget so a
            // slow gateway handshake cannot stall the boot chain (same reason
            // the YouTube connect above is bounded); one-shot per profile load.
            _ = AutoStartDiscordBotsAsync(discordPlatform);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error checking platform connections: {ex}");
            // Silently fail - platforms won't show if check fails
            IsTwitchConnected = false;
            IsKickConnected = false;
            IsYouTubeConnected = false;
            IsDiscordConnected = false;
        }
    }

    /// <summary>
    /// Auto-starts Discord bots at app launch. Runs once per profile load (the
    /// <see cref="_discordBotsAutoStartDone"/> guard — this is fired from
    /// <see cref="CheckPlatformConnectionsAsync"/>, which also runs on every
    /// connection-changed refresh). Two independent triggers:
    /// <list type="number">
    /// <item>The Platform row's <c>DiscordChatAutoStart</c> flag (Settings →
    /// Platforms "Discord" toggle): starts the saved bot whose token matches
    /// the Platform row's AccessToken, falling back to the FIRST saved bot
    /// (the migrated "Primary" row) when no row matches. The primary bot is
    /// deliberately NOT given a seeded AutoStart flag — the Platform flag
    /// alone starts it.</item>
    /// <item>Each managed bot whose own <c>AutoStart</c> flag is true
    /// (opt-in per extra bot; defaults to off).</item>
    /// </list>
    /// The Settings → Platforms "Discord bots" master toggle
    /// (<c>Startup.DiscordBots.AutoConnect</c>, default on) disables both,
    /// keeping the existing per-platform startup settings working. Never
    /// double-starts: a bot with a live manager service is skipped, and every
    /// start is individually wrapped in try/catch.
    /// </summary>
    private async Task AutoStartDiscordBotsAsync(Platform? discordPlatform)
    {
        if (_discordBotsAutoStartDone)
        {
            return;
        }
        _discordBotsAutoStartDone = true;

        try
        {
            // Master toggle from Settings → Platforms ("auto-start Discord
            // bots"). A missing key means enabled (installs predating it).
            var botsAutoStartRaw = await _serviceProvider.GetRequiredService<IDataService>()
                .GetConfigValueAsync("Startup.DiscordBots.AutoConnect");
            if (bool.TryParse(botsAutoStartRaw, out var botsAutoStart) && !botsAutoStart)
            {
                _logger.LogInformation("Discord bot auto-start disabled (Settings → Platforms)");
                return;
            }

            // LoadBotsAsync also performs the one-time legacy adoption: when
            // the DiscordBots table is still empty, the Platform row's token
            // becomes the "Primary" bot row, so the fallback below is reliable
            // even for installs that never opened the bots page.
            var bots = await _discordBotManager.LoadBotsAsync();
            if (bots.Count == 0)
            {
                _logger.LogInformation("Discord bot auto-start: no saved bots, nothing to start");
                return;
            }

            // Trigger 1: the Platform row's auto-start flag starts the primary
            // bot through the manager (token match first, else the first bot).
            if (discordPlatform?.DiscordChatAutoStart == true)
            {
                var platformToken = discordPlatform.AccessToken;
                var match = bots.FirstOrDefault(b =>
                    !string.IsNullOrEmpty(b.Token) &&
                    string.Equals(b.Token, platformToken, StringComparison.Ordinal));
                await StartDiscordBotAtLaunchAsync(match ?? bots[0]);
            }

            // Trigger 2: opt-in per-bot auto-start for extra bots.
            foreach (var bot in bots.Where(b => b.AutoStart))
            {
                await StartDiscordBotAtLaunchAsync(bot);
            }

            // Reflect the multi-bot connection state in the sidebar (the setup
            // wizard's singleton service usually stays disconnected).
            var anyConnected = _discordBotManager.Connected.Count > 0 || _discordService.IsConnected;
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                IsDiscordConnected = anyConnected;
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error auto-starting Discord bots at startup");
        }
    }

    /// <summary>Starts one saved bot at launch unless it is already running.</summary>
    private async Task StartDiscordBotAtLaunchAsync(DiscordBotConfig bot)
    {
        try
        {
            // Manager's running-state guard: a live service means the bot is
            // already up (e.g. started by adoption or an earlier trigger).
            if (_discordBotManager.Get(bot.Id) != null)
            {
                _logger.LogDebug("Discord bot '{Name}' already running — skipping auto-start", bot.Name);
                return;
            }

            if (await _discordBotManager.StartBotAsync(bot.Id))
            {
                _logger.LogInformation("Discord bot '{Name}' auto-started at app launch", bot.Name);
            }
            else
            {
                _logger.LogWarning("Discord bot '{Name}' auto-start failed (missing token or connect error)", bot.Name);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error auto-starting Discord bot '{Name}'", bot.Name);
        }
    }

    /// <summary>
    /// Early Discord bot auto-start hook for App startup: fires the SAME
    /// guarded auto-start logic as <see cref="CheckPlatformConnectionsAsync"/>
    /// (which only runs after the full settings/platform load chain) so bots
    /// connect as early as possible. Fetches the Discord Platform row itself —
    /// Trigger 1 (the row's DiscordChatAutoStart flag) needs it — and relies on
    /// the one-shot <see cref="_discordBotsAutoStartDone"/> guard, which makes
    /// the later CheckPlatformConnectionsAsync invocation a harmless no-op
    /// (guard-reuse; no duplicated connection logic). The auto-start logic
    /// already calls <c>LoadBotsAsync</c> before starting anything.
    /// </summary>
    public async Task StartDiscordBotsEarlyAsync()
    {
        Platform? discordPlatform = null;
        try
        {
            discordPlatform = await _serviceProvider.GetRequiredService<IDataService>()
                .GetPlatformByNameAsync("Discord");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Early Discord bot auto-start: could not read the Discord Platform row — continuing with per-bot flags only");
        }

        await AutoStartDiscordBotsAsync(discordPlatform);
    }

    /// <summary>
    /// One-time backfill of the per-platform auto-start flags from the current
    /// connection status, so adding the Settings → Platforms startup controls
    /// does not change which platforms existing installs auto-connect.
    /// </summary>
    private static async Task BackfillAutoStartFlagsAsync(IDataService dataService)
    {
        const string migrationKey = "Startup.AutoStartFlagsBackfilled";

        try
        {
            var done = await dataService.GetConfigValueAsync(migrationKey);
            if (bool.TryParse(done, out var isDone) && isDone)
            {
                return;
            }

            var platforms = await dataService.GetPlatformsAsync();
            foreach (var platform in platforms)
            {
                var isConnected = platform.Status == "Connected";
                var changed = false;

                if (platform.Name == "Twitch" && platform.TwitchChatAutoStart != isConnected) { platform.TwitchChatAutoStart = isConnected; changed = true; }
                if (platform.Name == "Kick" && platform.KickChatAutoStart != isConnected) { platform.KickChatAutoStart = isConnected; changed = true; }
                if (platform.Name == "YouTube" && platform.YoutubeChatAutoStart != isConnected) { platform.YoutubeChatAutoStart = isConnected; changed = true; }
                if (platform.Name == "TikTok" && platform.TiktokChatAutoStart != isConnected) { platform.TiktokChatAutoStart = isConnected; changed = true; }
                if (platform.Name == "Discord" && platform.DiscordChatAutoStart != isConnected) { platform.DiscordChatAutoStart = isConnected; changed = true; }

                if (changed)
                {
                    await dataService.SavePlatformAsync(platform);
                }
            }

            await dataService.SetConfigValueAsync(migrationKey, true.ToString(), "Startup");
        }
        catch
        {
            // Backfill is best-effort — if it fails the flags keep their stored
            // values and the user can set them in Settings → Platforms.
        }
    }

    /// <summary>
    /// Reads the per-platform "refresh tokens at startup" setting (Settings → Platforms).
    /// Defaults to true, matching the pre-settings behavior.
    /// </summary>
    private static async Task<bool> ShouldRefreshTokensAtStartupAsync(IDataService dataService, string platformName)
    {
        var value = await dataService.GetConfigValueAsync($"Startup.{platformName}.RefreshTokens");
        return !bool.TryParse(value, out var refresh) || refresh;
    }

    private async Task DisconnectAllPlatformsAsync()
    {
        try
        {
            _logger.LogInformation("Disconnecting from all platforms for profile switch");

            // Stop the command timer scheduler first — timers must not fire
            // while platforms are down or pointing at the previous profile.
            try
            {
                var commandTimerService = _serviceProvider.GetService<CommandTimerService>();
                if (commandTimerService != null)
                {
                    await commandTimerService.StopAsync();
                    _logger.LogInformation("Stopped command timers");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error stopping command timers");
            }

            // Stop workflow triggers next — command/event/timer workflows must
            // not fire while platforms are down or pointing at the previous profile.
            try
            {
                var workflowTriggerService = _serviceProvider.GetService<WorkflowTriggerService>();
                if (workflowTriggerService != null)
                {
                    await workflowTriggerService.StopAsync();
                    _logger.LogInformation("Stopped workflow triggers");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error stopping workflow triggers");
            }

            // Stop the restream engine — its FFmpeg fan-out is running the previous
            // profile's destinations/stream keys and must not keep streaming under
            // the new profile. The engine is a singleton that outlives the page VM,
            // so nothing else stops it on a switch.
            try
            {
                var restreamEngine = _serviceProvider.GetService<VyralBot.Integrations.Restream.RestreamEngine>();
                if (restreamEngine?.IsRunning == true)
                {
                    await restreamEngine.StopAsync();
                    _logger.LogInformation("Stopped restream engine for profile switch");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error stopping restream engine");
            }

            var twitchService = _serviceProvider.GetService<ITwitchService>();
            if (twitchService?.IsConnected == true)
            {
                await twitchService.DisconnectAsync();
                _logger.LogInformation("Disconnected from Twitch");
            }

            var kickService = _serviceProvider.GetService<IKickService>();
            if (kickService?.IsConnected == true)
            {
                await kickService.DisconnectAsync();
                _logger.LogInformation("Disconnected from Kick");
            }

            var youtubeService = _serviceProvider.GetService<IYouTubeService>();
            if (youtubeService?.IsConnected == true)
            {
                await youtubeService.DisconnectAsync();
                _logger.LogInformation("Disconnected from YouTube");
            }

            if (_discordService.IsConnected)
            {
                await _discordService.DisconnectAsync();
                _logger.LogInformation("Disconnected from Discord");
            }

            // Stop every running Discord bot (multi-bot support)
            try
            {
                await _discordBotManager.StopAllAsync();
                _logger.LogInformation("Stopped all Discord bots");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error stopping Discord bots");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error disconnecting platforms");
        }
    }

    /// <summary>
    /// Refreshes platform connection status. Called when platforms are connected/disconnected.
    /// </summary>
    public async Task RefreshPlatformConnectionsAsync()
    {
        await CheckPlatformConnectionsAsync();

        // Reflect the live Discord connection state (service may have connected/disconnected since last check).
        // Multi-bot: any manager-run bot counts — the setup wizard's singleton
        // service usually stays disconnected while the real bots run.
        IsDiscordConnected = _discordBotManager.Connected.Count > 0 || _discordService.IsConnected;

        // Notify ChatViewModel to update its connected platforms list on UI thread
        if (_chatViewModel != null)
        {
            _logger.LogInformation("Refreshing ChatViewModel connected platforms");
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                _chatViewModel.UpdateConnectedPlatforms();
            });
        }
    }

    [RelayCommand]
    private void ToggleSidebar()
    {
        _logger.LogInformation("User clicked: Toggle Sidebar (Expanded: {IsExpanded})", !IsSidebarExpanded);
        IsSidebarExpanded = !IsSidebarExpanded;
    }

    [RelayCommand]
    private void TogglePlatforms()
    {
        _logger.LogInformation("User clicked: Toggle Platforms dropdown (Expanded: {IsExpanded})", !IsPlatformsExpanded);
        IsPlatformsExpanded = !IsPlatformsExpanded;
    }

    [RelayCommand]
    private void ToggleChatFeatures()
    {
        _logger.LogInformation("User clicked: Toggle Chat Features dropdown (Expanded: {IsExpanded})", !IsChatFeaturesExpanded);
        IsChatFeaturesExpanded = !IsChatFeaturesExpanded;
    }

    [RelayCommand]
    private void ToggleStreamTools()
    {
        _logger.LogInformation("User clicked: Toggle Stream Tools dropdown (Expanded: {IsExpanded})", !IsStreamToolsExpanded);
        IsStreamToolsExpanded = !IsStreamToolsExpanded;
    }

    [RelayCommand]
    private void ToggleRestream()
    {
        _logger.LogInformation("User clicked: Toggle Restream dropdown (Expanded: {IsExpanded})", !IsRestreamExpanded);
        IsRestreamExpanded = !IsRestreamExpanded;
    }

    /// <summary>
    /// Checks if a setup wizard is in progress and prompts the user to confirm navigation.
    /// </summary>
    /// <param name="platformName">Name of the platform for the confirmation message ("Twitch" or "Kick")</param>
    /// <returns>True if navigation should proceed, false if cancelled</returns>
    private bool CheckSetupInProgress(out string? platformName)
    {
        platformName = null;

        // Check if Twitch setup is in progress
        if (_platformsViewModel?.ShowTwitchSetup == true)
        {
            platformName = "Twitch";
            var dialog = new Views.ConfirmDialog
            {
                Owner = System.Windows.Application.Current.MainWindow
            };

            var result = dialog.ShowDialog();

            if (result != true)
            {
                _logger.LogInformation("Navigation cancelled - Twitch setup in progress");
                return false;
            }

            // Reset the Twitch wizard if user confirms
            _platformsViewModel.WebSetupViewModel?.ResetWizard(disposeWebView: true);
            _platformsViewModel.ShowTwitchSetup = false;
            _logger.LogInformation("Twitch setup cancelled by user");
            return true;
        }

        // Check if Kick setup is in progress
        if (_platformsViewModel?.ShowKickSetup == true)
        {
            platformName = "Kick";
            var dialog = new Views.ConfirmDialog
            {
                Owner = System.Windows.Application.Current.MainWindow
            };

            var result = dialog.ShowDialog();

            if (result != true)
            {
                _logger.LogInformation("Navigation cancelled - Kick setup in progress");
                return false;
            }

            // Reset the Kick wizard if user confirms
            _platformsViewModel.KickWebSetupViewModel?.ResetWizard(disposeWebView: true);
            _platformsViewModel.ShowKickSetup = false;
            _logger.LogInformation("Kick setup cancelled by user");
            return true;
        }

        // Check if YouTube setup is in progress
        if (_platformsViewModel?.ShowYouTubeSetup == true)
        {
            platformName = "YouTube";
            var dialog = new Views.ConfirmDialog
            {
                Owner = System.Windows.Application.Current.MainWindow
            };

            var result = dialog.ShowDialog();

            if (result != true)
            {
                _logger.LogInformation("Navigation cancelled - YouTube setup in progress");
                return false;
            }

            // Reset the YouTube wizard if user confirms
            _platformsViewModel.YouTubeWebSetupViewModel?.ResetWizard(disposeWebView: true);
            _platformsViewModel.ShowYouTubeSetup = false;
            _logger.LogInformation("YouTube setup cancelled by user");
            return true;
        }

        // Check if TikTok setup is in progress
        if (_platformsViewModel?.ShowTikTokSetup == true)
        {
            platformName = "TikTok";
            var dialog = new Views.ConfirmDialog
            {
                Owner = System.Windows.Application.Current.MainWindow
            };

            var result = dialog.ShowDialog();

            if (result != true)
            {
                _logger.LogInformation("Navigation cancelled - TikTok setup in progress");
                return false;
            }

            // Reset the TikTok wizard if user confirms
            _platformsViewModel.TikTokWebSetupViewModel?.ResetWizard(disposeWebView: true);
            _platformsViewModel.ShowTikTokSetup = false;
            _logger.LogInformation("TikTok setup cancelled by user");
            return true;
        }

        // Check if the Discord setup wizard (its own full page) is open — it
        // nags on every step including step 1, matching Twitch/Kick/YouTube.
        if (_discordSetupPageViewModel is { } discordSetup && CurrentViewModel == discordSetup)
        {
            platformName = "Discord";
            var dialog = new Views.ConfirmDialog
            {
                Owner = System.Windows.Application.Current.MainWindow
            };

            var result = dialog.ShowDialog();

            if (result != true)
            {
                _logger.LogInformation("Navigation cancelled - Discord setup in progress");
                return false;
            }

            // Reset the Discord wizard if user confirms
            discordSetup.GoToStep1Command.Execute(null);
            _logger.LogInformation("Discord setup cancelled by user");
            return true;
        }

        return true; // No setup in progress
    }

    [RelayCommand]
    private async Task NavigateToHome()
    {
        _logger.LogInformation("User clicked: Navigate to Home");

        // Skip if already on Home to avoid unnecessary disposal cycles
        if (CurrentViewModel == _dashboardViewModel)
        {
            _logger.LogInformation("Already on Home, skipping navigation");
            return;
        }

        // Check if setup is in progress and prompt user
        if (!CheckSetupInProgress(out _))
        {
            return; // User cancelled navigation
        }

        // CRITICAL ORDER: Null CurrentViewModel FIRST to release WPF references
        CurrentViewModel = null;

        // Reset all platform view flags when going home
        if (_platformsViewModel != null)
        {
            _platformsViewModel.ShowTwitchConnection = false;
            _platformsViewModel.ShowKickConnection = false;
            _platformsViewModel.ShowTwitchSetup = false;
            _platformsViewModel.ShowKickSetup = false;
            _platformsViewModel.ShowYouTubeSetup = false;
            _platformsViewModel.ShowPlatformSelection = true;
        }

        // Now dispose after WPF releases its references
        await DisposeNonEssentialViewModelsAsync(excludeViewModel: _dashboardViewModel);

        CurrentViewModel = _dashboardViewModel;
        Title = "Vyral - Home";
    }

    [RelayCommand]
    private void NavigateToTriggers()
    {
        _logger.LogInformation("User clicked: Navigate to Triggers");

        // Check if setup is in progress and prompt user
        if (!CheckSetupInProgress(out _))
        {
            return; // User cancelled navigation
        }

        // TODO: Navigate to triggers
        Title = "Vyral - Triggers";
    }

    [RelayCommand]
    private async Task NavigateToChatAsync()
    {
        _logger.LogInformation("User clicked: Navigate to Chat");

        // Check if setup is in progress and prompt user FIRST
        if (!CheckSetupInProgress(out _))
        {
            return; // User cancelled navigation
        }

        // Skip if already on Chat to avoid unnecessary disposal cycles and WPF visual tree recreation
        if (CurrentViewModel == _chatViewModel && _chatViewModel != null)
        {
            _logger.LogInformation("Already on Chat, skipping navigation");
            return;
        }

        try
        {
            // ChatViewModel should already be initialized during profile loading
            // If somehow null (edge case), create it now
            if (_chatViewModel == null)
            {
                _logger.LogWarning("ChatViewModel was null (unexpected), creating now...");
                _chatScope = _serviceProvider.CreateScope();
                _chatViewModel = _chatScope.ServiceProvider.GetRequiredService<ChatViewModel>();
                await _chatViewModel.InitializeAsync();
            }

            // CRITICAL ORDER: Null CurrentViewModel FIRST to release WPF references
            CurrentViewModel = null;

            // Now dispose after WPF releases its references
            await DisposeNonEssentialViewModelsAsync(excludeViewModel: _chatViewModel);

            // Let WPF's DataTemplate system create the view lazily (avoids 100MB spike)
            CurrentViewModel = _chatViewModel;
            Title = "Vyral - Chat";
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error navigating to chat: {ex}");
            System.Windows.MessageBox.Show($"Error loading chat: {ex.Message}\n\n{ex.InnerException?.Message}", "Chat Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private async Task NavigateToCurrencyAsync()
    {
        _logger.LogInformation("User clicked: Navigate to Currency");

        // Check if setup is in progress and prompt user
        if (!CheckSetupInProgress(out _))
        {
            return; // User cancelled navigation
        }

        try
        {
            // Create CurrencyViewModel on first navigation
            var currencyViewModel = _serviceProvider.GetRequiredService<CurrencyViewModel>();

            CurrentViewModel = null;
            await DisposeNonEssentialViewModelsAsync(excludeViewModel: currencyViewModel);

            await currencyViewModel.InitializeAsync();
            CurrentViewModel = currencyViewModel;
            Title = "Vyral - Currency System";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error navigating to currency");
            System.Windows.MessageBox.Show($"Error loading currency: {ex.Message}", "Currency Error",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private async Task NavigateToCommandsAsync()
    {
        _logger.LogInformation("User clicked: Navigate to Commands");

        // Check if setup is in progress and prompt user
        if (!CheckSetupInProgress(out _))
        {
            return; // User cancelled navigation
        }

        try
        {
            // Create CommandsViewModel on first navigation
            var commandsViewModel = _serviceProvider.GetRequiredService<CommandsViewModel>();

            CurrentViewModel = null;
            await DisposeNonEssentialViewModelsAsync(excludeViewModel: commandsViewModel);

            await commandsViewModel.InitializeAsync();
            CurrentViewModel = commandsViewModel;
            Title = "Vyral - Command System";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error navigating to commands");
            System.Windows.MessageBox.Show($"Error loading commands: {ex.Message}", "Commands Error",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private async Task NavigateToAutomationAsync()
    {
        _logger.LogInformation("User clicked: Navigate to Automation");

        if (!CheckSetupInProgress(out _))
        {
            return;
        }

        try
        {
            CurrentViewModel = null;
            await DisposeNonEssentialViewModelsAsync(excludeViewModel: _automationViewModel);

            _automationScope?.Dispose();
            _automationScope = _serviceProvider.CreateScope();
            _automationViewModel = _automationScope.ServiceProvider.GetRequiredService<AutomationViewModel>();
            await _automationViewModel.InitializeAsync();

            _logger.LogInformation("Setting CurrentViewModel to AutomationViewModel");
            CurrentViewModel = _automationViewModel;
            Title = "Vyral - Automation";
            _logger.LogInformation("Navigation to Automation complete");

            GC.Collect(1, GCCollectionMode.Optimized, blocking: false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error navigating to automation");
            System.Diagnostics.Debug.WriteLine($"Error navigating to automation: {ex}");
        }
    }

    [RelayCommand]
    private void NavigateToActions()
    {
        _logger.LogInformation("User clicked: Navigate to Actions");

        // Check if setup is in progress and prompt user
        if (!CheckSetupInProgress(out _))
        {
            return; // User cancelled navigation
        }

        // TODO: Navigate to actions
        Title = "Vyral - Actions";
    }

    [RelayCommand]
    private async Task NavigateToSettingsAsync()
    {
        _logger.LogInformation("User clicked: Navigate to Settings");

        // Check if setup is in progress and prompt user
        if (!CheckSetupInProgress(out _))
        {
            _logger.LogInformation("Navigation cancelled - setup in progress");
            return; // User cancelled navigation
        }

        try
        {
            // CRITICAL ORDER: Null CurrentViewModel FIRST to release WPF references
            CurrentViewModel = null;

            // Now dispose after WPF releases its references
            await DisposeNonEssentialViewModelsAsync(excludeViewModel: _settingsViewModel);

            // Always recreate Settings to ensure fresh state and free memory when navigating away
            _settingsScope?.Dispose();
            _settingsScope = _serviceProvider.CreateScope();
            _settingsViewModel = _settingsScope.ServiceProvider.GetRequiredService<SettingsViewModel>();
            await _settingsViewModel.InitializeAsync();

            _logger.LogInformation("Setting CurrentViewModel to SettingsViewModel");
            CurrentViewModel = _settingsViewModel;
            Title = "Vyral - Settings";
            _logger.LogInformation("Navigation to Settings complete");

            // Force GC collection
            GC.Collect(1, GCCollectionMode.Optimized, blocking: false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error navigating to settings");
            System.Diagnostics.Debug.WriteLine($"Error navigating to settings: {ex}");
        }
    }

    [RelayCommand]
    private async Task NavigateToAudioSettingsAsync()
    {
        _logger.LogInformation("User clicked: Navigate to Audio Settings");

        if (!CheckSetupInProgress(out _))
        {
            _logger.LogInformation("Navigation cancelled - setup in progress");
            return;
        }

        try
        {
            CurrentViewModel = null;
            await DisposeNonEssentialViewModelsAsync(excludeViewModel: _audioSettingsViewModel);

            _audioSettingsScope?.Dispose();
            _audioSettingsScope = _serviceProvider.CreateScope();
            _audioSettingsViewModel = _audioSettingsScope.ServiceProvider.GetRequiredService<AudioSettingsViewModel>();
            await _audioSettingsViewModel.InitializeAsync();

            _logger.LogInformation("Setting CurrentViewModel to AudioSettingsViewModel");
            CurrentViewModel = _audioSettingsViewModel;
            Title = "Vyral - Audio Settings";
            _logger.LogInformation("Navigation to Audio Settings complete");

            GC.Collect(1, GCCollectionMode.Optimized, blocking: false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error navigating to audio settings");
            System.Diagnostics.Debug.WriteLine($"Error navigating to audio settings: {ex}");
        }
    }

    [RelayCommand]
    private async Task NavigateToSoundsAsync()
    {
        _logger.LogInformation("User clicked: Navigate to Sounds");

        if (!CheckSetupInProgress(out _))
        {
            _logger.LogInformation("Navigation cancelled - setup in progress");
            return;
        }

        try
        {
            CurrentViewModel = null;
            await DisposeNonEssentialViewModelsAsync(excludeViewModel: _soundsViewModel);

            _soundsScope?.Dispose();
            _soundsScope = _serviceProvider.CreateScope();
            _soundsViewModel = _soundsScope.ServiceProvider.GetRequiredService<SoundsViewModel>();
            await _soundsViewModel.InitializeAsync();

            _logger.LogInformation("Setting CurrentViewModel to SoundsViewModel");
            CurrentViewModel = _soundsViewModel;
            Title = "Vyral - Sounds Management";
            _logger.LogInformation("Navigation to Sounds complete");

            GC.Collect(1, GCCollectionMode.Optimized, blocking: false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error navigating to sounds");
            System.Diagnostics.Debug.WriteLine($"Error navigating to sounds: {ex}");
        }
    }

    [RelayCommand]
    private async Task NavigateToStatisticsAsync()
    {
        _logger.LogInformation("User clicked: Navigate to Statistics");

        if (!CheckSetupInProgress(out _))
        {
            _logger.LogInformation("Navigation cancelled - setup in progress");
            return;
        }

        try
        {
            CurrentViewModel = null;
            await DisposeNonEssentialViewModelsAsync(excludeViewModel: _statisticsViewModel);

            _statisticsScope?.Dispose();
            _statisticsScope = _serviceProvider.CreateScope();
            _statisticsViewModel = _statisticsScope.ServiceProvider.GetRequiredService<StatisticsViewModel>();
            await _statisticsViewModel.InitializeAsync();

            _logger.LogInformation("Setting CurrentViewModel to StatisticsViewModel");
            CurrentViewModel = _statisticsViewModel;
            Title = "Vyral - Statistics";
            _logger.LogInformation("Navigation to Statistics complete");

            GC.Collect(1, GCCollectionMode.Optimized, blocking: false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error navigating to statistics");
            System.Diagnostics.Debug.WriteLine($"Error navigating to statistics: {ex}");
        }
    }

    [RelayCommand]
    private async Task NavigateToEventHistoryAsync()
    {
        _logger.LogInformation("User clicked: Navigate to Event History");

        if (!CheckSetupInProgress(out _))
        {
            _logger.LogInformation("Navigation cancelled - setup in progress");
            return;
        }

        try
        {
            CurrentViewModel = null;
            await DisposeNonEssentialViewModelsAsync(excludeViewModel: _eventHistoryViewModel);

            _eventHistoryScope?.Dispose();
            _eventHistoryScope = _serviceProvider.CreateScope();
            _eventHistoryViewModel = _eventHistoryScope.ServiceProvider.GetRequiredService<EventHistoryViewModel>();
            await _eventHistoryViewModel.InitializeAsync();

            _logger.LogInformation("Setting CurrentViewModel to EventHistoryViewModel");
            CurrentViewModel = _eventHistoryViewModel;
            Title = "Vyral - Event History";
            _logger.LogInformation("Navigation to Event History complete");

            GC.Collect(1, GCCollectionMode.Optimized, blocking: false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error navigating to event history");
            System.Diagnostics.Debug.WriteLine($"Error navigating to event history: {ex}");
        }
    }

    [RelayCommand]
    private async Task NavigateToAlertSettingsAsync()
    {
        _logger.LogInformation("User clicked: Navigate to Alert Settings");

        if (!CheckSetupInProgress(out _))
        {
            _logger.LogInformation("Navigation cancelled - setup in progress");
            return;
        }

        try
        {
            CurrentViewModel = null;
            await DisposeNonEssentialViewModelsAsync(excludeViewModel: _alertSettingsViewModel);

            _alertSettingsScope?.Dispose();
            _alertSettingsScope = _serviceProvider.CreateScope();
            _alertSettingsViewModel = _alertSettingsScope.ServiceProvider.GetRequiredService<AlertSettingsViewModel>();
            await _alertSettingsViewModel.InitializeAsync();

            _logger.LogInformation("Setting CurrentViewModel to AlertSettingsViewModel");
            CurrentViewModel = _alertSettingsViewModel;
            Title = "Vyral - Alert Settings";
            _logger.LogInformation("Navigation to Alert Settings complete");

            GC.Collect(1, GCCollectionMode.Optimized, blocking: false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error navigating to alert settings");
            System.Diagnostics.Debug.WriteLine($"Error navigating to alert settings: {ex}");
        }
    }

    [RelayCommand]
    private async Task NavigateToConditionalRulesAsync()
    {
        _logger.LogInformation("User clicked: Navigate to Conditional Rules");

        if (!CheckSetupInProgress(out _))
        {
            _logger.LogInformation("Navigation cancelled - setup in progress");
            return;
        }

        try
        {
            CurrentViewModel = null;
            await DisposeNonEssentialViewModelsAsync(excludeViewModel: _conditionalRulesViewModel);

            _conditionalRulesScope?.Dispose();
            _conditionalRulesScope = _serviceProvider.CreateScope();
            _conditionalRulesViewModel = _conditionalRulesScope.ServiceProvider.GetRequiredService<ConditionalRulesViewModel>();
            await _conditionalRulesViewModel.LoadRulesAsync();

            _logger.LogInformation("Setting CurrentViewModel to ConditionalRulesViewModel");
            CurrentViewModel = _conditionalRulesViewModel;
            Title = "Vyral - Conditional Rules";
            _logger.LogInformation("Navigation to Conditional Rules complete");

            GC.Collect(1, GCCollectionMode.Optimized, blocking: false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error navigating to Conditional Rules");
            System.Diagnostics.Debug.WriteLine($"Error navigating to Conditional Rules: {ex}");
        }
    }

    [RelayCommand]
    private async Task NavigateToGiveawaysAsync()
    {
        _logger.LogInformation("User clicked: Navigate to Giveaways");

        if (!CheckSetupInProgress(out _))
        {
            _logger.LogInformation("Navigation cancelled - setup in progress");
            return;
        }

        try
        {
            CurrentViewModel = null;
            await DisposeNonEssentialViewModelsAsync(excludeViewModel: _giveawayViewModel);

            // GiveawayViewModel is a DI singleton, so its EventBus subscriptions
            // and debounce timer outlive the scope — release them before the
            // scope is recycled; InitializeAsync re-subscribes on next navigation.
            _giveawayViewModel?.Cleanup();
            _giveawayScope?.Dispose();
            _giveawayScope = _serviceProvider.CreateScope();
            _giveawayViewModel = _giveawayScope.ServiceProvider.GetRequiredService<GiveawayViewModel>();
            await _giveawayViewModel.InitializeAsync();

            _logger.LogInformation("Setting CurrentViewModel to GiveawayViewModel");
            CurrentViewModel = _giveawayViewModel;
            Title = "Vyral - Giveaways";
            _logger.LogInformation("Navigation to Giveaways complete");

            GC.Collect(1, GCCollectionMode.Optimized, blocking: false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error navigating to giveaways");
            System.Diagnostics.Debug.WriteLine($"Error navigating to giveaways: {ex}");
        }
    }

    [RelayCommand]
    private async Task NavigateToPollsAsync()
    {
        _logger.LogInformation("User clicked: Navigate to Polls");

        if (!CheckSetupInProgress(out _))
        {
            _logger.LogInformation("Navigation cancelled - setup in progress");
            return;
        }

        try
        {
            CurrentViewModel = null;
            await DisposeNonEssentialViewModelsAsync(excludeViewModel: _pollViewModel);

            _pollScope?.Dispose();
            _pollScope = _serviceProvider.CreateScope();
            _pollViewModel = _pollScope.ServiceProvider.GetRequiredService<PollViewModel>();
            await _pollViewModel.InitializeAsync();

            _logger.LogInformation("Setting CurrentViewModel to PollViewModel");
            CurrentViewModel = _pollViewModel;
            Title = "Vyral - Polls";
            _logger.LogInformation("Navigation to Polls complete");

            GC.Collect(1, GCCollectionMode.Optimized, blocking: false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error navigating to polls");
            System.Diagnostics.Debug.WriteLine($"Error navigating to polls: {ex}");
        }
    }

    [RelayCommand]
    private async Task NavigateToWatchtimeAsync()
    {
        _logger.LogInformation("User clicked: Navigate to Watchtime");

        if (!CheckSetupInProgress(out _))
        {
            _logger.LogInformation("Navigation cancelled - setup in progress");
            return;
        }

        try
        {
            CurrentViewModel = null;
            await DisposeNonEssentialViewModelsAsync(excludeViewModel: _watchtimeViewModel);

            _watchtimeScope?.Dispose();
            _watchtimeScope = _serviceProvider.CreateScope();
            _watchtimeViewModel = _watchtimeScope.ServiceProvider.GetRequiredService<WatchtimeViewModel>();
            await _watchtimeViewModel.InitializeAsync();

            _logger.LogInformation("Setting CurrentViewModel to WatchtimeViewModel");
            CurrentViewModel = _watchtimeViewModel;
            Title = "Vyral - Watchtime";
            _logger.LogInformation("Navigation to Watchtime complete");

            GC.Collect(1, GCCollectionMode.Optimized, blocking: false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error navigating to watchtime");
            System.Diagnostics.Debug.WriteLine($"Error navigating to watchtime: {ex}");
        }
    }

    [RelayCommand]
    private async Task NavigateToEventTestingAsync()
    {
        _logger.LogInformation("User clicked: Navigate to Event Testing");

        if (!CheckSetupInProgress(out _))
        {
            _logger.LogInformation("Navigation cancelled - setup in progress");
            return;
        }

        try
        {
            CurrentViewModel = null;
            await DisposeNonEssentialViewModelsAsync(excludeViewModel: _eventTestingViewModel);

            _eventTestingScope?.Dispose();
            _eventTestingScope = _serviceProvider.CreateScope();
            _eventTestingViewModel = _eventTestingScope.ServiceProvider.GetRequiredService<EventTestingViewModel>();

            _logger.LogInformation("Setting CurrentViewModel to EventTestingViewModel");
            CurrentViewModel = _eventTestingViewModel;
            Title = "Vyral - Event Testing Tool";
            _logger.LogInformation("Navigation to Event Testing complete");

            GC.Collect(1, GCCollectionMode.Optimized, blocking: false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error navigating to event testing");
            System.Diagnostics.Debug.WriteLine($"Error navigating to event testing: {ex}");
        }
    }

    private IServiceScope? _discordSetupPageScope;
    private DiscordWebSetupViewModel? _discordSetupPageViewModel;

    [RelayCommand]
    private Task NavigateToDiscordSetupAsync()
    {
        _logger.LogInformation("User opened: Discord setup wizard (full page)");
        try
        {
            if (_discordSetupPageViewModel == null)
            {
                _discordSetupPageScope?.Dispose();
                _discordSetupPageScope = _serviceProvider.CreateScope();
                _discordSetupPageViewModel = _discordSetupPageScope.ServiceProvider.GetRequiredService<DiscordWebSetupViewModel>();

                _discordSetupPageViewModel.SetupCompleted += (s, e) =>
                {
                    _logger.LogInformation("Discord setup wizard completed, navigating to Platforms");
                    // Leave the wizard page BEFORE navigating — otherwise the
                    // setup-in-progress gate would prompt on completion.
                    CurrentViewModel = null;
                    NavigateToPlatformsCommand.Execute(null);
                };
            }

            CurrentViewModel = _discordSetupPageViewModel;
            Title = "Vyral - Discord Setup";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error navigating to Discord setup wizard");
        }

        return Task.CompletedTask;
    }

    [RelayCommand]
    private Task NavigateToDemoWizardAsync()
    {
        _logger.LogInformation("User clicked: Navigate to Demo Wizard");
        try
        {
            if (_demoSetupViewModel == null)
            {
                // Dispose old scope if it exists
                _demoSetupScope?.Dispose();

                // Create a new scope for this ViewModel
                _demoSetupScope = _serviceProvider.CreateScope();
                _demoSetupViewModel = _demoSetupScope.ServiceProvider.GetRequiredService<GoogleDemoSetupViewModel>();
                _logger.LogInformation("GoogleDemoSetupViewModel created");

                // Hook up completion event
                _demoSetupViewModel.SetupCompleted += (s, e) =>
                {
                    _logger.LogInformation("Demo wizard completed, navigating to home");
                    NavigateToHomeCommand.Execute(null);
                };
            }

            CurrentViewModel = _demoSetupViewModel;
            Title = "Vyral - Demo Setup Wizard";
            _logger.LogInformation("Navigation to Demo Wizard complete");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error navigating to demo wizard");
        }

        return Task.CompletedTask;
    }

    [RelayCommand]
    public void NavigateToNgrokSetupWizard()
    {
        _logger.LogInformation("User clicked: Navigate to Ngrok Setup");
        try
        {
            if (_ngrokSetupWizardViewModel == null)
            {
                // Dispose old scope if it exists
                _ngrokSetupWizardScope?.Dispose();

                // Create a new scope for this ViewModel
                _ngrokSetupWizardScope = _serviceProvider.CreateScope();
                _ngrokSetupWizardViewModel = _ngrokSetupWizardScope.ServiceProvider.GetRequiredService<NgrokSetupWizardViewModel>();
                _logger.LogInformation("NgrokSetupWizardViewModel created");

                // Hook up completion event
                _ngrokSetupWizardViewModel.SetupCompleted += (s, e) =>
                {
                    _logger.LogInformation("Ngrok setup wizard completed, navigating to home");
                    NavigateToHomeCommand.Execute(null);
                };
            }

            CurrentViewModel = _ngrokSetupWizardViewModel;
            Title = "Vyral - Ngrok Setup";
            _logger.LogInformation("Navigation to Ngrok Setup complete");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error navigating to ngrok setup wizard");
        }
    }

    public void DisposeSettingsViewModel()
    {
        _logger.LogInformation("Disposing SettingsViewModel to force refresh");
        _settingsViewModel = null;
        _settingsScope?.Dispose();
        _settingsScope = null;
    }

    [RelayCommand]
    private async Task NavigateToPlatformsAsync()
    {
        _logger.LogInformation("User clicked: Navigate to Platforms");

        // Captured so a failed navigation can restore it — never leave a blank content area
        var previousViewModel = CurrentViewModel;

        try
        {
            // Prompt when a setup wizard is open (Twitch/Kick/YouTube/TikTok
            // overlays, or the Discord full-page wizard) — same as going Home.
            if (!CheckSetupInProgress(out _))
            {
                return; // User cancelled navigation
            }

            // CRITICAL ORDER: Null CurrentViewModel FIRST to release WPF references
            CurrentViewModel = null;

            // Now dispose after WPF releases its references
            await DisposeNonEssentialViewModelsAsync(excludeViewModel: _platformsViewModel);

            // Always recreate Platforms to ensure fresh state
            _platformsScope?.Dispose();
            _platformsScope = _serviceProvider.CreateScope();
            _platformsViewModel = _platformsScope.ServiceProvider.GetRequiredService<PlatformsViewModel>();

            // Set callbacks for platform connections and navigation
            _platformsViewModel.SetConnectionChangedCallback(async () =>
            {
                await RefreshPlatformConnectionsAsync();
            });

            // Discord setup opens as its own full-window page
            _platformsViewModel.SetDiscordSetupCallback(() => NavigateToDiscordSetupCommand.Execute(null));

            // Header back button: Home when idle, Connections when a wizard is open
            _platformsViewModel.SetHomeNavigationCallback(async () =>
            {
                await NavigateToHome();
            });

            _platformsViewModel.SetNavigationCallback(async () =>
            {
                // Just ensure Platforms view is current, don't re-initialize
                if (CurrentViewModel != _platformsViewModel)
                {
                    CurrentViewModel = _platformsViewModel;
                    Title = "Vyral - Connections";
                }
                await Task.CompletedTask;
            });

            await _platformsViewModel.InitializeAsync();
            CurrentViewModel = _platformsViewModel;
            Title = "Vyral - Connections";

            // Don't call CheckPlatformConnectionsAsync - InitializeAsync already loaded platform status
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error navigating to Platforms");
            // Restore the previous view so a failed navigation never leaves a blank content area
            CurrentViewModel = previousViewModel;
        }
    }

    [RelayCommand]
    private async Task NavigateToChatHubAsync()
    {
        _logger.LogInformation("User clicked: Navigate to Chat Hub");

        if (!CheckSetupInProgress(out _))
            return;

        try
        {
            CurrentViewModel = null;
            await DisposeNonEssentialViewModelsAsync(excludeViewModel: _chatHubViewModel);

            _chatHubScope?.Dispose();
            _chatHubScope = _serviceProvider.CreateScope();
            _chatHubViewModel = _chatHubScope.ServiceProvider.GetRequiredService<ChatHubViewModel>();
            await _chatHubViewModel.InitializeAsync();
            CurrentViewModel = _chatHubViewModel;
            Title = "Vyral - Chat Hub";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error navigating to Chat Hub");
        }
    }

    /// <summary>
    /// Opens the Moderator page: mod channel setup, mod role assignments,
    /// the action log and the !mod command builder.
    /// </summary>
    [RelayCommand]
    private async Task NavigateToModeratorAsync()
    {
        _logger.LogInformation("User clicked: Navigate to Moderator");

        if (!CheckSetupInProgress(out _))
            return;

        try
        {
            CurrentViewModel = null;
            await DisposeNonEssentialViewModelsAsync(excludeViewModel: _moderatorViewModel);

            _moderatorScope?.Dispose();
            _moderatorScope = _serviceProvider.CreateScope();
            _moderatorViewModel = _moderatorScope.ServiceProvider.GetRequiredService<ModeratorViewModel>();
            await _moderatorViewModel.InitializeAsync();
            CurrentViewModel = _moderatorViewModel;
            Title = "Vyral - Moderator";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error navigating to Moderator");
        }
    }

    [RelayCommand]
    private async Task NavigateToToolsAsync()
    {
        _logger.LogInformation("User clicked: Navigate to Tools");

        if (!CheckSetupInProgress(out _))
            return;

        try
        {
            CurrentViewModel = null;
            await DisposeNonEssentialViewModelsAsync(excludeViewModel: _toolsViewModel);

            _toolsScope?.Dispose();
            _toolsScope = _serviceProvider.CreateScope();
            _toolsViewModel = _toolsScope.ServiceProvider.GetRequiredService<ToolsViewModel>();
            CurrentViewModel = _toolsViewModel;
            Title = "Vyral - Tools";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error navigating to Tools");
        }
    }

    [RelayCommand]
    private async Task NavigateToIntegrationsAsync()
    {
        _logger.LogInformation("User clicked: Navigate to Integrations");

        if (!CheckSetupInProgress(out _))
            return;

        try
        {
            CurrentViewModel = null;
            await DisposeNonEssentialViewModelsAsync(excludeViewModel: _integrationsViewModel);

            _integrationsScope?.Dispose();
            _integrationsScope = _serviceProvider.CreateScope();
            _integrationsViewModel = _integrationsScope.ServiceProvider.GetRequiredService<IntegrationsViewModel>();
            CurrentViewModel = _integrationsViewModel;
            Title = "Vyral - Integrations";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error navigating to Integrations");
        }
    }

    [RelayCommand]
    private async Task NavigateToObsAsync()
    {
        _logger.LogInformation("User clicked: Navigate to OBS");

        if (!CheckSetupInProgress(out _))
            return;

        try
        {
            CurrentViewModel = null;
            await DisposeNonEssentialViewModelsAsync(excludeViewModel: _obsViewModel);

            _obsScope?.Dispose();
            _obsScope = _serviceProvider.CreateScope();
            _obsViewModel = _obsScope.ServiceProvider.GetRequiredService<ObsViewModel>();
            // Show the page immediately — OBS hosting (launch + window
            // discovery) can take tens of seconds and streams its progress
            // through the page's own status line.
            CurrentViewModel = _obsViewModel;
            Title = "Vyral - OBS";
            await _obsViewModel.InitializeAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error navigating to OBS");
        }
    }

    [RelayCommand]
    private async Task NavigateToRestreamAsync()
    {
        _logger.LogInformation("User clicked: Navigate to Restream");

        if (!CheckSetupInProgress(out _))
            return;

        try
        {
            CurrentViewModel = null;
            await DisposeNonEssentialViewModelsAsync(excludeViewModel: _restreamViewModel);

            _restreamScope?.Dispose();
            _restreamScope = _serviceProvider.CreateScope();
            _restreamViewModel = _restreamScope.ServiceProvider.GetRequiredService<RestreamViewModel>();
            CurrentViewModel = _restreamViewModel;
            Title = "Vyral - Restream";
            await _restreamViewModel.InitializeAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error navigating to Restream");
        }
    }

    [RelayCommand]
    private async Task NavigateToStreamDeckAsync()
    {
        _logger.LogInformation("User clicked: Navigate to StreamDeck");

        if (!CheckSetupInProgress(out _))
            return;

        try
        {
            CurrentViewModel = null;
            await DisposeNonEssentialViewModelsAsync(excludeViewModel: _streamDeckViewModel);

            _streamDeckScope?.Dispose();
            _streamDeckScope = _serviceProvider.CreateScope();
            _streamDeckViewModel = _streamDeckScope.ServiceProvider.GetRequiredService<StreamDeckViewModel>();
            CurrentViewModel = _streamDeckViewModel;
            Title = "Vyral - StreamDeck";
            await _streamDeckViewModel.InitializeAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error navigating to StreamDeck");
        }
    }

    [RelayCommand]
    private Task OpenDiscordLinkAsync() => OpenSupportLinkAsync("Support.DiscordUrl", "Discord");

    [RelayCommand]
    private Task OpenIdeasLinkAsync() => OpenSupportLinkAsync("Support.IdeasUrl", "Ideas & Suggestions");

    private async Task OpenSupportLinkAsync(string configKey, string label)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var dataService = scope.ServiceProvider.GetRequiredService<IDataService>();
            var url = await dataService.GetConfigValueAsync(configKey);

            if (string.IsNullOrWhiteSpace(url))
            {
                // No link configured: do nothing visible. A stray click on the wide
                // support row (e.g. focusing the window after launch) must never
                // produce a popup — no other platform shows one either.
                _logger.LogInformation("{Label} support link not configured; ignoring click", label);
                return;
            }

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error opening {Label} support link", label);
            System.Windows.MessageBox.Show($"Could not open the {label} link: {ex.Message}", label,
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private async Task NavigateToDocumentationAsync()
    {
        _logger.LogInformation("User clicked: Navigate to Documentation");

        if (!CheckSetupInProgress(out _))
            return;

        try
        {
            CurrentViewModel = null;
            await DisposeNonEssentialViewModelsAsync(excludeViewModel: _documentationViewModel);

            _documentationScope?.Dispose();
            _documentationScope = _serviceProvider.CreateScope();
            _documentationViewModel = _documentationScope.ServiceProvider.GetRequiredService<DocumentationViewModel>();
            CurrentViewModel = _documentationViewModel;
            Title = "Vyral - Documentation";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error navigating to Documentation");
        }
    }

    [RelayCommand]
    private async Task NavigateToTwitchSettingsAsync()
    {
        _logger.LogInformation("User clicked: Navigate to Twitch Dashboard");

        // Check if setup is in progress and prompt user
        if (!CheckSetupInProgress(out _))
        {
            return; // User cancelled navigation
        }

        try
        {
            // CRITICAL ORDER: Null CurrentViewModel FIRST to release WPF references
            CurrentViewModel = null;

            // Now dispose after WPF releases its references
            await DisposeNonEssentialViewModelsAsync(excludeViewModel: _twitchDashboardViewModel);

            // Always recreate to free memory
            _twitchDashboardScope?.Dispose();
            _twitchDashboardScope = _serviceProvider.CreateScope();
            _twitchDashboardViewModel = _twitchDashboardScope.ServiceProvider.GetRequiredService<TwitchDashboardViewModel>();
            await _twitchDashboardViewModel.InitializeAsync();
            CurrentViewModel = _twitchDashboardViewModel;
            Title = "Vyral - Twitch";
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error navigating to Twitch dashboard: {ex}");
        }
    }

    [RelayCommand]
    private async Task NavigateToYouTubeSettingsAsync()
    {
        _logger.LogInformation("User clicked: Navigate to YouTube Dashboard");

        // Check if setup is in progress and prompt user
        if (!CheckSetupInProgress(out _))
        {
            return; // User cancelled navigation
        }

        try
        {
            // CRITICAL ORDER: Null CurrentViewModel FIRST to release WPF references
            CurrentViewModel = null;

            // Now dispose after WPF releases its references
            await DisposeNonEssentialViewModelsAsync(excludeViewModel: _youTubeDashboardViewModel);

            // Always recreate to free memory
            _youTubeDashboardScope?.Dispose();
            _youTubeDashboardScope = _serviceProvider.CreateScope();
            _youTubeDashboardViewModel = _youTubeDashboardScope.ServiceProvider.GetRequiredService<YouTubeDashboardViewModel>();
            await _youTubeDashboardViewModel.InitializeAsync();
            CurrentViewModel = _youTubeDashboardViewModel;
            Title = "Vyral - YouTube";
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error navigating to YouTube dashboard: {ex}");
        }
    }

    [RelayCommand]
    private async Task NavigateToKickSettingsAsync()
    {
        _logger.LogInformation("User clicked: Navigate to Kick Dashboard");

        // Check if setup is in progress and prompt user
        if (!CheckSetupInProgress(out _))
        {
            return; // User cancelled navigation
        }

        try
        {
            // CRITICAL ORDER: Null CurrentViewModel FIRST to release WPF references
            CurrentViewModel = null;

            // Now dispose after WPF releases its references
            await DisposeNonEssentialViewModelsAsync(excludeViewModel: _kickDashboardViewModel);

            // Always recreate to free memory
            _kickDashboardScope?.Dispose();
            _kickDashboardScope = _serviceProvider.CreateScope();
            _kickDashboardViewModel = _kickDashboardScope.ServiceProvider.GetRequiredService<KickDashboardViewModel>();
            await _kickDashboardViewModel.InitializeAsync();
            CurrentViewModel = _kickDashboardViewModel;
            Title = "Vyral - Kick";
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error navigating to Kick dashboard: {ex}");
        }
    }

    [RelayCommand]
    private async Task NavigateToDiscordDashboardAsync()
    {
        _logger.LogInformation("User clicked: Navigate to Discord Dashboard");

        // Check if setup is in progress and prompt user
        if (!CheckSetupInProgress(out _))
        {
            return; // User cancelled navigation
        }

        try
        {
            // CRITICAL ORDER: Null CurrentViewModel FIRST to release WPF references
            CurrentViewModel = null;

            // Now dispose after WPF releases its references
            await DisposeNonEssentialViewModelsAsync(excludeViewModel: _discordDashboardViewModel);

            // Always recreate to free memory
            _discordDashboardScope?.Dispose();
            _discordDashboardScope = _serviceProvider.CreateScope();
            _discordDashboardViewModel = _discordDashboardScope.ServiceProvider.GetRequiredService<DiscordDashboardViewModel>();
            await _discordDashboardViewModel.InitializeAsync();
            CurrentViewModel = _discordDashboardViewModel;
            Title = "Vyral - Discord";
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error navigating to Discord dashboard: {ex}");
        }
    }

    /// <summary>
    /// Opens the Discord Bots page: the multi-bot home where bots are added,
    /// started/stopped, renamed, removed, and opened into their own dashboards.
    /// </summary>
    [RelayCommand]
    private async Task NavigateToDiscordBotsAsync()
    {
        _logger.LogInformation("User clicked: Navigate to Discord Bots");

        // Check if setup is in progress and prompt user
        if (!CheckSetupInProgress(out _))
        {
            return; // User cancelled navigation
        }

        try
        {
            // CRITICAL ORDER: Null CurrentViewModel FIRST to release WPF references
            CurrentViewModel = null;

            // Now dispose after WPF releases its references
            await DisposeNonEssentialViewModelsAsync(excludeViewModel: _discordBotsViewModel);

            // Always recreate to ensure fresh state
            _discordBotsScope?.Dispose();
            _discordBotsScope = _serviceProvider.CreateScope();
            _discordBotsViewModel = _discordBotsScope.ServiceProvider.GetRequiredService<DiscordBotsViewModel>();

            // Opening a bot card opens that bot's own dashboard
            _discordBotsViewModel.SetOpenDetailCallback(botId => _ = NavigateToDiscordBotDetailAsync(botId));

            // Secondary add path: the full Discord setup wizard (full-page)
            _discordBotsViewModel.SetOpenWizardCallback(() => NavigateToDiscordSetupCommand.Execute(null));

            await _discordBotsViewModel.InitializeAsync();
            CurrentViewModel = _discordBotsViewModel;
            Title = "Vyral - Discord Bots";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error navigating to Discord Bots");
            System.Diagnostics.Debug.WriteLine($"Error navigating to Discord Bots: {ex}");
        }
    }

    /// <summary>
    /// Opens the per-bot dashboard for a specific Discord bot.
    /// Invoked from the Discord Bots page via the open-detail callback.
    /// </summary>
    private async Task NavigateToDiscordBotDetailAsync(string botId)
    {
        if (string.IsNullOrEmpty(botId))
            return;

        _logger.LogInformation("User clicked: Open Discord bot {BotId} dashboard", botId);

        try
        {
            // CRITICAL ORDER: Null CurrentViewModel FIRST to release WPF references
            CurrentViewModel = null;

            // Now dispose after WPF releases its references
            await DisposeNonEssentialViewModelsAsync(excludeViewModel: _discordBotDetailViewModel);

            // Always recreate to ensure fresh state for the selected bot
            _discordBotDetailScope?.Dispose();
            _discordBotDetailScope = _serviceProvider.CreateScope();
            _discordBotDetailViewModel = _discordBotDetailScope.ServiceProvider.GetRequiredService<DiscordBotDetailViewModel>();
            await _discordBotDetailViewModel.LoadAsync(botId);
            CurrentViewModel = _discordBotDetailViewModel;
            Title = "Vyral - Discord Bot";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error navigating to Discord bot detail");
            System.Diagnostics.Debug.WriteLine($"Error navigating to Discord bot detail: {ex}");
        }
    }

    /// <summary>
    /// Shows the profile selector overlay with the list of profiles.
    /// Called during app startup if multiple profiles exist.
    /// </summary>
    public async Task ShowProfileSelectorAsync()
    {
        try
        {
            var profilesList = await _profileService.GetProfilesAsync();
            Profiles = new ObservableCollection<Profile>(profilesList);
            ShowProfileSelector = true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading profiles: {ex}");
        }
    }

    [RelayCommand]
    private void SelectProfile(Profile profile)
    {
        _logger.LogInformation("User clicked: Select Profile '{DisplayName}' ({Name})", profile.DisplayName, profile.Name);
        _selectedProfile = profile;

        // If profile has PIN, show PIN entry
        if (!string.IsNullOrEmpty(profile.PinCode))
        {
            _logger.LogInformation("Profile has PIN protection, showing PIN entry");
            ShowPinEntry = true;
            ProfileErrorMessage = null;
        }
        else
        {
            // No PIN, select immediately
            _logger.LogInformation("Profile has no PIN, selecting immediately");
            _ = ConfirmProfileSelectionAsync();
        }
    }

    [RelayCommand]
    private async Task ConfirmProfileSelectionAsync()
    {
        if (_selectedProfile == null) return;

        _logger.LogInformation("User clicked: Confirm Profile Selection for '{ProfileName}'", _selectedProfile.Name);

        try
        {
            // Validate PIN if profile has one
            if (!string.IsNullOrEmpty(_selectedProfile.PinCode))
            {
                _logger.LogInformation("Validating PIN for profile selection");
                var isValid = await _profileService.ValidatePinAsync(_selectedProfile.Name, ProfilePin);
                if (!isValid)
                {
                    _logger.LogWarning("Invalid PIN provided");
                    ProfileErrorMessage = "Invalid PIN. Please try again.";
                    return;
                }
                _logger.LogInformation("PIN validated successfully");
            }

            await _profileService.SwitchProfileAsync(_selectedProfile.Name, string.IsNullOrEmpty(ProfilePin) ? null : ProfilePin);
            _logger.LogInformation("Successfully switched to profile '{ProfileName}'", _selectedProfile.Name);
            ShowProfileSelector = false;
            ShowPinEntry = false;
            ProfilePin = string.Empty;
            ProfileErrorMessage = null;

            // Update title with new profile
            _ = UpdateTitleAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error switching to profile '{ProfileName}'", _selectedProfile.Name);
            ProfileErrorMessage = $"Error switching profile: {ex.Message}";
        }
    }

    /// <summary>
    /// Disposes non-essential ViewModels to free memory when navigating between views.
    /// Essential ViewModels (Dashboard, Chat) are kept alive for performance.
    /// </summary>
    private async Task DisposeNonEssentialViewModelsAsync(ViewModelBase? excludeViewModel = null)
    {
        var disposedCount = 0;

        // NEVER dispose ChatViewModel (singleton, frequently used)
        // NEVER dispose DashboardViewModel (singleton, lightweight home screen)
        // ALWAYS dispose everything else when navigating away

        // Dispose Settings (heavy, loads all settings on each init)
        if (_settingsViewModel != null && _settingsViewModel != excludeViewModel)
        {
            _settingsScope?.Dispose();
            _settingsScope = null;
            _settingsViewModel = null;
            _logger.LogInformation("♻️ Disposed SettingsViewModel to free memory");
            disposedCount++;
        }

        // Dispose Audio Settings (not frequently used)
        if (_audioSettingsViewModel != null && _audioSettingsViewModel != excludeViewModel)
        {
            _audioSettingsScope?.Dispose();
            _audioSettingsScope = null;
            _audioSettingsViewModel = null;
            _logger.LogInformation("♻️ Disposed AudioSettingsViewModel to free memory");
            disposedCount++;
        }

        // Dispose Sounds (not frequently used)
        if (_soundsViewModel != null && _soundsViewModel != excludeViewModel)
        {
            _soundsScope?.Dispose();
            _soundsScope = null;
            _soundsViewModel = null;
            _logger.LogInformation("♻️ Disposed SoundsViewModel to free memory");
            disposedCount++;
        }

        // Dispose Statistics (not frequently used)
        if (_statisticsViewModel != null && _statisticsViewModel != excludeViewModel)
        {
            _statisticsScope?.Dispose();
            _statisticsScope = null;
            _statisticsViewModel = null;
            _logger.LogInformation("♻️ Disposed StatisticsViewModel to free memory");
            disposedCount++;
        }

        // Dispose Event History (not frequently used)
        if (_eventHistoryViewModel != null && _eventHistoryViewModel != excludeViewModel)
        {
            _eventHistoryScope?.Dispose();
            _eventHistoryScope = null;
            _eventHistoryViewModel = null;
            _logger.LogInformation("♻️ Disposed EventHistoryViewModel to free memory");
            disposedCount++;
        }

        // Dispose Alert Settings (not frequently used)
        if (_alertSettingsViewModel != null && _alertSettingsViewModel != excludeViewModel)
        {
            _alertSettingsScope?.Dispose();
            _alertSettingsScope = null;
            _alertSettingsViewModel = null;
            _logger.LogInformation("♻️ Disposed AlertSettingsViewModel to free memory");
            disposedCount++;
        }

        // Dispose Conditional Rules (not frequently used)
        if (_conditionalRulesViewModel != null && _conditionalRulesViewModel != excludeViewModel)
        {
            _conditionalRulesScope?.Dispose();
            _conditionalRulesScope = null;
            _conditionalRulesViewModel = null;
            _logger.LogInformation("♻️ Disposed ConditionalRulesViewModel to free memory");
            disposedCount++;
        }

        // Dispose Giveaways (not frequently used)
        if (_giveawayViewModel != null && _giveawayViewModel != excludeViewModel)
        {
            // Singleton VM: release its EventBus subscriptions and debounce
            // timer explicitly, otherwise they live for the app's lifetime.
            _giveawayViewModel.Cleanup();
            _giveawayScope?.Dispose();
            _giveawayScope = null;
            _giveawayViewModel = null;
            _logger.LogInformation("♻️ Disposed GiveawayViewModel to free memory");
            disposedCount++;
        }

        // Dispose Polls (not frequently used)
        if (_pollViewModel != null && _pollViewModel != excludeViewModel)
        {
            _pollScope?.Dispose();
            _pollScope = null;
            _pollViewModel = null;
            _logger.LogInformation("♻️ Disposed PollViewModel to free memory");
            disposedCount++;
        }

        // Dispose Watchtime (not frequently used)
        if (_watchtimeViewModel != null && _watchtimeViewModel != excludeViewModel)
        {
            _watchtimeScope?.Dispose();
            _watchtimeScope = null;
            _watchtimeViewModel = null;
            _logger.LogInformation("♻️ Disposed WatchtimeViewModel to free memory");
            disposedCount++;
        }

        // Dispose Event Testing (developer tool)
        if (_eventTestingViewModel != null && _eventTestingViewModel != excludeViewModel)
        {
            _eventTestingScope?.Dispose();
            _eventTestingScope = null;
            _eventTestingViewModel = null;
            _logger.LogInformation("♻️ Disposed EventTestingViewModel to free memory");
            disposedCount++;
        }

        // Dispose Platforms (re-initializes on each navigation)
        if (_platformsViewModel != null && _platformsViewModel != excludeViewModel)
        {
            _platformsScope?.Dispose();
            _platformsScope = null;
            _platformsViewModel = null;
            _logger.LogInformation("♻️ Disposed PlatformsViewModel to free memory");
            disposedCount++;
        }

        // Dispose Kick Dashboard (not frequently used)
        if (_kickDashboardViewModel != null && _kickDashboardViewModel != excludeViewModel)
        {
            _kickDashboardScope?.Dispose();
            _kickDashboardScope = null;
            _kickDashboardViewModel = null;
            _logger.LogInformation("♻️ Disposed KickDashboardViewModel to free memory");
            disposedCount++;
        }

        // Dispose YouTube Dashboard (not frequently used)
        if (_youTubeDashboardViewModel != null && _youTubeDashboardViewModel != excludeViewModel)
        {
            _youTubeDashboardScope?.Dispose();
            _youTubeDashboardScope = null;
            _youTubeDashboardViewModel = null;
            _logger.LogInformation("♻️ Disposed YouTubeDashboardViewModel to free memory");
            disposedCount++;
        }

        // Dispose Discord Dashboard (not frequently used)
        if (_discordDashboardViewModel != null && _discordDashboardViewModel != excludeViewModel)
        {
            _discordDashboardScope?.Dispose();
            _discordDashboardScope = null;
            _discordDashboardViewModel = null;
            _logger.LogInformation("♻️ Disposed DiscordDashboardViewModel to free memory");
            disposedCount++;
        }

        // Dispose Discord Bots page (re-initializes on each navigation)
        if (_discordBotsViewModel != null && _discordBotsViewModel != excludeViewModel)
        {
            _discordBotsScope?.Dispose();
            _discordBotsScope = null;
            _discordBotsViewModel = null;
            _logger.LogInformation("♻️ Disposed DiscordBotsViewModel to free memory");
            disposedCount++;
        }

        // Dispose Discord Bot Detail (re-created for the selected bot on each navigation)
        if (_discordBotDetailViewModel != null && _discordBotDetailViewModel != excludeViewModel)
        {
            _discordBotDetailScope?.Dispose();
            _discordBotDetailScope = null;
            _discordBotDetailViewModel = null;
            _logger.LogInformation("♻️ Disposed DiscordBotDetailViewModel to free memory");
            disposedCount++;
        }

        // Dispose Twitch Dashboard (not frequently used)
        if (_twitchDashboardViewModel != null && _twitchDashboardViewModel != excludeViewModel)
        {
            _twitchDashboardScope?.Dispose();
            _twitchDashboardScope = null;
            _twitchDashboardViewModel = null;
            _logger.LogInformation("♻️ Disposed TwitchDashboardViewModel to free memory");
            disposedCount++;
        }

        // Dispose Demo Setup (rarely used)
        if (_demoSetupViewModel != null && _demoSetupViewModel != excludeViewModel)
        {
            _demoSetupScope?.Dispose();
            _demoSetupScope = null;
            _demoSetupViewModel = null;
            _logger.LogInformation("♻️ Disposed DemoSetupViewModel to free memory");
            disposedCount++;
        }

        // Dispose Ngrok Setup Wizard (rarely used)
        if (_ngrokSetupWizardViewModel != null && _ngrokSetupWizardViewModel != excludeViewModel)
        {
            _ngrokSetupWizardScope?.Dispose();
            _ngrokSetupWizardScope = null;
            _ngrokSetupWizardViewModel = null;
            _logger.LogInformation("♻️ Disposed NgrokSetupWizardViewModel to free memory");
            disposedCount++;
        }

        // Dispose Chat Hub (hub page, re-initializes each time)
        if (_chatHubViewModel != null && _chatHubViewModel != excludeViewModel)
        {
            _chatHubScope?.Dispose();
            _chatHubScope = null;
            _chatHubViewModel = null;
            _logger.LogInformation("♻️ Disposed ChatHubViewModel to free memory");
            disposedCount++;
        }

        // Dispose Tools (lightweight hub)
        if (_toolsViewModel != null && _toolsViewModel != excludeViewModel)
        {
            _toolsScope?.Dispose();
            _toolsScope = null;
            _toolsViewModel = null;
            _logger.LogInformation("♻️ Disposed ToolsViewModel to free memory");
            disposedCount++;
        }

        // Dispose Integrations (lightweight hub)
        if (_integrationsViewModel != null && _integrationsViewModel != excludeViewModel)
        {
            _integrationsScope?.Dispose();
            _integrationsScope = null;
            _integrationsViewModel = null;
            _logger.LogInformation("♻️ Disposed IntegrationsViewModel to free memory");
            disposedCount++;
        }

        // Dispose Documentation
        if (_documentationViewModel != null && _documentationViewModel != excludeViewModel)
        {
            _documentationScope?.Dispose();
            _documentationScope = null;
            _documentationViewModel = null;
            _logger.LogInformation("♻️ Disposed DocumentationViewModel to free memory");
            disposedCount++;
        }

        // Dispose Automation (workflow builder, re-initializes on each navigation)
        if (_automationViewModel != null && _automationViewModel != excludeViewModel)
        {
            _automationScope?.Dispose();
            _automationScope = null;
            _automationViewModel = null;
            _logger.LogInformation("♻️ Disposed AutomationViewModel to free memory");
            disposedCount++;
        }

        // Dispose Moderator (re-initializes on each navigation)
        if (_moderatorViewModel != null && _moderatorViewModel != excludeViewModel)
        {
            _moderatorScope?.Dispose();
            _moderatorScope = null;
            _moderatorViewModel = null;
            _logger.LogInformation("♻️ Disposed ModeratorViewModel to free memory");
            disposedCount++;
        }

        // Dispose OBS (re-initializes on each navigation)
        if (_obsViewModel != null && _obsViewModel != excludeViewModel)
        {
            // Singleton VMs: the scope below holds singletons, so its Dispose
            // never runs theirs — dispose explicitly or the studio's 1 Hz
            // stats timer keeps polling the native gate after navigation away.
            _obsViewModel.Dispose();
            _obsScope?.Dispose();
            _obsScope = null;
            _obsViewModel = null;
            _logger.LogInformation("♻️ Disposed ObsViewModel to free memory");
            disposedCount++;
        }

        if (_restreamViewModel != null && _restreamViewModel != excludeViewModel)
        {
            // Same singleton trap as OBS: dispose explicitly so the 1 Hz uptime
            // ticker stops. The restream ENGINE is a separate singleton — a
            // live session intentionally keeps streaming after this disposal.
            _restreamViewModel.Dispose();
            _restreamScope?.Dispose();
            _restreamScope = null;
            _restreamViewModel = null;
            _logger.LogInformation("♻️ Disposed RestreamViewModel to free memory");
            disposedCount++;
        }

        if (_streamDeckViewModel != null && _streamDeckViewModel != excludeViewModel)
        {
            // Singleton like Restream: dispose the page VM (event hooks) while
            // the DeckServer singleton intentionally keeps running in the
            // background so paired phones stay connected.
            _streamDeckViewModel.Dispose();
            _streamDeckScope?.Dispose();
            _streamDeckScope = null;
            _streamDeckViewModel = null;
            _logger.LogInformation("♻️ Disposed StreamDeckViewModel to free memory");
            disposedCount++;
        }

        if (disposedCount > 0)
        {
            _logger.LogInformation("♻️ Disposed {Count} ViewModels, forcing UI update and GC...", disposedCount);

            // CRITICAL: Force WPF to process ALL pending UI updates synchronously
            // This ensures ContentControl releases the old view before GC
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                // Force layout and rendering updates
                System.Windows.Application.Current.MainWindow?.UpdateLayout();
            }, System.Windows.Threading.DispatcherPriority.Loaded);

            // Wait for WPF rendering thread to complete
            await Task.Delay(100);

            _logger.LogInformation("♻️ Triggering Gen2 GC with compacting...");
            // Aggressive compacting collection to reclaim SettingsViewModel memory
            // Blocking is necessary to ensure disposal completes before navigation continues
            GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);

            _logger.LogInformation("♻️ Memory reclaim complete");
        }
    }

    [RelayCommand]
    private void CreateNewProfile()
    {
        _logger.LogInformation("User clicked: Create New Profile");
        ShowCreateProfile = true;
        NewProfileName = string.Empty;
        NewProfileDisplayName = string.Empty;
        NewProfilePin = string.Empty;
        ProfileErrorMessage = null;
    }

    [RelayCommand]
    private void CancelCreateProfile()
    {
        _logger.LogInformation("User clicked: Cancel Create Profile");
        ShowCreateProfile = false;
        NewProfileName = string.Empty;
        NewProfileDisplayName = string.Empty;
        NewProfilePin = string.Empty;
        ProfileErrorMessage = null;
    }

    [RelayCommand]
    private async Task ConfirmCreateProfileAsync()
    {
        _logger.LogInformation("User clicked: Confirm Create Profile (Name: '{Name}')", NewProfileName);

        try
        {
            // Validate profile name
            if (string.IsNullOrWhiteSpace(NewProfileName))
            {
                ProfileErrorMessage = "Profile name is required";
                _logger.LogWarning("Profile creation failed: Name is empty");
                return;
            }

            // Create the profile
            var displayName = string.IsNullOrWhiteSpace(NewProfileDisplayName) ? NewProfileName : NewProfileDisplayName;
            var pin = string.IsNullOrWhiteSpace(NewProfilePin) ? null : NewProfilePin;

            _logger.LogInformation("Creating profile '{Name}' with display name '{DisplayName}'", NewProfileName, displayName);
            var profile = await _profileService.CreateProfileAsync(NewProfileName, displayName, pin);

            // Switch to the new profile (pass PIN if it was set)
            await _profileService.SwitchProfileAsync(profile.Name, pin);
            _logger.LogInformation("Successfully created and switched to profile '{Name}'", profile.Name);

            // Hide profile selector
            ShowCreateProfile = false;
            ShowProfileSelector = false;
            ProfileErrorMessage = null;

            // Update title
            _ = UpdateTitleAsync();

            // Refresh the main view
            await CheckPlatformConnectionsAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating profile '{Name}'", NewProfileName);
            ProfileErrorMessage = $"Error creating profile: {ex.Message}";
        }
    }
}
