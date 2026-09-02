using System.Globalization;
using System.Windows.Data;

namespace VyralBot.UI.Converters;

#pragma warning disable CS8603

/// <summary>
/// Converter to display "(All)" for null values.
/// </summary>
public class NullToAllConverter : IValueConverter
{
    public static NullToAllConverter Instance { get; } = new();

#pragma warning disable CS8603
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value == null ? "(All)" : value.ToString() ?? "(All)";
    }
#pragma warning restore CS8603

#pragma warning disable CS8603
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var str = value?.ToString();
        return str == "(All)" ? Binding.DoNothing : (object?)str;
    }
#pragma warning restore CS8603
}

#pragma warning restore CS8603
