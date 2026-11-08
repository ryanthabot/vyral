using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;
using VyralBot.Data;
using VyralBot.Core.Services.Chat;
using VyralBot.Core.Services.EventBus;

namespace VyralBot.UI.ViewModels;

public partial class DiscordChatSettingsViewModel : ViewModelBase
{
    private readonly ILogger<DiscordChatSettingsViewModel> _logger;
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

    // Display preferences (badges/timestamps/compact) shared with the
    // YouTube/TikTok/Twitch/Kick quick-settings cards
    [ObservableProperty]
    private bool _showBadges = true;

    [ObservableProperty]
    private bool _showTimestamps;

    [ObservableProperty]
    private bool _compactMode;

    // Member Join Event Settings — a guild join is Discord's "follow" moment.
    // The ChatEventSetting row uses the shared "Follow" event vocabulary so
    // conditional auto-reply rules match across platforms.
    [ObservableProperty]
    private bool _showMemberJoinsInChat = true;

    [ObservableProperty]
    private bool _memberJoinAutoReplyEnabled = false;

    [ObservableProperty]
    private string _memberJoinAutoReplyMessage = "Welcome to {guild}, {username}! 🎉";

    // Server Boost Event Settings — a boost is Discord's subscribe equivalent
    // (shared "Subscribe" event vocabulary).
    [ObservableProperty]
    private bool _showBoostsInChat = true;

    [ObservableProperty]
    private bool _boostAutoReplyEnabled = false;

    [ObservableProperty]
    private string _boostAutoReplyMessage = "Thanks for boosting {guild}, {username}! 🚀";

    [ObservableProperty]
    private string _status = "";

    /// <summary>Config key backing the auto-replies master toggle (read by ChatViewModel's Discord event handlers).</summary>
    private const string AutoRepliesEnabledKey = "Chat.Discord.AutoRepliesEnabled";

    private const string ShowBadgesKey = "Chat.Discord.ShowBadges";
    private const string ShowTimestampsKey = "Chat.Discord.ShowTimestamps";
    private const string CompactModeKey = "Chat.Discord.CompactMode";

    public DiscordChatSettingsViewModel(
        ILogger<DiscordChatSettingsViewModel> logger,
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

            // Load Member Join settings (row "Discord"/"Follow" — a guild join is
            // Discord's follow moment)
            var memberJoinSettings = await _eventSettingsService.GetSettingAsync("Discord", "Follow");
            if (memberJoinSettings != null)
            {
                ShowMemberJoinsInChat = memberJoinSettings.ShowInChat;
                MemberJoinAutoReplyEnabled = memberJoinSettings.AutoReplyEnabled;
                MemberJoinAutoReplyMessage = memberJoinSettings.AutoReplyMessage ?? "Welcome to {guild}, {username}! 🎉";
            }

            // Load Server Boost settings (row "Discord"/"Subscribe")
            var boostSettings = await _eventSettingsService.GetSettingAsync("Discord", "Subscribe");
            if (boostSettings != null)
            {
                ShowBoostsInChat = boostSettings.ShowInChat;
                BoostAutoReplyEnabled = boostSettings.AutoReplyEnabled;
                BoostAutoReplyMessage = boostSettings.AutoReplyMessage ?? "Thanks for boosting {guild}, {username}! 🚀";
            }
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
            await PersistDisplayFlagAsync(ShowBadgesKey, ShowBadges);
            await PersistDisplayFlagAsync(ShowTimestampsKey, ShowTimestamps);
            await PersistDisplayFlagAsync(CompactModeKey, CompactMode);

            // Save Member Join settings (row "Discord"/"Follow")
            var memberJoinSetting = await _eventSettingsService.GetSettingAsync("Discord", "Follow")
                ?? new VyralBot.Data.Entities.ChatEventSetting { Platform = "Discord", EventType = "Follow" };
            memberJoinSetting.ShowInChat = ShowMemberJoinsInChat;
            memberJoinSetting.AutoReplyEnabled = MemberJoinAutoReplyEnabled;
            memberJoinSetting.AutoReplyMessage = MemberJoinAutoReplyMessage;
            await _eventSettingsService.SaveSettingAsync(memberJoinSetting);

            // Save Server Boost settings (row "Discord"/"Subscribe")
            var boostSetting = await _eventSettingsService.GetSettingAsync("Discord", "Subscribe")
                ?? new VyralBot.Data.Entities.ChatEventSetting { Platform = "Discord", EventType = "Subscribe" };
            boostSetting.ShowInChat = ShowBoostsInChat;
            boostSetting.AutoReplyEnabled = BoostAutoReplyEnabled;
            boostSetting.AutoReplyMessage = BoostAutoReplyMessage;
            await _eventSettingsService.SaveSettingAsync(boostSetting);

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

    /// <summary>Persists a chat display preference (badges/timestamps/compact) to config.</summary>
    private async Task PersistDisplayFlagAsync(string key, bool value)
    {
        try
        {
            await _dataService.SetConfigValueAsync(key, value.ToString());
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error persisting Discord chat display flag {Key}", key);
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
