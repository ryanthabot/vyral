using System.IO;
using Microsoft.Extensions.Logging.Abstractions;
using VyralBot.Core.Services.Channels;
using VyralBot.Data.Entities;
using Xunit;

namespace VyralBot.UI.Tests;

/// <summary>
/// Tests for the channel-purpose service (ChannelPurposes + ConnectedChannelService),
/// backed by a real DapperDataService over a temp SQLite db.
/// </summary>
public class ConnectedChannelServiceTests
{
    private static VyralBot.Data.IDataService CreateDataService()
    {
        var path = Path.Combine(Path.GetTempPath(), $"vyral-test-{Guid.NewGuid():N}.db");
        return new VyralBot.Data.DapperDataService($"Data Source={path}", NullLogger<VyralBot.Data.DapperDataService>.Instance);
    }

    private static ConnectedChannel MakeChannel(string channelId, string purposes, bool monitoring = true) =>
        new() { BotId = "bot1", ChannelId = channelId, ChannelName = $"chan-{channelId}", Purposes = purposes, MonitoringEnabled = monitoring };

    [Fact]
    public void Parse_And_ToCsv_RoundTrip_IgnoringCaseAndJunk()
    {
        var parsed = ChannelPurposes.Parse("moderation, junk, Logging");
        Assert.Equal(new List<string> { "Moderation", "Logging" }, parsed);
        Assert.Equal("Moderation,Logging", ChannelPurposes.ToCsv(parsed));
    }

    [Theory]
    [InlineData("Moderation,Support", "Moderation", true)]
    [InlineData("support", "Support", true)]
    [InlineData("Support", "Admin", false)]
    [InlineData(null, "Support", false)]
    public void HasPurpose_MatchesCaseInsensitive(string? csv, string purpose, bool expected)
    {
        Assert.Equal(expected, ChannelPurposes.HasPurpose(csv, purpose));
    }

    [Fact]
    public async Task AcceptsCommands_NoChannelsConfigured_LegacyAllowAll()
    {
        var data = CreateDataService();
        await data.EnsureDatabaseCreatedAsync();
        var svc = new ConnectedChannelService(data);
        Assert.True(await svc.AcceptsCommandsAsync("any-bot", "999"));
    }

    [Fact]
    public async Task AcceptsCommands_TaggedChannelOnly()
    {
        var data = CreateDataService();
        await data.EnsureDatabaseCreatedAsync();
        await data.UpsertConnectedChannelAsync(MakeChannel("10", "Moderation"));
        await data.UpsertConnectedChannelAsync(MakeChannel("11", "Logging"));
        await data.UpsertConnectedChannelAsync(MakeChannel("12", "Moderation", monitoring: false));
        var svc = new ConnectedChannelService(data);

        Assert.True(await svc.AcceptsCommandsAsync("bot1", "10"));
        Assert.False(await svc.AcceptsCommandsAsync("bot1", "11"));
        Assert.False(await svc.AcceptsCommandsAsync("bot1", "12"));
        Assert.False(await svc.AcceptsCommandsAsync("bot1", "99"));
    }

    [Fact]
    public async Task IsMonitored_And_GetByPurpose_HonorMonitoringDisabled()
    {
        var data = CreateDataService();
        await data.EnsureDatabaseCreatedAsync();
        await data.UpsertConnectedChannelAsync(MakeChannel("20", "Support"));
        await data.UpsertConnectedChannelAsync(MakeChannel("21", "Support", monitoring: false));
        var svc = new ConnectedChannelService(data);

        Assert.True(await svc.IsMonitoredAsync("bot1", "20"));
        Assert.False(await svc.IsMonitoredAsync("bot1", "21"));
        Assert.False(await svc.IsMonitoredAsync("bot1", "99"));

        // purpose argument is expected to be a canonical tag (case-insensitivity applies to the CSV side only)
        var support = await svc.GetByPurposeAsync("bot1", ChannelPurposes.Support);
        var ids = support.Select(c => c.ChannelId).ToList();
        Assert.Equal(new List<string> { "20" }, ids);
    }
}
