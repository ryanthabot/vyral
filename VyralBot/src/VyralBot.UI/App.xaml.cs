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

                // Currency system
                services.AddSingleton<ICurrencyService, CurrencyService>();
                services.AddSingleton<ICurrencyRewardsService, CurrencyRewardsService>();
                services.AddSingleton<ICurrencyCommandService, CurrencyCommandService>();
                services.AddSingleton<ILinkedAccountService, LinkedAccountService>();
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

                // Watchtime system
                services.AddSingleton<WatchtimeService>();
                services.AddSingleton<WatchtimeCommandService>();
                services.AddHostedService<WatchtimeBackgroundService>();

                // Variable system
                services.AddSingleton<IVariableReplacementService, VariableReplacementService>();

                // Command system
                services.AddSingleton<ICommandService, CommandService>();
                services.AddSingleton<CommandTimerService>(); // scheduled command firing (Commands page → Timers tab)
                services.AddSingleton<CommandActionRunner>(); // custom-command action-sequence executor (needs every platform sender)

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

                // Trigger service consumes the bridge + bot manager via its
                // optional trailing ctor parameters (DI fills them when present)
                services.AddSingleton<UI.Services.WorkflowTriggerService>();

                // Visual flow editor (workflow canvas; pinned ctor signature:
                // IDataService, WorkflowTriggerService, WorkflowDiscordBridge,
                // ILogger<FlowEditorViewModel>)
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
                services.AddSingleton<ToolsViewModel>();
                services.AddSingleton<IntegrationsViewModel>();
                services.AddSingleton<DocumentationViewModel>();
                services.AddSingleton<AutomationViewModel>(); // Automation page (structured workflow builder)
                services.AddSingleton<MainViewModel>();
                services.AddTransient<ProfileSelectorViewModel>();
                services.AddSingleton<WebServerSettingsViewModel>();

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

            Log.Information("Starting application host (WPF + Kestrel)...");
            await _host!.StartAsync();
            Log.Information("✅ Application host started successfully - Kestrel is now listening on http://localhost:5000");

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

            // Initialize database schema
            var dataService = _host.Services.GetRequiredService<IDataService>();
            await dataService.EnsureDatabaseCreatedAsync();

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
                    _ = mainViewModel.OnProfileLoadedAsync();
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
                _ = mainViewModel.OnProfileLoadedAsync();
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

    private async Task AutoConnectTwitchAsync()
    {
        try
        {
            Log.Information("Checking for Twitch auto-connect...");

            using var scope = _host!.Services.CreateScope();
            var dataService = scope.ServiceProvider.GetRequiredService<IDataService>();

            // Ensure database exists first
            await dataService.EnsureDatabaseCreatedAsync();

            var platform = await dataService.GetPlatformByNameAsync("Twitch");

            if (platform != null && platform.Status == "Connected" && !string.IsNullOrEmpty(platform.AccessToken))
            {
                Log.Information("Found connected Twitch platform for {Username}, connecting to IRC...", platform.Username);

                var twitchService = _host.Services.GetRequiredService<ITwitchService>();
                var accessToken = TwitchOAuthService.Decrypt(platform.AccessToken);
                var botUsername = platform.Username?.ToLower() ?? "bot";
                var channel = platform.Username ?? "";

                await twitchService.ConnectAsync(botUsername, accessToken, channel);

                Log.Information("Successfully auto-connected to Twitch IRC for #{Channel}", channel);
            }
            else
            {
                Log.Information("No connected Twitch platform found for auto-connect");
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error during Twitch auto-connect - continuing with app startup");
            // Don't throw - let the app continue even if auto-connect fails
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
}

