using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using VyralBot.Integrations.Testing;

namespace VyralBot.UI.ViewModels;

/// <summary>
/// One row of the "last fired" log list. <see cref="Failed"/> rows render in
/// the error brush so a rejected publish is immediately visible.
/// </summary>
/// <param name="Platform">Platform name ("Twitch", "Kick", "YouTube", "Discord").</param>
/// <param name="EventName">Short event label ("Cheer", "Super Chat", ...).</param>
/// <param name="Detail">Username the test fired for.</param>
/// <param name="Time">Local HH:mm:ss stamp of the fire.</param>
/// <param name="Failed">True when the publish threw.</param>
public sealed record FiredEventEntry(string Platform, string EventName, string Detail, string Time, bool Failed);

/// <summary>
/// Streamer/developer tool for verifying the event pipeline without live
/// traffic. Every button publishes the REAL event record through the EventBus
/// (construction lives in <see cref="TestEventPublisher"/>, shared with the
/// token-gated HTTP control API), so the full pipeline runs exactly as it
/// would for a live viewer: alerts, sounds, chat lines, statistics rows,
/// currency bonuses — and matching workflows REALLY execute.
/// </summary>
public partial class EventTestingViewModel : ViewModelBase
{
    /// <summary>Cap of the recent-fires log list (newest first).</summary>
    private const int RecentFireCapacity = 10;

    private readonly ILogger<EventTestingViewModel> _logger;
    private readonly TestEventPublisher _testEventPublisher;

    [ObservableProperty]
    private string _testUsername = "TestUser123";

    [ObservableProperty]
    private string _testMessage = "This is a test message";

    /// <summary>Status line shown under the safety header; updated on every fire.</summary>
    [ObservableProperty]
    private string _lastFireStatus = "Ready - click any button to fire a real event through the live pipeline.";

    /// <summary>True when the last publish threw; drives the status line color.</summary>
    [ObservableProperty]
    private bool _lastFireFailed;

    /// <summary>Last-fired log (newest first, capped at 10 entries).</summary>
    public ObservableCollection<FiredEventEntry> RecentFires { get; } = new();

    public EventTestingViewModel(
        ILogger<EventTestingViewModel> logger,
        TestEventPublisher testEventPublisher)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _testEventPublisher = testEventPublisher ?? throw new ArgumentNullException(nameof(testEventPublisher));
    }

    private async Task TriggerAsync(string platform, string eventType, string eventName)
    {
        var time = DateTime.Now.ToString("HH:mm:ss");
        try
        {
            await _testEventPublisher.PublishTestEventAsync(eventType, TestUsername, TestMessage);
            RecordFire(platform, eventName, time, failed: false, error: null);
            _logger.LogInformation("Test event fired: {Platform} {EventType}", platform, eventType);
        }
        catch (Exception ex)
        {
            RecordFire(platform, eventName, time, failed: true, error: ex.Message);
            _logger.LogError(ex, "Failed to trigger {EventType} test event", eventType);
        }
    }

    private void RecordFire(string platform, string eventName, string time, bool failed, string? error)
    {
        RecentFires.Insert(0, new FiredEventEntry(platform, eventName, TestUsername, time, failed));
        while (RecentFires.Count > RecentFireCapacity)
        {
            RecentFires.RemoveAt(RecentFires.Count - 1);
        }

        LastFireFailed = failed;
        LastFireStatus = failed
            ? $"FAILED {platform} {eventName} at {time}: {error ?? "unknown error"}"
            : $"FIRED {platform} {eventName} for {TestUsername} at {time} - alert, sound, statistics, currency and workflow handlers ran for real.";
    }

    #region Twitch Event Commands

    [RelayCommand]
    private Task TriggerTwitchFollow() => TriggerAsync("Twitch", TestEventPublisher.TwitchFollow, "Follow");

    [RelayCommand]
    private Task TriggerTwitchSubscribe() => TriggerAsync("Twitch", TestEventPublisher.TwitchSubscribe, "Subscribe");

    [RelayCommand]
    private Task TriggerTwitchResub() => TriggerAsync("Twitch", TestEventPublisher.TwitchResub, "Resub");

    [RelayCommand]
    private Task TriggerTwitchGiftSub() => TriggerAsync("Twitch", TestEventPublisher.TwitchGiftSub, "Gift Sub");

    [RelayCommand]
    private Task TriggerTwitchRaid() => TriggerAsync("Twitch", TestEventPublisher.TwitchRaid, "Raid");

    [RelayCommand]
    private Task TriggerTwitchCheer() => TriggerAsync("Twitch", TestEventPublisher.TwitchCheer, "Cheer (100 bits)");

    [RelayCommand]
    private Task TriggerTwitchChannelPointsRedeem() =>
        TriggerAsync("Twitch", TestEventPublisher.TwitchChannelPointsRedeem, "Channel Points Redeem");

    #endregion

    #region Kick Event Commands

    [RelayCommand]
    private Task TriggerKickFollow() => TriggerAsync("Kick", TestEventPublisher.KickFollow, "Follow");

    [RelayCommand]
    private Task TriggerKickSubscribe() => TriggerAsync("Kick", TestEventPublisher.KickSubscribe, "Subscribe");

    [RelayCommand]
    private Task TriggerKickGiftSub() => TriggerAsync("Kick", TestEventPublisher.KickGiftSub, "Gift Sub");

    [RelayCommand]
    private Task TriggerKickHost() => TriggerAsync("Kick", TestEventPublisher.KickHost, "Host");

    [RelayCommand]
    private Task TriggerKickKicksGifted() => TriggerAsync("Kick", TestEventPublisher.KickKicksGifted, "Kicks Gifted");

    [RelayCommand]
    private Task TriggerKickRewardRedeemed() =>
        TriggerAsync("Kick", TestEventPublisher.KickRewardRedeemed, "Reward Redeemed");

    #endregion

    #region YouTube Event Commands

    [RelayCommand]
    private Task TriggerYouTubeSuperChat() =>
        TriggerAsync("YouTube", TestEventPublisher.YouTubeSuperChat, "Super Chat ($5.00)");

    [RelayCommand]
    private Task TriggerYouTubeMembership() =>
        TriggerAsync("YouTube", TestEventPublisher.YouTubeMembership, "Membership");

    #endregion

    #region Discord Event Commands

    [RelayCommand]
    private Task TriggerDiscordMemberJoined() =>
        TriggerAsync("Discord", TestEventPublisher.DiscordMemberJoined, "Member Joined");

    [RelayCommand]
    private Task TriggerDiscordBoost() =>
        TriggerAsync("Discord", TestEventPublisher.DiscordBoost, "Server Boost");

    #endregion

    [RelayCommand]
    private void ClearLog()
    {
        RecentFires.Clear();
        LastFireFailed = false;
        LastFireStatus = "Log cleared.";
    }
}
