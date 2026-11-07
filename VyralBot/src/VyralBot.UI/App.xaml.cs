﻿using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using System.IO;
using System.Runtime;
using System.Windows;
using VyralBot.Core.Services.ActionDispatcher;
using VyralBot.Core.Services.EventBus;
using VyralBot.Core.Services.Chat;
using VyralBot.Core.Services.TriggerEngine;
using VyralBot.Core.Services.ProcessManagement;
using VyralBot.Core.Services.Performance;
using VyralBot.Core.Services.Profiles;
using VyralBot.Core.Services.Currency;
using VyralBot.Core.Services.Linking;
using VyralBot.Core.Services.Commands;
using VyralBot.Core.Services.Variables;
using VyralBot.Core.Services;
using VyralBot.Core.Services.Rules;
using VyralBot.Data;
using VyralBot.Integrations.Twitch;
using VyralBot.Integrations.Kick;
using VyralBot.Integrations.YouTube;
using VyralBot.Integrations.Discord;
using VyralBot.UI.ViewModels;
using TwitchLib.EventSub.Websockets.Extensions;
using VyralBot.UI.Services;
using VyralBot.Api.Services;
using VyralBot.Core.Services.WebServer;
using System.Threading;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using VyralBot.Api.Middleware;
using VyralBot.Api.Controllers;

namespace VyralBot.UI;

/// <summary>
/// WPF Application with dependency injection and logging.
/// </summary>
public partial class App : Application
{
    private readonly IHost? _host;
    private static Mutex? _instanceMutex;

    /// <summary>
    /// True once <see cref="OnStartup"/> fully started the host. When startup
    /// fails early, OnExit must skip host teardown — calling StopAsync on a
    /// half-started host throws and the shutdown watchdog force-kills instead.
    /// </summary>
    private static bool _hostStarted;

    /// <summary>
    /// Set by the Settings → Advanced "Restart App" button: OnExit launches a
    /// new instance AFTER all cleanup completes (mutex released, host stopped,
    /// bots disconnected), with a short extra delay so ports settle and the old
    /// process is fully gone — preventing two instances from ever polling the
    /// platform APIs at once.
    /// </summary>
    public static bool PendingRestart { get; set; }

    /// <summary>
    /// Gets the dependency injection host.
    /// </summary>
    public IHost? Host => _host;

    /// <summary>
    /// Gets the service provider for accessing services.
    /// </summary>
    public static IServiceProvider? ServiceProvider { get; private set; }

    public App()
    {
        // Check for existing instance
        bool createdNew;
        _instanceMutex = new Mutex(true, "VyralBot_SingleInstance_Mutex", out createdNew);

        if (!createdNew)
        {
            MessageBox.Show("Vyral is already running.", "Vyral", MessageBoxButton.OK, MessageBoxImage.Information);
            Environment.Exit(0);
            return;
        }

        // Log process ID for debugging
        var processId = System.Diagnostics.Process.GetCurrentProcess().Id;

        // Configure Serilog
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.Console()
            .WriteTo.File(
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs", "vyralbot-.txt"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7)
            .CreateLogger();

        Log.Information("VyralBot starting with PID: {ProcessId}", processId);

        // Enable low-latency GC mode for responsive UI
        // Per updated_performance_upgrade.md: TIER 4 GC optimization
        GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;
        Log.Information("GC configured: LatencyMode={LatencyMode}, IsServerGC={IsServerGC}",
            GCSettings.LatencyMode, GCSettings.IsServerGC);

        // Pre-warm WebView2 environment on background thread
        // Per updated_performance_upgrade.md: TIER 2 optimization
        WebView2EnvironmentManager.Instance.PreWarmAsync();

        // Build host with DI (Kestrel will be started on-demand by WebhookCoordinatorService)
        _host = Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder()
            .UseSerilog()
            .ConfigureServices((context, services) =>
            {
                // Profile service - must be registered first
                services.AddSingleton<IProfileService, ProfileService>();

                // Database - Profile-aware service that dynamically switches databases
                services.AddSingleton<IDataService, ProfileAwareDataService>();

                // Core services - SHARED EventBus instance for both UI and API!
                services.AddSingleton<IEventBus, EventBus>();
                // Chat message store backed by profile DB (persists history until UI loads it)
                services.AddSingleton<IChatMessageStore, VyralBot.UI.Services.Chat.DataChatMessageStore>();
                services.AddSingleton<IChatEventSettingsService, ChatEventSettingsService>();
                services.AddSingleton<IRuleEvaluator, RuleEvaluator>();
                services.AddSingleton<IAutoReplyService, AutoReplyService>();
                services.AddSingleton<IActionDispatcher, ActionDispatcher>();
                services.AddSingleton<ITriggerEngine, TriggerEngine>();
                services.AddSingleton<IProcessManager, ProcessManager>();
                services.AddHostedService<PerformanceMonitor>();

                // Live performance telemetry (Settings → Performance tab):
                // registry is the shared counter sink every instrumented
                // service writes to; telemetry service samples process/child
                // process metrics once per second for the UI. The concrete
                // singleton is what PerformanceViewModel injects; the hosted
                // wrapper starts that same instance with the app lifetime.
                services.AddSingleton<VyralBot.Core.Services.Performance.IMetricsRegistry,
                    VyralBot.Core.Services.Performance.MetricsRegistry>();
                services.AddSingleton<VyralBot.Core.Services.Performance.PerformanceTelemetryService>();
                services.AddHostedService(sp =>
                    sp.GetRequiredService<VyralBot.Core.Services.Performance.PerformanceTelemetryService>());

                // Currency system
                services.AddSingleton<ICurrencyService, CurrencyService>();
                services.AddSingleton<ICurrencyRewardsService, CurrencyRewardsService>();
                services.AddSingleton<ICurrencyCommandService, CurrencyCommandService>();
                services.AddSingleton<ILinkedAccountService, LinkedAccountService>();
                // Cross-platform linking (link codes, viewer status, multi-platform bonus, social goals)
                services.AddSingleton<ILinkCodeService, LinkCodeService>();
                services.AddSingleton<IViewerStatusService, ViewerStatusService>();
                services.AddSingleton<IMultiPlatformBonusService, MultiPlatformBonusService>();
                services.AddSingleton<ISocialGoalService, SocialGoalService>();
                services.AddSingleton<IViewerProfileService, ViewerProfileService>(); // admin viewer-profile aggregate (read-only)
                services.AddSingleton<ILoyaltyTierService, LoyaltyTierService>();
                services.AddSingleton<ICurrencyGameService, CurrencyGameService>();
                services.AddSingleton<ICurrencyBoostService, CurrencyBoostService>();
                services.AddSingleton<ICurrencyAuctionService, CurrencyAuctionService>();
                services.AddSingleton<ICurrencyBankService, CurrencyBankService>();
                services.AddHostedService<CurrencyDecayBackgroundService>();

                // Giveaway system
                services.AddSingleton<GiveawayService>();
                services.AddSingleton<GiveawayCommandService>();

                // Poll system
                services.AddSingleton<PollService>();
                services.AddSingleton<PollCommandService>();

                // Giveaway/Poll scheduler: auto-ends giveaways and polls at their ScheduledEndTime
                services.AddHostedService<GiveawayPollSchedulerService>();

                // Watchtime system
                services.AddSingleton<WatchtimeService>();
            services.AddSingleton<VyralBot.Core.Services.StreamStatus.IStreamLiveRegistry, VyralBot.Integrations.StreamStatus.StreamLiveRegistry>();
                services.AddSingleton<WatchtimeCommandService>();
                services.AddHostedService<WatchtimeBackgroundService>();

                // Variable system
                services.AddSingleton<IVariableReplacementService, VariableReplacementService>();

                // Command system
                services.AddSingleton<ICommandService, CommandService>();
                services.AddSingleton<CommandTimerService>(); // scheduled command firing (Commands page → Timers tab)
                services.AddSingleton<CommandActionRunner>(); // custom-command action-sequence executor (needs every platform sender)
                services.AddSingleton<UI.Services.PlatformConnectionService>(); // shared connect/disconnect/token-refresh for Settings + Chat Hub
                services.AddSingleton<UI.Services.WindowsStartupService>(); // HKCU Run-key toggle: "Launch VyralBot when my PC starts"
                services.AddSingleton<IStreamActionsService, StreamActionsService>(); // stream-management quick actions (Twitch Helix / Kick mod commands / YouTube liveBroadcasts)

                // Workflow automation engine (Automation page)
                var workflowNodeRegistry = new VyralBot.Workflows.Nodes.NodeRegistry();
                VyralBot.Workflows.Nodes.BuiltInNodes.RegisterAll(workflowNodeRegistry);
                services.AddSingleton(workflowNodeRegistry);
                services.AddSingleton<VyralBot.Workflows.Runtime.WorkflowRuntime>();

                // Workflow trigger wiring (BotGhostLikeIntegration phases 3/11/13/14/15/16/25):
                // sender routes workflow output per platform/bot, trigger service feeds
                // command/event/timer/manual triggers into the runtime above
                services.AddSingleton<UI.Services.WorkflowMessageSender>();

                // Workflow response waiters ("wait_for_response" node): fed by
                // ChatViewModel's chat pipeline, awaited by the engine node
                services.AddSingleton<UI.Services.WorkflowResponseWaiters>();
                services.AddSingleton<VyralBot.Workflows.Abstractions.IResponseWaiterRegistry>(sp =>
                    sp.GetRequiredService<UI.Services.WorkflowResponseWaiters>());

                // Workflow Discord interaction bridge: translates native
                // DiscordService slash/component gateway events into the
                // engine's WorkflowSlashInvocation/WorkflowComponentInteraction
                services.AddSingleton<UI.Services.WorkflowDiscordBridge>();
                services.AddSingleton<VyralBot.Workflows.Abstractions.IDiscordWorkflowBridge>(sp =>
                    sp.GetRequiredService<UI.Services.WorkflowDiscordBridge>());

                // Trigger service consumes the bridge + bot manager + the
                // effective mod-tier resolver via its optional trailing ctor
                // parameters (DI fills them when present)
                services.AddSingleton<UI.Services.WorkflowTriggerService>();

                // Custom "!mod <verb>" workflows: the Core !mod engine's
                // unknown-verb fallback runs them through the trigger service
                // (tier gate + awaited execution). The ModCommandEngine
                // singleton below receives this via its optional trailing
                // IModWorkflowInvoker ctor parameter.
                services.AddSingleton<VyralBot.Core.Services.Moderation.IModWorkflowInvoker, UI.Services.ModWorkflowInvoker>();

                // Visual flow editor (workflow canvas; pinned ctor signature:
                // IDataService, WorkflowTriggerService, WorkflowDiscordBridge,
                // MarketplaceService, ILogger<FlowEditorViewModel>)
                services.AddSingleton<UI.Services.MarketplaceService>();
                services.AddSingleton<ViewModels.MarketplaceViewModel>();
                services.AddSingleton<ViewModels.FlowEditorViewModel>();

                // Integration services
                services.AddSingleton<ITwitchService, TwitchService>();
                services.AddTransient<TwitchOAuthService>();

                // Twitch EventSub WebSocket integration
                services.AddTwitchLibEventSubWebsockets();
                services.AddSingleton<ITwitchEventSubService, TwitchEventSubService>();

                // Kick integration using KickLib (replaces WebView2-based KickService)
                services.AddKickLib(); // Register KickLib services (IKickApi, EventSubscriptions, etc.)
                services.AddSingleton<IKickService, KickLibChatService>();
                services.AddSingleton<KickLibOAuthService>();

                // YouTube integration using YouTube Data API v3
                services.AddSingleton<IYouTubeService, YouTubeChatService>();

                // Discord integration using Discord.Net
                services.AddSingleton<IDiscordService, DiscordService>();
                // Multi-bot Discord management
                services.AddSingleton<IDiscordBotManager, DiscordBotManager>();
                services.AddSingleton<IDiscordCommandHandler, DiscordModCommandHandler>();

                // Note: Old WebView2-based services removed - no longer needed with KickLib!

                // Webhook services (now shared with Kestrel)
                services.AddSingleton<WebServerDependencyTracker>();
                services.AddSingleton<WebServerService>();
                services.AddSingleton<NgrokService>();
                services.AddSingleton<KickWebhookSubscriptionService>();
                services.AddSingleton<WebhookCoordinatorService>();

                // Audio services
                // AudioWebSocketServer removed; now handled by Kestrel server
                services.AddSingleton<Core.Services.Audio.ISoundService, Core.Services.Audio.SoundService>();

                // Statistics service
                services.AddSingleton<Core.Services.Statistics.IStatisticsService, Core.Services.Statistics.StatisticsService>();

                // Alert service
                services.AddSingleton<UI.Services.IAlertService, UI.Services.AlertService>();

                // Profile settings management
                services.AddSingleton<ProfileSettingsLoader>();

                // Theme system (ThemeManager is a static singleton; registered for DI discoverability)
                services.AddSingleton<UiPreferencesService>();
                services.AddSingleton(_ => ThemeManager.Instance);

                // ViewModels - Use Singleton to prevent memory leaks from duplicate instances
                services.AddSingleton<DashboardViewModel>();
                services.AddSingleton<PlatformsViewModel>();
                services.AddSingleton<TwitchWebSetupViewModel>();
                services.AddSingleton<TwitchDashboardViewModel>();
                services.AddSingleton<TwitchSettingsViewModel>();
                services.AddSingleton<TwitchChatSettingsViewModel>();
                services.AddSingleton<KickChatSettingsViewModel>();
                services.AddSingleton<YoutubeChatSettingsViewModel>();
                services.AddSingleton<TiktokChatSettingsViewModel>();
                services.AddSingleton<DiscordChatSettingsViewModel>();
                services.AddSingleton<KickWebSetupViewModel>();
                services.AddSingleton<YouTubeWebSetupViewModel>();
                services.AddSingleton<TikTokWebSetupViewModel>();
                services.AddSingleton<DiscordWebSetupViewModel>();
                services.AddSingleton<KickDashboardViewModel>();
                services.AddSingleton<YouTubeDashboardViewModel>();
                services.AddSingleton<DiscordDashboardViewModel>();
                services.AddSingleton<DiscordBotsViewModel>();
                services.AddSingleton<DiscordBotDetailViewModel>();
                services.AddSingleton<KickSettingsViewModel>();
                services.AddSingleton<ChatViewModel>();
                services.AddSingleton<CurrencyViewModel>();
                services.AddSingleton<CommandsViewModel>();
                services.AddSingleton<VariablesViewModel>();
                services.AddSingleton<VariablePickerViewModel>();
                services.AddSingleton<SettingsViewModel>();
                services.AddSingleton<AudioSettingsViewModel>();
                services.AddSingleton<SoundsViewModel>();
                services.AddSingleton<StatisticsViewModel>();
                services.AddSingleton<EventHistoryViewModel>();
                services.AddSingleton<AlertSettingsViewModel>();
                services.AddSingleton<ConditionalRulesViewModel>();
                services.AddSingleton<GiveawayViewModel>();
                services.AddSingleton<PollViewModel>();
                services.AddSingleton<WatchtimeViewModel>();
                services.AddSingleton<EventTestingViewModel>();
                services.AddSingleton<ChatHubViewModel>();
                services.AddSingleton<ViewerHubViewModel>(); // Viewer Hub page (linked-platform status, goals, multi-platform bonus) — ctor fully DI-resolved
                services.AddTransient<ViewerProfileViewModel>(); // admin viewer-profile window (one instance per opened window)
                services.AddSingleton<ToolsViewModel>();
                services.AddSingleton<IntegrationsViewModel>();
                // OBS page: verbatim scene-collection store + its ViewModel.
                services.AddSingleton<VyralBot.Integrations.Obs.ObsSceneCollectionStore>();
                // Embedded OBS studio: install locator + libobs engine lifecycle.
                // Singletons — the engine owns the process-wide native state and
                // serializes every call through its gate.
                services.AddSingleton<VyralBot.Integrations.Obs.Engine.ObsInstallLocator>();
                services.AddSingleton<VyralBot.Integrations.Obs.Engine.ObsEngine>();
                // The loader depends on the gate abstraction; ObsEngine is the implementation.
                services.AddSingleton<VyralBot.Integrations.Obs.Engine.IObsNativeGate>(sp =>
                    sp.GetRequiredService<VyralBot.Integrations.Obs.Engine.ObsEngine>());
                // Task 5: the loader (engine-gated) and the studio facade over both.
                services.AddSingleton<VyralBot.Integrations.Obs.Engine.ObsSceneCollectionLoader>();
                services.AddSingleton<VyralBot.Integrations.Obs.Engine.ObsStudioService>();
                // Task 6: stream/record/replay outputs driven from the OBS profile.
                services.AddSingleton<VyralBot.Integrations.Obs.Engine.ObsOutputService>();
                // Task 7: embedded studio page view model (consumed by ObsViewModel's Studio tab).
                services.AddSingleton<ObsStudioViewModel>();
                // Full OBS tab: real obs64.exe window hosting + silent acquisition.
                // Singletons — the host owns the parking HWND and the adopted
                // process for the whole app lifetime; App.OnExit detaches the
                // window to the desktop.
                // Bundled Vyral starter collection: strictly additive one-time
                // import into the user's OBS scene collections (first run,
                // version updates, or the Settings → Advanced re-import).
                services.AddSingleton<VyralBot.Integrations.Obs.Hosting.ObsBundledCollectionService>();
                // GitHub-backed collection updates (fetch → non-destructive merge →
                // write back into the user's ACTIVE collection file).
                services.AddSingleton<VyralBot.Integrations.Obs.Hosting.ObsCollectionUpdateService>();
                services.AddSingleton<VyralBot.Integrations.Obs.Hosting.ObsWindowHostService>();
                services.AddSingleton<VyralBot.Integrations.Obs.Hosting.ObsAcquisitionService>();
                // Registered before ObsViewModel — the OBS page injects it as its default tab.
                services.AddSingleton<ObsHostViewModel>();
                services.AddSingleton<ObsViewModel>();
                // Restream page: FFmpeg-based multi-platform restreaming. The
                // engine is a singleton — a live session keeps running when the
                // page is closed; App.OnExit stops it before killing children.
                services.AddSingleton<VyralBot.Integrations.Restream.RestreamFfmpegLocator>();
                services.AddSingleton<VyralBot.Integrations.Restream.RestreamFfmpegAcquisitionService>();
                services.AddSingleton<VyralBot.Integrations.Restream.RestreamEngine>();
                services.AddSingleton<RestreamViewModel>();
                // StreamDeck page: button grid + LAN server for the VyralDeck
                // mobile companion app. The server/layout/pairing services are
                // singletons — the server keeps running when the page closes.
                services.AddSingleton<VyralBot.UI.Services.VyralDeck.ObsRemoteBridge>();
                services.AddSingleton<VyralBot.UI.Services.VyralDeck.DeckActionExecutor>();
                services.AddSingleton<VyralBot.UI.Services.VyralDeck.DeckLayoutStore>();
                services.AddSingleton<VyralBot.UI.Services.VyralDeck.DeckPairingStore>();
                services.AddSingleton<VyralBot.UI.Services.VyralDeck.DeckServer>();
                services.AddSingleton<StreamDeckViewModel>();
                services.AddSingleton<DocumentationViewModel>();
                services.AddSingleton<AutomationViewModel>(); // Automation page (structured workflow builder)
                // Moderation system (Moderator page + Settings → Moderator):
                // role assignments + the !mod command engine (built by the
                // moderation task in VyralBot.Core/Services/Moderation) and the
                // Moderator page ViewModel.
                // IModRoleService: factory (not type-only) because ModRoleService
                // takes the Core-side IModDiscordBot mirror, which the primary
                // Discord service satisfies through ModDiscordBotAdapter.
                services.AddSingleton<VyralBot.Core.Services.Moderation.IModRoleService>(sp =>
                    new VyralBot.Core.Services.Moderation.ModRoleService(
                        sp.GetRequiredService<IDataService>(),
                        sp.GetService<IDiscordService>() is { } primaryDiscord
                            ? new VyralBot.UI.Services.ModDiscordBotAdapter(primaryDiscord)
                            : null,
                        sp.GetRequiredService<ILogger<VyralBot.Core.Services.Moderation.ModRoleService>>()));
                // Effective tier resolver: the explicit assignment OR the highest
                // Discord-role→tier mapping, whichever ranks higher. Role lookups
                // run best-effort on the primary bot; without one the stored
                // assignment answers alone.
                services.AddSingleton<VyralBot.Core.Services.Moderation.IModTierResolver>(sp =>
                    new VyralBot.UI.Services.ModTierResolver(
                        sp.GetRequiredService<VyralBot.Core.Services.Moderation.IModRoleService>(),
                        sp.GetService<IDiscordService>() is { } tierDiscord
                            ? new VyralBot.UI.Services.ModDiscordBotAdapter(tierDiscord)
                            : null));
                // Connected-channel registry: gates tier-gated bot commands to
                // channels tagged Moderation/Admin (legacy: all channels while
                // nothing is configured — see ConnectedChannelService).
                services.AddSingleton<VyralBot.Core.Services.Channels.IConnectedChannelService, VyralBot.Core.Services.Channels.ConnectedChannelService>();
                // Stream-effect command router ("!flashlights", "!lightson",
                // "!lightsoff", "!streamdeck") — used by ChatViewModel.
                services.AddSingleton<VyralBot.Core.Services.StreamEffects.StreamEffectCommandRouter>();
                // Stream-effect execution: config KV facade, lights facade
                // (config re-read per call so Settings changes apply live),
                // Stream Deck service (actions persisted in the same config KV
                // via DelegateStreamDeckStore) and the EventBus subscriber.
                services.AddSingleton<StreamEffectsConfigService>();
                services.AddSingleton<VyralBot.Integrations.Lights.ILightsClient>(sp =>
                    new VyralBot.Integrations.Lights.LightsManager(async () =>
                    {
                        var s = await sp.GetRequiredService<StreamEffectsConfigService>().GetSnapshotAsync();
                        return new VyralBot.Integrations.Lights.LightsConfig(
                            s.Provider, s.BridgeIp, s.AppKey, s.LightIds, s.WebhookUrl, s.WebhookToken);
                    }));
                services.AddSingleton<VyralBot.Integrations.StreamDeck.IStreamDeckStore>(sp =>
                {
                    var cfg = sp.GetRequiredService<StreamEffectsConfigService>();
                    return new DelegateStreamDeckStore(
                        () => cfg.GetAsync(StreamEffectsConfigService.DeckActionsKey),
                        json => cfg.SetAsync(StreamEffectsConfigService.DeckActionsKey, json));
                });
                services.AddSingleton<VyralBot.Integrations.StreamDeck.IStreamDeckService, VyralBot.Integrations.StreamDeck.StreamDeckService>();
                services.AddSingleton<StreamEffectSubscriber>();
                // The !mod engine: register the concrete type too so the startup
                // wiring can set its seams on the SAME instance the interface
                // consumers resolve.
                services.AddSingleton<VyralBot.Core.Services.Moderation.ModCommandEngine>();
                services.AddSingleton<VyralBot.Core.Services.Moderation.IModCommandEngine>(sp =>
                    sp.GetRequiredService<VyralBot.Core.Services.Moderation.ModCommandEngine>());
                // Auto-moderation stack (Moderator page → Auto-Mod card + the
                // chat pipelines): the synchronous rule engine, the persisted
                // rule repository (Config key "Moderation.AutoModRules") and
                // the verdict executor. All three resolve to the process-wide
                // Shared instances so DI consumers, the Moderator dashboard
                // and the chat pipelines share ONE rule snapshot, ONE
                // escalation table and ONE adapter registry.
                services.AddSingleton<VyralBot.Core.Services.Moderation.IChatModerationEngine>(
                    _ => VyralBot.Core.Services.Moderation.ChatModerationEngine.Shared);
                services.AddSingleton<VyralBot.Core.Services.Moderation.ModerationRulesRepository>(sp =>
                    new VyralBot.Core.Services.Moderation.ModerationRulesRepository(
                        sp.GetRequiredService<IDataService>(),
                        sp.GetRequiredService<VyralBot.Core.Services.Moderation.IChatModerationEngine>(),
                        sp.GetRequiredService<ILogger<VyralBot.Core.Services.Moderation.ModerationRulesRepository>>()));
                services.AddSingleton<VyralBot.Core.Services.Moderation.ModerationActionService>(
                    _ => VyralBot.Core.Services.Moderation.ModerationActionService.Shared);
                services.AddSingleton<ModeratorViewModel>();
                services.AddSingleton<MainViewModel>();
                services.AddTransient<ProfileSelectorViewModel>();
                services.AddSingleton<WebServerSettingsViewModel>();
                services.AddSingleton<PerformanceViewModel>();
                services.AddSingleton<PlatformStartupSettingsViewModel>(); // Settings → Platforms startup cards (ctor-injected into the singleton SettingsViewModel; InitializeAsync re-runs per navigation)
                services.AddSingleton<DataManagementViewModel>();           // Settings → Data Management (ctor-injected into the singleton SettingsViewModel; InitializeAsync re-runs per navigation)

                // Views
                services.AddTransient<Views.TwitchDashboardView>();
                services.AddTransient<Views.TwitchSettingsView>();
                services.AddTransient<Views.KickDashboardView>();
                services.AddTransient<Views.KickWebSetupView>();
                services.AddTransient<Views.KickSettingsView>();
                services.AddTransient<Views.ChatView>();
                services.AddTransient<Views.CurrencyView>();
                services.AddTransient<Views.SettingsView>();
                services.AddTransient<Views.AudioSettingsView>();
                services.AddTransient<Views.SoundsView>();
                services.AddTransient<Views.StatisticsView>();
                services.AddTransient<Views.EventHistoryView>();
                services.AddTransient<Views.AlertSettingsView>();
                services.AddTransient<Views.ConditionalRulesView>();
                services.AddTransient<Views.EventTestingView>();
                // CommandsView, WebServerSettingsView - WPF DataTemplate creates them lazily from ViewModel

                services.AddTransient<ViewModels.GoogleDemoSetupViewModel>();
                services.AddTransient<Views.NgrokSetupWizardView>();
                services.AddTransient<ViewModels.NgrokSetupWizardViewModel>();

                // Windows
                services.AddSingleton<MainWindow>();
            })
            .Build();
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        try
        {
            // --obs-selftest: headless embedded-OBS engine boot validation (run
            // by the verification wave). Must happen BEFORE any host/window
            // work; runs synchronously and exits the process with 0 (ok),
            // 1 (OBS not found) or 2 (failure). A WinExe has no console, so the
            // durable output is the report file (--obs-selftest-report=<path>).
            if (e.Args.Any(a => a.Equals(Services.ObsSelftest.Flag, StringComparison.OrdinalIgnoreCase)))
            {
                Environment.Exit(Services.ObsSelftest.Run(e.Args));
                return; // Environment.Exit never returns; here for the compiler
            }

            // Kill leftover WebView2 processes from previous runs FIRST.
            // Scoped to processes whose command line references VyralBot's WebView2
            // temp user-data folder — other applications' WebView2 processes are untouched (KI-23).
            await Task.Run(KillLeftoverVyralWebView2Processes);

            // Clean up WebView2 temp cache from previous runs
            try
            {
                var tempFolder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "VyralBot_WebView2_Temp");
                if (System.IO.Directory.Exists(tempFolder))
                {
                    System.IO.Directory.Delete(tempFolder, recursive: true);
                    Log.Information("🧹 Cleared WebView2 temp cache from previous run");
                }
            }
            catch { /* Ignore if locked or doesn't exist */ }

            Log.Information("Starting application host (WPF + Kestrel on demand)...");
            await _host!.StartAsync();
            _hostStarted = true;
            Log.Information("✅ Application host started successfully (the Kestrel web server starts on demand via WebhookCoordinatorService)");

            // Set static ServiceProvider for DI access
            ServiceProvider = _host.Services;

            // Apply the saved theme before any window renders (app-level preference).
            try
            {
                ThemeManager.Instance.Initialize();
                Log.Information("Theme applied: {ThemeId}", ThemeManager.Instance.CurrentThemeId);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to apply saved theme - using native defaults");
            }

            // Workflow Discord bridge: attach any bot that is ALREADY running
            // at this point (the bridge itself auto-subscribes the manager's
            // future ServiceStarted/ServiceStopped events, and its constructor
            // attaches the setup wizard's singleton bot). Best-effort — a
            // failure here must not break startup.
            try
            {
                var bridge = _host.Services.GetRequiredService<UI.Services.WorkflowDiscordBridge>();
                var discordManager = _host.Services.GetRequiredService<IDiscordBotManager>();
                var attached = new HashSet<string>(StringComparer.Ordinal);
                foreach (var service in discordManager.Connected)
                {
                    if (service is DiscordService runningBot && attached.Add(runningBot.BotId ?? string.Empty))
                    {
                        bridge.Attach(runningBot);
                    }
                }

                Log.Information("Workflow Discord bridge attached to {Count} already-running bot(s)", attached.Count);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to attach the workflow Discord bridge to running bots");
            }

            // Moderator: wire the mod command engine to the mod channel, the
            // multi-bot manager, the Discord chat event bus and the underlying
            // moderation command handler. ModDiscordBotAdapter bridges the
            // Core-side IModDiscordBot mirror to IDiscordService (Core cannot
            // reference VyralBot.Integrations). The channel provider resolves
            // Config "Mod.ChannelId" (saved by the Moderator page / Settings →
            // Moderator) first and falls back to the primary running bot's
            // DiscordBots row. Best-effort — a failure here must not break
            // startup.
            try
            {
                var modEngine = _host.Services.GetRequiredService<VyralBot.Core.Services.Moderation.ModCommandEngine>();
                var modData = _host.Services.GetRequiredService<IDataService>();
                var modBotManager = _host.Services.GetRequiredService<IDiscordBotManager>();
                var modEventBus = _host.Services.GetRequiredService<IEventBus>();
                var modChannels = _host.Services.GetRequiredService<VyralBot.Core.Services.Channels.IConnectedChannelService>();
                var modHandler = new DiscordModCommandHandler(); // stateless, no DI needed

                modEngine.ModChannelIdProvider = () =>
                {
                    var configured = modData.GetConfigValueAsync("Mod.ChannelId").GetAwaiter().GetResult();
                    if (!string.IsNullOrWhiteSpace(configured))
                    {
                        return configured;
                    }

                    var primaryBotId = modBotManager.Primary?.BotId;
                    var botRows = modData.GetDiscordBotsAsync().GetAwaiter().GetResult();
                    return !string.IsNullOrWhiteSpace(primaryBotId)
                        ? botRows.FirstOrDefault(b => string.Equals(b.Id, primaryBotId, StringComparison.Ordinal))?.ModChannelId
                        : botRows.FirstOrDefault(b => !string.IsNullOrWhiteSpace(b.ModChannelId))?.ModChannelId;
                };

                modEngine.BotResolver = botId =>
                {
                    var svc = modBotManager.Get(botId);
                    return svc == null ? null : new VyralBot.UI.Services.ModDiscordBotAdapter(svc);
                };

                // Feed the engine every Discord chat message posted in the
                // configured mod channel or in any channel that accepts
                // tier-gated commands, projected onto the Core-side
                // ModChannelMessage record (the event type is not nameable from
                // Core; GuildId feeds the Discord-role→tier resolution). The
                // returned Guid lets StopAsync unsubscribe via the engine's own
                // IEventBus.
                modEngine.DiscordChatSubscriber = handler =>
                    modEventBus.SubscribeAsync<VyralBot.Integrations.Discord.Events.DiscordChatMessageEvent>(async e =>
                    {
                        var modChannelId = modEngine.ModChannelIdProvider?.Invoke();
                        var isModChannel = !string.IsNullOrWhiteSpace(e.ChannelId) &&
                            string.Equals(e.ChannelId.Trim(), modChannelId?.Trim(), StringComparison.OrdinalIgnoreCase);
                        if (!isModChannel &&
                            !await modChannels.AcceptsCommandsAsync(e.BotId ?? "", e.ChannelId ?? "").ConfigureAwait(false))
                        {
                            return;
                        }

                        await handler(new VyralBot.Core.Services.Moderation.ModChannelMessage(
                            e.ChannelId, e.SenderUserId, e.Username, e.Message, e.BotId)
                        {
                            GuildId = e.GuildId
                        }).ConfigureAwait(false);
                    });

                // Delegated moderation ("!kick @user" …) runs through the same
                // command handler the bot's own chat uses, as a moderator.
                modEngine.ModerationInvoker = async (botId, commandLine, channelId) =>
                {
                    var bot = modBotManager.Get(botId);
                    if (bot == null)
                    {
                        return null;
                    }

                    var result = await modHandler.HandleAsync(
                        bot, commandLine, callerIsModerator: true, callerIsOwner: false, channelId).ConfigureAwait(false);
                    return result.Handled ? result.Response : null;
                };

                await modEngine.StartAsync();

                Log.Information("Mod command engine wired (channel provider + bot resolver + chat subscriber + moderation invoker)");
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to wire the mod command engine");
            }

            // Stream effects: start the EventBus subscriber that executes
            // StreamEffectEvents (light flashes/scenes, Stream Deck actions)
            // on the configured providers. Best-effort — a failure here must
            // not break startup.
            try
            {
                var streamEffects = _host.Services.GetRequiredService<StreamEffectSubscriber>();
                await streamEffects.StartAsync();
                Log.Information("Stream effect subscriber started");
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to start the stream effect subscriber");
            }

            // Initialize database schema
            var dataService = _host.Services.GetRequiredService<IDataService>();
            await dataService.EnsureDatabaseCreatedAsync();

            // Auto-moderation: configure the shared action service with the
            // profile's persistence + logging, register the per-platform
            // adapters (capability flags follow ModerationPlatformParity —
            // Twitch/Kick/YouTube are notice-only until their services gain
            // per-viewer action APIs; Discord enforces ban/timeout by name)
            // and load the saved rule set so the engine evaluates with the
            // stored rules from the first chat message after startup.
            // Best-effort — a failure must not break startup.
            try
            {
                var autoModActions = _host.Services.GetRequiredService<VyralBot.Core.Services.Moderation.ModerationActionService>();
                autoModActions.Configure(
                    dataService,
                    _host.Services.GetRequiredService<ILogger<VyralBot.Core.Services.Moderation.ModerationActionService>>());
                autoModActions.RegisterAdapter(new TwitchModerationAdapter(
                    _host.Services.GetRequiredService<ITwitchService>()));
                autoModActions.RegisterAdapter(new DiscordModerationAdapter(
                    _host.Services.GetRequiredService<IDiscordService>()));
                autoModActions.RegisterAdapter(new KickModerationAdapter(
                    _host.Services.GetRequiredService<IKickService>()));
                autoModActions.RegisterAdapter(new YouTubeModerationAdapter(
                    _host.Services.GetRequiredService<IYouTubeService>()));

                var autoModRules = await _host.Services
                    .GetRequiredService<VyralBot.Core.Services.Moderation.ModerationRulesRepository>()
                    .LoadAsync();
                var enabledAutoModRules = new[]
                {
                    autoModRules.BannedWords, autoModRules.Links, autoModRules.Caps,
                    autoModRules.Emojis, autoModRules.RepeatedMessage, autoModRules.AsciiArt
                }.Count(r => r.Enabled);

                Log.Information("Auto-mod engine active: {AdapterCount} platform adapter(s) [{Platforms}], master switch {Master}, {EnabledRules}/6 rules enabled",
                    autoModActions.GetAdapters().Count,
                    string.Join(", ", autoModActions.GetAdapters().Select(a => a.Platform)),
                    autoModRules.MasterEnabled ? "ON" : "OFF",
                    enabledAutoModRules);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to activate the auto-moderation engine");
            }

            // Social goals: seed the default goals once (no-op when any already exist).
            // Fire-and-forget — a failure must never block or break startup.
            _ = Task.Run(async () =>
            {
                try
                {
                    var socialGoals = _host.Services.GetRequiredService<ISocialGoalService>();
                    await socialGoals.SeedDefaultsAsync();
                    Log.Information("Default social goals ensured");
                }
                catch (Exception ex)
                {
                    _host.Services.GetRequiredService<ILogger<App>>().LogError(ex, "Failed to seed default social goals");
                }

            // UNIFIED BALANCES: consolidate points for viewers that were
            // linked in previous sessions (merge-on-link only covers new
            // links; this pass heals everyone already linked).
            _ = Task.Run(async () =>
            {
                try
                {
                    using var scope = _host.Services.CreateScope();
                    var currency = scope.ServiceProvider.GetRequiredService<VyralBot.Core.Services.Currency.ICurrencyService>();
                    var data = scope.ServiceProvider.GetRequiredService<VyralBot.Data.IDataService>();
                    var accounts = await data.GetAllLinkedAccountsAsync();
                    foreach (var group in accounts.GroupBy(a => a.ViewerId).Where(g => g.Count() >= 2))
                    {
                        await currency.ConsolidateViewerBalancesAsync(group.Key);
                    }
                }
                catch (Exception ex)
                {
                    _host.Services.GetRequiredService<ILogger<App>>().LogError(ex, "Startup unified-balance consolidation failed");
                }
            });
            });

            // Discord bots: auto-start them as EARLY as possible. The previous
            // trigger (CheckPlatformConnectionsAsync) only runs after the full
            // settings/platform load chain. Fire-and-forget: resolve the DI
            // singleton MainViewModel and call its public early-start hook.
            // The one-shot guard inside AutoStartDiscordBotsAsync keeps the
            // later CheckPlatformConnectionsAsync trigger as a harmless no-op
            // (guard-reuse — no duplicated connection logic). A failure here
            // must never block or break startup.
            _ = Task.Run(async () =>
            {
                try
                {
                    var mainViewModel = _host.Services.GetRequiredService<MainViewModel>();
                    await mainViewModel.StartDiscordBotsEarlyAsync();
                    Log.Information("Early Discord bot auto-start finished");
                }
                catch (Exception ex)
                {
                    _host.Services.GetRequiredService<ILogger<App>>().LogError(ex, "Early Discord bot auto-start failed");
                }
            });

            // Link codes: one-time purge of link codes expired for more than 24h.
            // Fire-and-forget — a failure must never block or break startup.
            _ = Task.Run(async () =>
            {
                try
                {
                    var linkCodes = _host.Services.GetRequiredService<ILinkCodeService>();
                    await linkCodes.CleanupExpiredAsync();
                    Log.Information("Expired link codes cleaned up");
                }
                catch (Exception ex)
                {
                    _host.Services.GetRequiredService<ILogger<App>>().LogError(ex, "Failed to clean up expired link codes");
                }
            });

            // Start trigger engine
            var triggerEngine = _host.Services.GetRequiredService<ITriggerEngine>();
            await triggerEngine.StartAsync();

            // NOTE: Webhook coordinator is initialized AFTER profile loads in MainViewModel.OnProfileLoadedAsync

            Log.Information("🎯 CRITICAL: EventBus is now SHARED between WPF UI and Kestrel webhooks!");
            Log.Information("   Webhook events published in WebhooksController will reach ChatViewModel");

            // Show main window FIRST
            Log.Information("Resolving MainWindow...");
            var mainWindow = _host.Services.GetRequiredService<MainWindow>();
            Log.Information("MainWindow resolved: {IsNull}", mainWindow == null);

            // Set as the application's main window
            MainWindow = mainWindow;

            Log.Information("Showing MainWindow...");
            mainWindow!.Show();
            mainWindow.Activate();
            Log.Information("MainWindow displayed");

            // First-launch dependency check: the exe is self-contained, so the
            // only machine dependency is the WebView2 runtime. Fire-and-forget
            // behind the shown window — a missing runtime prompts (and can
            // silently install) without blocking startup.
            _ = Task.Run(async () =>
            {
                try
                {
                    await UI.Services.RuntimeDependencyCheck.RunStartupCheckAsync(
                        _host.Services.GetRequiredService<ILogger<App>>());
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Runtime dependency check failed");
                }
            });

            // Get MainViewModel to control the profile selector overlay
            var mainViewModel = _host.Services.GetRequiredService<MainViewModel>();

            // Now check if profile selector should be shown as overlay
            var profileService = _host.Services.GetRequiredService<IProfileService>();
            var initialProfileName = profileService.GetCurrentProfileName();
            var profiles = await profileService.GetProfilesAsync();

            // First run - no profiles exist, force create new profile
            if (profiles.Count() == 0 || string.IsNullOrEmpty(initialProfileName))
            {
                Log.Information("First run detected - no profiles exist, showing profile creation");
                await mainViewModel.ShowProfileSelectorAsync();
            }
            // Validate current profile exists in profiles list
            else if (!profiles.Any(p => p.Name == initialProfileName))
            {
                Log.Warning("Current profile '{ProfileName}' from file does not exist, showing profile selector", initialProfileName);
                await mainViewModel.ShowProfileSelectorAsync();
            }
            else if (profiles.Count() > 1)
            {
                // Check if current profile has auto-login enabled
                var currentProfile = await profileService.GetCurrentProfileAsync();
                var hasAutoLogin = currentProfile?.Settings.TryGetValue("AutoLogin", out var autoLoginValue) == true
                    && bool.TryParse(autoLoginValue, out var enabled) && enabled;

                if (hasAutoLogin)
                {
                    Log.Information("Auto-login enabled for current profile, skipping profile selector");
                    // Initialize profile data for auto-login
                    await InitializeProfileDataAsync(mainViewModel);
                }
                else
                {
                    Log.Information("Multiple profiles detected, showing profile selector overlay");
                    await mainViewModel.ShowProfileSelectorAsync();
                }
            }
            else
            {
                Log.Information("Single profile detected, skipping profile selector");
                // Initialize profile data for single profile auto-load
                await InitializeProfileDataAsync(mainViewModel);
            }

            base.OnStartup(e);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Failed to start application");
            MessageBox.Show($"Failed to start: {ex.Message}\n\n{ex.StackTrace}", "Vyral Error", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    /// <summary>
    /// Kills leftover WebView2 processes that belong to VyralBot's temp user-data folder.
    /// Targeted via the process command line so WebView2 processes owned by other
    /// applications on the machine are never touched (KI-23).
    /// </summary>
    private static void KillLeftoverVyralWebView2Processes()
    {
        try
        {
            const string script =
                "Get-CimInstance Win32_Process -Filter \\\"Name='msedgewebview2.exe'\\\" | " +
                "Where-Object { $_.CommandLine -like '*VyralBot_WebView2_Temp*' } | " +
                "ForEach-Object { Stop-Process -Id $_.ProcessId -Force }";

            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "powershell",
                Arguments = $"-NoProfile -Command \"{script}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var process = System.Diagnostics.Process.Start(startInfo);
            if (process == null)
            {
                Log.Warning("Failed to start PowerShell for targeted WebView2 cleanup");
                return;
            }

            if (!process.WaitForExit(5_000))
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                Log.Warning("Targeted WebView2 cleanup timed out; PowerShell process force-killed");
            }
            else
            {
                Log.Information("Targeted WebView2 cleanup completed (VyralBot-owned processes only)");
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Targeted WebView2 cleanup failed");
        }
    }

    /// <summary>
    /// Awaits MainViewModel.OnProfileLoadedAsync with guarded error reporting. The first
    /// steps of profile initialization are unguarded, so a failure there must not
    /// silently kill settings load + platform auto-connect without a trace.
    /// </summary>
    private async Task InitializeProfileDataAsync(MainViewModel mainViewModel)
    {
        try
        {
            await mainViewModel.OnProfileLoadedAsync();
        }
        catch (Exception ex)
        {
            _host!.Services.GetRequiredService<ILogger<App>>().LogError(ex, "Error initializing profile data after startup");
            MessageBox.Show(
                $"Failed to initialize profile data: {ex.Message}\n\nSettings and platform auto-connect may not be loaded. See logs/vyralbot-*.txt for details.",
                "Vyral", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Start a watchdog FIRST — if any cleanup deadlocks, this guarantees
        // the process dies. Must be a foreground thread so it survives even
        // if the CLR tries to exit.
        var watchdog = new System.Threading.Thread(() =>
        {
            System.Threading.Thread.Sleep(10_000);
            Log.Warning("Shutdown watchdog fired — force-killing process");
            try { Log.CloseAndFlush(); } catch { }
            System.Diagnostics.Process.GetCurrentProcess().Kill();
        })
        { IsBackground = false, Name = "ShutdownWatchdog" };
        watchdog.Start();

        // Disconnect every Discord bot FIRST (graceful gateway logout) so bots go
        // offline the moment the app closes and reconnect cleanly on next launch.
        try
        {
            var manager = ServiceProvider?.GetService(typeof(VyralBot.Integrations.Discord.IDiscordBotManager))
                as VyralBot.Integrations.Discord.IDiscordBotManager;
            if (manager != null)
            {
                var stopAll = manager.StopAllAsync();
                if (!stopAll.Wait(TimeSpan.FromSeconds(6)))
                {
                    Log.Warning("Discord bot shutdown timed out; the watchdog will force-kill");
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Error stopping Discord bots on exit");
        }

        try
        {
            var processId = System.Diagnostics.Process.GetCurrentProcess().Id;
            Log.Information("Application exiting (PID: {ProcessId}) - cleanup starting...", processId);

            // Release mutex first
            _instanceMutex?.ReleaseMutex();
            _instanceMutex?.Dispose();

            // Stop the host properly (this stops all BackgroundServices like PerformanceMonitor)
            if (_hostStarted)
            {
                Log.Information("Stopping application host and all background services...");
                try
                {
                    _host!.StopAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
                    Log.Information("Host stopped successfully");
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error stopping host");
                }
            }
            else
            {
                Log.Warning("Host never fully started — skipping host teardown");
            }

            if (!_hostStarted)
            {
                // Startup failed before any service was constructed — resolving
                // them now would re-run the failing constructors (and crash
                // exit the way a failed ProfileService did). Log and leave.
                Log.Warning("Skipping service cleanup — the host did not start");
                try { base.OnExit(e); } catch { }
                Log.CloseAndFlush();
                return;
            }

            // Kill all tracked processes first to ensure zero orphans
            var processManager = _host!.Services.GetService<IProcessManager>();
            if (processManager != null)
            {
                try
                {
                    processManager.KillAllProcessesAsync().GetAwaiter().GetResult();
                    Log.Information("All tracked processes terminated");
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error killing tracked processes");
                }
            }

            // Quick synchronous cleanup - don't wait for async operations
            var chatViewModel = _host.Services.GetService<ChatViewModel>();
            if (chatViewModel != null)
            {
                try
                {
                    chatViewModel.Cleanup();
                    Log.Information("ChatViewModel cleaned up");
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error cleaning up ChatViewModel");
                }
            }

            // Stop webhook infrastructure (embedded Kestrel + ngrok) BEFORE host dispose
            // so the child WebApplication shuts down while DI services are still alive.
            var webhookCoordinator = _host.Services.GetService<VyralBot.Api.Services.WebhookCoordinatorService>();
            if (webhookCoordinator != null)
            {
                try
                {
                    webhookCoordinator.StopAsync().GetAwaiter().GetResult();
                    Log.Information("Webhook infrastructure stopped");
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error stopping webhook infrastructure");
                }
            }

            // Stop the stream-effect subscriber (unsubscribes from the shared
            // EventBus before the host disposes it).
            var streamEffectSubscriber = _host.Services.GetService<StreamEffectSubscriber>();
            if (streamEffectSubscriber != null)
            {
                try
                {
                    streamEffectSubscriber.StopAsync().GetAwaiter().GetResult();
                    Log.Information("Stream effect subscriber stopped");
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Error stopping the stream effect subscriber");
                }
            }

            // Full OBS tab: detach the hosted obs64 window back to the desktop
            // BEFORE the engine sequence — OBS KEEPS RUNNING as a normal window
            // after VyralBot exits (Dispose never kills it).
            var obsWindowHost = _host.Services.GetService<VyralBot.Integrations.Obs.Hosting.ObsWindowHostService>();
            if (obsWindowHost != null)
            {
                try
                {
                    obsWindowHost.MinimizeDetached();
                    Log.Information("OBS host window minimized (ownership cleared) on exit");
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Error detaching the OBS host window on exit");
                }
            }

            // Embedded-OBS exit sequence (BEFORE the host disposes its
            // services): 1) stop every output gracefully, 2) clear the output
            // channels (no obs_shutdown yet), 3) call obs_shutdown ONLY when
            // no native state was loaded — shutdown on a loaded instance is
            // known to access-violate, so a loaded engine is left for process
            // teardown to reclaim.
            var obsEngine = _host.Services.GetService<VyralBot.Integrations.Obs.Engine.ObsEngine>();
            if (obsEngine != null)
            {
                var obsOutputs = _host.Services.GetService<VyralBot.Integrations.Obs.Engine.ObsOutputService>();
                if (obsOutputs != null)
                {
                    try
                    {
                        obsOutputs.StopAllAsync().GetAwaiter().GetResult();
                        Log.Information("OBS outputs stopped for exit");
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "Error stopping the OBS outputs on exit");
                    }
                }

                try
                {
                    obsEngine.PrepareExitAsync().GetAwaiter().GetResult();
                    Log.Information("OBS engine prepared for exit");
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Error preparing the OBS engine for exit");
                }

                try
                {
                    if (!obsEngine.HasLoadedNativeState)
                    {
                        obsEngine.ShutdownAsync().GetAwaiter().GetResult();
                        Log.Information("OBS engine shut down (no native state was loaded)");
                    }
                    else
                    {
                        Log.Information("OBS engine: native state was loaded; letting process teardown reclaim to avoid the known obs_shutdown AV");
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Error shutting down the OBS engine");
                }
            }

            // Restream: stop any live session (kills the FFmpeg process tree).
            // ProcessManager's kill-all below is the orphan backstop.
            var restreamEngine = _host.Services.GetService<VyralBot.Integrations.Restream.RestreamEngine>();
            if (restreamEngine != null)
            {
                try
                {
                    restreamEngine.StopAsync().GetAwaiter().GetResult();
                    Log.Information("Restream engine stopped for exit");
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Error stopping the restream engine on exit");
                }
            }

            var twitchService = _host.Services.GetService<ITwitchService>();
            if (twitchService is IDisposable disposableTwitch)
            {
                try { disposableTwitch.Dispose(); } catch { }
            }

            var kickService = _host.Services.GetService<IKickService>();
            if (kickService is IDisposable disposableKick)
            {
                try { disposableKick.Dispose(); } catch { }
            }

            // Dispose host (this will dispose all services)
            Log.Information("Disposing host and all services...");
            try
            {
                _host.Dispose();
                Log.Information("Host disposed successfully");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error disposing host");
            }

            // Force cleanup of VyralBot's own WebView2 runtime processes (135MB memory usage).
            // Scoped to processes whose command line references VyralBot's WebView2 temp
            // user-data folder — other applications' WebView2 processes are untouched (KI-23).
            try
            {
                Log.Information("Killing VyralBot WebView2 runtime processes to free memory...");
                KillLeftoverVyralWebView2Processes();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to kill WebView2 cache processes");
            }

            // Force garbage collection to release memory immediately
            Log.Information("Forcing garbage collection...");
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
            Log.Information("Garbage collection complete");

            Log.Information("Application cleanup complete (PID: {ProcessId}), calling base.OnExit...", processId);

            // Call base.OnExit BEFORE log cleanup to avoid threading issues with WPF's WeakEventManager
            try
            {
                base.OnExit(e);
            }
            catch (Exception baseExitEx)
            {
                // WeakEventTable.OnShutDown can throw cross-thread exceptions during
                // cleanup of Loaded/Unloaded event handlers - safe to ignore at exit
                Log.Warning(baseExitEx, "Suppressed exception during base.OnExit (WeakEventTable cleanup)");
            }

            Log.CloseAndFlush();

            // Give the OS a moment to clean up
            System.Threading.Thread.Sleep(100);

            // LAST step of all: if the user asked for a restart, launch a detached
            // helper that waits ~3s (old process gone, mutex released, ports and
            // file locks settled) and only then starts the new instance.
            if (PendingRestart)
            {
                try
                {
                    var exePath = Environment.ProcessPath;
                    if (!string.IsNullOrWhiteSpace(exePath) && System.IO.File.Exists(exePath))
                    {
                        Log.Information("Restart requested — launching the new instance in 3 seconds");
                        var psi = new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c timeout /t 3 /nobreak >nul & start \"\" \"{exePath}\"")
                        {
                            CreateNoWindow = true,
                            UseShellExecute = false,
                            WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
                        };
                        System.Diagnostics.Process.Start(psi);
                    }
                    else
                    {
                        Log.Error("Restart requested but the executable path is unavailable");
                    }
                }
                catch (Exception restartEx)
                {
                    Log.Error(restartEx, "Failed to schedule the application restart");
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error during application exit");
            try { base.OnExit(e); } catch { } // Call base even on error, suppress WeakEventTable exceptions
        }
        finally
        {
            // Watchdog is already running (started at top of OnExit).
            // Nothing else to do here — if cleanup completed, the process
            // exits naturally; if it didn't, the watchdog kills it.
        }
    }

    // ======================================================================
    // Auto-moderation platform adapters (IModerationPlatformAdapter). They
    // live in the composition root because VyralBot.Core cannot reference the
    // integration services (reference direction is Integrations → Core).
    // Capability flags follow the parity audit (ModerationPlatformParity):
    // Twitch/Kick/YouTube expose no per-viewer timeout/ban/delete APIs yet,
    // so their adapters only post warning notices and verdicts degrade
    // delete→warn/timeout→warn per ModerationActionService's ladder; Discord
    // can ban and timeout by name (no delete-by-id API — "trigger"-scope
    // deletes track workflow triggers, not viewer messages). Every method is
    // best-effort and NEVER throws.
    // ======================================================================

    /// <summary>Twitch adapter: notice-only (chat-wide APIs exist; per-viewer Helix actions are not wired yet).</summary>
    internal sealed class TwitchModerationAdapter : VyralBot.Core.Services.Moderation.IModerationPlatformAdapter
    {
        private readonly ITwitchService _twitch;

        public TwitchModerationAdapter(ITwitchService twitch)
            => _twitch = twitch ?? throw new ArgumentNullException(nameof(twitch));

        public string Platform => "Twitch";
        public bool CanDeleteMessage => false;
        public bool CanTimeout => false;
        public bool CanBan => false;

        public Task<bool> DeleteMessageAsync(VyralBot.Core.Services.Moderation.ModerationIncomingMessage message) => Task.FromResult(false);

        public Task<bool> TimeoutAsync(VyralBot.Core.Services.Moderation.ModerationIncomingMessage message, int seconds, string reason) => Task.FromResult(false);

        public Task<bool> BanAsync(VyralBot.Core.Services.Moderation.ModerationIncomingMessage message, string reason) => Task.FromResult(false);

        public async Task<bool> SendNoticeAsync(string message)
        {
            if (!_twitch.IsConnected)
            {
                return false;
            }

            try
            {
                await _twitch.SendMessageAsync(message).ConfigureAwait(false);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>Kick adapter: notice-only (IKickService is connect/send only — outbound moderation is webhook-inbound).</summary>
    internal sealed class KickModerationAdapter : VyralBot.Core.Services.Moderation.IModerationPlatformAdapter
    {
        private readonly IKickService _kick;

        public KickModerationAdapter(IKickService kick)
            => _kick = kick ?? throw new ArgumentNullException(nameof(kick));

        public string Platform => "Kick";
        public bool CanDeleteMessage => false;
        public bool CanTimeout => false;
        public bool CanBan => false;

        public Task<bool> DeleteMessageAsync(VyralBot.Core.Services.Moderation.ModerationIncomingMessage message) => Task.FromResult(false);

        public Task<bool> TimeoutAsync(VyralBot.Core.Services.Moderation.ModerationIncomingMessage message, int seconds, string reason) => Task.FromResult(false);

        public Task<bool> BanAsync(VyralBot.Core.Services.Moderation.ModerationIncomingMessage message, string reason) => Task.FromResult(false);

        public async Task<bool> SendNoticeAsync(string message)
        {
            if (!_kick.IsConnected)
            {
                return false;
            }

            try
            {
                await _kick.SendMessageAsync(message).ConfigureAwait(false);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>YouTube adapter: notice-only (IYouTubeService is connect/send + stream info only).</summary>
    internal sealed class YouTubeModerationAdapter : VyralBot.Core.Services.Moderation.IModerationPlatformAdapter
    {
        private readonly IYouTubeService _youtube;

        public YouTubeModerationAdapter(IYouTubeService youtube)
            => _youtube = youtube ?? throw new ArgumentNullException(nameof(youtube));

        public string Platform => "YouTube";
        public bool CanDeleteMessage => false;
        public bool CanTimeout => false;
        public bool CanBan => false;

        public Task<bool> DeleteMessageAsync(VyralBot.Core.Services.Moderation.ModerationIncomingMessage message) => Task.FromResult(false);

        public Task<bool> TimeoutAsync(VyralBot.Core.Services.Moderation.ModerationIncomingMessage message, int seconds, string reason) => Task.FromResult(false);

        public Task<bool> BanAsync(VyralBot.Core.Services.Moderation.ModerationIncomingMessage message, string reason) => Task.FromResult(false);

        public async Task<bool> SendNoticeAsync(string message)
        {
            if (!_youtube.IsConnected)
            {
                return false;
            }

            try
            {
                await _youtube.SendMessageAsync(message).ConfigureAwait(false);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>
    /// Discord adapter: the only platform able to enforce actions today —
    /// ban + timeout by username on the primary bot. Message deletion is not
    /// advertised (no delete-by-id/by-author API), so Delete verdicts degrade
    /// to a warning notice in the selected channel.
    /// </summary>
    internal sealed class DiscordModerationAdapter : VyralBot.Core.Services.Moderation.IModerationPlatformAdapter
    {
        private readonly IDiscordService _discord;

        public DiscordModerationAdapter(IDiscordService discord)
            => _discord = discord ?? throw new ArgumentNullException(nameof(discord));

        public string Platform => "Discord";
        public bool CanDeleteMessage => false;
        public bool CanTimeout => true;
        public bool CanBan => true;

        public Task<bool> DeleteMessageAsync(VyralBot.Core.Services.Moderation.ModerationIncomingMessage message) => Task.FromResult(false);

        public async Task<bool> TimeoutAsync(VyralBot.Core.Services.Moderation.ModerationIncomingMessage message, int seconds, string reason)
        {
            try
            {
                // Discord timeouts are minute-granular; round up, minimum 1.
                var minutes = Math.Max(1, (int)Math.Ceiling(seconds / 60.0));
                return await _discord.TimeoutMemberByNameAsync(message.Username, minutes, reason).ConfigureAwait(false);
            }
            catch
            {
                return false;
            }
        }

        public async Task<bool> BanAsync(VyralBot.Core.Services.Moderation.ModerationIncomingMessage message, string reason)
        {
            try
            {
                return await _discord.BanMemberByNameAsync(message.Username, reason).ConfigureAwait(false);
            }
            catch
            {
                return false;
            }
        }

        public async Task<bool> SendNoticeAsync(string message)
        {
            try
            {
                return await _discord.SendMessageAsync(message).ConfigureAwait(false);
            }
            catch
            {
                return false;
            }
        }
    }
}

