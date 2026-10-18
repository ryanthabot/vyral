using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;
using VyralBot.Data;
using VyralBot.Core.Services.EventBus;

namespace VyralBot.UI.ViewModels;

public partial class DiscordChatSettingsViewModel : ViewModelBase
{
    private readonly ILogger<DiscordChatSettingsViewModel> _logger;
    private readonly IDataService _dataService;
    private readonly IEventBus _eventBus;
    private bool _isInitialized;

    [ObservableProperty]
    private bool _isChatEnabled;

    [ObservableProperty]
    private bool _autoStartChat;

    [ObservableProperty]
    private bool _autoRepliesEnabled = true;

    [ObservableProperty]
    private string _status = "";

    /// <summary>Config key backing the auto-replies master toggle (read by ChatViewModel's Discord event handlers).</summary>
    private const string AutoRepliesEnabledKey = "Chat.Discord.AutoRepliesEnabled";

    public DiscordChatSettingsViewModel(ILogger<DiscordChatSettingsViewModel> logger, IDataService dataService, IEventBus eventBus)
    {
        _logger = logger;
        _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
    }

    public async Task InitializeAsync()
    {
        try
        {
            var platform = await _dataService.GetPlatformByNameAsync("Discord");

            if (platform != null)
            {
                // Just load the persisted values — no force-enable: the user's
                // "chat disabled" choice must survive reconnects.
                _isInitialized = false;
                IsChatEnabled = platform.DiscordChatEnabled;
                AutoStartChat = platform.DiscordChatAutoStart;
                _isInitialized = true;
            }
            else
            {
                _isInitialized = true;
            }

            // Load the auto-replies master toggle from config (default true)
            _isInitialized = false;
            var storedAutoReplies = await _dataService.GetConfigValueAsync(AutoRepliesEnabledKey);
            AutoRepliesEnabled = !bool.TryParse(storedAutoReplies, out var autoRepliesEnabled) || autoRepliesEnabled;
            _isInitialized = true;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error initializing Discord chat settings");
        }
    }

    [RelayCommand]
    private async Task SaveSettingsAsync()
    {
        try
        {
            // Persist the current values via the same paths the toggles use
            // (PersistPlatformFlagAsync also publishes the routing-changed event).
            await PersistPlatformFlagAsync("Discord", IsChatEnabled);
            await PersistAutoStartFlagAsync(AutoStartChat);
            await PersistAutoRepliesEnabledAsync(AutoRepliesEnabled);

            Status = "Saved";
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error saving Discord chat settings");
            Status = "Failed to save settings";
        }
    }

    partial void OnIsChatEnabledChanged(bool value)
    {
        if (_isInitialized)
        {
            _ = PersistPlatformFlagAsync("Discord", value);
        }
    }

    partial void OnAutoStartChatChanged(bool value)
    {
        if (_isInitialized)
        {
            _ = PersistAutoStartFlagAsync(value);
        }
    }

    partial void OnAutoRepliesEnabledChanged(bool value)
    {
        if (_isInitialized)
        {
            _ = PersistAutoRepliesEnabledAsync(value);
        }
    }

    /// <summary>
    /// Persists the auto-replies master toggle to config and publishes the
    /// routing-changed event so ChatViewModel re-reads the toggle immediately.
    /// The event carries the current chat-routing flag (NOT the toggle value)
    /// so the chat filter is untouched. Mirrors TwitchChatSettingsViewModel.
    /// </summary>
    private async Task PersistAutoRepliesEnabledAsync(bool value)
    {
        try
        {
            await _dataService.SetConfigValueAsync(AutoRepliesEnabledKey, value.ToString());

            var platform = await _dataService.GetPlatformByNameAsync("Discord");
            var evt = new PlatformChatRoutingChangedEvent { Source = "UI", Platform = "Discord", Enabled = platform?.DiscordChatEnabled ?? true };
            await _eventBus.PublishAsync(evt);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error persisting Discord auto-replies master toggle");
        }
    }

    /// <summary>Persists the DiscordChatAutoStart flag consumed at app launch (Settings → Platforms).</summary>
    private async Task PersistAutoStartFlagAsync(bool value)
    {
        try
        {
            var platform = await _dataService.GetPlatformByNameAsync("Discord");
            if (platform != null)
            {
                platform.DiscordChatAutoStart = value;
                platform.UpdatedAt = DateTime.UtcNow;
                await _dataService.SavePlatformAsync(platform);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error persisting Discord auto-start flag");
        }
    }

    private async Task PersistPlatformFlagAsync(string platformName, bool enabled)
    {
        try
        {
            var platform = await _dataService.GetPlatformByNameAsync(platformName);
            if (platform != null)
            {
                platform.DiscordChatEnabled = enabled;
                platform.UpdatedAt = DateTime.UtcNow;
                await _dataService.SavePlatformAsync(platform);
            }

            var evt = new PlatformChatRoutingChangedEvent { Source = "UI", Platform = platformName, Enabled = enabled };
            await _eventBus.PublishAsync(evt);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error persisting Discord chat enabled flag");
        }
    }
}
