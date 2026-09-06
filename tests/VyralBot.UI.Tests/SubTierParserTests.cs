using VyralBot.Core.Services.Linking;
using Xunit;

namespace VyralBot.UI.Tests;

public class SubTierParserTests
{
    [Fact]
    public void Parse_NumericTier1000_Returns1()
    {
        Assert.Equal(1, SubTierParser.Parse("1000"));
    }

    [Fact]
    public void Parse_NumericTier2000_Returns2()
    {
        Assert.Equal(2, SubTierParser.Parse("2000"));
    }

    [Fact]
    public void Parse_NumericTier3000_Returns3()
    {
        Assert.Equal(3, SubTierParser.Parse("3000"));
    }

    [Fact]
    public void Parse_PrimePascalCase_Returns1()
    {
        Assert.Equal(1, SubTierParser.Parse("Prime"));
    }

    [Fact]
    public void Parse_PrimeLowerCase_Returns1()
    {
        Assert.Equal(1, SubTierParser.Parse("prime"));
    }

    [Fact]
    public void Parse_Null_Returns0()
    {
        Assert.Equal(0, SubTierParser.Parse(null));
    }

    [Fact]
    public void Parse_Empty_Returns0()
    {
        Assert.Equal(0, SubTierParser.Parse(""));
    }

    [Fact]
    public void Parse_UnknownValue999_Returns0()
    {
        Assert.Equal(0, SubTierParser.Parse("999"));
    }
}
