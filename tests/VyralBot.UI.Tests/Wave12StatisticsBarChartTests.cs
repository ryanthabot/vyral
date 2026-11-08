using VyralBot.Core.Services.Statistics;
using VyralBot.UI.Controls.Charts;
using VyralBot.UI.ViewModels;
using Xunit;

namespace VyralBot.UI.Tests;

/// <summary>
/// Wave-12 coverage for the statistics bar charts (KI-54): the pure
/// aggregation/normalization half of
/// <see cref="StatisticsChartBuilder"/> — value normalization to the max row,
/// deterministic ordering, top-N capping and platform canonicalization. The
/// rendering half (<see cref="SimpleBarChart"/>) is XAML-only.
/// </summary>
public class Wave12StatisticsBarChartTests
{
    private static void AssertBars(IReadOnlyList<BarChartItem> bars, params (string Label, double Fraction)[] expected)
    {
        Assert.Equal(expected.Length, bars.Count);
        for (var i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i].Label, bars[i].Label);
            Assert.Equal(expected[i].Fraction, bars[i].Fraction, 5);
        }
    }

    [Fact]
    public void Normalization_MaxRowIsOne_OthersProportional()
    {
        var bars = StatisticsChartBuilder.BuildContributorChartItems(new[]
        {
            new UserStatistic { Username = "alice", Platform = "Twitch", Count = 100 },
            new UserStatistic { Username = "bob", Platform = "Kick", Count = 50 },
            new UserStatistic { Username = "carol", Platform = "Twitch", Count = 25 }
        });

        AssertBars(bars, ("alice", 1.0), ("bob", 0.5), ("carol", 0.25));
        Assert.Equal(100, bars[0].Value);
    }

    [Fact]
    public void Normalization_EmptyInput_NoDivisionByZero()
    {
        var bars = StatisticsChartBuilder.BuildContributorChartItems(Array.Empty<UserStatistic>());

        Assert.Empty(bars);
    }

    [Fact]
    public void Normalization_ZeroAndNegativeCountsDropped()
    {
        var bars = StatisticsChartBuilder.BuildContributorChartItems(new[]
        {
            new UserStatistic { Username = "alice", Count = 10 },
            new UserStatistic { Username = "ghost", Count = 0 },
            new UserStatistic { Username = "negative", Count = -3 }
        });

        AssertBars(bars, ("alice", 1.0));
    }

    [Fact]
    public void Ordering_ValueDescending_LabelOrdinalTieBreak()
    {
        var bars = StatisticsChartBuilder.BuildContributorChartItems(new[]
        {
            new UserStatistic { Username = "zoe", Count = 10 },
            new UserStatistic { Username = "anna", Count = 30 },
            new UserStatistic { Username = "mila", Count = 10 },
            new UserStatistic { Username = "bella", Count = 10 }
        });

        // Ties at 10 resolve alphabetically: "bella" < "mila" < "zoe".
        AssertBars(
            bars,
            ("anna", 1.0),
            ("bella", 1.0 / 3),
            ("mila", 1.0 / 3),
            ("zoe", 1.0 / 3));
    }

    [Fact]
    public void TopN_CapsAtMaxItems_AfterOrdering()
    {
        var contributors = Enumerable.Range(1, 12)
            .Select(i => new UserStatistic { Username = $"user{i:00}", Count = i })
            .ToList();

        var bars = StatisticsChartBuilder.BuildContributorChartItems(contributors);

        Assert.Equal(8, bars.Count);
        // Highest counts survive (12..5) and the leader normalizes to 1.
        Assert.Equal("user12", bars[0].Label);
        Assert.Equal(1.0, bars[0].Fraction, 5);
        Assert.Equal("user05", bars[7].Label);
    }

    [Fact]
    public void EventTypeChart_AggregatesAcrossPlatforms_ColorsByDominantPlatform()
    {
        var rows = new[]
        {
            new EventCountSummary { Platform = "Twitch", EventType = "Follow", Count = 30 },
            new EventCountSummary { Platform = "Kick", EventType = "Follow", Count = 10 },
            new EventCountSummary { Platform = "Kick", EventType = "Cheer", Count = 40 }
        };

        var bars = StatisticsChartBuilder.BuildEventTypeChartItems(rows);

        // "Cheer" (40) beats the aggregated "Follow" total (30+10).
        Assert.Equal(2, bars.Count);
        Assert.Equal("Cheer", bars[0].Label);
        Assert.Equal(40, bars[0].Value);
        Assert.Equal("Kick", bars[0].Platform);
        Assert.Equal("Follow", bars[1].Label);
        Assert.Equal(40, bars[1].Value); // 30 + 10 summed across platforms
        Assert.Equal("Twitch", bars[1].Platform); // dominant (30 > 10)
        Assert.Equal(1.0, bars[1].Fraction, 5); // bar width reflects the aggregated total (40/40); the dominant platform only picks the color
    }

    [Fact]
    public void EventTypeChart_DominantPlatformTieBreaksOrdinal()
    {
        var rows = new[]
        {
            new EventCountSummary { Platform = "Twitch", EventType = "Follow", Count = 5 },
            new EventCountSummary { Platform = "Kick", EventType = "Follow", Count = 5 }
        };

        var bars = StatisticsChartBuilder.BuildEventTypeChartItems(rows);

        var bar = Assert.Single(bars);
        Assert.Equal(10, bar.Value);
        // 5 == 5 → ordinal platform tie-break: "Kick" < "Twitch".
        Assert.Equal("Kick", bar.Platform);
    }

    [Fact]
    public void PlatformCasing_CanonicalizedForColorTriggers()
    {
        var bars = StatisticsChartBuilder.BuildContributorChartItems(new[]
        {
            new UserStatistic { Username = "u", Platform = "YOUTUBE", Count = 1 },
            new UserStatistic { Username = "v", Platform = "unknown-platform", Count = 2 }
        });

        Assert.Equal("YouTube", bars[1].Platform); // trigger-matched canonical casing
        Assert.Equal("unknown-platform", bars[0].Platform); // passthrough → PrimaryBrush fallback
        Assert.Equal(string.Empty, StatisticsChartBuilder.CanonicalPlatform(null));
    }

    [Fact]
    public void BarChartItem_ValueAndTooltipText_CarryExactValue()
    {
        var bars = StatisticsChartBuilder.BuildContributorChartItems(new[]
        {
            new UserStatistic { Username = "alice", Platform = "Twitch", Count = 1234567 }
        });

        var bar = Assert.Single(bars);
        Assert.Equal("1234567", bar.ValueText.Replace(",", string.Empty).Replace(".", string.Empty)
                                             .Replace(" ", string.Empty));
        Assert.StartsWith("alice", bar.ToolTipText, StringComparison.Ordinal);
        Assert.EndsWith("(Twitch)", bar.ToolTipText, StringComparison.Ordinal);
    }
}
