using System.Collections.Specialized;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace VyralBot.UI.Controls.Charts;

/// <summary>
/// One horizontal bar of a <see cref="SimpleBarChart"/>. Immutable by design:
/// the view model rebuilds the collection instead of mutating items, so the
/// ItemsControl re-renders naturally on every reload.
/// </summary>
public class BarChartItem
{
    public string Label { get; set; } = string.Empty;
    public string Platform { get; set; } = string.Empty;
    public int Value { get; set; }

    /// <summary>Bar width as a fraction of the largest item (0..1), pre-normalized by the view model.</summary>
    public double Fraction { get; set; }

    /// <summary>Exact value formatted for the value column / tooltip (grouped digits).</summary>
    public string ValueText => Value.ToString("N0", CultureInfo.CurrentCulture);

    public string ToolTipText
    {
        get
        {
            var platform = string.IsNullOrEmpty(Platform) ? string.Empty : $" ({Platform})";
            return $"{Label}: {ValueText}{platform}";
        }
    }
}

/// <summary>
/// Dependency-free, theme-aware horizontal bar chart: an <see cref="ItemsControl"/>
/// whose default style (<c>Themes/Generic.xaml</c>) renders each
/// <see cref="BarChartItem"/> as label + proportional bar + value. Bar colors
/// come from theme brushes via <c>DynamicResource</c> (TwitchBrush/KickBrush/
/// YouTubeBrush/DiscordBrush/TikTokBrush, PrimaryBrush fallback) so a theme
/// switch re-colors bars automatically. When <see cref="Items"/> is empty the
/// items host collapses and the <see cref="EmptyText"/> hint is shown instead.
/// </summary>
[TemplatePart(Name = PartEmptyText, Type = typeof(FrameworkElement))]
[TemplatePart(Name = PartItemsHost, Type = typeof(FrameworkElement))]
public class SimpleBarChart : ItemsControl
{
    private const string PartEmptyText = "PART_EmptyText";
    private const string PartItemsHost = "PART_ItemsHost";

    private FrameworkElement? _emptyTextPart;
    private FrameworkElement? _itemsHostPart;

    public static readonly DependencyProperty EmptyTextProperty = DependencyProperty.Register(
        nameof(EmptyText), typeof(string), typeof(SimpleBarChart), new PropertyMetadata("No data for the selected range"));

    public static readonly DependencyProperty LabelWidthProperty = DependencyProperty.Register(
        nameof(LabelWidth), typeof(double), typeof(SimpleBarChart), new PropertyMetadata(140.0));

    /// <summary>Hint shown instead of the bars when there are no items.</summary>
    public string EmptyText
    {
        get => (string)GetValue(EmptyTextProperty);
        set => SetValue(EmptyTextProperty, value);
    }

    /// <summary>Fixed width of the per-row label column so bars stay aligned across rows.</summary>
    public double LabelWidth
    {
        get => (double)GetValue(LabelWidthProperty);
        set => SetValue(LabelWidthProperty, value);
    }

    static SimpleBarChart()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(SimpleBarChart), new FrameworkPropertyMetadata(typeof(SimpleBarChart)));
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _emptyTextPart = GetTemplateChild(PartEmptyText) as FrameworkElement;
        _itemsHostPart = GetTemplateChild(PartItemsHost) as FrameworkElement;
        UpdateEmptyState();
    }

    protected override void OnItemsChanged(NotifyCollectionChangedEventArgs e)
    {
        base.OnItemsChanged(e);
        UpdateEmptyState();
    }

    /// <summary>Show the empty hint XOR the bars (template parts may be null before apply).</summary>
    private void UpdateEmptyState()
    {
        if (_emptyTextPart != null)
        {
            _emptyTextPart.Visibility = HasItems ? Visibility.Collapsed : Visibility.Visible;
        }

        if (_itemsHostPart != null)
        {
            _itemsHostPart.Visibility = HasItems ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}

/// <summary>
/// Bar width = fraction × track ActualWidth (MultiBinding). Returns 0 until the
/// track has been measured so no stale widths render during layout.
/// </summary>
public class BarWidthConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values is [double fraction, double trackWidth] && !double.IsNaN(trackWidth) && trackWidth > 0)
        {
            var width = fraction * trackWidth;
            // Non-zero fractions stay at least sliver-visible even at tiny proportions.
            return fraction > 0 ? Math.Max(width, 2d) : 0d;
        }

        return 0d;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException("One-way conversion only.");
}
