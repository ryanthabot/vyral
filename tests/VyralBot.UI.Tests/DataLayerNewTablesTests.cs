using System.IO;
using Microsoft.Extensions.Logging.Abstractions;
using VyralBot.Data.Entities;
using Xunit;

namespace VyralBot.UI.Tests;

/// <summary>
/// Data-layer round-trip tests for the three new tables (ConnectedChannels,
/// DiscordRoleTierAssignments, ObsSceneCollections) backed by a temp SQLite db.
/// </summary>
public class DataLayerNewTablesTests
{
    private static VyralBot.Data.IDataService CreateService()
    {
        var path = Path.Combine(Path.GetTempPath(), $"vyral-test-{Guid.NewGuid():N}.db");
        return new VyralBot.Data.DapperDataService($"Data Source={path}", NullLogger<VyralBot.Data.DapperDataService>.Instance);
    }

    [Fact]
    public async Task ConnectedChannel_UpsertRoundTrips()
    {
        var svc = CreateService();
        await svc.EnsureDatabaseCreatedAsync();
        var row = new ConnectedChannel { BotId = "bot1", ChannelId = "123", ChannelName = "mod-chat", GuildId = "9", GuildName = "G", Purposes = "Moderation,Support", MonitoringEnabled = true };
        await svc.UpsertConnectedChannelAsync(row);
        var one = Assert.Single(await svc.GetConnectedChannelsAsync("bot1"));
        Assert.Equal("mod-chat", one.ChannelName);
        Assert.Equal("Moderation,Support", one.Purposes);
        Assert.True(one.MonitoringEnabled);
    }

    [Fact]
    public async Task ConnectedChannel_UpsertUpdatesExistingRow()
    {
        var svc = CreateService();
        await svc.EnsureDatabaseCreatedAsync();
        await svc.UpsertConnectedChannelAsync(new ConnectedChannel { BotId = "b", ChannelId = "1", ChannelName = "a", Purposes = "" });
        await svc.UpsertConnectedChannelAsync(new ConnectedChannel { BotId = "b", ChannelId = "1", ChannelName = "a", Purposes = "Logging", MonitoringEnabled = false });
        var one = Assert.Single(await svc.GetConnectedChannelsAsync("b"));
        Assert.Equal("Logging", one.Purposes);
        Assert.False(one.MonitoringEnabled);
        Assert.Equal(1, await svc.CountConnectedChannelsAsync("b"));
    }

    [Fact]
    public async Task RoleTierAssignment_UpsertAndDelete()
    {
        var svc = CreateService();
        await svc.EnsureDatabaseCreatedAsync();
        await svc.UpsertRoleTierAssignmentAsync(new DiscordRoleTierAssignment { BotId = "b", GuildId = "g", DiscordRoleId = "55", RoleName = "Mods", Tier = "SeniorMod" });
        var one = Assert.Single(await svc.GetRoleTierAssignmentsAsync("b"));
        Assert.Equal("SeniorMod", one.Tier);
        await svc.UpsertRoleTierAssignmentAsync(new DiscordRoleTierAssignment { BotId = "b", GuildId = "g", DiscordRoleId = "55", RoleName = "Mods", Tier = "Admin" });
        Assert.Equal("Admin", Assert.Single(await svc.GetRoleTierAssignmentsAsync("b")).Tier);
        await svc.DeleteRoleTierAssignmentAsync("b", "55");
        Assert.Empty(await svc.GetRoleTierAssignmentsAsync("b"));
    }

    [Fact]
    public async Task ObsSceneCollection_SaveInsertsUpdatesDeletes_BytesExact()
    {
        var svc = CreateService();
        await svc.EnsureDatabaseCreatedAsync();
        var bytes = System.Text.Encoding.UTF8.GetBytes("{\"sources\":[]}");
        var saved = await svc.SaveObsSceneCollectionAsync(new ObsSceneCollection { Name = "MyScenes", RawJsonBytes = bytes, SourcePath = @"C:\x.json", SceneCount = 2, SourceCount = 7 });
        Assert.NotEqual(0, saved.Id);
        var fetched = await svc.GetObsSceneCollectionAsync(saved.Id);
        Assert.NotNull(fetched);
        Assert.Equal(bytes, fetched!.RawJsonBytes);
        fetched.SceneCount = 3;
        await svc.SaveObsSceneCollectionAsync(fetched);
        Assert.Equal(3, (await svc.GetObsSceneCollectionAsync(saved.Id))!.SceneCount);
        Assert.Single(await svc.GetObsSceneCollectionsAsync());
        await svc.DeleteObsSceneCollectionAsync(saved.Id);
        Assert.Null(await svc.GetObsSceneCollectionAsync(saved.Id));
    }
}
