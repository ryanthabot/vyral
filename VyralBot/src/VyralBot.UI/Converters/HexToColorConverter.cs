using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using VyralBot.UI.Models;

namespace VyralBot.UI.Converters;

/// <summary>Converts "#RRGGBB" strings to Color for theme swatch previews.</summary>
public sealed class HexToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => ColorShadeGenerator.TryParseHex(value as string, out var color) ? color : Colors.Transparent;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is Color c ? ColorShadeGenerator.ToHex(c) : "#000000";
}
