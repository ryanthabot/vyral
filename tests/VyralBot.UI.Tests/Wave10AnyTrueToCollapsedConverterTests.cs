using System.Globalization;
using System.Windows;
using VyralBot.UI.Converters;
using Xunit;

namespace VyralBot.UI.Tests;

/// <summary>
/// Wave-10 coverage for the wave-9 HOME "GET STARTED" STRIP converter
/// (<see cref="AnyTrueToCollapsedConverter"/>): Collapsed as soon as ANY
/// bound boolean is true (strip dismissed OR any platform connected),
/// Visible while all are false — with the WPF realities of multi-binding:
/// null bindings, non-boolean values, empty value arrays.
/// </summary>
public class Wave10AnyTrueToCollapsedConverterTests
{
    private readonly AnyTrueToCollapsedConverter _converter = new();

    private object Convert(params object?[] values)
        => _converter.Convert(values, typeof(Visibility), null!, CultureInfo.InvariantCulture);

    // ------------------------------------------------------------------
    // The core contract
    // ------------------------------------------------------------------

    [Theory]
    [InlineData(new[] { true }, Visibility.Collapsed)]
    [InlineData(new[] { false }, Visibility.Visible)]
    [InlineData(new[] { false, false, false }, Visibility.Visible)]
    [InlineData(new[] { true, false, false }, Visibility.Collapsed)]
    [InlineData(new[] { false, true, false }, Visibility.Collapsed)]
    [InlineData(new[] { false, false, true }, Visibility.Collapsed)]
    [InlineData(new[] { true, true, true }, Visibility.Collapsed)]
    public void AnyTrue_Collapses_AllFalse_StaysVisible(bool[] values, Visibility expected)
    {
        Assert.Equal(expected, Convert(values.Cast<object>().ToArray()));
    }

    [Fact]
    public void EmptyValueArray_StaysVisible()
    {
        // Nothing bound yet (e.g. before the parent DataContext lands) —
        // the strip shows, it never vanishes on a binding hiccup.
        Assert.Equal(Visibility.Visible, Convert());
    }

    // ------------------------------------------------------------------
    // Mixed / null / non-boolean values (WPF multi-binding realities)
    // ------------------------------------------------------------------

    [Fact]
    public void Nulls_AreTreatedAsFalse()
    {
        Assert.Equal(Visibility.Visible, Convert(null, null, null));
    }

    [Fact]
    public void NullBesideTrue_Collapses()
    {
        Assert.Equal(Visibility.Collapsed, Convert(null, true, null));
        Assert.Equal(Visibility.Collapsed, Convert(true, null));
    }

    [Fact]
    public void NonBooleanValues_AreIgnored()
    {
        // Unset bindings can arrive as DependencyProperty.UnsetValue, boxed
        // ints or strings — only actual bools participate.
        Assert.Equal(Visibility.Visible, Convert("true", 1, System.Windows.DependencyProperty.UnsetValue));
        Assert.Equal(Visibility.Collapsed, Convert("false", 0, false, true));
    }

    [Fact]
    public void BoxedBoolean_TrueCollapses_FalseDoesNot()
    {
        Assert.Equal(Visibility.Collapsed, Convert(false, box(true)));
        Assert.Equal(Visibility.Visible, Convert(box(false), "x"));

        static object box(bool value) => value;
    }

    // ------------------------------------------------------------------
    // One-way converter
    // ------------------------------------------------------------------

    [Fact]
    public void ConvertBack_IsNotSupported()
    {
        Assert.Throws<NotSupportedException>(() =>
            _converter.ConvertBack(Visibility.Collapsed, new[] { typeof(bool) }, null!, CultureInfo.InvariantCulture));
    }
}
