using VyralBot.Core.Services.Linking;
using Xunit;

namespace VyralBot.UI.Tests;

public class MultiPlatformBonusLadderTests
{
    [Fact]
    public void Resolve_OnePlatform_Returns1()
    {
        Assert.Equal(1.0, MultiPlatformBonusLadder.Resolve(1, 1.25, 1.5, 2.0, 2.5));
    }

    [Fact]
    public void Resolve_TwoPlatforms_ReturnsBonus2()
    {
        Assert.Equal(1.25, MultiPlatformBonusLadder.Resolve(2, 1.25, 1.5, 2.0, 2.5));
    }

    [Fact]
    public void Resolve_ThreePlatforms_ReturnsBonus3()
    {
        Assert.Equal(1.5, MultiPlatformBonusLadder.Resolve(3, 1.25, 1.5, 2.0, 2.5));
    }

    [Fact]
    public void Resolve_FourPlatforms_ReturnsBonus4()
    {
        Assert.Equal(2.0, MultiPlatformBonusLadder.Resolve(4, 1.25, 1.5, 2.0, 2.5));
    }

    [Fact]
    public void Resolve_SevenPlatforms_ReturnsBonus5()
    {
        Assert.Equal(2.5, MultiPlatformBonusLadder.Resolve(7, 1.25, 1.5, 2.0, 2.5));
    }

    [Fact]
    public void Resolve_DefaultLadder_ReturnsConfiguredBonuses()
    {
        // The documented default ladder from MultiPlatformBonusService:
        // Bonus2=1.25, Bonus3=1.5, Bonus4=2.0, Bonus5=2.5 (5+ platforms).
        Assert.Equal(1.25, MultiPlatformBonusLadder.Resolve(2, 1.25, 1.5, 2.0, 2.5));
        Assert.Equal(1.5, MultiPlatformBonusLadder.Resolve(3, 1.25, 1.5, 2.0, 2.5));
        Assert.Equal(2.0, MultiPlatformBonusLadder.Resolve(4, 1.25, 1.5, 2.0, 2.5));
        Assert.Equal(2.5, MultiPlatformBonusLadder.Resolve(5, 1.25, 1.5, 2.0, 2.5));
    }
}
