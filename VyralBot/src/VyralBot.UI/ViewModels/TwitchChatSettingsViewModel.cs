using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;
using VyralBot.Core.Services.Chat;
using VyralBot.Core.Services.EventBus;
using VyralBot.Data;

namespace VyralBot.UI.ViewModels;

public partial class TwitchChatSettingsViewModel : ViewModelBase
{
    private readonly ILogger<TwitchChatSettingsViewModel> _logger;
    private readonly IChatEventSettingsService _eventSettingsService;
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

    // Follow Event Settings
    [ObservableProperty]
    private bool _showFollowsInChat = true;

    [ObservableProperty]
    private bool _followAutoReplyEnabled = false;

    [ObservableProperty]
    private string _followAutoReplyMessage = "Thanks for the follow {username}!";

    // Subscribe Event Settings
    [ObservableProperty]
    private bool _showSubscribesInChat = true;

    [ObservableProperty]
    private bool _subscribeAutoReplyEnabled = false;

    [ObservableProperty]
    private string _subscribeAutoReplyMessage = "Thanks for subscribing {username}! 🎉";

    // Resub Event Settings
    [ObservableProperty]
    private bool _showResubsInChat = true;

    [ObservableProperty]
    private bool _resubAutoReplyEnabled = false;

    [ObservableProperty]
    private string _resubAutoReplyMessage = "Thanks for the {months} month resub {username}!";

    // Gift Sub Event Settings
    [ObservableProperty]
    private bool _showGiftSubsInChat = true;

    [ObservableProperty]
    private bool _giftSubAutoReplyEnabled = false;

    [ObservableProperty]
    private string _giftSubAutoReplyMessage = "Thank you {gifter} for gifting {count} subs! 🎁";

    // Raid Event Settings
    [ObservableProperty]
    private bool _showRaidsInChat = true;

    [ObservableProperty]
    private bool _raidAutoReplyEnabled = false;

    [ObservableProperty]
    private string _raidAutoReplyMessage = "Thanks for the raid {raider} with {viewers} viewers!";

    // Cheer Event Settings
    [ObservableProperty]
    private bool _showCheersInChat = true;

    [ObservableProperty]
    private bool _cheerAutoReplyEnabled = false;

    [ObservableProperty]
    private string _cheerAutoReplyMessage = "Thanks for the {bits} bits {username}!";

    public TwitchChatSettingsViewModel(
        ILogger<TwitchChatSettingsViewModel> logger,
        IChatEventSettingsService eventSettingsService,
        IDataService dataService,
        IEventBus eventBus)
    {
        _logger = logger;
        _eventSettingsService = eventSettingsService;
        _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
    }

    /// <summary>
    /// Two-argument constructor retained for the TwitchChatSettingsView code-behind path.
    /// Resolves the data service and event bus from the application service provider;
    /// DI uses the full constructor above.
    /// </summary>
    public TwitchChatSettingsViewModel(
        ILogger<TwitchChatSettingsViewModel> logger,
        IChatEventSettingsService eventSettingsService)
        : this(
            logger,
            eventSettingsService,
            App.ServiceProvider?.GetRequiredService<IDataService>()
                ?? throw new InvalidOperationException("Data service is not available"),
            App.ServiceProvider?.GetRequiredService<IEventBus>()
                ?? throw new InvalidOperationException("Event bus is not available"))
    {
    }

    public async Task InitializeAsync()
    {
        try
        {
            _logger.LogInformation("Loading Twitch chat event settings...");

            // Load the chat routing flag from the platform row
            var platform = await _dataService.GetPlatformByNameAsync("Twitch");
            if (platform != null)
            {
                _isInitialized = false;
                IsChatEnabled = platform.TwitchChatEnabled;
                AutoStartChat = platform.TwitchChatAutoStart;
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

            // Load Follow settings
            var followSettings = await _eventSettingsService.GetSettingAsync("Twitch", "Follow");
            if (followSettings != null)
            {
                ShowFollowsInChat = followSettings.ShowInChat;
                FollowAutoReplyEnabled = followSettings.AutoReplyEnabled;
                FollowAutoReplyMessage = followSettings.AutoReplyMessage ?? "Thanks for the follow {username}!";
            }

            // Load Subscribe settings
            var subscribeSettings = await _eventSettingsService.GetSettingAsync("Twitch", "Subscribe");
            if (subscribeSettings != null)
            {
                ShowSubscribesInChat = subscribeSettings.ShowInChat;
                SubscribeAutoReplyEnabled = subscribeSettings.AutoReplyEnabled;
                SubscribeAutoReplyMessage = subscribeSettings.AutoReplyMessage ?? "Thanks for subscribing {username}! 🎉";
            }

            // Load Resub settings
            var resubSettings = await _eventSettingsService.GetSettingAsync("Twitch", "Resub");
            if (resubSettings != null)
            {
                ShowResubsInChat = resubSettings.ShowInChat;
                ResubAutoReplyEnabled = resubSettings.AutoReplyEnabled;
                ResubAutoReplyMessage = resubSettings.AutoReplyMessage ?? "Thanks for the {months} month resub {username}!";
            }

            // Load Gift Sub settings
            var giftSubSettings = await _eventSettingsService.GetSettingAsync("Twitch", "GiftSub");
            if (giftSubSettings != null)
            {
                ShowGiftSubsInChat = giftSubSettings.ShowInChat;
                GiftSubAutoReplyEnabled = giftSubSettings.AutoReplyEnabled;
                GiftSubAutoReplyMessage = giftSubSettings.AutoReplyMessage ?? "Thank you {gifter} for gifting {count} subs! 🎁";
            }

            // Load Raid settings
            var raidSettings = await _eventSettingsService.GetSettingAsync("Twitch", "Raid");
            if (raidSettings != null)
            {
                ShowRaidsInChat = raidSettings.ShowInChat;
                RaidAutoReplyEnabled = raidSettings.AutoReplyEnabled;
                RaidAutoReplyMessage = raidSettings.AutoReplyMessage ?? "Thanks for the raid {raider} with {viewers} viewers!";
            }

            // Load Cheer settings
            var cheerSettings = await _eventSettingsService.GetSettingAsync("Twitch", "Cheer");
            if (cheerSettings != null)
            {
                ShowCheersInChat = cheerSettings.ShowInChat;
                CheerAutoReplyEnabled = cheerSettings.AutoReplyEnabled;
                CheerAutoReplyMessage = cheerSettings.AutoReplyMessage ?? "Thanks for the {bits} bits {username}!";
            }

            _logger.LogInformation("Twitch chat event settings loaded successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load Twitch chat event settings");
            Status = "Failed to load settings";
        }
    }

    partial void OnIsChatEnabledChanged(bool value)
    {
        if (_isInitialized)
        {
            _ = PersistChatRoutingFlagAsync(value);
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

    /// <summary>Config key backing the auto-replies master toggle (read by ChatViewModel's Twitch event handlers).</summary>
    private const string AutoRepliesEnabledKey = "Chat.Twitch.AutoRepliesEnabled";

    /// <summary>
    /// Persists the auto-replies master toggle to config and publishes the
    /// routing-changed event so ChatViewModel re-reads the toggle immediately.
    /// The event carries the current chat-routing flag (NOT the toggle value)
    /// so the chat filter is untouched.
    /// </summary>
    private async Task PersistAutoRepliesEnabledAsync(bool value)
    {
        try
        {
            await _dataService.SetConfigValueAsync(AutoRepliesEnabledKey, value.ToString());

            var platform = await _dataService.GetPlatformByNameAsync("Twitch");
            var evt = new PlatformChatRoutingChangedEvent { Source = "UI", Platform = "Twitch", Enabled = platform?.TwitchChatEnabled ?? true };
            await _eventBus.PublishAsync(evt);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error persisting Twitch auto-replies master toggle");
        }
    }

    /// <summary>
    /// Persists the TwitchChatAutoStart flag consumed at app launch (Settings → Platforms
    /// and MainViewModel's startup auto-connect).
    /// </summary>
    private async Task PersistAutoStartFlagAsync(bool value)
    {
        try
        {
            var platform = await _dataService.GetPlatformByNameAsync("Twitch");
            if (platform != null)
            {
                platform.TwitchChatAutoStart = value;
                platform.UpdatedAt = DateTime.UtcNow;
                await _dataService.SavePlatformAsync(platform);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error persisting Twitch auto-start flag");
        }
    }

    /// <summary>
    /// Persists the TwitchChatEnabled platform flag and publishes the routing-changed
    /// event so the chat feed's display filter updates live (mirrors the YouTube/Kick pattern).
    /// </summary>
    private async Task PersistChatRoutingFlagAsync(bool enabled)
    {
        try
        {
            var platform = await _dataService.GetPlatformByNameAsync("Twitch");
            if (platform != null)
            {
                platform.TwitchChatEnabled = enabled;
                platform.UpdatedAt = DateTime.UtcNow;
                await _dataService.SavePlatformAsync(platform);
            }

            var evt = new PlatformChatRoutingChangedEvent { Source = "UI", Platform = "Twitch", Enabled = enabled };
            await _eventBus.PublishAsync(evt);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error persisting Twitch chat enabled flag");
        }
    }

    /// <summary>
    /// Mirrors a saved ChatEventSetting's sound fields into the EventSound_/EventVolume_
    /// config keys SoundService reads for real playback: the file name (empty string
    /// disables) and an invariant 0..1 volume string. Rows without a sound file are
    /// skipped so mappings made on the Sounds page are never cleared from here.
    /// </summary>
    private async Task PersistEventSoundConfigAsync(string playbackEventType, VyralBot.Data.Entities.ChatEventSetting setting)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(setting.SoundFile))
            {
                return;
            }

            await _dataService.SetConfigValueAsync($"EventSound_Twitch_{playbackEventType}", setting.SoundEnabled ? setting.SoundFile : string.Empty);
            await _dataService.SetConfigValueAsync(
                $"EventVolume_Twitch_{playbackEventType}",
                (Math.Clamp(setting.SoundVolume, 0, 100) / 100.0).ToString("F2", System.Globalization.CultureInfo.InvariantCulture));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist the event sound config for Twitch/{EventType}", playbackEventType);
        }
    }

    [RelayCommand]
    private async Task SaveSettingsAsync()
    {
        try
        {
            _logger.LogInformation("Saving Twitch chat event settings...");

            // Save Follow settings
            var followSetting = await _eventSettingsService.GetSettingAsync("Twitch", "Follow") ?? new VyralBot.Data.Entities.ChatEventSetting { Platform = "Twitch", EventType = "Follow" };
            followSetting.ShowInChat = ShowFollowsInChat;
            followSetting.AutoReplyEnabled = FollowAutoReplyEnabled;
            followSetting.AutoReplyMessage = FollowAutoReplyMessage;
            await _eventSettingsService.SaveSettingAsync(followSetting);
            await PersistEventSoundConfigAsync("follow", followSetting);

            // Save Subscribe settings
            var subscribeSetting = await _eventSettingsService.GetSettingAsync("Twitch", "Subscribe") ?? new VyralBot.Data.Entities.ChatEventSetting { Platform = "Twitch", EventType = "Subscribe" };
            subscribeSetting.ShowInChat = ShowSubscribesInChat;
            subscribeSetting.AutoReplyEnabled = SubscribeAutoReplyEnabled;
            subscribeSetting.AutoReplyMessage = SubscribeAutoReplyMessage;
            await _eventSettingsService.SaveSettingAsync(subscribeSetting);
            await PersistEventSoundConfigAsync("subscribe", subscribeSetting);

            // Save Resub settings
            var resubSetting = await _eventSettingsService.GetSettingAsync("Twitch", "Resub") ?? new VyralBot.Data.Entities.ChatEventSetting { Platform = "Twitch", EventType = "Resub" };
            resubSetting.ShowInChat = ShowResubsInChat;
            resubSetting.AutoReplyEnabled = ResubAutoReplyEnabled;
            resubSetting.AutoReplyMessage = ResubAutoReplyMessage;
            await _eventSettingsService.SaveSettingAsync(resubSetting);
            await PersistEventSoundConfigAsync("resub", resubSetting);

            // Save Gift Sub settings
            var giftSubSetting = await _eventSettingsService.GetSettingAsync("Twitch", "GiftSub") ?? new VyralBot.Data.Entities.ChatEventSetting { Platform = "Twitch", EventType = "GiftSub" };
            giftSubSetting.ShowInChat = ShowGiftSubsInChat;
            giftSubSetting.AutoReplyEnabled = GiftSubAutoReplyEnabled;
            giftSubSetting.AutoReplyMessage = GiftSubAutoReplyMessage;
            await _eventSettingsService.SaveSettingAsync(giftSubSetting);
            await PersistEventSoundConfigAsync("giftsub", giftSubSetting);

            // Save Raid settings
            var raidSetting = await _eventSettingsService.GetSettingAsync("Twitch", "Raid") ?? new VyralBot.Data.Entities.ChatEventSetting { Platform = "Twitch", EventType = "Raid" };
            raidSetting.ShowInChat = ShowRaidsInChat;
            raidSetting.AutoReplyEnabled = RaidAutoReplyEnabled;
            raidSetting.AutoReplyMessage = RaidAutoReplyMessage;
            await _eventSettingsService.SaveSettingAsync(raidSetting);
            await PersistEventSoundConfigAsync("raid", raidSetting);

            // Save Cheer settings
            var cheerSetting = await _eventSettingsService.GetSettingAsync("Twitch", "Cheer") ?? new VyralBot.Data.Entities.ChatEventSetting { Platform = "Twitch", EventType = "Cheer" };
            cheerSetting.ShowInChat = ShowCheersInChat;
            cheerSetting.AutoReplyEnabled = CheerAutoReplyEnabled;
            cheerSetting.AutoReplyMessage = CheerAutoReplyMessage;
            await _eventSettingsService.SaveSettingAsync(cheerSetting);
            await PersistEventSoundConfigAsync("cheer", cheerSetting);

            _logger.LogInformation("Twitch chat event settings saved successfully");
            Status = "Settings saved!";
            await Task.Delay(2000);
            Status = "";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save Twitch chat event settings");
            Status = "Failed to save settings";
        }
    }
}
