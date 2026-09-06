using VyralBot.Core.Services.EventBus;

namespace VyralBot.Core.Services.EventBus.Events;

/// <summary>
/// Published when a giveaway starts accepting entries.
/// </summary>
public record GiveawayStartedEvent : EventBase
{
    /// <summary>ID of the giveaway that started.</summary>
    public required Guid GiveawayId { get; init; }

    /// <summary>Display title of the giveaway.</summary>
    public required string Title { get; init; }

    /// <summary>Chat keyword that enters viewers into the giveaway.</summary>
    public required string EntryKeyword { get; init; }
}

/// <summary>
/// Published when a viewer enters a giveaway.
/// </summary>
public record GiveawayEntryEvent : EventBase
{
    /// <summary>ID of the giveaway that received the entry.</summary>
    public required Guid GiveawayId { get; init; }

    /// <summary>Username of the entrant.</summary>
    public required string Username { get; init; }

    /// <summary>Platform identity string the entry came from.</summary>
    public required string Platform { get; init; }

    /// <summary>Total number of entries received so far.</summary>
    public required int TotalEntries { get; init; }

    /// <summary>Total number of distinct participants so far.</summary>
    public required int UniqueParticipants { get; init; }

    /// <summary>Whether the giveaway is in timed intake mode.</summary>
    public required bool IsTimed { get; init; }
}

/// <summary>
/// Published when winners have been drawn for a giveaway.
/// </summary>
public record GiveawayWinnersDrawnEvent : EventBase
{
    /// <summary>ID of the giveaway the winners were drawn for.</summary>
    public required Guid GiveawayId { get; init; }

    /// <summary>Display title of the giveaway.</summary>
    public required string Title { get; init; }

    /// <summary>Ready-to-send chat announcement naming the winners.</summary>
    public required string WinnersText { get; init; }
}

/// <summary>
/// Published when a giveaway is cancelled before a draw.
/// </summary>
public record GiveawayCancelledEvent : EventBase
{
    /// <summary>ID of the cancelled giveaway.</summary>
    public required Guid GiveawayId { get; init; }

    /// <summary>Display title of the cancelled giveaway.</summary>
    public required string Title { get; init; }
}

/// <summary>
/// Published when a giveaway switches its entry intake mode.
/// </summary>
public record GiveawayIntakeModeChangedEvent : EventBase
{
    /// <summary>ID of the giveaway whose intake mode changed.</summary>
    public required Guid GiveawayId { get; init; }

    /// <summary>New intake mode: "Live" or "Timed".</summary>
    public required string Mode { get; init; }

    /// <summary>Entries accepted per minute while in "Timed" mode.</summary>
    public required int EntriesPerMinute { get; init; }
}

/// <summary>
/// Thread-safe rolling one-minute window used to rate-limit giveaway entries
/// during "Timed" intake mode. Pure in-memory helper (public test target).
/// </summary>
public sealed class EntryRateWindow
{
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    private readonly object _lock = new();
    private readonly Queue<DateTime> _timestamps = new();

    /// <summary>Records an entry occurrence at the given UTC time.</summary>
    public void Record(DateTime utcNow)
    {
        lock (_lock)
        {
            _timestamps.Enqueue(utcNow);
            Prune(utcNow);
        }
    }

    /// <summary>Counts the entries recorded within the last minute relative to <paramref name="utcNow"/>.</summary>
    public int CountInLastMinute(DateTime utcNow)
    {
        lock (_lock)
        {
            Prune(utcNow);
            return _timestamps.Count;
        }
    }

    private void Prune(DateTime utcNow)
    {
        while (_timestamps.Count > 0 && utcNow - _timestamps.Peek() >= Window)
        {
            _timestamps.Dequeue();
        }
    }
}
