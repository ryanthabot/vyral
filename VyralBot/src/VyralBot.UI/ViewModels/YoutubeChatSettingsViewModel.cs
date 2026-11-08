using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;
using VyralBot.Data;
using VyralBot.Core.Services.Chat;
using VyralBot.Core.Services.EventBus;

namespace VyralBot.UI.ViewModels;

public partial class YoutubeChatSettingsViewModel : ViewModelBase
{
    private readonly ILogger<YoutubeChatSettingsViewModel> _logger;
    private readonly IDataService _dataService;
    private readonly IEventBus _eventBus;
    private readonly IChatEventSettingsService _eventSettingsService;
    private bool _isInitialized;

    [ObservableProperty]
    private bool _isChatEnabled;

    [ObservableProperty]
    private bool _autoStartChat;

    [ObservableProperty]
    private bool _autoRepliesEnabled = true;

    // Display preferences for the YouTube quick-settings card (Views/YoutubeChatSettingsView.xaml)
    [ObservableProperty]
    private bool _showBadges = true;

    [ObservableProperty]
    private bool _showTimestamps;

    [ObservableProperty]
    private bool _compactMode;

    // Membership Event Settings — membership is YouTube's subscribe equivalent.
    // The ChatEventSetting row uses the shared "Subscribe" event vocabulary so
    // conditional auto-reply rules match across platforms. There is no
    // "Show in chat" toggle: membership lines are always rendered by the
    // synthetic chat message YouTubeChatService publishes with the event.
    [ObservableProperty]
    private bool _membershipAutoReplyEnabled = false;

    [ObservableProperty]
    private string _membershipAutoReplyMessage = "Thanks for the membership {username}! 🎉";

    // Super Chat Event Settings — Super Chat/Sticker is YouTube's cheer
    // equivalent (shared "Cheer" event vocabulary). Same as membership, the
    // chat line comes from the synthetic message, so no "Show in chat" toggle.
    [ObservableProperty]
    private bool _superChatAutoReplyEnabled = false;

    [ObservableProperty]
    private string _superChatAutoReplyMessage = "Thanks for the {amount} Super Chat {username}! 💎";

    [ObservableProperty]
    private string _status = "";

    /// <summary>Config key backing the auto-replies master toggle (read by ChatViewModel's YouTube event handlers).</summary>
    private const string AutoRepliesEnabledKey = "Chat.YouTube.AutoRepliesEnabled";

    private const string ShowBadgesKey = "Chat.YouTube.ShowBadges";
    private const string ShowTimestampsKey = "Chat.YouTube.ShowTimestamps";
    private const string CompactModeKey = "Chat.YouTube.CompactMode";

    public YoutubeChatSettingsViewModel(
        ILogger<YoutubeChatSettingsViewModel> logger,
        IDataService dataService,
        IEventBus eventBus,
        IChatEventSettingsService eventSettingsService)
    {
        _logger = logger;
        _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        _eventSettingsService = eventSettingsService ?? throw new ArgumentNullException(nameof(eventSettingsService));
    }

    public async Task InitializeAsync()
    {
        try
        {
            var platform = await _dataService.GetPlatformByNameAsync("YouTube");

            if (platform != null)
            {
                // Just load the persisted values — no force-enable: the user's
                // "chat disabled" choice must survive reconnects.
                _isInitialized = false;
                IsChatEnabled = platform.YoutubeChatEnabled;
                AutoStartChat = platform.YoutubeChatAutoStart;
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

            // Load Membership settings (row "YouTube"/"Subscribe" — membership is
            // YouTube's subscribe equivalent)
            var membershipSettings = await _eventSettingsService.GetSettingAsync("YouTube", "Subscribe");
            if (membershipSettings != null)
            {
                MembershipAutoReplyEnabled = membershipSettings.AutoReplyEnabled;
                MembershipAutoReplyMessage = membershipSettings.AutoReplyMessage ?? "Thanks for the membership {username}! 🎉";
            }

            // Load Super Chat settings (row "YouTube"/"Cheer")
            var superChatSettings = await _eventSettingsService.GetSettingAsync("YouTube", "Cheer");
            if (superChatSettings != null)
            {
                SuperChatAutoReplyEnabled = superChatSettings.AutoReplyEnabled;
                SuperChatAutoReplyMessage = superChatSettings.AutoReplyMessage ?? "Thanks for the {amount} Super Chat {username}! 💎";
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error initializing YouTube chat settings");
        }
    }

    [RelayCommand]
    private async Task SaveSettingsAsync()
    {
        try
        {
            // Persist the current values via the same paths the toggles use
            // (PersistPlatformFlagAsync also publishes the routing-changed event).
            await PersistPlatformFlagAsync("YouTube", IsChatEnabled);
            await PersistAutoStartFlagAsync(AutoStartChat);
            await PersistAutoRepliesEnabledAsync(AutoRepliesEnabled);
            await PersistDisplayFlagAsync(ShowBadgesKey, ShowBadges);
            await PersistDisplayFlagAsync(ShowTimestampsKey, ShowTimestamps);
            await PersistDisplayFlagAsync(CompactModeKey, CompactMode);

            // Save Membership settings (row "YouTube"/"Subscribe"). ShowInChat is
            // forced true: the synthetic chat line from YouTubeChatService always
            // renders, so persisting a false value would be a dead flag.
            var membershipSetting = await _eventSettingsService.GetSettingAsync("YouTube", "Subscribe")
                ?? new VyralBot.Data.Entities.ChatEventSetting { Platform = "YouTube", EventType = "Subscribe" };
            membershipSetting.ShowInChat = true;
            membershipSetting.AutoReplyEnabled = MembershipAutoReplyEnabled;
            membershipSetting.AutoReplyMessage = MembershipAutoReplyMessage;
            await _eventSettingsService.SaveSettingAsync(membershipSetting);

            // Save Super Chat settings (row "YouTube"/"Cheer")
            var superChatSetting = await _eventSettingsService.GetSettingAsync("YouTube", "Cheer")
                ?? new VyralBot.Data.Entities.ChatEventSetting { Platform = "YouTube", EventType = "Cheer" };
            superChatSetting.ShowInChat = true;
            superChatSetting.AutoReplyEnabled = SuperChatAutoReplyEnabled;
            superChatSetting.AutoReplyMessage = SuperChatAutoReplyMessage;
            await _eventSettingsService.SaveSettingAsync(superChatSetting);

            Status = "Saved";
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error saving YouTube chat settings");
            Status = "Failed to save settings";
        }
    }

    partial void OnIsChatEnabledChanged(bool value)
    {
        if (_isInitialized)
        {
            _ = PersistPlatformFlagAsync("YouTube", value);
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

            var platform = await _dataService.GetPlatformByNameAsync("YouTube");
            var evt = new PlatformChatRoutingChangedEvent { Source = "UI", Platform = "YouTube", Enabled = platform?.YoutubeChatEnabled ?? true };
            await _eventBus.PublishAsync(evt);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error persisting YouTube auto-replies master toggle");
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
            _logger?.LogError(ex, "Error persisting YouTube chat display flag {Key}", key);
        }
    }

    /// <summary>Persists the YoutubeChatAutoStart flag consumed at app launch (Settings → Platforms).</summary>
    private async Task PersistAutoStartFlagAsync(bool value)
    {
        try
        {
            var platform = await _dataService.GetPlatformByNameAsync("YouTube");
            if (platform != null)
            {
                platform.YoutubeChatAutoStart = value;
                platform.UpdatedAt = DateTime.UtcNow;
                await _dataService.SavePlatformAsync(platform);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error persisting YouTube auto-start flag");
        }
    }

    private async Task PersistPlatformFlagAsync(string platformName, bool enabled)
    {
        try
        {
            var platform = await _dataService.GetPlatformByNameAsync(platformName);
            if (platform != null)
            {
                platform.YoutubeChatEnabled = enabled;
                platform.UpdatedAt = DateTime.UtcNow;
                await _dataService.SavePlatformAsync(platform);
            }

            var evt = new PlatformChatRoutingChangedEvent { Source = "UI", Platform = platformName, Enabled = enabled };
            await _eventBus.PublishAsync(evt);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error persisting YouTube chat enabled flag");
        }
    }
}
