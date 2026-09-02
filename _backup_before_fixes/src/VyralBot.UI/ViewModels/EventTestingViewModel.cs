using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using VyralBot.Core.Services.EventBus;
using VyralBot.Integrations.Kick.Events;
using VyralBot.Integrations.Twitch.Events;

namespace VyralBot.UI.ViewModels;

/// <summary>
/// Developer tool for testing chat events manually.
/// Publishes test events to the EventBus to verify handlers, UI, and auto-replies work correctly.
/// </summary>
public partial class EventTestingViewModel : ViewModelBase
{
    private readonly ILogger<EventTestingViewModel> _logger;
    private readonly IEventBus _eventBus;

    [ObservableProperty]
    private string _testUsername = "TestUser123";

    [ObservableProperty]
    private string _testMessage = "This is a test message";

    [ObservableProperty]
    private string _outputLog = string.Empty;

    public EventTestingViewModel(
        ILogger<EventTestingViewModel> logger,
        IEventBus eventBus)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        
        AddLog("Event Testing Tool Initialized");
        AddLog("Click any button to trigger a test event");
    }

    private void AddLog(string message)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss");
        OutputLog += $"[{timestamp}] {message}\n";
    }

    #region Kick Event Commands

    [RelayCommand]
    private async Task TriggerKickFollow()
    {
        try
        {
            AddLog($"🎯 Triggering Kick Follow Event - Username: {TestUsername}");
            
            var evt = new KickFollowEvent
            {
                Source = "Kick",
                Username = TestUsername,
                UserId = "12345",
                FollowerCount = 42,
                Timestamp = DateTime.UtcNow
            };
            
            await _eventBus.PublishAsync(evt);
            AddLog("✅ Kick Follow Event Published");
        }
        catch (Exception ex)
        {
            AddLog($"❌ Error: {ex.Message}");
            _logger.LogError(ex, "Failed to trigger Kick follow test event");
        }
    }

    [RelayCommand]
    private async Task TriggerKickSubscribe()
    {
        try
        {
            AddLog($"🎯 Triggering Kick Subscribe Event - Username: {TestUsername}");
            
            var evt = new KickSubscribeEvent
            {
                Source = "Kick",
                Username = TestUsername,
                UserId = "12345",
                MonthsSubscribed = 3,
                Message = TestMessage,
                Timestamp = DateTime.UtcNow
            };
            
            await _eventBus.PublishAsync(evt);
            AddLog("✅ Kick Subscribe Event Published");
        }
        catch (Exception ex)
        {
            AddLog($"❌ Error: {ex.Message}");
            _logger.LogError(ex, "Failed to trigger Kick subscribe test event");
        }
    }

    [RelayCommand]
    private async Task TriggerKickGiftSub()
    {
        try
        {
            AddLog($"🎯 Triggering Kick Gift Sub Event - Gifter: {TestUsername}");
            
            var evt = new KickGiftSubEvent
            {
                Source = "Kick",
                GifterUsername = TestUsername,
                GifterUserId = "12345",
                Count = 5,
                RecipientUsernames = new List<string> { "Recipient1", "Recipient2", "Recipient3", "Recipient4", "Recipient5" },
                Timestamp = DateTime.UtcNow
            };
            
            await _eventBus.PublishAsync(evt);
            AddLog("✅ Kick Gift Sub Event Published");
        }
        catch (Exception ex)
        {
            AddLog($"❌ Error: {ex.Message}");
            _logger.LogError(ex, "Failed to trigger Kick gift sub test event");
        }
    }

    [RelayCommand]
    private async Task TriggerKickHost()
    {
        try
        {
            AddLog($"🎯 Triggering Kick Host Event - Hoster: {TestUsername}");
            
            var evt = new KickHostEvent
            {
                Source = "Kick",
                HostUsername = TestUsername,
                HostUserId = "12345",
                ViewerCount = 150,
                HostedAt = DateTime.UtcNow,
                Timestamp = DateTime.UtcNow
            };
            
            await _eventBus.PublishAsync(evt);
            AddLog("✅ Kick Host Event Published");
        }
        catch (Exception ex)
        {
            AddLog($"❌ Error: {ex.Message}");
            _logger.LogError(ex, "Failed to trigger Kick host test event");
        }
    }

    [RelayCommand]
    private async Task TriggerKickKicksGifted()
    {
        try
        {
            AddLog($"🎯 Triggering Kick Kicks Gifted Event - Sender: {TestUsername}");
            
            var evt = new KickKicksGiftedEvent
            {
                Source = "Kick",
                SenderUsername = TestUsername,
                SenderUserId = "12345",
                Amount = 1000,
                Message = TestMessage,
                Timestamp = DateTime.UtcNow
            };
            
            await _eventBus.PublishAsync(evt);
            AddLog("✅ Kick Kicks Gifted Event Published");
        }
        catch (Exception ex)
        {
            AddLog($"❌ Error: {ex.Message}");
            _logger.LogError(ex, "Failed to trigger Kick kicks gifted test event");
        }
    }

    [RelayCommand]
    private async Task TriggerKickRewardRedeemed()
    {
        try
        {
            AddLog($"🎯 Triggering Kick Reward Redeemed Event - Username: {TestUsername}");
            
            var evt = new KickRewardRedeemedEvent
            {
                Source = "Kick",
                Username = TestUsername,
                UserId = "12345",
                RewardName = "Test Reward",
                Cost = 500,
                UserInput = TestMessage,
                Timestamp = DateTime.UtcNow
            };
            
            await _eventBus.PublishAsync(evt);
            AddLog("✅ Kick Reward Redeemed Event Published");
        }
        catch (Exception ex)
        {
            AddLog($"❌ Error: {ex.Message}");
            _logger.LogError(ex, "Failed to trigger Kick reward redeemed test event");
        }
    }

    #endregion

    #region Twitch Event Commands

    [RelayCommand]
    private async Task TriggerTwitchFollow()
    {
        try
        {
            AddLog($"🎯 Triggering Twitch Follow Event - Username: {TestUsername}");
            
            var evt = new TwitchFollowEvent
            {
                Source = "Twitch",
                Username = TestUsername,
                UserId = "12345",
                DisplayName = TestUsername,
                Timestamp = DateTime.UtcNow
            };
            
            await _eventBus.PublishAsync(evt);
            AddLog("✅ Twitch Follow Event Published");
        }
        catch (Exception ex)
        {
            AddLog($"❌ Error: {ex.Message}");
            _logger.LogError(ex, "Failed to trigger Twitch follow test event");
        }
    }

    [RelayCommand]
    private async Task TriggerTwitchSubscribe()
    {
        try
        {
            AddLog($"🎯 Triggering Twitch Subscribe Event - Username: {TestUsername}");
            
            var evt = new TwitchSubscribeEvent
            {
                Source = "Twitch",
                Username = TestUsername,
                UserId = "12345",
                DisplayName = TestUsername,
                Tier = "1000",
                IsGift = false,
                MonthsTotal = 1,
                StreakMonths = 1,
                Timestamp = DateTime.UtcNow
            };
            
            await _eventBus.PublishAsync(evt);
            AddLog("✅ Twitch Subscribe Event Published");
        }
        catch (Exception ex)
        {
            AddLog($"❌ Error: {ex.Message}");
            _logger.LogError(ex, "Failed to trigger Twitch subscribe test event");
        }
    }

    [RelayCommand]
    private async Task TriggerTwitchResub()
    {
        try
        {
            AddLog($"🎯 Triggering Twitch Resub Event - Username: {TestUsername}");
            
            var evt = new TwitchResubEvent
            {
                Source = "Twitch",
                Username = TestUsername,
                UserId = "12345",
                Tier = "1000",
                CumulativeMonths = 6,
                StreakMonths = 4,
                DurationMonths = 1,
                Message = TestMessage,
                ResubscribedAt = DateTime.UtcNow,
                Timestamp = DateTime.UtcNow
            };
            
            await _eventBus.PublishAsync(evt);
            AddLog("✅ Twitch Resub Event Published");
        }
        catch (Exception ex)
        {
            AddLog($"❌ Error: {ex.Message}");
            _logger.LogError(ex, "Failed to trigger Twitch resub test event");
        }
    }

    [RelayCommand]
    private async Task TriggerTwitchGiftSub()
    {
        try
        {
            AddLog($"🎯 Triggering Twitch Gift Sub Event - Gifter: {TestUsername}");
            
            var evt = new TwitchGiftSubEvent
            {
                Source = "Twitch",
                GifterUsername = TestUsername,
                GifterUserId = "12345",
                Tier = "1000",
                Total = 5,
                CumulativeTotal = 25,
                IsAnonymous = false,
                GiftedAt = DateTime.UtcNow,
                Timestamp = DateTime.UtcNow
            };
            
            await _eventBus.PublishAsync(evt);
            AddLog("✅ Twitch Gift Sub Event Published");
        }
        catch (Exception ex)
        {
            AddLog($"❌ Error: {ex.Message}");
            _logger.LogError(ex, "Failed to trigger Twitch gift sub test event");
        }
    }

    [RelayCommand]
    private async Task TriggerTwitchRaid()
    {
        try
        {
            AddLog($"🎯 Triggering Twitch Raid Event - Raider: {TestUsername}");
            
            var evt = new TwitchRaidEvent
            {
                Source = "Twitch",
                FromBroadcasterUsername = TestUsername,
                FromBroadcasterUserId = "12345",
                ToBroadcasterUsername = "YourChannel",
                ToBroadcasterUserId = "67890",
                Viewers = 200,
                RaidedAt = DateTime.UtcNow,
                Timestamp = DateTime.UtcNow
            };
            
            await _eventBus.PublishAsync(evt);
            AddLog("✅ Twitch Raid Event Published");
        }
        catch (Exception ex)
        {
            AddLog($"❌ Error: {ex.Message}");
            _logger.LogError(ex, "Failed to trigger Twitch raid test event");
        }
    }

    [RelayCommand]
    private async Task TriggerTwitchCheer()
    {
        try
        {
            AddLog($"🎯 Triggering Twitch Cheer Event - Username: {TestUsername}");
            
            var evt = new TwitchCheerEvent
            {
                Source = "Twitch",
                Username = TestUsername,
                UserId = "12345",
                Bits = 100,
                Message = TestMessage,
                IsAnonymous = false,
                CheeredAt = DateTime.UtcNow,
                Timestamp = DateTime.UtcNow
            };
            
            await _eventBus.PublishAsync(evt);
            AddLog("✅ Twitch Cheer Event Published");
        }
        catch (Exception ex)
        {
            AddLog($"❌ Error: {ex.Message}");
            _logger.LogError(ex, "Failed to trigger Twitch cheer test event");
        }
    }

    #endregion

    [RelayCommand]
    private void ClearLog()
    {
        OutputLog = string.Empty;
        AddLog("Log cleared");
    }
}
