namespace VyralBot.Core.Services.Performance;

/// <summary>
/// Average duration bookkeeping for one (source) dimension.
/// </summary>
/// <param name="Count">Number of recorded durations.</param>
/// <param name="TotalMs">Sum of all recorded durations, in milliseconds.</param>
public readonly record struct DurationStats(long Count, double TotalMs)
{
    /// <summary>Mean duration in milliseconds (0 when nothing recorded).</summary>
    public double AverageMs => Count > 0 ? TotalMs / Count : 0;
}

/// <summary>
/// Immutable point-in-time copy of every counter and duration total in the
/// registry. UI code reads this; producers keep writing without contention.
/// </summary>
public sealed record MetricsSnapshot
{
    /// <summary>When the snapshot was taken.</summary>
    public required DateTime TimestampUtc { get; init; }

    /// <summary>source → metric → cumulative count.</summary>
    public required IReadOnlyDictionary<string, IReadOnlyDictionary<string, long>> Counters { get; init; }

    /// <summary>source → (count, total ms) duration aggregates.</summary>
    public required IReadOnlyDictionary<string, DurationStats> Durations { get; init; }

    /// <summary>Gets a cumulative counter, or 0 when the source/metric pair is unknown.</summary>
    public long GetCounter(string source, string metric) =>
        Counters.TryGetValue(source, out var metrics) && metrics.TryGetValue(metric, out var value)
            ? value
            : 0;

    /// <summary>Sum of one metric across every source (e.g. total events published).</summary>
    public long GetCounterSum(string metric) =>
        Counters.Values.Sum(m => m.TryGetValue(metric, out var value) ? value : 0);
}

/// <summary>
/// Thread-safe counter store for live performance telemetry. Instrumentation
/// points call <see cref="Increment"/>/<see cref="RecordDuration"/> on hot
/// paths (Interlocked ops, no locks, no allocations); the UI takes immutable
/// <see cref="MetricsSnapshot"/> copies once per refresh.
/// </summary>
public interface IMetricsRegistry
{
    /// <summary>Adds <paramref name="amount"/> to the (source, metric) counter.</summary>
    void Increment(string source, string metric, long amount = 1);

    /// <summary>Records one duration sample for the source (averaged in the UI).</summary>
    void RecordDuration(string source, double durationMs);

    /// <summary>Returns an immutable copy of all counters and duration totals.</summary>
    MetricsSnapshot GetSnapshot();
}
