using Microsoft.Extensions.Logging.Abstractions;
using VyralBot.UI.Services;
using VyralBot.UI.ViewModels;
using Xunit;

namespace VyralBot.UI.Tests;

/// <summary>
/// Wave-13 coverage for the wave-12 per-event TEST buttons on the Alert
/// Settings page: <see cref="AlertSettingsViewModel.TestEventAlertCommand"/>
/// must fire each preview with the EXACT production title ChatViewModel uses
/// (so AlertService's title→toggle gating routes previews like live events),
/// must respect the page toggles WITHOUT a save, and must ignore unknown
/// event types.
///
/// NOT DIRECTLY COVERED (by design): <c>AlertService.TitleToEventMap</c> /
/// <c>IsEventAlertEnabled</c> are private and run inside ShowAlertAsync behind
/// Application.Current.Dispatcher + a real AlertOverlayWindow (WPF window) —
/// the pure title→toggle resolution table is not extractable without app-code
/// changes. These tests pin the extractable half: the payload titles the page
/// sends are exactly the titles that map to toggles in that table.
/// </summary>
public class Wave13AlertSettingsEventTestTests
{
    private sealed class RecordingAlertService : IAlertService
    {
        public sealed record Call(
            string Title,
            string Username,
            string? Message,
            int DurationSeconds,
            bool ApplySettings);

        public List<Call> Calls { get; } = new();

        public Task ShowAlertAsync(
            string title,
            string username,
            string? message = null,
            string? imageUrl = null,
            VyralBot.UI.Views.AlertAnimation animation = VyralBot.UI.Views.AlertAnimation.SlideInFromTop,
            VyralBot.UI.Views.AlertPosition position = VyralBot.UI.Views.AlertPosition.TopCenter,
            int durationSeconds = 5,
            bool applySettings = true)
        {
            Calls.Add(new Call(title, username, message, durationSeconds, applySettings));
            return Task.CompletedTask;
        }

        public Task HideCurrentAlertAsync() => Task.CompletedTask;

        public bool IsAlertVisible => false;

        public AlertSettings? Stored { get; private set; }

        public Task<AlertSettings> GetAlertSettingsAsync()
            => Task.FromResult(Stored ?? new AlertSettings());

        public Task SaveAlertSettingsAsync(AlertSettings settings)
        {
            Stored = settings;
            return Task.CompletedTask;
        }
    }

    private static AlertSettingsViewModel MakeVm(RecordingAlertService alerts)
        => new(NullLogger<AlertSettingsViewModel>.Instance, alerts);

    private static void SetEventToggle(AlertSettingsViewModel vm, string eventType, bool value)
    {
        switch (eventType)
        {
            case "follow": vm.ShowFollowAlerts = value; break;
            case "subscribe": vm.ShowSubscribeAlerts = value; break;
            case "giftsub": vm.ShowGiftSubAlerts = value; break;
            case "raid": vm.ShowRaidAlerts = value; break;
            case "host": vm.ShowHostAlerts = value; break;
            case "cheer": vm.ShowCheerAlerts = value; break;
            default: throw new ArgumentOutOfRangeException(nameof(eventType), eventType, "unknown event type");
        }
    }

    // ==================================================================
    // Production-title mapping (feeds AlertService.TitleToEventMap)
    // ==================================================================

    [Theory]
    [InlineData("follow", "New Follower!")]
    [InlineData("subscribe", "New Subscriber!")]
    [InlineData("giftsub", "Gift Subs!")]
    [InlineData("raid", "Raid!")]
    [InlineData("host", "New Host!")]
    [InlineData("cheer", "Bits!")]
    public async Task TestEventAlert_FiresProductionTitle(string eventType, string expectedTitle)
    {
        var alerts = new RecordingAlertService();
        var vm = MakeVm(alerts);
        vm.AlertDuration = 9; // preview must use the CURRENT page duration

        await vm.TestEventAlertCommand.ExecuteAsync(eventType);

        var call = Assert.Single(alerts.Calls);
        Assert.Equal(expectedTitle, call.Title);          // exact ChatViewModel title
        Assert.Equal("TestUser123", call.Username);
        Assert.NotNull(call.Message);
        Assert.Equal(9, call.DurationSeconds);
        Assert.False(call.ApplySettings);                 // preview ignores persisted settings
        Assert.Contains(expectedTitle, vm.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TestEventAlert_AllSixEvents_FireDistinctProductionTitles()
    {
        var alerts = new RecordingAlertService();
        var vm = MakeVm(alerts);

        foreach (var eventType in new[] { "follow", "subscribe", "giftsub", "raid", "host", "cheer" })
        {
            await vm.TestEventAlertCommand.ExecuteAsync(eventType);
        }

        Assert.Equal(6, alerts.Calls.Count);
        Assert.Equal(
            new[] { "New Follower!", "New Subscriber!", "Gift Subs!", "Raid!", "New Host!", "Bits!" },
            alerts.Calls.Select(c => c.Title));
    }

    // ==================================================================
    // Toggle gating (page state, no save required)
    // ==================================================================

    [Fact]
    public async Task TestEventAlert_MasterToggleOff_SuppressesEverything()
    {
        var alerts = new RecordingAlertService();
        var vm = MakeVm(alerts);
        vm.AlertsEnabled = false;

        await vm.TestEventAlertCommand.ExecuteAsync("follow");

        Assert.Empty(alerts.Calls);
        Assert.Contains("disabled", vm.StatusMessage, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("follow", "New Followers")]
    [InlineData("subscribe", "New Subscribers")]
    [InlineData("giftsub", "Gifted Subscriptions")]
    [InlineData("raid", "Raids")]
    [InlineData("host", "Hosts")]
    [InlineData("cheer", "Bits/Cheers")]
    public async Task TestEventAlert_EventToggleOff_SuppressesOnlyThatEvent(string eventType, string displayName)
    {
        var alerts = new RecordingAlertService();
        var vm = MakeVm(alerts);
        SetEventToggle(vm, eventType, false);

        await vm.TestEventAlertCommand.ExecuteAsync(eventType);

        Assert.Empty(alerts.Calls);
        Assert.Contains(displayName, vm.StatusMessage, StringComparison.Ordinal);
        Assert.Contains("toggled off", vm.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TestEventAlert_OneToggleOff_OtherEventsStillFire()
    {
        var alerts = new RecordingAlertService();
        var vm = MakeVm(alerts);
        vm.ShowRaidAlerts = false;

        await vm.TestEventAlertCommand.ExecuteAsync("raid");   // suppressed...
        Assert.Empty(alerts.Calls);
        Assert.Contains("Raids", vm.StatusMessage, StringComparison.Ordinal);

        vm.StatusMessage = string.Empty;
        await vm.TestEventAlertCommand.ExecuteAsync("cheer");  // ...but cheer still previews
        var call = Assert.Single(alerts.Calls);
        Assert.Equal("Bits!", call.Title);
        Assert.DoesNotContain("toggled off", vm.StatusMessage, StringComparison.Ordinal);
        Assert.Contains("preview shown", vm.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TestEventAlert_ReEnabledToggle_FiresAgain()
    {
        var alerts = new RecordingAlertService();
        var vm = MakeVm(alerts);

        vm.ShowHostAlerts = false;
        await vm.TestEventAlertCommand.ExecuteAsync("host");
        Assert.Empty(alerts.Calls);

        vm.ShowHostAlerts = true;
        await vm.TestEventAlertCommand.ExecuteAsync("host");
        Assert.Equal("New Host!", Assert.Single(alerts.Calls).Title);
    }

    // ==================================================================
    // Robustness — unknown/missing event types must be ignored quietly
    // ==================================================================

    [Theory]
    [InlineData("bogus")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task TestEventAlert_UnknownEventType_NoFireNoThrow(string? eventType)
    {
        var alerts = new RecordingAlertService();
        var vm = MakeVm(alerts);

        await vm.TestEventAlertCommand.ExecuteAsync(eventType);

        Assert.Empty(alerts.Calls);
        Assert.Equal(string.Empty, vm.StatusMessage);
    }
}
