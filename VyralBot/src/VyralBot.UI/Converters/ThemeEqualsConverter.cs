using System.Globalization;
using System.Windows.Data;

namespace VyralBot.UI.Converters;

/// <summary>
/// Multi-value converter: returns true when both string operands are equal
/// (case-insensitive). Used by theme cards to show the selected indicator
/// by comparing the item's Id with the VM's SelectedTheme.Id.
/// </summary>
public sealed class ThemeEqualsConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        => values.Length == 2
            && string.Equals(values[0]?.ToString(), values[1]?.ToString(), StringComparison.OrdinalIgnoreCase);

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
