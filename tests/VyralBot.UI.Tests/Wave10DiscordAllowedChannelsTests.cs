using Microsoft.Extensions.Logging.Abstractions;
using VyralBot.Core.Services.EventBus;
using VyralBot.Integrations.Discord;
using Xunit;

namespace VyralBot.UI.Tests;

/// <summary>
/// Wave-10 coverage for the wave-8 per-bot DISCORD ALLOWED CHANNELS command
/// permissions: the <see cref="DiscordBotAllowedChannels"/> record's pure
/// JSON + membership helpers (an EMPTY list = every channel allowed, so
/// legacy setups keep working; picked lists gate ordinally with trimming;
/// broken rows fall back to unrestricted instead of throwing) plus the real
/// manager persistence round-trip under the per-bot
/// <c>DiscordBot.{botId}.AllowedChannels</c> Configs key (the same key
/// CommandService enforces live on the Core side).
/// </summary>
public class Wave10DiscordAllowedChannelsTests : IDisposable
{
    private readonly List<DiscordBotManager> _managers = new();

    private DiscordBotManager CreateManager(Wave2TestDataService data)
    {
        var manager = new DiscordBotManager(data, new NoOpBus(), NullLogger<DiscordBotManager>.Instance, NullLoggerFactory.Instance);
        _managers.Add(manager); // disposed with the test (the manager owns a 30s timer)
        return manager;
    }

    public void Dispose()
    {
        foreach (var manager in _managers)
        {
            manager.Dispose();
        }
    }

    // ==================================================================
    // Record: default (unrestricted) semantics
    // ==================================================================

    [Fact]
    public void Default_IsUnrestricted_AndAllowsEverything()
    {
        var allowed = DiscordBotAllowedChannels.Default;

        Assert.True(allowed.IsUnrestricted);
        Assert.True(allowed.Allows("999"));      // any channel id passes
        Assert.True(allowed.Allows(null));       // even an unknown/DM id
        Assert.True(allowed.Allows("  "));       // never locks a bot out
        Assert.False(allowed.IsPicked("999"));   // but the detail page shows nothing checked
    }

    [Fact]
    public void EmptyList_AlsoMeansEveryChannel()
    {
        var allowed = new DiscordBotAllowedChannels([]);

        Assert.True(allowed.IsUnrestricted);
        Assert.True(allowed.Allows("123"));
    }

    [Fact]
    public void ConfigKey_IsPerBot()
    {
        Assert.Equal("DiscordBot.bot-7.AllowedChannels", DiscordBotAllowedChannels.ConfigKey("bot-7"));
    }

    // ==================================================================
    // Record: JSON round-trip + normalization
    // ==================================================================

    [Fact]
    public void ToJson_FromJson_RoundTripsPickedChannels()
    {
        var allowed = new DiscordBotAllowedChannels(["100000000000000001", "100000000000000002"]);

        var parsed = DiscordBotAllowedChannels.FromJson(allowed.ToJson());

        Assert.False(parsed.IsUnrestricted);
        Assert.Equal(new[] { "100000000000000001", "100000000000000002" }, parsed.ChannelIds);
    }

    [Fact]
    public void FromJson_TrimsWhitespace_DropsBlankEntries_AndDedupes()
    {
        // [" 1 ", "1", "", "   ", "2"] -> the stored row stays minimal:
        // trimmed, blanks dropped, duplicates collapsed (ordinal).
        var parsed = DiscordBotAllowedChannels.FromJson("""[" 1 ", "1", "", "   ", "2"]""");

        Assert.Equal(new[] { "1", "2" }, parsed.ChannelIds);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json {")]
    [InlineData("null")]
    public void FromJson_MissingOrMalformed_FallsBackToUnrestricted(string? json)
    {
        // A broken row must never lock a bot out of its own commands.
        var parsed = DiscordBotAllowedChannels.FromJson(json);
        Assert.Equal(DiscordBotAllowedChannels.Default, parsed);
        Assert.True(parsed.IsUnrestricted);
    }

    // ==================================================================
    // Record: membership checks (Allows vs pure IsPicked)
    // ==================================================================

    [Fact]
    public void Allows_PickedChannel_TrueWithTrimming_UnequivalentCase()
    {
        var allowed = new DiscordBotAllowedChannels(["111", "222"]);

        Assert.True(allowed.Allows("111"));
        Assert.True(allowed.Allows(" 222 "));          // trimmed compare both sides
        Assert.False(allowed.Allows("333"));           // not picked
        Assert.False(allowed.Allows(null));            // unknown id while restricted
        Assert.False(allowed.Allows(""));              // blank id while restricted
        Assert.False(allowed.Allows("1111"));          // prefix is not membership
    }

    [Fact]
    public void Allows_IsOrdinal_SnowflakeCaseMatters()
    {
        // Snowflakes are digits in practice, but the contract is ordinal:
        // "ABC" and "abc" are DIFFERENT channels.
        var allowed = new DiscordBotAllowedChannels(["AbC"]);
        Assert.True(allowed.Allows("AbC"));
        Assert.False(allowed.Allows("abc"));
    }

    [Fact]
    public void IsPicked_MatchesAllows_WhenRestricted()
    {
        var allowed = new DiscordBotAllowedChannels(["111"]);

        Assert.True(allowed.IsPicked("111"));
        Assert.True(allowed.IsPicked(" 111 "));
        Assert.False(allowed.IsPicked("222"));
        Assert.False(allowed.IsPicked(null));
    }

    // ==================================================================
    // Manager persistence (real DiscordBotManager + in-memory configs)
    // ==================================================================

    [Fact]
    public async Task SaveThenGet_RoundTripsThroughTheConfigStore()
    {
        var data = new Wave2TestDataService();
        var manager = CreateManager(data);

        await manager.SaveAllowedChannelsAsync("bot-1", new DiscordBotAllowedChannels(["10", "20"]));

        var loaded = await manager.GetAllowedChannelsAsync("bot-1");
        Assert.False(loaded.IsUnrestricted);
        Assert.Equal(new[] { "10", "20" }, loaded.ChannelIds);

        // The stored payload is the plain JSON string array Core parses.
        var key = DiscordBotAllowedChannels.ConfigKey("bot-1");
        Assert.True(data.ConfigValues.ContainsKey(key));
        Assert.Contains("\"10\"", data.ConfigValues[key], StringComparison.Ordinal);
    }

    [Fact]
    public async Task SaveAllowedChannels_PerBotKeys_DoNotCollide()
    {
        var data = new Wave2TestDataService();
        var manager = CreateManager(data);

        await manager.SaveAllowedChannelsAsync("bot-a", new DiscordBotAllowedChannels(["1"]));
        await manager.SaveAllowedChannelsAsync("bot-b", DiscordBotAllowedChannels.Default);

        Assert.False((await manager.GetAllowedChannelsAsync("bot-a")).IsUnrestricted);
        Assert.True((await manager.GetAllowedChannelsAsync("bot-b")).IsUnrestricted);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SaveAllowedChannels_BlankBotId_WritesNothing(string? botId)
    {
        var data = new Wave2TestDataService();
        var manager = CreateManager(data);

        await manager.SaveAllowedChannelsAsync(botId!, new DiscordBotAllowedChannels(["1"]));

        Assert.Empty(data.ConfigWrites);
    }

    [Fact]
    public async Task SaveAllowedChannels_NullList_WritesNothing()
    {
        var data = new Wave2TestDataService();
        var manager = CreateManager(data);

#pragma warning disable CS8625 // deliberate null: the manager contract guards it
        await manager.SaveAllowedChannelsAsync("bot-1", null!);
#pragma warning restore CS8625

        Assert.Empty(data.ConfigWrites);
    }

    [Fact]
    public async Task GetAllowedChannels_UnknownBot_ReturnsUnrestricted()
    {
        var data = new Wave2TestDataService();
        var manager = CreateManager(data);

        var loaded = await manager.GetAllowedChannelsAsync("never-seen");
        Assert.Equal(DiscordBotAllowedChannels.Default, loaded);
    }

    [Fact]
    public async Task GetAllowedChannels_MalformedRow_FallsBackToUnrestricted()
    {
        var data = new Wave2TestDataService();
        data.ConfigValues[DiscordBotAllowedChannels.ConfigKey("bot-9")] = "{ broken";
        var manager = CreateManager(data);

        var loaded = await manager.GetAllowedChannelsAsync("bot-9");
        Assert.True(loaded.IsUnrestricted);
    }

    /// <summary>
    /// In-memory bus double: the manager's constructor only subscribes to
    /// member-join events, which these persistence paths never publish.
    /// </summary>
    private sealed class NoOpBus : IEventBus
    {
        public Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default) where TEvent : IEvent
            => Task.CompletedTask;

        public Task<Guid> SubscribeAsync<TEvent>(Func<TEvent, Task> handler, CancellationToken cancellationToken = default) where TEvent : IEvent
            => Task.FromResult(Guid.NewGuid());

        public void Unsubscribe(Guid subscriptionId)
        {
        }

        public int SubscriptionCount => 0;
    }
}
