using VyralBot.Data;
using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using VyralBot.Core.Services.Currency;
using VyralBot.Core.Services.EventBus;
using VyralBot.Core.Services.EventBus.Events;
using VyralBot.Core.Services.Linking;
using VyralBot.Data.Entities;
using Xunit;

namespace VyralBot.UI.Tests;

/// <summary>
/// Wave-10 coverage for the WAVE-9 GIVEAWAY FLUSH STATUS GUARD: when the
/// timed-intake flush (or a live entry) races a winner draw, the giveaway
/// must never be RESURRECTED as Active. Two layers guard it:
/// 1. <c>PersistEntryAsync</c> re-validates Status == Active inside the draw
///    gate — a completed giveaway rejects queued entries outright.
/// 2. <c>DrainTimedQueueAsync</c> only refreshes totals (+ publishes the
///    aggregated entry event) when a FRESH read still says Active.
///
/// The flush path is private, so the wave-8 reflection pattern drives it
/// (<c>DrainTimedQueueAsync</c> invoked like CommandTimerService.TickAsync);
/// entries are queued through the PUBLIC <c>EnterGiveawayAsync</c> by
/// flipping the intake to Timed via the "Giveaway.LiveMaxEntriesPerMinute"
/// config (1 attempt/min is enough). The data fake re-implements the five
/// giveaway members the base wave-2 fake leaves as throwers (interface
/// reimplementation — calls through IDataService bind to these).
/// </summary>
public class Wave10GiveawayFlushGuardTests : IDisposable
{
    private readonly List<VyralBot.Core.Services.GiveawayService> _services = new();

    public void Dispose()
    {
        // Stops each test's timed-intake flush loop (zero orphans).
        foreach (var service in _services)
        {
            service.Dispose();
        }
    }

    [Fact]
    public async Task Flush_SkipsTotalsRefresh_AndDoesNotResurrect_WhenDrawCompletesMidFlush()
    {
        var (service, data, bus) = await CreateQueuedTimedGiveawayAsync();

        // The race the guard exists for: the winner draw completes the
        // giveaway exactly when this entry's persist lands. The fake flips
        // the stored row to Completed inside SaveGiveawayEntryAsync — after
        // the persist's own Active validation, before the totals read.
        data.OnEntrySaved = _ => data.Giveaways.Values.Single().Status = GiveawayStatus.Completed;

        var savesBeforeFlush = data.GiveawaySaves.Count; // create + start
        await DrainAsync(service);

        var row = data.Giveaways.Values.Single();

        // The entry itself WAS persisted (the persist ran while still Active)…
        Assert.Single(data.Entries, e => e.Username == "racer");
        // …but the row is not resurrected: no totals-refresh save happened
        // and the completed status survived the flush.
        Assert.Equal(GiveawayStatus.Completed, row.Status);
        Assert.Equal(savesBeforeFlush, data.GiveawaySaves.Count);
        // And the aggregated entry event (only sent inside the Active
        // branch) was never published for a completed giveaway.
        Assert.DoesNotContain(bus.Published, e => e is GiveawayEntryEvent);
    }

    [Fact]
    public async Task Flush_WhileStillActive_RefreshesTotalsAndPublishesAggregatedEvent()
    {
        var (service, data, bus) = await CreateQueuedTimedGiveawayAsync();
        var savesBeforeFlush = data.GiveawaySaves.Count; // create + start

        await DrainAsync(service);

        var row = data.Giveaways.Values.Single();

        // Control case for the guard: still Active -> totals refresh save…
        Assert.Equal(GiveawayStatus.Active, row.Status);
        Assert.Equal(savesBeforeFlush + 1, data.GiveawaySaves.Count);
        Assert.Equal(1, row.TotalEntries);
        Assert.Equal(1, row.UniqueParticipants);
        // …one entry persisted, one aggregated (IsTimed) event.
        Assert.Single(data.Entries, e => e.Username == "racer");
        var entryEvent = Assert.Single(bus.Published.OfType<GiveawayEntryEvent>());
        Assert.True(entryEvent.IsTimed);
        Assert.Equal("racer", entryEvent.Username);
        Assert.Equal(1, entryEvent.TotalEntries);
    }

    [Fact]
    public async Task Flush_RejectsQueuedEntries_WhenGiveawayAlreadyCompleted()
    {
        var (service, data, bus) = await CreateQueuedTimedGiveawayAsync();

        // Completed BEFORE the flush: the gated persist re-validation inside
        // the draw gate must reject the queued entry outright.
        data.Giveaways.Values.Single().Status = GiveawayStatus.Completed;
        var savesBeforeFlush = data.GiveawaySaves.Count;

        await DrainAsync(service);

        Assert.Empty(data.Entries);
        Assert.Equal(savesBeforeFlush, data.GiveawaySaves.Count);
        Assert.Equal(GiveawayStatus.Completed, data.Giveaways.Values.Single().Status);
        Assert.DoesNotContain(bus.Published, e => e is GiveawayEntryEvent);
    }

    // ==================================================================
    // Harness
    // ==================================================================

    private async Task<(VyralBot.Core.Services.GiveawayService Service, Wave10GiveawayDataService Data, Wave10RecordingBus Bus)>
        CreateQueuedTimedGiveawayAsync()
    {
        var data = new Wave10GiveawayDataService();
        var bus = new Wave10RecordingBus();

        // One entry attempt per minute already exceeds the live threshold:
        // the very first EnterGiveawayAsync flips intake to Timed and QUEUES
        // the entry instead of persisting it. The flush window stays huge so
        // only the explicit reflection drain ever persists it.
        data.ConfigValues["Giveaway.LiveMaxEntriesPerMinute"] = "1";
        data.ConfigValues["Giveaway.TimedFlushSeconds"] = "600";

        var service = new VyralBot.Core.Services.GiveawayService(
            data, bus, new ThrowingLinkedAccounts(), new ThrowingViewerStatus(),
            NullLogger<VyralBot.Core.Services.GiveawayService>.Instance);
        _services.Add(service); // disposed with the test (stops the flush loop)

        var giveaway = await service.CreateGiveawayAsync("Flush Guard", "prize", "!enter");
        Assert.True(await service.StartGiveawayAsync(giveaway.Id));

        var (ok, message) = await service.EnterGiveawayAsync(giveaway.Id, "racer", "Twitch");
        Assert.True(ok);
        Assert.Equal(VyralBot.Core.Services.GiveawayIntakeMode.Timed, service.CurrentIntakeMode);

        return (service, data, bus);
    }

    /// <summary>
    /// Drains the timed-entry queue through the private flush path (the wave-8
    /// reflection pattern for private async members), gated like the flush
    /// loop's own call.
    /// </summary>
    private static async Task DrainAsync(VyralBot.Core.Services.GiveawayService service)
    {
        var method = typeof(VyralBot.Core.Services.GiveawayService)
            .GetMethod("DrainTimedQueueAsync", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("DrainTimedQueueAsync moved — update the wave-10 tests");

        var task = (Task)method.Invoke(service, new object?[] { CancellationToken.None, true })!;
        await task;
    }
}

/// <summary>
/// In-memory IDataService for the wave-10 giveaway flush tests: the wave-2
/// fake's real config table + giveaway ROW behavior, plus REAL behavior for
/// the five giveaway members the base leaves as throwers. This is an
/// interface REIMPLEMENTATION (the interface is re-listed, so calls through
/// IDataService bind to these `new` members even though the base ones are
/// non-virtual) — if the rebind ever stopped working these tests would fail
/// loudly with the base NotImplementedException rather than pass vacuously.
/// </summary>
public class Wave10GiveawayDataService : Wave2TestDataService, IDataService
{
    /// <summary>Entry rows in save order.</summary>
    public List<GiveawayEntry> Entries { get; } = new();

    /// <summary>Every SaveGiveawayAsync call, in order (create/start/totals-refresh).</summary>
    public List<Giveaway> GiveawaySaves { get; } = new();

    /// <summary>Hook fired inside SaveGiveawayEntryAsync — the mid-flush race point.</summary>
    public Action<GiveawayEntry>? OnEntrySaved { get; set; }

    public new Task SaveGiveawayAsync(Giveaway giveaway)
    {
        GiveawaySaves.Add(giveaway);
        return base.SaveGiveawayAsync(giveaway); // keep the row dict behavior
    }

    public new Task<GiveawayEntry?> GetGiveawayEntryAsync(Guid giveawayId, string username, string platform)
        => Task.FromResult(Entries.FirstOrDefault(e =>
            e.GiveawayId == giveawayId
            && string.Equals(e.Username, username, StringComparison.OrdinalIgnoreCase)
            && string.Equals(e.Platform, platform, StringComparison.OrdinalIgnoreCase)));

    public new Task SaveGiveawayEntryAsync(GiveawayEntry entry)
    {
        Entries.Add(entry);
        OnEntrySaved?.Invoke(entry);
        return Task.CompletedTask;
    }

    public new Task<int> GetGiveawayEntryCountAsync(Guid giveawayId)
        => Task.FromResult(Entries.Count(e => e.GiveawayId == giveawayId));

    public new Task<int> GetGiveawayUniqueParticipantsAsync(Guid giveawayId)
        => Task.FromResult(Entries
            .Where(e => e.GiveawayId == giveawayId)
            .Select(e => e.Username)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count());
}

/// <summary>Records every published event (giveaway lifecycle + entry events).</summary>
public sealed class Wave10RecordingBus : IEventBus
{
    public List<object> Published { get; } = new();

    public Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default) where TEvent : IEvent
    {
        Published.Add(@event!);
        return Task.CompletedTask;
    }

    public Task<Guid> SubscribeAsync<TEvent>(Func<TEvent, Task> handler, CancellationToken cancellationToken = default) where TEvent : IEvent
        => Task.FromResult(Guid.NewGuid());

    public void Unsubscribe(Guid subscriptionId)
    {
    }

    public int SubscriptionCount => 0;
}

/// <summary>Never reached: the flush-guard giveaways run with the odds boost off.</summary>
public sealed class ThrowingLinkedAccounts : ILinkedAccountService
{
    private const string NotImplemented = "Not needed for wave-10 giveaway flush tests.";

    public Task<string?> GetViewerIdAsync(string username, string platform) => throw new NotImplementedException(NotImplemented);
    public Task<IEnumerable<LinkedAccount>> GetLinkedAccountsAsync(string viewerId) => throw new NotImplementedException(NotImplemented);
    public Task<string> GetOrCreateViewerIdAsync(string username, string platform) => throw new NotImplementedException(NotImplemented);
    public Task<bool> InitiateLinkAsync(string fromUsername, string fromPlatform, string targetUsername, string targetPlatform) => throw new NotImplementedException(NotImplemented);
    public Task<bool> VerifyLinkAsync(string username, string platform, string verificationCode) => throw new NotImplementedException(NotImplemented);
    public Task<bool> UnlinkAccountAsync(string username, string platform) => throw new NotImplementedException(NotImplemented);
    public Task<IEnumerable<(string Username, string Platform)>> ResolveIdentitiesAsync(string username, string platform) => throw new NotImplementedException(NotImplemented);
}

/// <summary>Never reached: the flush-guard giveaways run with the odds boost off.</summary>
public sealed class ThrowingViewerStatus : IViewerStatusService
{
    private const string NotImplemented = "Not needed for wave-10 giveaway flush tests.";

    public Task RecordSubscriptionAsync(string username, string platform, int tier, int months) => throw new NotImplementedException(NotImplemented);
    public Task RecordFollowAsync(string username, string platform) => throw new NotImplementedException(NotImplemented);
    public Task<ViewerPlatformStatus?> GetStatusAsync(string username, string platform) => throw new NotImplementedException(NotImplemented);
    public Task<int> GetBestSubTierAcrossLinkedAsync(string viewerId) => throw new NotImplementedException(NotImplemented);
    public Task<bool> IsFollowerAnywhereAsync(string viewerId) => throw new NotImplementedException(NotImplemented);
    public Task<int> GetLinkedPlatformCountAsync(string viewerId) => throw new NotImplementedException(NotImplemented);
}
