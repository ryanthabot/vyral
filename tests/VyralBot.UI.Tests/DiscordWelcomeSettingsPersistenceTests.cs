using Microsoft.Extensions.Logging.Abstractions;
using VyralBot.Core.Services.EventBus;
using VyralBot.Data;
using VyralBot.Integrations.Discord;
using Xunit;

namespace VyralBot.UI.Tests;

/// <summary>
/// Wave-6 regression coverage for the per-bot welcome/auto-role settings
/// persistence (wave 5): <see cref="DiscordBotManager.GetWelcomeSettingsAsync"/>
/// / <see cref="DiscordBotManager.SaveWelcomeSettingsAsync"/> round-trip a
/// <see cref="DiscordBotWelcomeSettings"/> through the Configs table under the
/// <c>DiscordBot.{botId}.WelcomeSettings</c> key, and a missing/malformed row
/// falls back to <see cref="DiscordBotWelcomeSettings.Default"/> instead of
/// throwing. Uses the shared in-memory <see cref="IDataService"/> fake from
/// MarathonWave2Fakes (real config-table behavior).
/// </summary>
public class DiscordWelcomeSettingsPersistenceTests
{
    private static DiscordBotManager CreateManager(Wave2TestDataService data)
        => new(data, new NoOpEventBus(), NullLogger<DiscordBotManager>.Instance, NullLoggerFactory.Instance);

    [Fact]
    public async Task SaveThenGet_RoundTripsEveryField()
    {
        var data = new Wave2TestDataService();
        var manager = CreateManager(data);
        var saved = new DiscordBotWelcomeSettings(
            Enabled: true,
            GuildId: "100000000000000001",
            ChannelId: "100000000000000002",
            Message: "Welcome to {server}, {user}!",
            AutoRoleId: "100000000000000003");

        await manager.SaveWelcomeSettingsAsync("bot-1", saved);
        var loaded = await manager.GetWelcomeSettingsAsync("bot-1");

        Assert.Equal(saved, loaded);
    }

    [Fact]
    public async Task Save_WritesJsonUnderPerBotConfigKey()
    {
        var data = new Wave2TestDataService();
        var manager = CreateManager(data);

        await manager.SaveWelcomeSettingsAsync("bot-7",
            new DiscordBotWelcomeSettings(Enabled: false, GuildId: "g1", ChannelId: null, Message: null, AutoRoleId: "r1"));

        // Per-bot key (never a shared row — bots must not see each other's settings).
        var key = DiscordBotWelcomeSettings.ConfigKey("bot-7");
        Assert.Equal("DiscordBot.bot-7.WelcomeSettings", key);
        Assert.True(data.ConfigValues.ContainsKey(key));

        // The stored payload itself must parse back to what was saved.
        var parsed = DiscordBotWelcomeSettings.FromJson(data.ConfigValues[key]);
        Assert.False(parsed.Enabled);
        Assert.Equal("g1", parsed.GuildId);
        Assert.Equal("r1", parsed.AutoRoleId);
    }

    [Fact]
    public async Task Save_PerBotKeys_DoNotCollide()
    {
        var data = new Wave2TestDataService();
        var manager = CreateManager(data);

        await manager.SaveWelcomeSettingsAsync("bot-a", new DiscordBotWelcomeSettings(Enabled: true, GuildId: "guild-a"));
        await manager.SaveWelcomeSettingsAsync("bot-b", new DiscordBotWelcomeSettings(Enabled: false, GuildId: "guild-b"));

        var a = await manager.GetWelcomeSettingsAsync("bot-a");
        var b = await manager.GetWelcomeSettingsAsync("bot-b");

        Assert.True(a.Enabled);
        Assert.Equal("guild-a", a.GuildId);
        Assert.False(b.Enabled);
        Assert.Equal("guild-b", b.GuildId);
    }

    [Fact]
    public async Task Get_UnknownBot_ReturnsDefaultsWithoutWriting()
    {
        var data = new Wave2TestDataService();
        var manager = CreateManager(data);

        var loaded = await manager.GetWelcomeSettingsAsync("never-seen");

        Assert.Equal(DiscordBotWelcomeSettings.Default, loaded);
        Assert.Empty(data.ConfigWrites);
    }

    [Fact]
    public async Task Get_MalformedJson_FallsBackToDefaults()
    {
        var data = new Wave2TestDataService();
        data.ConfigValues[DiscordBotWelcomeSettings.ConfigKey("bot-9")] = "{ this is not json";
        var manager = CreateManager(data);

        var loaded = await manager.GetWelcomeSettingsAsync("bot-9");

        Assert.Equal(DiscordBotWelcomeSettings.Default, loaded);
    }

    [Fact]
    public async Task Get_ConfigStoreThrows_FallsBackToDefaults()
    {
        var data = new ThrowingConfigDataService();
        var manager = CreateManager(data);

        var loaded = await manager.GetWelcomeSettingsAsync("bot-x");

        // A broken data layer must not disable-or-enable the feature by throwing.
        Assert.Equal(DiscordBotWelcomeSettings.Default, loaded);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Save_BlankBotId_WritesNothing(string? botId)
    {
        var data = new Wave2TestDataService();
        var manager = CreateManager(data);

        await manager.SaveWelcomeSettingsAsync(botId!, new DiscordBotWelcomeSettings(Enabled: true));

        Assert.Empty(data.ConfigWrites);
        Assert.Empty(data.ConfigValues);
    }

    [Fact]
    public async Task Save_NullSettings_WritesNothing()
    {
        var data = new Wave2TestDataService();
        var manager = CreateManager(data);

#pragma warning disable CS8625 // deliberate null: the manager contract guards it
        await manager.SaveWelcomeSettingsAsync("bot-1", null!);
#pragma warning restore CS8625

        Assert.Empty(data.ConfigWrites);
    }

    // ==================================================================
    // Record-level JSON behavior (the payload the manager stores)
    // ==================================================================

    [Fact]
    public void SettingsJson_RoundTripsAllFields()
    {
        var original = new DiscordBotWelcomeSettings(
            Enabled: true, GuildId: "g", ChannelId: "c", Message: "hi {user}", AutoRoleId: "r");

        var parsed = DiscordBotWelcomeSettings.FromJson(original.ToJson());

        Assert.Equal(original, parsed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-json-at-all")]
    public void FromJson_InvalidPayload_ReturnsDefaults(string? json)
    {
        Assert.Equal(DiscordBotWelcomeSettings.Default, DiscordBotWelcomeSettings.FromJson(json));
    }

    /// <summary>
    /// In-memory bus double: the manager's constructor only subscribes to
    /// member-join events, which these persistence paths never publish.
    /// </summary>
    private sealed class NoOpEventBus : IEventBus
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

    /// <summary>
    /// Config-table failure simulator: every read throws, so the manager's
    /// load-path try/catch fallback is exercised (logged, never thrown).
    /// </summary>
    private sealed class ThrowingConfigDataService : Wave2TestDataService
    {
        public override Task<string?> GetConfigValueAsync(string key)
            => throw new InvalidOperationException("configs table unavailable");
    }
}
