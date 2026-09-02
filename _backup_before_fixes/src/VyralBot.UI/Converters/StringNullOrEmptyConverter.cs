using System.Globalization;
using System.Windows.Data;

namespace VyralBot.UI.Converters;

/// <summary>
/// Converter to check if a string is null or empty.
/// </summary>
public class StringNullOrEmptyConverter : IValueConverter
{
    public static StringNullOrEmptyConverter Instance { get; } = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return string.IsNullOrEmpty(value as string);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
