using Microsoft.Extensions.DependencyInjection;
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
            MessageBox.Show("VyralBot is already running.", "VyralBot", MessageBoxButton.OK, MessageBoxImage.Information);
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
            // Kill any leftover WebView2 processes from previous runs FIRST
            try
            {
                var webview2Processes = System.Diagnostics.Process.GetProcessesByName("msedgewebview2");
                if (webview2Processes.Length > 0)
                {
                    Log.Information("🧹 Killing {Count} leftover WebView2 processes from previous run...", webview2Processes.Length);
                    foreach (var proc in webview2Processes)
                    {
                        try
                        {
                            proc.Kill(entireProcessTree: true);
                        }
                        catch { }
                        finally
                        {
                            proc.Dispose();
                        }
                    }
                    // Wait a moment for processes to fully terminate
                    await Task.Delay(500);
                    Log.Information("✅ WebView2 processes cleared (~135MB freed)");
                }
            }
            catch { }

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
            MessageBox.Show($"Failed to start: {ex.Message}\n\n{ex.StackTrace}", "VyralBot Error", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
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

            // Force cleanup of WebView2 runtime cache (135MB memory usage)
            try
            {
                Log.Information("Killing cached WebView2 runtime processes to free memory...");
                var webview2Processes = System.Diagnostics.Process.GetProcessesByName("msedgewebview2");
                int killed = 0;
                foreach (var proc in webview2Processes)
                {
                    try
                    {
                        proc.Kill(entireProcessTree: true);
                        killed++;
                    }
                    catch { }
                    finally
                    {
                        proc.Dispose();
                    }
                }
                if (killed > 0)
                {
                    Log.Information("Killed {Count} cached WebView2 processes (~135MB freed)", killed);
                }
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

