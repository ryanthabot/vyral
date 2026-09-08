using VyralBot.Integrations.Obs.Engine;
using Xunit;

namespace VyralBot.UI.Tests;

/// <summary>
/// Scene-collection string → int enum mapping for the deinterlace setters.
/// Every expected value is read off obs.h @ 32.1.1 (enum obs_deinterlace_mode
/// lines 339-348, enum obs_deinterlace_field_order lines 350-353).
/// </summary>
public class ObsDeinterlaceTests
{
    [Theory]
    [InlineData("disable", 0)]
    [InlineData("discard", 1)]
    [InlineData("retro", 2)]
    [InlineData("blend", 3)]
    [InlineData("blend_2x", 4)]
    [InlineData("linear", 5)]
    [InlineData("linear_2x", 6)]
    [InlineData("yadif", 7)]
    [InlineData("yadif_2x", 8)]
    public void ParseMode_MapsObsCollectionStrings(string value, int expected)
    {
        Assert.Equal(expected, ObsDeinterlace.ParseMode(value));
    }

    [Theory]
    [InlineData("top", 0)]
    [InlineData("bottom", 1)]
    [InlineData("Bottom", 1)] // case-insensitive
    public void ParseFieldOrder_MapsObsCollectionStrings(string value, int expected)
    {
        Assert.Equal(expected, ObsDeinterlace.ParseFieldOrder(value));
    }

    [Fact]
    public void Parse_AbsentOrUnknownValue_ReturnsNullSoExecutorSkips()
    {
        Assert.Null(ObsDeinterlace.ParseMode(null));
        Assert.Null(ObsDeinterlace.ParseMode(""));
        Assert.Null(ObsDeinterlace.ParseMode("gui"));
        Assert.Null(ObsDeinterlace.ParseFieldOrder(null));
        Assert.Null(ObsDeinterlace.ParseFieldOrder("left"));
    }
}
