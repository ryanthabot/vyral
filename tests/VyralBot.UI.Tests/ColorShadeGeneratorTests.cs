using System.Windows.Media;
using VyralBot.UI.Models;
using Xunit;

namespace VyralBot.UI.Tests;

public class ColorShadeGeneratorTests
{
    [Fact]
    public void Hover_is_lighter_than_base()
        => Assert.True(CompareLightness(ColorShadeGenerator.Hover(Colors.Teal), Colors.Teal) > 0);

    [Fact]
    public void Pressed_is_darker_than_base()
        => Assert.True(CompareLightness(ColorShadeGenerator.Pressed(Colors.Teal), Colors.Teal) < 0);

    [Fact]
    public void Light_is_lighter_and_Dark_is_darker()
    {
        var teal = Color.FromRgb(0x14, 0xB8, 0xA6);
        Assert.True(CompareLightness(ColorShadeGenerator.Light(teal), teal) > 0);
        Assert.True(CompareLightness(ColorShadeGenerator.Dark(teal), teal) < 0);
    }

    [Fact]
    public void Hover_on_very_light_color_stays_lighter_or_equal()
    {
        var nearWhite = Color.FromRgb(0xF5, 0xF5, 0xF5);
        var hover = ColorShadeGenerator.Hover(nearWhite);
        Assert.True(CompareLightness(hover, nearWhite) >= 0);
    }

    [Fact]
    public void TryParseHex_accepts_six_digit_hex()
    {
        Assert.True(ColorShadeGenerator.TryParseHex("#14B8A6", out var c));
        Assert.Equal(0x14, c.R);
        Assert.Equal(0xB8, c.G);
        Assert.Equal(0xA6, c.B);
    }

    [Fact]
    public void TryParseHex_accepts_eight_digit_hex_and_rejects_garbage()
    {
        Assert.True(ColorShadeGenerator.TryParseHex("#FF14B8A6", out _));
        Assert.False(ColorShadeGenerator.TryParseHex("hello", out _));
        Assert.False(ColorShadeGenerator.TryParseHex(null, out _));
    }

    [Fact]
    public void ToHex_round_trips_through_TryParseHex()
    {
        var original = Color.FromRgb(0x14, 0xB8, 0xA6);
        Assert.True(ColorShadeGenerator.TryParseHex(ColorShadeGenerator.ToHex(original), out var parsed));
        Assert.Equal(original, parsed);
    }

    private static double CompareLightness(Color a, Color b) => Lightness(a) - Lightness(b);
    private static double Lightness(Color c) => (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;
}
