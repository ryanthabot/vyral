using Microsoft.Extensions.Logging;
using VyralBot.Core.Services.WebServer;
using VyralBot.Data;
using VyralBot.Data.Entities;
using VyralBot.Integrations.Kick;

namespace VyralBot.Api.Services;

/// <summary>
/// Orchestrates the webhook infrastructure: web server, ngrok tunnel, and webhook subscriptions.
/// Handles startup, URL changes, and automatic reconnection.
/// </summary>
public class WebhookCoordinatorService : IDisposable
{
    private readonly ILogger<WebhookCoordinatorService> _logger;
    private readonly WebServerService _webServerService;
    private readonly NgrokService _ngrokService;
    private readonly KickWebhookSubscriptionService _kickSubscriptionService;
    private readonly IDataService _dataService;
    private readonly WebServerDependencyTracker _dependencyTracker;
    private readonly KickLibOAuthService _kickOAuthService;
    private bool _isInitialized;
    private WebServerSettings? _settings;

    public bool IsRunning => _webServerService.IsRunning && _ngrokService.IsConnected;
    public string? PublicUrl => _ngrokService.PublicUrl;
    public int Port => _webServerService.Port;

    public event EventHandler<string>? StatusChanged;
    public event EventHandler<string>? ErrorOccurred;

    public WebhookCoordinatorService(
        ILogger<WebhookCoordinatorService> logger,
        WebServerService webServerService,
        NgrokService ngrokService,
        KickWebhookSubscriptionService kickSubscriptionService,
        IDataService dataService,
        WebServerDependencyTracker dependencyTracker,
        KickLibOAuthService kickOAuthService)
    {
        _logger = logger;
        _webServerService = webServerService;
        _ngrokService = ngrokService;
        _kickSubscriptionService = kickSubscriptionService;
        _dataService = dataService;
        _dependencyTracker = dependencyTracker;
        _kickOAuthService = kickOAuthService;

        // Subscribe to URL change events for auto-reconnection
        _ngrokService.PublicUrlChanged += OnNgrokUrlChanged;
    }

    public async Task InitializeAsync()
    {
        if (_isInitialized) return;

        _logger.LogInformation("Initializing WebhookCoordinatorService...");

        // Load settings
        _settings = await _dataService.GetWebServerSettingsAsync();
        _logger.LogInformation("Loaded settings - AutoStartOnLaunch: {AutoStart}, Port: {Port}", _settings.AutoStartOnLaunch, _settings.Port);

        // Auto-start if configured AND setup is complete
        if (_settings.AutoStartOnLaunch)
        {
            _logger.LogInformation("AutoStartOnLaunch is enabled, checking ngrok setup...");
            
            // Load auth token to check if setup is complete
            await _ngrokService.LoadAuthTokenAsync();
            
            if (!string.IsNullOrEmpty(_ngrokService.AuthToken))
            {
                _logger.LogInformation("Auto-starting webhook infrastructure (ngrok token found)");
                await StartAsync();
            }
            else
            {
                _logger.LogWarning("Auto-start skipped: Ngrok setup not complete (no auth token found)");
            }
        }
        else
        {
            _logger.LogInformation("AutoStartOnLaunch is disabled, skipping auto-start");
        }

        _isInitialized = true;
        _logger.LogInformation("WebhookCoordinatorService initialized");
    }

    public async Task<bool> StartAsync()
    {
        if (IsRunning)
        {
            _logger.LogWarning("Webhook infrastructure is already running");
            return true;
        }

        try
        {
            _logger.LogInformation("Starting webhook infrastructure...");
            StatusChanged?.Invoke(this, "Starting web server...");

            // Load settings if not loaded
            _settings ??= await _dataService.GetWebServerSettingsAsync();

            // Start Kestrel web server
            _logger.LogInformation("Starting Kestrel web server on port {Port}...", _settings.Port);
            var webServerStarted = await _webServerService.StartAsync(_settings.Port);
            if (!webServerStarted)
            {
                var error = "Failed to start web server - port may be in use";
                ErrorOccurred?.Invoke(this, error);
                return false;
            }

            StatusChanged?.Invoke(this, "Starting ngrok tunnel...");

            // Load auth token from JSON file
            await _ngrokService.LoadAuthTokenAsync();

            // Start ngrok tunnel
            var ngrokStarted = await _ngrokService.StartTunnelAsync(
                _settings.Port,
                _ngrokService.AuthToken,
                _settings.NgrokRegion);

            if (!ngrokStarted)
            {
                var error = "Ngrok is not installed. Please download it from https://ngrok.com/download and add it to your PATH.";
                ErrorOccurred?.Invoke(this, error);
                await _webServerService.StopAsync();
                return false;
            }

            // Update settings with current tunnel URL (reload first to avoid overwriting other changes)
            _settings = await _dataService.GetWebServerSettingsAsync();
            _settings.CurrentTunnelUrl = _ngrokService.PublicUrl;
            _settings.LastStarted = DateTime.UtcNow;
            await _dataService.SaveWebServerSettingsAsync(_settings);

            _logger.LogInformation("Webhook infrastructure started successfully. Public URL: {Url}", _ngrokService.PublicUrl);
            StatusChanged?.Invoke(this, $"Running: {_ngrokService.PublicUrl}");

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting webhook infrastructure");
            ErrorOccurred?.Invoke(this, $"Error: {ex.Message}");
            return false;
        }
    }

    public async Task StopAsync()
    {
        if (!IsRunning)
        {
            _logger.LogWarning("Webhook infrastructure is not running");
            return;
        }

        try
        {
            _logger.LogInformation("Stopping webhook infrastructure...");
            StatusChanged?.Invoke(this, "Stopping...");

            // Stop with timeout to prevent hanging
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            
            try
            {
                // Stop ngrok tunnel
                await _ngrokService.StopTunnelAsync().WaitAsync(cts.Token);

                // Stop Kestrel web server
                await _webServerService.StopAsync().WaitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Webhook infrastructure stop timed out, forcing shutdown");
            }

            // Update settings (reload first to avoid overwriting other changes)
            _settings = await _dataService.GetWebServerSettingsAsync();
            _settings.CurrentTunnelUrl = null;
            _settings.LastStopped = DateTime.UtcNow;
            await _dataService.SaveWebServerSettingsAsync(_settings);

            _logger.LogInformation("Webhook infrastructure stopped");
            StatusChanged?.Invoke(this, "Stopped");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error stopping webhook infrastructure");
            ErrorOccurred?.Invoke(this, $"Error stopping: {ex.Message}");
        }
    }

    public async Task<bool> EnableKickWebhooksAsync(string accessToken, string broadcasterUserId)
    {
        if (!IsRunning)
        {
            _logger.LogWarning("Cannot enable Kick webhooks: infrastructure not running");
            return false;
        }

        try
        {
            _logger.LogInformation("🔔 Enabling Kick webhooks for broadcaster {BroadcasterId}", broadcasterUserId);

            // Clean up any leftover chat.message.sent subscriptions from previous versions
            var existingSubscriptions = await _dataService.GetWebhookSubscriptionsAsync("Kick");
            var chatMessageSubscriptions = existingSubscriptions
                .Where(s => s.EventType == "ChatMessageSent" && s.IsActive)
                .ToList();

            if (chatMessageSubscriptions.Any())
            {
                _logger.LogInformation("🧹 Found {Count} leftover chat.message.sent subscriptions, cleaning up...", chatMessageSubscriptions.Count);
                var subscriptionIds = chatMessageSubscriptions.Select(s => s.Id).ToList();
                await _kickSubscriptionService.UnsubscribeAllAsync(subscriptionIds, accessToken);
                
                foreach (var sub in chatMessageSubscriptions)
                {
                    sub.IsActive = false;
                    await _dataService.SaveWebhookSubscriptionAsync(sub);
                }
                _logger.LogInformation("✅ Cleaned up old chat.message.sent subscriptions");
            }

            // Check for ALL duplicate/orphaned subscriptions to prevent accumulation
            _logger.LogInformation("🔍 Checking for duplicate webhook subscriptions...");
            var allActiveKickSubscriptions = await _kickSubscriptionService.GetActiveSubscriptionsAsync(accessToken);
            _logger.LogInformation("📋 Found {Count} active subscriptions on Kick servers", allActiveKickSubscriptions.Count);
            
            if (allActiveKickSubscriptions.Count > 0)
            {
                _logger.LogWarning("⚠️ Cleaning up {Count} existing subscriptions to prevent duplicates", allActiveKickSubscriptions.Count);
                var allSubscriptionIds = allActiveKickSubscriptions.Select(s => s.Id).ToList();
                await _kickSubscriptionService.UnsubscribeAllAsync(allSubscriptionIds, accessToken);
                
                // Mark all as inactive in database
                var allDbSubs = await _dataService.GetWebhookSubscriptionsAsync("Kick");
                foreach (var sub in allDbSubs.Where(s => s.IsActive))
                {
                    sub.IsActive = false;
                    await _dataService.SaveWebhookSubscriptionAsync(sub);
                }
                _logger.LogInformation("✅ Cleaned up all existing subscriptions");
            }

            // Subscribe to events using KickLib
            // NOTE: KickLib Pusher WebSocket takes priority - only subscribe to events NOT handled by Pusher
            var webhookUrl = $"{_ngrokService.PublicUrl}/webhooks/kick";
            var eventTypes = new List<KickLib.Models.v1.EventSubscriptions.EventType>
            {
                // Events handled by Pusher (REMOVED to prevent duplicates):
                // - ChannelFollowed (Pusher: OnFollowersUpdated)
                // - ChannelSubscriptionNew (Pusher: OnSubscription)
                // - ChannelSubscriptionRenewal (Pusher: OnSubscription)
                // - ChannelSubscriptionGifts (Pusher: OnGiftedSubscription)
                // - LivestreamStatusUpdated (Pusher: OnStreamStatusChanged)
                // - KicksGifted (Pusher: OnKicksGifted)
                
                // Events NOT handled by Pusher (webhook-only):
                KickLib.Models.v1.EventSubscriptions.EventType.LivestreamMetadataUpdated,
                KickLib.Models.v1.EventSubscriptions.EventType.ModerationUserBanned
            };

            _logger.LogInformation("📡 Webhook endpoint: {WebhookUrl}", webhookUrl);

            var subscriptions = await _kickSubscriptionService.SubscribeToEventsAsync(accessToken, eventTypes);

            if (subscriptions.Count == 0)
            {
                _logger.LogError("Failed to subscribe to any Kick webhook events");
                return false;
            }

            // Save subscriptions to database
            foreach (var (eventType, subscriptionId) in subscriptions)
            {
                var subscription = new WebhookSubscription
                {
                    Id = subscriptionId,
                    Platform = "Kick",
                    EventType = eventType.ToString(),
                    EventVersion = 1,
                    BroadcasterUserId = broadcasterUserId,
                    WebhookUrl = webhookUrl,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                };

                await _dataService.SaveWebhookSubscriptionAsync(subscription);
            }

            // Register dependency
            _dependencyTracker.RegisterDependency("Kick", "Real-time event webhooks");

            _logger.LogInformation("Successfully enabled Kick webhooks with {Count} subscriptions", subscriptions.Count);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error enabling Kick webhooks");
            return false;
        }
    }

    public async Task DisableKickWebhooksAsync(string accessToken)
    {
        try
        {
            _logger.LogInformation("🔕 Disabling Kick webhooks...");

            // Get active subscriptions
            var subscriptions = await _dataService.GetWebhookSubscriptionsAsync("Kick");
            var activeSubscriptions = subscriptions.Where(s => s.IsActive).ToList();

            if (activeSubscriptions.Count == 0)
            {
                _logger.LogWarning("⚠️ No active Kick webhook subscriptions found");
                return;
            }

            // Unsubscribe from all using KickLib
            var subscriptionIds = activeSubscriptions.Select(s => s.Id).ToList();
            await _kickSubscriptionService.UnsubscribeAllAsync(subscriptionIds, accessToken);

            // Mark as inactive in database
            foreach (var subscription in activeSubscriptions)
            {
                subscription.IsActive = false;
                await _dataService.SaveWebhookSubscriptionAsync(subscription);
            }

            // Unregister dependency
            _dependencyTracker.UnregisterDependency("Kick", "Real-time event webhooks");

            _logger.LogInformation("Successfully disabled Kick webhooks");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error disabling Kick webhooks");
        }
    }

    private async void OnNgrokUrlChanged(object? sender, string newUrl)
    {
        if (string.IsNullOrEmpty(newUrl))
        {
            _logger.LogWarning("Ngrok tunnel disconnected");
            StatusChanged?.Invoke(this, "Tunnel disconnected - reconnecting...");
            return;
        }

        try
        {
            _logger.LogInformation("Ngrok URL changed to: {NewUrl}", newUrl);
            StatusChanged?.Invoke(this, $"URL changed: {newUrl}");

            // Update settings (reload first to avoid overwriting other changes)
            _settings = await _dataService.GetWebServerSettingsAsync();
            var oldUrl = _settings.CurrentTunnelUrl;
            _settings.CurrentTunnelUrl = newUrl;
            await _dataService.SaveWebServerSettingsAsync(_settings);

            // Check if Kick webhooks need resubscription
            var kickSubscriptions = await _dataService.GetWebhookSubscriptionsAsync("Kick");
            var activeKickSubs = kickSubscriptions.Where(s => s.IsActive).ToList();

            if (activeKickSubs.Count > 0 && !string.IsNullOrEmpty(oldUrl))
            {
                await TryResubscribeKickWebhooksAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling ngrok URL change");
            ErrorOccurred?.Invoke(this, $"Error updating webhooks: {ex.Message}");
        }
    }

    /// <summary>
    /// Attempts to repoint active Kick webhook subscriptions at the current (rotated) ngrok URL by
    /// re-running the enable flow with the stored Kick OAuth token. Refreshes the token first when it
    /// has expired. Falls back to the documented manual path (toggle Kick webhooks off/on) on failure.
    /// </summary>
    private async Task TryResubscribeKickWebhooksAsync()
    {
        var platform = await _dataService.GetPlatformByNameAsync("Kick");
        var accessToken = platform?.AccessToken;
        var userId = platform?.UserId;

        if (string.IsNullOrEmpty(accessToken) || string.IsNullOrEmpty(userId))
        {
            WarnManualResubscribeRequired("no Kick OAuth token or user ID is stored in the database");
            return;
        }

        // Refresh the access token if it has expired, then retry once with the new token
        if (platform != null && platform.TokenExpiresAt.HasValue && platform.TokenExpiresAt.Value <= DateTime.UtcNow)
        {
            _logger.LogInformation("Kick access token has expired - refreshing before resubscribing webhooks...");
            if (!await _kickOAuthService.RefreshTokenAsync())
            {
                WarnManualResubscribeRequired("the Kick token refresh failed");
                return;
            }

            var refreshed = await _dataService.GetPlatformByNameAsync("Kick");
            if (string.IsNullOrEmpty(refreshed?.AccessToken))
            {
                WarnManualResubscribeRequired("no access token was available after the refresh");
                return;
            }
            accessToken = refreshed.AccessToken;
        }

        var resubscribed = await EnableKickWebhooksAsync(accessToken, userId);
        if (resubscribed)
        {
            _logger.LogInformation("Kick webhooks automatically resubscribed to the new ngrok URL: {Url}",
                _ngrokService.PublicUrl);
            StatusChanged?.Invoke(this, $"Kick webhooks resubscribed: {_ngrokService.PublicUrl}");
        }
        else
        {
            WarnManualResubscribeRequired("the resubscription call failed");
        }
    }

    private void WarnManualResubscribeRequired(string reason)
    {
        _logger.LogWarning(
            "Could not automatically resubscribe Kick webhooks to the new ngrok URL ({Reason}). " +
            "Please disable and re-enable Kick webhooks in the Kick settings to subscribe with the new URL.",
            reason);
        StatusChanged?.Invoke(this, "⚠️ Please re-enable Kick webhooks to update subscription");
    }

    public bool CanDisableWebServer()
    {
        return _dependencyTracker.CanDisableWebServer();
    }

    public List<string> GetActiveDependencies()
    {
        return _dependencyTracker.GetActiveDependencies();
    }

    public void Dispose()
    {
        _ngrokService.PublicUrlChanged -= OnNgrokUrlChanged;
        
        // Force stop if still running
        if (IsRunning)
        {
            try
            {
                StopAsync().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during dispose");
            }
        }
        
        _webServerService.Dispose();
        _ngrokService.Dispose();
        GC.SuppressFinalize(this);
    }
}
