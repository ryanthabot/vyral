using System.Collections.Concurrent;

namespace VyralBot.Core.Services.Performance;

/// <summary>
/// Default <see cref="IMetricsRegistry"/>: per-(source, metric) cumulative
/// counters updated with Interlocked operations. Sources are things like
/// "Twitch", "EventBus", "Workflows"; metrics like "events" or "runs".
/// </summary>
public sealed class MetricsRegistry : IMetricsRegistry
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, StrongBox>> _counters = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, StrongBox> _durationTotals = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, StrongBox> _durationCounts = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public void Increment(string source, string metric, long amount = 1)
    {
        if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(metric)) return;

        var metrics = _counters.GetOrAdd(source, _ => new ConcurrentDictionary<string, StrongBox>(StringComparer.Ordinal));
        var box = metrics.GetOrAdd(metric, _ => new StrongBox());
        Interlocked.Add(ref box.Value, amount);
    }

    /// <inheritdoc />
    public void RecordDuration(string source, double durationMs)
    {
        if (string.IsNullOrEmpty(source)) return;

        // Two boxes so each aggregate stays a single Interlocked.Add; the pair
        // is only ever read together on the UI thread, so slight skew between
        // count and total on concurrent writes is acceptable (next tick heals).
        var total = _durationTotals.GetOrAdd(source, _ => new StrongBox());
        var count = _durationCounts.GetOrAdd(source, _ => new StrongBox());
        Interlocked.Add(ref total.Value, (long)Math.Round(durationMs * 1000)); // 0.001ms resolution
        Interlocked.Increment(ref count.Value);
    }

    /// <inheritdoc />
    public MetricsSnapshot GetSnapshot()
    {
        var counters = new Dictionary<string, IReadOnlyDictionary<string, long>>(_counters.Count, StringComparer.Ordinal);
        foreach (var (source, metrics) in _counters)
        {
            var copy = new Dictionary<string, long>(metrics.Count, StringComparer.Ordinal);
            foreach (var (metric, box) in metrics)
            {
                copy[metric] = Interlocked.Read(ref box.Value);
            }
            counters[source] = copy;
        }

        var durations = new Dictionary<string, DurationStats>(_durationCounts.Count, StringComparer.Ordinal);
        foreach (var (source, countBox) in _durationCounts)
        {
            var count = Interlocked.Read(ref countBox.Value);
            var totalMilli = _durationTotals.TryGetValue(source, out var totalBox)
                ? Interlocked.Read(ref totalBox.Value) / 1000.0
                : 0;
            durations[source] = new DurationStats(count, totalMilli);
        }

        return new MetricsSnapshot
        {
            TimestampUtc = DateTime.UtcNow,
            Counters = counters,
            Durations = durations,
        };
    }

    /// <summary>Mutable long holder so Interlocked works on dictionary values.</summary>
    private sealed class StrongBox
    {
        public long Value;
    }
}
