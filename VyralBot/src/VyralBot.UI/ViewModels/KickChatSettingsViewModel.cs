using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;
using VyralBot.Core.Services.Chat;

namespace VyralBot.UI.ViewModels;

public partial class KickChatSettingsViewModel : ViewModelBase
{
    private readonly ILogger<KickChatSettingsViewModel> _logger;
    private readonly IChatEventSettingsService _eventSettingsService;

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
    private string _followAutoReplyMessage = "Thanks for the follow {username}! Welcome to the stream!";

    // Subscribe Event Settings
    [ObservableProperty]
    private bool _showSubscribesInChat = true;

    [ObservableProperty]
    private bool _subscribeAutoReplyEnabled = false;

    [ObservableProperty]
    private string _subscribeAutoReplyMessage = "Thanks for subscribing {username}! 🎉";

    // Gift Sub Event Settings
    [ObservableProperty]
    private bool _showGiftSubsInChat = true;

    [ObservableProperty]
    private bool _giftSubAutoReplyEnabled = false;

    [ObservableProperty]
    private string _giftSubAutoReplyMessage = "Thank you {gifter} for gifting {count} subs! 🎁";

    // Host Event Settings
    [ObservableProperty]
    private bool _showHostsInChat = true;

    [ObservableProperty]
    private bool _hostAutoReplyEnabled = false;

    [ObservableProperty]
    private string _hostAutoReplyMessage = "Thanks for the host {hoster}!";

    // Kicks Gifted Event Settings
    [ObservableProperty]
    private bool _showKicksGiftedInChat = true;

    [ObservableProperty]
    private bool _kicksGiftedAutoReplyEnabled = false;

    [ObservableProperty]
    private string _kicksGiftedAutoReplyMessage = "Thanks {sender} for the {amount} kicks!";

    // Reward Redeemed Event Settings
    [ObservableProperty]
    private bool _showRewardsInChat = true;

    [ObservableProperty]
    private bool _rewardAutoReplyEnabled = false;

    [ObservableProperty]
    private string _rewardAutoReplyMessage = "{username} redeemed {reward}!";

    // Stream Status Event Settings
    [ObservableProperty]
    private bool _showStreamStatusInChat = true;

    public KickChatSettingsViewModel(
        ILogger<KickChatSettingsViewModel> logger,
        IChatEventSettingsService eventSettingsService,
        VyralBot.Data.IDataService? dataService = null)
    {
        _logger = logger;
        _eventSettingsService = eventSettingsService;
        _dataService = dataService
            ?? App.ServiceProvider?.GetRequiredService<VyralBot.Data.IDataService>();
    }

    private readonly VyralBot.Data.IDataService? _dataService;
    private bool _isInitialized;

    public async Task InitializeAsync()
    {
        try
        {
            _logger.LogInformation("Loading Kick chat event settings...");

            // Load the chat routing + auto-start flags from the platform row
            if (_dataService != null)
            {
                var platform = await _dataService.GetPlatformByNameAsync("Kick");
                if (platform != null)
                {
                    _isInitialized = false;
                    IsChatEnabled = platform.KickChatEnabled;
                    AutoStartChat = platform.KickChatAutoStart;
                    _isInitialized = true;
                }
                else
                {
                    _isInitialized = true;
                }
            }
            else
            {
                _isInitialized = true;
            }

            // Load Follow settings
            var followSettings = await _eventSettingsService.GetSettingAsync("Kick", "Follow");
            if (followSettings != null)
            {
                ShowFollowsInChat = followSettings.ShowInChat;
                FollowAutoReplyEnabled = followSettings.AutoReplyEnabled;
                FollowAutoReplyMessage = followSettings.AutoReplyMessage ?? "Thanks for the follow {username}! Welcome to the stream!";
            }

            // Load Subscribe settings
            var subscribeSettings = await _eventSettingsService.GetSettingAsync("Kick", "Subscribe");
            if (subscribeSettings != null)
            {
                ShowSubscribesInChat = subscribeSettings.ShowInChat;
                SubscribeAutoReplyEnabled = subscribeSettings.AutoReplyEnabled;
                SubscribeAutoReplyMessage = subscribeSettings.AutoReplyMessage ?? "Thanks for subscribing {username}! 🎉";
            }

            // Load Gift Sub settings
            var giftSubSettings = await _eventSettingsService.GetSettingAsync("Kick", "GiftSub");
            if (giftSubSettings != null)
            {
                ShowGiftSubsInChat = giftSubSettings.ShowInChat;
                GiftSubAutoReplyEnabled = giftSubSettings.AutoReplyEnabled;
                GiftSubAutoReplyMessage = giftSubSettings.AutoReplyMessage ?? "Thank you {gifter} for gifting {count} subs! 🎁";
            }

            // Load Host settings
            var hostSettings = await _eventSettingsService.GetSettingAsync("Kick", "Host");
            if (hostSettings != null)
            {
                ShowHostsInChat = hostSettings.ShowInChat;
                HostAutoReplyEnabled = hostSettings.AutoReplyEnabled;
                HostAutoReplyMessage = hostSettings.AutoReplyMessage ?? "Thanks for the host {hoster}!";
            }

            // Load Kicks Gifted settings
            var kicksGiftedSettings = await _eventSettingsService.GetSettingAsync("Kick", "KicksGifted");
            if (kicksGiftedSettings != null)
            {
                ShowKicksGiftedInChat = kicksGiftedSettings.ShowInChat;
                KicksGiftedAutoReplyEnabled = kicksGiftedSettings.AutoReplyEnabled;
                KicksGiftedAutoReplyMessage = kicksGiftedSettings.AutoReplyMessage ?? "Thanks {sender} for the {amount} kicks!";
            }

            // Load Reward Redeemed settings
            var rewardSettings = await _eventSettingsService.GetSettingAsync("Kick", "RewardRedeemed");
            if (rewardSettings != null)
            {
                ShowRewardsInChat = rewardSettings.ShowInChat;
                RewardAutoReplyEnabled = rewardSettings.AutoReplyEnabled;
                RewardAutoReplyMessage = rewardSettings.AutoReplyMessage ?? "{username} redeemed {reward}!";
            }

            // Load Stream Status settings
            var streamStatusSettings = await _eventSettingsService.GetSettingAsync("Kick", "StreamStatus");
            if (streamStatusSettings != null)
            {
                ShowStreamStatusInChat = streamStatusSettings.ShowInChat;
            }

            _logger.LogInformation("Kick chat event settings loaded successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load Kick chat event settings");
            Status = "Failed to load settings";
        }
    }

    partial void OnIsChatEnabledChanged(bool value)
    {
        if (_isInitialized && _dataService != null)
        {
            _ = PersistKickFlagsAsync(chatEnabled: value, autoStart: null);
        }
    }

    partial void OnAutoStartChatChanged(bool value)
    {
        if (_isInitialized && _dataService != null)
        {
            _ = PersistKickFlagsAsync(chatEnabled: null, autoStart: value);
        }
    }

    /// <summary>
    /// Persists the KickChatEnabled / KickChatAutoStart platform flags (the auto-start
    /// flag is consumed at app launch by MainViewModel's startup auto-connect).
    /// </summary>
    private async Task PersistKickFlagsAsync(bool? chatEnabled, bool? autoStart)
    {
        try
        {
            var platform = await _dataService!.GetPlatformByNameAsync("Kick");
            if (platform != null)
            {
                if (chatEnabled.HasValue) platform.KickChatEnabled = chatEnabled.Value;
                if (autoStart.HasValue) platform.KickChatAutoStart = autoStart.Value;
                platform.UpdatedAt = DateTime.UtcNow;
                await _dataService.SavePlatformAsync(platform);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error persisting Kick chat flags");
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
        if (_dataService == null)
        {
            return;
        }

        try
        {
            if (string.IsNullOrWhiteSpace(setting.SoundFile))
            {
                return;
            }

            await _dataService.SetConfigValueAsync($"EventSound_Kick_{playbackEventType}", setting.SoundEnabled ? setting.SoundFile : string.Empty);
            await _dataService.SetConfigValueAsync(
                $"EventVolume_Kick_{playbackEventType}",
                (Math.Clamp(setting.SoundVolume, 0, 100) / 100.0).ToString("F2", System.Globalization.CultureInfo.InvariantCulture));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist the event sound config for Kick/{EventType}", playbackEventType);
        }
    }

    [RelayCommand]
    private async Task SaveSettingsAsync()
    {
        try
        {
            _logger.LogInformation("Saving Kick chat event settings...");

            // Save Follow settings
            var followSetting = await _eventSettingsService.GetSettingAsync("Kick", "Follow") ?? new VyralBot.Data.Entities.ChatEventSetting { Platform = "Kick", EventType = "Follow" };
            followSetting.ShowInChat = ShowFollowsInChat;
            followSetting.AutoReplyEnabled = FollowAutoReplyEnabled;
            followSetting.AutoReplyMessage = FollowAutoReplyMessage;
            await _eventSettingsService.SaveSettingAsync(followSetting);
            await PersistEventSoundConfigAsync("follow", followSetting);

            // Save Subscribe settings
            var subscribeSetting = await _eventSettingsService.GetSettingAsync("Kick", "Subscribe") ?? new VyralBot.Data.Entities.ChatEventSetting { Platform = "Kick", EventType = "Subscribe" };
            subscribeSetting.ShowInChat = ShowSubscribesInChat;
            subscribeSetting.AutoReplyEnabled = SubscribeAutoReplyEnabled;
            subscribeSetting.AutoReplyMessage = SubscribeAutoReplyMessage;
            await _eventSettingsService.SaveSettingAsync(subscribeSetting);
            await PersistEventSoundConfigAsync("subscribe", subscribeSetting);

            // Save Gift Sub settings
            var giftSubSetting = await _eventSettingsService.GetSettingAsync("Kick", "GiftSub") ?? new VyralBot.Data.Entities.ChatEventSetting { Platform = "Kick", EventType = "GiftSub" };
            giftSubSetting.ShowInChat = ShowGiftSubsInChat;
            giftSubSetting.AutoReplyEnabled = GiftSubAutoReplyEnabled;
            giftSubSetting.AutoReplyMessage = GiftSubAutoReplyMessage;
            await _eventSettingsService.SaveSettingAsync(giftSubSetting);
            await PersistEventSoundConfigAsync("giftsub", giftSubSetting);

            // Save Host settings
            var hostSetting = await _eventSettingsService.GetSettingAsync("Kick", "Host") ?? new VyralBot.Data.Entities.ChatEventSetting { Platform = "Kick", EventType = "Host" };
            hostSetting.ShowInChat = ShowHostsInChat;
            hostSetting.AutoReplyEnabled = HostAutoReplyEnabled;
            hostSetting.AutoReplyMessage = HostAutoReplyMessage;
            await _eventSettingsService.SaveSettingAsync(hostSetting);
            await PersistEventSoundConfigAsync("host", hostSetting);

            // Save Kicks Gifted settings
            var kicksGiftedSetting = await _eventSettingsService.GetSettingAsync("Kick", "KicksGifted") ?? new VyralBot.Data.Entities.ChatEventSetting { Platform = "Kick", EventType = "KicksGifted" };
            kicksGiftedSetting.ShowInChat = ShowKicksGiftedInChat;
            kicksGiftedSetting.AutoReplyEnabled = KicksGiftedAutoReplyEnabled;
            kicksGiftedSetting.AutoReplyMessage = KicksGiftedAutoReplyMessage;
            await _eventSettingsService.SaveSettingAsync(kicksGiftedSetting);

            // Save Reward Redeemed settings
            var rewardSetting = await _eventSettingsService.GetSettingAsync("Kick", "RewardRedeemed") ?? new VyralBot.Data.Entities.ChatEventSetting { Platform = "Kick", EventType = "RewardRedeemed" };
            rewardSetting.ShowInChat = ShowRewardsInChat;
            rewardSetting.AutoReplyEnabled = RewardAutoReplyEnabled;
            rewardSetting.AutoReplyMessage = RewardAutoReplyMessage;
            await _eventSettingsService.SaveSettingAsync(rewardSetting);

            // Save Stream Status settings
            var streamStatusSetting = await _eventSettingsService.GetSettingAsync("Kick", "StreamStatus") ?? new VyralBot.Data.Entities.ChatEventSetting { Platform = "Kick", EventType = "StreamStatus" };
            streamStatusSetting.ShowInChat = ShowStreamStatusInChat;
            streamStatusSetting.AutoReplyEnabled = false;
            streamStatusSetting.AutoReplyMessage = null;
            await _eventSettingsService.SaveSettingAsync(streamStatusSetting);

            _logger.LogInformation("Kick chat event settings saved successfully");
            Status = "Settings saved!";
            await Task.Delay(2000);
            Status = "";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save Kick chat event settings");
            Status = "Failed to save settings";
        }
    }
}
