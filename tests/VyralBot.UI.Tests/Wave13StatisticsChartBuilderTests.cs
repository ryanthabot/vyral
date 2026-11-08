using VyralBot.Core.Services.Statistics;
using VyralBot.UI.Controls.Charts;
using VyralBot.UI.ViewModels;
using Xunit;

namespace VyralBot.UI.Tests;

/// <summary>
/// Wave-13 depth pass over <see cref="StatisticsChartBuilder"/> (wave-12, KI-54),
/// complementing Wave12StatisticsBarChartTests: event-chart aggregation with
/// mixed-casing event types, dominant-platform selection by row COUNT (not row
/// presence), top-N cap applied after ordering, degenerate caps, empty /
/// single-event normalization, and the full platform canonicalization matrix.
/// </summary>
public class Wave13StatisticsChartBuilderTests
{
    private static EventCountSummary Row(string platform, string eventType, int count)
        => new() { Platform = platform, EventType = eventType, Count = count };

    // ==================================================================
    // Event-type chart: aggregation across platforms
    // ==================================================================

    [Fact]
    public void EventTypeChart_EmptyInput_ReturnsEmpty()
    {
        Assert.Empty(StatisticsChartBuilder.BuildEventTypeChartItems(Array.Empty<EventCountSummary>()));
    }

    [Fact]
    public void EventTypeChart_SingleEvent_FractionIsOne()
    {
        var bars = StatisticsChartBuilder.BuildEventTypeChartItems(new[] { Row("Twitch", "Follow", 7) });

        var bar = Assert.Single(bars);
        Assert.Equal("Follow", bar.Label);
        Assert.Equal("Twitch", bar.Platform);
        Assert.Equal(7, bar.Value);
        Assert.Equal(1.0, bar.Fraction, 5); // leader normalizes to a full-width bar
    }

    [Fact]
    public void EventTypeChart_SingleContributor_FractionIsOne()
    {
        var bars = StatisticsChartBuilder.BuildContributorChartItems(new[]
        {
            new UserStatistic { Username = "solo", Platform = "Kick", Count = 3 }
        });

        var bar = Assert.Single(bars);
        Assert.Equal(1.0, bar.Fraction, 5);
    }

    [Fact]
    public void EventTypeChart_AggregatesSameTypeAcrossPlatformCasing()
    {
        // "Follow"/"follow"/"FOLLOW" are one event type (OrdinalIgnoreCase grouping);
        // the group label is the first-seen spelling.
        var bars = StatisticsChartBuilder.BuildEventTypeChartItems(new[]
        {
            Row("Twitch", "Follow", 3),
            Row("Kick", "follow", 2),
            Row("Discord", "FOLLOW", 1)
        });

        var bar = Assert.Single(bars);
        Assert.Equal("Follow", bar.Label);
        Assert.Equal(6, bar.Value); // 3 + 2 + 1 summed across platforms
        Assert.Equal("Twitch", bar.Platform); // dominant row (3 > 2 > 1)
        Assert.Equal(1.0, bar.Fraction, 5);
    }

    [Fact]
    public void EventTypeChart_DominantPlatformPickedByRowCount_NotRowPresence()
    {
        // Kick contributes TWO rows (2+2) but its best row is 2; Twitch's single
        // row has the highest count (7), so Twitch colors the bar.
        var bars = StatisticsChartBuilder.BuildEventTypeChartItems(new[]
        {
            Row("Kick", "Cheer", 2),
            Row("Kick", "Cheer", 2),
            Row("Twitch", "Cheer", 7)
        });

        var bar = Assert.Single(bars);
        Assert.Equal(11, bar.Value);
        Assert.Equal("Twitch", bar.Platform);
    }

    [Fact]
    public void EventTypeChart_DominantTie_ResolvesToOrdinalLowestPlatform()
    {
        // Three-way count tie (4 each): ordinal platform tie-break picks "Kick".
        var bars = StatisticsChartBuilder.BuildEventTypeChartItems(new[]
        {
            Row("Twitch", "Raid", 4),
            Row("YouTube", "Raid", 4),
            Row("Kick", "Raid", 4)
        });

        var bar = Assert.Single(bars);
        Assert.Equal(12, bar.Value);
        Assert.Equal("Kick", bar.Platform);
    }

    [Fact]
    public void EventTypeChart_ZeroCountRowsDropped_AllZeroInputStaysEmpty()
    {
        var bars = StatisticsChartBuilder.BuildEventTypeChartItems(new[]
        {
            Row("Twitch", "Follow", 0),
            Row("Kick", "Cheer", 10),
            Row("Kick", "Raid", 0)
        });

        var bar = Assert.Single(bars);
        Assert.Equal("Cheer", bar.Label);

        // Entirely zero input: no bars and no divide-by-zero on the max.
        Assert.Empty(StatisticsChartBuilder.BuildEventTypeChartItems(new[]
        {
            Row("Twitch", "Follow", 0),
            Row("Twitch", "Cheer", 0)
        }));
    }

    // ==================================================================
    // Top-N cap (applied after ordering, so the largest values survive)
    // ==================================================================

    [Fact]
    public void EventTypeChart_TopEightCap_KeepsLargestValues()
    {
        var rows = Enumerable.Range(1, 10)
            .Select(i => Row("Twitch", $"Type{i:00}", i * 10))
            .ToList();

        var bars = StatisticsChartBuilder.BuildEventTypeChartItems(rows);

        Assert.Equal(8, bars.Count);
        // Descending: Type10..Type03 survive, Type02/Type01 are cut.
        Assert.Equal("Type10", bars[0].Label);
        Assert.Equal("Type03", bars[7].Label);
        Assert.DoesNotContain(bars, b => b.Label is "Type02" or "Type01");
        Assert.Equal(1.0, bars[0].Fraction, 5);
        Assert.Equal(0.3, bars[7].Fraction, 5); // 30 / 100
    }

    [Fact]
    public void EventTypeChart_CustomCapThree()
    {
        var rows = new[]
        {
            Row("Twitch", "A", 5),
            Row("Twitch", "B", 50),
            Row("Twitch", "C", 10),
            Row("Twitch", "D", 20)
        };

        var bars = StatisticsChartBuilder.BuildEventTypeChartItems(rows, maxItems: 3);

        Assert.Equal(new[] { "B", "D", "C" }, bars.Select(b => b.Label));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void EventTypeChart_DegenerateCap_ReturnsEmpty(int maxItems)
    {
        var bars = StatisticsChartBuilder.BuildEventTypeChartItems(new[] { Row("Twitch", "Follow", 9) }, maxItems);

        Assert.Empty(bars);
    }

    // ==================================================================
    // Platform canonicalization (feeds the chart's DataTrigger colors)
    // ==================================================================

    [Theory]
    [InlineData("twitch", "Twitch")]
    [InlineData("TWITCH", "Twitch")]
    [InlineData("Kick", "Kick")]
    [InlineData("KICK", "Kick")]
    [InlineData("youtube", "YouTube")]
    [InlineData("YouTube", "YouTube")]
    [InlineData("YOUTUBE", "YouTube")]
    [InlineData("discord", "Discord")]
    [InlineData("tiktok", "TikTok")]
    [InlineData("TikTok", "TikTok")]
    [InlineData("TIKTOK", "TikTok")]
    public void CanonicalPlatform_KnownPlatforms_CanonicalCasing(string input, string expected)
    {
        Assert.Equal(expected, StatisticsChartBuilder.CanonicalPlatform(input));
    }

    [Theory]
    [InlineData("trovo")]
    [InlineData("Rumble")]
    [InlineData("facebook")]
    public void CanonicalPlatform_UnknownPlatform_PassesThrough(string input)
    {
        // Unknown names pass through unchanged (chart falls back to PrimaryBrush).
        Assert.Equal(input, StatisticsChartBuilder.CanonicalPlatform(input));
    }

    [Fact]
    public void CanonicalPlatform_NullAndEmpty_ReturnEmpty()
    {
        Assert.Equal(string.Empty, StatisticsChartBuilder.CanonicalPlatform(null));
        Assert.Equal(string.Empty, StatisticsChartBuilder.CanonicalPlatform(string.Empty));
    }

    [Fact]
    public void EventTypeChart_DominantPlatformCanonicalizedForColorTrigger()
    {
        // The dominant row's raw platform casing must still land on the
        // DataTrigger-matched canonical spelling.
        var bars = StatisticsChartBuilder.BuildEventTypeChartItems(new[]
        {
            Row("KICK", "Follow", 5),
            Row("twitch", "Follow", 1)
        });

        var bar = Assert.Single(bars);
        Assert.Equal("Kick", bar.Platform);
    }
}
