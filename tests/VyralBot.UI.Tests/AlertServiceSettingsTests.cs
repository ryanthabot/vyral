using Microsoft.Extensions.Logging.Abstractions;
using VyralBot.UI.Services;
using Xunit;

namespace VyralBot.UI.Tests;

/// <summary>
/// Marathon wave-2 AlertService persistence tolerance (config key
/// "AlertSettings"): a missing or corrupt settings blob must fall back to
/// the safe defaults, and saves must round-trip.
///
/// NOT COVERED HERE (by design): the bounded pending-alert FIFO (cap 10,
/// drop-oldest) and the window-close queue drain. That logic lives inside
/// ShowAlertAsync behind Application.Current.Dispatcher and a REAL
/// AlertOverlayWindow (WPF Window), so exercising it requires a live WPF
/// Application and on-screen windows — not possible in this unit suite.
/// The queue bound is enforced by the MaxPendingAlerts drop-oldest loop in
/// AlertService.ShowAlertAsync; verifying it needs an integration harness.
/// </summary>
public class AlertServiceSettingsTests
{
    private static AlertService MakeService(Wave2TestDataService data)
        => new(NullLogger<AlertService>.Instance, data);

    [Fact]
    public async Task GetAlertSettings_NoStoredValue_ReturnsDefaults()
    {
        var settings = await MakeService(new Wave2TestDataService()).GetAlertSettingsAsync();

        Assert.True(settings.AlertsEnabled);
        Assert.Equal(5, settings.DurationSeconds);
        Assert.Equal(nameof(Views.AlertAnimation.SlideInFromTop), settings.Animation);
        Assert.Equal(nameof(Views.AlertPosition.TopCenter), settings.Position);
        Assert.True(settings.FollowAlertsEnabled);
        Assert.True(settings.RaidAlertsEnabled);
    }

    [Fact]
    public async Task GetAlertSettings_CorruptJson_ReturnsDefaults()
    {
        var data = new Wave2TestDataService();
        data.ConfigValues["AlertSettings"] = "{ not alert settings json";

        var settings = await MakeService(data).GetAlertSettingsAsync();

        // A corrupt blob must never brick the alert pipeline.
        Assert.True(settings.AlertsEnabled);
        Assert.Equal(5, settings.DurationSeconds);
    }

    [Fact]
    public async Task SaveAlertSettings_RoundTrips_ThroughTheConfigStore()
    {
        var data = new Wave2TestDataService();
        var service = MakeService(data);
        var saved = new AlertSettings
        {
            AlertsEnabled = true,
            DurationSeconds = 10,
            Animation = nameof(Views.AlertAnimation.FadeIn),
            Position = nameof(Views.AlertPosition.BottomRight),
            CheerAlertsEnabled = false
        };

        await service.SaveAlertSettingsAsync(saved);
        var loaded = await service.GetAlertSettingsAsync();

        Assert.Equal(10, loaded.DurationSeconds);
        Assert.Equal(nameof(Views.AlertAnimation.FadeIn), loaded.Animation);
        Assert.Equal(nameof(Views.AlertPosition.BottomRight), loaded.Position);
        Assert.False(loaded.CheerAlertsEnabled);
    }
}
