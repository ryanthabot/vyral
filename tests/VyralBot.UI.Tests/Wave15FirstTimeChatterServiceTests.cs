using Microsoft.Extensions.Logging.Abstractions;
using VyralBot.Core.Models;
using VyralBot.Core.Services.Chat;
using VyralBot.Core.Services.EventBus;
using VyralBot.Core.Services.EventBus.Events;
using VyralBot.Data;
using Xunit;

namespace VyralBot.UI.Tests;

/// <summary>
/// Wave-15 welcome-newcomer detection: <see cref="FirstTimeChatterService"/>
/// publishes <see cref="FirstTimeChatterEvent"/> the first time a viewer with
/// NO recorded activity (no UserWatchtime row AND no UserCurrency row on any
/// platform — the read-only <see cref="IDataService.HasAnyActivityAsync"/>
/// EXISTS pair) sends a chat message, with an in-memory burst-dedup window so
/// a newcomer's message burst fires exactly once, a per-platform dedup key so
/// the same username on two platforms is greeted twice, and a bounded map
/// (prune past capacity) that keeps detection working under load.
/// </summary>
public class Wave15FirstTimeChatterServiceTests
{
    private static ChatMessageReceivedEvent Message(
        string platform = "Twitch", string username = "newbie", string text = "hi!") => new()
    {
        Source = platform,
        Platform = platform,
        Username = username,
        Message = text
    };

    private sealed class Harness
    {
        public Wave15DispatchingEventBus Bus { get; } = new();
        public Wave15FirstTimeChatterDataService Data { get; } = new();
        public FirstTimeChatterService Service { get; }

        public Harness() => Service = new FirstTimeChatterService(
            NullLogger<FirstTimeChatterService>.Instance, Bus, Data);

        public async Task StartAsync() => await Service.StartAsync();

        /// <summary>Publishes a chat message through the bus (runs handlers inline).</summary>
        public async Task ChatAsync(ChatMessageReceivedEvent message) => await Bus.PublishAsync(message);

        public List<FirstTimeChatterEvent> Fired =>
            Bus.Published.OfType<FirstTimeChatterEvent>().ToList();
    }

    [Fact]
    public async Task StartAsync_SubscribesOnce_AndIsIdempotent()
    {
        var h = new Harness();

        await h.StartAsync();
        Assert.Equal(1, h.Bus.SubscriptionCount);

        await h.Service.StartAsync(); // second call is a no-op
        Assert.Equal(1, h.Bus.SubscriptionCount);
    }

    [Fact]
    public async Task NoRecordedActivity_FiresExactlyOnce_WithExpectedPayload()
    {
        var h = new Harness();
        await h.StartAsync();

        await h.ChatAsync(Message(platform: "Kick", username: "freshface", text: "first!"));

        var fired = Assert.Single(h.Fired);
        Assert.Equal("Kick", fired.Platform);
        Assert.Equal("Kick", fired.Source);
        Assert.Equal("freshface", fired.Username);
        Assert.Equal("first!", fired.Message);
        Assert.Null(fired.DisplayName); // ChatMessageReceivedEvent carries no display name
        Assert.Single(h.Data.ActivityChecks); // one EXISTS check for one message
    }

    [Fact]
    public async Task ExistingWatchtimeRow_DoesNotFire_EvenOnADifferentPlatform()
    {
        var h = new Harness();
        await h.StartAsync();
        // Source of truth is username-only across ALL platforms (the SQL EXISTS
        // pair ignores platform), so a watchtime row on Twitch suppresses the
        // greeting when the same name later chats on Kick.
        h.Data.WatchtimeUsernames.Add("regular");

        await h.ChatAsync(Message(platform: "Kick", username: "regular"));

        Assert.Empty(h.Fired);
        Assert.Equal(1, h.Data.ActivityChecks.Count); // checked, found activity, stayed quiet
    }

    [Fact]
    public async Task ExistingCurrencyRow_DoesNotFire()
    {
        var h = new Harness();
        await h.StartAsync();
        h.Data.CurrencyUsernames.Add("earner");

        await h.ChatAsync(Message(username: "earner"));

        Assert.Empty(h.Fired);
    }

    [Fact]
    public async Task ActivityCheck_IsCaseInsensitive_LikeTheSqlCollation()
    {
        var h = new Harness();
        await h.StartAsync();
        h.Data.WatchtimeUsernames.Add("Regular"); // SQL compares COLLATE NOCASE

        await h.ChatAsync(Message(username: "regular")); // different casing

        Assert.Empty(h.Fired);
    }

    [Fact]
    public async Task MessageBurst_FromSameUser_FiresOnce_AndChecksActivityOnce()
    {
        var h = new Harness();
        await h.StartAsync();

        await h.ChatAsync(Message(username: "bursty", text: "one"));
        await h.ChatAsync(Message(username: "bursty", text: "two"));
        await h.ChatAsync(Message(username: "bursty", text: "three"));

        Assert.Single(h.Fired);
        Assert.Single(h.Data.ActivityChecks); // the burst guard short-circuits before the DB
    }

    [Fact]
    public async Task SameUsername_OnDifferentPlatforms_FiresTwice()
    {
        var h = new Harness();
        await h.StartAsync();

        await h.ChatAsync(Message(platform: "Twitch", username: "dual"));
        await h.ChatAsync(Message(platform: "Kick", username: "dual"));

        Assert.Equal(2, h.Fired.Count);
        Assert.Equal(new[] { "Twitch", "Kick" }, h.Fired.Select(e => e.Platform));
        Assert.Equal(2, h.Data.ActivityChecks.Count); // dedup key is "platform|username"
    }

    [Fact]
    public async Task BlankUsername_OrBlankPlatform_IsIgnored_WithoutCheckingActivity()
    {
        var h = new Harness();
        await h.StartAsync();

        await h.ChatAsync(Message(username: ""));
        await h.ChatAsync(Message(username: "   "));
        await h.Bus.PublishAsync(new ChatMessageReceivedEvent
        {
            Source = "Twitch",
            Platform = "", // blank platform
            Username = "someone",
            Message = "hi"
        });

        Assert.Empty(h.Fired);
        Assert.Empty(h.Data.ActivityChecks);
    }

    [Fact]
    public async Task Username_IsTrimmed_InThePublishedEvent()
    {
        var h = new Harness();
        await h.StartAsync();

        await h.ChatAsync(Message(username: "  spaced  "));

        var fired = Assert.Single(h.Fired);
        Assert.Equal("spaced", fired.Username);
    }

    [Fact]
    public async Task ActivityCheckFailure_IsSwallowed_NeverDisturbsChat()
    {
        var h = new Harness();
        await h.StartAsync();
        h.Data.ThrowOnActivityCheck = true;

        await h.ChatAsync(Message(username: "anyone")); // must not propagate

        Assert.Empty(h.Fired);
    }

    [Fact]
    public async Task BoundedMap_ExceedingPruneCapacity_StillDetectsEveryNewcomer()
    {
        // Past the 1024-entry prune threshold the map only drops entries older
        // than the dedup window — fresh sightings survive, so detection and
        // dedup both keep working with more than a roomful of newcomers.
        var h = new Harness();
        await h.StartAsync();

        const int users = 1100; // > DedupPruneThreshold (1024)
        for (var i = 0; i < users; i++)
        {
            await h.ChatAsync(Message(username: $"viewer{i}"));
        }

        Assert.Equal(users, h.Fired.Count); // nobody lost to the capacity valve

        // A repeat from the very first newcomer is still deduped (fresh entry
        // survived pruning), and never reaches the DB again.
        var checksBefore = h.Data.ActivityChecks.Count;
        await h.ChatAsync(Message(username: "viewer0"));
        Assert.Equal(users, h.Fired.Count);
        Assert.Equal(checksBefore, h.Data.ActivityChecks.Count);
    }

    [Fact]
    public async Task StopAsync_Unsubscribes_AndClearsTheDedupMap()
    {
        var h = new Harness();
        await h.StartAsync();

        await h.ChatAsync(Message(username: "once"));
        Assert.Single(h.Fired);

        await h.Service.StopAsync();
        Assert.Equal(0, h.Bus.SubscriptionCount);

        await h.ChatAsync(Message(username: "once")); // no subscription → no event
        Assert.Single(h.Fired);

        // Restart clears the in-memory map, so the same still-unknown viewer is
        // detected again (their durable rows are the real one-fire guarantee).
        await h.StartAsync();
        await h.ChatAsync(Message(username: "once"));
        Assert.Equal(2, h.Fired.Count);
    }
}

/// <summary>
/// IDataService fake for the first-time-chatter tests: models exactly the
/// source-of-truth <c>HasAnyActivityAsync</c> reads (a UserWatchtime row OR a
/// UserCurrency row for the username, case-insensitive, platform-agnostic —
/// mirroring the Dapper COLLATE NOCASE EXISTS pair) and counts calls so the
/// burst-dedup tests can prove the DB was never re-queried. Everything else
/// inherits the wave-8 throwing fake — the service is read-only by design, so
/// any accidental write fails loudly.
/// </summary>
public sealed class Wave15FirstTimeChatterDataService : IntegrationWave8DataService, IDataService
{
    private static readonly StringComparer SqlNocase = StringComparer.OrdinalIgnoreCase;

    /// <summary>Usernames that have a UserWatchtime row (any platform).</summary>
    public HashSet<string> WatchtimeUsernames { get; } = new(SqlNocase);

    /// <summary>Usernames that have a UserCurrency row (any platform).</summary>
    public HashSet<string> CurrencyUsernames { get; } = new(SqlNocase);

    /// <summary>Every username handed to HasAnyActivityAsync, in call order.</summary>
    public List<string> ActivityChecks { get; } = new();

    /// <summary>Simulates a database failure on the activity check.</summary>
    public bool ThrowOnActivityCheck { get; set; }

    public new Task<bool> HasAnyActivityAsync(string username)
    {
        ActivityChecks.Add(username ?? "");
        if (ThrowOnActivityCheck)
        {
            throw new InvalidOperationException("simulated database failure");
        }

        var known = WatchtimeUsernames.Contains(username) || CurrencyUsernames.Contains(username);
        return Task.FromResult(known);
    }
}

/// <summary>
/// IEventBus fake that runs subscribed handlers INLINE on publish (the real
/// EventBus fans out through a background channel, which would make
/// "was it published?" assertions racy) and records every published event so
/// tests can assert on payloads. Subscribe/unsubscribe bookkeeping is real so
/// lifecycle tests can count live subscriptions.
/// </summary>
public sealed class Wave15DispatchingEventBus : IEventBus
{
    private readonly Dictionary<Type, List<(Guid Id, Delegate Handler)>> _subscriptions = new();

    public List<object> Published { get; } = new();

    public int SubscriptionCount => _subscriptions.Values.Sum(list => list.Count);

    public async Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
        where TEvent : IEvent
    {
        ArgumentNullException.ThrowIfNull(@event);
        Published.Add(@event);

        if (!_subscriptions.TryGetValue(typeof(TEvent), out var handlers) || handlers.Count == 0)
        {
            return;
        }

        // Snapshot: a handler may subscribe/unsubscribe while we dispatch.
        foreach (var (_, handler) in handlers.ToList())
        {
            var task = (Task)handler.DynamicInvoke(@event)!;
            await task;
        }
    }

    public Task<Guid> SubscribeAsync<TEvent>(Func<TEvent, Task> handler, CancellationToken cancellationToken = default)
        where TEvent : IEvent
    {
        ArgumentNullException.ThrowIfNull(handler);
        var id = Guid.NewGuid();
        if (!_subscriptions.TryGetValue(typeof(TEvent), out var handlers))
        {
            handlers = new List<(Guid, Delegate)>();
            _subscriptions[typeof(TEvent)] = handlers;
        }

        handlers.Add((id, handler));
        return Task.FromResult(id);
    }

    public void Unsubscribe(Guid subscriptionId)
    {
        foreach (var handlers in _subscriptions.Values)
        {
            handlers.RemoveAll(entry => entry.Id == subscriptionId);
        }
    }
}
