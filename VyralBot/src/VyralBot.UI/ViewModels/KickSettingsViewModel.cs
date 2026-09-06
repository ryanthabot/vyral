using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using VyralBot.Data;
using VyralBot.Integrations.Kick;
using VyralBot.Integrations.Twitch;
using VyralBot.Api.Services;
using VyralBot.UI.Services;

namespace VyralBot.UI.ViewModels;

public partial class KickSettingsViewModel : ViewModelBase
{
    private readonly ILogger<KickSettingsViewModel> _logger;
    private readonly IDataService _dataService;
    private readonly VyralBot.Core.Services.EventBus.IEventBus _eventBus;
    private readonly IKickService _kickService;
    private readonly WebhookCoordinatorService _webhookCoordinator;

    // Guards the persist-backs while InitializeAsync loads persisted values
    // into the properties (mirrors KickChatSettingsViewModel's pattern).
    private bool _isInitializing;

    [ObservableProperty]
    private string _username = "";

    [ObservableProperty]
    private string _channelName = "";

    [ObservableProperty]
    private string _status = "Loading...";

    [ObservableProperty]
    private bool _isConnected = false;

    [ObservableProperty]
    private bool _isWebhookEnabled = false;

    [ObservableProperty]
    private string _webhookStatus = "Not configured";

    [ObservableProperty]
    private bool _isWebhookServerRunning = false;

    // Chat WebView2 properties
    [ObservableProperty]
    private bool _isChatEnabled = false;

    [ObservableProperty]
    private bool _autoStartChat = false;

    [ObservableProperty]
    private bool _isChatRunning = false;

    [ObservableProperty]
    private string _chatStatus = "Not started";

    [ObservableProperty]
    private bool _autoRepliesEnabled = true;

    partial void OnAutoRepliesEnabledChanged(bool value)
    {
        // Persist immediately when the inline toggle changes in quick-settings popup
        _ = _dataService.SetConfigValueAsync("Kick.AutoRepliesEnabled", value.ToString());
    }

    public KickSettingsViewModel(
        ILogger<KickSettingsViewModel> logger,
        IDataService dataService,
        IKickService kickService,
        WebhookCoordinatorService webhookCoordinator,
        VyralBot.Core.Services.EventBus.IEventBus eventBus)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
        _kickService = kickService ?? throw new ArgumentNullException(nameof(kickService));
        _webhookCoordinator = webhookCoordinator ?? throw new ArgumentNullException(nameof(webhookCoordinator));
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        
        // Subscribe to webhook status changes
        _webhookCoordinator.StatusChanged += OnWebhookStatusChanged;
    }

    public async Task InitializeAsync()
    {
        _logger.LogInformation("Initializing Kick settings");
        
        try
        {
            var platform = await _dataService.GetPlatformByNameAsync("Kick");

            if (platform != null)
            {
                // Always load chat settings from database regardless of connection status.
                // Wrap in an _isInitializing guard so the property setters don't fire the
                // persist-backs (DB rewrite + redundant routing event) on every page open.
                _isInitializing = true;
                IsChatEnabled = platform.KickChatEnabled;
                AutoStartChat = platform.KickChatAutoStart;
                _isInitializing = false;
                _logger.LogInformation("Loaded Kick chat enabled from DB: {Enabled}", IsChatEnabled);
                
                if (platform.Status == "Connected")
                {
                    Username = platform.Username ?? "";
                    ChannelName = platform.Username ?? "";
                    IsConnected = _kickService.IsConnected;
                    Status = IsConnected 
                        ? $"Connected as {Username}" 
                        : "Disconnected (reconnect on app restart)";
                    
                    IsChatRunning = _kickService.IsConnected;
                    UpdateChatStatus();
                }
                else
                {
                    Status = "Not connected";
                    IsConnected = false;
                }
            }

            // Load auto-replies setting (persisted in Configs table)
            var autoReplies = await _dataService.GetConfigValueAsync("Kick.AutoRepliesEnabled");
            if (!string.IsNullOrEmpty(autoReplies) && bool.TryParse(autoReplies, out var parsed))
            {
                AutoRepliesEnabled = parsed;
            }

            // Load webhook status
            await LoadWebhookStatusAsync();
        }
        catch (Exception ex)
        {
            _isInitializing = false;
            _logger.LogError(ex, "Error loading Kick settings");
            Status = $"Error: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task DisconnectAsync()
    {
        _logger.LogInformation("Disconnecting from Kick");
        
        try
        {
            await _kickService.DisconnectAsync();
            
            var platform = await _dataService.GetPlatformByNameAsync("Kick");

            if (platform != null)
            {
                platform.Status = "Disconnected";
                platform.UpdatedAt = DateTime.UtcNow;
                await _dataService.SavePlatformAsync(platform);
            }

            IsConnected = false;
            Status = "Disconnected";
            _logger.LogInformation("Disconnected from Kick successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error disconnecting from Kick");
            Status = $"Error: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task SaveSettingsAsync()
    {
        _logger.LogInformation("Saving Kick settings");
        
        try
        {
            var platform = await _dataService.GetPlatformByNameAsync("Kick");

            if (platform != null)
            {
                platform.Username = Username;
                platform.UpdatedAt = DateTime.UtcNow;
                
                await _dataService.SavePlatformAsync(platform);
                
                // Persist auto-replies setting
                await _dataService.SetConfigValueAsync("Kick.AutoRepliesEnabled", AutoRepliesEnabled.ToString());

                Status = "Settings saved successfully";
                _logger.LogInformation("Kick settings saved");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving Kick settings");
            Status = $"Error saving: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ToggleWebhooksAsync()
    {
        _logger.LogInformation("Toggling Kick webhooks: {Enabled}", IsWebhookEnabled);
        
        try
        {
            // Get platform info for both enable and disable (need access token)
            var platform = await _dataService.GetPlatformByNameAsync("Kick");
            if (platform == null || string.IsNullOrEmpty(platform.AccessToken))
            {
                WebhookStatus = "Not connected to Kick. Please connect first.";
                IsWebhookEnabled = false;
                return;
            }

            // Kick tokens are NOT encrypted (KickLib manages token security)
            var accessToken = platform.AccessToken;
            
            if (IsWebhookEnabled)
            {
                // Enable webhooks
                if (!_webhookCoordinator.IsRunning)
                {
                    WebhookStatus = "Webhook server is not running. Please start it in Settings → API & Webhooks";
                    IsWebhookEnabled = false;
                    return;
                }

                if (string.IsNullOrEmpty(platform.UserId))
                {
                    WebhookStatus = "Broadcaster ID not found. Please reconnect to Kick.";
                    IsWebhookEnabled = false;
                    return;
                }

                var enableResult = await _webhookCoordinator.EnableKickWebhooksAsync(accessToken, platform.UserId);
                if (enableResult)
                {
                    WebhookStatus = "Webhooks enabled - receiving events";
                    _logger.LogInformation("Kick webhooks enabled successfully");
                }
                else
                {
                    WebhookStatus = "Failed to enable webhooks - one or more subscriptions were rejected. Check the webhook server status and Kick credentials.";
                    IsWebhookEnabled = false;
                    _logger.LogWarning("Kick webhook enable reported failure (subscriptions not registered)");
                }
            }
            else
            {
                // Disable webhooks
                await _webhookCoordinator.DisableKickWebhooksAsync(accessToken);
                WebhookStatus = "Webhooks disabled";
                _logger.LogInformation("Kick webhooks disabled");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error toggling Kick webhooks");
            WebhookStatus = $"Error: {ex.Message}";
            IsWebhookEnabled = false;
        }
    }

    private void OnWebhookStatusChanged(object? sender, string status)
    {
        IsWebhookServerRunning = _webhookCoordinator.IsRunning;
        
        if (!IsWebhookServerRunning && IsWebhookEnabled)
        {
            WebhookStatus = "Webhook server stopped - webhooks disabled";
            IsWebhookEnabled = false;
        }
    }

    private async Task LoadWebhookStatusAsync()
    {
        try
        {
            IsWebhookServerRunning = _webhookCoordinator.IsRunning;
            
            var subscriptions = await _dataService.GetWebhookSubscriptionsAsync("Kick");
            var activeSubscriptions = subscriptions.Where(s => s.IsActive).ToList();
            
            if (activeSubscriptions.Any())
            {
                IsWebhookEnabled = true;
                WebhookStatus = $"Active - {activeSubscriptions.Count} event type(s) subscribed";
            }
            else
            {
                IsWebhookEnabled = false;
                WebhookStatus = IsWebhookServerRunning 
                    ? "Disabled - webhook server is running" 
                    : "Disabled - webhook server is not running";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading webhook status");
            WebhookStatus = "Error loading webhook status";
        }
    }

    // Chat WebView2 methods
    partial void OnIsChatEnabledChanged(bool value)
    {
        // Keep the Start/Stop Chat button's CanExecute in sync with the toggle,
        // otherwise the button stays stuck after IsChatEnabled changes.
        ToggleChatCommand.NotifyCanExecuteChanged();

        if (!_isInitializing)
        {
            _ = SaveChatEnabledSettingAsync(value);
        }
    }

    partial void OnAutoStartChatChanged(bool value)
    {
        if (!_isInitializing)
        {
            _ = SaveAutoStartSettingAsync(value);
        }
    }

    private async Task SaveChatEnabledSettingAsync(bool value)
    {
        try
        {
            var platform = await _dataService.GetPlatformByNameAsync("Kick");
            if (platform != null)
            {
                platform.KickChatEnabled = value;
                platform.UpdatedAt = DateTime.UtcNow;
                await _dataService.SavePlatformAsync(platform);
                _logger.LogInformation("Kick chat enabled setting saved: {Value}", value);
                
                // Publish routing change so ChatViewModel and services update filters immediately
                try
                {
                    var evt = new VyralBot.Core.Services.EventBus.PlatformChatRoutingChangedEvent { Source = "UI", Platform = "Kick", Enabled = value };
                    await _eventBus.PublishAsync(evt);
                    _logger.LogInformation("Published PlatformChatRoutingChangedEvent for Kick: {Enabled}", value);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to publish PlatformChatRoutingChangedEvent for Kick");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving chat enabled setting");
        }
    }

    private async Task SaveAutoStartSettingAsync(bool value)
    {
        try
        {
            var platform = await _dataService.GetPlatformByNameAsync("Kick");
            if (platform != null)
            {
                platform.KickChatAutoStart = value;
                platform.UpdatedAt = DateTime.UtcNow;
                await _dataService.SavePlatformAsync(platform);
                _logger.LogInformation("Kick chat auto-start setting saved: {Value}", value);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving auto-start setting");
        }
    }

    [RelayCommand(CanExecute = nameof(CanToggleChat))]
    private async Task ToggleChatAsync()
    {
        try
        {
            if (IsChatRunning)
            {
                _logger.LogInformation("Stopping Kick chat");
                await _kickService.DisconnectAsync();
                IsChatRunning = false;
                ChatStatus = "Stopped";
            }
            else
            {
                _logger.LogInformation("Starting Kick chat");

                // Get credentials from database
                var platform = await _dataService.GetPlatformByNameAsync("Kick");
                if (platform == null || string.IsNullOrEmpty(platform.Username))
                {
                    ChatStatus = "Error: Kick not configured";
                    return;
                }

                // Kick tokens are NOT encrypted (KickLib manages token security)
                string? accessToken = platform.AccessToken;

                if (string.IsNullOrEmpty(accessToken))
                {
                    ChatStatus = "Error: No access token";
                    return;
                }

                ChatStatus = "Connecting...";
                
                // Connect using KickLib (automatically fetches channel/chatroom IDs)
                await _kickService.ConnectAsync(
                    platform.Username, 
                    accessToken, 
                    platform.Username, // channelName same as username
                    int.Parse(platform.UserId ?? "0"));
                
                IsChatRunning = true;
                ChatStatus = "Connected";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error toggling Kick chat");
            ChatStatus = $"Error: {ex.Message}";
            IsChatRunning = false;
        }
    }

    private bool CanToggleChat()
    {
        return !string.IsNullOrEmpty(Username) && IsChatEnabled;
    }

    private void UpdateChatStatus()
    {
        if (!IsChatEnabled)
        {
            ChatStatus = "Chat disabled";
        }
        else if (IsChatRunning)
        {
            ChatStatus = "Connected";
        }
        else
        {
            ChatStatus = "Not started";
        }
    }
}
