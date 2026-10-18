using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;
using VyralBot.Data;
using VyralBot.Core.Services.EventBus;

namespace VyralBot.UI.ViewModels;

public partial class TiktokChatSettingsViewModel : ViewModelBase
{
    private readonly ILogger<TiktokChatSettingsViewModel> _logger;
    private readonly IDataService _dataService;
    private readonly IEventBus _eventBus;
    private bool _isInitialized;

    [ObservableProperty]
    private bool _isChatEnabled;

    [ObservableProperty]
    private bool _autoStartChat;

    [ObservableProperty]
    private bool _autoRepliesEnabled = true;

    // Display preferences for the TikTok quick-settings card (Views/TiktokChatSettingsView.xaml)
    [ObservableProperty]
    private bool _showBadges = true;

    [ObservableProperty]
    private bool _showTimestamps;

    [ObservableProperty]
    private bool _compactMode;

    [ObservableProperty]
    private string _status = "";

    /// <summary>Config key backing the auto-replies master toggle (read by ChatViewModel's TikTok event handlers).</summary>
    private const string AutoRepliesEnabledKey = "Chat.TikTok.AutoRepliesEnabled";

    private const string ShowBadgesKey = "Chat.TikTok.ShowBadges";
    private const string ShowTimestampsKey = "Chat.TikTok.ShowTimestamps";
    private const string CompactModeKey = "Chat.TikTok.CompactMode";

    public TiktokChatSettingsViewModel(ILogger<TiktokChatSettingsViewModel> logger, IDataService dataService, IEventBus eventBus)
    {
        _logger = logger;
        _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
    }

    public async Task InitializeAsync()
    {
        try
        {
            var platform = await _dataService.GetPlatformByNameAsync("TikTok");

            if (platform != null)
            {
                // Just load the persisted values — no force-enable: the user's
                // "chat disabled" choice must survive reconnects.
                _isInitialized = false;
                IsChatEnabled = platform.TiktokChatEnabled;
                AutoStartChat = platform.TiktokChatAutoStart;
                _isInitialized = true;
            }
            else
            {
                _isInitialized = true;
            }

            // Load the auto-replies master toggle + display prefs from config
            // (defaults: auto-replies on, badges on, timestamps/compact off)
            _isInitialized = false;
            var storedAutoReplies = await _dataService.GetConfigValueAsync(AutoRepliesEnabledKey);
            AutoRepliesEnabled = !bool.TryParse(storedAutoReplies, out var autoRepliesEnabled) || autoRepliesEnabled;
            var storedShowBadges = await _dataService.GetConfigValueAsync(ShowBadgesKey);
            ShowBadges = !bool.TryParse(storedShowBadges, out var showBadges) || showBadges;
            var storedShowTimestamps = await _dataService.GetConfigValueAsync(ShowTimestampsKey);
            ShowTimestamps = bool.TryParse(storedShowTimestamps, out var showTimestamps) && showTimestamps;
            var storedCompactMode = await _dataService.GetConfigValueAsync(CompactModeKey);
            CompactMode = bool.TryParse(storedCompactMode, out var compactMode) && compactMode;
            _isInitialized = true;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error initializing TikTok chat settings");
        }
    }

    [RelayCommand]
    private async Task SaveSettingsAsync()
    {
        try
        {
            // Persist the current values via the same paths the toggles use
            // (PersistPlatformFlagAsync also publishes the routing-changed event).
            await PersistPlatformFlagAsync("TikTok", IsChatEnabled);
            await PersistAutoStartFlagAsync(AutoStartChat);
            await PersistAutoRepliesEnabledAsync(AutoRepliesEnabled);
            await PersistDisplayFlagAsync(ShowBadgesKey, ShowBadges);
            await PersistDisplayFlagAsync(ShowTimestampsKey, ShowTimestamps);
            await PersistDisplayFlagAsync(CompactModeKey, CompactMode);

            Status = "Saved";
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error saving TikTok chat settings");
            Status = "Failed to save settings";
        }
    }

    partial void OnIsChatEnabledChanged(bool value)
    {
        if (_isInitialized)
        {
            _ = PersistPlatformFlagAsync("TikTok", value);
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

    partial void OnShowBadgesChanged(bool value)
    {
        if (_isInitialized)
        {
            _ = PersistDisplayFlagAsync(ShowBadgesKey, value);
        }
    }

    partial void OnShowTimestampsChanged(bool value)
    {
        if (_isInitialized)
        {
            _ = PersistDisplayFlagAsync(ShowTimestampsKey, value);
        }
    }

    partial void OnCompactModeChanged(bool value)
    {
        if (_isInitialized)
        {
            _ = PersistDisplayFlagAsync(CompactModeKey, value);
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

            var platform = await _dataService.GetPlatformByNameAsync("TikTok");
            var evt = new PlatformChatRoutingChangedEvent { Source = "UI", Platform = "TikTok", Enabled = platform?.TiktokChatEnabled ?? true };
            await _eventBus.PublishAsync(evt);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error persisting TikTok auto-replies master toggle");
        }
    }

    /// <summary>Persists a chat display preference (badges/timestamps/compact) to config.</summary>
    private async Task PersistDisplayFlagAsync(string key, bool value)
    {
        try
        {
            await _dataService.SetConfigValueAsync(key, value.ToString());
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error persisting TikTok chat display flag {Key}", key);
        }
    }

    /// <summary>Persists the TiktokChatAutoStart flag consumed at app launch (Settings → Platforms).</summary>
    private async Task PersistAutoStartFlagAsync(bool value)
    {
        try
        {
            var platform = await _dataService.GetPlatformByNameAsync("TikTok");
            if (platform != null)
            {
                platform.TiktokChatAutoStart = value;
                platform.UpdatedAt = DateTime.UtcNow;
                await _dataService.SavePlatformAsync(platform);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error persisting TikTok auto-start flag");
        }
    }

    private async Task PersistPlatformFlagAsync(string platformName, bool enabled)
    {
        try
        {
            var platform = await _dataService.GetPlatformByNameAsync(platformName);
            if (platform == null)
            {
                // TikTok platform row missing (TikTok not set up): surface why the
                // toggle silently does nothing instead of no-oping quietly.
                _logger?.LogWarning("PersistPlatformFlagAsync skipped: the {Platform} platform row does not exist", platformName);
                Status = "TikTok is not set up — connect it first";
                return;
            }

            platform.TiktokChatEnabled = enabled;
            platform.UpdatedAt = DateTime.UtcNow;
            await _dataService.SavePlatformAsync(platform);

            var evt = new PlatformChatRoutingChangedEvent { Source = "UI", Platform = platformName, Enabled = enabled };
            await _eventBus.PublishAsync(evt);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error persisting TikTok chat enabled flag");
        }
    }
}
