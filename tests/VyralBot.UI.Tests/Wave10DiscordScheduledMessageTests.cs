using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using VyralBot.Core.Services.EventBus;
using VyralBot.Integrations.Discord;
using Xunit;

namespace VyralBot.UI.Tests;

/// <summary>
/// Wave-10 coverage for the wave-8 DISCORD BOT SCHEDULED MESSAGES (recurring
/// announcements): the <see cref="DiscordBotScheduledMessage"/> record's pure
/// helpers (interval clamp, JSON round-trip, send-time {time}/{date} format,
/// per-bot bound) plus the real persistence paths on
/// <see cref="DiscordBotManager"/> (save bounds the list and resets every
/// NextRunUtc so an edit always restarts the cycle; load tolerates broken
/// rows). Uses the in-memory <see cref="Wave2TestDataService"/> config table
/// (the wave-6 DiscordWelcomeSettingsPersistenceTests pattern).
///
/// Seam note: the "first run = now + interval" computation itself is inline
/// in the manager's private EvaluateBotScheduledMessagesAsync, which requires
/// a RUNNING connected Discord bot — not unit-testable here. Its ingredients
/// are covered instead: EffectiveIntervalMinutes (the clamp the timer adds)
/// and the null-NextRunUtc reset this save path performs (the state that
/// tells the timer to schedule one full interval out).
/// </summary>
public class Wave10DiscordScheduledMessageTests : IDisposable
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
    // EffectiveIntervalMinutes: the stored value can never spin the timer
    // ==================================================================

    [Theory]
    [InlineData(-1000, 5)]   // negative junk -> minimum
    [InlineData(0, 5)]       // a stored 0 (typo / legacy row) -> minimum
    [InlineData(4, 5)]       // just under the floor
    [InlineData(5, 5)]       // the floor itself
    [InlineData(30, 30)]     // ordinary value passes through
    [InlineData(1440, 1440)] // the ceiling itself (one day)
    [InlineData(1441, 1440)] // just over the ceiling
    [InlineData(999999, 1440)]   // runaway value -> one day max
    public void EffectiveInterval_ClampsInto5To1440Minutes(int stored, int effective)
    {
        var message = new DiscordBotScheduledMessage("row-1", Enabled: true, ChannelId: "123",
            IntervalMinutes: stored, Message: "hi");
        Assert.Equal(effective, message.EffectiveIntervalMinutes);
    }

    [Fact]
    public void Bounds_PinTheContract()
    {
        Assert.Equal(5, DiscordBotScheduledMessage.MinIntervalMinutes);
        Assert.Equal(1440, DiscordBotScheduledMessage.MaxIntervalMinutes);
        Assert.Equal(25, DiscordBotScheduledMessage.MaxPerBot);
        Assert.Equal("DiscordBot.bot-42.ScheduledMessages",
            DiscordBotScheduledMessage.ConfigKey("bot-42"));
    }

    // ==================================================================
    // JSON round-trip (the payload SaveScheduledMessagesAsync stores)
    // ==================================================================

    [Fact]
    public void ListToJson_FromJson_RoundTripsEveryField()
    {
        var due = new DateTime(2026, 11, 7, 12, 0, 0, DateTimeKind.Utc);
        var messages = new List<DiscordBotScheduledMessage>
        {
            new("id-1", Enabled: true, ChannelId: "100", IntervalMinutes: 15,
                Message: "Stream starting soon {time}", NextRunUtc: due),
            new("id-2", Enabled: false, ChannelId: null, IntervalMinutes: 1440,
                Message: null, NextRunUtc: null),
        };

        var parsed = DiscordBotScheduledMessage.ListFromJson(DiscordBotScheduledMessage.ListToJson(messages));

        Assert.Equal(messages, parsed); // record value-equality, field by field
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json at all")]
    [InlineData("null")]
    public void ListFromJson_MissingOrMalformed_YieldsEmptyList(string? json)
    {
        // A broken row must never disable-or-fire entries by throwing.
        Assert.Empty(DiscordBotScheduledMessage.ListFromJson(json));
    }

    // ==================================================================
    // FormatMessage: send-time variables (case-insensitive)
    // ==================================================================

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void FormatMessage_BlankTemplate_ReturnsEmpty(string? template)
    {
        Assert.Equal(string.Empty, DiscordBotScheduledMessage.FormatMessage(template));
    }

    [Fact]
    public void FormatMessage_PlainTemplate_ReturnedVerbatim()
    {
        Assert.Equal("No tokens here!", DiscordBotScheduledMessage.FormatMessage("No tokens here!"));
    }

    [Fact]
    public void FormatMessage_ReplacesTimeToken_WithShortLocalTime()
    {
        // Snapshot the local short time before AND after the call so the
        // assertion cannot flake on a minute rollover (or culture digits).
        var before = DateTime.Now.ToString("HH:mm", CultureInfo.CurrentCulture);
        var formatted = DiscordBotScheduledMessage.FormatMessage("Live at {time}!");
        var after = DateTime.Now.ToString("HH:mm", CultureInfo.CurrentCulture);

        Assert.DoesNotContain("{time}", formatted, StringComparison.OrdinalIgnoreCase);
        Assert.True(formatted == $"Live at {before}!" || formatted == $"Live at {after}!",
            $"expected 'Live at {before}!' or 'Live at {after}!' but got '{formatted}'");
    }

    [Fact]
    public void FormatMessage_ReplacesDateToken_WithShortLocalDate()
    {
        var before = DateTime.Now.ToString("d", CultureInfo.CurrentCulture);
        var formatted = DiscordBotScheduledMessage.FormatMessage("Today is {date}.");
        var after = DateTime.Now.ToString("d", CultureInfo.CurrentCulture);

        Assert.DoesNotContain("{date}", formatted, StringComparison.OrdinalIgnoreCase);
        Assert.True(formatted == $"Today is {before}." || formatted == $"Today is {after}.",
            $"expected 'Today is {before}.' or 'Today is {after}.' but got '{formatted}'");
    }

    [Fact]
    public void FormatMessage_TokensAreCaseInsensitive_AndBothApply()
    {
        var beforeTime = DateTime.Now.ToString("HH:mm", CultureInfo.CurrentCulture);
        var beforeDate = DateTime.Now.ToString("d", CultureInfo.CurrentCulture);
        var formatted = DiscordBotScheduledMessage.FormatMessage("{TIME} on {Date}");
        var afterTime = DateTime.Now.ToString("HH:mm", CultureInfo.CurrentCulture);
        var afterDate = DateTime.Now.ToString("d", CultureInfo.CurrentCulture);

        // Production reads DateTime.Now TWICE (once per token replace), so a
        // clock boundary can fall between the two reads. Accept any
        // before/after mix — fully rollover-proof for minute ticks AND for a
        // local-midnight date flip landing inside the production call.
        Assert.True(
            (formatted == $"{beforeTime} on {beforeDate}")
            || (formatted == $"{beforeTime} on {afterDate}")
            || (formatted == $"{afterTime} on {beforeDate}")
            || (formatted == $"{afterTime} on {afterDate}"),
            $"unexpected formatted text '{formatted}' "
            + $"(before: '{beforeTime} on {beforeDate}', after: '{afterTime} on {afterDate}')");
    }

    // ==================================================================
    // Manager persistence (real DiscordBotManager + in-memory configs)
    // ==================================================================

    [Fact]
    public async Task SaveScheduledMessages_BoundsListToMaxPerBot()
    {
        var data = new Wave2TestDataService();
        var manager = CreateManager(data);

        // 30 rows saved; the card's bound means only the first 25 survive.
        var rows = Enumerable.Range(0, 30)
            .Select(i => new DiscordBotScheduledMessage($"row-{i}", Enabled: true,
                ChannelId: $"chan-{i}", IntervalMinutes: 10 + i, Message: $"msg {i}"))
            .ToList();

        await manager.SaveScheduledMessagesAsync("bot-1", rows);

        var loaded = await manager.GetScheduledMessagesAsync("bot-1");
        Assert.Equal(DiscordBotScheduledMessage.MaxPerBot, loaded.Count);
        // Take() keeps the FIRST rows — row-0..row-24, never the tail.
        Assert.Equal("row-24", loaded[^1].Id);
        Assert.DoesNotContain(loaded, m => m.Id is "row-25" or "row-26" or "row-27" or "row-28" or "row-29");
    }

    [Fact]
    public async Task SaveScheduledMessages_ResetsEveryNextRunUtcToNull()
    {
        // Null on save is what makes the timer schedule the first run a full
        // interval out (and an edit restart the cycle) — a stale due-time
        // from before the save must never replay.
        var data = new Wave2TestDataService();
        var manager = CreateManager(data);

        var staleDue = DateTime.UtcNow.AddDays(-7); // long overdue
        await manager.SaveScheduledMessagesAsync("bot-2", new[]
        {
            new DiscordBotScheduledMessage("a", Enabled: true, ChannelId: "c", IntervalMinutes: 30,
                Message: "m", NextRunUtc: staleDue),
            new DiscordBotScheduledMessage("b", Enabled: true, ChannelId: "c", IntervalMinutes: 60,
                Message: "m", NextRunUtc: DateTime.UtcNow.AddMinutes(1)),
        });

        var loaded = await manager.GetScheduledMessagesAsync("bot-2");
        Assert.All(loaded, m => Assert.Null(m.NextRunUtc));

        // And the stored payload itself carries the nulls (not just the
        // read-back view): System.Text.Json writes them out explicitly.
        var storedJson = data.ConfigValues[DiscordBotScheduledMessage.ConfigKey("bot-2")];
        Assert.Contains("\"NextRunUtc\":null", storedJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ScheduledMessages_PerBotKeys_DoNotCollide()
    {
        var data = new Wave2TestDataService();
        var manager = CreateManager(data);

        await manager.SaveScheduledMessagesAsync("bot-a",
            new[] { new DiscordBotScheduledMessage("a1", Enabled: true, ChannelId: "1", Message: "x") });
        await manager.SaveScheduledMessagesAsync("bot-b",
            new[] { new DiscordBotScheduledMessage("b1", Enabled: true, ChannelId: "2", Message: "y") });

        var a = await manager.GetScheduledMessagesAsync("bot-a");
        var b = await manager.GetScheduledMessagesAsync("bot-b");

        Assert.Single(a, m => m.Id == "a1");
        Assert.Single(b, m => m.Id == "b1");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SaveScheduledMessages_BlankBotId_WritesNothing(string? botId)
    {
        var data = new Wave2TestDataService();
        var manager = CreateManager(data);

        await manager.SaveScheduledMessagesAsync(botId!,
            new[] { new DiscordBotScheduledMessage("a", Enabled: true) });

        Assert.Empty(data.ConfigWrites);
        Assert.Empty(data.ConfigValues);
    }

    [Fact]
    public async Task SaveScheduledMessages_NullList_WritesNothing()
    {
        var data = new Wave2TestDataService();
        var manager = CreateManager(data);

#pragma warning disable CS8625 // deliberate null: the manager contract guards it
        await manager.SaveScheduledMessagesAsync("bot-1", null!);
#pragma warning restore CS8625

        Assert.Empty(data.ConfigWrites);
    }

    [Fact]
    public async Task GetScheduledMessages_UnknownBot_ReturnsEmpty()
    {
        var data = new Wave2TestDataService();
        var manager = CreateManager(data);
        Assert.Empty(await manager.GetScheduledMessagesAsync("never-seen"));
    }

    [Fact]
    public async Task GetScheduledMessages_MalformedRow_ReturnsEmptyWithoutThrowing()
    {
        var data = new Wave2TestDataService();
        data.ConfigValues[DiscordBotScheduledMessage.ConfigKey("bot-9")] = "{ this is not json";
        var manager = CreateManager(data);

        Assert.Empty(await manager.GetScheduledMessagesAsync("bot-9"));
    }

    /// <summary>
    /// In-memory bus double: the manager's constructor only subscribes to
    /// member-join events, which these persistence paths never publish.
    /// (Sibling of the wave-6 welcome-settings test fake.)
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
