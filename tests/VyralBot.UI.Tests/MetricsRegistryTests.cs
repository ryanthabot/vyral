using VyralBot.Core.Services.Performance;
using Xunit;

namespace VyralBot.UI.Tests;

public class MetricsRegistryTests
{
    [Fact]
    public void Increment_AccumulatesPerSourceAndMetric()
    {
        var registry = new MetricsRegistry();

        registry.Increment("Twitch", "events");
        registry.Increment("Twitch", "events");
        registry.Increment("Kick", "events");
        registry.Increment("Twitch", "events:ChatMessageReceivedEvent", 5);

        var snapshot = registry.GetSnapshot();
        Assert.Equal(2, snapshot.GetCounter("Twitch", "events"));
        Assert.Equal(1, snapshot.GetCounter("Kick", "events"));
        Assert.Equal(5, snapshot.GetCounter("Twitch", "events:ChatMessageReceivedEvent"));
    }

    [Fact]
    public void GetCounterSum_TotalsMetricAcrossSources()
    {
        var registry = new MetricsRegistry();

        registry.Increment("Twitch", "events", 2);
        registry.Increment("Kick", "events", 3);
        registry.Increment("EventBus", "published", 10);

        Assert.Equal(5, registry.GetSnapshot().GetCounterSum("events"));
        Assert.Equal(10, registry.GetSnapshot().GetCounterSum("published"));
    }

    [Fact]
    public void GetCounter_UnknownSourceOrMetric_ReturnsZero()
    {
        var registry = new MetricsRegistry();

        var snapshot = registry.GetSnapshot();
        Assert.Equal(0, snapshot.GetCounter("Nope", "events"));
        Assert.Equal(0, snapshot.GetCounter("Twitch", "nope"));
    }

    [Fact]
    public void RecordDuration_AveragesSamples()
    {
        var registry = new MetricsRegistry();

        registry.RecordDuration("Workflows", 100);
        registry.RecordDuration("Workflows", 200);
        registry.RecordDuration("Workflows", 300);

        var snapshot = registry.GetSnapshot();
        var stats = snapshot.Durations["Workflows"];
        Assert.Equal(3, stats.Count);
        Assert.Equal(200, stats.AverageMs, 3);
    }

    [Fact]
    public void GetSnapshot_ReturnsIsolatedCopy()
    {
        var registry = new MetricsRegistry();
        registry.Increment("Twitch", "events", 1);

        var snapshot = registry.GetSnapshot();
        registry.Increment("Twitch", "events", 1);

        // The snapshot must not see later writes.
        Assert.Equal(1, snapshot.GetCounter("Twitch", "events"));
        Assert.Equal(2, registry.GetSnapshot().GetCounter("Twitch", "events"));
    }

    [Fact]
    public void Increment_IgnoresEmptySourceOrMetric()
    {
        var registry = new MetricsRegistry();

        registry.Increment("", "events");
        registry.Increment("Twitch", "");

        Assert.Equal(0, registry.GetSnapshot().GetCounterSum("events"));
    }

    [Fact]
    public void SampleOnce_ProducesSaneTelemetry()
    {
        var service = new PerformanceTelemetryService(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<PerformanceTelemetryService>.Instance);

        var sample = service.SampleOnce();

        Assert.True(sample.WorkingSetMb > 0);
        Assert.True(sample.PrivateMemoryMb > 0);
        Assert.True(sample.ThreadCount > 0);
        Assert.True(sample.CpuPercent >= 0);
        Assert.True(sample.HeapBytes >= 0);
        Assert.Equal(1, service.History.Count);
        Assert.Equal(sample.TimestampUtc, service.Latest.TimestampUtc);
    }
}
